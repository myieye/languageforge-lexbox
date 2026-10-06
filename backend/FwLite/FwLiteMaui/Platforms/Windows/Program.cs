using Microsoft.UI.Dispatching;

namespace FwLiteMaui.WinUI;

// Replaces the XAML-generated Main (DISABLE_XAML_GENERATED_MAIN) so a login-redirect activation can be
// handed to the waiting process before this one starts any UI.
public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (ProtocolLoginRedirect.TryGetRedirectUri(args, out var redirectUri))
        {
            ProtocolLoginRedirect.HandleActivation(redirectUri);
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
