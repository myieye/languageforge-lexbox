using System.Net.Sockets;
using System.Text.Json;
using FwLiteShared.Projects;
using LcmCrdt.RemoteSync;

namespace FwLiteShared.Tests.Projects;

public class DownloadRetryTests
{
    [Fact]
    public void ADroppedConnectionIsWorthRetrying()
    {
        // How a dropped wifi connection arrives: the socket abort is two levels down, under the IOException
        // from the aborted body read and the CrdtSyncException the sync layer wraps it in.
        var droppedMidDownload = new CrdtSyncException("Lost the connection.",
            CrdtSyncException.CrdtSyncStep.Download,
            new IOException("net_io_readfailure", new SocketException((int)SocketError.ConnectionAborted)));

        CombinedProjectsService.IsConnectionFailure(droppedMidDownload).Should().BeTrue();
    }

    [Fact]
    public void ALocalWriteFailureIsNotWorthRetrying()
    {
        // Harmony writes downloaded commits to sqlite, so an IOException here isn't necessarily the network.
        CombinedProjectsService.IsConnectionFailure(new IOException("disk full")).Should().BeFalse();
    }

    [Fact]
    public void AChangeWeCannotReadIsNotWorthRetrying()
    {
        var tooNewForThisVersion = new CrdtSyncException("Out of date.",
            CrdtSyncException.CrdtSyncStep.Download,
            new JsonException("unknown change type"));

        CombinedProjectsService.IsConnectionFailure(tooNewForThisVersion).Should().BeFalse();
    }
}
