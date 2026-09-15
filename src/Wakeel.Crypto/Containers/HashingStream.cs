using System.Security.Cryptography;

namespace Wakeel.Crypto;

/// <summary>
/// Write through stream that hashes and counts everything on its way to the inner stream,
/// so a large payload never has to be read twice.
/// </summary>
internal sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _written;
    private byte[]? _result;

    public HashingStream(Stream inner, bool leaveOpen = true)
    {
        _inner = inner;
        _leaveOpen = leaveOpen;
    }

    public long BytesWritten => _written;

    public byte[] Hash => _result ??= _hash.GetHashAndReset();

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _hash.AppendData(buffer);
        _written += buffer.Length;
        _inner.Write(buffer);
    }

    public override void WriteByte(byte value) => Write([value]);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _result ??= _hash.GetHashAndReset();
            _hash.Dispose();
            if (!_leaveOpen)
            {
                _inner.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
