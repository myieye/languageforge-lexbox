using FwHeadless;
using FwHeadless.Services;
using LcmCrdt;
using LcmCrdt.Project;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Testing.FwHeadless;

/// <summary>
/// When CrdtRebuildService refuses to start, and that it leaves the project's sync block as it found it.
/// </summary>
public class CrdtRebuildServiceTests : IDisposable
{
    private const string ProjectCode = "rebuild-test";
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly FwHeadlessConfig _config;
    private readonly ProjectMetadataService _metadataService;
    private readonly MergeQueueSpy _mergeService;
    private readonly SyncJobStatusService _syncJobStatusService = new();
    private readonly ServiceProvider _services;
    private readonly CrdtRebuildService _rebuildService;

    public CrdtRebuildServiceTests()
    {
        _config = new FwHeadlessConfig
        {
            ProjectStorageRoot = Directory.CreateTempSubdirectory(nameof(CrdtRebuildServiceTests)).FullName,
            LexboxUrl = "http://localhost/",
            LexboxUsername = "test",
            LexboxPassword = "test",
            MediaFileAuthority = "localhost"
        };
        var options = Options.Create(_config);
        _metadataService = new ProjectMetadataService(options, NullLogger<ProjectMetadataService>.Instance);
        _mergeService = new MergeQueueSpy(_metadataService);

        var projectLookup = new Mock<IProjectLookupService>();
        projectLookup.Setup(l => l.GetProjectCode(_projectId)).ReturnsAsync(ProjectCode);
        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(options)
            .AddSingleton(projectLookup.Object)
            .AddSingleton<ProjectDataCache>()
            .AddScoped<ProjectContextFromIdService>()
            .BuildServiceProvider();

        _rebuildService = new CrdtRebuildService(_services, null!, _mergeService, _syncJobStatusService, _metadataService,
            null!, null!, null!, null!, NullLogger<CrdtRebuildService>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        Directory.Delete(_config.ProjectStorageRoot, true);
    }

    private void CreateSyncedProject()
    {
        Directory.CreateDirectory(_config.GetProjectFolder(ProjectCode, _projectId));
        File.WriteAllText(_config.GetCrdtFile(ProjectCode, _projectId), "");
        File.WriteAllText(Path.Combine(_config.GetProjectFolder(ProjectCode, _projectId), "fw_snapshot.json"), "{}");
    }

    private async Task<bool> IsBlocked() => await _metadataService.GetSyncBlockedInfoAsync(_projectId) is { IsBlocked: true };

    [Fact]
    public async Task AQueuedMerge_IsLookedForOnlyOnceTheProjectIsBlocked_AndTheBlockIsLifted()
    {
        CreateSyncedProject();
        _mergeService.QueuedOrRunning = true;

        var (result, _, rebuild) = await _rebuildService.Start(_projectId, new());

        result.Should().Be(StartCrdtRebuild.ProjectBusy);
        rebuild.Should().BeNull();
        _mergeService.BlockedWhenAsked.Should().Equal([true], "a merge that starts after an earlier check would not see the block");
        (await IsBlocked()).Should().BeFalse();
        _rebuildService.IsRunning(_projectId).Should().BeFalse();
    }

    [Fact]
    public async Task ARunningSync_RefusesTheRebuildAndLiftsTheBlock()
    {
        CreateSyncedProject();
        _syncJobStatusService.StartSyncing(_projectId);

        var (result, _, _) = await _rebuildService.Start(_projectId, new());

        result.Should().Be(StartCrdtRebuild.ProjectBusy);
        (await IsBlocked()).Should().BeFalse();
    }

    [Fact]
    public async Task ABlockedProject_IsRefusedAndStaysBlockedForItsOwnReason()
    {
        CreateSyncedProject();
        await _metadataService.BlockFromSyncAsync(_projectId, "Rollback detected");

        var (result, problem, _) = await _rebuildService.Start(_projectId, new());

        result.Should().Be(StartCrdtRebuild.ProjectBusy);
        problem.Should().Contain("Rollback detected");
        (await _metadataService.GetSyncBlockedInfoAsync(_projectId))!.Reason.Should().Be("Rollback detected");
        _mergeService.BlockedWhenAsked.Should().BeEmpty();
    }

    [Fact]
    public async Task AProjectWithoutAFinishedMerge_IsNotFoundAndNotBlocked()
    {
        Directory.CreateDirectory(_config.GetProjectFolder(ProjectCode, _projectId));

        var (result, _, _) = await _rebuildService.Start(_projectId, new());

        result.Should().Be(StartCrdtRebuild.ProjectNotFound);
        (await IsBlocked()).Should().BeFalse();
        _rebuildService.IsRunning(_projectId).Should().BeFalse();
    }

    private class MergeQueueSpy(IProjectMetadataService metadataService) : SyncHostedService(null!, NullLogger<SyncHostedService>.Instance, null!)
    {
        public bool QueuedOrRunning { get; set; }
        public List<bool> BlockedWhenAsked { get; } = [];

        public override bool IsJobQueuedOrRunning(Guid projectId)
        {
            BlockedWhenAsked.Add(metadataService.GetSyncBlockedInfoAsync(projectId).GetAwaiter().GetResult() is { IsBlocked: true });
            return QueuedOrRunning;
        }
    }
}
