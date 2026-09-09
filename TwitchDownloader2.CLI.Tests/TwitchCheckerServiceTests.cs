using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class TwitchCheckerServiceTests
{
    [Fact]
    public async Task CheckNowAsync_DelegatesSchedulingAndUsesDownloaderAsSingleSourceOfState()
    {
        var settings = new AppSettings { TrackedChannels = ["alpha", "ALPHA", "beta"] };
        var downloader = new FakeDownloader
        {
            Active =
            [
                new ActiveDownloadInfo(
                    "session-beta",
                    "beta",
                    DateTimeOffset.UtcNow,
                    "/downloads/beta.recording",
                    ActiveDownloadState.Finalizing)
            ]
        };
        using var checker = new TwitchCheckerService(settings, downloader);

        await checker.CheckNowAsync(CancellationToken.None);
        var statuses = checker.GetStatuses();

        Assert.Equal(["alpha", "beta"], downloader.StartedChannels.OrderBy(channel => channel));
        Assert.False(statuses["alpha"]);
        Assert.True(statuses["beta"]);
    }

    private sealed class FakeDownloader : ITwitchDownloaderService
    {
        public List<string> StartedChannels { get; } = new();
        public IReadOnlyList<ActiveDownloadInfo> Active { get; init; } = [];

        public Task<StartDownloadResult> TryStartDownloadAsync(string channel, CancellationToken cancellationToken)
        {
            lock (StartedChannels)
                StartedChannels.Add(channel);
            return Task.FromResult(StartDownloadResult.Offline);
        }

        public Task<StopDownloadResult> RequestStopAsync(string sessionId, CancellationToken cancellationToken)
            => Task.FromResult(StopDownloadResult.NotFound);

        public IReadOnlyList<ActiveDownloadInfo> GetActiveDownloads() => Active;

        public Task StopForShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
