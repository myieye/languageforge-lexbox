using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;

namespace FwLiteMaui;

/// <summary>
/// Logs in through the system browser and receives the redirect as a protocol activation
/// (see <see cref="ProtocolLoginRedirect"/>) instead of MSAL's http://localhost listener,
/// which VPNs, firewalls and proxies can block.
/// MSAL only asks for response_mode=form_post with its own listener, so the code comes back in the query string.
/// </summary>
public class ProtocolLoginWebUi(ILogger<ProtocolLoginWebUi> logger, Action<Uri>? openBrowser = null, TimeSpan? loginTimeout = null)
    : ICustomWebUi
{
    // there's no cancel button while the browser is open, so this is how long an abandoned login blocks the button
    private static readonly TimeSpan DefaultLoginTimeout = TimeSpan.FromMinutes(10);

    public async Task<Uri> AcquireAuthorizationCodeAsync(Uri authorizationUri, Uri redirectUri, CancellationToken cancellationToken)
    {
        var state = ProtocolLoginRedirect.GetState(authorizationUri)
                    ?? throw new MsalClientException(MsalError.AuthenticationFailed, "Authorization request has no state");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(loginTimeout ?? DefaultLoginTimeout);
        try
        {
            var result = await ProtocolLoginRedirect.WaitForRedirect(state,
                () =>
                {
                    logger.LogDebug("Waiting for login redirect with state {State}", state);
                    (openBrowser ?? OpenSystemBrowser)(authorizationUri);
                },
                timeout.Token);
            logger.LogInformation("Received login redirect");
            return result;
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
