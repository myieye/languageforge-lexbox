using LexCore.Analytics;
using LexCore.Entities;
using LexCore.ServiceInterfaces;
using LexSyncReverseProxy;
using LexSyncReverseProxy.Services;
using Microsoft.AspNetCore.Http;
using Moq;

namespace Testing.SyncReverseProxy;

public class ProxyEventsServiceTests
{
    private const string ProjectCode = "proxy-events-test";
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Mock<ILexProxyService> _proxyService = new();
    private readonly Mock<ILexboxAnalyticsService> _analytics = new();
    private readonly ProxyEventsService _service;

    public ProxyEventsServiceTests()
    {
        _proxyService.Setup(s => s.LookupProjectId(ProjectCode)).ReturnsAsync(_projectId);
        _proxyService.Setup(s => s.LookupProjectRetentionPolicy(_projectId)).ReturnsAsync(RetentionPolicy.Training);
        _service = new ProxyEventsService(_proxyService.Object, _analytics.Object);
    }

    private static HttpContext HgRequest(string cmd, string projectCode = ProjectCode)
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString($"?cmd={cmd}");
        context.Request.RouteValues[ProxyConstants.HgProjectCodeRouteKey] = projectCode;
        return context;
    }

    private static HttpContext PushChunkRequest(int offset)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v03/pushBundleChunk";
        context.Request.QueryString = new QueryString($"?repoId={ProjectCode}&chunksize=10&offset={offset}&bundlesize=30");
        return context;
    }

    [Theory]
    [InlineData("unbundle", ILexboxAnalyticsService.SendDirection)]
    [InlineData("getbundle", ILexboxAnalyticsService.ReceiveDirection)]
    public async Task HgRequestTracksSendReceiveWithTheProjectsRetentionPolicy(string cmd, string direction)
    {
        await _service.OnHgRequest(HgRequest(cmd));

        _analytics.Verify(a => a.TrackSendReceive(_projectId, RetentionPolicy.Training, direction), Times.Once);
    }

    [Fact]
    public async Task HgRequestTracksNothingWhenTheProjectCodeDoesNotResolve()
    {
        _proxyService.Setup(s => s.LookupProjectId("no-such-project")).ReturnsAsync((Guid?)null);

        await _service.OnHgRequest(HgRequest("unbundle", "no-such-project"));

        _analytics.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task ResumablePushTracksNothingBeforeTheLastChunk(int offset)
    {
        await _service.OnResumableRequest(PushChunkRequest(offset));

        _analytics.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResumablePushTracksTheLastChunk()
    {
        await _service.OnResumableRequest(PushChunkRequest(20));

        _analytics.Verify(a => a.TrackSendReceive(_projectId, RetentionPolicy.Training, ILexboxAnalyticsService.SendDirection), Times.Once);
    }
}
