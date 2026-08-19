using LcmCrdt.Changes;
using Microsoft.EntityFrameworkCore;
using IChange = SIL.Harmony.Changes.IChange;

namespace LcmCrdt.Tests;

public class RepairDuplicateOrdersTests : IAsyncLifetime
{
    private readonly MiniLcmApiFixture _fixture = new();
    private readonly Guid _entryId = Guid.NewGuid();
    private readonly Guid _senseId = Guid.NewGuid();
    private readonly Guid _controlEntryId = Guid.NewGuid();
    private readonly Guid _controlSenseId = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    // Every type RepairDuplicateOrders repairs. Pictures are not among them: they live on the sense
    // rather than in a table of their own.
    public enum Ordered { Sense, ExampleSentence, ComplexFormComponent, WritingSystem }

    // Tied is the group the test breaks; Control is a second, untied group that must come out
    // untouched. Writing systems group by Type rather than by a parent Guid, so these are their two.
    private enum Group { Tied, Control }

    private const WritingSystemType TiedWsType = WritingSystemType.Vernacular;
    private const WritingSystemType ControlWsType = WritingSystemType.Analysis;

    // The control group is spread out rather than left at 1, 2, 3. A repair that renumbered every
    // group instead of only tied ones would be invisible against consecutive orders, since
    // renumbering already-consecutive siblings is a no-op.
    private static readonly double[] ControlOrders = [10, 20, 30];

    [Theory]
    [InlineData(Ordered.Sense)]
    [InlineData(Ordered.ExampleSentence)]
    [InlineData(Ordered.ComplexFormComponent)]
    [InlineData(Ordered.WritingSystem)]
    public async Task RepairMakesTiedOrdersDistinctWithoutChangingTheReadOrder(Ordered type)
    {
        var ids = await CreateSiblings(type);
        await SetOrder(type, ids[2], (await Read(type, Group.Tied)).First(s => s.Id == ids[1]).Order);

        var before = await Read(type, Group.Tied);
        before.Select(s => s.Order).Distinct().Should().HaveCountLessThan(before.Length, "the tie must actually exist");

        var changed = await _fixture.Api.RepairDuplicateOrders();

        changed.Should().BeGreaterThan(0);
        var after = await Read(type, Group.Tied);
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
        await SetOrder(type, ids[2], (await Read(type, Group.Tied)).First(s => s.Id == ids[1]).Order);
        var controlBefore = await Read(type, Group.Control);

        (await _fixture.Api.RepairDuplicateOrders()).Should().BeGreaterThan(0);
        var afterFirst = await Read(type, Group.Tied);
        (await Read(type, Group.Control)).Should().Equal(controlBefore,
            "a tie in one group is no reason to renumber a group that does not have one");

        (await _fixture.Api.RepairDuplicateOrders()).Should().Be(0, "a repaired project has no ties left to break");
        (await Read(type, Group.Tied)).Should().Equal(afterFirst);
        (await Read(type, Group.Control)).Should().Equal(controlBefore);
    }

    [Theory]
    [InlineData(Ordered.Sense)]
    [InlineData(Ordered.ExampleSentence)]
    [InlineData(Ordered.ComplexFormComponent)]
    [InlineData(Ordered.WritingSystem)]
    public async Task RepairDoesNothingOnAProjectWithoutTies(Ordered type)
    {
        await CreateSiblings(type);
        var before = await Read(type, Group.Tied);
        var controlBefore = await Read(type, Group.Control);

        (await _fixture.Api.RepairDuplicateOrders()).Should().Be(0);
        (await Read(type, Group.Tied)).Should().Equal(before);
        (await Read(type, Group.Control)).Should().Equal(controlBefore,
            "distinct but non-consecutive orders must not be renumbered");
    }

    // Three siblings in the tied group, in creation order, plus an untied control group.
    private async Task<Guid[]> CreateSiblings(Ordered type)
    {
        var (tied, control) = type switch
        {
            Ordered.Sense => await CreateSenses(),
            Ordered.ExampleSentence => await CreateExampleSentences(),
            Ordered.ComplexFormComponent => await CreateComplexFormComponents(),
            Ordered.WritingSystem => await CreateWritingSystems(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        for (var i = 0; i < control.Length; i++) await SetOrder(type, control[i], ControlOrders[i]);
        return tied;
    }

    /// <summary>
    /// Orders are set directly rather than through the api because the api can no longer produce a
    /// tie, which is the point of the OrderPicker fix; history left them on real projects.
    /// </summary>
    private async Task SetOrder(Ordered type, Guid id, double order)
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

    // Reads a group back the way RepairDuplicateOrders reads it, so "the read order did not move"
    // means what it says. Writing systems are the reason this is not one query: they tie-break in SQL,
    // where SQLite's Guid ordering differs from .NET's, so re-sorting them here would compare the
    // repair against an order the app never shows.
    private async Task<(Guid Id, double Order)[]> Read(Ordered type, Group group)
    {
        if (type == Ordered.WritingSystem)
        {
            var wsType = group == Group.Tied ? TiedWsType : ControlWsType;
            return [.. (await _fixture.DbContext.WritingSystemsOrdered.Where(ws => ws.Type == wsType)
                .Select(ws => new { ws.Id, ws.Order }).ToListAsync())
                .Select(ws => (ws.Id, ws.Order))];
        }

        var senseId = group == Group.Tied ? _senseId : _controlSenseId;
        var entryId = group == Group.Tied ? _entryId : _controlEntryId;
        var rows = type switch
        {
            Ordered.Sense => await _fixture.DbContext.Senses.Where(s => s.EntryId == entryId)
                .Select(s => new { s.Id, s.Order }).ToListAsync(),
            Ordered.ExampleSentence => await _fixture.DbContext.ExampleSentences.Where(e => e.SenseId == senseId)
                .Select(e => new { e.Id, e.Order }).ToListAsync(),
            Ordered.ComplexFormComponent => await _fixture.DbContext.ComplexFormComponents
                .Where(c => c.ComplexFormEntryId == entryId)
                .Select(c => new { c.Id, c.Order }).ToListAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        return [.. rows.OrderBy(r => r.Order).ThenBy(r => r.Id).Select(r => (r.Id, r.Order))];
    }

    private async Task<(Guid[] Tied, Guid[] Control)> CreateSenses()
    {
        await _fixture.Api.CreateEntry(new Entry { Id = _entryId, LexemeForm = { ["en"] = "test" } });
        await _fixture.Api.CreateEntry(new Entry { Id = _controlEntryId, LexemeForm = { ["en"] = "control" } });
        return (await AddSenses(_entryId, "s"), await AddSenses(_controlEntryId, "c"));
    }

    private async Task<Guid[]> AddSenses(Guid entryId, string prefix)
    {
        var ids = new Guid[3];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = Guid.NewGuid();
            await _fixture.Api.CreateSense(entryId, new Sense { Id = ids[i], Gloss = { ["en"] = $"{prefix}{i}" } });
        }
        return ids;
    }

    private async Task<(Guid[] Tied, Guid[] Control)> CreateExampleSentences()
    {
        await _fixture.Api.CreateEntry(new Entry { Id = _entryId, LexemeForm = { ["en"] = "test" } });
        await _fixture.Api.CreateSense(_entryId, new Sense { Id = _senseId, Gloss = { ["en"] = "s" } });
        await _fixture.Api.CreateSense(_entryId, new Sense { Id = _controlSenseId, Gloss = { ["en"] = "c" } });
        return (await AddExamples(_senseId, "e"), await AddExamples(_controlSenseId, "f"));
    }

    private async Task<Guid[]> AddExamples(Guid senseId, string prefix)
    {
        var ids = new Guid[3];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = Guid.NewGuid();
            await _fixture.Api.CreateExampleSentence(_entryId, senseId, new ExampleSentence
            {
                Id = ids[i],
                Sentence = { ["en"] = new RichString($"{prefix}{i}", "en") }
            });
        }
        return ids;
    }

    private async Task<(Guid[] Tied, Guid[] Control)> CreateComplexFormComponents()
    {
        var tiedForm = await _fixture.Api.CreateEntry(new Entry { Id = _entryId, LexemeForm = { ["en"] = "complex" } });
        var controlForm = await _fixture.Api.CreateEntry(new Entry { Id = _controlEntryId, LexemeForm = { ["en"] = "complex-control" } });
        return (await AddComponents(tiedForm!, "c"), await AddComponents(controlForm!, "d"));
    }

    private async Task<Guid[]> AddComponents(Entry complexForm, string prefix)
    {
        var ids = new Guid[3];
        for (var i = 0; i < ids.Length; i++)
        {
            var component = await _fixture.Api.CreateEntry(new Entry { Id = Guid.NewGuid(), LexemeForm = { ["en"] = $"{prefix}{i}" } });
            // CreateComplexFormComponent always mints its own entity id, so take the one it assigned.
            var created = await _fixture.Api.CreateComplexFormComponent(
                ComplexFormComponent.FromEntries(complexForm, component!));
            ids[i] = created.Id;
        }
        return ids;
    }

    private async Task<(Guid[] Tied, Guid[] Control)> CreateWritingSystems() =>
        (await AddWritingSystems(TiedWsType, "t"), await AddWritingSystems(ControlWsType, "a"));

    private async Task<Guid[]> AddWritingSystems(WritingSystemType type, string prefix)
    {
        var ids = new Guid[3];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = Guid.NewGuid();
            await _fixture.Api.CreateWritingSystem(new WritingSystem
            {
                Id = ids[i],
                WsId = $"qaa-x-{prefix}{i}",
                Name = $"{prefix}{i}",
                Abbreviation = $"{prefix}{i}",
                Font = "Arial",
                Type = type
            });
        }
        return ids;
    }
}
