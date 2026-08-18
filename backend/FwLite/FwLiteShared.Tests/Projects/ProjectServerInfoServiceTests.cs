using FwLiteShared.Auth;
using FwLiteShared.Projects;
using LcmCrdt;
using LexCore.Entities;

namespace FwLiteShared.Tests.Projects;

public class ProjectServerInfoServiceTests
{
    private static readonly LexboxServer Staging = new(new Uri("https://staging.languagedepot.org"), "Staging");
    private static readonly LexboxServer Dev = new(new Uri("https://lexbox.dev.languagetechnology.org"), "Dev");
    private static readonly LexboxUser User = new("Test User", "user-id");

    private static ProjectData ProjectFrom(LexboxServer origin, string? lastUserId = null) =>
        new("Sena 3", "sena-3", Guid.NewGuid(), ProjectData.GetOriginDomain(origin.Authority), Guid.NewGuid(),
            LastUserId: lastUserId);

    [Fact]
    public void ServerOwnsProject_TrueForOriginServer()
    {
        ProjectServerInfoService.ServerOwnsProject(ProjectFrom(Staging), Staging).Should().BeTrue();
    }

    [Fact]
    public void ServerOwnsProject_FalseForOtherServerSharingTheGuid()
    {
        // A project downloaded from staging must not be claimed by dev when both list the same GUID.
        ProjectServerInfoService.ServerOwnsProject(ProjectFrom(Staging), Dev).Should().BeFalse();
    }

    [Fact]
    public void ServerOwnsProject_FalseForLocalOnlyProject()
    {
        var localOnly = new ProjectData("Local", "local", Guid.NewGuid(), OriginDomain: null, Guid.NewGuid());
        ProjectServerInfoService.ServerOwnsProject(localOnly, Staging).Should().BeFalse();
    }

    [Fact]
    public void ResolveRole_ProjectInList_MapsTheRole()
    {
        var project = ProjectFrom(Staging);
        var list = new ListProjectsResult([new(project.Id, "sena-3", "Sena 3", false, true, ProjectRole.Observer)], false);

        ProjectServerInfoService.ResolveRole(list, project, User).Should().Be(UserProjectRole.Observer);
    }

    [Fact]
    public void ResolveRole_FailedListFetch_KeepsStoredRoleOfSameUser()
    {
        var project = ProjectFrom(Staging, lastUserId: User.Id);

        ProjectServerInfoService.ResolveRole(null, project, User).Should().BeNull();
    }

    [Fact]
    public void ResolveRole_ProjectMissingFromList_KeepsStoredRoleOfSameUser()
    {
        // Missing from the list isn't "no access": admins can download projects they aren't members of,
        // and a project deleted on the server shouldn't strand unpushed local edits as readonly.
        var project = ProjectFrom(Staging, lastUserId: User.Id);
        var list = new ListProjectsResult([], true);

        ProjectServerInfoService.ResolveRole(list, project, User).Should().BeNull();
    }

    [Fact]
    public void ResolveRole_NoFreshRoleForADifferentUser_TrustsNeitherRole()
    {
        // The stored role belongs to the previous user; carrying it onto the new user could grant or
        // deny write access they don't actually have.
        var project = ProjectFrom(Staging, lastUserId: "someone-else");

        ProjectServerInfoService.ResolveRole(null, project, User).Should().Be(UserProjectRole.Unknown);
    }

    [Fact]
    public void ResolveRole_NoStoredUser_KeepsStoredRole()
    {
        var project = ProjectFrom(Staging, lastUserId: null);

        ProjectServerInfoService.ResolveRole(null, project, User).Should().BeNull();
    }
}
