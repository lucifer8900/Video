using System.ComponentModel;
using System.Diagnostics;

namespace Lingmai.RedMist.MediaPipeline;

public sealed class FfmpegProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessSpec spec,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = BuildStartInfo(spec),
            EnableRaisingEvents = true,
        };

        try
        {
            if (!process.Start()) throw StartFailure();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw StartFailure();
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(spec.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await WaitAfterKillAsync(process).ConfigureAwait(false);
            await DrainQuietlyAsync(standardOutput, standardError).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            throw new ProcessExecutionException(
                "media.process_timeout",
                "The media tool exceeded its configured execution time.");
        }

        string output = await standardOutput.ConfigureAwait(false);
        string error = await standardError.ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static ProcessStartInfo BuildStartInfo(ProcessSpec spec)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
        };
        foreach (string argument in spec.ArgumentList)
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process may have exited between the cancellation signal and the kill request.
        }
    }

    private static async Task WaitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // A failed start has no process to wait for.
        }
    }

    private static async Task DrainQuietlyAsync(params Task<string>[] readers)
    {
        try
        {
            await Task.WhenAll(readers).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Output is intentionally discarded on cancellation and timeout.
        }
    }

    private static ProcessExecutionException StartFailure() =>
        new("media.process_start_failed", "The configured media tool could not be started.");
}
