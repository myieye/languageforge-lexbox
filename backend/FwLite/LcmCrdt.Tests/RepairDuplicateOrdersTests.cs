using LcmCrdt.Changes;
using Microsoft.EntityFrameworkCore;

namespace LcmCrdt.Tests;

public class RepairDuplicateOrdersTests : IAsyncLifetime
{
    private readonly MiniLcmApiFixture _fixture = new();
    private readonly Guid _entryId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid[]> CreateSenses(int count)
    {
        await _fixture.Api.CreateEntry(new Entry { Id = _entryId, LexemeForm = { ["en"] = "test" } });
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = Guid.NewGuid();
            await _fixture.Api.CreateSense(_entryId, new Sense { Id = ids[i], Gloss = { ["en"] = $"s{i}" } });
        }
        return ids;
    }

    /// <summary>
    /// Ties can no longer be produced through the api, which is the point of the OrderPicker fix,
    /// so force one the way history left it on real projects: two siblings holding the same Order.
    /// </summary>
    private async Task ForceTie(Guid senseId, double order)
    {
        await _fixture.DataModel.AddChange(Guid.NewGuid(), new SetOrderChange<Sense>(senseId, order));
    }

    private async Task<(Guid Id, double Order)[]> ReadSenses() =>
        [.. (await _fixture.DbContext.Senses.Where(s => s.EntryId == _entryId)
            .Select(s => new { s.Id, s.Order }).ToListAsync())
            .OrderBy(s => s.Order).ThenBy(s => s.Id)
            .Select(s => (s.Id, s.Order))];

    [Fact]
    public async Task RepairMakesTiedOrdersDistinctWithoutChangingTheReadOrder()
    {
        var ids = await CreateSenses(3);
        await ForceTie(ids[2], (await ReadSenses()).First(s => s.Id == ids[1]).Order);

        var before = await ReadSenses();
        before.Select(s => s.Order).Distinct().Should().HaveCountLessThan(3, "the tie must actually exist");

        var changed = await _fixture.Api.RepairDuplicateOrders();

        changed.Should().BeGreaterThan(0);
        var after = await ReadSenses();
        after.Select(s => s.Order).Should().OnlyHaveUniqueItems();
        after.Select(s => s.Id).Should().Equal(before.Select(s => s.Id),
            "renumbering follows the existing read order, so nothing a user sees may move");
    }

    [Fact]
    public async Task RepairIsIdempotentAndLeavesUntiedGroupsAlone()
    {
        var ids = await CreateSenses(3);
        await ForceTie(ids[2], (await ReadSenses()).First(s => s.Id == ids[1]).Order);

        (await _fixture.Api.RepairDuplicateOrders()).Should().BeGreaterThan(0);
        var afterFirst = await ReadSenses();

        (await _fixture.Api.RepairDuplicateOrders()).Should().Be(0, "a repaired project has no ties left to break");
        (await ReadSenses()).Should().Equal(afterFirst);
    }

    [Fact]
    public async Task RepairDoesNothingOnAProjectWithoutTies()
    {
        await CreateSenses(3);
        var before = await ReadSenses();

        (await _fixture.Api.RepairDuplicateOrders()).Should().Be(0);
        (await ReadSenses()).Should().Equal(before, "fractional but distinct orders must not be renumbered");
    }
}
