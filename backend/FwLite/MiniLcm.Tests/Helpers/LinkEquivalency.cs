using FluentAssertions.Equivalency;

namespace MiniLcm.Tests.Helpers;

public static class LinkEquivalency
{
    /// <summary>
    /// For tests whose generated linked entries share headwords: the APIs number those as homographs, so the links' copies don't round-trip.
    /// </summary>
    public static EquivalencyOptions<Entry> ExcludingLinkHomographNumbers(this EquivalencyOptions<Entry> options)
    {
        return options
            .For(e => e.Components).Exclude(c => c.ComplexFormHomographNumber)
            .For(e => e.Components).Exclude(c => c.ComponentHomographNumber)
            .For(e => e.ComplexForms).Exclude(c => c.ComplexFormHomographNumber)
            .For(e => e.ComplexForms).Exclude(c => c.ComponentHomographNumber);
    }
}
