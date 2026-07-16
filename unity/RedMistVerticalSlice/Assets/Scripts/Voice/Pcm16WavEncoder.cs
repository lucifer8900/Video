using System;

namespace Lingmai.RedMist
{
    public static class Pcm16WavEncoder
    {
        public const int OutputSampleRateHz = 16000;
        public const int OutputChannels = 1;
        public const int MaximumDurationSeconds = 8;

        private const int HeaderLength = 44;

        public static bool TryEncode(
            VoiceAudioPayload payload,
            out byte[] wav,
            out string error)
        {
            wav = null;
            error = string.Empty;

            if (payload == null)
                return Fail("Audio payload is required.", out error);
            if (payload.Samples == null || payload.Samples.Length == 0)
                return Fail("Audio payload contains no samples.", out error);
            if (payload.SampleRateHz <= 0)
                return Fail("Audio sample rate must be positive.", out error);
            if (payload.Channels != 1 && payload.Channels != 2)
                return Fail("Only mono or stereo input is supported.", out error);
            if (payload.Samples.Length % payload.Channels != 0)
                return Fail("Audio samples do not contain complete interleaved frames.", out error);

            int sourceFrames = payload.Samples.Length / payload.Channels;
            if ((long)sourceFrames > (long)payload.SampleRateHz * MaximumDurationSeconds)
                return Fail("Audio duration exceeds the 8 second limit.", out error);

            try
            {
                int outputFrames = CalculateOutputFrames(sourceFrames, payload.SampleRateHz);
                if (outputFrames <= 0)
                    return Fail("Audio duration is too short to encode.", out error);
                if (outputFrames > OutputSampleRateHz * MaximumDurationSeconds)
                    return Fail("Resampled audio exceeds the 8 second limit.", out error);

                int dataLength = checked(outputFrames * sizeof(short));
                byte[] result = new byte[checked(HeaderLength + dataLength)];
                WriteHeader(result, dataLength);

                for (int outputIndex = 0; outputIndex < outputFrames; outputIndex++)
                {
                    double sourcePosition = (double)outputIndex * payload.SampleRateHz / OutputSampleRateHz;
                    int leftFrame = Math.Min((int)sourcePosition, sourceFrames - 1);
                    int rightFrame = Math.Min(leftFrame + 1, sourceFrames - 1);
                    float fraction = (float)(sourcePosition - leftFrame);
                    float left = ReadMonoFrame(payload, leftFrame);
                    float right = ReadMonoFrame(payload, rightFrame);
                    float sample = left + (right - left) * fraction;
                    WriteInt16(result, HeaderLength + outputIndex * sizeof(short), ToPcm16(sample));
                }

                wav = result;
                return true;
            }
            catch (Exception exception) when (
                exception is OverflowException ||
                exception is OutOfMemoryException ||
                exception is ArgumentException)
            {
                wav = null;
                error = "Audio could not be encoded: " + exception.Message;
                return false;
            }
        }

        private static int CalculateOutputFrames(int sourceFrames, int sourceRate)
        {
            double exactFrames = (double)sourceFrames * OutputSampleRateHz / sourceRate;
            return checked((int)Math.Round(exactFrames, MidpointRounding.AwayFromZero));
        }

        private static float ReadMonoFrame(VoiceAudioPayload payload, int frame)
        {
            int offset = frame * payload.Channels;
            if (payload.Channels == 1) return Sanitize(payload.Samples[offset]);
            return (Sanitize(payload.Samples[offset]) + Sanitize(payload.Samples[offset + 1])) * 0.5f;
        }

        private static float Sanitize(float sample)
        {
            if (float.IsNaN(sample)) return 0f;
            if (sample > 1f) return 1f;
            if (sample < -1f) return -1f;
            return sample;
        }

        private static short ToPcm16(float sample)
        {
            sample = Sanitize(sample);
            if (sample >= 1f) return short.MaxValue;
            if (sample <= -1f) return short.MinValue;
            return checked((short)Math.Round(
                sample * short.MaxValue,
                MidpointRounding.AwayFromZero));
        }

        private static void WriteHeader(byte[] target, int dataLength)
        {
            WriteAscii(target, 0, "RIFF");
            WriteInt32(target, 4, checked(HeaderLength + dataLength - 8));
            WriteAscii(target, 8, "WAVE");
            WriteAscii(target, 12, "fmt ");
            WriteInt32(target, 16, 16);
            WriteInt16(target, 20, 1);
            WriteInt16(target, 22, OutputChannels);
            WriteInt32(target, 24, OutputSampleRateHz);
            WriteInt32(target, 28, OutputSampleRateHz * OutputChannels * sizeof(short));
            WriteInt16(target, 32, OutputChannels * sizeof(short));
            WriteInt16(target, 34, 16);
            WriteAscii(target, 36, "data");
            WriteInt32(target, 40, dataLength);
        }

        private static void WriteAscii(byte[] target, int offset, string value)
        {
            for (int index = 0; index < value.Length; index++)
                target[offset + index] = checked((byte)value[index]);
        }

        private static void WriteInt16(byte[] target, int offset, int value)
        {
            target[offset] = unchecked((byte)value);
            target[offset + 1] = unchecked((byte)(value >> 8));
        }

        private static void WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = unchecked((byte)value);
            target[offset + 1] = unchecked((byte)(value >> 8));
            target[offset + 2] = unchecked((byte)(value >> 16));
            target[offset + 3] = unchecked((byte)(value >> 24));
        }

        private static bool Fail(string message, out string error)
        {
            error = message ?? "Audio encoding failed.";
            return false;
        }
    }
}
