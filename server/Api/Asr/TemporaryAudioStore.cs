namespace Lingmai.RedMist.Api.Asr;

public sealed class TemporaryAudioStore
{
    private const int CopyBufferBytes = 64 * 1024;
    private readonly string _stagingDirectory;
    private readonly int _maxAudioBytes;
    private readonly bool _deleteAfterProcessing;

    public TemporaryAudioStore(AsrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.StagingDirectory))
            throw new ArgumentException("A staging directory is required.", nameof(options));
        if (options.MaxAudioBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxAudioBytes must be positive.");

        _stagingDirectory = Path.GetFullPath(options.StagingDirectory);
        _maxAudioBytes = options.MaxAudioBytes;
        _deleteAfterProcessing = options.DeleteAfterProcessing;
    }

    public async Task<T> UseAsync<T>(
        Stream source,
        string extension,
        Func<string, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(operation);
        if (!source.CanRead) throw new ArgumentException("The audio stream must be readable.", nameof(source));
        if (!string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
            throw new AsrValidationException(AsrErrorCodes.InvalidWav, "Only WAV staging is supported.");

        Directory.CreateDirectory(_stagingDirectory);
        string path = Path.Combine(_stagingDirectory, Guid.NewGuid().ToString("N") + ".wav");
        bool staged = false;
        try
        {
            await StageAsync(source, path, cancellationToken).ConfigureAwait(false);
            staged = true;
            return await operation(path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (!staged || _deleteAfterProcessing) DeleteIfPresent(path);
        }
    }

    private async Task StageAsync(Stream source, string path, CancellationToken cancellationToken)
    {
        await using var destination = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[CopyBufferBytes];
        int total = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total = checked(total + read);
            if (total > _maxAudioBytes)
                throw new AsrValidationException(AsrErrorCodes.AudioTooLarge, "The audio exceeds the configured byte limit.");
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
