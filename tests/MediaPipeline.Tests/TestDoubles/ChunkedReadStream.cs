namespace Lingmai.RedMist.MediaPipeline.Tests.TestDoubles;

internal sealed class ChunkedReadStream(byte[] content, int maximumChunkBytes) : Stream
{
    private int _position;

    public int LargestRequestedRead { get; private set; }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => content.LongLength;
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        LargestRequestedRead = Math.Max(LargestRequestedRead, count);
        int available = content.Length - _position;
        if (available <= 0) return 0;
        int length = Math.Min(Math.Min(count, maximumChunkBytes), available);
        content.AsSpan(_position, length).CopyTo(buffer.AsSpan(offset, length));
        _position += length;
        return length;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LargestRequestedRead = Math.Max(LargestRequestedRead, buffer.Length);
        int available = content.Length - _position;
        if (available <= 0) return ValueTask.FromResult(0);
        int length = Math.Min(Math.Min(buffer.Length, maximumChunkBytes), available);
        content.AsMemory(_position, length).CopyTo(buffer);
        _position += length;
        return ValueTask.FromResult(length);
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
