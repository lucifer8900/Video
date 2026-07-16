namespace LipSyncReviewer.Tests;

public sealed class LipSyncReviewerCliTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 16, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ActualBlockedManifestWritesBlockedReportAndReturnsTwo()
    {
        DirectoryInfo root = TestRepository.FindRoot();
        using var temporary = new TemporaryDirectory();
        string mediaRoot = Path.Combine(temporary.Path, "media-root");
        Assert.False(Directory.Exists(mediaRoot));
        string output = Path.Combine(temporary.Path, "lip-sync-review.report.json");
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int exitCode = await LipSyncReviewerCli.RunAsync(
            [
                "review",
                "--manifest",
                Path.Combine(root.FullName, "content", "story", "red-mist", "shot-manifest.json"),
                "--media-root",
                mediaRoot,
                "--output",
                output,
                "--port",
                "0",
            ],
            stdout,
            stderr,
            new FixedTimeProvider(FixedNow),
            CancellationToken.None,
            Path.Combine(root.FullName, "server", "Contracts", "schemas"));

        Assert.Equal(2, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stderr.ToString()), stderr.ToString());
        Assert.True(File.Exists(output));
        Assert.Contains("\"overallStatus\": \"blocked\"", File.ReadAllText(output), StringComparison.Ordinal);
        Assert.Equal(File.ReadAllText(output).Trim(), stdout.ToString().Trim());
    }

    [Fact]
    public async Task InvalidArgumentsReturnOneAndWriteNoReport()
    {
        using var temporary = new TemporaryDirectory();
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        int exitCode = await LipSyncReviewerCli.RunAsync(
            ["review", "--output", Path.Combine(temporary.Path, "report.json")],
            stdout,
            stderr,
            new FixedTimeProvider(FixedNow),
            CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.True(string.IsNullOrWhiteSpace(stdout.ToString()));
        Assert.Contains("Usage:", stderr.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(temporary.Path));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cx503-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
