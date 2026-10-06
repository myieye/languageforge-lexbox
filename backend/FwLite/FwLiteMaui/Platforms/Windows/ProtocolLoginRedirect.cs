using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using FwLiteShared.Auth;
using Microsoft.Win32;

namespace FwLiteMaui;

/// <summary>
/// Login redirects arrive as a protocol activation, which Windows delivers to a new process. That process
/// forwards the redirect URI over a named pipe named after the login's OAuth state, so it reaches exactly
/// the process (packaged, portable or dev build) that started that login, and nothing else.
/// </summary>
public static class ProtocolLoginRedirect
{
    public const string Scheme = "msal" + AuthConfig.DefaultClientId;
    public const string RedirectUri = Scheme + "://auth";

    public static bool TryGetRedirectUri(string[] args, [NotNullWhen(true)] out Uri? redirectUri)
    {
        foreach (var arg in args)
        {
            if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) &&
                uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
            {
                redirectUri = uri;
                return true;
            }
        }

        redirectUri = null;
        return false;
    }

    public static string? GetState(Uri uri) => HttpUtility.ParseQueryString(uri.Query).Get("state");

    // state comes from an external URI, so hash it rather than splice it into a pipe path
    public static string PipeName(string state) =>
        "FwLiteLogin-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)))[..32];

    private const byte Ack = 1;

    /// <returns>false unless the process waiting for a login with this redirect's state confirmed it got it</returns>
    public static bool ForwardToWaitingLogin(Uri redirectUri)
    {
        var state = GetState(redirectUri);
        if (string.IsNullOrEmpty(state)) return false;
        using var pipe = new NamedPipeClientStream(".", PipeName(state), PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            return false;
        }

        // we were launched by the user's click in the browser, so we may hand our foreground rights on;
        // without this the waiting app can't bring its window to the front after login
        if (GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out var serverPid))
            AllowSetForegroundWindow(serverPid);

        try
        {
            pipe.Write(Encoding.UTF8.GetBytes(redirectUri.OriginalString + "\n"));
            // the login can time out between our connect and its read
            return pipe.ReadByte() == Ack;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void HandleActivation(Uri redirectUri)
    {
        if (ForwardToWaitingLogin(redirectUri)) return;
        MessageBox(IntPtr.Zero,
            "FieldWorks Lite is not waiting for this login. It may have been closed or the login may have timed out.\n\nPlease log in again from FieldWorks Lite.",
            "FieldWorks Lite",
            MbIconWarning);
    }

    /// <summary>
    /// Points the login scheme at the current exe. Only for unpackaged (portable/dev) runs: a packaged
    /// install declares the scheme in its manifest instead, and its registry writes would be virtualized.
    /// </summary>
    public static void RegisterForUnpackagedApp()
    {
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path");
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
        key.SetValue("", "URL:FieldWorks Lite login");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exePath}\" \"%1\"");
    }

    public static async Task<Uri> WaitForRedirect(string state, Action onListening, CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeServerStream(PipeName(state),
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        onListening();
        while (true)
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            using (var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true))
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (Uri.TryCreate(line, UriKind.Absolute, out var uri) && GetState(uri) == state)
                {
                    try
                    {
                        pipe.WriteByte(Ack);
                        // closing the pipe before the client reads would discard the ack
                        pipe.WaitForPipeDrain();
                    }
                    catch (IOException)
                    {
                        // the activation process is gone, but we have the redirect
                    }
                    return uri;
                }
            }

            pipe.Disconnect();
        }
    }

    private const uint MbIconWarning = 0x30;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);
}
