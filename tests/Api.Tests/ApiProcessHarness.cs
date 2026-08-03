using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit.Sdk;

namespace Lingmai.RedMist.Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiProcessCollection
{
    public const string Name = "CX-301 API process";
}

internal sealed class ApiProcessHarness : IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(15);
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _logLines = new();
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;

    private ApiProcessHarness(Process process, Uri baseAddress)
    {
        _process = process;
        Client = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(10),
        };
        _stdoutPump = PumpAsync(process.StandardOutput, _logLines);
        _stderrPump = PumpAsync(process.StandardError, _logLines);
    }

    public HttpClient Client { get; }

    public string[] LogLines => _logLines.ToArray();

    public string Logs => string.Join(Environment.NewLine, LogLines);

    public static async Task<ApiProcessHarness> StartAsync(
        IReadOnlyDictionary<string, string>? configuration = null)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ApiProcessHarness? server = null;
            try
            {
                server = StartCore(configuration);
                await server.WaitUntilReadyAsync(StartupTimeout);
                return server;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (server is not null) await server.DisposeAsync();
            }
        }

        throw new XunitException(
            "The API process did not become ready after three loopback-port attempts." +
            Environment.NewLine + lastError);
    }

    public static async Task<ApiProcessExitResult> RunToExitAsync(
        IReadOnlyDictionary<string, string> configuration,
        TimeSpan timeout)
    {
        await using ApiProcessHarness server = StartCore(configuration);
        using var timeoutSource = new CancellationTokenSource(timeout);
        var timedOut = false;
        try
        {
            await server._process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
        }

        int? exitCode = !timedOut && server._process.HasExited
            ? server._process.ExitCode
            : null;
        if (!timedOut)
            await Task.WhenAll(server._stdoutPump, server._stderrPump).WaitAsync(TimeSpan.FromSeconds(5));
        return new ApiProcessExitResult(exitCode, timedOut, server.Logs);
    }

    public async Task WaitForLogAsync(string marker, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marker);
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                if (_logLines.Any(line => line.Contains(marker, StringComparison.Ordinal))) return;
                if (_process.HasExited)
                {
                    throw new XunitException(
                        $"The API exited with code {_process.ExitCode} before logging '{marker}'." +
                        Environment.NewLine + Logs);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), timeoutSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            throw new XunitException(
                $"The API did not log '{marker}' within {timeout.TotalSeconds} seconds." +
                Environment.NewLine + Logs);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (!_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the state check and Kill.
            }
        }

        if (!_process.HasExited)
        {
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _process.WaitForExitAsync(exitTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                // The process tree was already asked to terminate; do not hang the test run.
            }
        }

        try
        {
            await Task.WhenAll(_stdoutPump, _stderrPump).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Stream pumps are best-effort during forced process teardown.
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static ApiProcessHarness StartCore(
        IReadOnlyDictionary<string, string>? configuration)
    {
        DirectoryInfo repositoryRoot = FindRepositoryRoot();
        string apiDll = Path.Combine(
            repositoryRoot.FullName,
            "server",
            "Api",
            "bin",
            CurrentConfiguration(),
            "net8.0",
            "Api.dll");
        if (!File.Exists(apiDll))
            throw new XunitException($"API output is missing: {apiDll}");

        int port = FindAvailableLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}", UriKind.Absolute);
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(apiDll)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(apiDll);
        startInfo.Environment["ASPNETCORE_URLS"] = baseAddress.AbsoluteUri.TrimEnd('/');
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        if (configuration is not null)
        {
            foreach ((string key, string value) in configuration)
                startInfo.Environment[key] = value;
        }

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new XunitException("The API process could not be started.");
        }

        return new ApiProcessHarness(process, baseAddress);
    }

    private async Task WaitUntilReadyAsync(TimeSpan timeout)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                if (_process.HasExited)
                {
                    throw new XunitException(
                        $"The API exited with code {_process.ExitCode} before opening its loopback endpoint." +
                        Environment.NewLine + Logs);
                }

                try
                {
                    using HttpResponseMessage response = await Client.GetAsync(
                        "/health",
                        timeoutSource.Token);
                    return;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(50), timeoutSource.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw new XunitException(
                $"The API did not open its loopback endpoint within {timeout.TotalSeconds} seconds." +
                Environment.NewLine + Logs);
        }
    }

    private static async Task PumpAsync(
        StreamReader reader,
        ConcurrentQueue<string> destination)
    {
        while (await reader.ReadLineAsync() is { } line)
            destination.Enqueue(line);
    }

    private static int FindAvailableLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string CurrentConfiguration()
    {
        var frameworkDirectory = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        return frameworkDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException("Could not determine the test configuration.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath)) return directory;
        }

        throw new DirectoryNotFoundException("Could not find the Video repository root.");
    }
}

internal sealed record ApiProcessExitResult(
    int? ExitCode,
    bool TimedOut,
    string Logs);
