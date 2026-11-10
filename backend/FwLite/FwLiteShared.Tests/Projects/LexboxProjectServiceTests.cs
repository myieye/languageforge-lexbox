using FwLiteShared.Auth;
using FwLiteShared.Events;
using FwLiteShared.Projects;
using LexCore.Entities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace FwLiteShared.Tests.Projects;

public class LexboxProjectServiceTests
{
    private static readonly LexboxServer Server = new(new Uri("https://example.test/"), "Test");
    private static readonly ListProjectsResult SomeProjects = new(
        [new FieldWorksLiteProject(Guid.NewGuid(), "code", "name", false, true, ProjectRole.Editor)], false);

    // A failure cached as "no projects" kept the list empty for minutes after the connection came back.
    [Fact]
    public async Task GetLexboxProjects_DoesNotCacheFailedFetch()
    {
        var service = new FakeFetchLexboxProjectService(null, SomeProjects);

        (await service.GetLexboxProjects(Server)).Should().BeNull();
        (await service.GetLexboxProjects(Server)).Should().Be(SomeProjects);
        service.FetchCount.Should().Be(2);
    }

    [Fact]
    public async Task GetLexboxProjects_CachesSuccessfulFetch()
    {
        var service = new FakeFetchLexboxProjectService(SomeProjects, null);

        (await service.GetLexboxProjects(Server)).Should().Be(SomeProjects);
        (await service.GetLexboxProjects(Server)).Should().Be(SomeProjects);
        service.FetchCount.Should().Be(1);
    }

    private class FakeFetchLexboxProjectService(params ListProjectsResult?[] results) : LexboxProjectService(
        new OAuthClientFactory(Mock.Of<IServiceProvider>(), Options, NullLogger<OAuthClientFactory>.Instance),
        NullLogger<LexboxProjectService>.Instance,
        Options,
        new MemoryCache(new MemoryCacheOptions()),
        new LexboxProjectChangeListener(Mock.Of<IServiceProvider>(), Options, EventBus, NullLogger<LexboxProjectChangeListener>.Instance),
        EventBus)
    {
        private static readonly IOptions<AuthConfig> Options =
            Microsoft.Extensions.Options.Options.Create(new AuthConfig { ClientId = "test", LexboxServers = [Server] });
        private static readonly GlobalEventBus EventBus = new(NullLogger<GlobalEventBus>.Instance);

        public int FetchCount { get; private set; }

        internal override Task<ListProjectsResult?> FetchLexboxProjects(LexboxServer server) =>
            Task.FromResult(results[FetchCount++]);
    }
}
