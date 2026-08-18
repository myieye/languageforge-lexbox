using FwLiteShared.Auth;
using FwLiteShared.Events;
using LcmCrdt;
using LexCore.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FwLiteShared.Projects;

/// <summary>
/// Central updater of per-project user state (<see cref="ProjectData.LastUserName"/>/<see cref="ProjectData.LastUserId"/>
/// and <see cref="ProjectData.Role"/>). Whenever fresh knowledge about a server arrives — a login, a remote
/// project-list fetch, an upload binding a project to a server — the signed-in user and their roles are stamped
/// onto the local projects that originate from that server. Login can happen anywhere (home page, sync dialog),
/// so the trigger is the auth-changed event, not any particular UI flow. The safety net for state this can't
/// reach in time is project-open self-healing: see ProjectServicesProvider.ResolveOriginUser.
/// </summary>
public class ProjectServerInfoService(
    OAuthClientFactory oAuthClientFactory,
    LexboxProjectService lexboxProjectService,
    CrdtProjectsService crdtProjectsService,
    ProjectEventBus projectEventBus,
    GlobalEventBus globalEventBus,
    ILogger<ProjectServerInfoService> logger) : IHostedService, IDisposable
{
    private IDisposable? _authSubscription;
    //serializes stamping so two quick auth changes can't interleave one server's user with another's role
    private readonly SemaphoreSlim _applyLock = new(1, 1);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _authSubscription = globalEventBus.OnAuthenticationChanged.Subscribe(e => _ = OnAuthenticationChanged(e.Server));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _authSubscription?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _authSubscription?.Dispose();
        _applyLock.Dispose();
    }

    private async Task OnAuthenticationChanged(LexboxServer server)
    {
        try
        {
            //idempotent with LexboxProjectService's own invalidation, done here too so subscription order
            //can never make us stamp from a pre-login project list
            lexboxProjectService.InvalidateProjectsCache(server);
            await RefreshProjectsServerInfo(server);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to update local project user state after an auth change for {Server}", server.DisplayName);
        }
    }

    public async Task RefreshProjectsServerInfo(LexboxServer server)
    {
        await ApplyServerInfo(server, await lexboxProjectService.GetLexboxProjects(server));
    }

    /// <summary>
    /// Stamps the server's signed-in user (and roles from <paramref name="userProjects"/>, when available)
    /// onto the local projects originating from <paramref name="server"/>. Signed out means no fresh knowledge:
    /// the stored user and role are kept as the last known state.
    /// </summary>
    public async Task ApplyServerInfo(LexboxServer server, UserProjectList? userProjects)
    {
        await _applyLock.WaitAsync();
        try
        {
            LexboxUser? user;
            try
            {
                user = await oAuthClientFactory.GetClient(server).GetCachedUser();
            }
            catch (Exception e)
            {
                //best-effort: stale user state is recoverable (next login/list refresh), so never fail the caller
                logger.LogError(e, "Failed to read the cached user for {Server}", server.DisplayName);
                return;
            }
            if (user is null) return;
            //a list fetched under a different account (user switched mid-fetch) is no fresh knowledge for this user
            var lexboxProjects = userProjects is not null && userProjects.UserId == user.Id ? userProjects.Result : null;
            //materialize: the lazy directory enumeration shouldn't span the awaits below
            foreach (var project in crdtProjectsService.ListProjects().ToArray())
            {
                if (project.Data is null || !ServerOwnsProject(project.Data, server)) continue;
                try
                {
                    var role = ResolveRole(lexboxProjects, project.Data, user);
                    var updated = await crdtProjectsService.UpdateProjectServerInfo(project, user.Name, user.Id, role);
                    if (updated is not null) projectEventBus.PublishEvent(project, new ProjectDataChangedEvent(updated));
                }
                catch (Exception e)
                {
                    //per-project so one locked/corrupt db doesn't leave the projects after it stale
                    logger.LogError(e, "Failed to stamp user state on project {Project}", project.Name);
                }
            }
        }
        finally
        {
            _applyLock.Release();
        }
    }

    /// <summary>
    /// A server may only stamp its user onto projects it is the origin of: the same project GUID can exist on
    /// several logged-in servers (e.g. staging and dev), and matching by GUID alone would clobber the origin
    /// server's user with another server's. Matches by authority, like every other server↔project lookup.
    /// </summary>
    internal static bool ServerOwnsProject(ProjectData projectData, LexboxServer server)
    {
        return projectData.ServerId is { } serverId && serverId == server.Id;
    }

    //Missing from the list is NOT "no access": admins download projects they aren't members of, and a
    //project deleted on the server shouldn't strand unpushed local edits as readonly. So without fresh
    //role knowledge keep the stored role (null) — unless it belongs to a different user, then trust neither.
    internal static UserProjectRole? ResolveRole(ListProjectsResult? lexboxProjects, ProjectData projectData, LexboxUser user)
    {
        var remote = lexboxProjects?.Projects.FirstOrDefault(p => p.Id == projectData.Id);
        if (remote is not null) return remote.Role.ToUserProjectRole();
        return projectData.LastUserId is null || projectData.LastUserId == user.Id ? null : UserProjectRole.Unknown;
    }
}
