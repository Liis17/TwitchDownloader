using Microsoft.Data.Sqlite;
using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class RecordingHistoryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"twitch-history-{Guid.NewGuid():N}");

    private string DatabasePath => Path.Combine(_directory, "Data", "history.db");

    [Fact]
    public void Store_PersistsEntriesAcrossReopenNewestFirst()
    {
        var started = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.FromHours(3));
        var store = new RecordingHistoryStore(DatabasePath);
        store.Add(CreateCompletion("old", "alpha", started.AddDays(-1), succeeded: true), started.AddDays(-1).AddHours(3));
        store.Add(
            CreateCompletion("new", "beta", started, succeeded: false, stoppedManually: true, error: "ffprobe"),
            started.AddHours(2));

        var reopened = new RecordingHistoryStore(DatabasePath);
        var entries = reopened.GetRecent(10);

        Assert.Equal(2, reopened.Count());
        Assert.Equal(["new", "old"], entries.Select(entry => entry.SessionId));
        var newest = entries[0];
        Assert.Equal("beta", newest.Channel);
        Assert.Equal(started, newest.StartedAt);
        Assert.Equal(started.AddMinutes(90), newest.RecordingEndedAt);
        Assert.Equal(started.AddHours(2), newest.CompletedAt);
        Assert.False(newest.Succeeded);
        Assert.True(newest.StoppedManually);
        Assert.Null(newest.OutputPath);
        Assert.Equal("ffprobe", newest.ErrorMessage);
        Assert.Equal(5400, newest.DurationSeconds);
        Assert.Equal(3, newest.AdvertisementCount);
        Assert.Equal(1, newest.GapCount);
        Assert.Equal("/downloads/old.mp4", entries[1].OutputPath);
        Assert.Single(reopened.GetRecent(1));
    }

    [Fact]
    public async Task Sink_SavesCompletionAndForwardsNotification()
    {
        var store = new RecordingHistoryStore(DatabasePath);
        var inner = new ForwardingProbe();
        var sink = new RecordingHistoryNotificationSink(store, inner);
        var completion = CreateCompletion("session", "alpha", DateTimeOffset.UtcNow, succeeded: true);

        await sink.RecordingCompletedAsync(completion, CancellationToken.None);

        Assert.Same(completion, inner.Completed);
        Assert.Equal("session", Assert.Single(store.GetRecent(10)).SessionId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch { }
    }

    private static RecordingCompletionInfo CreateCompletion(
        string sessionId,
        string channel,
        DateTimeOffset startedAt,
        bool succeeded,
        bool stoppedManually = false,
        string? error = null)
    {
        return new RecordingCompletionInfo(
            sessionId,
            channel,
            succeeded,
            $"/downloads/{sessionId}.recording",
            succeeded ? $"/downloads/{sessionId}.mp4" : null,
            5400,
            1024,
            3,
            60,
            1,
            error,
            startedAt,
            startedAt.AddMinutes(90),
            stoppedManually);
    }

    private sealed class ForwardingProbe : IRecordingNotificationSink
    {
        public RecordingCompletionInfo? Completed { get; private set; }

        public Task RecordingStartedAsync(RecordingStartedInfo info, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RecordingEndedAsync(RecordingEndedInfo info, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RecordingCompletedAsync(RecordingCompletionInfo info, CancellationToken cancellationToken)
        {
            Completed = info;
            return Task.CompletedTask;
        }
    }
}
