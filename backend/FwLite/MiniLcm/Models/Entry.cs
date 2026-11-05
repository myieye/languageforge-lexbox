
namespace MiniLcm.Models;

public record Entry : IObjectWithId<Entry>
{
    public Guid Id { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }

    public virtual MultiString LexemeForm { get; set; } = new();

    public virtual MultiString CitationForm { get; set; } = new();

    public virtual RichMultiString LiteralMeaning { get; set; } = new();
    public virtual MorphTypeKind MorphType { get; set; } = MorphTypeKind.Stem;
    public virtual int HomographNumber { get; set; }
    public virtual List<Sense> Senses { get; set; } = [];

    public virtual RichMultiString Note { get; set; } = new();

    /// <summary>
    /// Components making up this complex entry
    /// </summary>
    public virtual List<ComplexFormComponent> Components { get; set; } = [];

    /// <summary>
    /// This entry is a part of these complex forms
    /// </summary>
    public virtual List<ComplexFormComponent> ComplexForms { get; set; } = [];

    public virtual List<ComplexFormType> ComplexFormTypes { get; set; } = [];

    public virtual List<Publication> PublishIn { get; set; } = [];

    public const string UnknownHeadword = "(Unknown)";

    public string Headword()
    {
        //order by code to ensure the headword is stable
        //todo choose ws by preference based on ws order/default
        //todo this does not apply morph tokens — see #1284
        //https://github.com/sillsdev/languageforge-lexbox/issues/1284
        var word = CitationForm.Values.OrderBy(kvp => kvp.Key.Code).FirstOrDefault().Value?.Trim();
        if (string.IsNullOrEmpty(word)) word = LexemeForm.Values.OrderBy(kvp => kvp.Key.Code).FirstOrDefault().Value?.Trim();
        return string.IsNullOrEmpty(word) ? UnknownHeadword : word;
    }

    /// <summary>
    /// The headword in every non-audio writing system that has one: the citation form, else the lexeme form with the morph type's tokens.
    /// Covers every writing system with data, not just current ones, so headwords survive writing system list changes.
    /// </summary>
    public MultiString Headwords(MorphType? morphType = null)
    {
        var result = new MultiString();
        var wsIds = CitationForm.Values.Keys
            .Union(LexemeForm.Values.Keys)
            .Where(ws => !ws.IsAudio);

        foreach (var wsId in wsIds)
        {
            var citation = CitationForm[wsId]?.Trim();
            if (!string.IsNullOrEmpty(citation))
            {
                result[wsId] = citation;
                continue;
            }

            var lexeme = LexemeForm[wsId]?.Trim();
            if (!string.IsNullOrEmpty(lexeme))
            {
                result[wsId] = ((morphType?.Prefix ?? "") + lexeme + (morphType?.Postfix ?? "")).Trim();
            }
        }

        return result;
    }

    public Entry Copy()
    {
        return new Entry
        {
            Id = Id,
            DeletedAt = DeletedAt,
            LexemeForm = LexemeForm.Copy(),
            CitationForm = CitationForm.Copy(),
            LiteralMeaning = LiteralMeaning.Copy(),
            Note = Note.Copy(),
            MorphType = MorphType,
            HomographNumber = HomographNumber,
            Senses = [..Senses.Select(s => s.Copy())],
            Components =
            [
                ..Components.Select(c => c.Copy())
            ],
            ComplexForms =
            [
                ..ComplexForms.Select(c => c.Copy())
            ],
            ComplexFormTypes =
            [
                ..ComplexFormTypes.Select(cft => cft.Copy())
            ],
            PublishIn = [ ..PublishIn.Select(p => p.Copy())]
        };
    }

    public Guid[] GetReferences()
    {
        return [];
    }

    public void RemoveReference(Guid id, DateTimeOffset time)
    {
    }
}

public class Variants
{
    public Guid Id { get; set; }
    public IList<ComplexFormComponent> VariantsOf { get; set; } = [];
    public IList<VariantType> Types { get; set; } = [];
}


public class VariantType
{
    public required Guid Id { get; set; }
    public required MultiString Name { get; set; }
}
