using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;

namespace FwLiteMaui.Tests;

#if WINDOWS
public class ProtocolLoginWebUiTests
{
    private static readonly Uri RedirectUri = new(ProtocolLoginRedirect.RedirectUri);

    private static Uri AuthUri(string state) =>
        new($"https://lexbox.example/api/oauth/open-id-auth?client_id=x&redirect_uri=x&state={state}");

    private static Uri Redirect(string state) => new($"{ProtocolLoginRedirect.RedirectUri}?code=abc&state={state}");

    private static string NewState() => Guid.NewGuid().ToString("N");

    [Fact]
    public async Task ActivationForThePendingLoginCompletesIt()
    {
        var state = NewState();
        Uri? openedUri = null;
        var webUi = new ProtocolLoginWebUi(NullLogger<ProtocolLoginWebUi>.Instance, uri =>
        {
            openedUri = uri;
            _ = Task.Run(() => ProtocolLoginRedirect.ForwardToWaitingLogin(Redirect(state)));
        });

        var result = await webUi.AcquireAuthorizationCodeAsync(AuthUri(state), RedirectUri, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        result.Should().Be(Redirect(state));
        openedUri.Should().Be(AuthUri(state));
    }

    [Fact]
    public async Task ActivationForAnotherLoginIsNotDelivered()
    {
        var state = NewState();
        bool? otherDelivered = null;
        var webUi = new ProtocolLoginWebUi(NullLogger<ProtocolLoginWebUi>.Instance,
            _ => otherDelivered = ProtocolLoginRedirect.ForwardToWaitingLogin(Redirect(NewState())),
            loginTimeout: TimeSpan.FromSeconds(4));

        var act = () => webUi.AcquireAuthorizationCodeAsync(AuthUri(state), RedirectUri, CancellationToken.None);

        await act.Should().ThrowAsync<MsalClientException>().Where(e => e.ErrorCode == MsalError.AuthenticationCanceledError);
        otherDelivered.Should().BeFalse();
    }

    [Fact]
    public void ActivationWithNoPendingLoginIsNotDelivered()
    {
        ProtocolLoginRedirect.ForwardToWaitingLogin(Redirect(NewState())).Should().BeFalse();
    }

    [Fact]
    public async Task CancellingThePendingLoginThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var webUi = new ProtocolLoginWebUi(NullLogger<ProtocolLoginWebUi>.Instance, _ => cts.CancelAfter(100));

        var act = () => webUi.AcquireAuthorizationCodeAsync(AuthUri(NewState()), RedirectUri, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("-ToastActivated")]
    [InlineData("https://lexbox.org/?state=x")]
    public void TryGetRedirectUriIgnoresOtherArgs(string arg)
    {
        ProtocolLoginRedirect.TryGetRedirectUri([arg], out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetRedirectUriFindsTheLoginRedirect()
    {
        var redirect = Redirect("s1").ToString();
        ProtocolLoginRedirect.TryGetRedirectUri(["--foo", redirect], out var uri).Should().BeTrue();
        uri!.ToString().Should().Be(redirect);
    }
}
#endif
