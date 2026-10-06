using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Win32;

namespace FwLiteMaui.Tests;

#if WINDOWS
public class ProtocolLoginWebUiTests
{
    private static readonly Uri RedirectUri = new("https://lexbox.example/fw-lite/signed-in");

    private static Uri AuthUri(string state) =>
        new($"https://lexbox.example/api/oauth/open-id-auth?client_id=x&redirect_uri=x&state={state}");

    private static Uri Redirect(string state) => new($"{ProtocolLoginRedirect.ActivationUri}?code=abc&state={state}");

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

        result.Should().Be(new Uri($"{RedirectUri}?code=abc&state={state}"));
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
    public async Task ActivationIsNotDeliveredWhenTheLoginClosesWithoutAcceptingIt()
    {
        var state = NewState();
        var server = new NamedPipeServerStream(ProtocolLoginRedirect.PipeName(state), PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connected = server.WaitForConnectionAsync();
        var forward = Task.Run(() => ProtocolLoginRedirect.ForwardToWaitingLogin(Redirect(state)));
        await connected.WaitAsync(TimeSpan.FromSeconds(10));
        using (var reader = new StreamReader(server, leaveOpen: true))
            await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await server.DisposeAsync();

        (await forward.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeFalse();
    }

    [Fact]
    public async Task AnActivationThatNeverReadsTheAckDoesNotBlockTheLogin()
    {
        var state = NewState();
        using var client = new NamedPipeClientStream(".", ProtocolLoginRedirect.PipeName(state), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        var login = ProtocolLoginRedirect.WaitForRedirect(state, () => { }, CancellationToken.None);
        await client.ConnectAsync(10_000);
        await client.WriteAsync(Encoding.UTF8.GetBytes(Redirect(state) + "\n"));

        (await login.WaitAsync(TimeSpan.FromSeconds(10))).Should().Be(Redirect(state));
    }

    [Fact]
    public void UnpackagedRegistrationIsRemovedOnlyIfStillOurs()
    {
        const string commandKey = @"Software\Classes\silfwlite\shell\open\command";
        string? Command()
        {
            using var key = Registry.CurrentUser.OpenSubKey(commandKey);
            return key?.GetValue("") as string;
        }

        try
        {
            using (ProtocolLoginRedirect.RegisterForUnpackagedApp())
                Command().Should().Contain(Environment.ProcessPath);
            Command().Should().BeNull();

            var first = ProtocolLoginRedirect.RegisterForUnpackagedApp();
            using (ProtocolLoginRedirect.RegisterForUnpackagedApp())
            {
                first.Dispose();
                Command().Should().Contain(Environment.ProcessPath, "another login still waits");
            }
            Command().Should().BeNull();

            var registration = ProtocolLoginRedirect.RegisterForUnpackagedApp();
            using (var key = Registry.CurrentUser.CreateSubKey(commandKey)) key.SetValue("", "\"other.exe\" \"%1\"");
            registration.Dispose();
            Command().Should().Be("\"other.exe\" \"%1\"");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\silfwlite", throwOnMissingSubKey: false);
        }
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
    [InlineData("silfwlite://localhost/link?x=1")]
    [InlineData("silfwlite://lexbox.org/auth?state=x")]
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
