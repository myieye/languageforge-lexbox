using LexBoxApi.Controllers;
using LexCore.Entities;
using LexData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SIL.Harmony.Core;
using Testing.Fixtures;

namespace Testing.LexBoxApi.Controllers;

[Collection(nameof(TestingServicesFixture))]
[Trait("Category", "RequiresDb")]
public class MetricsControllerTests
{
    private readonly LexBoxDbContext _dbContext;
    private readonly MetricsController _controller;

    public MetricsControllerTests(TestingServicesFixture testing)
    {
        _dbContext = testing.ConfigureServices().GetRequiredService<LexBoxDbContext>();
        _controller = new MetricsController(_dbContext);
    }

    // The database is shared with the other RequiresDb tests, so each test counts inside a far-off year of its own.
    private static int _lastYear = 2040;
    private static int NextYear() => Interlocked.Increment(ref _lastYear);
    private static DateOnly Start(int year) => new(year, 1, 1);
    private static DateOnly End(int year) => new(year, 4, 1);
    private static DateTimeOffset Utc(DateOnly date) => new(date, TimeOnly.MinValue, TimeSpan.Zero);

    private async Task<Project> CreateProject(RetentionPolicy retentionPolicy = RetentionPolicy.Verified,
        DateTimeOffset? createdDate = null,
        bool deleted = false)
    {
        var project = new Project
        {
            Id = Guid.NewGuid(),
            Code = "metrics-test-" + Guid.NewGuid().ToString("N")[..8],
            Name = "Metrics Test",
            Type = ProjectType.FLEx,
            RetentionPolicy = retentionPolicy,
            IsConfidential = null,
            Users = [],
            Organizations = [],
            LastCommit = null,
            DeletedDate = deleted ? DateTimeOffset.UtcNow : null,
        };
        if (createdDate is not null) project.CreatedDate = createdDate.Value;
        _dbContext.Add(project);
        await _dbContext.SaveChangesAsync();
        return project;
    }

    private async Task SeedCommit(Guid projectId, DateTimeOffset dateTime)
    {
        _dbContext.Add(new ServerCommit(Guid.NewGuid())
        {
            ClientId = Guid.NewGuid(),
            HybridDateTime = new HybridDateTime(dateTime, 0),
            ProjectId = projectId,
            Metadata = new CommitMetadata(),
        });
        await _dbContext.SaveChangesAsync();
    }

    private async Task<MetricsController.ProjectMetrics> Query(int year)
    {
        var result = await _controller.Projects(Start(year), End(year), CancellationToken.None);
        return result.Value ?? throw new InvalidOperationException($"unexpected result {result.Result}");
    }

    [Fact]
    public async Task ACommitAtTheStartCountsAndMakesTheProjectNew()
    {
        var year = NextYear();
        var atStart = await CreateProject();
        await SeedCommit(atStart.Id, Utc(Start(year)));
        var justBeforeStart = await CreateProject();
        await SeedCommit(justBeforeStart.Id, Utc(Start(year)).AddTicks(-1));

        var metrics = await Query(year);

        metrics.FwLite.ActiveProjects.Should().Be(1);
        metrics.FwLite.NewProjects.Should().Be(1);
    }

    [Fact]
    public async Task ACommitAtTheEndDoesNotCount()
    {
        var year = NextYear();
        var atEnd = await CreateProject();
        await SeedCommit(atEnd.Id, Utc(End(year)));
        var justBeforeEnd = await CreateProject();
        await SeedCommit(justBeforeEnd.Id, Utc(End(year)).AddTicks(-1));

        var metrics = await Query(year);

        metrics.FwLite.ActiveProjects.Should().Be(1);
    }

    [Fact]
    public async Task ActiveProjectsIncludeDeletedProjectsAndCountEachProjectOnce()
    {
        var year = NextYear();
        var deleted = await CreateProject(deleted: true);
        await SeedCommit(deleted.Id, Utc(Start(year)).AddDays(1));
        await SeedCommit(deleted.Id, Utc(Start(year)).AddDays(2));

        var metrics = await Query(year);

        metrics.FwLite.ActiveProjects.Should().Be(1);
        metrics.FwLite.NewProjects.Should().Be(1);
    }

    [Fact]
    public async Task NewProjectsAreThoseWithoutACommitBeforeTheRange()
    {
        var year = NextYear();
        var existing = await CreateProject();
        await SeedCommit(existing.Id, Utc(Start(year)).AddDays(-1));
        await SeedCommit(existing.Id, Utc(Start(year)).AddDays(1));
        var fresh = await CreateProject();
        await SeedCommit(fresh.Id, Utc(Start(year)).AddDays(1));
        var laterOnly = await CreateProject();
        await SeedCommit(laterOnly.Id, Utc(End(year)).AddDays(1));

        var metrics = await Query(year);

        metrics.FwLite.ActiveProjects.Should().Be(2);
        metrics.FwLite.NewProjects.Should().Be(1);
    }

    [Theory]
    [InlineData(RetentionPolicy.Unknown, 1)]
    [InlineData(RetentionPolicy.Verified, 1)]
    [InlineData(RetentionPolicy.Test, 0)]
    [InlineData(RetentionPolicy.Dev, 0)]
    [InlineData(RetentionPolicy.Training, 0)]
    public async Task LanguagesImpactedLeaveOutTestDevAndTrainingProjects(RetentionPolicy policy, int languagesImpacted)
    {
        var year = NextYear();
        var project = await CreateProject(policy);
        await SeedCommit(project.Id, Utc(Start(year)).AddDays(1));

        var metrics = await Query(year);

        metrics.FwLite.ActiveProjects.Should().Be(1);
        metrics.FwLite.LanguagesImpacted.Should().Be(languagesImpacted);
    }

    [Fact]
    public async Task CreatedProjectsCountCreatedDateInRangeIncludingDeleted()
    {
        var year = NextYear();
        await CreateProject(createdDate: Utc(Start(year)));
        await CreateProject(createdDate: Utc(End(year)).AddTicks(-1), deleted: true);
        await CreateProject(createdDate: Utc(End(year)));
        await CreateProject(createdDate: Utc(Start(year)).AddTicks(-1));

        var metrics = await Query(year);

        metrics.Lexbox.CreatedProjects.Should().Be(2);
        metrics.FwLite.ActiveProjects.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task RejectsARangeThatDoesNotStartBeforeItEnds(int endOffsetDays)
    {
        var start = Start(NextYear());

        var result = await _controller.Projects(start, start.AddDays(endOffsetDays), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
