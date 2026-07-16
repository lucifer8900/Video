using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace LipSyncReviewer;

public sealed record LipSyncReviewMediaItem(
    LipSyncReviewCandidate Candidate,
    string LipSyncPath,
    string AudioPath,
    string FallbackPath,
    long LipSyncLength,
    long AudioLength,
    long LipSyncDurationMilliseconds,
    long AudioDurationMilliseconds);

public sealed class LipSyncReviewMediaLease : IAsyncDisposable
{
    private readonly IReadOnlyList<GuardedMedia> _guards;
    private readonly IReadOnlyDictionary<PlaybackMediaKey, GuardedMedia> _playbackMedia;
    private readonly SemaphoreSlim _verificationGate = new(1, 1);
    private int _disposed;

    internal LipSyncReviewMediaLease(
        IReadOnlyList<LipSyncReviewMediaItem> items,
        IReadOnlyList<GuardedMedia> guards)
    {
        Items = items;
        MediaBindingHash = LipSyncReviewMediaBinding.Compute(items);
        _guards = guards;
        _playbackMedia = guards
            .Where(guard => guard.PlaybackKind is not null)
            .ToDictionary(
                guard => new PlaybackMediaKey(guard.ResponseId, guard.PlaybackKind!.Value),
                guard => guard);
    }

    public IReadOnlyList<LipSyncReviewMediaItem> Items { get; }

    internal string MediaBindingHash { get; }

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _verificationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (GuardedMedia guarded in _guards)
            {
                byte[] actual = await HashAsync(guarded, cancellationToken)
                    .ConfigureAwait(false);
                string actualHash = "sha256:" + Convert.ToHexString(actual).ToLowerInvariant();
                if (!string.Equals(actualHash, guarded.ContentHash, StringComparison.Ordinal))
                {
                    throw new LipSyncReviewInputException(
                        "media.hash_changed",
                        "A locked review media file changed before report export.");
                }
            }
        }
        finally
        {
            _verificationGate.Release();
        }
    }

    internal bool TryGetPlaybackMedia(
        string responseId,
        LipSyncPlaybackKind kind,
        out LipSyncPlaybackMediaDescriptor descriptor)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_playbackMedia.TryGetValue(new PlaybackMediaKey(responseId, kind), out GuardedMedia? media))
        {
            descriptor = new LipSyncPlaybackMediaDescriptor(
                media.Stream.Length,
                ContentType(media.Extension));
            return true;
        }
        descriptor = default;
        return false;
    }

    internal async ValueTask<int> ReadPlaybackAsync(
        string responseId,
        LipSyncPlaybackKind kind,
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (!_playbackMedia.TryGetValue(new PlaybackMediaKey(responseId, kind), out GuardedMedia? media))
            throw new LipSyncReviewGateException(
                "review.unknown_response",
                "The response is not part of this review session.");
        if (offset < 0 || offset > media.Stream.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return await RandomAccess.ReadAsync(
                media.Stream.SafeFileHandle,
                destination,
                offset,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (GuardedMedia guard in _guards.Reverse())
            await guard.Stream.DisposeAsync().ConfigureAwait(false);
        _verificationGate.Dispose();
    }

    private static async Task<byte[]> HashAsync(
        GuardedMedia guarded,
        CancellationToken cancellationToken)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81_920];
        long offset = 0;
        while (offset < guarded.Stream.Length)
        {
            int read = await RandomAccess.ReadAsync(
                    guarded.Stream.SafeFileHandle,
                    buffer,
                    offset,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            offset += read;
        }
        if (offset != guarded.Stream.Length)
        {
            throw new LipSyncReviewInputException(
                "media.read_incomplete",
                "A locked review media file could not be read completely.");
        }
        return hash.GetHashAndReset();
    }

    private static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".wav" => "audio/wav",
        ".m4a" => "audio/mp4",
        ".mp3" => "audio/mpeg",
        ".ogg" => "audio/ogg",
        _ => "application/octet-stream",
    };
}

internal readonly record struct LipSyncPlaybackMediaDescriptor(long Length, string ContentType);

internal readonly record struct PlaybackMediaKey(string ResponseId, LipSyncPlaybackKind Kind);

internal static class LipSyncReviewMediaBinding
{
    public static string Compute(IEnumerable<LipSyncReviewMediaItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        string canonical = string.Join(
            "\n",
            items.OrderBy(item => item.Candidate.ResponseId, StringComparer.Ordinal)
                .SelectMany(Parts));
        return "sha256:" + Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static IEnumerable<string> Parts(LipSyncReviewMediaItem item)
    {
        yield return item.Candidate.ResponseId;
        yield return item.Candidate.DialogueContentHash;
        yield return item.Candidate.PresentationHash;
        foreach (LipSyncReviewMediaPin pin in new[]
                 {
                     item.Candidate.LipSyncMedia,
                     item.Candidate.AudioMedia,
                     item.Candidate.FallbackMedia,
                 })
        {
            yield return pin.MediaRef;
            yield return pin.AssetVersion;
            yield return pin.ContentHash;
            yield return pin.MediaType;
        }
        yield return item.LipSyncLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return item.AudioLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        yield return item.LipSyncDurationMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        yield return item.AudioDurationMilliseconds.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed partial class ContentAddressedMediaStore
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedExtensions =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["video"] = [".mp4", ".webm"],
            ["animation"] = [".mp4", ".webm"],
            ["audio"] = [".wav", ".m4a", ".mp3", ".ogg"],
            ["image"] = [".png", ".jpg", ".jpeg", ".webp"],
        };

    private readonly IReviewMediaProbe _probe;

    public ContentAddressedMediaStore(IReviewMediaProbe? probe = null) =>
        _probe = probe ?? new FfprobeReviewMediaProbe();

    public async Task<LipSyncReviewMediaLease> OpenAsync(
        LoadedLipSyncReviewManifest manifest,
        string mediaRoot,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaRoot);
        string root = Path.GetFullPath(mediaRoot);
        if (!Directory.Exists(root))
            throw Invalid("media.root_missing", "The content-addressed media root was not found.");
        RejectReparsePoint(root, root);

        var guards = new List<GuardedMedia>();
        var items = new List<LipSyncReviewMediaItem>(manifest.ReviewableItems.Count);
        try
        {
            foreach (LipSyncReviewCandidate candidate in manifest.ReviewableItems)
            {
                GuardedMedia lipSync = await ResolveAndLockAsync(
                        root,
                        candidate.ResponseId,
                        LipSyncPlaybackKind.OriginalVideo,
                        candidate.LipSyncMedia,
                        guards,
                        cancellationToken)
                    .ConfigureAwait(false);
                GuardedMedia audio = await ResolveAndLockAsync(
                        root,
                        candidate.ResponseId,
                        LipSyncPlaybackKind.ReferenceAudio,
                        candidate.AudioMedia,
                        guards,
                        cancellationToken)
                    .ConfigureAwait(false);
                GuardedMedia fallback = await ResolveAndLockAsync(
                        root,
                        candidate.ResponseId,
                        playbackKind: null,
                        candidate.FallbackMedia,
                        guards,
                        cancellationToken)
                    .ConfigureAwait(false);
                items.Add(new LipSyncReviewMediaItem(
                    candidate,
                    lipSync.Path,
                    audio.Path,
                    fallback.Path,
                    lipSync.Stream.Length,
                    audio.Stream.Length,
                    lipSync.DurationMilliseconds,
                    audio.DurationMilliseconds));
            }

            return new LipSyncReviewMediaLease(items.AsReadOnly(), guards.AsReadOnly());
        }
        catch
        {
            foreach (GuardedMedia guard in guards.AsEnumerable().Reverse())
                await guard.Stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<GuardedMedia> ResolveAndLockAsync(
        string root,
        string responseId,
        LipSyncPlaybackKind? playbackKind,
        LipSyncReviewMediaPin pin,
        ICollection<GuardedMedia> guards,
        CancellationToken cancellationToken)
    {
        string contentHash = pin.ContentHash ?? string.Empty;
        if (!Sha256Pattern().IsMatch(contentHash) ||
            !AllowedExtensions.TryGetValue(pin.MediaType, out string[]? extensions))
        {
            throw Invalid("media.pin_invalid", "A review media pin is invalid.");
        }

        string hashHex = contentHash["sha256:".Length..];
        string directory = Path.Combine(root, "media", "sha256", hashHex[..2]);
        if (!Directory.Exists(directory))
            throw Invalid("media.missing", $"Pinned media '{pin.MediaRef}' was not found.");
        RejectReparsePoint(root, directory);
        string[] matches = Directory.EnumerateFiles(directory, hashHex + ".*")
            .Where(path => extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (matches.Length == 0)
            throw Invalid("media.missing", $"Pinned media '{pin.MediaRef}' was not found.");
        if (matches.Length > 1)
            throw Invalid("media.ambiguous", $"Pinned media '{pin.MediaRef}' has multiple local files.");

        string path = matches[0];
        EnsureUnderRoot(root, path);
        RejectReparsePoint(root, path);
        var guard = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        try
        {
            byte[] actual = await SHA256.HashDataAsync(guard, cancellationToken).ConfigureAwait(false);
            string actualHash = "sha256:" + Convert.ToHexString(actual).ToLowerInvariant();
            if (!string.Equals(actualHash, contentHash, StringComparison.Ordinal))
            {
                throw Invalid(
                    "media.hash_mismatch",
                    $"Pinned media '{pin.MediaRef}' does not match its SHA-256 hash.");
            }

            guard.Position = 0;
            await ValidateContainerSignatureAsync(guard, pin.MediaType, Path.GetExtension(path), cancellationToken)
                .ConfigureAwait(false);
            long durationMilliseconds = 0;
            if (playbackKind is not null)
            {
                ReviewMediaProbeResult probe = await _probe.ProbeAsync(
                        guard,
                        pin.MediaType,
                        cancellationToken)
                    .ConfigureAwait(false);
                durationMilliseconds = probe.DurationMilliseconds;
                if (durationMilliseconds <= 0)
                    throw Invalid("media.probe_invalid", "The review media duration is invalid.");
            }

            guard.Position = 0;
            var guarded = new GuardedMedia(
                guard,
                contentHash,
                path,
                Path.GetExtension(path),
                responseId,
                playbackKind,
                durationMilliseconds);
            guards.Add(guarded);
            return guarded;
        }
        catch
        {
            await guard.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task ValidateContainerSignatureAsync(
        FileStream stream,
        string mediaType,
        string extension,
        CancellationToken cancellationToken)
    {
        byte[] prefix = new byte[Math.Min(64, checked((int)Math.Min(stream.Length, 64L)))];
        int read = await RandomAccess.ReadAsync(
                stream.SafeFileHandle,
                prefix,
                fileOffset: 0,
                cancellationToken)
            .ConfigureAwait(false);
        string normalized = extension.ToLowerInvariant();
        if (!HasContainerSignature(prefix, read, normalized))
        {
            throw Invalid(
                "media.container_invalid",
                $"The pinned {mediaType} media does not match its declared container.");
        }
    }

    private static bool HasContainerSignature(byte[] prefix, int length, string extension)
    {
        ReadOnlySpan<byte> bytes = prefix.AsSpan(0, length);
        return extension switch
        {
            ".mp4" or ".m4a" => bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8),
            ".webm" => bytes.Length >= 4 &&
                       bytes[..4].SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }),
            ".wav" => bytes.Length >= 12 &&
                      bytes[..4].SequenceEqual("RIFF"u8) &&
                      bytes[8..12].SequenceEqual("WAVE"u8),
            ".mp3" => bytes.Length >= 3 &&
                      (bytes[..3].SequenceEqual("ID3"u8) ||
                       (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0)),
            ".ogg" => bytes.Length >= 4 && bytes[..4].SequenceEqual("OggS"u8),
            ".png" => bytes.Length >= 8 &&
                      bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".webp" => bytes.Length >= 12 &&
                       bytes[..4].SequenceEqual("RIFF"u8) &&
                       bytes[8..12].SequenceEqual("WEBP"u8),
            _ => false,
        };
    }

    private static void EnsureUnderRoot(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw Invalid("media.path_escape", "A review media path escapes the configured root.");
        }
    }

    private static void RejectReparsePoint(string root, string path)
    {
        string current = Path.GetFullPath(path);
        while (true)
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw Invalid("media.reparse_point", "Review media cannot use a reparse point.");
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) return;
            string? parent = Path.GetDirectoryName(current);
            if (parent is null)
                throw Invalid("media.path_escape", "A review media path escapes the configured root.");
            current = parent;
        }
    }

    private static LipSyncReviewInputException Invalid(string code, string message) => new(code, message);

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

internal sealed record GuardedMedia(
    FileStream Stream,
    string ContentHash,
    string Path,
    string Extension,
    string ResponseId,
    LipSyncPlaybackKind? PlaybackKind,
    long DurationMilliseconds);
