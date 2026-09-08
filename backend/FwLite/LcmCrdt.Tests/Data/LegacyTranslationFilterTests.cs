using Microsoft.EntityFrameworkCore;

namespace LcmCrdt.Tests.Data;

/// <summary>
/// Puts every shape the Translations column can hold (see <see cref="Json.TranslationsPlainText"/>) in one project
/// and checks the translation filters against them side by side.
/// </summary>
public class LegacyTranslationFilterTests : IAsyncLifetime
{
    private const string CurrentEmpty = "current empty";
    private const string CurrentEn = "current en";
    private const string LegacyEmpty = "legacy empty";
    private const string LegacyEn = "legacy en";
    private const string LegacyEs = "legacy es";
    private const string LegacyEmptyEn = "legacy empty en";
    private const string LegacyPlain = "legacy plain";

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
        await SetRawTranslations(await CreateEntryWithExample(LegacyEmptyEn, []), """{"en":{"Spans":[]}}""");
        //before rich text, both columns held a plain string per writing system
        var plain = await CreateEntryWithExample(LegacyPlain, []);
        await SetRawTranslations(plain, """{"en":"legacy plain translation"}""");
        await SetRawColumn(plain, "Sentence", """{"en":"legacy plain sentence"}""");
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

    private Task SetRawTranslations(Guid exampleSentenceId, string json)
    {
        return SetRawColumn(exampleSentenceId, "Translations", json);
    }

    private async Task SetRawColumn(Guid exampleSentenceId, string column, string json)
    {
        var rowsUpdated = await _fixture.DbContext.Database.ExecuteSqlRawAsync(
            //NOCASE because we don't want to depend on how EF cases the guid it stored
            $"UPDATE ExampleSentence SET {column} = {{0}} WHERE Id = {{1}} COLLATE NOCASE",
            json,
            exampleSentenceId.ToString());
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
    public async Task SeedRowsHoldBothJsonShapes()
    {
        var translationColumns = await _fixture.DbContext.Database
            .SqlQuery<string>($"SELECT Translations AS Value FROM ExampleSentence").ToArrayAsync();
        translationColumns.Should().HaveCount(7);
        translationColumns.Where(c => c.StartsWith('[')).Should().HaveCount(2);
        translationColumns.Where(c => c.StartsWith('{')).Should().HaveCount(5);
    }

    [Theory]
    [InlineData("Senses.ExampleSentences.Translations=null")]
    [InlineData("Senses.ExampleSentences.Translations=[]")]
    public async Task LegacyEmptyObjectCountsAsNoTranslations(string gridifyFilter)
    {
        var results = await Filter(gridifyFilter);
        results.Should().BeEquivalentTo([CurrentEmpty, LegacyEmpty]);
    }

    [Fact]
    public async Task LegacyObjectWithTextCountsAsHavingTranslations()
    {
        var results = await Filter("Senses.ExampleSentences.Translations!=null");
        results.Should().BeEquivalentTo([CurrentEn, LegacyEn, LegacyEs, LegacyEmptyEn, LegacyPlain]);
    }

    [Fact]
    public async Task CanFilterToMissingTranslationTextInEn()
    {
        var results = await Filter("Senses.ExampleSentences.Translations.Text[en]=");
        results.Should().BeEquivalentTo([CurrentEmpty, LegacyEmpty, LegacyEs, LegacyEmptyEn]);
    }

    [Fact]
    public async Task CanFilterToMissingTranslationTextInEs()
    {
        var results = await Filter("Senses.ExampleSentences.Translations.Text[es]=");
        results.Should().BeEquivalentTo([CurrentEmpty, CurrentEn, LegacyEmpty, LegacyEn, LegacyEmptyEn, LegacyPlain]);
    }

    [Fact]
    public async Task CanFilterToTranslationTextInLegacyRows()
    {
        var results = await Filter("Senses.ExampleSentences.Translations.Text[en]=*legacy");
        results.Should().BeEquivalentTo([LegacyEn, LegacyPlain]);
    }

    [Fact]
    public async Task CanFilterSentenceTextInLegacyPlainRows()
    {
        var results = await Filter("Senses.ExampleSentences.Sentence[en]=*plain sentence");
        results.Should().BeEquivalentTo([LegacyPlain]);
        var missing = await Filter("Senses.ExampleSentences.Sentence[en]=");
        missing.Should().BeEmpty();
    }
}
