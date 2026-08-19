using MiniLcm.SyncHelpers;

namespace LcmCrdt.Tests;

public class OrderPickerTests : IAsyncLifetime
{
    private readonly MiniLcmApiFixture _fixture = new();
    private readonly Guid _entryId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        // A parent entry must exist to satisfy Sense.EntryId when we seed senses directly.
        await _fixture.Api.CreateEntry(new Entry { Id = _entryId, LexemeForm = { ["en"] = "test" } });
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    public enum Variant
    {
        // OrderPicker.PickOrder(List<T>, ...) — synchronous, in-memory
        List,
        // OrderPicker.PickOrder(IQueryable<T>, ...) — async, against real SQLite
        Async
    }

    // Sentinel for a between reference whose id is not present among the siblings
    // (i.e. the referenced item was deleted by another user in the meantime).
    private const int Missing = -1;

    // Offset by 1 so index 0 does not map to Guid.Empty (which EF will not round-trip as a key).
    private static Guid ItemId(int index) => Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}");
    private static readonly Guid MissingId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    // The item being placed, when it is not one of the siblings (i.e. a create).
    private static readonly Guid NewItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");

    private static Guid? Ref(int? r) => r switch
    {
        null => null,
        Missing => MissingId,
        _ => ItemId(r.Value)
    };

    // Builds a between position from sibling indices. Missing means the referenced item is
    // not among the siblings (deleted by another user); null means that end is unspecified.
    private static BetweenPosition Between(int? previous = null, int? next = null) =>
        new(Ref(previous), Ref(next));

    // Bounds are exclusive; PositiveInfinity means the slot is open ended, so only "above every
    // sibling" is being claimed. Exact values are not asserted because the picked order carries a
    // jitter offset derived from the item's id. PickOrder_JitterIsPinned covers the arithmetic.
    public record OrderScenario(string Name, double[] ExistingOrders, BetweenPosition? Between, double Above, double Below)
    {
        public override string ToString() => Name;
    }

    private static IEnumerable<OrderScenario> AllScenarios()
    {
        // 1. No siblings, no between → the first order in an empty group
        yield return new("empty, no between", [], null, 0, double.PositiveInfinity);
        // 2. between is null → append after max (async optimized MaxAsync path)
        yield return new("no between → append", [1, 2, 3], null, 3, double.PositiveInfinity);
        // 3. between {null,null} → append after max (async ToListAsync path, distinct from #2)
        yield return new("between {null,null} → append", [1, 2, 3], Between(), 3, double.PositiveInfinity);
        // 4. previous only → land in the gap above previous, rather than striding to previous + 1,
        //    which would land on a sibling whenever one sits directly above.
        yield return new("previous only", [10, 20], Between(previous: 0), 10, 20);
        // 5. next only → the gap below next
        yield return new("next only", [10, 20], Between(next: 1), 10, 20);
        // 6. previous < next → between the two
        yield return new("previous < next", [10, 20], Between(previous: 0, next: 1), 10, 20);
        // 7. previous > next (shifted past each other) → previous wins, into the gap above it
        yield return new("inverted previous > next", [20, 10], Between(previous: 0, next: 1), 20, double.PositiveInfinity);
        // 8. previous == next order (distinct items, equal order) → previous wins; nothing sits
        //    above 10, so append past it
        yield return new("equal orders", [10, 10], Between(previous: 0, next: 1), 10, double.PositiveInfinity);
        // 9. deleted references
        yield return new("both refs deleted → append", [1, 2, 3], Between(previous: Missing, next: Missing), 3, double.PositiveInfinity);
        yield return new("previous deleted, next present", [10, 20], Between(previous: Missing, next: 1), 10, 20);
        yield return new("next deleted, previous present", [10, 20], Between(previous: 0, next: Missing), 10, 20);
        // 10. siblings supplied out of Order sequence → result unaffected by list ordering
        yield return new("unordered siblings", [30, 10, 20], Between(previous: 1, next: 2), 10, 20);
        // 11. the collision cases: a sibling occupies the slot the old code strode onto. These are
        //     what the fwdata sync hits, because the between window is diffed from the merge base
        //     and fwdata while the order is computed against the live crdt siblings.
        yield return new("occupied slot above previous", [2, 3], Between(previous: 0), 2, 3);
        yield return new("occupied slot below next", [1, 2], Between(next: 1), 1, 2);
        // the interloper at 2.5 sits inside the between window, so only the gap below it is free
        yield return new("interloper inside the window", [2, 2.5, 3], Between(previous: 0, next: 2), 2, 2.5);
    }

    public static IEnumerable<object[]> Scenarios()
    {
        foreach (var scenario in AllScenarios())
        {
            yield return [Variant.List, scenario];
            yield return [Variant.Async, scenario];
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task PickOrder_LandsInTheIntendedGap(Variant variant, OrderScenario scenario)
    {
        var siblings = await Arrange(variant, scenario.ExistingOrders);

        var result = await Pick(siblings, NewItemId, scenario.Between);

        result.Should().BeGreaterThan(scenario.Above).And.BeLessThan(scenario.Below);
        scenario.ExistingOrders.Should().NotContain(result,
            "no result may land on an order a sibling already holds, whatever the between window says");
        (await Pick(siblings, NewItemId, scenario.Between)).Should().Be(result,
            "the same inputs must always pick the same order");
    }

    [Theory]
    [InlineData(Variant.List)]
    [InlineData(Variant.Async)]
    public async Task PickOrder_GivesTwoItemsPickingTheSameSlotDistinctOrders(Variant variant)
    {
        // The hole nothing else covers: two clients editing offline both insert into the gap between
        // orders 1 and 2, neither seeing the other's item. The plain midpoint hands both 1.5 and the
        // merge mints a duplicate order; the jitter offset is what makes them differ.
        var siblings = await Arrange(variant, [1, 2]);
        var between = Between(previous: 0, next: 1);

        var first = await Pick(siblings, ItemId(50), between);
        var second = await Pick(siblings, ItemId(51), between);

        first.Should().NotBe(second);
        new[] { first, second }.Should().AllSatisfy(o => o.Should().BeGreaterThan(1).And.BeLessThan(2));
    }

    [Theory]
    [InlineData(Variant.List)]
    [InlineData(Variant.Async)]
    public async Task PickOrder_GivesTwoItemsAppendingToTheSameGroupDistinctOrders(Variant variant)
    {
        // The same hole on the commonest path of all: two offline clients each add an item to the end
        // of the same group. Both used to compute max + 1.
        var siblings = await Arrange(variant, [1, 2]);

        var first = await Pick(siblings, ItemId(50));
        var second = await Pick(siblings, ItemId(51));

        first.Should().NotBe(second);
        new[] { first, second }.Should().AllSatisfy(o => o.Should().BeGreaterThan(2,
            "an appended item outranks every sibling it can see, whatever its jitter comes out as"));
    }

    [Fact]
    public void PickOrder_JitterIsPinned()
    {
        // The offset has to come out the same on every client and every framework version, so it is
        // computed from the id by hand (byte fold plus murmur3 finalizer) instead of via
        // Guid.GetHashCode. This is the value that construction yields; a different one means the
        // function drifted and clients built from different commits place the same item differently.
        var order = OrderPicker.PickOrder(BuildList([1, 2]),
            Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Between(previous: 0, next: 1));

        order.Should().Be(1.4912295549675059);
    }

    [Theory]
    [InlineData(Variant.List)]
    [InlineData(Variant.Async)]
    public async Task PickOrder_RepickingAPositionalNoOpReturnsTheSameOrder(Variant variant)
    {
        // Moving an item to where it already is has to settle, or every fwdata sync hands it a fresh
        // order and reorders the entry forever. The item is left out of its own sibling set, so the
        // first pick lands in the slot the item itself occupies (jittered, so not the integer it
        // happens to hold today), and every pick after that agrees with it.
        var siblings = await Arrange(variant, [2, 3]);
        var movingId = ItemId(1); // the item at order 3
        var between = Between(previous: 0);

        var first = await Pick(siblings, movingId, between);
        await SetOrder(siblings, movingId, first);
        var second = await Pick(siblings, movingId, between);

        first.Should().BeGreaterThan(2, "the only sibling above order 2 is the item being moved, so it keeps its place");
        second.Should().Be(first);
    }

    [Theory]
    [InlineData(Variant.List)]
    [InlineData(Variant.Async)]
    public async Task PickOrder_RepickingAnAppendOfTheLastItemReturnsTheSameOrder(Variant variant)
    {
        // No between means "append", which the async path answers from max(Order) without loading the
        // siblings. That shortcut must drop the moving item too, or re-appending the last item hands
        // it a bigger order every sync.
        var siblings = await Arrange(variant, [1, 2, 3]);
        var movingId = ItemId(2); // the item at order 3

        var first = await Pick(siblings, movingId);
        await SetOrder(siblings, movingId, first);
        var second = await Pick(siblings, movingId);

        first.Should().BeGreaterThan(2, "the highest remaining sibling is at 2, so the moving item stays last");
        second.Should().Be(first);
    }

    [Theory]
    [InlineData(Variant.List)]
    [InlineData(Variant.Async)]
    public async Task RepeatedInsertionIntoSameGap_StaysBetweenNeighborsAndDistinct(Variant variant)
    {
        // Two fixed neighbors at orders 0 and 1. We repeatedly insert between the lower
        // neighbor and the most-recently-inserted item, which is what happens when a user
        // keeps dropping items into the same spot, and the gap shrinks each time.
        var siblings = await Arrange(variant, [0, 1]);
        var lowerId = ItemId(0);
        var upperId = ItemId(1);

        var results = new List<double>();
        const int insertions = 18;
        for (var i = 2; i < 2 + insertions; i++)
        {
            var newId = ItemId(i);
            var order = await Pick(siblings, newId, new BetweenPosition(lowerId, upperId));
            results.Add(order);
            await Add(siblings, newId, order);
            upperId = newId;
        }

        results.Should().OnlyContain(o => o > 0 && o < 1, "every insertion lands strictly between the two neighbors");
        results.Should().BeInDescendingOrder("each insertion subdivides the shrinking gap above the lower neighbor");
        results.Distinct().Should().HaveCount(results.Count, "no two insertions collapse to the same order");
    }

    [Theory]
    [InlineData(Variant.List)]
    [InlineData(Variant.Async)]
    public async Task PickOrder_SubdividesTheSameGapNTimesBeforePrecisionRunsOut(Variant variant)
    {
        // Worst case: every insertion subdivides the gap above the same fixed lower neighbour,
        // against the item inserted immediately before it, so the gap shrinks each time until double
        // precision can no longer represent a value distinct from its bounds.
        //
        // This count is the reordering budget available within one sibling group between fwdata
        // syncs: CrdtMiniLcmApi.RepairDuplicateOrders renumbers siblings to integers at each sync,
        // which resets the gap back to full width.
        var siblings = await Arrange(variant, [1, 2]);
        var lowerId = ItemId(0);
        var upperId = ItemId(1);

        // Measured, not derived: one mantissa bit per subdivision predicts 52, and that is what both
        // variants produce. The jitter band is only +/-1/32 of the gap, so a step leaves between
        // 15/32 and 17/32 of it and the budget comes out the same as a plain bisection's, which is
        // the point of a band that narrow. It does depend on the ids, which are fixed here.
        const int subdivisions = 52;
        const double lowerOrder = 1;
        var upperOrder = 2.0;
        var seenOrders = new HashSet<double> { 1, 2 };

        // Asserting inside the loop rather than breaking out of it: a loop that stops on any unusable
        // value would report the same count whether the 53rd pick collapsed onto the lower bound
        // (what exhaustion looks like) or escaped the gap entirely, which would be a different bug.
        for (var i = 2; i < 2 + subdivisions; i++)
        {
            var newId = ItemId(i);
            var order = await Pick(siblings, newId, new BetweenPosition(lowerId, upperId));

            order.Should().BeGreaterThan(lowerOrder).And.BeLessThan(upperOrder);
            seenOrders.Add(order).Should().BeTrue("every subdivision within the budget yields a fresh order");

            await Add(siblings, newId, order);
            upperId = newId;
            upperOrder = order;
        }

        // One past the budget the gap is a single ulp wide, so the jittered value rounds onto a bound
        // and the fallback midpoint is the lower bound itself: the duplicate order that
        // RepairDuplicateOrders exists to renumber away.
        (await Pick(siblings, ItemId(2 + subdivisions), new BetweenPosition(lowerId, upperId)))
            .Should().Be(lowerOrder);
    }

    [Theory]
    [InlineData(Variant.List)]
    [InlineData(Variant.Async)]
    public async Task PickOrder_DoesNotLandOnAnOrderASiblingAlreadyHas(Variant variant)
    {
        // `previous.Order + 1` was returned without checking whether a sibling already sat there, so
        // inserting after the sense at order 1 returned 2 when a sense already held order 2.
        //
        // A duplicate order is not cosmetic: nothing can then be placed *between* those two senses,
        // because there is no representable value between x and x. The fwdata->crdt pass can no
        // longer reproduce fwdata's order, so the crdt->fwdata pass "corrects" fwdata instead, and
        // the two stores swap that entry's senses back and forth on every sync.
        // Observed on a real project: 13 entries carrying duplicate sense orders, all created
        // within one sync run.
        double[] existingOrders = [1, 2];
        var siblings = await Arrange(variant, existingOrders);

        var result = await Pick(siblings, NewItemId, Between(previous: 0));

        existingOrders.Should().NotContain(result,
            "an order that a sibling already holds makes the gap either side of it unaddressable");
    }

    // An arranged sibling set, pickable more than once so a test can check that a second pick agrees
    // with the first. For the async variant the DbContext tracks these same Sense instances, so
    // SetOrder and Add reach the database through them.
    private sealed record Siblings(Variant Variant, List<Sense> Items, IQueryable<Sense>? Query);

    private async Task<Siblings> Arrange(Variant variant, double[] orders)
    {
        var items = BuildList(orders);
        if (variant == Variant.List) return new(variant, items, null);
        _fixture.DbContext.AddRange(items);
        await _fixture.DbContext.SaveChangesAsync();
        return new(variant, items, _fixture.DbContext.Senses.Where(s => s.EntryId == _entryId));
    }

    private static async Task<double> Pick(Siblings siblings, Guid itemId, BetweenPosition? between = null) =>
        siblings.Variant == Variant.List
            ? OrderPicker.PickOrder(siblings.Items, itemId, between)
            : await OrderPicker.PickOrder(siblings.Query!, itemId, between);

    private async Task SetOrder(Siblings siblings, Guid itemId, double order)
    {
        siblings.Items.Single(s => s.Id == itemId).Order = order;
        if (siblings.Variant == Variant.Async) await _fixture.DbContext.SaveChangesAsync();
    }

    private async Task Add(Siblings siblings, Guid itemId, double order)
    {
        var sense = new Sense { Id = itemId, EntryId = _entryId, Order = order };
        siblings.Items.Add(sense);
        if (siblings.Variant == Variant.Async)
        {
            _fixture.DbContext.Add(sense);
            await _fixture.DbContext.SaveChangesAsync();
        }
    }

    private List<Sense> BuildList(double[] orders) =>
        orders.Select((order, index) => new Sense { Id = ItemId(index), EntryId = _entryId, Order = order }).ToList();
}
