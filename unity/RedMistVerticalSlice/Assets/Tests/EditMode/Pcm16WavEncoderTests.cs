using System;
using System.Text;
using NUnit.Framework;

namespace Lingmai.RedMist.Tests
{
    public sealed class Pcm16WavEncoderTests
    {
        private const int HeaderLength = 44;

        [Test]
        public void EncodesCanonicalRiffPcm16MonoAtSixteenKilohertz()
        {
            VoiceAudioPayload payload = new VoiceAudioPayload(
                new[] { 0f, 1f, -1f },
                16000,
                1);

            bool encoded = Pcm16WavEncoder.TryEncode(payload, out byte[] wav, out string error);

            Assert.IsTrue(encoded, error);
            Assert.IsEmpty(error);
            Assert.AreEqual(HeaderLength + 6, wav.Length);
            Assert.AreEqual("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual(wav.Length - 8, ReadInt32(wav, 4));
            Assert.AreEqual("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
            Assert.AreEqual(16, ReadInt32(wav, 16));
            Assert.AreEqual(1, ReadInt16(wav, 20));
            Assert.AreEqual(1, ReadInt16(wav, 22));
            Assert.AreEqual(16000, ReadInt32(wav, 24));
            Assert.AreEqual(32000, ReadInt32(wav, 28));
            Assert.AreEqual(2, ReadInt16(wav, 32));
            Assert.AreEqual(16, ReadInt16(wav, 34));
            Assert.AreEqual("data", Encoding.ASCII.GetString(wav, 36, 4));
            Assert.AreEqual(6, ReadInt32(wav, 40));
            Assert.AreEqual(0, ReadInt16(wav, 44));
            Assert.AreEqual(short.MaxValue, ReadInt16(wav, 46));
            Assert.AreEqual(short.MinValue, ReadInt16(wav, 48));
        }

        [Test]
        public void DownmixesInterleavedStereoFramesToMono()
        {
            VoiceAudioPayload payload = new VoiceAudioPayload(
                new[]
                {
                    1f, -1f,
                    1f, 1f,
                    -1f, -1f
                },
                16000,
                2);

            bool encoded = Pcm16WavEncoder.TryEncode(payload, out byte[] wav, out string error);

            Assert.IsTrue(encoded, error);
            Assert.AreEqual(1, ReadInt16(wav, 22));
            Assert.AreEqual(6, ReadInt32(wav, 40));
            Assert.AreEqual(0, ReadInt16(wav, 44));
            Assert.AreEqual(short.MaxValue, ReadInt16(wav, 46));
            Assert.AreEqual(short.MinValue, ReadInt16(wav, 48));
        }

        [Test]
        public void ResamplesFortyEightKilohertzInputToSixteenKilohertz()
        {
            float[] source = new float[48];
            for (int i = 0; i < source.Length; i++) source[i] = 0.25f;
            VoiceAudioPayload payload = new VoiceAudioPayload(source, 48000, 1);

            bool encoded = Pcm16WavEncoder.TryEncode(payload, out byte[] wav, out string error);

            Assert.IsTrue(encoded, error);
            Assert.AreEqual(16000, ReadInt32(wav, 24));
            Assert.AreEqual(16 * sizeof(short), ReadInt32(wav, 40));
            Assert.AreEqual(HeaderLength + 16 * sizeof(short), wav.Length);
            for (int offset = HeaderLength; offset < wav.Length; offset += sizeof(short))
                Assert.AreEqual(8192, ReadInt16(wav, offset));
        }

        [Test]
        public void AcceptsExactlyEightSecondsAndRejectsAnyLongerPayload()
        {
            VoiceAudioPayload boundary = new VoiceAudioPayload(new float[16000 * 8], 16000, 1);
            VoiceAudioPayload tooLong = new VoiceAudioPayload(new float[16000 * 8 + 1], 16000, 1);

            Assert.IsTrue(Pcm16WavEncoder.TryEncode(boundary, out byte[] boundaryWav, out string boundaryError), boundaryError);
            Assert.AreEqual(HeaderLength + 16000 * 8 * sizeof(short), boundaryWav.Length);

            Assert.IsFalse(Pcm16WavEncoder.TryEncode(tooLong, out byte[] rejectedWav, out string error));
            Assert.That(rejectedWav, Is.Null.Or.Empty);
            Assert.IsNotEmpty(error);
        }

        [Test]
        public void RejectsUnsupportedChannelsAndIncompleteInterleavedFrames()
        {
            AssertRejected(new VoiceAudioPayload(new[] { 0f, 0f }, 16000, 0));
            AssertRejected(new VoiceAudioPayload(new[] { 0f, 0f, 0f }, 16000, 3));
            AssertRejected(new VoiceAudioPayload(new[] { 0f, 0f, 0f }, 16000, 2));
        }

        [Test]
        public void ClampsFloatingPointSamplesToPcm16Range()
        {
            VoiceAudioPayload payload = new VoiceAudioPayload(
                new[] { 1.5f, -1.5f, 1f, -1f, 0f },
                16000,
                1);

            bool encoded = Pcm16WavEncoder.TryEncode(payload, out byte[] wav, out string error);

            Assert.IsTrue(encoded, error);
            Assert.AreEqual(short.MaxValue, ReadInt16(wav, 44));
            Assert.AreEqual(short.MinValue, ReadInt16(wav, 46));
            Assert.AreEqual(short.MaxValue, ReadInt16(wav, 48));
            Assert.AreEqual(short.MinValue, ReadInt16(wav, 50));
            Assert.AreEqual(0, ReadInt16(wav, 52));
        }

        private static short ReadInt16(byte[] bytes, int offset)
        {
            return unchecked((short)(bytes[offset] | bytes[offset + 1] << 8));
        }

        private static int ReadInt32(byte[] bytes, int offset)
        {
            return bytes[offset] |
                   bytes[offset + 1] << 8 |
                   bytes[offset + 2] << 16 |
                   bytes[offset + 3] << 24;
        }

        private static void AssertRejected(VoiceAudioPayload payload)
        {
            bool encoded = Pcm16WavEncoder.TryEncode(payload, out byte[] wav, out string error);

            Assert.IsFalse(encoded);
            Assert.That(wav, Is.Null.Or.Empty);
            Assert.IsNotEmpty(error);
        }
    }
}
