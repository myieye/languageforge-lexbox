using Microsoft.EntityFrameworkCore;

namespace LcmCrdt.Tests.Data;

//seeds every shape the Translations column can hold (see Json.TranslationsPlainText) and runs the translation filters across them
public class LegacyTranslationFilterTests : IAsyncLifetime
{
    private const string CurrentEmpty = "current empty";
    private const string CurrentEn = "current en";
    private const string LegacyEmpty = "legacy empty";
    private const string LegacyEn = "legacy en";
    private const string LegacyEs = "legacy es";
    private const string LegacyPlain = "legacy plain";
    private const string LegacyNumeric = "legacy numeric";

    private readonly MiniLcmApiFixture _fixture = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        await CreateEntryWithExample(CurrentEmpty, []);
        await CreateEntryWithExample(CurrentEn, [new Translation() { Text = { { "en", new RichString("current translation") } } }]);
        await SetRawTranslations(await CreateEntryWithExample(LegacyEmpty, []), "{}");
        await SetRawTranslations(await CreateEntryWithExample(LegacyEn, []),
            """{"en":{"Spans":[{"Text":"legacy translation","Ws":"en"}]}}""");
        await SetRawTranslations(await CreateEntryWithExample(LegacyEs, []),
            """{"es":{"Spans":[{"Text":"legacy es translation","Ws":"es"}]}}""");
        //before rich text, both columns held a plain string per writing system
        var plain = await CreateEntryWithExample(LegacyPlain, []);
        await SetRawTranslations(plain, """{"en":"legacy plain translation"}""");
        await SetRawSentence(plain, """{"en":"legacy plain sentence"}""");
        //plain text that happens to parse as json is still text
        await SetRawTranslations(await CreateEntryWithExample(LegacyNumeric, []), """{"en":"42"}""");
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }

    private async Task<Guid> CreateEntryWithExample(string lexemeForm, IList<Translation> translations)
    {
        var exampleSentenceId = Guid.NewGuid();
        await _fixture.Api.CreateEntry(new Entry()
        {
            LexemeForm = { { "en", lexemeForm } },
            Senses =
            [
                new Sense()
                {
                    ExampleSentences =
                    [
                        new ExampleSentence()
                        {
                            Id = exampleSentenceId,
                            Sentence = { { "en", new RichString($"{lexemeForm} example") } },
                            Translations = translations
                        }
                    ]
                }
            ]
        });
        return exampleSentenceId;
    }

    private async Task SetRawTranslations(Guid exampleSentenceId, string json)
    {
        var rowsUpdated = await _fixture.DbContext.Database.ExecuteSqlAsync(
            $"UPDATE ExampleSentence SET Translations = {json} WHERE Id = {exampleSentenceId}");
        rowsUpdated.Should().Be(1);
    }

    private async Task SetRawSentence(Guid exampleSentenceId, string json)
    {
        var rowsUpdated = await _fixture.DbContext.Database.ExecuteSqlAsync(
            $"UPDATE ExampleSentence SET Sentence = {json} WHERE Id = {exampleSentenceId}");
        rowsUpdated.Should().Be(1);
    }

    private async Task<string[]> Filter(string gridifyFilter)
    {
        var entries = await _fixture.Api.GetEntries(new(Filter: new() { GridifyFilter = gridifyFilter })).ToArrayAsync();
        var count = await _fixture.Api.CountEntries(null, new(Filter: new() { GridifyFilter = gridifyFilter }));
        count.Should().Be(entries.Length);
        return entries.Select(e => e.LexemeForm["en"]).ToArray();
    }

    [Fact]
    public async Task LegacyEmptyObjectCountsAsNoTranslations()
    {
        var results = await Filter("Senses.ExampleSentences.Translations=null");
        results.Should().BeEquivalentTo([CurrentEmpty, LegacyEmpty]);
    }

    [Fact]
    public async Task LegacyObjectWithTextCountsAsHavingTranslations()
    {
        var results = await Filter("Senses.ExampleSentences.Translations!=null");
        results.Should().BeEquivalentTo([CurrentEn, LegacyEn, LegacyEs, LegacyPlain, LegacyNumeric]);
    }

    [Fact]
    public async Task CanFilterToMissingTranslationTextInEn()
    {
        var results = await Filter("Senses.ExampleSentences.Translations.Text[en]=");
        results.Should().BeEquivalentTo([CurrentEmpty, LegacyEmpty, LegacyEs]);
    }

    [Fact]
    public async Task CanFilterToMissingTranslationTextInEs()
    {
        var results = await Filter("Senses.ExampleSentences.Translations.Text[es]=");
        results.Should().BeEquivalentTo([CurrentEmpty, CurrentEn, LegacyEmpty, LegacyEn, LegacyPlain, LegacyNumeric]);
    }

    [Fact]
    public async Task CanFilterToTranslationTextInLegacyRows()
    {
        var results = await Filter("Senses.ExampleSentences.Translations.Text[en]=*legacy");
        results.Should().BeEquivalentTo([LegacyEn, LegacyPlain]);
        var numeric = await Filter("Senses.ExampleSentences.Translations.Text[en]=42");
        numeric.Should().BeEquivalentTo([LegacyNumeric]);
    }

    [Fact]
    public async Task CanFilterSentenceTextInLegacyPlainRows()
    {
        var results = await Filter("Senses.ExampleSentences.Sentence[en]=*plain sentence");
        results.Should().BeEquivalentTo([LegacyPlain]);
    }
}
