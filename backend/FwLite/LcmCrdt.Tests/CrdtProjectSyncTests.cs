using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using LcmCrdt.RemoteSync;
using Refit;

namespace LcmCrdt.Tests;

public class CrdtProjectSyncTests
{
    private const string Authority = "lexbox.org";

    [Fact]
    public void ConnectionDroppedMidDownload_DoesNotBlameTheAppVersion()
    {
        // A dropped connection surfaces as a body-read failure part-way through deserializing, so it arrives
        // on the same Refit code path as a change we can't parse.
        var connectionAborted = new IOException("net_io_readfailure",
            new SocketException((int)SocketError.ConnectionAborted));

        CrdtProjectSync.DownloadFailureMessage(connectionAborted).Should().Be("Lost the connection while downloading dictionary changes.");
    }

    [Fact]
    public void UnreadableChange_PointsAtTheAppVersion()
    {
        CrdtProjectSync.DownloadFailureMessage(new JsonException("unknown change type"))
            .Should().Contain("out of date");
    }

    [Fact]
    public async Task ServerError_ReportsTheStatusCode()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Authority}/api/crdt/changes");
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError) { RequestMessage = request };
        var apiException = await ApiException.Create(request, HttpMethod.Post, response, new RefitSettings());

        CrdtProjectSync.DownloadFailureMessage(apiException).Should().Be("Failed to download dictionary changes, the server returned 500.");
    }
}
