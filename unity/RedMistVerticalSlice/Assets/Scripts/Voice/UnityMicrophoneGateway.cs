using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lingmai.RedMist
{
    public sealed class UnityRealtimeClock : IVoiceClock
    {
        public double NowSeconds => Time.realtimeSinceStartup;
    }

    /// <summary>
    /// Unity documents its UserAuthorization microphone flow for iOS and WebGL. The Editor and
    /// desktop players use device discovery/start failure as their permission signal instead.
    /// </summary>
    public sealed class UnityVoicePermissionGateway : IVoicePermissionGateway
    {
#if !UNITY_EDITOR && (UNITY_IOS || UNITY_WEBGL)
        private VoicePermissionStatus _current = VoicePermissionStatus.Unknown;

        public VoicePermissionStatus Current
        {
            get
            {
                if (Application.HasUserAuthorization(UserAuthorization.Microphone))
                    _current = VoicePermissionStatus.Granted;
                return _current;
            }
        }

        public void Request(Action<VoicePermissionStatus> completed)
        {
            if (Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Complete(VoicePermissionStatus.Granted, completed);
                return;
            }

            AsyncOperation request;
            try
            {
                request = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            }
            catch
            {
                Complete(VoicePermissionStatus.Denied, completed);
                return;
            }

            if (request == null)
            {
                Complete(VoicePermissionStatus.Denied, completed);
                return;
            }

            bool delivered = false;
            Action deliver = () =>
            {
                if (delivered) return;
                delivered = true;
                Complete(
                    Application.HasUserAuthorization(UserAuthorization.Microphone)
                        ? VoicePermissionStatus.Granted
                        : VoicePermissionStatus.Denied,
                    completed);
            };

            if (request.isDone)
            {
                deliver();
            }
            else
            {
                request.completed += _ => deliver();
            }
        }

        private void Complete(VoicePermissionStatus status, Action<VoicePermissionStatus> completed)
        {
            _current = status;
            completed?.Invoke(status);
        }
#else
        public VoicePermissionStatus Current => VoicePermissionStatus.Granted;

        public void Request(Action<VoicePermissionStatus> completed)
        {
            completed?.Invoke(VoicePermissionStatus.Granted);
        }
#endif
    }

    /// <summary>
    /// Thin owner of Unity's native microphone session. Exactly one explicitly named device is
    /// locked for a session, and every exit path releases the native recording AudioClip.
    /// </summary>
    public sealed class UnityMicrophoneGateway : IVoiceCaptureGateway, IDisposable
    {
        private const double DeviceCacheSeconds = 0.25d;
        private const int LevelWindowFrames = 256;

        private IReadOnlyList<string> _cachedDevices = Array.Empty<string>();
        private double _deviceCacheUpdatedAt = double.NegativeInfinity;
        private string _activeDeviceId;
        private AudioClip _recordingClip;
        private float[] _levelSamples = Array.Empty<float>();

        public IReadOnlyList<string> DeviceIds
        {
            get
            {
                RefreshDevices(false);
                return _cachedDevices;
            }
        }

        public bool TryStart(string deviceId, int sampleRateHz, int maxSeconds)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || sampleRateHz <= 0 || maxSeconds <= 0)
                return false;
            if (_recordingClip != null)
                return false;

            RefreshDevices(true);
            if (!ContainsDevice(_cachedDevices, deviceId))
                return false;

            int actualSampleRate = ChooseSupportedSampleRate(deviceId, sampleRateHz);
            AudioClip clip = null;
            try
            {
                clip = Microphone.Start(deviceId, false, maxSeconds, actualSampleRate);
                if (clip == null) return false;

                _activeDeviceId = deviceId;
                _recordingClip = clip;
                _levelSamples = Array.Empty<float>();
                return true;
            }
            catch
            {
                if (clip != null) UnityEngine.Object.Destroy(clip);
                _activeDeviceId = null;
                _recordingClip = null;
                _levelSamples = Array.Empty<float>();
                return false;
            }
        }

        public bool IsRecording(string deviceId)
        {
            if (_recordingClip == null ||
                !string.Equals(deviceId, _activeDeviceId, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                return Microphone.IsRecording(_activeDeviceId);
            }
            catch
            {
                return false;
            }
        }

        public float ReadLevel01(string deviceId)
        {
            AudioClip clip = _recordingClip;
            if (clip == null ||
                !string.Equals(deviceId, _activeDeviceId, StringComparison.Ordinal))
            {
                return 0f;
            }

            try
            {
                if (!Microphone.IsRecording(_activeDeviceId)) return 0f;
                int position = Microphone.GetPosition(_activeDeviceId);
                if (position <= 0) return 0f;

                int frames = Math.Min(LevelWindowFrames, Math.Min(position, clip.samples));
                int channels = Math.Max(1, clip.channels);
                int sampleCount = checked(frames * channels);
                if (_levelSamples.Length != sampleCount)
                    _levelSamples = new float[sampleCount];

                int offsetFrames = Math.Max(0, position - frames);
                if (!clip.GetData(_levelSamples, offsetFrames)) return 0f;

                double sumOfSquares = 0d;
                for (int index = 0; index < sampleCount; index++)
                {
                    double sample = _levelSamples[index];
                    sumOfSquares += sample * sample;
                }

                return Mathf.Clamp01((float)Math.Sqrt(sumOfSquares / sampleCount));
            }
            catch
            {
                return 0f;
            }
        }

        public VoiceAudioPayload Stop(string deviceId)
        {
            AudioClip clip = _recordingClip;
            string lockedDeviceId = _activeDeviceId;
            if (clip == null || string.IsNullOrEmpty(lockedDeviceId))
                return EmptyPayload();

            VoiceAudioPayload payload = EmptyPayload();
            try
            {
                // Position and PCM are captured before End because Unity resets the native record
                // cursor when the device is stopped.
                int recordedFrames = Microphone.GetPosition(lockedDeviceId);
                recordedFrames = Math.Max(0, Math.Min(recordedFrames, clip.samples));
                int channels = Math.Max(1, clip.channels);
                int sampleRate = Math.Max(1, clip.frequency);
                if (recordedFrames > 0)
                {
                    float[] samples = new float[checked(recordedFrames * channels)];
                    if (clip.GetData(samples, 0))
                        payload = new VoiceAudioPayload(samples, sampleRate, channels);
                }
            }
            catch
            {
                payload = EmptyPayload();
            }
            finally
            {
                ReleaseSession(lockedDeviceId, clip);
            }

            return payload;
        }

        public void Cancel(string deviceId)
        {
            AudioClip clip = _recordingClip;
            string lockedDeviceId = _activeDeviceId;
            if (clip == null && string.IsNullOrEmpty(lockedDeviceId)) return;
            ReleaseSession(lockedDeviceId, clip);
        }

        public void Dispose()
        {
            Cancel(_activeDeviceId);
        }

        private void ReleaseSession(string lockedDeviceId, AudioClip clip)
        {
            // Clear first so reentrant or repeated cancellation cannot release the same clip twice.
            _recordingClip = null;
            _activeDeviceId = null;
            _levelSamples = Array.Empty<float>();

            try
            {
                if (!string.IsNullOrEmpty(lockedDeviceId)) Microphone.End(lockedDeviceId);
            }
            catch
            {
                // Native device removal can make End fail; the managed clip still must be freed.
            }
            finally
            {
                if (clip != null) UnityEngine.Object.Destroy(clip);
            }
        }

        private void RefreshDevices(bool force)
        {
            double now = Time.realtimeSinceStartup;
            if (!force && now - _deviceCacheUpdatedAt < DeviceCacheSeconds) return;
            _deviceCacheUpdatedAt = now;

            try
            {
                string[] devices = Microphone.devices ?? Array.Empty<string>();
                var uniqueDevices = new List<string>(devices.Length);
                foreach (string device in devices)
                {
                    if (string.IsNullOrWhiteSpace(device) || ContainsDevice(uniqueDevices, device))
                        continue;
                    uniqueDevices.Add(device);
                }
                _cachedDevices = uniqueDevices.AsReadOnly();
            }
            catch
            {
                _cachedDevices = Array.Empty<string>();
            }
        }

        private static int ChooseSupportedSampleRate(string deviceId, int requestedSampleRate)
        {
            try
            {
                Microphone.GetDeviceCaps(deviceId, out int minimum, out int maximum);
                // Unity may report 0/0 when the backend does not expose fixed limits. In that case
                // preserve the requested rate and let Start be the authoritative capability check.
                if (minimum > 0 && requestedSampleRate < minimum) return minimum;
                if (maximum > 0 && requestedSampleRate > maximum) return maximum;
            }
            catch
            {
                // Start will still provide a deterministic success/failure result.
            }
            return requestedSampleRate;
        }

        private static bool ContainsDevice(IReadOnlyList<string> devices, string deviceId)
        {
            for (int index = 0; index < devices.Count; index++)
            {
                if (string.Equals(devices[index], deviceId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static VoiceAudioPayload EmptyPayload() =>
            new VoiceAudioPayload(Array.Empty<float>(), 0, 0);
    }
}
