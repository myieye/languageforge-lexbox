using Microsoft.EntityFrameworkCore;
using MiniLcm.SyncHelpers;

namespace LcmCrdt;

public static class OrderPicker
{
    public static async Task<double> PickOrder<T>(IQueryable<T> siblings, BetweenPosition? between = null, Guid? movingId = null)
        where T : class, IOrderableNoId, IObjectWithId//this is weird, but WritingSystems should not be IOrderable, because that won't work with FW data, but they have Ids when working with CRDTs
    {
        // a common case that we can optimize by not querying whole objects
        if (between is null or { Previous: null, Next: null })
        {
            var others = movingId is { } id ? siblings.Where(s => s.Id != id) : siblings;
            var currMaxOrder = await others.Select(s => s.Order).DefaultIfEmpty().MaxAsync();
            return currMaxOrder + 1;
        }

        var items = await siblings.Select(s => new OrderedItem(s.Id, s.Order)).ToListAsync();
        return PickOrder(items, between, movingId);
    }

    // Duplicated entry point because Picture can't be IObjectWithId but needs to be ordered
    public static double PickOrder<T>(List<T> items, BetweenPosition? between = null, Guid? movingId = null)
        where T : class, IOrderable
    {
        return PickOrder([.. items.Select(i => new OrderedItem(i.Id, i.Order))], between, movingId);
    }

    private sealed record OrderedItem(Guid Id, double Order);

    private static double PickOrder(List<OrderedItem> items, BetweenPosition? between, Guid? movingId)
    {
        // An item is not its own neighbour: when a move is a positional no-op, leaving it in would
        // bisect against its own current order and churn a new value on every sync.
        if (movingId is not null) items.RemoveAll(i => i.Id == movingId);

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
            (null, null) => items.Select(i => i.Order).DefaultIfEmpty().Max() + 1,
            // when next is missing, deleted, or has shifted before previous, "between" is not
            // representable and previous wins: place into the first free gap above it
            (not null, _) => FirstGapAbove(previous.Order, items),
            (null, not null) => FirstGapBelow(next.Order, items),
        };
    }

    private static double FirstGapAbove(double lower, List<OrderedItem> items)
    {
        var upper = items.Where(i => i.Order > lower).Select(i => (double?)i.Order).Min();
        return upper is null ? lower + 1 : Midpoint(lower, upper.Value);
    }

    private static double FirstGapBelow(double upper, List<OrderedItem> items)
    {
        var lower = items.Where(i => i.Order < upper).Select(i => (double?)i.Order).Max();
        return lower is null ? upper - 1 : Midpoint(lower.Value, upper);
    }

    // ~50 bisections of the same gap exhaust double precision; the midpoint then equals a bound and
    // mints a duplicate after all. RepairDuplicateOrders renumbers such ties away at the next fwdata
    // sync; a crdt-only project would keep the tie.
    private static double Midpoint(double lower, double upper) => lower + (upper - lower) / 2;

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
