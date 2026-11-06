using LexBoxApi.Services;
using LexBoxApi.Services.Email;
using LexCore.Entities;
using LexCore.ServiceInterfaces;
using LexData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using FluentAssertions;
using LexCore.Config;
using Microsoft.Extensions.Options;
using Testing.Fixtures;

namespace Testing.LexCore.Services;

[Collection(nameof(TestingServicesFixture))]
[Trait("Category", "RequiresDb")]
public class ProjectServiceTest
{
    private readonly ProjectService _projectService;
    private readonly ProjectWritingSystems _writingSystems = new()
    {
        VernacularWss = new List<FLExWsId>()
        {
            new() { Tag = "en", IsActive = true, IsDefault = true },
            new() { Tag = "fr", IsActive = false, IsDefault = false }
        },
        AnalysisWss = new List<FLExWsId>()
        {
            new() { Tag = "en", IsActive = true, IsDefault = true },
            new() { Tag = "fr", IsActive = false, IsDefault = false }
        }
    };

    private readonly LexBoxDbContext _lexBoxDbContext;
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());

    public ProjectServiceTest(TestingServicesFixture testing)
    {
        var hgService = Mock.Of<IHgService>(service => service.GetProjectWritingSystems(It.IsAny<ProjectCode>(), It.IsAny<CancellationToken>()) == Task.FromResult(_writingSystems));
        var serviceProvider = testing.ConfigureServices();
        _lexBoxDbContext = serviceProvider.GetRequiredService<LexBoxDbContext>();
        _projectService = new ProjectService(
            _lexBoxDbContext,
            hgService,
            serviceProvider.GetRequiredService<IOptions<HgConfig>>(),
            _memoryCache,
            Mock.Of<IEmailService>(),
            null!
        );
    }

    [Fact]
    public async Task CanCreateProject()
    {
        var projectId = await _projectService.CreateProject(
            new(null, "TestProject", "Test", "test1", ProjectType.FLEx, RetentionPolicy.Test, false, null, null));
        projectId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task RetentionPolicyLookupIsCachedUntilInvalidated()
    {
        var projectId = await _projectService.CreateProject(
            new(null, "TestProject", "Test", "test-retention-cache", ProjectType.FLEx, RetentionPolicy.Test, false, null, null));
        (await _projectService.LookupProjectRetentionPolicy(projectId)).Should().Be(RetentionPolicy.Test);

        await _lexBoxDbContext.Projects.Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.RetentionPolicy, RetentionPolicy.Verified));
        (await _projectService.LookupProjectRetentionPolicy(projectId)).Should().Be(RetentionPolicy.Test);

        _projectService.InvalidateProjectRetentionPolicyCache(projectId);
        (await _projectService.LookupProjectRetentionPolicy(projectId)).Should().Be(RetentionPolicy.Verified);
    }

    [Fact]
    public async Task CreatingAProjectClearsACachedUnknownRetentionPolicy()
    {
        var projectId = Guid.NewGuid();
        (await _projectService.LookupProjectRetentionPolicy(projectId)).Should().Be(RetentionPolicy.Unknown);

        await _projectService.CreateProject(
            new(projectId, "TestProject", "Test", "test-retention-create", ProjectType.FLEx, RetentionPolicy.Test, false, null, null));

        (await _projectService.LookupProjectRetentionPolicy(projectId)).Should().Be(RetentionPolicy.Test);
    }

    [Fact]
    public async Task RetentionPolicyLookupIsUnknownForMissingProject()
    {
        (await _projectService.LookupProjectRetentionPolicy(Guid.NewGuid())).Should().Be(RetentionPolicy.Unknown);
    }

    [Fact]
    public async Task CanUpdateProjectLangTags()
    {
        var projectId = await _projectService.CreateProject(
            new(null, "TestProject", "Test", "test2", ProjectType.FLEx, RetentionPolicy.Test, false, null, null));
        await _projectService.UpdateProjectLangTags(projectId);
        var project = await _lexBoxDbContext.Projects.Include(p => p.FlexProjectMetadata).SingleAsync(p => p.Id == projectId);
        project.FlexProjectMetadata.Should().NotBeNull();
        project.FlexProjectMetadata.WritingSystems.Should().BeEquivalentTo(_writingSystems);
    }

    [Fact]
    public async Task ShouldErrorIfCreatingAProjectWithTheSameCode()
    {
        //first project should be created
        await _projectService.CreateProject(
            new(null, "TestProject", "Test", "test-dup-code", ProjectType.FLEx, RetentionPolicy.Test, false, null, null));

        var act = () => _projectService.CreateProject(
            new(null, "Test2", "Test desc", "test-dup-code", ProjectType.Unknown, RetentionPolicy.Dev, false, null, null)
        );

        (await act.Should().ThrowAsync<DbUpdateException>())
            .WithInnerException<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }
}
