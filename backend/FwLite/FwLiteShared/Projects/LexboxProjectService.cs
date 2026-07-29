using System.Net.Http.Json;
using FwLiteShared.Auth;
using FwLiteShared.Events;
using LcmCrdt;
using LexCore.Entities;
using LexCore.Sync;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SIL.Harmony.Core;

namespace FwLiteShared.Projects;

public class LexboxProjectService : IDisposable
{
    private readonly OAuthClientFactory clientFactory;
    private readonly ILogger<LexboxProjectService> logger;
    private readonly IOptions<AuthConfig> options;
    private readonly IMemoryCache cache;
    private readonly LexboxProjectChangeListener projectChangeListener;
    private readonly IDisposable authChangedSubscription;

    public LexboxProjectService(
        OAuthClientFactory clientFactory,
        ILogger<LexboxProjectService> logger,
        IOptions<AuthConfig> options,
        IMemoryCache cache,
        LexboxProjectChangeListener projectChangeListener,
        GlobalEventBus globalEventBus)
    {
        this.clientFactory = clientFactory;
        this.logger = logger;
        this.options = options;
        this.cache = cache;
        this.projectChangeListener = projectChangeListener;

        authChangedSubscription = globalEventBus.OnAuthenticationChanged
            .Subscribe(@event => InvalidateProjectsCache(@event.Server));
    }

    public void Dispose()
    {
        authChangedSubscription.Dispose();
    }

    public LexboxServer[] Servers()
    {
        return options.Value.LexboxServers;
    }

    public LexboxServer? GetServer(ProjectData? projectData)
    {
        if (projectData is null) return null;
        return Servers().FirstOrDefault(s => s.Id == projectData.ServerId);
    }

    //the list is per-user (roles, membership), so entries are stamped with the identity they were fetched
    //under: a fetch that raced a user switch can never be read back as the new user's list
    private record CachedProjectList(string UserId, ListProjectsResult Result);

    /// <summary>
    /// The server's project list for the signed-in user, cached for a few minutes. Null when the list can't
    /// be fetched (signed out, offline, server error) — callers must treat that as "no fresh knowledge", not
    /// as an empty list. Failures are never cached, so e.g. a fetch that raced a login isn't stuck looking
    /// signed out for the cache duration.
    /// </summary>
    public async Task<ListProjectsResult?> GetLexboxProjects(LexboxServer server)
    {
        var user = await clientFactory.GetClient(server).GetCachedUser();
        if (user is null) return null;
        if (cache.TryGetValue(ProjectListCacheKey(server), out CachedProjectList? cached) && cached?.UserId == user.Id)
            return cached.Result;
        var result = await FetchLexboxProjects(server);
        if (result is not null)
            cache.Set(ProjectListCacheKey(server), new CachedProjectList(user.Id, result), TimeSpan.FromMinutes(5));
        return result;
    }

    private async Task<ListProjectsResult?> FetchLexboxProjects(LexboxServer server)
    {
        var httpClient = await clientFactory.GetClient(server).CreateHttpClient();
        if (httpClient is null) return null;
        try
        {
            return await httpClient.GetFromJsonAsync<ListProjectsResult>("api/crdt/listProjectsV2");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error getting lexbox projects");
            return null;
        }
    }

    public async Task<(DownloadProjectByCodeResult, Guid?)> GetLexboxProjectId(LexboxServer server, string code)
    {
        var httpClient = await clientFactory.GetClient(server).CreateHttpClient();
        if (httpClient is null) return (DownloadProjectByCodeResult.Forbidden, null);
        try
        {
            var result = await httpClient.GetAsync($"api/crdt/lookupProjectId?code={code}");
            if (result.StatusCode == System.Net.HttpStatusCode.Forbidden) // 403
            {
                return (DownloadProjectByCodeResult.Forbidden, null);
            }
            if (result.StatusCode == System.Net.HttpStatusCode.NotFound) // 404
            {
                return (DownloadProjectByCodeResult.ProjectNotFound, null);
            }
            if (result.StatusCode == System.Net.HttpStatusCode.NotAcceptable) // 406
            {
                return (DownloadProjectByCodeResult.NotCrdtProject, null);
            }
            var guid = await result.Content.ReadFromJsonAsync<Guid?>();
            return (DownloadProjectByCodeResult.Success, guid);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error getting lexbox project id");
            return (DownloadProjectByCodeResult.Forbidden, null);
        }
    }

    public async Task<ProjectSyncStatus> GetLexboxSyncStatus(LexboxServer server, Guid projectId)
    {
        var client = clientFactory.GetClient(server);
        var httpClient = await client.CreateHttpClient();
        if (httpClient is null)
            return ProjectSyncStatus.Unknown(await client.IsSignedIn()
                ? ProjectSyncStatusErrorCode.Offline
                : ProjectSyncStatusErrorCode.NotLoggedIn);
        try
        {
            var status = await httpClient.GetFromJsonAsync<ProjectSyncStatus>($"api/fw-lite/sync/status/{projectId}");
            return status ?? ProjectSyncStatus.Unknown(ProjectSyncStatusErrorCode.Unknown, "No status returned from server");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error getting lexbox sync status");
            return ProjectSyncStatus.Unknown(ProjectSyncStatusErrorCode.Unknown, e.Message);
        }
    }

    public async Task<HttpResponseMessage?> TriggerLexboxSync(LexboxServer server, Guid projectId)
    {
        var httpClient = await clientFactory.GetClient(server).CreateHttpClient();
        if (httpClient is null) return null;
        try
        {
            return await httpClient.PostAsync($"api/fw-lite/sync/trigger/{projectId}", null);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error triggering lexbox sync");
            return null;
        }
    }

    public async Task<SyncJobResult> AwaitLexboxSyncFinished(LexboxServer server, Guid projectId, int timeoutSeconds = 15 * 60)
    {
        var client = clientFactory.GetClient(server);
        var httpClient = await client.CreateHttpClient();
        if (httpClient is null)
        {
            return await client.IsSignedIn()
                ? new SyncJobResult(SyncJobStatusEnum.UnableToSync, "Unable to reach the lexbox server, check your internet connection and try again")
                : new SyncJobResult(SyncJobStatusEnum.UnableToAuthenticate, "Unable to retrieve sync status when logged out, try again after logging in to lexbox server");
        }
        var giveUpAt = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        while (giveUpAt > DateTime.UtcNow)
        {
            try
            {
                // Avoid 30-second timeout by retrying every 25 seconds until max time reached
                var result = await httpClient.GetAsync(
                        $"api/fw-lite/sync/await-sync-finished/{projectId}",
                        new CancellationTokenSource(TimeSpan.FromSeconds(25)).Token);
                if (result.IsSuccessStatusCode)
                {
                    var content = await result.Content.ReadFromJsonAsync<SyncJobResult?>();
                    if (content is { Status: SyncJobStatusEnum.TimedOutAwaitingSyncStatus }) continue;
                    return content ?? new SyncJobResult(SyncJobStatusEnum.UnknownError, "Unknown error retrieving sync status");
                }
                else
                {
                    var errorMessage = await result.Content.ReadAsStringAsync();
                    return new SyncJobResult(SyncJobStatusEnum.UnknownError, errorMessage);
                }
            }
            catch (OperationCanceledException) { continue; }
            catch (Exception e)
            {
                logger.LogError(e, "Error waiting for lexbox sync to finish");
                return new SyncJobResult(SyncJobStatusEnum.UnknownError, e.ToString());
            }
        }
        logger.LogError("Timed out waiting for lexbox sync to finish");
        return new SyncJobResult(SyncJobStatusEnum.SyncJobTimedOut, "Timed out waiting for lexbox sync to finish");
    }

    public async Task<int?> CountPendingCrdtCommits(LexboxServer server, Guid projectId, SyncState localSyncState)
    {
        var httpClient = await clientFactory.GetClient(server).CreateHttpClient();
        if (httpClient is null) return null;
        try
        {
            var result = await httpClient.PostAsJsonAsync<SyncState>($"/api/crdt/{projectId}/countChanges", localSyncState);
            var text = await result.Content.ReadAsStringAsync();
            return int.Parse(text);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error counting pending changes in lexbox");
            return null;
        }
    }

    public void InvalidateProjectsCache(LexboxServer server)
    {
        cache.Remove(ProjectListCacheKey(server));
    }

    public Task ListenForProjectChanges(ProjectData projectData, CancellationToken stoppingToken, bool kickReconnecting = false) =>
        projectChangeListener.ListenForProjectChanges(projectData, stoppingToken, kickReconnecting);

    internal static string ProjectListCacheKey(LexboxServer server) => $"Projects|{server.Authority.Authority}";
}
