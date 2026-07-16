using System.Text;

namespace Lingmai.RedMist.Api.Tests;

internal static class WavFixture
{
    public static byte[] CreatePcm(
        int sampleRateHz = 16_000,
        int channels = 1,
        int bitsPerSample = 16,
        double durationSeconds = 1d,
        int? totalLength = null)
    {
        int bytesPerSample = bitsPerSample / 8;
        int blockAlign = checked(channels * bytesPerSample);
        int frames = checked((int)Math.Ceiling(sampleRateHz * durationSeconds));
        int dataLength = checked(frames * blockAlign);

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)channels);
            writer.Write(sampleRateHz);
            writer.Write(checked(sampleRateHz * blockAlign));
            writer.Write((ushort)blockAlign);
            writer.Write((ushort)bitsPerSample);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            writer.Write(new byte[dataLength]);

            if (totalLength is int requestedLength)
            {
                int remaining = checked(requestedLength - (int)stream.Length);
                if (remaining < 8 || (remaining & 1) != 0)
                    throw new ArgumentOutOfRangeException(nameof(totalLength), "The requested WAV length must leave room for an even JUNK chunk.");

                int junkLength = remaining - 8;
                writer.Write(Encoding.ASCII.GetBytes("JUNK"));
                writer.Write(junkLength);
                writer.Write(new byte[junkLength]);
            }

            writer.Flush();
            stream.Position = 4;
            writer.Write(checked((int)stream.Length - 8));
        }

        return stream.ToArray();
    }
}
