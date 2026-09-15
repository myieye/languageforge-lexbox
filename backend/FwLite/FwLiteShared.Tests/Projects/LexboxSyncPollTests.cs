using System.Net;
using System.Net.Http.Json;
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

    private static Func<HttpResponseMessage> Throws(HttpRequestError error) =>
        () => throw new HttpRequestException(error, "boom");

    private static Func<HttpResponseMessage> Finished() =>
        () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SyncJobResult(new SyncResult(1, 2)))
        };

    [Fact]
    public async Task ConnectionDropIsRetriedAndRecoversTheResult()
    {
        var client = Client(new([Throws(HttpRequestError.ConnectionError), Throws(HttpRequestError.ResponseEnded), Finished()]));
        var result = await Poll(client);
        result.Status.Should().Be(SyncJobStatusEnum.Success);
    }

    [Fact]
    public async Task ExhaustedRetriesReportLostConnectionNotFailure()
    {
        var client = Client(new([Throws(HttpRequestError.ConnectionError), Throws(HttpRequestError.ConnectionError)]));
        var result = await Poll(client, maxRetries: 1);
        result.Status.Should().Be(SyncJobStatusEnum.LostConnectionAwaitingStatus);
    }

    [Fact]
    public async Task PersistentErrorsAreNotRetried()
    {
        var client = Client(new([Throws(HttpRequestError.SecureConnectionError), Finished()]));
        var result = await Poll(client);
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
