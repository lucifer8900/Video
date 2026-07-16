using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    public enum GenerationJobState
    {
        Created,
        Queued,
        Generating,
        Moderating,
        Transcoding,
        Ready,
        Failed,
        Expired
    }

    public enum GenerationTransportFailure
    {
        NetworkUnavailable,
        ServerRejected,
        InvalidResponse
    }

    public enum MediaPlaybackCompletion
    {
        Completed,
        Skipped,
        Failed,
        TimedOut,
        Busy
    }

    public enum GenerationPlaybackOutcome
    {
        GeneratedMediaPlayed,
        FallbackPlayed,
        FallbackUnavailable
    }

    public enum GenerationPlaybackFailure
    {
        None,
        OnlineGenerationDisabled,
        TotalTimeout,
        ModerationRejected,
        JobFailed,
        JobExpired,
        IdempotencyConflict,
        NetworkUnavailable,
        ServerRejected,
        InvalidResponse,
        DownloadTicketExpired,
        CacheFailure,
        GeneratedPlaybackFailure
    }

    /// <summary>
    /// Immutable input to one generated-media playback attempt. Online generation is disabled
    /// by default so merely constructing the runtime cannot make a network or paid-provider call.
    /// </summary>
    public sealed class GenerationPlaybackRequest
    {
        public GenerationPlaybackRequest(
            string idempotencyKey,
            string inputHash,
            string fallbackCueId,
            bool onlineGenerationEnabled = false,
            double totalTimeoutSeconds = 30d,
            double pollIntervalSeconds = 1d)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 256)
                throw new ArgumentException("A bounded idempotency key is required.", nameof(idempotencyKey));
            if (!GenerationHashValidation.IsSha256(inputHash))
                throw new ArgumentException("The input hash must be a canonical SHA-256 value.", nameof(inputHash));
            if (string.IsNullOrWhiteSpace(fallbackCueId) || fallbackCueId.Length > 256)
                throw new ArgumentException("An approved fallback cue is required.", nameof(fallbackCueId));
            if (double.IsNaN(totalTimeoutSeconds) || double.IsInfinity(totalTimeoutSeconds) ||
                totalTimeoutSeconds < 1d || totalTimeoutSeconds > 900d)
            {
                throw new ArgumentOutOfRangeException(nameof(totalTimeoutSeconds));
            }
            if (double.IsNaN(pollIntervalSeconds) || double.IsInfinity(pollIntervalSeconds) ||
                pollIntervalSeconds <= 0d || pollIntervalSeconds > 60d ||
                pollIntervalSeconds > totalTimeoutSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(pollIntervalSeconds));
            }

            IdempotencyKey = idempotencyKey;
            InputHash = inputHash;
            FallbackCueId = fallbackCueId;
            OnlineGenerationEnabled = onlineGenerationEnabled;
            TotalTimeoutSeconds = totalTimeoutSeconds;
            PollIntervalSeconds = pollIntervalSeconds;
        }

        public string IdempotencyKey { get; }
        public string InputHash { get; }
        public string FallbackCueId { get; }
        public bool OnlineGenerationEnabled { get; }
        public double TotalTimeoutSeconds { get; }
        public double PollIntervalSeconds { get; }

        public override string ToString()
        {
            return "GenerationPlaybackRequest(OnlineEnabled=" + OnlineGenerationEnabled + ")";
        }
    }

    public sealed class GenerationJobSnapshot
    {
        public GenerationJobSnapshot(
            string jobId,
            GenerationJobState state,
            string failureCode = null)
        {
            if (string.IsNullOrWhiteSpace(jobId) || jobId.Length > 256)
                throw new ArgumentException("A bounded generation job ID is required.", nameof(jobId));
            if (!Enum.IsDefined(typeof(GenerationJobState), state))
                throw new ArgumentOutOfRangeException(nameof(state));
            if (failureCode != null && failureCode.Length > 256)
                throw new ArgumentException("The failure code is too long.", nameof(failureCode));

            JobId = jobId;
            State = state;
            FailureCode = failureCode ?? string.Empty;
        }

        public string JobId { get; }
        public GenerationJobState State { get; }
        public string FailureCode { get; }
    }

    public sealed class GenerationDownloadTicket
    {
        public GenerationDownloadTicket(
            string mediaId,
            string contentHash,
            string downloadUrl,
            DateTimeOffset expiresAtUtc,
            string contentType,
            long length)
        {
            if (string.IsNullOrWhiteSpace(mediaId) || mediaId.Length > 256)
                throw new ArgumentException("A bounded media ID is required.", nameof(mediaId));
            if (!GenerationHashValidation.IsSha256(contentHash))
                throw new ArgumentException("The media hash must be a canonical SHA-256 value.", nameof(contentHash));
            if (string.IsNullOrWhiteSpace(downloadUrl) || downloadUrl.Length > 8192)
                throw new ArgumentException("A bounded download URL is required.", nameof(downloadUrl));
            if (expiresAtUtc == default(DateTimeOffset))
                throw new ArgumentException("A ticket expiry is required.", nameof(expiresAtUtc));
            if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128)
                throw new ArgumentException("A bounded content type is required.", nameof(contentType));
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            MediaId = mediaId;
            ContentHash = contentHash;
            DownloadUrl = downloadUrl;
            ExpiresAtUtc = expiresAtUtc;
            ContentType = contentType;
            Length = length;
        }

        public string MediaId { get; }
        public string ContentHash { get; }
        public string DownloadUrl { get; }
        public DateTimeOffset ExpiresAtUtc { get; }
        public string ContentType { get; }
        public long Length { get; }

        public override string ToString()
        {
            return "GenerationDownloadTicket(MediaId=" + MediaId + ", ExpiresAtUtc=" +
                   ExpiresAtUtc.ToString("O") + ")";
        }
    }

    /// <summary>
    /// A cache implementation may construct this handle only after length and SHA-256 checks
    /// succeed and its temporary file has been atomically committed.
    /// </summary>
    public sealed class VerifiedMediaHandle
    {
        public VerifiedMediaHandle(string contentHash, string localPath, long length)
        {
            if (!GenerationHashValidation.IsSha256(contentHash))
                throw new ArgumentException("The verified media hash is invalid.", nameof(contentHash));
            if (string.IsNullOrWhiteSpace(localPath))
                throw new ArgumentException("A verified local path is required.", nameof(localPath));
            if (length <= 0)
                throw new ArgumentOutOfRangeException(nameof(length));

            ContentHash = contentHash;
            LocalPath = localPath;
            Length = length;
        }

        public string ContentHash { get; }
        public string LocalPath { get; }
        public long Length { get; }

        public override string ToString()
        {
            return "VerifiedMediaHandle(ContentHash=" + ContentHash + ", Length=" + Length + ")";
        }
    }

    public sealed class GenerationPlaybackResult
    {
        internal GenerationPlaybackResult(
            GenerationPlaybackOutcome outcome,
            GenerationPlaybackFailure failure,
            string jobId,
            MediaPlaybackCompletion playbackCompletion)
        {
            Outcome = outcome;
            Failure = failure;
            JobId = jobId ?? string.Empty;
            PlaybackCompletion = playbackCompletion;
        }

        public GenerationPlaybackOutcome Outcome { get; }
        public GenerationPlaybackFailure Failure { get; }
        public string JobId { get; }
        public MediaPlaybackCompletion PlaybackCompletion { get; }
    }

    public sealed class GenerationTransportException : Exception
    {
        public GenerationTransportException(GenerationTransportFailure failure)
            : base("The generation transport could not complete the request.")
        {
            if (!Enum.IsDefined(typeof(GenerationTransportFailure), failure))
                throw new ArgumentOutOfRangeException(nameof(failure));
            Failure = failure;
        }

        public GenerationTransportFailure Failure { get; }
    }

    public interface IGenerationJobGateway
    {
        Task<GenerationJobSnapshot> CreateOrGetAsync(
            GenerationPlaybackRequest request,
            CancellationToken cancellationToken);

        Task<GenerationJobSnapshot> GetAsync(
            string jobId,
            CancellationToken cancellationToken);

        Task<GenerationDownloadTicket> CreateDownloadTicketAsync(
            string jobId,
            CancellationToken cancellationToken);
    }

    public interface IVerifiedMediaCache
    {
        Task<VerifiedMediaHandle> GetOrDownloadAsync(
            GenerationDownloadTicket ticket,
            CancellationToken cancellationToken);
    }

    public interface IMediaPlaybackGateway
    {
        Task<MediaPlaybackCompletion> PlayGeneratedAsync(
            VerifiedMediaHandle media,
            CancellationToken cancellationToken);

        Task<MediaPlaybackCompletion> PlayFallbackAsync(
            string fallbackCueId,
            CancellationToken cancellationToken);
    }

    public interface IGenerationFlowClock
    {
        double MonotonicSeconds { get; }
        DateTimeOffset UtcNow { get; }
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    internal static class GenerationHashValidation
    {
        public static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 71 ||
                !value.StartsWith("sha256:", StringComparison.Ordinal))
            {
                return false;
            }

            for (int index = 7; index < value.Length; index++)
            {
                char character = value[index];
                bool decimalDigit = character >= '0' && character <= '9';
                bool lowerHex = character >= 'a' && character <= 'f';
                if (!decimalDigit && !lowerHex) return false;
            }
            return true;
        }
    }
}
