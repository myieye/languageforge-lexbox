using System.Collections.Concurrent;
using LcmCrdt;
using LcmCrdt.RemoteSync;
using LexCore.Sync;

namespace FwHeadless.Services;

public record CrdtRebuildRequest(bool IgnoreSyncError = false, bool DiscardUnsyncedCommits = false, bool AllowDataDifference = false);

public enum StartCrdtRebuild
{
    Started,
    ProjectNotFound,
    // blocked from syncing, a merge or sync is under way, or a rebuild is already running
    ProjectBusy,
}

/// <summary>
/// Replaces a project's headless CRDT db with a fresh download from Lexbox (<see cref="CrdtProjectRebuilder"/>),
/// after a harmony sync so nothing local is lost. The project is blocked from syncing for the duration, which keeps
/// merges, scheduled harmony syncs and lexicon API writes off the db. A rebuild runs to the end whether or not the
/// request that started it is still there, and its report is kept until FwHeadless restarts.
/// </summary>
public class CrdtRebuildService(
    IServiceProvider services,
    HarmonySyncScheduler harmonySyncScheduler,
    SyncHostedService mergeService,
    ISyncJobStatusService syncJobStatusService,
    IProjectMetadataService metadataService,
    CrdtProjectRebuilder rebuilder,
    CrdtHttpSyncService httpSyncService,
    IHttpClientFactory httpClientFactory,
    IHostApplicationLifetime lifetime,
    ILogger<CrdtRebuildService> logger)
{
    public const string BlockReason = "CRDT rebuild in progress";

    private readonly ConcurrentDictionary<Guid, Task<CrdtRebuildReport>> _running = new();
    private readonly ConcurrentDictionary<Guid, CrdtRebuildReport> _reports = new();

    public bool IsRunning(Guid projectId) => _running.ContainsKey(projectId);

    public CrdtRebuildReport? Report(Guid projectId) => _reports.GetValueOrDefault(projectId);

    public async Task<(StartCrdtRebuild Result, string? Problem, Task<CrdtRebuildReport>? Rebuild)> Start(Guid projectId, CrdtRebuildRequest request)
    {
        var finished = new TaskCompletionSource<CrdtRebuildReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_running.TryAdd(projectId, finished.Task))
            return (StartCrdtRebuild.ProjectBusy, "A rebuild of this project is already running", null);

        var blocked = false;
        try
        {
            CrdtProject? project;
            await using (var scope = services.CreateAsyncScope())
            {
                project = await scope.ServiceProvider.GetRequiredService<ProjectContextFromIdService>().FindReadyCrdtProject(projectId);
            }
            if (project is null)
                return NotStarted(StartCrdtRebuild.ProjectNotFound, "The project doesn't exist or its first merge hasn't finished");
            if (await metadataService.GetSyncBlockedInfoAsync(projectId) is { IsBlocked: true } block)
                return NotStarted(StartCrdtRebuild.ProjectBusy, $"The project is blocked from syncing. Reason: {block.Reason}");

            // Block before looking for a merge or sync: one that starts after the block sees it and stops,
            // one that started before it is found here.
            await metadataService.BlockFromSyncAsync(projectId, BlockReason);
            blocked = true;
            if (mergeService.IsJobQueuedOrRunning(projectId) || syncJobStatusService.SyncStatus(projectId) == SyncJobStatus.Running)
            {
                await metadataService.UnblockFromSyncAsync(projectId);
                return NotStarted(StartCrdtRebuild.ProjectBusy, "A sync of this project is queued or running, try again once it has finished");
            }

            var report = new CrdtRebuildReport(CrdtRebuildReportStatus.Running, null, DateTimeOffset.UtcNow);
            _reports[projectId] = report;
            _ = Task.Run(async () =>
            {
                report = await Run(projectId, project, request, report);
                _reports[projectId] = report;
                _running.TryRemove(projectId, out _);
                finished.SetResult(report);
            });
            return (StartCrdtRebuild.Started, null, finished.Task);
        }
        catch
        {
            if (blocked) await metadataService.UnblockFromSyncAsync(projectId);
            _running.TryRemove(projectId, out _);
            throw;
        }

        (StartCrdtRebuild, string?, Task<CrdtRebuildReport>?) NotStarted(StartCrdtRebuild result, string problem)
        {
            _running.TryRemove(projectId, out _);
            return (result, problem, null);
        }
    }

    private async Task<CrdtRebuildReport> Run(Guid projectId, CrdtProject project, CrdtRebuildRequest request, CrdtRebuildReport report)
    {
        using var activity = FwHeadlessActivitySource.Value.StartActivity();
        activity?.SetTag("app.project_id", projectId);
        try
        {
            // a scheduled harmony sync that got past the block check before the block was set finishes first
            using var pausedSyncs = await harmonySyncScheduler.PauseSyncs(projectId, lifetime.ApplicationStopping);
            report = await Rebuild(project, request, report) with { FinishedAt = DateTimeOffset.UtcNow };
        }
        catch (Exception e)
        {
            logger.LogError(e, "CRDT rebuild of project {ProjectId} failed", projectId);
            activity?.AddException(e);
            report = report with { Status = CrdtRebuildReportStatus.Failed, Message = e.ToString(), FinishedAt = DateTimeOffset.UtcNow };
        }

        try
        {
            // Safe after a failure too: the db is replaced in one SQLite transaction, so it is either the old or the new one.
            await metadataService.UnblockFromSyncAsync(projectId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to unblock project {ProjectId} after its CRDT rebuild", projectId);
            report = report with { Message = $"{report.Message} The project is still blocked from syncing: {e.Message}".TrimStart() };
        }
        logger.LogInformation("CRDT rebuild of project {ProjectId}: {Report}", projectId, report);
        activity?.SetTag("app.crdt_rebuild.status", report.Status.ToString());
        return report;
    }

    private async Task<CrdtRebuildReport> Rebuild(CrdtProject project, CrdtRebuildRequest request, CrdtRebuildReport report)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CurrentProjectService>().SetupProjectContext(project);
            await scope.ServiceProvider.GetRequiredService<CrdtSyncService>().SyncHarmonyProject();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "The harmony sync before the CRDT rebuild failed, ignored: {Ignored}", request.IgnoreSyncError);
            if (!request.IgnoreSyncError) return report with
            {
                Status = CrdtRebuildReportStatus.SyncFailed,
                Message = $"The harmony sync before the rebuild failed, so local commits may not be on the server. Pass ignoreSyncError=true to rebuild anyway. {e}"
            };
        }

        var result = await rebuilder.Rebuild(project,
            async data => await httpSyncService.CreateProjectSyncable(data, httpClientFactory.CreateClient(FwHeadlessKernel.LexboxDownloadHttpClientName)),
            new(request.DiscardUnsyncedCommits, request.AllowDataDifference));
        return report with
        {
            Status = result.Status switch
            {
                LcmCrdt.CrdtRebuildStatus.Rebuilt => CrdtRebuildReportStatus.Rebuilt,
                LcmCrdt.CrdtRebuildStatus.UnsyncedCommits => CrdtRebuildReportStatus.UnsyncedCommits,
                LcmCrdt.CrdtRebuildStatus.DataDifference => CrdtRebuildReportStatus.DataDifference,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result.Status, null)
            },
            Message = result.Status switch
            {
                LcmCrdt.CrdtRebuildStatus.UnsyncedCommits =>
                    "FwHeadless has commits the server doesn't, a rebuild would lose them. Pass discardUnsyncedCommits=true to rebuild anyway.",
                LcmCrdt.CrdtRebuildStatus.DataDifference =>
                    "The data on the server is not the data FwHeadless has. Pass allowDataDifference=true to take the server's.",
                _ => null
            },
            MatchedServerBefore = result.MatchedServerBefore,
            MatchedServerAfter = result.MatchedServerAfter,
            UnsyncedCommitIds = result.UnsyncedCommitIds,
            DataDifferences = result.DataDifferences,
            BackupPath = result.BackupPath
        };
    }
}
