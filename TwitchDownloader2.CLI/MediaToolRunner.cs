using System.Diagnostics;

namespace TwitchDownloader2.CLI
{
    internal sealed record MediaToolResult(int ExitCode, string StandardOutput, string StandardError);

    internal interface IMediaToolRunner
    {
        Task<MediaToolResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken);
    }

    internal sealed class ProcessMediaToolRunner : IMediaToolRunner
    {
        public async Task<MediaToolResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException($"Could not start {executable}.");

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best effort cleanup when the caller cancels an external tool.
                }
                throw;
            }

            return new MediaToolResult(
                process.ExitCode,
                await standardOutput,
                await standardError);
        }
    }
}
