using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lingmai.RedMist
{
    /// <summary>
    /// Narrow port implemented by MediaDirector so the generation adapter stays independently
    /// testable without constructing Unity video/audio resources in EditMode.
    /// </summary>
    public interface IMediaDirectorPlaybackPort
    {
        bool PlayVerified(
            VerifiedMediaHandle media,
            string cacheRoot,
            Action<MediaEndReason> finished);

        bool Play(string cueId, Action<MediaEndReason> finished);

        void Skip();
    }

    /// <summary>
    /// Maps the pure generation coordinator playback contract onto MediaDirector. It accepts only
    /// verified local cache artifacts; it never accepts a URL or performs a network request.
    /// </summary>
    public sealed class MediaDirectorPlaybackGateway : IMediaPlaybackGateway
    {
        private readonly IMediaDirectorPlaybackPort _director;
        private readonly string _cacheRoot;
        private readonly SynchronizationContext _unityContext;
        private readonly int _unityThreadId;

        public MediaDirectorPlaybackGateway(
            IMediaDirectorPlaybackPort director,
            string cacheRoot)
        {
            _director = director ?? throw new ArgumentNullException(nameof(director));
            if (string.IsNullOrWhiteSpace(cacheRoot) || !Path.IsPathRooted(cacheRoot))
                throw new ArgumentException("An absolute generated-media cache root is required.", nameof(cacheRoot));

            try
            {
                _cacheRoot = Path.GetFullPath(cacheRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                              exception is NotSupportedException ||
                                              exception is PathTooLongException)
            {
                throw new ArgumentException("The generated-media cache root is invalid.", nameof(cacheRoot));
            }

            if (!Directory.Exists(_cacheRoot))
                throw new ArgumentException("The generated-media cache root does not exist.", nameof(cacheRoot));
            _unityContext = SynchronizationContext.Current;
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public Task<MediaPlaybackCompletion> PlayGeneratedAsync(
            VerifiedMediaHandle media,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<MediaPlaybackCompletion>(cancellationToken);
            if (!MediaDirector.IsPlayableVerifiedMedia(media, _cacheRoot))
                return Task.FromResult(MediaPlaybackCompletion.Failed);

            return BeginPlayback(
                callback => _director.PlayVerified(media, _cacheRoot, callback),
                cancellationToken);
        }

        public Task<MediaPlaybackCompletion> PlayFallbackAsync(
            string fallbackCueId,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<MediaPlaybackCompletion>(cancellationToken);
            if (string.IsNullOrWhiteSpace(fallbackCueId) || fallbackCueId.Length > 256)
                return Task.FromResult(MediaPlaybackCompletion.Failed);

            return BeginPlayback(
                callback => _director.Play(fallbackCueId, callback),
                cancellationToken);
        }

        private Task<MediaPlaybackCompletion> BeginPlayback(
            Func<Action<MediaEndReason>, bool> start,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<MediaPlaybackCompletion>();
            CancellationTokenRegistration registration = default(CancellationTokenRegistration);

            Action<MediaEndReason> finished = reason =>
            {
                if (completion.TrySetResult(Map(reason))) registration.Dispose();
            };

            bool accepted;
            try
            {
                accepted = start(finished);
            }
            catch (Exception)
            {
                completion.TrySetResult(MediaPlaybackCompletion.Failed);
                return completion.Task;
            }

            if (!accepted)
            {
                completion.TrySetResult(MediaPlaybackCompletion.Failed);
                return completion.Task;
            }

            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    if (!completion.TrySetCanceled()) return;
                    RequestSkip();
                });
                if (completion.Task.IsCompleted) registration.Dispose();
            }

            return completion.Task;
        }

        private void RequestSkip()
        {
            if (Thread.CurrentThread.ManagedThreadId == _unityThreadId)
            {
                SafeSkip();
                return;
            }

            // Unity objects cannot be touched from an arbitrary cancellation callback. A gateway
            // constructed on the Unity thread normally captures UnitySynchronizationContext; if
            // no context exists, leave the already-cancelled task final instead of calling Unity
            // from the wrong thread.
            _unityContext?.Post(_ => SafeSkip(), null);
        }

        private void SafeSkip()
        {
            try
            {
                _director.Skip();
            }
            catch (Exception)
            {
                // Cancellation is already final. Never leak decoder or path details from cleanup.
            }
        }

        private static MediaPlaybackCompletion Map(MediaEndReason reason)
        {
            switch (reason)
            {
                case MediaEndReason.Completed:
                    return MediaPlaybackCompletion.Completed;
                case MediaEndReason.Skipped:
                    return MediaPlaybackCompletion.Skipped;
                case MediaEndReason.Timeout:
                    return MediaPlaybackCompletion.TimedOut;
                case MediaEndReason.Busy:
                    return MediaPlaybackCompletion.Busy;
                default:
                    return MediaPlaybackCompletion.Failed;
            }
        }
    }
}
