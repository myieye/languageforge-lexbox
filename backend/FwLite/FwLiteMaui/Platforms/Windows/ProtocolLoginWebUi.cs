using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;

namespace FwLiteMaui;

/// <summary>
/// Logs in through the system browser without MSAL's http://localhost listener, which VPNs, firewalls and proxies can block.
/// The OAuth redirect lands on Lexbox's FW Lite signed-in page, which hands its query back to us as a protocol activation
/// (see <see cref="ProtocolLoginRedirect"/>).
/// MSAL only asks for response_mode=form_post with its own listener, so the code comes back in the query string.
/// </summary>
public class ProtocolLoginWebUi(
    ILogger<ProtocolLoginWebUi> logger,
    Action<Uri>? openBrowser = null,
    TimeSpan? loginTimeout = null,
    bool registerProtocol = false,
    Action? onRedirectReceived = null) : ICustomWebUi
{
    // there's no cancel button while the browser is open, so this is how long an abandoned login blocks the button
    private static readonly TimeSpan DefaultLoginTimeout = TimeSpan.FromMinutes(10);

    public async Task<Uri> AcquireAuthorizationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
    {
        var state = ProtocolLoginRedirect.GetState(authorizationUri)
                    ?? throw new MsalClientException(MsalError.AuthenticationFailed, "Authorization request has no state");

        using var registration = registerProtocol ? ProtocolLoginRedirect.RegisterForUnpackagedApp() : null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(loginTimeout ?? DefaultLoginTimeout);
        try
        {
            var activationUri = await ProtocolLoginRedirect.WaitForRedirect(state,
                () =>
                {
                    logger.LogDebug("Waiting for login redirect with state {State}", state);
                    (openBrowser ?? OpenSystemBrowser)(authorizationUri);
                },
                timeout.Token);
            logger.LogInformation("Received login redirect");
            onRedirectReceived?.Invoke();
            // MSAL checks that it got back the redirect URI it asked for, which the page passed on as a silfwlite URI
            return new Uri(redirectUri.GetLeftPart(UriPartial.Path) + activationUri.Query);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MsalClientException(MsalError.AuthenticationCanceledError, "Login timed out waiting for the browser redirect");
        }
    }

    private static void OpenSystemBrowser(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
}
