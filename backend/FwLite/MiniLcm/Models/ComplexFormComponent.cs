using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniLcm.Attributes;

namespace MiniLcm.Models;

public record ComplexFormComponent : IObjectWithId<ComplexFormComponent>, IOrderable
{
    public static ComplexFormComponent FromEntries(Entry complexFormEntry,
        Entry componentEntry,
        Guid? componentSenseId = null)
    {
        if (componentEntry.Id == default) throw new ArgumentException("componentEntry.Id is empty");
        if (complexFormEntry.Id == default) throw new ArgumentException("complexFormEntry.Id is empty");
        return new ComplexFormComponent
        {
            Id = Guid.NewGuid(),
            ComplexFormEntryId = complexFormEntry.Id,
            ComplexFormHeadword = complexFormEntry.Headwords(),
            ComplexFormHomographNumber = complexFormEntry.HomographNumber,
            ComponentEntryId = componentEntry.Id,
            ComponentHeadword = componentEntry.Headwords(),
            ComponentHomographNumber = componentEntry.HomographNumber,
            ComponentSenseId = componentSenseId,
        };
    }

    private Guid _id;
    [MiniLcmInternal]
    public Guid Id
    {
        get
        {
            Debug.Assert(_id != Guid.Empty, "Id is not set and should not be used");
            return _id;
        }
        set
        {
            _id = value;
        }
    }

    [MiniLcmInternal]
    public Guid? MaybeId => _id == Guid.Empty ? null : _id;

    // The order property applies to the component NOT the complex form. Complex forms are sorted alphabetically in FieldWorks.
    [MiniLcmInternal]
    public double Order { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public virtual required Guid ComplexFormEntryId { get; set; }
    // Headwords and homograph numbers are display-only copies of the linked entries, recomputed on every read and ignored by sync.
    [JsonConverter(typeof(LegacyHeadwordConverter))]
    public MultiString ComplexFormHeadword { get; set; } = new();
    public int ComplexFormHomographNumber { get; set; }
    public virtual required Guid ComponentEntryId { get; set; }
    public virtual Guid? ComponentSenseId { get; set; } = null;
    [JsonConverter(typeof(LegacyHeadwordConverter))]
    public MultiString ComponentHeadword { get; set; } = new();
    public int ComponentHomographNumber { get; set; }


    public Guid[] GetReferences()
    {
        Span<Guid> senseId = (ComponentSenseId.HasValue ? [ComponentSenseId.Value] : []);
        return
        [
            ComplexFormEntryId,
            ComponentEntryId,
            ..senseId
        ];
    }

    public void RemoveReference(Guid id, DateTimeOffset time)
    {
        if (ComponentEntryId == id || ComplexFormEntryId == id || ComponentSenseId == id)
            DeletedAt = time;
    }

    public ComplexFormComponent Copy()
    {
        return new ComplexFormComponent
        {
            Id = _id,
            Order = Order,
            ComplexFormEntryId = ComplexFormEntryId,
            ComplexFormHeadword = ComplexFormHeadword.Copy(),
            ComplexFormHomographNumber = ComplexFormHomographNumber,
            ComponentEntryId = ComponentEntryId,
            ComponentHeadword = ComponentHeadword.Copy(),
            ComponentHomographNumber = ComponentHomographNumber,
            ComponentSenseId = ComponentSenseId,
            DeletedAt = DeletedAt,
        };
    }

    public override string ToString()
    {
        return
            $"{nameof(Order)}: {Order}, {nameof(DeletedAt)}: {DeletedAt}, {nameof(ComplexFormEntryId)}: {ComplexFormEntryId}, {nameof(ComplexFormHeadword)}: {ComplexFormHeadword}, {nameof(ComplexFormHomographNumber)}: {ComplexFormHomographNumber}, {nameof(ComponentEntryId)}: {ComponentEntryId}, {nameof(ComponentSenseId)}: {ComponentSenseId}, {nameof(ComponentHeadword)}: {ComponentHeadword}, {nameof(ComponentHomographNumber)}: {ComponentHomographNumber}";
    }
}

/// <summary>
/// Snapshots written before link headwords were multi-WS hold a single string with no WS label; it goes under "default".
/// </summary>
public class LegacyHeadwordConverter : JsonConverter<MultiString>
{
    private static readonly MultiStringConverter MultiStringConverter = new();

    public override bool HandleNull => true;

    public override MultiString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return new();
            case JsonTokenType.String:
                var value = reader.GetString();
                if (string.IsNullOrEmpty(value) || value == Entry.UnknownHeadword) return new();
                return new() { { WritingSystemId.Default.Code, value } };
            default:
                return MultiStringConverter.Read(ref reader, typeToConvert, options) ?? new();
        }
    }

    public override void Write(Utf8JsonWriter writer, MultiString? value, JsonSerializerOptions options)
    {
        MultiStringConverter.Write(writer, value ?? new MultiString(), options);
    }
}
