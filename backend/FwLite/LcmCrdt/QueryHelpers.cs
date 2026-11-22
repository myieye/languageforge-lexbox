using System.Globalization;

namespace LcmCrdt;

public static class QueryHelpers
{
    public static void Finalize(this Entry entry,
        IComparer<ComplexFormComponent> complexFormComparer)
    {
        entry.Senses.ApplySortOrder();
        entry.Components.ApplySortOrder();
        entry.ComplexForms.Sort(complexFormComparer);
        foreach (var sense in entry.Senses)
        {
            sense.Finalize();
        }
    }

    public static void Finalize(this Sense sense)
    {
        sense.ExampleSentences.ApplySortOrder();
        foreach (var exampleSentence in sense.ExampleSentences)
        {
            exampleSentence.Finalize();
        }
    }

    public static void Finalize(this ExampleSentence exampleSentence)
    {
        var firstTranslation = exampleSentence.Translations.FirstOrDefault();
        if (Translation.IsMissingTranslationId(firstTranslation?.Id))
        {
            firstTranslation.Id = exampleSentence.DefaultFirstTranslationId;
        }
    }

    private static void ApplySortOrder<T>(this List<T> items) where T : IOrderable
    {
        items.Sort((a, b) =>
        {
            var result = a.Order.CompareTo(b.Order);
            if (result != 0) return result;
            return a.Id.CompareTo(b.Id);
        });
    }

    public static IComparer<ComplexFormComponent> AsComplexFormComparer(this IComparer<string> headwordComparer, WritingSystemId? ws)
    {
        return Comparer<ComplexFormComponent>.Create((a, b) =>
        {
            var result = headwordComparer.Compare(SortHeadword(a.ComplexFormHeadword, ws), SortHeadword(b.ComplexFormHeadword, ws));
            if (result != 0) return result;
            result = a.ComplexFormHomographNumber.CompareTo(b.ComplexFormHomographNumber);
            if (result != 0) return result;
            return a.ComplexFormEntryId.CompareTo(b.ComplexFormEntryId);
        });
    }

    private static string SortHeadword(MultiString headword, WritingSystemId? ws)
    {
        if (ws is { } wsId && headword.Values.TryGetValue(wsId, out var value) && !string.IsNullOrEmpty(value)) return value;
        return headword.FirstValueByCode() ?? "";
    }
}
