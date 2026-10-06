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

        services.Configure<AuthConfig>(config =>
        {
            config.AfterLoginWebView = () =>
            {
                var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                if (window is null) throw new InvalidOperationException("Could not find window");
                //note, window.Activate() does not work per https://github.com/microsoft/microsoft-ui-xaml/issues/7595
                var hwnd = window.GetWindowHandle();
                WindowHelper.SetForegroundWindow(hwnd);
            };
        });

        services.AddOptions<AuthConfig>().Configure<IOptions<FwLiteMauiConfig>, ILoggerFactory>((config, mauiConfig, loggerFactory) =>
        {
            if (mauiConfig.Value.UseLoopbackLogin) return;
            config.CustomWebUiFactory = () =>
            {
                // per login, so a portable exe that was since moved or deleted gets replaced
                if (FwLiteMauiKernel.IsPortableApp) ProtocolLoginRedirect.RegisterForUnpackagedApp();
                return new ProtocolLoginWebUi(loggerFactory.CreateLogger<ProtocolLoginWebUi>());
            };
            config.CustomWebUiRedirectUri = ProtocolLoginRedirect.RedirectUri;
        });

        services.Configure<FwLiteConfig>(config =>
        {
            config.UseDevAssets = environment.IsDevelopment();
        });
    }
}

public class WindowHelper
{
    [DllImport("user32.dll")]
    public static extern void SetForegroundWindow(IntPtr hWnd);
}
