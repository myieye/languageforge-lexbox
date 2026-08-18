using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SIL.Harmony.Changes;
using SIL.Harmony.Config;

namespace LcmCrdt.Tests;

// With UnknownChangeHandling.Fallback, a change type from a newer client that this client doesn't
// have registered deserializes as OpaqueChange (null EntityType, apply skipped) — the activity view
// must tolerate it. Production still uses the default Throw (sync fails instead); only this fixture
// opts into Fallback, covering the activity view for when it's turned on.
public class HistoryServiceOpaqueChangeTests : HistoryServiceActivityTestsBase
{
    internal static void EnableFallback(IServiceCollection services) =>
        services.Configure<HarmonyConfig>(config => config.UnknownChangeHandling = UnknownChangeHandling.Fallback);

    protected override MiniLcmApiFixture CreateFixture() =>
        MiniLcmApiFixture.Create(configureServices: EnableFallback);

    internal static OpaqueChange UnknownChange(Guid entityId)
    {
        using var doc = JsonDocument.Parse(
            $$"""{"$type":"ChangeFromTheFuture","EntityId":"{{entityId}}","SomeValue":7}""");
        return new OpaqueChange
        {
            TypeName = "ChangeFromTheFuture",
            EntityId = entityId,
            RawJson = doc.RootElement.Clone()
        };
    }

    [Fact]
    public async Task ProjectActivity_ToleratesOpaqueChanges()
    {
        var entryId = await CreateEntry("apple");
        await DataModel.AddChange(ClientId, UnknownChange(entryId), Meta());

        var activities = await Service.ProjectActivity();

        var activity = activities.Should().ContainSingle(a => a.Changes.Any(c => c.Entity.Change is OpaqueChange)).Subject;
        activity.ChangeName.Should().Be("Unknown");
        activity.Changes.Single().Info.Subject.Should().BeNull();
    }

    [Fact]
    public async Task LoadChangeContext_ToleratesOpaqueEditOfExistingEntity()
    {
        var entryId = await CreateEntry("apple");
        var commit = await DataModel.AddChange(ClientId, UnknownChange(entryId), Meta());

        var context = await Service.LoadChangeContext(commit.Id, 0);

        context.ChangeName.Should().Be("Unknown");
        context.Snapshot.Should().BeNull();
        context.AffectedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadChangeContext_ToleratesOpaqueCreateOfUnknownEntity()
    {
        // An opaque create: the entity was never materialized on this client, so it has no snapshots.
        var commit = await DataModel.AddChange(ClientId, UnknownChange(Guid.NewGuid()), Meta());

        var context = await Service.LoadChangeContext(commit.Id, 0);

        context.ChangeName.Should().Be("Unknown");
        context.Snapshot.Should().BeNull();
        context.AffectedEntries.Should().BeEmpty();
    }
}

// Guards the fixture isolation the class above relies on: HarmonyConfig gets baked into the EF
// model, and EF caches the model process-wide, so without an isolated EF service provider the
// first fixture to build it would decide UnknownChangeHandling for the whole test run (that race
// passed locally and failed on CI).
public class OpaqueChangeFixtureIsolationTests
{
    [Fact]
    public async Task FallbackFixtureApplies_WhenADefaultFixtureBuiltTheEfModelFirst()
    {
        await using var defaultFixture = MiniLcmApiFixture.Create();
        await defaultFixture.InitializeAsync();

        await using var fallbackFixture = MiniLcmApiFixture.Create(
            configureServices: HistoryServiceOpaqueChangeTests.EnableFallback);
        await fallbackFixture.InitializeAsync();
        var clientId = fallbackFixture.GetService<CurrentProjectService>().ProjectData.ClientId;
        await fallbackFixture.DataModel.AddChange(clientId,
            HistoryServiceOpaqueChangeTests.UnknownChange(Guid.NewGuid()));

        var activities = await fallbackFixture.GetService<HistoryService>().ProjectActivity();

        activities.SelectMany(a => a.Changes).Should().Contain(c => c.Entity.Change is OpaqueChange);
    }
}
