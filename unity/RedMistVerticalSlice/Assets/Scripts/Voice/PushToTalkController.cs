using System;
using System.Collections.Generic;

namespace Lingmai.RedMist
{
    public sealed class PushToTalkController
    {
        private readonly IVoicePermissionGateway _permission;
        private readonly IVoiceCaptureGateway _capture;
        private readonly IVoiceClock _clock;
        private readonly VoiceCaptureOptions _options;

        private int _generation;
        private string _deviceId;
        private double _sessionStartedAt;
        private double _lastLevelSampleAt;
        private bool _captureActive;

        public PushToTalkController(
            IVoicePermissionGateway permission,
            IVoiceCaptureGateway capture,
            IVoiceClock clock,
            VoiceCaptureOptions options)
        {
            _permission = permission ?? throw new ArgumentNullException(nameof(permission));
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            Current = new VoiceCaptureUpdate(VoiceCaptureState.Idle, VoiceCaptureError.None);
        }

        public VoiceCaptureUpdate Current { get; private set; }

        public event Action<VoiceCaptureUpdate> Changed;
        public event Action<VoiceAudioPayload> Captured;

        public VoiceCaptureUpdate Press()
        {
            if (Current.State == VoiceCaptureState.AwaitingPermission ||
                Current.State == VoiceCaptureState.Recording)
            {
                return Current;
            }

            int generation = ++_generation;
            _deviceId = null;
            _captureActive = false;

            if (!TryReadClock(out _sessionStartedAt))
                return Fail(VoiceCaptureError.StartFailed);

            VoicePermissionStatus permission;
            try
            {
                permission = _permission.Current;
            }
            catch
            {
                return Fail(VoiceCaptureError.StartFailed);
            }

            if (permission == VoicePermissionStatus.Denied)
                return Fail(VoiceCaptureError.PermissionDenied);

            if (permission == VoicePermissionStatus.Granted)
                return StartRecording(generation);

            Publish(new VoiceCaptureUpdate(VoiceCaptureState.AwaitingPermission, VoiceCaptureError.None));
            try
            {
                _permission.Request(status => CompletePermission(generation, status));
            }
            catch
            {
                if (generation == _generation && Current.State == VoiceCaptureState.AwaitingPermission)
                    return Fail(VoiceCaptureError.StartFailed);
            }

            return Current;
        }

        public VoiceCaptureUpdate Tick()
        {
            if (Current.State != VoiceCaptureState.AwaitingPermission &&
                Current.State != VoiceCaptureState.Recording)
            {
                return Current;
            }

            if (!TryReadClock(out double now))
            {
                CancelCaptureSafely();
                return Fail(VoiceCaptureError.CaptureFailed);
            }

            double timeout = Current.State == VoiceCaptureState.AwaitingPermission
                ? _options.PermissionTimeoutSeconds
                : _options.MaxDurationSeconds;
            if (now - _sessionStartedAt >= timeout)
            {
                InvalidateSession();
                CancelCaptureSafely();
                return Fail(VoiceCaptureError.Timeout);
            }

            if (Current.State == VoiceCaptureState.AwaitingPermission)
                return Current;

            try
            {
                if (!ContainsDevice(_capture.DeviceIds, _deviceId) || !_capture.IsRecording(_deviceId))
                {
                    InvalidateSession();
                    CancelCaptureSafely();
                    return Fail(VoiceCaptureError.DeviceDisconnected);
                }

                float level = Current.Level01;
                if (_options.LevelSampleIntervalSeconds <= 0d ||
                    now - _lastLevelSampleAt >= _options.LevelSampleIntervalSeconds ||
                    _lastLevelSampleAt == _sessionStartedAt)
                {
                    level = ClampLevel(_capture.ReadLevel01(_deviceId));
                    _lastLevelSampleAt = now;
                }

                Publish(new VoiceCaptureUpdate(VoiceCaptureState.Recording, VoiceCaptureError.None, null, level));
                return Current;
            }
            catch
            {
                InvalidateSession();
                CancelCaptureSafely();
                return Fail(VoiceCaptureError.CaptureFailed);
            }
        }

        public VoiceCaptureUpdate Release()
        {
            if (Current.State == VoiceCaptureState.Completed || Current.State == VoiceCaptureState.Fallback)
                return Current;

            if (Current.State == VoiceCaptureState.AwaitingPermission)
            {
                InvalidateSession();
                return Fail(VoiceCaptureError.Cancelled);
            }

            if (Current.State != VoiceCaptureState.Recording)
                return Fail(VoiceCaptureError.Cancelled);

            VoiceAudioPayload audio;
            try
            {
                audio = _capture.Stop(_deviceId);
                _captureActive = false;
            }
            catch
            {
                CancelCaptureSafely();
                InvalidateSession();
                return Fail(VoiceCaptureError.CaptureFailed);
            }

            InvalidateSession();
            _deviceId = null;
            if (IsSilent(audio, _options.SilenceRmsThreshold))
                return Fail(VoiceCaptureError.Silence);

            Publish(new VoiceCaptureUpdate(VoiceCaptureState.Completed, VoiceCaptureError.None, audio));
            NotifyCaptured(audio);
            return Current;
        }

        public VoiceCaptureUpdate Cancel()
        {
            if (Current.State == VoiceCaptureState.Completed || Current.State == VoiceCaptureState.Fallback)
                return Current;

            InvalidateSession();
            bool cancelled = CancelCaptureSafely();
            return Fail(cancelled ? VoiceCaptureError.Cancelled : VoiceCaptureError.CaptureFailed);
        }

        private void CompletePermission(int generation, VoicePermissionStatus status)
        {
            if (generation != _generation || Current.State != VoiceCaptureState.AwaitingPermission)
                return;

            if (status != VoicePermissionStatus.Granted)
            {
                Fail(VoiceCaptureError.PermissionDenied);
                return;
            }

            StartRecording(generation);
        }

        private VoiceCaptureUpdate StartRecording(int generation)
        {
            if (generation != _generation)
                return Current;

            try
            {
                IReadOnlyList<string> devices = _capture.DeviceIds;
                _deviceId = FirstUsableDevice(devices);
                if (string.IsNullOrEmpty(_deviceId))
                    return Fail(VoiceCaptureError.NoMicrophone);

                int maxSeconds = Math.Max(1, (int)Math.Ceiling(_options.MaxDurationSeconds));
                if (!_capture.TryStart(_deviceId, _options.SampleRateHz, maxSeconds))
                {
                    _deviceId = null;
                    return Fail(VoiceCaptureError.StartFailed);
                }

                _captureActive = true;
                if (!TryReadClock(out _sessionStartedAt))
                {
                    CancelCaptureSafely();
                    return Fail(VoiceCaptureError.StartFailed);
                }

                _lastLevelSampleAt = _sessionStartedAt;
                Publish(new VoiceCaptureUpdate(VoiceCaptureState.Recording, VoiceCaptureError.None));
                return Current;
            }
            catch
            {
                CancelCaptureSafely();
                _deviceId = null;
                return Fail(VoiceCaptureError.StartFailed);
            }
        }

        private VoiceCaptureUpdate Fail(VoiceCaptureError error)
        {
            _captureActive = false;
            _deviceId = null;
            Publish(new VoiceCaptureUpdate(VoiceCaptureState.Fallback, error));
            return Current;
        }

        private bool CancelCaptureSafely()
        {
            if (!_captureActive || string.IsNullOrEmpty(_deviceId))
            {
                _captureActive = false;
                _deviceId = null;
                return true;
            }

            string deviceId = _deviceId;
            _captureActive = false;
            _deviceId = null;
            try
            {
                _capture.Cancel(deviceId);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void InvalidateSession()
        {
            _generation++;
        }

        private bool TryReadClock(out double now)
        {
            try
            {
                now = _clock.NowSeconds;
                return !double.IsNaN(now) && !double.IsInfinity(now);
            }
            catch
            {
                now = 0d;
                return false;
            }
        }

        private void Publish(VoiceCaptureUpdate update)
        {
            Current = update;
            Action<VoiceCaptureUpdate> changed = Changed;
            if (changed == null) return;

            foreach (Action<VoiceCaptureUpdate> handler in changed.GetInvocationList())
            {
                try
                {
                    handler(update);
                }
                catch
                {
                    // UI observers must not be able to break the capture state machine.
                }
            }
        }

        private void NotifyCaptured(VoiceAudioPayload audio)
        {
            Action<VoiceAudioPayload> captured = Captured;
            if (captured == null) return;

            foreach (Action<VoiceAudioPayload> handler in captured.GetInvocationList())
            {
                try
                {
                    handler(audio);
                }
                catch
                {
                    // The ASR consumer is an optional enhancement and cannot block fixed choices.
                }
            }
        }

        private static string FirstUsableDevice(IReadOnlyList<string> devices)
        {
            if (devices == null) return null;
            for (int index = 0; index < devices.Count; index++)
            {
                if (!string.IsNullOrWhiteSpace(devices[index])) return devices[index];
            }
            return null;
        }

        private static bool ContainsDevice(IReadOnlyList<string> devices, string expected)
        {
            if (devices == null || string.IsNullOrEmpty(expected)) return false;
            for (int index = 0; index < devices.Count; index++)
            {
                if (string.Equals(devices[index], expected, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static float ClampLevel(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0f;
            return value >= 1f ? 1f : value;
        }

        private static bool IsSilent(VoiceAudioPayload audio, float threshold)
        {
            if (audio == null || audio.Samples == null || audio.Samples.Length == 0) return true;

            double sumSquares = 0d;
            for (int index = 0; index < audio.Samples.Length; index++)
            {
                float sample = audio.Samples[index];
                if (float.IsNaN(sample) || float.IsInfinity(sample)) continue;
                sumSquares += (double)sample * sample;
            }

            double rms = Math.Sqrt(sumSquares / audio.Samples.Length);
            return rms < threshold;
        }
    }
}
