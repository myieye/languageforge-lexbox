using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using MiniLcm.SyncHelpers;

namespace LcmCrdt;

public static class OrderPicker
{
    public static async Task<double> PickOrder<T>(IQueryable<T> siblings, Guid itemId, BetweenPosition? between = null)
        where T : class, IOrderableNoId, IObjectWithId//this is weird, but WritingSystems should not be IOrderable, because that won't work with FW data, but they have Ids when working with CRDTs
    {
        // a common case that we can optimize by not querying whole objects
        if (between is null or { Previous: null, Next: null })
        {
            var currMaxOrder = await siblings.Where(s => s.Id != itemId).Select(s => s.Order).DefaultIfEmpty().MaxAsync();
            return Append(currMaxOrder, itemId);
        }

        var items = await siblings.Select(s => new OrderedItem(s.Id, s.Order)).ToListAsync();
        return PickOrder(items, itemId, between);
    }

    // Duplicated entry point because Picture can't be IObjectWithId but needs to be ordered
    public static double PickOrder<T>(List<T> items, Guid itemId, BetweenPosition? between = null)
        where T : class, IOrderable
    {
        return PickOrder([.. items.Select(i => new OrderedItem(i.Id, i.Order))], itemId, between);
    }

    private sealed record OrderedItem(Guid Id, double Order);

    private static double PickOrder(List<OrderedItem> items, Guid itemId, BetweenPosition? between)
    {
        // An item is not its own neighbour: when a move is a positional no-op, leaving it in would
        // bisect against its own current order and churn a new value on every sync.
        items.RemoveAll(i => i.Id == itemId);

        var previous = between?.Previous is { } previousId ? items.Find(i => i.Id == previousId) : null;
        var next = between?.Next is { } nextId ? items.Find(i => i.Id == nextId) : null;

        // The between window is computed by diffing two *other* views of this collection (the merge
        // base against fwdata), so the live sibling set here can hold orders the diff never saw.
        // Landing on one is not a misplacement, it is corruption: no value fits between x and x, so
        // nothing can ever be inserted between those two siblings again, and the fwdata sync then
        // reorders the entry on every run forever. Every branch therefore places the item in a gap
        // that is genuinely free rather than striding blindly by +/-1.
        return (previous, next) switch
        {
            // another user deleted items in the meantime?
            (null, null) => Append(items.Select(i => i.Order).DefaultIfEmpty().Max(), itemId),
            // when next is missing, deleted, or has shifted before previous, "between" is not
            // representable and previous wins: place into the first free gap above it
            (not null, _) => FirstGapAbove(previous.Order, items, itemId),
            (null, not null) => FirstGapBelow(next.Order, items, itemId),
        };
    }

    private static double FirstGapAbove(double lower, List<OrderedItem> items, Guid itemId)
    {
        var upper = items.Where(i => i.Order > lower).Select(i => (double?)i.Order).Min();
        return upper is null ? Append(lower, itemId) : Jittered(lower, upper.Value, itemId);
    }

    private static double FirstGapBelow(double upper, List<OrderedItem> items, Guid itemId)
    {
        var lower = items.Where(i => i.Order < upper).Select(i => (double?)i.Order).Max();
        return lower is null ? Prepend(upper, itemId) : Jittered(lower.Value, upper, itemId);
    }

    // Open ended, so a whole unit is free past the outermost sibling and the result clears every
    // local sibling whatever the jitter comes out as. That is why these two spread over a full unit
    // where Jittered has to fit inside 1/16 of the gap.
    private static double Append(double max, Guid itemId) => max + 0.5 + UnitJitter(itemId);
    private static double Prepend(double min, Guid itemId) => min - 1.5 + UnitJitter(itemId);

    private const double JitterHalfBand = 1.0 / 32;

    /// <summary>
    /// The midpoint of the gap, offset by a hash of the item's own id.
    ///
    /// Two clients editing offline at the same time both bisect the same gap, both land on the plain
    /// midpoint, and mint a duplicate order the moment their changes merge. Concurrent picks into one
    /// gap are necessarily for <i>different</i> items (a second pick for the same item is the same
    /// row, settled by last-writer-wins), so the item's id is a client-distinct token already at
    /// hand: offsetting by it makes the two values differ with no coordination.
    ///
    /// The band is +/-1/32 of the gap around the midpoint. Band width costs precision and jitter
    /// width buys collision resistance, and the two trade against each other: a band this narrow
    /// leaves 15/32 to 17/32 of the gap to subdivide next, so the ~50 same-gap subdivisions a plain
    /// bisection allows survive, while 52 bits of jitter spread over 1/16 of the gap keeps collisions
    /// near 2^-47. A collision is just another duplicate order, which
    /// CrdtMiniLcmApi.RepairDuplicateOrders sweeps at the next fwdata sync.
    /// </summary>
    private static double Jittered(double lower, double upper, Guid itemId)
    {
        var value = lower + (upper - lower) * (0.5 + JitterHalfBand * (2 * UnitJitter(itemId) - 1));
        // ~50 subdivisions of one gap exhaust double precision and round the offset onto a bound.
        // The plain midpoint equals a bound by then too, so this mints a tie after all;
        // RepairDuplicateOrders renumbers it away at the next fwdata sync, a crdt-only project keeps it.
        return value > lower && value < upper ? value : Midpoint(lower, upper);
    }

    private static double Midpoint(double lower, double upper) => lower + (upper - lower) / 2;

    // u(id) in [0,1). Pinned by construction (byte fold plus the murmur3 finalizer) rather than by a
    // framework contract, so every version and platform derives the same offset for the same id.
    // Guid.GetHashCode would not do: 32 bits, and hash codes carry no cross-version guarantee.
    private static double UnitJitter(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        var x = BinaryPrimitives.ReadUInt64LittleEndian(bytes)
              ^ BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        x *= 0xc4ceb9fe1a85ec53UL;
        x ^= x >> 33;
        return (x >> 12) * (1.0 / (1UL << 52));
    }

    /// <summary>
    /// The v1 algorithm, frozen. Called from <see cref="Changes.CreateSensePictureChange"/> while
    /// changes are being (re)applied, so every client must compute the same order for the same
    /// history no matter which app version replays it, or projected state diverges between clients.
    /// Never change this. A better algorithm needs a new change class so old serialized changes
    /// keep replaying identically.
    /// </summary>
    public static double PickOrderV1ForChangeReplay<T>(List<T> items, BetweenPosition? between = null)
        where T : class, IOrderable
    {
        var previous = between?.Previous is { } previousId ? items.Find(item => item.Id == previousId) : null;
        var next = between?.Next is { } nextId ? items.Find(item => item.Id == nextId) : null;
        return (previous, next) switch
        {
            (null, null) => items.Select(s => s.Order).DefaultIfEmpty().Max() + 1,
            (_, null) => previous.Order + 1,
            (null, _) => next.Order - 1,
            _ => previous.Order < next.Order ? (previous.Order + next.Order) / 2 : previous.Order + 1,
        };
    }
}
