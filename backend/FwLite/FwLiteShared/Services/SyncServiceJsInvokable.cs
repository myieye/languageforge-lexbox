using FwLiteShared.Auth;
using FwLiteShared.Sync;
using LcmCrdt.Data;
using LexCore.Sync;
using SIL.Harmony;
using Microsoft.JSInterop;

namespace FwLiteShared.Services;

public class SyncServiceJsInvokable(SyncService syncService, SyncRepository syncRepository)
{
    [JSInvokable]
    public Task<ProjectSyncStatus> GetSyncStatus()
    {
        return syncService.GetSyncStatus();
    }

    [JSInvokable]
    public async Task<SyncJobResult> TriggerFwHeadlessSync()
    {
        var triggerFailure = await syncService.TriggerSync();
        if (triggerFailure is not null) return triggerFailure;
        return await syncService.AwaitSyncFinished();
    }

    [JSInvokable]
    public Task<PendingCommits?> CountPendingCrdtCommits()
    {
        return syncService.CountPendingCrdtCommits();
    }

    [JSInvokable]
    public Task<DateTimeOffset?> GetLatestSyncedCommitDate()
    {
        return syncRepository.GetLatestSyncedCommitDate();
    }

    [JSInvokable]
    public Task<SyncResults> ExecuteSync(bool skipNotifications)
    {
        return Task.Run(async () => await syncService.ExecuteSync(skipNotifications));
    }

    [JSInvokable]
    public Task<LexboxServer?> GetCurrentServer()
    {
        return syncService.GetCurrentServer();
    }
}
