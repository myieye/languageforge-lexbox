using System.Text.Json;
using Windows.Foundation;
using Windows.Management.Deployment;
using Windows.Networking.Connectivity;
using LexCore.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.Uwp.Notifications;
using FwLiteShared.AppUpdate;
using FwLiteShared.Events;

namespace FwLiteMaui;

public class AppUpdateService(ILogger<AppUpdateService> logger, IPreferences preferences, GlobalEventBus eventBus)
    : IMauiInitializeService, IPlatformUpdateService
{
    //Must keep the .appinstaller file name: AddPackageByAppInstallerFileAsync rejects any other URI.
    //Kept in sync with FwLiteReleaseService.AppInstallerUrl, which the server bakes into the file itself.
    private const string AppInstallerUrl = "https://lexbox.org/api/fwlite-release/FieldWorksLite.appinstaller";
    //Staged but not registered because this app is still running. Windows registers it at the next
    //activation, so it's a pending update rather than a failure.
    private const int ErrorPackagesInUse = unchecked((int)0x80073D02);
    private const string LastUpdateCheckKey = "lastUpdateChecked";
    private  const string NotificationIdKey = "notificationId";
    private const string ActionKey = "action";
    private const string ResultRefKey = "resultRef";
    private static readonly Dictionary<string, TaskCompletionSource<string?>> NotificationCompletionSources = new();

    public void Initialize(IServiceProvider services)
    {
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
            ToastArguments args = ToastArguments.Parse(toastArgs.Argument);
            HandleNotificationAction(args.Get(ActionKey), args.Get(NotificationIdKey), args);
        };
        if (ToastNotificationManagerCompat.WasCurrentProcessToastActivated())
        {
            //don't check for updates if the user already clicked on a notification
            return;
        }
    }

    private async Task Test()
    {
        logger.LogInformation("Testing update notifications");
        var fwLiteRelease = new FwLiteRelease("1.0.0.0", "https://test.com");
        if (!await RequestPermissionToUpdate(fwLiteRelease))
        {
            logger.LogInformation("User declined update");
            return;
        }

        await ApplyUpdate(fwLiteRelease);
    }

    private void ShowUpdateInstallingNotification(FwLiteRelease latestRelease)
    {
        new ToastContentBuilder().AddText("FieldWorks Lite Installing update").AddText($"Version {latestRelease.Version} will be installed after FieldWorks Lite is closed").Show();
    }

    public async Task<bool> RequestPermissionToUpdate(FwLiteRelease latestRelease)
    {
        var notificationId = $"update-{Guid.NewGuid()}";
        var tcs = new TaskCompletionSource<string?>();
        NotificationCompletionSources.Add(notificationId, tcs);
        new ToastContentBuilder()
            .AddText("FieldWorks Lite Update")
            .AddText("A new version of FieldWorks Lite is available")
            .AddText($"Version {latestRelease.Version} would you like to download and install this update?")
            .AddArgument(NotificationIdKey, notificationId)
            .AddButton(new ToastButton()
                .SetContent("Download & Install")
                .AddArgument(ActionKey, "download")
                .AddArgument("release", JsonSerializer.Serialize(latestRelease)))
                .AddArgument(ResultRefKey, "release")
            .Show(toast =>
            {
                toast.Tag = "update";
            });
        var taskResult = await tcs.Task;
        return taskResult != null;
    }

    private void HandleNotificationAction(string action, string notificationId, ToastArguments args)
    {
        var result = args.Get(args.Get(ResultRefKey));
        if (!NotificationCompletionSources.TryGetValue(notificationId, out var tcs))
        {
            if (action == "download")
            {
                var release = JsonSerializer.Deserialize<FwLiteRelease>(result);
                if (release == null)
                {
                    logger.LogError("Invalid release {Release} for notification {NotificationId}", result, notificationId);
                    return;
                }
                _ = Task.Run(() => ApplyUpdate(release, true));
            }
            else
            {
                logger.LogError("Unknown action {Action} for notification {NotificationId}", action, notificationId);
            }
            return;
        }

        tcs.SetResult(result);
        NotificationCompletionSources.Remove(notificationId);
    }

    public async Task<UpdateResult> ApplyUpdate(FwLiteRelease latestRelease)
    {
        return await ApplyUpdate(latestRelease, false);
    }
    private async Task<UpdateResult> ApplyUpdate(FwLiteRelease latestRelease, bool quitOnUpdate)
    {
        logger.LogInformation("Installing new version: {Version}, Current version: {CurrentVersion}", latestRelease.Version, AppVersion.Version);
        var packageManager = new PackageManager();

        //If this install is associated with an .appinstaller file, it's on the OS update track and we
        //must update through the App Installer API. Updating the raw bundle via AddPackageByUriAsync
        //would detach it from that track (see App Installer non-store update docs). Installs from a plain
        //.msixbundle - how most users are installed today - have no association and take the fallback path.
        IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress> asyncOperation;
        if (IsOnAppInstallerTrack())
        {
            //Not the URI the package recorded: the API rejects any URI whose file name isn't *.appinstaller,
            //and installs attached before the FieldWorksLite.appinstaller route existed recorded a query-string URL.
            var appInstallerUri = new Uri(AppInstallerUrl);
            logger.LogInformation("Updating via App Installer file {AppInstallerUri}", appInstallerUri);
            //Never ForceTargetAppShutdown: that terminates this process without letting us close FwData
            //projects. None stages the update instead and Windows registers it at the next activation, which
            //is the same outcome the OS background updater produces. ForceUpdateFromAnyVersion is controlled
            //by the .appinstaller XML, not these options.
            asyncOperation = packageManager.AddPackageByAppInstallerFileAsync(appInstallerUri,
                AddPackageByAppInstallerOptions.None,
                packageManager.GetDefaultPackageVolume());
        }
        else
        {
            asyncOperation = packageManager.AddPackageByUriAsync(new Uri(latestRelease.Url),
                new AddPackageOptions()
                {
                    DeferRegistrationWhenPackagesAreInUse = true,
                    ForceUpdateFromAnyVersion = true,
                    ForceAppShutdown = quitOnUpdate
                });
        }
        asyncOperation.Progress = (info, progressInfo) =>
        {
            NotifyInstallProgress(progressInfo.percentage, latestRelease);
            if (progressInfo.state == DeploymentProgressState.Queued)
            {
                logger.LogInformation("Queued update");
                return;
            }
            logger.LogInformation("Downloading update: {ProgressPercentage}%", progressInfo.percentage);
        };
        ShowUpdateInstallingNotification(latestRelease);

        //note this asyncOperation is not reliable, it's possible the update will install and this will never resolve
        var updateTask = asyncOperation.AsTask();
        var completedTask = await Task.WhenAny(updateTask, Task.Delay(TimeSpan.FromMinutes(2)));
        if (completedTask == updateTask) return InterpretUpdateResult(await updateTask, latestRelease);

        //deployment carries on in AppXSvc after we stop waiting, so record how it ends instead of dropping the
        //result: an update that fails here is otherwise indistinguishable from one that worked
        _ = LogOutcomeWhenDone();
        return UpdateResult.Started;

        async Task LogOutcomeWhenDone()
        {
            try
            {
                //VSTHRD003: nothing awaits this local function, so awaiting a task from the enclosing scope
                //can't deadlock a caller
#pragma warning disable VSTHRD003
                InterpretUpdateResult(await updateTask, latestRelease);
#pragma warning restore VSTHRD003
            }
            catch (Exception e)
            {
                //nobody awaits this, so an exception would otherwise surface as an unobserved task exception
                logger.LogError(e, "Update to {Version} failed after we stopped waiting on it", latestRelease.Version);
            }
        }
    }

    private UpdateResult InterpretUpdateResult(DeploymentResult result, FwLiteRelease latestRelease)
    {
        if (result.ExtendedErrorCode?.HResult == ErrorPackagesInUse)
        {
            logger.LogInformation("Update to {Version} is staged, Windows will register it once this app closes",
                latestRelease.Version);
            return UpdateResult.Success;
        }

        if (!string.IsNullOrEmpty(result.ErrorText))
        {
            logger.LogError(result.ExtendedErrorCode, "Failed to download update: {ErrorText}", result.ErrorText);
            return UpdateResult.Failed;
        }

        logger.LogInformation("Update downloaded, will install on next restart");
        return UpdateResult.Success;
    }

    private void NotifyInstallProgress(uint percentage, FwLiteRelease release)
    {
        eventBus.PublishEvent(new AppUpdateProgressEvent(percentage, release));
    }

    /// <summary>
    /// Whether this install was deployed from an .appinstaller file, i.e. it's on the OS update track and
    /// must be updated through the App Installer API rather than the direct-bundle path.
    /// </summary>
    private bool IsOnAppInstallerTrack()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.GetAppInstallerInfo() is not null;
        }
        catch (Exception e)
        {
            //Package.Current throws for unpackaged/portable apps; treat as "not on the track".
            logger.LogWarning(e, "Unable to read App Installer info; falling back to direct bundle update");
            return false;
        }
    }

    public DateTime LastUpdateCheck
    {
        get => preferences.Get(LastUpdateCheckKey, DateTime.MinValue);
        set => preferences.Set(LastUpdateCheckKey, value);
    }

    public bool SupportsAutoUpdate => !FwLiteMauiKernel.IsPortableApp;

    public bool IsOnMeteredConnection()
    {
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile == null) return false;
        var cost = profile.GetConnectionCost();
        return cost.NetworkCostType != NetworkCostType.Unrestricted;
    }
}
