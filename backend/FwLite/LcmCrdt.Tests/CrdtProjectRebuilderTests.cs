using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SIL.Harmony.Core;

namespace LcmCrdt.Tests;

public class CrdtProjectRebuilderTests : IAsyncLifetime
{
    private static readonly Guid ProjectId = Guid.Parse("5b1c2f0e-6a7d-4f3b-9c21-8e4d7a0b6f15");
    private readonly MiniLcmApiFixture _server = MiniLcmApiFixture.Create(seedWs: true, projectId: ProjectId);
    private readonly string _folder = Directory.CreateTempSubdirectory("crdt-rebuild").FullName;
    private CrdtProject _local = null!;

    public async Task InitializeAsync()
    {
        await _server.InitializeAsync();
        _local = await _server.GetService<CrdtProjectsService>().CreateProject(new("Sena 3", "rebuild-test", ProjectId,
            new Uri("https://lexbox.example"),
            FwProjectId: Guid.NewGuid(),
            DbPath: Path.Combine(_folder, "crdt.sqlite")));
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        await using (var pooled = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_local.DbPath}"))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pooled);
        }
        Directory.Delete(_folder, true);
    }

    private async Task<T> InLocal<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _server.GetService<IServiceScopeFactory>().CreateAsyncScope();
        await scope.ServiceProvider.OpenCrdtProject(_local);
        return await action(scope.ServiceProvider);
    }

    private Task<SyncResults> SyncLocal() =>
        InLocal(services => services.GetRequiredService<DataModel>().SyncWith(_server.DataModel));

    private Task<(Guid Id, Guid ClientId)[]> LocalCommits() => InLocal(async services =>
    {
        await using var db = await services.GetRequiredService<IDbContextFactory<LcmCrdtDbContext>>().CreateDbContextAsync();
        return (await db.Set<Commit>().ToArrayAsync()).Select(c => (c.Id, c.ClientId)).ToArray();
    });

    private Task<CrdtRebuildResult> Rebuild(CrdtRebuildOptions? options = null) =>
        _server.GetService<CrdtProjectRebuilder>()
            .Rebuild(_local, _ => ValueTask.FromResult<ISyncable>(_server.DataModel), options ?? new());

    [Fact]
    public async Task ACommitTheServerHoldsUnderAnotherClientId_IsReplacedByTheServersCopy()
    {
        // #3003: every download authored the seed commit itself, with the same id under its own client id
        var commitId = Guid.NewGuid();
        await ((ISyncable)_server.DataModel).AddRangeFromSync([new EmptyCommit(commitId, Guid.NewGuid())]);
        var oldClientId = _local.Data!.ClientId;
        await InLocal(async services =>
        {
            await ((ISyncable)services.GetRequiredService<DataModel>()).AddRangeFromSync([new EmptyCommit(commitId, oldClientId)]);
            return true;
        });
        await SyncLocal();
        (await LocalCommits()).Should().Contain((commitId, oldClientId), "syncing can't fix it, the server skips a commit id it already has");

        var result = await Rebuild();

        result.Should().BeEquivalentTo(new { Status = CrdtRebuildStatus.Rebuilt, MatchedServerBefore = false, MatchedServerAfter = true });
        var serverCommits = (await _server.DbContext.Set<Commit>().AsNoTracking().ToArrayAsync()).Select(c => (c.Id, c.ClientId));
        (await LocalCommits()).Should().BeEquivalentTo(serverCommits);
        File.Exists(result.BackupPath).Should().BeTrue();
        var data = await InLocal(services => services.GetRequiredService<CurrentProjectService>().GetProjectData().AsTask());
        data.ClientId.Should().NotBe(oldClientId);
        data.FwProjectId.Should().NotBeNull();
        Directory.GetFiles(_folder, "*.rebuild-*").Should().BeEmpty();
    }

    [Fact]
    public async Task UnsyncedCommitsAndDataDifferences_EachStopTheRebuildUntilAllowed()
    {
        await SyncLocal();
        var entry = await InLocal(services => services.GetRequiredService<IMiniLcmApi>()
            .CreateEntry(new Entry { LexemeForm = { ["en"] = "only local" } }));
        var commitsBefore = await LocalCommits();

        var unsynced = await Rebuild();
        unsynced.Status.Should().Be(CrdtRebuildStatus.UnsyncedCommits);
        unsynced.UnsyncedCommitIds.Should().ContainSingle();

        var different = await Rebuild(new(DiscardUnsyncedCommits: true));
        different.Status.Should().Be(CrdtRebuildStatus.DataDifference);
        different.DataDifferences.Should().Equal($"Entry {entry.Id} is only in the local db");
        (await LocalCommits()).Should().BeEquivalentTo(commitsBefore);
        Directory.GetFiles(_folder, "*.bak-*").Should().BeEmpty();

        var rebuilt = await Rebuild(new(DiscardUnsyncedCommits: true, AllowDataDifference: true));
        rebuilt.Status.Should().Be(CrdtRebuildStatus.Rebuilt);
        (await InLocal(services => services.GetRequiredService<IMiniLcmApi>().GetEntry(entry.Id))).Should().BeNull();
    }

    private class EmptyCommit : Commit
    {
        [SetsRequiredMembers]
        public EmptyCommit(Guid id, Guid clientId) : base(id, "", NullParentHash, new HybridDateTime(DateTimeOffset.UtcNow, 0))
        {
            HybridDateTime = new HybridDateTime(DateTimeOffset.UtcNow, 0);
            ClientId = clientId;
            SetParentHash(NullParentHash);
        }
    }
}
