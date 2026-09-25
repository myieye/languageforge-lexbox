using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using FwLiteShared.Projects;
using LexCore.Sync;
using Microsoft.Extensions.Logging.Abstractions;

namespace FwLiteShared.Tests.Projects;

public class LexboxSyncPollTests
{
    private static HttpClient Client(Queue<Func<HttpResponseMessage>> responses)
    {
        return new HttpClient(new QueueHandler(responses)) { BaseAddress = new Uri("https://lexbox.test/") };
    }

    private static Task<SyncJobResult> Poll(HttpClient client, int maxRetries = 3)
    {
        return LexboxProjectService.PollLexboxSyncFinished(client,
            Guid.NewGuid(),
            TimeSpan.FromSeconds(30),
            NullLogger.Instance,
            retryDelay: TimeSpan.Zero,
            maxConnectionRetries: maxRetries);
    }

    // The shape SocketsHttpHandler produces when an open connection is aborted mid-request
    private static Func<HttpResponseMessage> SocketDrop() =>
        () => throw new HttpRequestException(HttpRequestError.Unknown, "An error occurred while sending the request.",
            new IOException("Unable to read data from the transport connection", new SocketException((int)SocketError.ConnectionAborted)));

    private static Func<HttpResponseMessage> Respond(HttpStatusCode status, SyncJobResult? body = null) =>
        () => new HttpResponseMessage(status) { Content = body is null ? null : JsonContent.Create(body) };

    // The server holds the poll open until the job ends, so a running job shows up as our own request timeout
    private static Func<HttpResponseMessage> StillRunning() =>
        () => throw new TaskCanceledException();

    private static Func<HttpResponseMessage> Finished() =>
        Respond(HttpStatusCode.OK, new SyncJobResult(new SyncResult(1, 2)));

    [Fact]
    public async Task SocketDropIsRetriedAndRecoversTheResult()
    {
        var result = await Poll(Client(new([SocketDrop(), SocketDrop(), Finished()])));
        result.Status.Should().Be(SyncJobStatusEnum.Success);
    }

    [Fact]
    public async Task ExhaustedRetriesReportLostConnectionNotFailure()
    {
        var result = await Poll(Client(new([SocketDrop(), SocketDrop()])), maxRetries: 1);
        result.Status.Should().Be(SyncJobStatusEnum.LostConnectionAwaitingStatus);
    }

    [Fact]
    public async Task RetryBudgetResetsAfterTheServerAnswers()
    {
        var result = await Poll(Client(new([SocketDrop(), StillRunning(), SocketDrop(), Finished()])), maxRetries: 1);
        result.Status.Should().Be(SyncJobStatusEnum.Success);
    }

    [Fact]
    public async Task GatewayErrorsAreRetried()
    {
        var result = await Poll(Client(new([Respond(HttpStatusCode.BadGateway), Finished()])));
        result.Status.Should().Be(SyncJobStatusEnum.Success);
    }

    [Fact]
    public async Task ExpiredLoginIsNotReportedAsFailure()
    {
        var result = await Poll(Client(new([Respond(HttpStatusCode.Unauthorized)])));
        result.Status.Should().Be(SyncJobStatusEnum.LostConnectionAwaitingStatus);
    }

    private class QueueHandler(Queue<Func<HttpResponseMessage>> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responses.Dequeue()());
        }
    }
}
