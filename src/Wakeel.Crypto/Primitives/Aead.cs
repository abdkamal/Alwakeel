using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Wakeel.Crypto;

/// <summary>
/// AES-256-GCM with a random 12 byte nonce written in front of the ciphertext and a
/// one byte format version header. The version byte and the associated data are both
/// authenticated, so downgrading or re-labelling a blob breaks decryption.
/// </summary>
/// <remarks>
/// Buffer layout: <c>[version:1][nonce:12][ciphertext][tag:16]</c>.
/// Stream layout: <c>[version:1][chunkSize:4]</c> then frames
/// <c>[final:1][nonce:12][length:4][ciphertext+tag]</c>; the last frame carries
/// <c>final = 1</c> and an empty plaintext, which makes truncation detectable.
/// Every frame authenticates the whole five byte stream header, so the declared chunk
/// size cannot be rewritten without breaking the very first frame.
/// </remarks>
public static class Aead
{
    public const byte FormatVersion = 1;
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int DefaultChunkSize = 1 << 20;

    /// <summary>Size of the stream header: the version byte plus the declared chunk size.</summary>
    internal const int HeaderSize = 5;

    private const int MinChunkSize = 1024;
    private const int MaxChunkSize = 16 << 20;

    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        CheckKey(key);
        var result = new byte[1 + NonceSize + plaintext.Length + TagSize];
        result[0] = FormatVersion;
        var nonce = result.AsSpan(1, NonceSize);
        RandomBytes.Fill(nonce);

        var associated = BuildAssociatedData(FormatVersion, associatedData);
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(
            nonce,
            plaintext,
            result.AsSpan(1 + NonceSize, plaintext.Length),
            result.AsSpan(1 + NonceSize + plaintext.Length, TagSize),
            associated);
        return result;
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, string associatedData) =>
        Encrypt(key, plaintext, Encoding.UTF8.GetBytes(associatedData ?? string.Empty));

    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedData, ReadOnlySpan<byte> associatedData)
    {
        CheckKey(key);
        if (sealedData.Length < 1 + NonceSize + TagSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The encrypted block is too short to be valid.");
        }

        var version = sealedData[0];
        if (version != FormatVersion)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "The encrypted block uses an unsupported format version.");
        }

        var nonce = sealedData.Slice(1, NonceSize);
        var cipherLength = sealedData.Length - 1 - NonceSize - TagSize;
        var cipher = sealedData.Slice(1 + NonceSize, cipherLength);
        var tag = sealedData.Slice(1 + NonceSize + cipherLength, TagSize);

        var associated = BuildAssociatedData(version, associatedData);
        var plaintext = new byte[cipherLength];
        using var gcm = new AesGcm(key, TagSize);
        try
        {
            gcm.Decrypt(nonce, cipher, tag, plaintext, associated);
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptoException(ErrorCode.Tampered, "The encrypted block failed its integrity check.", exception);
        }

        return plaintext;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> sealedData, string associatedData) =>
        Decrypt(key, sealedData, Encoding.UTF8.GetBytes(associatedData ?? string.Empty));

    /// <summary>
    /// Encrypts with a caller supplied nonce and returns only <c>ciphertext || tag</c>.
    /// Used by key wraps, which store the nonce in their own field.
    /// </summary>
    public static byte[] EncryptWithNonce(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData)
    {
        CheckKey(key);
        if (nonce.Length != NonceSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The nonce must be exactly twelve bytes.");
        }

        var associated = BuildAssociatedData(FormatVersion, associatedData);
        var result = new byte[plaintext.Length + TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plaintext, result.AsSpan(0, plaintext.Length), result.AsSpan(plaintext.Length, TagSize), associated);
        return result;
    }

    public static byte[] DecryptWithNonce(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertextAndTag,
        ReadOnlySpan<byte> associatedData)
    {
        CheckKey(key);
        if (nonce.Length != NonceSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The nonce must be exactly twelve bytes.");
        }

        if (ciphertextAndTag.Length < TagSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The encrypted block is too short to be valid.");
        }

        var cipherLength = ciphertextAndTag.Length - TagSize;
        var associated = BuildAssociatedData(FormatVersion, associatedData);
        var plaintext = new byte[cipherLength];
        using var gcm = new AesGcm(key, TagSize);
        try
        {
            gcm.Decrypt(
                nonce,
                ciphertextAndTag[..cipherLength],
                ciphertextAndTag.Slice(cipherLength, TagSize),
                plaintext,
                associated);
        }
        catch (CryptographicException exception)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptoException(ErrorCode.Tampered, "The encrypted block failed its integrity check.", exception);
        }

        return plaintext;
    }

    /// <summary>
    /// Chunked encryption for payloads that must not be held in memory in one piece.
    /// </summary>
    public static void EncryptStream(
        ReadOnlySpan<byte> key,
        Stream input,
        Stream output,
        ReadOnlySpan<byte> associatedData,
        int chunkSize = DefaultChunkSize)
    {
        CheckKey(key);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        if (chunkSize < MinChunkSize || chunkSize > MaxChunkSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The chunk size is outside the supported range.");
        }

        var header = new byte[HeaderSize];
        header[0] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), chunkSize);
        output.Write(header);

        var associated = associatedData.ToArray();
        var plainBuffer = new byte[chunkSize];
        var cipherBuffer = new byte[chunkSize + TagSize];
        var frameHeader = new byte[1 + NonceSize + 4];
        long index = 0;

        using var gcm = new AesGcm(key, TagSize);
        while (true)
        {
            var read = ReadAtMost(input, plainBuffer);
            var isFinal = read == 0;
            WriteFrame(gcm, output, header, associated, frameHeader, cipherBuffer, plainBuffer.AsSpan(0, read), index, isFinal);
            index++;
            if (isFinal)
            {
                break;
            }
        }

        output.Flush();
    }

    /// <summary>Chunked counterpart of <see cref="EncryptStream"/>.</summary>
    public static void DecryptStream(
        ReadOnlySpan<byte> key,
        Stream input,
        Stream output,
        ReadOnlySpan<byte> associatedData)
    {
        CheckKey(key);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var header = new byte[HeaderSize];
        ReadExactly(input, header);
        if (header[0] != FormatVersion)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "The encrypted payload uses an unsupported format version.");
        }

        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1));
        if (chunkSize < MinChunkSize || chunkSize > MaxChunkSize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The encrypted payload declares an impossible chunk size.");
        }

        var associated = associatedData.ToArray();
        var frameHeader = new byte[1 + NonceSize + 4];
        var cipherBuffer = new byte[chunkSize + TagSize];
        var plainBuffer = new byte[chunkSize];
        long index = 0;

        using var gcm = new AesGcm(key, TagSize);
        while (true)
        {
            ReadExactly(input, frameHeader);
            var flag = frameHeader[0];
            if (flag > 1)
            {
                throw new CryptoException(ErrorCode.Corrupt, "The encrypted payload contains an invalid frame.");
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(frameHeader.AsSpan(1 + NonceSize));
            if (length < TagSize || length > cipherBuffer.Length)
            {
                throw new CryptoException(ErrorCode.Corrupt, "The encrypted payload contains an invalid frame length.");
            }

            ReadExactly(input, cipherBuffer.AsSpan(0, length));
            var plainLength = length - TagSize;
            var associatedFrame = BuildFrameAssociatedData(header, associated, index, flag == 1);
            try
            {
                gcm.Decrypt(
                    frameHeader.AsSpan(1, NonceSize),
                    cipherBuffer.AsSpan(0, plainLength),
                    cipherBuffer.AsSpan(plainLength, TagSize),
                    plainBuffer.AsSpan(0, plainLength),
                    associatedFrame);
            }
            catch (CryptographicException exception)
            {
                throw new CryptoException(ErrorCode.Tampered, "The encrypted payload failed its integrity check.", exception);
            }

            if (plainLength > 0)
            {
                output.Write(plainBuffer, 0, plainLength);
            }

            index++;
            if (flag == 1)
            {
                if (input.ReadByte() != -1)
                {
                    throw new CryptoException(ErrorCode.Tampered, "The encrypted payload carries data after its final frame.");
                }

                break;
            }
        }

        output.Flush();
    }

    private static void WriteFrame(
        AesGcm gcm,
        Stream output,
        byte[] header,
        byte[] associated,
        byte[] frameHeader,
        byte[] cipherBuffer,
        ReadOnlySpan<byte> plaintext,
        long index,
        bool isFinal)
    {
        frameHeader[0] = isFinal ? (byte)1 : (byte)0;
        var nonce = frameHeader.AsSpan(1, NonceSize);
        RandomBytes.Fill(nonce);
        BinaryPrimitives.WriteInt32LittleEndian(frameHeader.AsSpan(1 + NonceSize), plaintext.Length + TagSize);

        var associatedFrame = BuildFrameAssociatedData(header, associated, index, isFinal);
        gcm.Encrypt(
            nonce,
            plaintext,
            cipherBuffer.AsSpan(0, plaintext.Length),
            cipherBuffer.AsSpan(plaintext.Length, TagSize),
            associatedFrame);

        output.Write(frameHeader);
        output.Write(cipherBuffer, 0, plaintext.Length + TagSize);
    }

    private static byte[] BuildAssociatedData(byte version, ReadOnlySpan<byte> associatedData)
    {
        var buffer = new byte[1 + associatedData.Length];
        buffer[0] = version;
        associatedData.CopyTo(buffer.AsSpan(1));
        return buffer;
    }

    /// <summary>
    /// A frame authenticates the whole stream header (version and declared chunk size), the
    /// caller's own associated data, the frame's position in the stream and whether it is last.
    /// </summary>
    private static byte[] BuildFrameAssociatedData(byte[] header, byte[] associated, long index, bool isFinal)
    {
        var buffer = new byte[header.Length + associated.Length + 8 + 1];
        header.CopyTo(buffer, 0);
        associated.CopyTo(buffer, header.Length);
        BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(header.Length + associated.Length, 8), index);
        buffer[^1] = isFinal ? (byte)1 : (byte)0;
        return buffer;
    }

    private static int ReadAtMost(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw new CryptoException(ErrorCode.Corrupt, "The encrypted payload ended earlier than expected.");
            }

            total += read;
        }
    }

    private static void CheckKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The encryption key must be exactly thirty two bytes.");
        }
    }
}
