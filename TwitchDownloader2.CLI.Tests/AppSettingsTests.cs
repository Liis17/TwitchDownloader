using TwitchDownloader2.CLI;
using Xunit;

namespace TwitchDownloader2.CLI.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public async Task SaveOrThrow_BlocksStateChangesUntilTheSnapshotIsWritten()
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new AppSettings((_, _) =>
        {
            writeStarted.SetResult();
            releaseWrite.Task.GetAwaiter().GetResult();
        });
        settings.AddPausedChannel("alpha");

        var saveTask = Task.Run(settings.SaveOrThrow);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var mutationTask = Task.Run(() =>
        {
            mutationStarted.SetResult();
            return settings.AddPausedChannel("beta");
        });
        await mutationStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        try
        {
            await Task.Delay(50);
            Assert.False(mutationTask.IsCompleted);
        }
        finally
        {
            releaseWrite.SetResult();
        }

        await saveTask;
        Assert.True(await mutationTask);
    }
}
