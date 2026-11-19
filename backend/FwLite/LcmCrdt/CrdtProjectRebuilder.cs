using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SIL.Harmony;
using SIL.Harmony.Config;
using SIL.Harmony.Core;

namespace LcmCrdt;

public record CrdtRebuildOptions(bool DiscardUnsyncedCommits = false, bool AllowDataDifference = false);

public enum CrdtRebuildStatus
{
    Rebuilt,
    // the local db has commits the server doesn't, a rebuild would lose them
    UnsyncedCommits,
    // the data in the rebuilt db is not the data in the local db
    DataDifference,
}

/// <param name="MatchedServerAfter">null when the db wasn't replaced, or when it was but couldn't be compared afterwards</param>
/// <param name="BackupPath">the db as it was before it was replaced</param>
public record CrdtRebuildResult(
    CrdtRebuildStatus Status,
    bool MatchedServerBefore,
    bool? MatchedServerAfter,
    Guid[] UnsyncedCommitIds,
    string[] DataDifferences,
    string? BackupPath);

/// <summary>
/// Replaces a project's db with a fresh download of its commits from the server, for a db whose commits can no longer
/// be brought in line with the server by syncing (e.g. a commit the server holds under another client id, #3003).
/// Sync first: commits the server doesn't have are lost. The caller must keep everything else off the db meanwhile.
/// </summary>
public class CrdtProjectRebuilder(
    IServiceProvider provider,
    CrdtProjectsService projectsService,
    IOptions<HarmonyConfig> harmonyConfig,
    ILogger<CrdtProjectRebuilder> logger)
{
    private const int MaxReportedDifferences = 200;

    private record ProjectContents(ProjectData Data, HashSet<Guid> CommitIds, SyncState SyncState, Dictionary<string, string>? Entities, string? ReadError);

    public async Task<CrdtRebuildResult> Rebuild(CrdtProject project, Func<ProjectData, ValueTask<ISyncable>> createRemote, CrdtRebuildOptions options)
    {
        var old = await Read(project);
        var remote = await createRemote(old.Data);
        var matchedServerBefore = SameCommits(old.SyncState, await remote.GetSyncState());

        // next to the live db, and not *.sqlite, so it is never listed as a project
        var rebuiltPath = $"{project.DbPath}.rebuild-{Guid.NewGuid():N}";
        try
        {
            var rebuiltProject = await projectsService.CreateProject(new(old.Data.Name,
                old.Data.Code,
                old.Data.Id,
                old.Data.OriginDomain is null ? null : new Uri(old.Data.OriginDomain),
                AfterCreate: async (services, created) =>
                {
                    var results = await services.GetRequiredService<DataModel>().SyncWith(await createRemote(created.Data!));
                    if (!results.IsSynced) throw new InvalidOperationException("Failed to download the project's commits");
                },
                FwProjectId: old.Data.FwProjectId,
                AuthenticatedUser: old.Data.LastUserName,
                AuthenticatedUserId: old.Data.LastUserId,
                Role: old.Data.Role,
                DbPath: rebuiltPath));
            var rebuilt = await Read(rebuiltProject);

            CrdtRebuildResult NotRebuilt(CrdtRebuildStatus status, Guid[] unsynced, string[] differences) =>
                new(status, matchedServerBefore, null, unsynced, differences, null);

            Guid[] unsyncedCommitIds = [.. old.CommitIds.Except(rebuilt.CommitIds)];
            if (unsyncedCommitIds is not [] && !options.DiscardUnsyncedCommits)
                return NotRebuilt(CrdtRebuildStatus.UnsyncedCommits, unsyncedCommitIds, []);

            var differences = Differences(old, rebuilt);
            if (differences is not [] && !options.AllowDataDifference)
                return NotRebuilt(CrdtRebuildStatus.DataDifference, unsyncedCommitIds, differences);

            // the checks above describe the db as it was read, so it must not have changed since
            if (!(await Read(project, withEntities: false)).CommitIds.SetEquals(old.CommitIds))
                throw new InvalidOperationException($"Project '{project.Name}' changed while it was being rebuilt");

            var backupPath = $"{project.DbPath}.bak-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}";
            Replace(project.DbPath, rebuiltPath, backupPath);
            logger.LogInformation("Rebuilt project {Project} from the server, the previous db is at {BackupPath}", project.Name, backupPath);

            bool? matchedServerAfter = null;
            try
            {
                var current = await Read(project, withEntities: false);
                matchedServerAfter = SameCommits(current.SyncState, await remote.GetSyncState());
            }
            catch (Exception e)
            {
                // the db is replaced either way, and the caller needs the backup path
                logger.LogWarning(e, "Could not compare the rebuilt project {Project} with the server", project.Name);
            }
            return new(CrdtRebuildStatus.Rebuilt,
                matchedServerBefore,
                matchedServerAfter,
                unsyncedCommitIds,
                differences,
                backupPath);
        }
        finally
        {
            await projectsService.EnsureDeleteProject(rebuiltPath, suppressException: true);
        }
    }

    private async Task<ProjectContents> Read(CrdtProject project, bool withEntities = true)
    {
        await using var scope = provider.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var api = await services.OpenCrdtProject(project);
        await using var dbContext = await services.GetRequiredService<IDbContextFactory<LcmCrdtDbContext>>().CreateDbContextAsync();
        var commitIds = await dbContext.Set<Commit>().Select(c => c.Id).ToHashSetAsync();
        var syncState = await services.GetRequiredService<DataModel>().GetSyncState();
        var data = await services.GetRequiredService<CurrentProjectService>().GetProjectData();
        if (!withEntities) return new(data, commitIds, syncState, null, null);
        try
        {
            return new(data, commitIds, syncState, Entities(await api.TakeProjectSnapshot()), null);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Unable to read the data of project {Project}", project.Name);
            return new(data, commitIds, syncState, null, e.Message);
        }
    }

    private Dictionary<string, string> Entities(ProjectSnapshot snapshot)
    {
        var entities = new Dictionary<string, string>();
        Add(snapshot.Entries);
        Add(snapshot.PartsOfSpeech);
        Add(snapshot.Publications);
        Add(snapshot.SemanticDomains);
        Add(snapshot.ComplexFormTypes);
        Add(snapshot.MorphTypes);
        Add(snapshot.WritingSystems.Analysis);
        Add(snapshot.WritingSystems.Vernacular);
        return entities;

        void Add<T>(IEnumerable<T> items) where T : IObjectWithId
        {
            foreach (var item in items)
                entities[$"{typeof(T).Name} {item.Id}"] = JsonSerializer.Serialize(item, harmonyConfig.Value.JsonSerializerOptions);
        }
    }

    private static string[] Differences(ProjectContents old, ProjectContents rebuilt)
    {
        if (old.Entities is null) return [$"The local db could not be read: {old.ReadError}"];
        if (rebuilt.Entities is null) return [$"The rebuilt db could not be read: {rebuilt.ReadError}"];
        return
        [
            .. old.Entities.Keys.Union(rebuilt.Entities.Keys)
                .Select(key => (key, local: old.Entities.GetValueOrDefault(key), server: rebuilt.Entities.GetValueOrDefault(key)))
                .Where(e => e.local != e.server)
                .Select(e => e.server is null ? $"{e.key} is only in the local db"
                    : e.local is null ? $"{e.key} is only on the server"
                    : $"{e.key} differs")
                .Order()
                .Take(MaxReportedDifferences)
        ];
    }

    internal static bool SameCommits(SyncState local, SyncState server)
    {
        // a peer on an older Harmony only reports each client's latest timestamp
        if (local.ClientStates is null || server.ClientStates is null || server.ClientStates.Any(c => c.OnlyHasTimestamp))
        {
            return local.ClientHeads.Count == server.ClientHeads.Count
                   && local.ClientHeads.All(head => server.ClientHeads.TryGetValue(head.Key, out var timestamp) && timestamp == head.Value);
        }
        return local.ClientStates.ToHashSet().SetEquals(server.ClientStates);
    }

    private static void Replace(string dbPath, string rebuiltPath, string backupPath)
    {
        using var live = Open(dbPath);
        using var rebuilt = Open(rebuiltPath);
        using (var backup = Open(backupPath))
        {
            live.BackupDatabase(backup);
        }

        using (var copyLocalResources = rebuilt.CreateCommand())
        {
            copyLocalResources.CommandText = """
                ATTACH DATABASE $backup AS old;
                INSERT OR IGNORE INTO LocalResource (Id, LocalPath) SELECT Id, LocalPath FROM old.LocalResource;
                DETACH DATABASE old;
                """;
            copyLocalResources.Parameters.AddWithValue("$backup", backupPath);
            copyLocalResources.ExecuteNonQuery();
        }

        // Written to the live file as one SQLite transaction, so a crash leaves either the old db or the new one.
        // Replacing the file instead would leave the old db's -wal next to the new db.
        rebuilt.BackupDatabase(live);
        using var pooled = new SqliteConnection($"Data Source={dbPath}");
        SqliteConnection.ClearPool(pooled);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        return connection;
    }
}
