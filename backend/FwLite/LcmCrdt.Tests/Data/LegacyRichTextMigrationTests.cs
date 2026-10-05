using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SIL.Harmony.Db;

namespace LcmCrdt.Tests.Data;

/// <summary>
/// The v1 dump only has some of the legacy rich text shapes, so this adds the rest before migrating and checks that
/// nothing legacy survives, in the db and through the API.
/// </summary>
[Collection("MigrationTests")]
public class LegacyRichTextMigrationTests : IAsyncLifetime
{
    private static readonly Guid PlainTranslation = new("A1000000-0000-0000-0000-000000000001");
    private static readonly Guid NumericTranslation = new("A1000000-0000-0000-0000-000000000002");
    private static readonly Guid BlankTranslation = new("A1000000-0000-0000-0000-000000000003");
    private static readonly Guid SpanlessTranslation = new("A1000000-0000-0000-0000-000000000004");
    private static readonly Guid NoTranslation = new("A1000000-0000-0000-0000-000000000005");
    private static readonly Guid NonBreakingSpaceTranslation = new("A1000000-0000-0000-0000-000000000006");

    //the sense these hang off is the apple sense in the v1 dump. Column names and types are the v1 ones too:
    //Translation before it was renamed to Translations, and Reference still TEXT.
    private const string LegacyRows = """
        INSERT INTO ExampleSentence (Id, "Order", Sentence, Translation, Reference, SenseId, DeletedAt, SnapshotId) VALUES
            ('A1000000-0000-0000-0000-000000000001', 2, '{"en":"plain sentence"}', '{"en":"plain translation"}', 'Genesis 1:1', 'D510AA82-5557-4DD4-8B1F-1EC89FACB979', null, null),
            ('A1000000-0000-0000-0000-000000000002', 3, '{}', '{"en":"42"}', null, 'D510AA82-5557-4DD4-8B1F-1EC89FACB979', null, null),
            ('A1000000-0000-0000-0000-000000000003', 4, '{}', '{"en":"   "}', '   ', 'D510AA82-5557-4DD4-8B1F-1EC89FACB979', null, null),
            ('A1000000-0000-0000-0000-000000000004', 5, '{}', '{"en":{"Spans":[]}}', null, 'D510AA82-5557-4DD4-8B1F-1EC89FACB979', null, null),
            ('A1000000-0000-0000-0000-000000000005', 6, '{}', '{}', null, 'D510AA82-5557-4DD4-8B1F-1EC89FACB979', null, null),
            ('A1000000-0000-0000-0000-000000000006', 7, '{}', '{"en":"' || char(160) || '"}', '{"a":1}', 'D510AA82-5557-4DD4-8B1F-1EC89FACB979', null, null);
        """;

    private readonly RegressionTestHelper _helper = new("LegacyRichTextMigrationTest");

    public Task InitializeAsync() => _helper.InitializeAsync(RegressionTestHelper.RegressionVersion.v1, LegacyRows);

    public Task DisposeAsync() => _helper.DisposeAsync();

    [Fact]
    public async Task NoLegacyShapesAreLeftInTheDb()
    {
        await using var dbContext = await _helper.Services.GetRequiredService<ICrdtDbContextFactory>().CreateDbContextAsync();

        var nonArrayTranslations = await dbContext.Database
            .SqlQuery<int>($"SELECT count(*) AS Value FROM ExampleSentence WHERE json_type(Translations) <> 'array'")
            .SingleAsync();
        nonArrayTranslations.Should().Be(0);

        var plainStringValues = await dbContext.Database.SqlQuery<int>($"""
            SELECT count(*) AS Value FROM (
                SELECT 1 FROM Entry, json_each(Entry.LiteralMeaning) kv WHERE kv.type <> 'object'
                UNION ALL SELECT 1 FROM Entry, json_each(Entry.Note) kv WHERE kv.type <> 'object'
                UNION ALL SELECT 1 FROM Sense, json_each(Sense.Definition) kv WHERE kv.type <> 'object'
                UNION ALL SELECT 1 FROM ExampleSentence, json_each(ExampleSentence.Sentence) kv WHERE kv.type <> 'object'
                UNION ALL SELECT 1 FROM ExampleSentence, json_each(ExampleSentence.Translations) t,
                                       json_each(json_extract(t.value, '$.Text')) kv WHERE kv.type <> 'object')
            """).SingleAsync();
        plainStringValues.Should().Be(0);

        var nonRichReferences = await dbContext.Database.SqlQuery<int>($"""
            SELECT count(*) AS Value FROM ExampleSentence
            WHERE Reference IS NOT NULL AND (json_valid(Reference) = 0 OR json_type(Reference) <> 'object')
            """).SingleAsync();
        nonRichReferences.Should().Be(0);

        var placeholderIds = await dbContext.Database.SqlQuery<int>($"""
            SELECT count(*) AS Value FROM ExampleSentence, json_each(ExampleSentence.Translations) t
            WHERE json_extract(t.value, '$.Id') = '3dce1982-8e93-44f1-b92c-e9c7bdf72801'
            """).SingleAsync();
        placeholderIds.Should().Be(2, "the two legacy translations that survived keep the placeholder id CrdtRepairs looks for");
    }

    [Fact]
    public async Task LegacyExamplesReadBackAsCurrentModels()
    {
        var api = _helper.Services.GetRequiredService<IMiniLcmApi>();
        var entries = await api.GetAllEntries().ToArrayAsync();
        var examples = entries.SelectMany(e => e.Senses)
            .SelectMany(s => s.ExampleSentences)
            .ToDictionary(e => e.Id);

        var plain = examples[PlainTranslation];
        plain.Sentence["en"].GetPlainText().Should().Be("plain sentence");
        var translation = plain.Translations.Should().ContainSingle().Subject;
        //the migration writes the missing-translation placeholder; QueryHelpers maps it to this on read
        translation.Id.Should().Be(plain.DefaultFirstTranslationId);
        translation.Text["en"].GetPlainText().Should().Be("plain translation");
        plain.Reference!.GetPlainText().Should().Be("Genesis 1:1");

        examples[NumericTranslation].Translations.Should().ContainSingle()
            .Subject.Text["en"].GetPlainText().Should().Be("42");

        examples[BlankTranslation].Translations.Should().BeEmpty();
        examples[BlankTranslation].Reference.Should().BeNull();
        examples[SpanlessTranslation].Translations.Should().BeEmpty();
        examples[NoTranslation].Translations.Should().BeEmpty();

        var nbsp = examples[NonBreakingSpaceTranslation];
        nbsp.Translations.Should().BeEmpty("a non-breaking space is whitespace to IsNullOrWhiteSpace");
        //a reference that is json but not a rich string is legacy text, so keep the text rather than dropping it
        nbsp.Reference!.GetPlainText().Should().Be("""{"a":1}""");
    }
}
