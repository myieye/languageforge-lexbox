using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Xunit.Abstractions;

namespace MiniLcm.Tests;

public class SerializationTests(ITestOutputHelper output)
{
    private const string ComponentJson = """
        "ComplexFormEntryId":"82cf7254-3a4f-497a-9e15-7d93fc830356","ComponentEntryId":"e5dbeb7f-4df4-4fc7-82c2-a1e14f49365a"
        """;

    [Fact]
    public void ComplexFormComponent_LegacyStringHeadwordsReadAsDefaultWritingSystem()
    {
        var component = JsonSerializer.Deserialize<ComplexFormComponent>(
            $$"""{{{ComponentJson}},"ComplexFormHeadword":"Pineapple","ComponentHeadword":"Pine apple"}""");

        component.Should().NotBeNull();
        component.ComplexFormHeadword.Values.Should().BeEquivalentTo(new Dictionary<WritingSystemId, string> { [WritingSystemId.Default] = "Pineapple" });
        component.ComponentHeadword.Values.Should().BeEquivalentTo(new Dictionary<WritingSystemId, string> { [WritingSystemId.Default] = "Pine apple" });
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\"(Unknown)\"")]
    [InlineData("{}")]
    public void ComplexFormComponent_EmptyLegacyHeadwordsReadAsEmpty(string headwordJson)
    {
        var component = JsonSerializer.Deserialize<ComplexFormComponent>(
            $$"""{{{ComponentJson}},"ComplexFormHeadword":{{headwordJson}},"ComponentHeadword":{{headwordJson}}}""");

        component.Should().NotBeNull();
        component.ComplexFormHeadword.Values.Should().BeEmpty();
        component.ComponentHeadword.Values.Should().BeEmpty();
    }

    [Fact]
    public void ComplexFormComponent_NullHeadwordWritesEmptyObject()
    {
        var component = new ComplexFormComponent
        {
            Id = Guid.NewGuid(),
            ComplexFormEntryId = Guid.NewGuid(),
            ComponentEntryId = Guid.NewGuid(),
            ComplexFormHeadword = null!,
        };

        JsonSerializer.Serialize(component).Should().Contain("\"ComplexFormHeadword\":{}");
    }

    [Fact]
    public void ComplexFormComponent_MultiStringHeadwordsRoundTrip()
    {
        var component = new ComplexFormComponent
        {
            Id = Guid.NewGuid(),
            ComplexFormEntryId = Guid.NewGuid(),
            ComplexFormHeadword = { { "en", "bank" }, { "fr", "banque" } },
            ComplexFormHomographNumber = 2,
            ComponentEntryId = Guid.NewGuid(),
            ComponentHeadword = { { "en", "-s" } },
        };

        var json = JsonSerializer.Serialize(component);
        json.Should().Contain("""
            "ComplexFormHeadword":{"en":"bank","fr":"banque"}
            """);
        var roundTripped = JsonSerializer.Deserialize<ComplexFormComponent>(json);

        roundTripped.Should().BeEquivalentTo(component);
    }

    [Fact]
    public void CanSerializeEntry()
    {
        var entryId = Guid.NewGuid();
        var entry = new Entry()
        {
            Id = entryId,
            LexemeForm = { { "en", "test" } },
            CitationForm = { { "en", "test" } },
            Senses =
            [
                new Sense
                {
                    Id = Guid.NewGuid(),
                    EntryId = entryId,
                    Gloss = { { "en", "test" } }
                }
            ]
        };
        var act = () => JsonSerializer.Serialize(entry);
        var json = act.Should().NotThrow().Subject;
        output.WriteLine(json);
    }

    [Fact]
    public void CanDeserializeEntry()
    {
        var entryId = Guid.NewGuid();
        var senseId = Guid.NewGuid();
        var entry = new Entry()
        {
            Id = entryId,
            LexemeForm = { { "en", "test" } },
            CitationForm = { { "en", "test" } },
            Senses =
            [
                new Sense
                {
                    Id = senseId,
                    EntryId = entryId,
                    Gloss = { { "en", "test" } },
                    ExampleSentences =
                    {
                        new ExampleSentence()
                        {
                            Id = Guid.NewGuid(),
                            SenseId = senseId,
                            Sentence = { { "en", new RichString("this is only a test", "en") } }
                        }
                    }
                }
            ]
        };
        var json = JsonSerializer.Serialize(entry);
        var act = () => JsonSerializer.Deserialize<Entry>(json);
        var fromJson = act.Should().NotThrow().Subject;
        fromJson.Should().BeEquivalentTo(entry);
    }


    [Fact]
    public void EqualityTest()
    {
        var entryId = Guid.NewGuid();
        var senseId = Guid.NewGuid();
        var entry = new Entry()
        {
            Id = entryId,
            LexemeForm = { { "en", "test" } },
            CitationForm = { { "en", "test" } },
            Senses =
            [
                new Sense
                {
                    Id = senseId,
                    EntryId = entryId,
                    Gloss = { { "en", "test" } }
                }
            ]
        };
        var entryCopy = new Entry()
        {
            Id = entryId,
            LexemeForm = { { "en", "test" } },
            CitationForm = { { "en", "test" } },
            Senses =
            [
                new Sense
                {
                    Id = senseId,
                    EntryId = entryId,
                    Gloss = { { "en", "test" } }
                }
            ]
        };
        entry.Should().BeEquivalentTo(entryCopy);
    }

    [Fact]
    public void ApiModifiersIgnoreInternalPropertiesInRequestBodies()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver().AddMiniLcmApiModifiers()
        };

        var sense = JsonSerializer.Deserialize<Sense>("""{"id":"5b4e3c2a-1d0f-4e9a-8b7c-6d5e4f3a2b1c","order":5}""", options);
        sense!.Order.Should().Be(0);
        // WritingSystem.Id is required, but an API caller can't send it
        var writingSystem = JsonSerializer.Deserialize<WritingSystem>("""{"wsId":"en","name":"English","abbreviation":"en","font":"Arial","type":0}""", options);
        writingSystem!.WsId.Should().Be(new WritingSystemId("en"));
    }
}
