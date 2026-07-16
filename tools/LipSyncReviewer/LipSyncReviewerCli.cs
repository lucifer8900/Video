using System.Security.Cryptography;
using System.Text;

namespace LipSyncReviewer;

public static class LipSyncReviewerCli
{
    private const string Usage =
        "Usage: LipSyncReviewer review --manifest <shot-manifest.json> " +
        "--media-root <content-addressed-root> --output <report.json> [--port <0-65535>]";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        TimeProvider timeProvider,
        CancellationToken cancellationToken,
        string? schemaDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (!TryParse(args, out ReviewArguments? parsed))
        {
            stderr.WriteLine(Usage);
            return 1;
        }

        try
        {
            ReviewArguments options = parsed!;
            string outputPath = Path.GetFullPath(options.OutputPath);
            if (File.Exists(outputPath))
                throw new LipSyncReviewGateException("report.exists", "The review report already exists.");
            string schemas = schemaDirectory ?? Path.Combine(AppContext.BaseDirectory, "schemas");
            LoadedLipSyncReviewManifest manifest = await new LipSyncReviewManifestLoader(schemas)
                .LoadAsync(options.ManifestPath, cancellationToken)
                .ConfigureAwait(false);
            string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var writer = new LipSyncReviewReportWriter(timeProvider);

            if (manifest.ReviewableItems.Count == 0)
            {
                var blockedSession = new LipSyncReviewSession(manifest, [], timeProvider, token);
                WrittenLipSyncReviewReport blocked = writer.Write(
                    blockedSession,
                    outputPath,
                    reviewerSubject: null);
                stdout.Write(blocked.Json);
                return 2;
            }

            await using LipSyncReviewMediaLease mediaLease = await new ContentAddressedMediaStore()
                .OpenAsync(manifest, options.MediaRoot, cancellationToken)
                .ConfigureAwait(false);
            var session = new LipSyncReviewSession(manifest, mediaLease.Items, timeProvider, token);

            string reviewerSubject = Hash(
                Environment.UserDomainName + "\\" + Environment.UserName);
            await using RunningLipSyncReviewHost host = await LipSyncReviewWebHost.StartAsync(
                    session,
                    mediaLease,
                    writer,
                    outputPath,
                    reviewerSubject,
                    token,
                    options.Port,
                    cancellationToken)
                .ConfigureAwait(false);
            stdout.WriteLine($"lip-sync-reviewer: {host.BaseAddress}");
            stdout.WriteLine("请在本机浏览器打开该地址；导出后工具会自动退出。");
            WrittenLipSyncReviewReport completed = await host.Completion
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            stdout.Write(completed.Json);
            return string.Equals(
                completed.Report.OverallStatus,
                "lip_sync_passed",
                StringComparison.Ordinal)
                ? 0
                : 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stderr.WriteLine("lip-sync-reviewer: cancelled");
            return 130;
        }
        catch (LipSyncReviewInputException exception)
        {
            stderr.WriteLine($"lip-sync-reviewer: {exception.Code}: {exception.Message}");
            return 1;
        }
        catch (LipSyncReviewGateException exception)
        {
            stderr.WriteLine($"lip-sync-reviewer: {exception.Code}: {exception.Message}");
            return 1;
        }
        catch (Exception exception)
        {
            stderr.WriteLine("lip-sync-reviewer: " + exception.Message);
            return 1;
        }
    }

    private static bool TryParse(string[] args, out ReviewArguments? parsed)
    {
        parsed = null;
        if (args.Length < 7 ||
            !string.Equals(args[0], "review", StringComparison.Ordinal) ||
            (args.Length - 1) % 2 != 0)
        {
            return false;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < args.Length; index += 2)
        {
            string option = args[index];
            if (option is not ("--manifest" or "--media-root" or "--output" or "--port") ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                !values.TryAdd(option, args[index + 1]))
            {
                return false;
            }
        }

        if (!values.TryGetValue("--manifest", out string? manifest) ||
            !values.TryGetValue("--media-root", out string? mediaRoot) ||
            !values.TryGetValue("--output", out string? output))
        {
            return false;
        }
        int port = 0;
        if (values.TryGetValue("--port", out string? configuredPort) &&
            (!int.TryParse(
                configuredPort,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out port) ||
             port is < 0 or > 65535))
        {
            return false;
        }

        parsed = new ReviewArguments(manifest, mediaRoot, output, port);
        return true;
    }

    private static string Hash(string value) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record ReviewArguments(
        string ManifestPath,
        string MediaRoot,
        string OutputPath,
        int Port);
}
