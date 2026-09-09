namespace TwitchDownloader2.CLI
{
    internal interface IAsyncDelay
    {
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    internal sealed class SystemAsyncDelay : IAsyncDelay
    {
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.Delay(delay, cancellationToken);
        }
    }
}
