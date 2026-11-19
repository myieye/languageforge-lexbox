namespace FwHeadless.Services;

public interface IProjectMetadataService
{
    Task BlockFromSyncAsync(Guid projectId, string? reason = null);
    Task UnblockFromSyncAsync(Guid projectId);
    /// <summary>Blocks the project unless it is already blocked. False when it was already blocked.</summary>
    Task<bool> TryBlockFromSyncAsync(Guid projectId, string reason);
    /// <summary>Lifts the block only if it still has this reason, so a block someone else set since stays.</summary>
    Task<bool> UnblockFromSyncAsync(Guid projectId, string reason);
    Task<SyncBlockedInfo?> GetSyncBlockedInfoAsync(Guid projectId);
}
