using System.Diagnostics;
using System.Runtime.InteropServices;
using Nini.Ini;
using SIL.Progress;
using Testing.Logging;
using Xunit.Abstractions;

namespace Testing.Services;

public record SendReceiveAuth(string Username, string Password);

public record SendReceiveParams(string ProjectCode, string BaseUrl, string DestDir);

public class SendReceiveService
{
    private readonly ITestOutputHelper _output;
    private const string fdoDataModelVersion = "7000072";
    private const string Protocol = "http";

    public SendReceiveService(ITestOutputHelper output)
    {
        _output = output;
        FixupCaCerts();
    }

    private static void FixupCaCerts()
    {
        var caCertsPem = Path.GetFullPath(Path.Join("Mercurial", "cacert.pem"));
        //this cacerts.rc file is what is used when doing a clone, all future actions on a repo use the hgrc file defined in the .hg folder
        var caCertsRc = new IniDocument(Path.Join("Mercurial", "default.d", "cacerts.rc"), IniFileType.MercurialStyle);
        caCertsRc.Sections.GetOrCreate("web").Set("cacerts", caCertsPem);
        caCertsRc.Save();
    }

    private StringBuilderProgress NewProgress()
    {
        return new XunitStringBuilderProgress(_output)
        {
            ProgressIndicator = new NullProgressIndicator(), ShowVerbose = true
        };
    }

    public async Task<string> GetHgVersion()
    {
        using Process hg = new();
        string hgFilename = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "hg.exe" : "hg";
        hg.StartInfo.FileName = Path.Join("Mercurial", hgFilename);
        if (!File.Exists(hg.StartInfo.FileName))
        {
            throw new FileNotFoundException("unable to find HG executable", hg.StartInfo.FileName);
        }

        hg.StartInfo.Arguments = "version";
        hg.StartInfo.RedirectStandardOutput = true;

        hg.Start();
        var output = await hg.StandardOutput.ReadToEndAsync();
        await hg.WaitForExitAsync();

        return output;
    }

    public string CloneProject(SendReceiveParams sendReceiveParams, SendReceiveAuth auth)
    {
        var (projectCode, baseUrl, destDir) = sendReceiveParams;
        var (username, password) = auth;
        var progress = NewProgress();
        var repoUrl = new UriBuilder($"http://{baseUrl}/{projectCode}") { Scheme = Protocol };
        if (String.IsNullOrEmpty(username) && String.IsNullOrEmpty(password))
        {
            // No username or password supplied, so we explicitly do *not* save user settings
        }
        else
        {
            var chorusSettings = new Chorus.Model.ServerSettingsModel
            {
                Username = username,
                RememberPassword = false, // Necessary for tests to work on Linux
                Password = password
            };
            chorusSettings.SaveUserSettings();
            repoUrl.UserName = username;
            repoUrl.Password = password;
        }

        progress.WriteMessage($"Cloning {repoUrl} with user {username} and password \"{password}\" ...");
        var flexBridgeOptions = new Dictionary<string, string>
        {
            { "fullPathToProject", destDir },
            { "fdoDataModelVersion", fdoDataModelVersion },
            { "languageDepotRepoName", "LexBox" },
            { "languageDepotRepoUri", repoUrl.ToString() },
            { "deleteRepoIfNoSuchBranch", "false" },
        };
        string cloneResult;
        LfMergeBridge.LfMergeBridge.Execute("Language_Forge_Clone", progress, flexBridgeOptions, out cloneResult);
        cloneResult += "Progress out: " + progress.Text;
        return cloneResult;
    }

    public string SendReceiveProject(SendReceiveParams sendReceiveParams, SendReceiveAuth auth)
    {
        var (projectCode, baseUrl, destDir) = sendReceiveParams;
        var (username, password) = auth;
        var progress = NewProgress();
        var repoUrl = new UriBuilder($"http://{baseUrl}/{projectCode}") { Scheme = Protocol };
        if (String.IsNullOrEmpty(username) && String.IsNullOrEmpty(password))
        {
            // No username or password supplied, so we explicitly do *not* save user settings
        }
        else
        {
            var chorusSettings = new Chorus.Model.ServerSettingsModel
            {
                Username = username,
                RememberPassword = false, // Necessary for tests to work on Linux
                Password = password
            };
            chorusSettings.SaveUserSettings();
            repoUrl.UserName = username;
            repoUrl.Password = password;
        }

        string fwdataFilename = Path.Join(destDir, $"{projectCode}.fwdata");
        progress.WriteMessage($"S/R for {repoUrl} with user {username} and password \"{password}\" ...");
        var flexBridgeOptions = new Dictionary<string, string>
        {
            { "fullPathToProject", destDir },
            { "fwdataFilename", fwdataFilename },
            { "fdoDataModelVersion", fdoDataModelVersion },
            { "languageDepotRepoName", "LexBox" },
            { "languageDepotRepoUri", repoUrl.ToString() },
            { "user", "LexBox" },
            { "commitMessage", "Testing" }
        };

        string cloneResult;

        LfMergeBridge.LfMergeBridge.Execute("Language_Forge_Send_Receive",
            progress,
            flexBridgeOptions,
            out cloneResult);

        cloneResult += "Progress out: " + progress.Text;

        return cloneResult;
    }
}
