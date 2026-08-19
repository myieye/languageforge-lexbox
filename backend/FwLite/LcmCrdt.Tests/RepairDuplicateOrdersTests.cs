using LcmCrdt.Changes;
using Microsoft.EntityFrameworkCore;
using IChange = SIL.Harmony.Changes.IChange;

namespace LcmCrdt.Tests;

public class RepairDuplicateOrdersTests : IAsyncLifetime
{
    private readonly MiniLcmApiFixture _fixture = new();
    private readonly Guid _entryId = Guid.NewGuid();
    private readonly Guid _senseId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    // Every type RepairDuplicateOrders repairs. Pictures are not among them: their order is picked
    // during change replay, which is frozen.
    public enum Ordered { Sense, ExampleSentence, ComplexFormComponent, WritingSystem }

    // Writing systems are grouped by Type rather than by a parent Guid, and this is the one they use.
    private const WritingSystemType WsType = WritingSystemType.Vernacular;

    [Theory]
    [InlineData(Ordered.Sense)]
    [InlineData(Ordered.ExampleSentence)]
    [InlineData(Ordered.ComplexFormComponent)]
    [InlineData(Ordered.WritingSystem)]
    public async Task RepairMakesTiedOrdersDistinctWithoutChangingTheReadOrder(Ordered type)
    {
        var ids = await CreateSiblings(type);
        await ForceTie(type, ids[2], (await Read(type)).First(s => s.Id == ids[1]).Order);

        var before = await Read(type);
        before.Select(s => s.Order).Distinct().Should().HaveCountLessThan(before.Length, "the tie must actually exist");

        var changed = await _fixture.Api.RepairDuplicateOrders();

        changed.Should().BeGreaterThan(0);
        var after = await Read(type);
        after.Select(s => s.Order).Should().OnlyHaveUniqueItems();
        after.Select(s => s.Id).Should().Equal(before.Select(s => s.Id),
            "renumbering follows the existing read order, so nothing a user sees may move");
    }

    [Theory]
    [InlineData(Ordered.Sense)]
    [InlineData(Ordered.ExampleSentence)]
    [InlineData(Ordered.ComplexFormComponent)]
    [InlineData(Ordered.WritingSystem)]
    public async Task RepairIsIdempotentAndLeavesUntiedGroupsAlone(Ordered type)
    {
        var ids = await CreateSiblings(type);
        await ForceTie(type, ids[2], (await Read(type)).First(s => s.Id == ids[1]).Order);

        (await _fixture.Api.RepairDuplicateOrders()).Should().BeGreaterThan(0);
        var afterFirst = await Read(type);

        (await _fixture.Api.RepairDuplicateOrders()).Should().Be(0, "a repaired project has no ties left to break");
        (await Read(type)).Should().Equal(afterFirst);
    }

    [Theory]
    [InlineData(Ordered.Sense)]
    [InlineData(Ordered.ExampleSentence)]
    [InlineData(Ordered.ComplexFormComponent)]
    [InlineData(Ordered.WritingSystem)]
    public async Task RepairDoesNothingOnAProjectWithoutTies(Ordered type)
    {
        await CreateSiblings(type);
        var before = await Read(type);

        (await _fixture.Api.RepairDuplicateOrders()).Should().Be(0);
        (await Read(type)).Should().Equal(before, "fractional but distinct orders must not be renumbered");
    }

    // Three siblings in one group, in creation order.
    private async Task<Guid[]> CreateSiblings(Ordered type) => type switch
    {
        Ordered.Sense => await CreateSenses(3),
        Ordered.ExampleSentence => await CreateExampleSentences(3),
        Ordered.ComplexFormComponent => await CreateComplexFormComponents(3),
        Ordered.WritingSystem => await CreateWritingSystems(3),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// Ties can no longer be produced through the api, which is the point of the OrderPicker fix,
    /// so force one the way history left it on real projects: two siblings holding the same Order.
    /// </summary>
    private async Task ForceTie(Ordered type, Guid id, double order)
    {
        IChange change = type switch
        {
            Ordered.Sense => new SetOrderChange<Sense>(id, order),
            Ordered.ExampleSentence => new SetOrderChange<ExampleSentence>(id, order),
            Ordered.ComplexFormComponent => new SetOrderChange<ComplexFormComponent>(id, order),
            Ordered.WritingSystem => new SetOrderChange<WritingSystem>(id, order),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        await _fixture.DataModel.AddChange(Guid.NewGuid(), change);
    }

    // Reads the group back the way RepairDuplicateOrders reads it, so "the read order did not move"
    // means what it says. Writing systems are the reason this is not one query: they tie-break in SQL,
    // where SQLite's Guid ordering differs from .NET's, so re-sorting them here would compare the
    // repair against an order the app never shows.
    private async Task<(Guid Id, double Order)[]> Read(Ordered type)
    {
        if (type == Ordered.WritingSystem)
        {
            return [.. (await _fixture.DbContext.WritingSystemsOrdered.Where(ws => ws.Type == WsType)
                .Select(ws => new { ws.Id, ws.Order }).ToListAsync())
                .Select(ws => (ws.Id, ws.Order))];
        }

        var rows = type switch
        {
            Ordered.Sense => await _fixture.DbContext.Senses.Where(s => s.EntryId == _entryId)
                .Select(s => new { s.Id, s.Order }).ToListAsync(),
            Ordered.ExampleSentence => await _fixture.DbContext.ExampleSentences.Where(e => e.SenseId == _senseId)
                .Select(e => new { e.Id, e.Order }).ToListAsync(),
            Ordered.ComplexFormComponent => await _fixture.DbContext.ComplexFormComponents
                .Where(c => c.ComplexFormEntryId == _entryId)
                .Select(c => new { c.Id, c.Order }).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        return [.. rows.OrderBy(r => r.Order).ThenBy(r => r.Id).Select(r => (r.Id, r.Order))];
    }

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

    private async Task<Guid[]> CreateExampleSentences(int count)
    {
        await _fixture.Api.CreateEntry(new Entry { Id = _entryId, LexemeForm = { ["en"] = "test" } });
        await _fixture.Api.CreateSense(_entryId, new Sense { Id = _senseId, Gloss = { ["en"] = "s" } });
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = Guid.NewGuid();
            await _fixture.Api.CreateExampleSentence(_entryId, _senseId, new ExampleSentence
            {
                Id = ids[i],
                Sentence = { ["en"] = new RichString($"e{i}", "en") }
            });
        }
        return ids;
    }

    private async Task<Guid[]> CreateComplexFormComponents(int count)
    {
        var complexForm = await _fixture.Api.CreateEntry(new Entry { Id = _entryId, LexemeForm = { ["en"] = "complex" } });
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            var component = await _fixture.Api.CreateEntry(new Entry { Id = Guid.NewGuid(), LexemeForm = { ["en"] = $"c{i}" } });
            // CreateComplexFormComponent always mints its own entity id, so take the one it assigned.
            var created = await _fixture.Api.CreateComplexFormComponent(
                ComplexFormComponent.FromEntries(complexForm, component));
            ids[i] = created.Id;
        }
        return ids;
    }

    private async Task<Guid[]> CreateWritingSystems(int count)
    {
        var ids = new Guid[count];
        for (var i = 0; i < count; i++)
        {
            ids[i] = Guid.NewGuid();
            await _fixture.Api.CreateWritingSystem(new WritingSystem
            {
                Id = ids[i],
                WsId = $"qaa-x-t{i}",
                Name = $"test{i}",
                Abbreviation = $"t{i}",
                Font = "Arial",
                Type = WsType
            });
        }
        return ids;
    }
}
