using System.Diagnostics;
using FwLiteProjectSync;
using LcmCrdt;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LcmDebugger;

/// <summary>
/// Runs a headless FwHeadless-style sync on a downloaded/demo project and reports per-phase
/// timing so optimizations have a before/after number. Phase boundaries come from the sync
/// service's own "Syncing ..." log messages, captured by <see cref="PhaseTimingCollector"/> —
/// the production sync code needs no instrumentation beyond its normal logging.
/// </summary>
public static class SyncHarness
{
    public static async Task Run(IServiceProvider services,
        string relativePath,
        PhaseTimingCollector timer,
        ILogger logger,
        bool dryRun = true,
        bool openCopy = true,
        string? downloadsRoot = null)
    {
        var total = Stopwatch.StartNew();
        var sw = Stopwatch.StartNew();
        using var project = await services.OpenDownloadedProject(relativePath, openCopy, downloadsRoot);
        var openElapsed = sw.Elapsed;
        var currentProjectService = services.GetRequiredService<CurrentProjectService>();
        await currentProjectService.UpdateUserRole(UserProjectRole.Editor);

        var syncService = services.GetRequiredService<CrdtFwdataProjectSyncService>();
        var snapshotService = services.GetRequiredService<ProjectSnapshotService>();
        sw.Restart();
        var projectSnapshot = await snapshotService.GetProjectSnapshot(project.FwApi.Project);
        var snapshotElapsed = sw.Elapsed;

        timer.Start();
        sw.Restart();
        var result = projectSnapshot is null
            ? await syncService.Import(project.CrdtApi, project.FwApi, dryRun)
            : await syncService.Sync(project.CrdtApi, project.FwApi, projectSnapshot, dryRun);
        var syncElapsed = sw.Elapsed;
        timer.Stop();

        if (!dryRun)
        {
            sw.Restart();
            await snapshotService.RegenerateProjectSnapshot(project.CrdtApi, project.FwApi.Project, keepBackup: false);
            logger.LogInformation("Regenerated snapshot in {Elapsed}", sw.Elapsed);
        }

        logger.LogInformation("=== Sync run summary ===");
        logger.LogInformation("Open project: {Open}; load snapshot: {Snapshot}; sync ({Mode}): {Sync}; total {Total}",
            openElapsed, snapshotElapsed, dryRun ? "dry run" : "real", syncElapsed, total.Elapsed);
        foreach (var phase in timer.Phases)
        {
            logger.LogInformation("Phase {Duration,12:g}  {Name}", phase.Duration, phase.Name);
        }
        logger.LogInformation("Result: CrdtChanges {CrdtChanges}, FwdataChanges {FwdataChanges}", result.CrdtChanges, result.FwdataChanges);

        if (result is CrdtFwdataProjectSyncService.DryRunSyncResult dryRunResult)
        {
            LogRecordSummary(logger, "crdt", dryRunResult.CrdtDryRunRecords, syncElapsed);
            LogRecordSummary(logger, "fwdata", dryRunResult.FwDataDryRunRecords, syncElapsed);
        }
    }

    private static void LogRecordSummary(ILogger logger,
        string side,
        List<RecordingMiniLcmApi.RunRecord> records,
        TimeSpan syncElapsed)
    {
        logger.LogInformation("{Side} dry-run records: {Count} total, avg {AvgMs:F0}ms/record over the whole sync",
            side, records.Count, records.Count == 0 ? 0 : syncElapsed.TotalMilliseconds / records.Count);
        foreach (var group in records.GroupBy(r => r.Method).OrderByDescending(g => g.Count()))
        {
            logger.LogInformation("  {Side} {Method}: {Count}", side, group.Key, group.Count());
        }
    }
}

/// <summary>
/// Captures the sync service's phase-marker log messages ("Syncing writing systems", …) and turns
/// the gaps between them into phase durations. Register with <c>builder.Logging.AddProvider</c>.
/// </summary>
public class PhaseTimingCollector : ILoggerProvider
{
    public record Phase(string Name, TimeSpan Duration);

    private readonly Stopwatch _stopwatch = new();
    private readonly List<(string Name, TimeSpan Start)> _marks = [];
    private bool _running;

    public void Start()
    {
        _running = true;
        _stopwatch.Restart();
    }

    public void Stop()
    {
        _running = false;
        _stopwatch.Stop();
    }

    public IReadOnlyList<Phase> Phases
    {
        get
        {
            var phases = new List<Phase>();
            for (var i = 0; i < _marks.Count; i++)
            {
                var end = i + 1 < _marks.Count ? _marks[i + 1].Start : _stopwatch.Elapsed;
                phases.Add(new Phase(_marks[i].Name, end - _marks[i].Start));
            }
            return phases;
        }
    }

    public ILogger CreateLogger(string categoryName) => new PhaseLogger(this);

    public void Dispose() { }

    private void OnMessage(string message)
    {
        if (!_running) return;
        if (message.StartsWith("Syncing "))
        {
            lock (_marks) _marks.Add((message, _stopwatch.Elapsed));
        }
    }

    private class PhaseLogger(PhaseTimingCollector collector) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            collector.OnMessage(formatter(state, exception));
        }
    }
}
