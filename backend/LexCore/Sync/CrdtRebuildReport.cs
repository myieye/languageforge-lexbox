using System.Text.Json.Serialization;

namespace LexCore.Sync;

/// <param name="MatchedServerBefore">Whether FwHeadless held exactly the server's commits before the rebuild; null when it never got that far.</param>
/// <param name="MatchedServerAfter">The same after the db was replaced; null when it wasn't.</param>
/// <param name="BackupPath">The db as it was before it was replaced, in the project's FwHeadless folder.</param>
public record CrdtRebuildReport(
    CrdtRebuildReportStatus Status,
    string? Message,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt = null,
    bool? MatchedServerBefore = null,
    bool? MatchedServerAfter = null,
    Guid[]? UnsyncedCommitIds = null,
    string[]? DataDifferences = null,
    string? BackupPath = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CrdtRebuildReportStatus
{
    Running,
    Rebuilt,
    // the statuses below left the db as it was
    SyncFailed,
    UnsyncedCommits,
    DataDifference,
    Failed,
}
