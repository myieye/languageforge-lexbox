using System.Diagnostics;
using System.Text.Json;
using FwDataMiniLcmBridge;
using FwDataMiniLcmBridge.Api;
using FwDataMiniLcmBridge.LcmUtils;
using FwLiteProjectSync;
using LcmCrdt;
using LcmCrdt.Changes;
using LcmCrdt.Changes.Entries;
using LcmCrdt.Objects;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniLcm;
using MiniLcm.Models;
using MiniLcm.SyncHelpers;
using SIL.Harmony;
using SIL.Harmony.Changes;

namespace LcmDebugger.DemoProject;

/// <summary>
/// Generates a demo project (fwdata + crdt.sqlite + fw_snapshot.json) whose FwData↔CRDT sync is
/// deliberately slow at realistic scale: a CRDT with tens of thousands of single-change commits,
/// a snapshot lagging fwdata by ~10% of entries, and complex-form components among the lagging
/// entries (the most expensive creates on the Harmony write path).
///
/// The CRDT side is written through <see cref="DataModel.AddChanges(Guid, IEnumerable{IChange}, SIL.Harmony.Core.CommitMetadata?)"/>
/// (one commit per call) instead of CrdtMiniLcmApi.CreateEntry: the api's multi-change path
/// (DataModel.AddManyChanges) validates the full commit history unconditionally, which makes
/// generating a large history O(n²). The single-commit path honors AlwaysValidateCommits=false,
/// which Program.cs sets for the generate command only.
/// </summary>
public static class DemoProjectGenerator
{
    public static async Task Generate(IServiceProvider services, DemoGenOptions opts, ILogger logger)
    {
        if (Directory.Exists(opts.OutDir) && Directory.EnumerateFileSystemEntries(opts.OutDir).Any())
            throw new InvalidOperationException($"Output directory {opts.OutDir} already exists and is not empty.");
        Directory.CreateDirectory(opts.OutDir);

        var spec = DemoSpec.Build(opts);
        var laggingCount = spec.Entries.Count(e => e.Lagging);
        logger.LogInformation("Spec: {Total} entries ({Lagging} lagging), {SyncedLinks} synced links, {LaggingLinks} lagging links, seed {Seed}",
            spec.Entries.Length, laggingCount, spec.SyncedLinks.Length, spec.LaggingLinks.Length, opts.Seed);

        var total = Stopwatch.StartNew();
        var fwProject = new FwDataProject("fw", opts.OutDir);
        var fwDataFactory = services.GetRequiredService<FwDataFactory>();
        using var keepAlive = fwDataFactory.PreventEviction(fwProject);

        var sw = Stopwatch.StartNew();
        services.GetRequiredService<IProjectLoader>().NewProject(fwProject, "en", "fr")?.Dispose();
        var fwApi = fwDataFactory.GetFwDataMiniLcmApi(fwProject, false);
        logger.LogInformation("Created fwdata project in {Elapsed} (project id {ProjectId})", sw.Elapsed, fwApi.ProjectId);

        sw.Restart();
        await fwApi.CreateWritingSystem(new WritingSystem
        {
            Id = Guid.NewGuid(),
            WsId = "pt",
            Name = "Portuguese",
            Abbreviation = "pt",
            Font = "Charis SIL",
            Type = WritingSystemType.Vernacular,
        });
        foreach (var pos in spec.PartsOfSpeech) await fwApi.CreatePartOfSpeech(pos);
        foreach (var domain in spec.SemanticDomains) await fwApi.CreateSemanticDomain(domain);
        // Read the lists back so the CRDT mirror includes the template's built-ins
        // (5 parts of speech, the full 1792-domain standard semantic-domain list).
        var partsOfSpeech = await fwApi.GetPartsOfSpeech().ToArrayAsync();
        var semanticDomains = await fwApi.GetSemanticDomains().ToArrayAsync();
        var writingSystems = await fwApi.GetWritingSystems();
        var complexFormTypes = await fwApi.GetComplexFormTypes().ToArrayAsync();
        var publications = await fwApi.GetPublications().ToArrayAsync();
        logger.LogInformation("fwdata metadata done in {Elapsed}: {WsCount} writing systems, {CftCount} complex form types, {PubCount} publications",
            sw.Elapsed, writingSystems.Analysis.Length + writingSystems.Vernacular.Length, complexFormTypes.Length, publications.Length);
        if (complexFormTypes.Length == 0) throw new InvalidOperationException("fwdata template has no complex form types.");

        sw.Restart();
        foreach (var entrySpec in spec.Entries)
        {
            await fwApi.CreateEntry(BuildEntry(entrySpec, spec), CreateEntryOptions.AsIs);
            if ((entrySpec.Index + 1) % 500 == 0)
                logger.LogInformation("fwdata: created {Count}/{Total} entries ({Rate:F1}/s)",
                    entrySpec.Index + 1, spec.Entries.Length, (entrySpec.Index + 1) / sw.Elapsed.TotalSeconds);
        }
        logger.LogInformation("fwdata: created {Total} entries in {Elapsed}", spec.Entries.Length, sw.Elapsed);

        sw.Restart();
        // Group links per complex form, synced links first, so the CRDT (synced links only) and
        // fwdata component lists keep the same relative order — otherwise the sync sees moves.
        var linksByComplexForm = spec.AllLinks
            .GroupBy(l => l.ComplexFormIndex)
            .OrderBy(g => g.Key)
            .Select(g => (CfIndex: g.Key, Links: g.OrderBy(l => LinkIsLagging(l, spec) ? 1 : 0).ToArray()))
            .ToArray();
        foreach (var (cfIndex, links) in linksByComplexForm)
        {
            // Set the type before the components so liblcm doesn't inject the "unspecified" type.
            await fwApi.AddComplexFormType(spec.Entries[cfIndex].Id, complexFormTypes[links[0].CftIndex % complexFormTypes.Length].Id);
            foreach (var link in links)
            {
                await fwApi.CreateComplexFormComponent(ComplexFormComponent.FromEntries(
                    BuildEntry(spec.Entries[cfIndex], spec),
                    BuildEntry(spec.Entries[link.ComponentIndex], spec)));
            }
        }
        fwApi.Save();
        logger.LogInformation("fwdata: created {Count} complex-form links and saved in {Elapsed}", spec.AllLinks.Count(), sw.Elapsed);

        var crdtProjectsService = services.GetRequiredService<CrdtProjectsService>();
        var crdtProject = await crdtProjectsService.CreateProject(new CrdtProjectsService.CreateProjectRequest(
            "slow-sync-demo", "crdt", Id: spec.ProjectId, Path: opts.OutDir,
            FwProjectId: fwApi.ProjectId, Role: UserProjectRole.Editor));

        await using var scope = services.CreateAsyncScope();
        var scoped = scope.ServiceProvider;
        var crdtApi = (CrdtMiniLcmApi)await crdtProjectsService.OpenProject(crdtProject, scoped);
        var dataModel = scoped.GetRequiredService<DataModel>();
        var clientId = crdtApi.ProjectData.ClientId;
        var commitCount = 0;

        sw.Restart();
        foreach (var ws in writingSystems.Analysis.Concat(writingSystems.Vernacular))
        {
            await crdtApi.CreateWritingSystem(ws);
            commitCount++;
        }
        foreach (var pos in partsOfSpeech)
        {
            await crdtApi.CreatePartOfSpeech(pos);
            commitCount++;
        }
        foreach (var domain in semanticDomains)
        {
            await crdtApi.CreateSemanticDomain(domain);
            commitCount++;
        }
        foreach (var cft in complexFormTypes)
        {
            await crdtApi.CreateComplexFormType(cft);
            commitCount++;
        }
        foreach (var publication in publications)
        {
            await crdtApi.CreatePublication(publication);
            commitCount++;
        }
        var fwMorphTypes = await fwApi.GetMorphTypes().ToArrayAsync();
        var crdtMorphTypes = await crdtApi.GetMorphTypes().ToArrayAsync();
        if (fwMorphTypes.Length != crdtMorphTypes.Length)
            logger.LogWarning("Morph type count mismatch: fwdata {FwCount}, crdt {CrdtCount} — sync will churn on these",
                fwMorphTypes.Length, crdtMorphTypes.Length);
        logger.LogInformation("crdt: mirrored metadata in {Elapsed}", sw.Elapsed);

        sw.Restart();
        var syncedEntries = spec.Entries.Where(e => !e.Lagging).ToArray();
        var created = 0;
        foreach (var entrySpec in syncedEntries)
        {
            await dataModel.AddChanges(clientId, BuildEntryChanges(entrySpec, spec));
            commitCount++;
            if (++created % 500 == 0)
                logger.LogInformation("crdt: created {Count}/{Total} entries ({Rate:F1}/s)",
                    created, syncedEntries.Length, created / sw.Elapsed.TotalSeconds);
        }
        logger.LogInformation("crdt: created {Total} entries in {Elapsed}", created, sw.Elapsed);

        sw.Restart();
        foreach (var (cfIndex, links) in linksByComplexForm)
        {
            if (spec.Entries[cfIndex].Lagging) continue;
            await crdtApi.AddComplexFormType(spec.Entries[cfIndex].Id, complexFormTypes[links[0].CftIndex % complexFormTypes.Length].Id);
            commitCount++;
            var order = 1;
            foreach (var link in links.Where(l => !LinkIsLagging(l, spec)))
            {
                await dataModel.AddChanges(clientId, [new AddEntryComponentChange(new ComplexFormComponent
                {
                    Id = link.ComponentRowId,
                    ComplexFormEntryId = spec.Entries[link.ComplexFormIndex].Id,
                    ComplexFormHeadword = spec.Entries[link.ComplexFormIndex].LexemeFr,
                    ComponentEntryId = spec.Entries[link.ComponentIndex].Id,
                    ComponentHeadword = spec.Entries[link.ComponentIndex].LexemeFr,
                    Order = order++,
                })]);
                commitCount++;
            }
        }
        logger.LogInformation("crdt: created {Count} synced complex-form links in {Elapsed}", spec.SyncedLinks.Length, sw.Elapsed);

        sw.Restart();
        // Net-zero edit pairs: bulk up the commit history (what makes ValidateCommits and the
        // snapshot CTE expensive) without making the CRDT state diverge from fwdata.
        for (var round = 0; round < opts.UpdateRounds; round++)
        {
            var updated = 0;
            foreach (var entrySpec in syncedEntries)
            {
                var original = new Entry { Id = entrySpec.Id, CitationForm = { ["fr"] = entrySpec.LexemeFr } };
                var edited = new Entry { Id = entrySpec.Id, CitationForm = { ["fr"] = $"{entrySpec.LexemeFr}-r{round}" } };
                var away = EntryDiffToUpdate(original, edited);
                var back = EntryDiffToUpdate(edited, original);
                await dataModel.AddChanges(clientId, away.Patch.ToChanges(entrySpec.Id));
                await dataModel.AddChanges(clientId, back.Patch.ToChanges(entrySpec.Id));
                commitCount += 2;
                if (++updated % 500 == 0)
                    logger.LogInformation("crdt: update round {Round}: {Count}/{Total} entries ({Rate:F1} commits/s)",
                        round + 1, updated, syncedEntries.Length, updated * 2 / sw.Elapsed.TotalSeconds);
            }
            sw.Restart();
        }
        logger.LogInformation("crdt: added {Count} update commits ({Rounds} rounds)", syncedEntries.Length * 2 * opts.UpdateRounds, opts.UpdateRounds);

        sw.Restart();
        var snapshotService = scoped.GetRequiredService<ProjectSnapshotService>();
        await snapshotService.RegenerateProjectSnapshot(crdtApi, fwProject, keepBackup: false);
        logger.LogInformation("Regenerated fw_snapshot.json in {Elapsed}", sw.Elapsed);

        var dbCommits = CountCommits(Path.Combine(opts.OutDir, "crdt.sqlite"));
        var manifest = new
        {
            GeneratedBy = "LcmDebugger generate",
            opts.Seed,
            opts.TotalEntries,
            opts.LagFraction,
            opts.UpdateRounds,
            LaggingEntries = laggingCount,
            SyncedLinks = spec.SyncedLinks.Length,
            LaggingLinks = spec.LaggingLinks.Length,
            Commits = dbCommits,
            FwProjectId = fwApi.ProjectId,
        };
        await File.WriteAllTextAsync(Path.Combine(opts.OutDir, "demo-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        logger.LogInformation("Done in {Elapsed}. {Commits} commits in crdt.sqlite (tracked {Tracked}). Output: {OutDir}",
            total.Elapsed, dbCommits, commitCount, opts.OutDir);
    }

    private static bool LinkIsLagging(LinkSpec link, DemoSpec spec) =>
        spec.Entries[link.ComplexFormIndex].Lagging || spec.Entries[link.ComponentIndex].Lagging;

    private static UpdateObjectInput<Entry> EntryDiffToUpdate(Entry before, Entry after) =>
        EntrySync.EntryDiffToUpdate(before, after) ?? throw new InvalidOperationException("expected a diff");

    /// <summary>Builds a fresh Entry per call: both apis mutate what they're given.</summary>
    public static Entry BuildEntry(EntrySpec spec, DemoSpec demoSpec)
    {
        var entry = new Entry
        {
            Id = spec.Id,
            LexemeForm = { ["fr"] = spec.LexemeFr },
            CitationForm = { ["fr"] = spec.LexemeFr },
        };
        if (spec.LexemePt is not null) entry.LexemeForm["pt"] = spec.LexemePt;
        foreach (var senseSpec in spec.Senses)
        {
            var sense = new Sense
            {
                Id = senseSpec.Id,
                EntryId = spec.Id,
                Gloss = { ["en"] = senseSpec.GlossEn },
                PartOfSpeechId = senseSpec.PosIndex >= 0 ? demoSpec.PartsOfSpeech[senseSpec.PosIndex].Id : null,
                SemanticDomains = [.. senseSpec.SemDomIndexes.Select(i => demoSpec.SemanticDomains[i])],
            };
            if (senseSpec.DefinitionEn is not null) sense.Definition["en"] = new RichString(senseSpec.DefinitionEn, "en");
            foreach (var exampleSpec in senseSpec.Examples)
            {
                sense.ExampleSentences.Add(new ExampleSentence
                {
                    Id = exampleSpec.Id,
                    SenseId = senseSpec.Id,
                    Sentence = { ["fr"] = new RichString(exampleSpec.SentenceFr, "fr") },
                    Translations = [new Translation { Id = exampleSpec.TranslationId, Text = { ["en"] = new RichString(exampleSpec.TranslationEn, "en") } }],
                });
            }
            entry.Senses.Add(sense);
        }
        return entry;
    }

    /// <summary>Mirrors CrdtMiniLcmApi.CreateEntryChanges so one entry lands as one commit.</summary>
    private static List<IChange> BuildEntryChanges(EntrySpec spec, DemoSpec demoSpec)
    {
        var entry = BuildEntry(spec, demoSpec);
        var changes = new List<IChange> { new CreateEntryChange(entry) };
        var senseOrder = 1;
        foreach (var sense in entry.Senses)
        {
            sense.Order = senseOrder++;
            changes.Add(new CreateSenseChange(sense, entry.Id));
            var exampleOrder = 1;
            foreach (var example in sense.ExampleSentences)
            {
                example.Order = exampleOrder++;
                changes.Add(new CreateExampleSentenceChange(example, sense.Id));
            }
        }
        return changes;
    }

    private static long CountCommits(string sqlitePath)
    {
        using var connection = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Commits";
        return (long)command.ExecuteScalar()!;
    }
}
