using FwDataMiniLcmBridge;
using FwLiteProjectSync;
using LcmCrdt;
using LcmDebugger;
using LcmDebugger.DemoProject;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MiniLcm.Project;
using Moq;
using SIL.Harmony;

var command = args.FirstOrDefault() ?? "help";
string? Arg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

int IntArg(string name, int fallback)
{
    var raw = Arg(name);
    if (raw is null) return fallback;
    return int.TryParse(raw, out var value) ? value : throw new ArgumentException($"{name} expects an integer, got '{raw}'");
}

double DoubleArg(string name, double fallback)
{
    var raw = Arg(name);
    if (raw is null) return fallback;
    return double.TryParse(raw, out var value) ? value : throw new ArgumentException($"{name} expects a number, got '{raw}'");
}

var builder = Host.CreateApplicationBuilder();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
builder.Logging.AddFilter("LinqToDB", LogLevel.Warning);
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss.fff ";
    options.SingleLine = true;
});
builder.Services.AddFwDataBridge();
// The LCModel templates are copied next to the binary; IProjectLoader.NewProject needs them.
builder.Services.PostConfigure<FwDataBridgeConfig>(c => c.TemplatesFolder = Path.Combine(AppContext.BaseDirectory, "Templates"));

//*
// does not include FTS
builder.Services.AddLcmCrdtClientCore();
/*/
// does include FTS
builder.Services.AddLcmCrdtClient();
//*/

builder.Services.AddFwLiteProjectSync();
builder.Services.AddScoped((_services) => new Mock<IServerHttpClientProvider>().Object);

if (command == "generate")
{
    // Keeps generating a large commit history O(n): only the single-commit write path checks this
    // flag, which is why the generator writes through DataModel.AddChanges. Never set for `sync` —
    // baseline measurements must match what FwHeadless runs.
    builder.Services.Configure<CrdtConfig>(c => c.AlwaysValidateCommits = false);
}

var phaseTimer = new PhaseTimingCollector();
builder.Logging.AddProvider(phaseTimer);

using var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();
var services = scope.ServiceProvider;
var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("LcmDebugger");

switch (command)
{
    case "generate":
    {
        var options = new DemoGenOptions
        {
            OutDir = Path.GetFullPath(Arg("--out") ?? Path.Combine(Utils.GetDefaultDownloadsPath(), "slow-sync-demo")),
            Seed = IntArg("--seed", 42),
            TotalEntries = IntArg("--entries", 10000),
            LagFraction = DoubleArg("--lag", 0.10),
            UpdateRounds = IntArg("--update-rounds", 1),
            SyncedLinks = IntArg("--synced-links", 800),
            LaggingLinks = IntArg("--lagging-links", 700),
        };
        await DemoProjectGenerator.Generate(services, options, logger);
        break;
    }
    case "sync":
    {
        var path = args.ElementAtOrDefault(1) ?? throw new InvalidOperationException("usage: sync <project-folder> [--real] [--in-place] [--downloads-root DIR]");
        var downloadsRoot = Arg("--downloads-root");
        if (Path.IsPathRooted(path))
        {
            downloadsRoot = Path.GetDirectoryName(path);
            path = Path.GetFileName(path);
        }
        await SyncHarness.Run(services,
            path,
            phaseTimer,
            logger,
            dryRun: !args.Contains("--real"),
            openCopy: !args.Contains("--in-place"),
            downloadsRoot: downloadsRoot);
        break;
    }
    case "print-entries":
    {
        await services.PrintAllEntries(args.ElementAtOrDefault(1) ?? "sena-3");
        break;
    }
    default:
    {
        Console.WriteLine("""
            usage:
              generate [--out DIR] [--seed N] [--entries N] [--lag F] [--update-rounds N] [--synced-links N] [--lagging-links N]
                  Generate the slow-sync demo project (fwdata + crdt.sqlite + fw_snapshot.json).
                  Default output: deployment/_downloads/slow-sync-demo
              sync <project-folder> [--real] [--in-place] [--downloads-root DIR]
                  Run a FwHeadless-style sync with per-phase timing. Dry run on a temp copy by
                  default; the folder is resolved under deployment/_downloads unless absolute.
              print-entries <code>
            """);
        break;
    }
}
