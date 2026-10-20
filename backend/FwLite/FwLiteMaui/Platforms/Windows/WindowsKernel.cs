using System.Runtime.InteropServices;
using FwLiteMaui.Services;
using FwLiteShared;
using FwLiteShared.AppUpdate;
using FwLiteShared.Auth;
using FwLiteShared.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Maui.Platform;

namespace FwLiteMaui;

public static class WindowsKernel
{

    public static void AddFwLiteWindows(this IServiceCollection services, IHostEnvironment environment)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        services.RemoveAll(typeof(IPlatformUpdateService));
        services.AddSingleton<AppUpdateService>();
        services.AddSingleton<IMauiInitializeService>(s => s.GetRequiredService<AppUpdateService>());
        services.AddSingleton<IPlatformUpdateService>(s => s.GetRequiredService<AppUpdateService>());
        services.AddSingleton<IMultiWindowService, WindowsMultiWindowService>();
        if (!FwLiteMauiKernel.IsPortableApp)
        {
            services.AddSingleton<IMauiInitializeService, WindowsShortcutService>();
            services.AddSingleton<IMauiInitializeService, PackageUpdateLogger>();
        }

        services.AddOptions<AuthConfig>().Configure<IOptions<FwLiteMauiConfig>, ILoggerFactory>((config, mauiConfig, loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger(typeof(WindowsKernel).FullName!);
            config.AfterLoginWebView = () => BringAppToFront(logger);
            if (mauiConfig.Value.UseLoopbackLogin) return;
            config.CustomWebUiFactory = () => new ProtocolLoginWebUi(loggerFactory.CreateLogger<ProtocolLoginWebUi>(),
                registerProtocol: FwLiteMauiKernel.IsPortableApp,
                // don't wait for the token request: user input in the meantime takes away the foreground rights we were given
                onRedirectReceived: () => MainThread.BeginInvokeOnMainThread(() => BringAppToFront(logger)));
            config.CustomWebUiRedirectUri = OAuthClient.SignedInPage;
        });

        services.Configure<FwLiteConfig>(config =>
        {
            config.UseDevAssets = environment.IsDevelopment();
        });
    }

    private static void BringAppToFront(ILogger logger)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
        {
            logger.LogWarning("Could not find the window to bring to the front after login");
            return;
        }
        //note, window.Activate() does not work per https://github.com/microsoft/microsoft-ui-xaml/issues/7595
        var hwnd = window.GetWindowHandle();
        if (WindowHelper.IsIconic(hwnd)) WindowHelper.ShowWindow(hwnd, WindowHelper.SwRestore);
        // Windows only allows this while we hold foreground rights, see ProtocolLoginRedirect.ForwardToWaitingLogin
        if (!WindowHelper.SetForegroundWindow(hwnd))
            logger.LogInformation("Windows did not let FieldWorks Lite come to the front after login");
    }
}

public class WindowHelper
{
    public const int SwRestore = 9;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
