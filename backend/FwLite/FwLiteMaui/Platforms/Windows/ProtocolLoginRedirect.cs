using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Security;
using System.Web;
using LexCore.Utils;
using Microsoft.Win32;

namespace FwLiteMaui;

/// <summary>
/// Login redirects arrive as a protocol activation (launched by Lexbox's FW Lite signed-in page), which Windows
/// delivers to a new process. That process forwards the URI over a named pipe named after the login's OAuth state,
/// so it reaches exactly the process (packaged, portable or dev build) that started that login, and nothing else.
/// </summary>
public static class ProtocolLoginRedirect
{
    // mirrors FieldWorks' silfw scheme (silfw://localhost/link?...); other silfwlite URIs are not login redirects
    public const string Scheme = "silfwlite";
    public const string ActivationUri = Scheme + "://localhost/auth";
    private static readonly Uri Activation = new(ActivationUri);

    public static bool TryGetRedirectUri(string[] args, [NotNullWhen(true)] out Uri? redirectUri)
    {
        foreach (var arg in args)
        {
            if (Uri.TryCreate(arg, UriKind.Absolute, out var uri) &&
                uri.Scheme.Equals(Activation.Scheme, StringComparison.OrdinalIgnoreCase) &&
                uri.Host.Equals(Activation.Host, StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath == Activation.AbsolutePath)
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
        // e.g. an elevated FW Lite owns the pipe, which CurrentUserOnly rejects
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
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
            "FieldWorks Lite is not waiting for this login. The login may have already finished or timed out, or FieldWorks Lite was closed.\n\nIf you are not logged in, please log in again from FieldWorks Lite.",
            "FieldWorks Lite",
            MbIconWarning);
    }

    private const string ClassesKey = $@"Software\Classes\{Scheme}";
    private static readonly Lock RegistrationLock = new();
    private static int _registrations;

    /// <summary>
    /// Points the scheme at the current exe until disposed. Only for unpackaged (portable/dev) runs: a packaged
    /// install declares the scheme in its manifest instead, and its registry writes would be virtualized.
    /// Registering only while a login waits means a portable exe that gets deleted or moved leaves nothing behind
    /// (it has no uninstaller), unless the app exits mid-login.
    /// </summary>
    public static IDisposable RegisterForUnpackagedApp()
    {
        var exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown process path");
        var commandLine = $"\"{exePath}\" \"%1\"";
        lock (RegistrationLock)
        {
            using var key = Registry.CurrentUser.CreateSubKey(ClassesKey);
            key.SetValue("", "URL:FieldWorks Lite");
            key.SetValue("URL Protocol", "");
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", commandLine);
            _registrations++;
        }
        return Defer.Action(() => Unregister(commandLine));
    }

    private static void Unregister(string commandLine)
    {
        lock (RegistrationLock)
        {
            // logins can overlap, e.g. a retry while an abandoned one waits out its timeout
            if (--_registrations > 0) return;
            try
            {
                // another copy of the app may have registered itself since
                using (var command = Registry.CurrentUser.OpenSubKey(ClassesKey + @"\shell\open\command"))
                {
                    if (command?.GetValue("") as string != commandLine) return;
                }
                Registry.CurrentUser.DeleteSubKeyTree(ClassesKey, throwOnMissingSubKey: false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException)
            {
                // must not fail a login that already got its redirect; the next login rewrites the key anyway
            }
        }
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
                    // closing our end before the client reads the ack could discard it, so wait for the client to
                    // hang up, but not for long: a stalled client must not hold up the login
                    using var ackTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    try
                    {
                        await pipe.WriteAsync(new[] { Ack }, ackTimeout.Token);
                        await pipe.ReadAsync(new byte[1], ackTimeout.Token);
                    }
                    catch (Exception e) when (e is IOException or OperationCanceledException)
                    {
                        // we have the redirect either way
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
