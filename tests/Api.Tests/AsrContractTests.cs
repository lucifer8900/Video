using Lingmai.RedMist.Api.Asr;
using Lingmai.RedMist.Contracts.Asr;

namespace Lingmai.RedMist.Api.Tests;

public sealed class AsrContractTests
{
    [Fact]
    public void DefaultLimitsAreOneQuarterMebibyteEightSecondsAndStrictPcm16Mono16Khz()
    {
        var options = new AsrOptions();

        Assert.Equal(262_144, options.MaxAudioBytes);
        Assert.Equal(8d, options.MaxDurationSeconds);
        Assert.Equal([16_000], options.AllowedSampleRatesHz);
        Assert.Equal(1, options.RequiredChannels);
        Assert.Equal(16, options.RequiredBitsPerSample);
        Assert.True(options.DeleteAfterProcessing);
    }

    [Fact]
    public void InspectorAcceptsExactlyEightSecondsOf16KhzMonoPcm16()
    {
        var options = new AsrOptions();
        var inspector = new WavAudioInspector(options);
        byte[] bytes = WavFixture.CreatePcm(durationSeconds: 8d);

        WavAudioMetadata metadata = inspector.Inspect(new MemoryStream(bytes));

        Assert.Equal(16_000, metadata.SampleRateHz);
        Assert.Equal(1, metadata.Channels);
        Assert.Equal(16, metadata.BitsPerSample);
        Assert.Equal(256_000, metadata.DataBytes);
        Assert.Equal(8d, metadata.DurationSeconds, 6);
    }

    [Theory]
    [InlineData(48_000, 1, 16, AsrErrorCodes.SampleRateUnsupported)]
    [InlineData(16_000, 2, 16, AsrErrorCodes.ChannelsUnsupported)]
    [InlineData(16_000, 1, 32, AsrErrorCodes.EncodingUnsupported)]
    public void InspectorRejectsAnythingOutsideTheStrictWaveContract(
        int sampleRateHz,
        int channels,
        int bitsPerSample,
        string expectedCode)
    {
        var inspector = new WavAudioInspector(new AsrOptions());
        byte[] bytes = WavFixture.CreatePcm(sampleRateHz, channels, bitsPerSample);

        AsrValidationException error = Assert.Throws<AsrValidationException>(
            () => inspector.Inspect(new MemoryStream(bytes)));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void InspectorAcceptsTheExactByteLimitAndRejectsAnythingLarger()
    {
        var inspector = new WavAudioInspector(new AsrOptions());
        byte[] exact = WavFixture.CreatePcm(durationSeconds: 8d, totalLength: 262_144);
        byte[] tooLarge = WavFixture.CreatePcm(durationSeconds: 8d, totalLength: 262_146);

        Assert.Equal(262_144, exact.Length);
        Assert.Equal(8d, inspector.Inspect(new MemoryStream(exact)).DurationSeconds, 6);

        AsrValidationException error = Assert.Throws<AsrValidationException>(
            () => inspector.Inspect(new MemoryStream(tooLarge)));
        Assert.Equal(AsrErrorCodes.AudioTooLarge, error.Code);
    }

    [Fact]
    public void InspectorRejectsAudioLongerThanEightSeconds()
    {
        var inspector = new WavAudioInspector(new AsrOptions());
        byte[] bytes = WavFixture.CreatePcm(durationSeconds: 8.001d);

        AsrValidationException error = Assert.Throws<AsrValidationException>(
            () => inspector.Inspect(new MemoryStream(bytes)));

        Assert.Equal(AsrErrorCodes.DurationExceeded, error.Code);
    }

    [Fact]
    public async Task MockProviderIsDeterministic()
    {
        var expected = new AsrProviderResult("deterministic transcript", "zh", 1d);
        var provider = new MockAsrProvider(expected);
        await using var stream = new MemoryStream(WavFixture.CreatePcm());
        var input = new AsrAudioInput(stream, "voice.wav", "audio/wav", stream.Length, 16_000, 1, 1d);

        AsrProviderResult first = await provider.TranscribeAsync(input, CancellationToken.None);
        stream.Position = 0;
        AsrProviderResult second = await provider.TranscribeAsync(input, CancellationToken.None);

        Assert.Equal(expected, first);
        Assert.Equal(first, second);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task InvalidAudioNeverCallsTheProvider()
    {
        using var directory = new TemporaryDirectory();
        var options = new AsrOptions
        {
            StagingDirectory = directory.Path,
            DeleteAfterProcessing = true,
        };
        var provider = new MockAsrProvider(new AsrProviderResult("unused", "zh", 0d));
        var service = new AsrTranscriptionService(
            new WavAudioInspector(options),
            new TemporaryAudioStore(options),
            provider);
        byte[] invalid = WavFixture.CreatePcm(sampleRateHz: 48_000);

        await Assert.ThrowsAsync<AsrValidationException>(
            () => service.TranscribeAsync(
                new MemoryStream(invalid),
                "voice.wav",
                "audio/wav",
                CancellationToken.None));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task TemporaryAudioIsDeletedAfterSuccess()
    {
        using var directory = new TemporaryDirectory();
        var store = Store(directory, deleteAfterProcessing: true);
        string? stagedPath = null;

        int result = await store.UseAsync(
            new MemoryStream(WavFixture.CreatePcm()),
            ".wav",
            (path, _) =>
            {
                stagedPath = path;
                Assert.True(File.Exists(path));
                return Task.FromResult(7);
            },
            CancellationToken.None);

        Assert.Equal(7, result);
        Assert.NotNull(stagedPath);
        Assert.False(File.Exists(stagedPath));
    }

    [Fact]
    public async Task TemporaryAudioIsDeletedAfterConsumerException()
    {
        using var directory = new TemporaryDirectory();
        var store = Store(directory, deleteAfterProcessing: true);
        string? stagedPath = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UseAsync<int>(
            new MemoryStream(WavFixture.CreatePcm()),
            ".wav",
            (path, _) =>
            {
                stagedPath = path;
                throw new InvalidOperationException("test consumer failure");
            },
            CancellationToken.None));

        Assert.NotNull(stagedPath);
        Assert.False(File.Exists(stagedPath));
    }

    [Fact]
    public async Task TemporaryAudioIsDeletedAfterCancellation()
    {
        using var directory = new TemporaryDirectory();
        var store = Store(directory, deleteAfterProcessing: true);
        using var cancellation = new CancellationTokenSource();
        string? stagedPath = null;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UseAsync<int>(
            new MemoryStream(WavFixture.CreatePcm()),
            ".wav",
            async (path, token) =>
            {
                stagedPath = path;
                cancellation.Cancel();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            },
            cancellation.Token));

        Assert.NotNull(stagedPath);
        Assert.False(File.Exists(stagedPath));
    }

    [Fact]
    public async Task TemporaryAudioIsRetainedOnlyWhenConfigured()
    {
        using var directory = new TemporaryDirectory();
        var store = Store(directory, deleteAfterProcessing: false);
        string? stagedPath = null;

        await store.UseAsync(
            new MemoryStream(WavFixture.CreatePcm()),
            ".wav",
            (path, _) =>
            {
                stagedPath = path;
                return Task.FromResult(0);
            },
            CancellationToken.None);

        Assert.NotNull(stagedPath);
        Assert.True(File.Exists(stagedPath));
    }

    private static TemporaryAudioStore Store(TemporaryDirectory directory, bool deleteAfterProcessing) =>
        new(new AsrOptions
        {
            StagingDirectory = directory.Path,
            DeleteAfterProcessing = deleteAfterProcessing,
        });

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "redmist-asr-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
