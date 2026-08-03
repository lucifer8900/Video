using System;
using System.Collections.Generic;

namespace Lingmai.RedMist
{
    public enum VoicePermissionStatus
    {
        Unknown,
        Granted,
        Denied
    }

    public enum VoiceCaptureState
    {
        Idle,
        AwaitingPermission,
        Recording,
        Completed,
        Fallback
    }

    public enum VoiceCaptureError
    {
        None,
        NoMicrophone,
        PermissionDenied,
        DeviceDisconnected,
        Timeout,
        Silence,
        Cancelled,
        StartFailed,
        CaptureFailed
    }

    public sealed class VoiceAudioPayload
    {
        public VoiceAudioPayload(float[] samples, int sampleRateHz, int channels)
        {
            Samples = samples ?? Array.Empty<float>();
            SampleRateHz = sampleRateHz;
            Channels = channels;
        }

        public float[] Samples { get; }
        public int SampleRateHz { get; }
        public int Channels { get; }
    }

    public sealed class VoiceCaptureOptions
    {
        public VoiceCaptureOptions(
            int sampleRateHz,
            double maxDurationSeconds,
            double permissionTimeoutSeconds,
            double levelSampleIntervalSeconds,
            float silenceRmsThreshold)
        {
            if (sampleRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            if (maxDurationSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(maxDurationSeconds));
            if (permissionTimeoutSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(permissionTimeoutSeconds));
            if (levelSampleIntervalSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(levelSampleIntervalSeconds));
            if (silenceRmsThreshold < 0f) throw new ArgumentOutOfRangeException(nameof(silenceRmsThreshold));

            SampleRateHz = sampleRateHz;
            MaxDurationSeconds = maxDurationSeconds;
            PermissionTimeoutSeconds = permissionTimeoutSeconds;
            LevelSampleIntervalSeconds = levelSampleIntervalSeconds;
            SilenceRmsThreshold = silenceRmsThreshold;
        }

        public int SampleRateHz { get; }
        public double MaxDurationSeconds { get; }
        public double PermissionTimeoutSeconds { get; }
        public double LevelSampleIntervalSeconds { get; }
        public float SilenceRmsThreshold { get; }
    }

    public readonly struct VoiceCaptureUpdate
    {
        public VoiceCaptureUpdate(
            VoiceCaptureState state,
            VoiceCaptureError error,
            VoiceAudioPayload audio = null,
            float level01 = 0f)
        {
            State = state;
            Error = error;
            Audio = audio;
            Level01 = level01;
        }

        public VoiceCaptureState State { get; }
        public VoiceCaptureError Error { get; }
        public VoiceAudioPayload Audio { get; }
        public float Level01 { get; }
        public bool HasAudio => Audio != null && Audio.Samples != null && Audio.Samples.Length > 0;
        public bool IsTerminal => State == VoiceCaptureState.Completed || State == VoiceCaptureState.Fallback;
        public bool ShouldUseFixedChoices => State == VoiceCaptureState.Fallback;
    }

    public interface IVoiceClock
    {
        double NowSeconds { get; }
    }

    public interface IVoicePermissionGateway
    {
        VoicePermissionStatus Current { get; }
        void Request(Action<VoicePermissionStatus> completed);
    }

    public interface IVoiceCaptureGateway
    {
        IReadOnlyList<string> DeviceIds { get; }
        bool TryStart(string deviceId, int sampleRateHz, int maxSeconds);
        bool IsRecording(string deviceId);
        float ReadLevel01(string deviceId);
        VoiceAudioPayload Stop(string deviceId);
        void Cancel(string deviceId);
    }
}
