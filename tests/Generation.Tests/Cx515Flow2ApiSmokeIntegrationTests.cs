using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lingmai.RedMist.Generation.Providers;

namespace Lingmai.RedMist.Generation.Tests;

public sealed class Cx515Flow2ApiSmokeIntegrationTests
{
    private const string EnableVariable = "FLOW2API_CX515_SMOKE";
    private const string AttemptVariable = "FLOW2API_CX515_ATTEMPT";
    private const string CredentialVariable = "Flow2API";

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GeneratesExactlyOneNonShippingLiteI2VArtifactWhenExplicitlyEnabled()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(EnableVariable),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string apiKey = Environment.GetEnvironmentVariable(CredentialVariable)
            ?? throw new InvalidOperationException("The Flow2API credential is unavailable.");
        Assert.False(string.IsNullOrWhiteSpace(apiKey));
        string attempt = Environment.GetEnvironmentVariable(AttemptVariable) ?? "1";
        if (attempt is not ("1" or "2"))
            throw new InvalidOperationException("Only CX-515 smoke attempts 1 and 2 are authorized.");
        string attemptSuffix = attempt == "1" ? string.Empty : ".attempt2";

        string repositoryRoot = FindRepositoryRoot();
        string inputPath = Path.Combine(
            repositoryRoot,
            "unity",
            "RedMistVerticalSlice",
            "Assets",
            "Resources",
            "Generated",
            "VideoFirstFrames",
            "firstframe_dialogue_scout_mist_v4.png");
        Assert.True(File.Exists(inputPath), "The approved CX-513 scout first frame is missing.");
        byte[] inputBytes = await File.ReadAllBytesAsync(inputPath);
        string inputHash = Hash(inputBytes);

        string outputDirectory = Path.Combine(
            Directory.GetParent(repositoryRoot)!.FullName,
            "TestVideo");
        Directory.CreateDirectory(outputDirectory);
        string lockPath = Path.Combine(outputDirectory, $"cx515-flow2api-smoke{attemptSuffix}.lock.json");
        string outputPath = Path.Combine(outputDirectory, $"cx515-flow2api-veo31-lite-i2v{attemptSuffix}.mp4");
        string tempPath = outputPath + ".partial";
        string reportPath = Path.Combine(outputDirectory, $"cx515-flow2api-smoke{attemptSuffix}.report.json");

        Assert.False(File.Exists(lockPath),
            "CX-515 smoke request was already attempted; refusing an automatic retry.");
        Assert.False(File.Exists(outputPath),
            "CX-515 smoke output already exists; refusing an automatic retry.");
        await using (FileStream guard = new(
                         lockPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.Read,
                         bufferSize: 4096,
                         useAsync: true))
        {
            await JsonSerializer.SerializeAsync(guard, new
            {
                cardId = "CX-515",
                attempt = int.Parse(attempt, System.Globalization.CultureInfo.InvariantCulture),
                status = "started",
                requestCountLimit = 1,
                provider = "Flow2API",
                endpoint = "http://127.0.0.1:18001/v1/chat/completions",
                model = Flow2ApiVideoGenerationProvider.ImageToVideoLiteModel,
                credentialSource = CredentialVariable,
                credentialValueRecorded = false,
                startedAtUtc = DateTimeOffset.UtcNow,
            });
        }

        const string prompt =
            "Non-shipping technical motion test in the existing Red Mist Secret Garden world. " +
            "Preserve the approved East Asian female scout's face, low braid, teal-gray narrow-sleeved robe, " +
            "charcoal shoulder armor, pale-jade listening talisman, body proportions and the monumental mist-gate set. " +
            "She breathes continuously, makes one subtle natural weight shift, turns her head slightly toward the mist, " +
            "then settles into alert eye contact; shoulders, cloth and loose hair react with restrained physical motion. " +
            "Keep both hands anatomically clear and naturally relaxed, five fingers stable, wrists connected, no blur. " +
            "Use one slow cinematic push with grounded parallax while the character remains fully readable in frame. " +
            "Photoreal live-action skin, cloth weight, contact shadow and shared environmental light. " +
            "Closed mouth, no speech, no audio, no subtitles, no text, no UI, no morphing, no disappearance, no cuts. " +
            "Landscape 16:9, exactly 8 seconds.";

        using var httpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromMinutes(26),
        };
        var provider = new Flow2ApiVideoGenerationProvider(
            httpClient,
            new Flow2ApiVideoProviderOptions
            {
                Endpoint = new Uri("http://127.0.0.1:18001/v1/chat/completions"),
                ApiKey = apiKey,
                TextToVideoModelId = Flow2ApiVideoGenerationProvider.TextToVideoLiteModel,
                ImageToVideoModelId = Flow2ApiVideoGenerationProvider.ImageToVideoLiteModel,
                MaxArtifactBytes = 128L * 1024 * 1024,
            });
        var request = new VideoGenerationRequest
        {
            JobId = Guid.NewGuid(),
            Prompt = prompt,
            InputHash = Hash(System.Text.Encoding.UTF8.GetBytes(prompt + "\n" + inputHash)),
            Seed = 0,
            AspectRatio = "16:9",
            DurationSeconds = 8,
            InputImages = [new VideoGenerationInputImage("image/png", inputBytes, inputHash)],
        };

        var stopwatch = Stopwatch.StartNew();
        ProviderOperation operation;
        try
        {
            operation = await provider.StartAsync(request, CancellationToken.None);
            Assert.Equal(ProviderOperationStatus.Succeeded, operation.Status);
            Assert.NotNull(operation.Artifact);
            await using (FileStream destination = new(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             useAsync: true))
            {
                await provider.DownloadAsync(operation.Artifact, destination, CancellationToken.None);
            }

            File.Move(tempPath, outputPath);
        }
        catch (Exception exception)
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            string failureCode = exception is GenerationProviderException providerFailure
                ? providerFailure.Code
                : "smoke.unexpected_failure";
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                cardId = "CX-515",
                attempt = int.Parse(attempt, System.Globalization.CultureInfo.InvariantCulture),
                status = "failed",
                shippingStatus = "non_shipping_smoke_test",
                requestCount = 1,
                automaticRetries = 0,
                provider = "Flow2API",
                endpoint = "http://127.0.0.1:18001/v1/chat/completions",
                method = "POST",
                stream = true,
                model = Flow2ApiVideoGenerationProvider.ImageToVideoLiteModel,
                failureCode,
                failureMessageRecorded = false,
                credentialSource = CredentialVariable,
                credentialValueRecorded = false,
                failedAtUtc = DateTimeOffset.UtcNow,
            }, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally
        {
            stopwatch.Stop();
        }

        var outputInfo = new FileInfo(outputPath);
        byte[] outputBytes = await File.ReadAllBytesAsync(outputPath);
        Assert.Equal(operation.Artifact!.Length, outputInfo.Length);
        Assert.Equal(operation.Artifact.Sha256, Hash(outputBytes));
        Assert.True(outputBytes.Length >= 8 &&
                    outputBytes[4] == (byte)'f' && outputBytes[5] == (byte)'t' &&
                    outputBytes[6] == (byte)'y' && outputBytes[7] == (byte)'p');

        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            cardId = "CX-515",
            attempt = int.Parse(attempt, System.Globalization.CultureInfo.InvariantCulture),
            status = "succeeded",
            shippingStatus = "non_shipping_smoke_test",
            requestCount = 1,
            automaticRetries = 0,
            provider = "Flow2API",
            endpoint = "http://127.0.0.1:18001/v1/chat/completions",
            method = "POST",
            stream = true,
            model = Flow2ApiVideoGenerationProvider.ImageToVideoLiteModel,
            durationSeconds = 8,
            aspectRatio = "16:9",
            inputFile = Path.GetFileName(inputPath),
            inputSha256 = inputHash,
            promptSha256 = Hash(System.Text.Encoding.UTF8.GetBytes(prompt)),
            outputFile = Path.GetFileName(outputPath),
            outputBytes = outputInfo.Length,
            outputSha256 = operation.Artifact.Sha256,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            credentialSource = CredentialVariable,
            credentialValueRecorded = false,
            completedAtUtc = DateTimeOffset.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Video.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Video repository root.");
    }

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
