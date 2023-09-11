using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Chorus.VcsDrivers.Mercurial;
using Shouldly;
using Testing.ApiTests;
using Testing.Logging;
using Testing.Services;
using Xunit.Abstractions;

namespace Testing.SyncReverseProxy;

[Trait("Category", "Integration")]
public class SendReceiveServiceTests
{
    public SendReceiveAuth ManagerAuth = new("manager", TestingEnvironmentVariables.DefaultPassword);
    public SendReceiveAuth AdminAuth = new("admin", TestingEnvironmentVariables.DefaultPassword);
    public SendReceiveAuth InvalidPass = new("manager", "incorrect_pass");
    public SendReceiveAuth InvalidUser = new("invalid_user", TestingEnvironmentVariables.DefaultPassword);
    public SendReceiveAuth UnauthorizedUser = new("user", TestingEnvironmentVariables.DefaultPassword);

    private readonly ITestOutputHelper _output;
    private string _basePath = Path.Join(Path.GetTempPath(), "SendReceiveTests");
    private SendReceiveService _sendReceiveService;

    public SendReceiveServiceTests(ITestOutputHelper output)
    {
        _output = output;
        _sendReceiveService = new SendReceiveService(_output);
        CleanUpTempDir();
    }

    private void CleanUpTempDir()
    {
        var dirInfo = new DirectoryInfo(_basePath);
        try
        {
            dirInfo.Delete(true);
        }
        catch (DirectoryNotFoundException)
        {
            // It's fine if it didn't exist beforehand
        }
    }

    private static int _folderIndex = 1;

    private string GetProjectDir(string projectCode,
        string? identifier = null,
        [CallerMemberName] string testName = "")
    {
        var projectDir = Path.Join(_basePath, testName);
        if (identifier is not null) projectDir = Path.Join(projectDir, identifier);
        //fwdata file containing folder name will be the same as the file name
        projectDir = Path.Join(projectDir, _folderIndex++.ToString(), projectCode);
        return projectDir;
    }

    private SendReceiveParams GetParams(HgProtocol protocol,
        string? projectCode = null,
        [CallerMemberName] string testName = "")
    {
        projectCode ??= TestingEnvironmentVariables.ProjectCode;
        var sendReceiveParams = new SendReceiveParams(projectCode, protocol.GetTestHostName(), GetProjectDir(projectCode, testName: testName));
        return sendReceiveParams;
    }

    [Fact]
    public async Task VerifyHgWorking()
    {
        string version = await _sendReceiveService.GetHgVersion();
        version.ShouldStartWith("Mercurial Distributed SCM");
        HgRunner.Run("hg version", Environment.CurrentDirectory, 5, new XunitStringBuilderProgress(_output) {ShowVerbose = true});
        HgRepository.GetEnvironmentReadinessMessage("en").ShouldBeNull();
    }

    [Fact]
    public void CloneBigProject()
    {
        RunCloneSendReceive(HgProtocol.Hgweb, "admin", "elawa-dev-flex");
    }

    [Theory]
    [InlineData(HgProtocol.Hgweb, "admin")]
    [InlineData(HgProtocol.Hgweb, "manager")]
    [InlineData(HgProtocol.Resumable, "admin")]
    [InlineData(HgProtocol.Resumable, "manager")]
    public void CanCloneSendReceive(HgProtocol hgProtocol, string user)
    {
        RunCloneSendReceive(hgProtocol, user, TestingEnvironmentVariables.ProjectCode);
    }
    private void RunCloneSendReceive(HgProtocol hgProtocol, string user, string projectCode)
    {
        var auth = new SendReceiveAuth(user, TestingEnvironmentVariables.DefaultPassword);
        var sendReceiveParams = new SendReceiveParams(projectCode, hgProtocol.GetTestHostName(),
            GetProjectDir(projectCode, Path.Join(hgProtocol.ToString(), user)));
        var projectDir = sendReceiveParams.DestDir;
        var fwDataFile = sendReceiveParams.FwDataFile;

        // Clone
        var cloneResult = _sendReceiveService.CloneProject(sendReceiveParams, auth);
        cloneResult.ShouldNotContain("abort");
        cloneResult.ShouldNotContain("error");
        Directory.Exists(projectDir).ShouldBeTrue($"Directory {projectDir} not found. Clone response: {cloneResult}");
        Directory.EnumerateFiles(projectDir).ShouldContain(fwDataFile);
        var fwDataFileInfo = new FileInfo(fwDataFile);
        fwDataFileInfo.Length.ShouldBeGreaterThan(0);
        var fwDataFileOriginalLength = fwDataFileInfo.Length;

        // SendReceive
        var srResult = _sendReceiveService.SendReceiveProject(sendReceiveParams, auth);
        srResult.ShouldNotContain("abort");
        srResult.ShouldNotContain("error");
        srResult.ShouldContain("no changes from others");
        fwDataFileInfo.Refresh();
        fwDataFileInfo.Exists.ShouldBeTrue();
        fwDataFileInfo.Length.ShouldBe(fwDataFileOriginalLength);
    }

    [Fact]
    public void ModifyProjectData()
    {
        var projectCode = TestingEnvironmentVariables.ProjectCode;

        // Clone
        var sendReceiveParams = GetParams(HgProtocol.Hgweb, projectCode);
        var cloneResult = _sendReceiveService.CloneProject(sendReceiveParams, AdminAuth);
        cloneResult.ShouldNotContain("abort");
        cloneResult.ShouldNotContain("error");
        var fwDataFileInfo = new FileInfo(sendReceiveParams.FwDataFile);
        fwDataFileInfo.Length.ShouldBeGreaterThan(0);
        ModifyProjectHelper.ModifyProject(sendReceiveParams.FwDataFile);

        // Send changes
        var srResult = _sendReceiveService.SendReceiveProject(sendReceiveParams, AdminAuth, "Modify project data automated test");
        srResult.ShouldNotContain("abort");
        srResult.ShouldNotContain("error");
    }

    [Fact(Skip = "unable to push a new project with the current setup")]
    public async Task SendNewProject()
    {
        var id = Guid.NewGuid();
        var apiTester = new ApiTestBase();
        var auth = AdminAuth;
        var projectCode = "kevin-test-01";
        await apiTester.LoginAs(auth.Username, auth.Password);
        await apiTester
            .ExecuteGql($$"""
                          mutation {
                              createProject(input: {
                                  name: "Kevin test 01",
                                  type: FL_EX,
                                  id: "{{id}}",
                                  code: "{{projectCode}}",
                                  description: "this is just a testing project for testing a race condition",
                                  retentionPolicy: DEV
                              }) {
                                  createProjectResponse {
                                      id
                                  }
                              }
                          }
                          """);

        var sendReceiveParams = GetParams(HgProtocol.Hgweb, projectCode);
        ZipFile.ExtractToDirectory(@"C:\clipboard\LexBox\kevin-test-01.zip", sendReceiveParams.DestDir);
        File.Exists(sendReceiveParams.FwDataFile).ShouldBeTrue();

        await Task.Delay(TimeSpan.FromSeconds(5));
        var srResult = _sendReceiveService.SendReceiveProject(sendReceiveParams, auth);
        _output.WriteLine(srResult);
        srResult.ShouldNotContain("abort");
        srResult.ShouldNotContain("failure");
        srResult.ShouldNotContain("error");

        await apiTester.HttpClient.DeleteAsync($"{apiTester.BaseUrl}/api/project/project/{id}");
    }


    [Fact]
    public void InvalidPassOnCloneHgWeb()
    {
        var sendReceiveParams = GetParams(HgProtocol.Hgweb);
        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, InvalidPass);

        act.ShouldThrow<RepositoryAuthorizationException>();
    }

    [Fact]
    public void InvalidPassOnCloneHgResumable()
    {
        var sendReceiveParams = GetParams(HgProtocol.Resumable);
        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, InvalidPass);

        act.ShouldThrow<UnauthorizedAccessException>();
    }

    [Fact]
    public void InvalidPassOnSendReceiveHgWeb()
    {
        var sendReceiveParams = GetParams(HgProtocol.Hgweb);
        _sendReceiveService.CloneProject(sendReceiveParams, ManagerAuth);

        var act = () => _sendReceiveService.SendReceiveProject(sendReceiveParams, InvalidPass);
        act.ShouldThrow<RepositoryAuthorizationException>();
    }

    [Fact]
    public void InvalidPassOnSendReceiveHgResumable()
    {
        var sendReceiveParams = GetParams(HgProtocol.Resumable);
        _sendReceiveService.CloneProject(sendReceiveParams, ManagerAuth);

        var act = () => _sendReceiveService.SendReceiveProject(sendReceiveParams, InvalidPass);
        act.ShouldThrow<UnauthorizedAccessException>();
    }

    [Fact]
    public void InvalidUserCloneHgWeb()
    {
        var sendReceiveParams = GetParams(HgProtocol.Hgweb);
        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, InvalidUser);
        act.ShouldThrow<RepositoryAuthorizationException>();
    }

    [Fact]
    public void InvalidUserCloneHgResumable()
    {
        var sendReceiveParams = GetParams(HgProtocol.Resumable);
        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, InvalidUser);
        act.ShouldThrow<UnauthorizedAccessException>();
    }

    [Fact]
    public void InvalidProjectAdminLogin()
    {
        var sendReceiveParams = GetParams(HgProtocol.Hgweb, "non-existent-project");
        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, AdminAuth);

        act.ShouldThrow<ProjectLabelErrorException>();
        Directory.GetFiles(sendReceiveParams.DestDir).ShouldBeEmpty();
    }

    [Fact]
    public void InvalidProjectManagerLogin()
    {
        var sendReceiveParams = GetParams(HgProtocol.Hgweb, "non-existent-project");
        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, ManagerAuth);

        act.ShouldThrow<RepositoryAuthorizationException>();
        Directory.GetFiles(sendReceiveParams.DestDir).ShouldBeEmpty();
    }

    [Fact]
    public void UnauthorizedUserCloneHgWeb()
    {
        var sendReceiveParams = GetParams(HgProtocol.Hgweb);

        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, UnauthorizedUser);
        act.ShouldThrow<RepositoryAuthorizationException>();
    }

    [Fact]
    public void UnauthorizedUserCloneHgResumable()
    {
        var sendReceiveParams = GetParams(HgProtocol.Resumable);

        var act = () => _sendReceiveService.CloneProject(sendReceiveParams, UnauthorizedUser);
        act.ShouldThrow<UnauthorizedAccessException>();
    }
}
