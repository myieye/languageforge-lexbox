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

    // The database is shared with the other RequiresDb tests, so each test counts inside its own far-off year.
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
        var result = await _controller.Projects(Start(year), End(year));
        return result.Value ?? throw new InvalidOperationException($"unexpected result {result.Result}");
    }

    [Fact]
    public async Task ActiveProjectsCountCommitsFromStartInclusiveToEndExclusive()
    {
        const int year = 2041;
        var atStart = await CreateProject();
        await SeedCommit(atStart.Id, Utc(Start(year)));
        var atEnd = await CreateProject();
        await SeedCommit(atEnd.Id, Utc(End(year)));
        var justBeforeEnd = await CreateProject();
        await SeedCommit(justBeforeEnd.Id, Utc(End(year)).AddTicks(-1));
        var beforeStart = await CreateProject();
        await SeedCommit(beforeStart.Id, Utc(Start(year)).AddTicks(-1));

        var metrics = await Query(year);

        metrics.FwLite.ActiveProjects.Should().Be(2);
    }

    [Fact]
    public async Task ActiveProjectsIncludeDeletedProjectsAndCountEachProjectOnce()
    {
        const int year = 2042;
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
        const int year = 2043;
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

    [Fact]
    public async Task LanguagesImpactedExcludeTestDevAndTrainingProjects()
    {
        const int year = 2044;
        var policies = Enum.GetValues<RetentionPolicy>();
        foreach (var policy in policies)
        {
            var project = await CreateProject(policy);
            await SeedCommit(project.Id, Utc(Start(year)).AddDays(1));
        }

        var metrics = await Query(year);

        metrics.FwLite.ActiveProjects.Should().Be(policies.Length);
        // Unknown and Verified
        metrics.FwLite.LanguagesImpacted.Should().Be(2);
    }

    [Fact]
    public async Task CreatedProjectsCountCreatedDateInRangeIncludingDeleted()
    {
        const int year = 2045;
        await CreateProject(createdDate: Utc(Start(year)));
        await CreateProject(createdDate: Utc(End(year)).AddTicks(-1), deleted: true);
        await CreateProject(createdDate: Utc(End(year)));
        await CreateProject(createdDate: Utc(Start(year)).AddTicks(-1));

        var metrics = await Query(year);

        metrics.Lexbox.CreatedProjects.Should().Be(2);
        metrics.FwLite.ActiveProjects.Should().Be(0);
    }

    [Fact]
    public async Task RejectsARangeThatDoesNotStartBeforeItEnds()
    {
        var result = await _controller.Projects(Start(2046), Start(2046));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}
