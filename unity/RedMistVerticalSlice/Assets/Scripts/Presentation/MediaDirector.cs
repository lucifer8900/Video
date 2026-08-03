using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Lingmai.RedMist
{
    public enum MediaEndReason
    {
        Completed,
        Skipped,
        Missing,
        Error,
        Timeout,
        Busy
    }

    /// <summary>
    /// Plays approved local cinematics from StreamingAssets/Media. It never performs network
    /// requests and always prepares a clip before exposing its first frame to the UI.
    /// </summary>
    public sealed class MediaDirector : MonoBehaviour, IMediaDirectorPlaybackPort
    {
        private enum PlaybackState
        {
            Idle,
            Preparing,
            Playing
        }

        // Retained for compatibility with the first approved vertical-slice cue.
        private static readonly Dictionary<string, string> LegacyApprovedCueFiles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "prologue", "rmv_001_gate_dawn.mp4" }
            };

        [SerializeField, Min(1f)] private float prepareTimeoutSeconds = 10f;
        [SerializeField, Min(5f)] private float playbackGraceSeconds = 12f;

        private VideoPlayer _player;
        private AudioSource _audio;
        private RawImage _target;
        private AspectCropRawImage _aspectCrop;
        private RenderTexture _renderTexture;
        private PlaybackState _state;
        private Coroutine _prepareTimeoutRoutine;
        private Coroutine _playbackTimeoutRoutine;
        private Action<MediaEndReason> _finished;
        private int _tokenCounter;
        private int _activeToken;
        private bool _completionDelivered;
        private bool _retainCompletedFrame;
        private bool _hasRetainedFrame;

        private VideoPlayer.EventHandler _preparedHandler;
        private VideoPlayer.EventHandler _startedHandler;
        private VideoPlayer.EventHandler _loopHandler;
        private VideoPlayer.ErrorEventHandler _errorHandler;

        public bool IsBusy => _state != PlaybackState.Idle;
        public bool IsPreparing => _state == PlaybackState.Preparing;
        public bool IsPlaying => _state == PlaybackState.Playing && _player != null && _player.isPlaying;
        public bool HasRetainedFrame => _hasRetainedFrame && _renderTexture != null;
        public string ActiveCueId { get; private set; }

        /// <summary>Raised after a prepared clip changes the presentation texture dimensions.</summary>
        public event Action<int, int> SourceDimensionsChanged;

        public void Initialize(RawImage target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (IsBusy) Skip();

            _target = target;
            _aspectCrop = _target.GetComponent<AspectCropRawImage>();
            _player = _player != null ? _player : gameObject.AddComponent<VideoPlayer>();
            _audio = _audio != null ? _audio : gameObject.AddComponent<AudioSource>();

            _player.playOnAwake = false;
            _player.waitForFirstFrame = true;
            _player.skipOnDrop = true;
            _player.isLooping = false;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _player.SetTargetAudioSource(0, _audio);

            _audio.playOnAwake = false;
            EnsureRenderTexture(16, 9);
            _target.gameObject.SetActive(false);
            _state = PlaybackState.Idle;
        }

        /// <summary>
        /// Starts a catalog or local cue. The callback is invoked exactly once for every request,
        /// including missing files and requests rejected while another clip is busy.
        /// </summary>
        public bool Play(string cueId, Action<MediaEndReason> finished)
        {
            return BeginPlay(cueId, finished, true, false);
        }

        /// <summary>
        /// Plays a cue and, only after a natural completion, keeps its final rendered frame
        /// visible. The retained frame is not considered busy, so story choices can be layered
        /// over it. Call <see cref="ReleaseRetainedFrame"/> before leaving that story beat.
        /// Skips, missing files, decode errors, and timeouts never retain a frame.
        /// </summary>
        public bool Play(string cueId, Action<MediaEndReason> finished, bool retainCompletedFrame)
        {
            return BeginPlay(cueId, finished, true, retainCompletedFrame);
        }

        /// <summary>
        /// Plays a cache artifact only when it still matches the immutable verified handle. The
        /// cache root, local path and hash are never written to the log.
        /// </summary>
        public bool PlayVerified(
            VerifiedMediaHandle media,
            string cacheRoot,
            Action<MediaEndReason> finished)
        {
            if (!TryResolveVerifiedLocalPath(media, cacheRoot, out string path))
            {
                Debug.LogWarning("RED_MIST_GENERATED_MEDIA_REJECTED");
                finished?.Invoke(MediaEndReason.Error);
                return false;
            }

            if (_player == null || _target == null)
            {
                Debug.LogError("RED_MIST_VIDEO_NOT_INITIALIZED");
                finished?.Invoke(MediaEndReason.Error);
                return false;
            }

            if (IsBusy)
            {
                Debug.LogWarning("RED_MIST_VIDEO_BUSY active=" + ActiveCueId + " requested=generated");
                finished?.Invoke(MediaEndReason.Busy);
                return false;
            }

            ReleaseRetainedFrame();
            return StartResolvedPlayback("generated", path, finished, false, false);
        }

        /// <summary>
        /// Performs the same final integrity gate used immediately before generated playback.
        /// This method never accepts URLs, nested paths, renamed files or modified cache bytes.
        /// </summary>
        public static bool IsPlayableVerifiedMedia(VerifiedMediaHandle media, string cacheRoot)
        {
            return TryResolveVerifiedLocalPath(media, cacheRoot, out _);
        }

        /// <summary>
        /// Backward-compatible API. As before, false means that no playback was started; the
        /// legacy callback is only invoked after a request that successfully started preparing.
        /// </summary>
        public bool TryPlay(string cueId, Action finished = null)
        {
            return BeginPlay(cueId, _ => finished?.Invoke(), false, false);
        }

        public void SetVolume(float volume)
        {
            if (_audio != null) _audio.volume = Mathf.Clamp01(volume);
        }

        public void Skip()
        {
            if (!IsBusy) return;
            Complete(_activeToken, MediaEndReason.Skipped);
        }

        /// <summary>
        /// Releases the paused final frame and its GPU texture. Safe to call repeatedly while
        /// idle; an active playback is deliberately left alone and must be skipped explicitly.
        /// </summary>
        public void ReleaseRetainedFrame()
        {
            if (IsBusy || !_hasRetainedFrame) return;
            _hasRetainedFrame = false;
            if (_player != null)
            {
                _player.Stop();
                _player.url = string.Empty;
                _player.targetTexture = null;
            }
            if (_target != null)
            {
                _target.texture = null;
                _target.gameObject.SetActive(false);
            }
            ReleaseRenderTexture();
            Debug.Log("RED_MIST_VIDEO_RETAINED_FRAME_RELEASED");
        }

        private bool BeginPlay(string cueId, Action<MediaEndReason> finished, bool reportImmediateFailure,
            bool retainCompletedFrame)
        {
            if (_player == null || _target == null)
            {
                Debug.LogError("RED_MIST_VIDEO_NOT_INITIALIZED");
                if (reportImmediateFailure) finished?.Invoke(MediaEndReason.Error);
                return false;
            }

            if (IsBusy)
            {
                Debug.LogWarning("RED_MIST_VIDEO_BUSY active=" + ActiveCueId + " requested=" + cueId);
                if (reportImmediateFailure) finished?.Invoke(MediaEndReason.Busy);
                return false;
            }

            // Starting another presentation is an explicit boundary between story beats.
            // Drop the old decoder/RenderTexture before resolving or preparing the next cue.
            ReleaseRetainedFrame();

            if (!TryResolveLocalPath(cueId, out string path))
            {
                Debug.LogWarning("RED_MIST_VIDEO_MISSING cue=" + cueId);
                if (reportImmediateFailure) finished?.Invoke(MediaEndReason.Missing);
                return false;
            }

            return StartResolvedPlayback(
                cueId,
                path,
                finished,
                retainCompletedFrame,
                true);
        }

        private bool StartResolvedPlayback(
            string cueId,
            string path,
            Action<MediaEndReason> finished,
            bool retainCompletedFrame,
            bool includePathInLog)
        {
            int token = ++_tokenCounter;
            _activeToken = token;
            _completionDelivered = false;
            _retainCompletedFrame = retainCompletedFrame;
            _finished = finished;
            ActiveCueId = cueId;
            _state = PlaybackState.Preparing;
            _target.gameObject.SetActive(false);

            AttachRequestHandlers(token);
            _player.url = path;
            _prepareTimeoutRoutine = StartCoroutine(PrepareTimeout(token));
            _player.Prepare();
            Debug.Log(includePathInLog
                ? "RED_MIST_VIDEO_PREPARE cue=" + cueId + " path=" + path
                : "RED_MIST_VIDEO_PREPARE cue=generated source=verified_cache");
            return true;
        }

        private void AttachRequestHandlers(int token)
        {
            DetachRequestHandlers();
            _preparedHandler = source => OnPrepared(source, token);
            _startedHandler = source => OnStarted(source, token);
            _loopHandler = source => Complete(token, MediaEndReason.Completed);
            _errorHandler = (source, message) => OnError(token, message);
            _player.prepareCompleted += _preparedHandler;
            _player.started += _startedHandler;
            _player.loopPointReached += _loopHandler;
            _player.errorReceived += _errorHandler;
        }

        private void DetachRequestHandlers()
        {
            if (_player == null) return;
            if (_preparedHandler != null) _player.prepareCompleted -= _preparedHandler;
            if (_startedHandler != null) _player.started -= _startedHandler;
            if (_loopHandler != null) _player.loopPointReached -= _loopHandler;
            if (_errorHandler != null) _player.errorReceived -= _errorHandler;
            _preparedHandler = null;
            _startedHandler = null;
            _loopHandler = null;
            _errorHandler = null;
        }

        private void OnPrepared(VideoPlayer source, int token)
        {
            if (!IsCurrent(token) || _state != PlaybackState.Preparing) return;

            int width = SafeDimension(source.width, 1280);
            int height = SafeDimension(source.height, 720);
            EnsureRenderTexture(width, height);
            _player.targetTexture = _renderTexture;
            _target.texture = _renderTexture;
            _aspectCrop?.Refresh(true);
            NotifySourceDimensions(width, height);

            // Keep the RawImage hidden until VideoPlayer.started so the previous backdrop remains
            // visible throughout preparation instead of flashing the empty RenderTexture.
            _player.Play();
            Debug.Log("RED_MIST_VIDEO_PREPARED cue=" + ActiveCueId + " size=" + width + "x" + height);
        }

        private void OnStarted(VideoPlayer source, int token)
        {
            if (!IsCurrent(token) || _state != PlaybackState.Preparing) return;
            StopRoutine(ref _prepareTimeoutRoutine);
            _state = PlaybackState.Playing;
            _target.gameObject.SetActive(true);
            _aspectCrop?.Refresh(true);
            _playbackTimeoutRoutine = StartCoroutine(PlaybackTimeout(token, source.length));
            Debug.Log("RED_MIST_VIDEO_START cue=" + ActiveCueId);
        }

        private void OnError(int token, string message)
        {
            if (!IsCurrent(token)) return;
            // VideoPlayer errors may echo its URL. Keep the diagnostic categorical so generated
            // cache paths (and future transport details) never enter logs.
            Debug.LogWarning("RED_MIST_VIDEO_ERROR cue=" + ActiveCueId);
            Complete(token, MediaEndReason.Error);
        }

        private IEnumerator PrepareTimeout(int token)
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(1f, prepareTimeoutSeconds));
            if (!IsCurrent(token) || _state != PlaybackState.Preparing) yield break;
            _prepareTimeoutRoutine = null;
            Debug.LogWarning("RED_MIST_VIDEO_PREPARE_TIMEOUT cue=" + ActiveCueId);
            Complete(token, MediaEndReason.Timeout);
        }

        private IEnumerator PlaybackTimeout(int token, double clipLength)
        {
            float expected = clipLength > 0.01 && !double.IsInfinity(clipLength) && !double.IsNaN(clipLength)
                ? (float)Math.Min(clipLength, 21600d)
                : 600f;
            yield return new WaitForSecondsRealtime(Mathf.Max(30f, expected + playbackGraceSeconds));
            if (!IsCurrent(token) || _state != PlaybackState.Playing) yield break;
            _playbackTimeoutRoutine = null;
            Debug.LogWarning("RED_MIST_VIDEO_PLAYBACK_TIMEOUT cue=" + ActiveCueId);
            Complete(token, MediaEndReason.Timeout);
        }

        private void Complete(int token, MediaEndReason reason)
        {
            if (!IsCurrent(token) || _completionDelivered) return;
            _completionDelivered = true;

            StopRoutine(ref _prepareTimeoutRoutine);
            StopRoutine(ref _playbackTimeoutRoutine);
            DetachRequestHandlers();

            bool retainFrame = reason == MediaEndReason.Completed &&
                               _retainCompletedFrame &&
                               _renderTexture != null &&
                               _target != null &&
                               _target.gameObject.activeSelf;
            if (retainFrame)
            {
                // loopPointReached fires after the final decoded frame has reached the target
                // texture. Pause instead of Stop so platforms that clear/reseek on Stop keep the
                // exact frame the viewer just saw.
                _player.Pause();
                _target.texture = _renderTexture;
                _target.gameObject.SetActive(true);
                _hasRetainedFrame = true;
            }
            else
            {
                _player.Stop();
                _player.url = string.Empty;
                _player.targetTexture = null;
                _target.texture = null;
                _target.gameObject.SetActive(false);
                _hasRetainedFrame = false;
                ReleaseRenderTexture();
            }

            Action<MediaEndReason> callback = _finished;
            string completedCue = ActiveCueId;
            _finished = null;
            _retainCompletedFrame = false;
            ActiveCueId = null;
            _state = PlaybackState.Idle;
            _activeToken = 0;

            Debug.Log("RED_MIST_VIDEO_COMPLETE cue=" + completedCue + " reason=" + reason +
                      " retained=" + retainFrame);
            callback?.Invoke(reason);
        }

        private bool IsCurrent(int token)
        {
            return token != 0 && token == _activeToken && _state != PlaybackState.Idle;
        }

        private void EnsureRenderTexture(int width, int height)
        {
            width = Mathf.Max(1, width);
            height = Mathf.Max(1, height);
            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
            {
                _player.targetTexture = _renderTexture;
                if (_target != null) _target.texture = _renderTexture;
                return;
            }

            if (_player != null) _player.targetTexture = null;
            if (_target != null) _target.texture = null;
            ReleaseRenderTexture();

            _renderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "CinematicVideoRT_" + width + "x" + height,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _renderTexture.Create();
            if (_player != null) _player.targetTexture = _renderTexture;
            if (_target != null) _target.texture = _renderTexture;
        }

        private void NotifySourceDimensions(int width, int height)
        {
            try
            {
                SourceDimensionsChanged?.Invoke(width, height);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static int SafeDimension(uint value, int fallback)
        {
            return value == 0 || value > 8192 ? fallback : (int)value;
        }

        private static bool TryResolveLocalPath(string cueId, out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(cueId)) return false;
            try
            {
                string fileName;
                if (CinematicCatalog.TryGetCue(cueId, out CinematicCueDefinition cue))
                    fileName = cue.FileName;
                else if (!LegacyApprovedCueFiles.TryGetValue(cueId, out fileName))
                    fileName = cueId.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? cueId : cueId + ".mp4";

                if (!IsSafeMediaFileName(fileName)) return false;
                string mediaRoot = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "Media"));
                string candidate = Path.GetFullPath(Path.Combine(mediaRoot, fileName));
                if (!IsInsideMediaRoot(mediaRoot, candidate)) return false;

                // The original prologue cue keeps its old same-id fallback.
                if (!File.Exists(candidate) && LegacyApprovedCueFiles.ContainsKey(cueId))
                {
                    string fallback = Path.GetFullPath(Path.Combine(mediaRoot, cueId + ".mp4"));
                    if (IsInsideMediaRoot(mediaRoot, fallback) && File.Exists(fallback)) candidate = fallback;
                }

                if (!File.Exists(candidate)) return false;
                path = candidate;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                              exception is NotSupportedException ||
                                              exception is PathTooLongException)
            {
                Debug.LogWarning("RED_MIST_VIDEO_PATH_REJECTED cue=" + cueId + " message=" + exception.Message);
                return false;
            }
        }

        private static bool TryResolveVerifiedLocalPath(
            VerifiedMediaHandle media,
            string cacheRoot,
            out string path)
        {
            path = null;
            if (media == null || string.IsNullOrWhiteSpace(cacheRoot) ||
                !Path.IsPathRooted(cacheRoot) || !Path.IsPathRooted(media.LocalPath) ||
                !GenerationHashValidation.IsSha256(media.ContentHash))
            {
                return false;
            }

            try
            {
                string root = Path.GetFullPath(cacheRoot).TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                string volumeRoot = Path.GetPathRoot(root)?.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
                StringComparison pathComparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(volumeRoot) ||
                    string.Equals(root, volumeRoot, pathComparison) || !Directory.Exists(root))
                {
                    return false;
                }

                string expectedName = media.ContentHash.Substring(7) + ".mp4";
                string expectedPath = Path.GetFullPath(Path.Combine(root, expectedName));
                string candidate = Path.GetFullPath(media.LocalPath);
                if (!string.Equals(candidate, expectedPath, pathComparison) ||
                    !string.Equals(Path.GetFileName(candidate), expectedName, StringComparison.Ordinal) ||
                    !File.Exists(candidate))
                {
                    return false;
                }

                FileAttributes rootAttributes = File.GetAttributes(root);
                FileAttributes fileAttributes = File.GetAttributes(candidate);
                if ((rootAttributes & FileAttributes.ReparsePoint) != 0 ||
                    (fileAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                var file = new FileInfo(candidate);
                if (file.Length != media.Length) return false;
                if (!string.Equals(HashFile(candidate), media.ContentHash, StringComparison.Ordinal))
                    return false;

                path = candidate;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException ||
                                              exception is IOException ||
                                              exception is NotSupportedException ||
                                              exception is PathTooLongException ||
                                              exception is UnauthorizedAccessException ||
                                              exception is System.Security.SecurityException)
            {
                return false;
            }
        }

        private static string HashFile(string path)
        {
            using (SHA256 algorithm = SHA256.Create())
            using (FileStream stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       81920,
                       FileOptions.SequentialScan))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                const string alphabet = "0123456789abcdef";
                var characters = new char[hash.Length * 2];
                for (int index = 0; index < hash.Length; index++)
                {
                    characters[index * 2] = alphabet[hash[index] >> 4];
                    characters[index * 2 + 1] = alphabet[hash[index] & 15];
                }
                return "sha256:" + new string(characters);
            }
        }

        private static bool IsSafeMediaFileName(string fileName)
        {
            return !string.IsNullOrWhiteSpace(fileName) &&
                   string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) &&
                   fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInsideMediaRoot(string mediaRoot, string candidate)
        {
            string prefix = mediaRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                            Path.DirectorySeparatorChar;
            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine == null) return;
            StopCoroutine(routine);
            routine = null;
        }

        private void ReleaseRenderTexture()
        {
            if (_renderTexture == null) return;
            if (_renderTexture.IsCreated()) _renderTexture.Release();
            Destroy(_renderTexture);
            _renderTexture = null;
        }

        private void OnDestroy()
        {
            StopRoutine(ref _prepareTimeoutRoutine);
            StopRoutine(ref _playbackTimeoutRoutine);
            DetachRequestHandlers();
            if (_player != null)
            {
                _player.Stop();
                _player.targetTexture = null;
            }
            if (_target != null) _target.texture = null;
            _hasRetainedFrame = false;
            ReleaseRenderTexture();
        }
    }
}
