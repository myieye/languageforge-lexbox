using MiniLcm.Models;

namespace LcmDebugger.DemoProject;

public record DemoGenOptions
{
    public required string OutDir { get; init; }
    public int Seed { get; init; } = 42;
    public int TotalEntries { get; init; } = 10_000;
    public double LagFraction { get; init; } = 0.10;
    /// <summary>Each round adds two net-zero update commits per synced entry (edit away, edit back).</summary>
    public int UpdateRounds { get; init; } = 1;
    public int SyncedLinks { get; init; } = 800;
    public int LaggingLinks { get; init; } = 700;
}

public record EntrySpec(Guid Id, int Index, bool Lagging, string LexemeFr, string? LexemePt, SenseSpec[] Senses);

public record SenseSpec(Guid Id, string GlossEn, string? DefinitionEn, int PosIndex, int[] SemDomIndexes, ExampleSpec[] Examples);

public record ExampleSpec(Guid Id, Guid TranslationId, string SentenceFr, string TranslationEn);

/// <summary>A complex-form component link. Component index &lt; complex-form index, so the graph is acyclic.</summary>
public record LinkSpec(int ComplexFormIndex, int ComponentIndex, Guid ComponentRowId, int CftIndex);

public class DemoSpec
{
    public required DemoGenOptions Options { get; init; }
    public required Guid ProjectId { get; init; }
    public required PartOfSpeech[] PartsOfSpeech { get; init; }
    public required SemanticDomain[] SemanticDomains { get; init; }
    public required EntrySpec[] Entries { get; init; }
    /// <summary>Links whose ends are both synced entries; these exist in the CRDT project too.</summary>
    public required LinkSpec[] SyncedLinks { get; init; }
    /// <summary>Links with at least one lagging end; these exist only in fwdata and get created during sync.</summary>
    public required LinkSpec[] LaggingLinks { get; init; }

    public IEnumerable<LinkSpec> AllLinks => SyncedLinks.Concat(LaggingLinks);

    private static readonly string[] Syllables =
        ["ba", "be", "bi", "bo", "bu", "da", "de", "di", "do", "du", "ka", "ke", "ki", "ko", "ku",
         "la", "le", "li", "lo", "lu", "ma", "me", "mi", "mo", "mu", "na", "ne", "ni", "no", "nu",
         "pa", "pe", "pi", "po", "pu", "ra", "re", "ri", "ro", "ru", "sa", "se", "si", "so", "su",
         "ta", "te", "ti", "to", "tu", "va", "ve", "vi", "vo", "vu", "za", "ze", "zi", "zo", "zu"];

    public static DemoSpec Build(DemoGenOptions opts)
    {
        var rng = new Random(opts.Seed);
        var usedWords = new HashSet<string>();

        Guid NextGuid()
        {
            var bytes = new byte[16];
            rng.NextBytes(bytes);
            return new Guid(bytes);
        }

        string Word(int minSyllables, int maxSyllables)
        {
            while (true)
            {
                var count = rng.Next(minSyllables, maxSyllables + 1);
                var word = string.Concat(Enumerable.Range(0, count).Select(_ => Syllables[rng.Next(Syllables.Length)]));
                if (usedWords.Add(word)) return word;
            }
        }

        string Sentence(int words)
        {
            // Sentences needn't be unique; don't burn the used-word set on them.
            return string.Join(" ", Enumerable.Range(0, words)
                .Select(_ => string.Concat(Enumerable.Range(0, rng.Next(1, 4)).Select(_ => Syllables[rng.Next(Syllables.Length)]))));
        }

        var partsOfSpeech = Enumerable.Range(0, 8)
            .Select(i => new PartOfSpeech { Id = NextGuid(), Name = { ["en"] = $"demo-pos-{i}-{Word(2, 2)}" } })
            .ToArray();

        var semanticDomains = Enumerable.Range(0, 30)
            .Select(i => new SemanticDomain
            {
                Id = NextGuid(),
                Code = $"9.{i / 10}.{i % 10}",
                Name = { ["en"] = $"demo-domain-{Word(2, 3)}" },
            })
            .ToArray();

        var entries = new EntrySpec[opts.TotalEntries];
        var lagModulus = opts.LagFraction > 0 ? Math.Max(2, (int)Math.Round(1 / opts.LagFraction)) : int.MaxValue;
        for (var i = 0; i < opts.TotalEntries; i++)
        {
            // Spread lagging entries evenly instead of clustering them at the end.
            var lagging = lagModulus != int.MaxValue && i % lagModulus == lagModulus - 1;
            var senses = new SenseSpec[rng.Next(1, 4)];
            for (var s = 0; s < senses.Length; s++)
            {
                var examples = new ExampleSpec[rng.Next(0, 3)];
                for (var e = 0; e < examples.Length; e++)
                {
                    examples[e] = new ExampleSpec(NextGuid(), NextGuid(), Sentence(rng.Next(3, 8)), Sentence(rng.Next(3, 8)));
                }
                senses[s] = new SenseSpec(
                    NextGuid(),
                    GlossEn: Word(2, 4),
                    DefinitionEn: rng.Next(3) > 0 ? Sentence(rng.Next(4, 10)) : null,
                    PosIndex: rng.Next(5) > 0 ? rng.Next(partsOfSpeech.Length) : -1,
                    SemDomIndexes: [.. Enumerable.Range(0, rng.Next(0, 3)).Select(_ => rng.Next(semanticDomains.Length)).Distinct()],
                    Examples: examples);
            }
            entries[i] = new EntrySpec(
                NextGuid(),
                Index: i,
                Lagging: lagging,
                LexemeFr: Word(2, 5),
                LexemePt: rng.Next(3) == 0 ? Word(2, 5) : null,
                Senses: senses);
        }

        var syncedIndexes = entries.Where(e => !e.Lagging).Select(e => e.Index).ToArray();
        var laggingIndexes = entries.Where(e => e.Lagging).Select(e => e.Index).ToArray();
        var usedPairs = new HashSet<(int, int)>();
        // Complex-form entries seen so far; component picks are biased toward them so the
        // reference graph forms chains, giving the sync's cycle check real hops to walk.
        var complexFormIndexes = new List<int>();

        LinkSpec? MakeLink(int cfIndex, int componentIndex, int cftCount)
        {
            if (cfIndex == componentIndex) return null;
            if (componentIndex > cfIndex) (cfIndex, componentIndex) = (componentIndex, cfIndex);
            if (!usedPairs.Add((cfIndex, componentIndex))) return null;
            if (!complexFormIndexes.Contains(cfIndex)) complexFormIndexes.Add(cfIndex);
            return new LinkSpec(cfIndex, componentIndex, NextGuid(), rng.Next(cftCount));
        }

        int PickComponent(int[] pool, int belowIndex)
        {
            var chained = complexFormIndexes.Where(i => i < belowIndex).ToArray();
            if (chained.Length > 0 && rng.Next(10) < 3) return chained[rng.Next(chained.Length)];
            return pool[rng.Next(pool.Length)];
        }

        var syncedLinks = new List<LinkSpec>();
        while (syncedLinks.Count < opts.SyncedLinks && syncedIndexes.Length >= 2)
        {
            var cf = syncedIndexes[rng.Next(syncedIndexes.Length)];
            var link = MakeLink(cf, PickComponent(syncedIndexes, cf), cftCount: 7);
            // A chained component pick can land on a lagging complex form; those belong in the lagging set.
            if (link is null || entries[link.ComplexFormIndex].Lagging || entries[link.ComponentIndex].Lagging) continue;
            syncedLinks.Add(link);
        }

        var laggingLinks = new List<LinkSpec>();
        while (laggingLinks.Count < opts.LaggingLinks && laggingIndexes.Length >= 1 && syncedIndexes.Length >= 1)
        {
            var kind = laggingLinks.Count % 3;
            var (cf, component) = kind switch
            {
                0 => (laggingIndexes[rng.Next(laggingIndexes.Length)], PickComponent(syncedIndexes, int.MaxValue)),
                1 => (syncedIndexes[rng.Next(syncedIndexes.Length)], laggingIndexes[rng.Next(laggingIndexes.Length)]),
                _ => (laggingIndexes[rng.Next(laggingIndexes.Length)], laggingIndexes[rng.Next(laggingIndexes.Length)]),
            };
            var link = MakeLink(cf, component, cftCount: 7);
            if (link is null) continue;
            if (!entries[link.ComplexFormIndex].Lagging && !entries[link.ComponentIndex].Lagging) continue;
            laggingLinks.Add(link);
        }

        return new DemoSpec
        {
            Options = opts,
            ProjectId = NextGuid(),
            PartsOfSpeech = partsOfSpeech,
            SemanticDomains = semanticDomains,
            Entries = entries,
            SyncedLinks = [.. syncedLinks],
            LaggingLinks = [.. laggingLinks],
        };
    }
}
