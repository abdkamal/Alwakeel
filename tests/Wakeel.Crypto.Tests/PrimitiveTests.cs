using System.Text;

namespace Wakeel.Crypto.Tests;

public class PrimitiveTests
{
    [Fact]
    public void RandomBytes_returns_the_requested_length_and_does_not_repeat()
    {
        var first = RandomBytes.Next(32);
        var second = RandomBytes.Next(32);

        Assert.Equal(32, first.Length);
        Assert.Equal(32, second.Length);
        Assert.NotEqual(Convert.ToHexString(first), Convert.ToHexString(second));
        Assert.Empty(RandomBytes.Next(0));
    }

    [Fact]
    public void Sha256_matches_the_published_value_for_the_empty_input()
    {
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Sha256.HashHex(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Sha256_over_a_stream_matches_the_same_bytes_in_memory()
    {
        var data = RandomBytes.Next(200_000);
        using var stream = new MemoryStream(data);

        Assert.Equal(Sha256.HashHex(data), Sha256.HashHex(stream));
    }

    [Fact]
    public void Sha256_base64url_carries_no_padding_and_no_unsafe_characters()
    {
        var text = Sha256.HashBase64Url(Encoding.UTF8.GetBytes("wakeel"));

        Assert.Equal(43, text.Length);
        Assert.DoesNotContain('=', text);
        Assert.DoesNotContain('+', text);
        Assert.DoesNotContain('/', text);
    }

    [Fact]
    public void Base64Url_round_trips_arbitrary_bytes()
    {
        for (var length = 0; length < 40; length++)
        {
            var data = RandomBytes.Next(length);
            Assert.Equal(data, Base64Url.Decode(Base64Url.Encode(data)));
        }
    }

    [Fact]
    public void Hkdf_is_deterministic_and_separated_by_its_context_label()
    {
        var ikm = RandomBytes.Next(32);
        var salt = RandomBytes.Next(16);

        var first = Hkdf.DeriveKey(ikm, 32, salt, "wakeel.one");
        var again = Hkdf.DeriveKey(ikm, 32, salt, "wakeel.one");
        var other = Hkdf.DeriveKey(ikm, 32, salt, "wakeel.two");

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void Hkdf_extract_then_expand_equals_a_single_derivation()
    {
        var ikm = RandomBytes.Next(32);
        var salt = RandomBytes.Next(16);
        var info = Encoding.UTF8.GetBytes("wakeel.split");

        var direct = Hkdf.DeriveKey(ikm, 64, salt, info);
        var split = Hkdf.Expand(Hkdf.Extract(ikm, salt), 64, info);

        Assert.Equal(direct, split);
    }

    [Fact]
    public void Aead_round_trips_a_payload()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var payload = Encoding.UTF8.GetBytes("one approved correspondence");

        var sealedBlock = Aead.Encrypt(key, payload, "wakeel.test");

        Assert.Equal(Aead.FormatVersion, sealedBlock[0]);
        Assert.Equal(1 + Aead.NonceSize + payload.Length + Aead.TagSize, sealedBlock.Length);
        Assert.Equal(payload, Aead.Decrypt(key, sealedBlock, "wakeel.test"));
    }

    [Fact]
    public void Aead_uses_a_fresh_nonce_for_every_call()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var payload = RandomBytes.Next(64);

        var first = Aead.Encrypt(key, payload, "wakeel.test");
        var second = Aead.Encrypt(key, payload, "wakeel.test");

        Assert.NotEqual(Convert.ToHexString(first), Convert.ToHexString(second));
    }

    [Fact]
    public void Aead_refuses_a_different_associated_data()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var sealedBlock = Aead.Encrypt(key, RandomBytes.Next(48), "wakeel.one");

        var error = Assert.Throws<CryptoException>(() => Aead.Decrypt(key, sealedBlock, "wakeel.two"));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void Aead_refuses_a_flipped_ciphertext_bit()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var sealedBlock = Aead.Encrypt(key, RandomBytes.Next(48), "wakeel.test");
        sealedBlock[20] ^= 0x01;

        var error = Assert.Throws<CryptoException>(() => Aead.Decrypt(key, sealedBlock, "wakeel.test"));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void Aead_refuses_a_changed_version_header()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var sealedBlock = Aead.Encrypt(key, RandomBytes.Next(48), "wakeel.test");
        sealedBlock[0] = 9;

        var error = Assert.Throws<CryptoException>(() => Aead.Decrypt(key, sealedBlock, "wakeel.test"));
        Assert.Equal(ErrorCode.UnknownKind, error.Code);
    }

    [Fact]
    public void Aead_refuses_another_key()
    {
        var sealedBlock = Aead.Encrypt(RandomBytes.Next(Aead.KeySize), RandomBytes.Next(48), "wakeel.test");

        var error = Assert.Throws<CryptoException>(
            () => Aead.Decrypt(RandomBytes.Next(Aead.KeySize), sealedBlock, "wakeel.test"));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void Aead_with_an_explicit_nonce_round_trips()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var nonce = RandomBytes.Next(Aead.NonceSize);
        var payload = RandomBytes.Next(32);
        var associated = Encoding.UTF8.GetBytes("wakeel.wrap");

        var cipher = Aead.EncryptWithNonce(key, nonce, payload, associated);

        Assert.Equal(payload.Length + Aead.TagSize, cipher.Length);
        Assert.Equal(payload, Aead.DecryptWithNonce(key, nonce, cipher, associated));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4096)]
    [InlineData(300_000)]
    public void Aead_streaming_round_trips_payloads_of_every_size(int size)
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var payload = RandomBytes.Next(size);
        var associated = Encoding.UTF8.GetBytes("wakeel.payload");

        using var encrypted = new MemoryStream();
        Aead.EncryptStream(key, new MemoryStream(payload), encrypted, associated, chunkSize: 4096);

        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        Aead.DecryptStream(key, encrypted, decrypted, associated);

        Assert.Equal(payload, decrypted.ToArray());
    }

    [Fact]
    public void Aead_streaming_refuses_a_truncated_payload()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var associated = Encoding.UTF8.GetBytes("wakeel.payload");

        using var encrypted = new MemoryStream();
        Aead.EncryptStream(key, new MemoryStream(RandomBytes.Next(20_000)), encrypted, associated, chunkSize: 4096);

        var truncated = encrypted.ToArray()[..^(Aead.TagSize + 200)];
        using var output = new MemoryStream();

        var error = Assert.Throws<CryptoException>(
            () => Aead.DecryptStream(key, new MemoryStream(truncated), output, associated));
        Assert.True(error.Code is ErrorCode.Corrupt or ErrorCode.Tampered);
    }

    [Fact]
    public void Aead_streaming_refuses_a_flipped_bit()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var associated = Encoding.UTF8.GetBytes("wakeel.payload");

        using var encrypted = new MemoryStream();
        Aead.EncryptStream(key, new MemoryStream(RandomBytes.Next(9_000)), encrypted, associated, chunkSize: 4096);

        var bytes = encrypted.ToArray();
        bytes[^3] ^= 0x20;
        using var output = new MemoryStream();

        var error = Assert.Throws<CryptoException>(
            () => Aead.DecryptStream(key, new MemoryStream(bytes), output, associated));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void Aead_rejects_a_key_of_the_wrong_length()
    {
        var error = Assert.Throws<CryptoException>(
            () => Aead.Encrypt(RandomBytes.Next(16), RandomBytes.Next(8), "wakeel.test"));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void Argon2_is_deterministic_for_the_same_password_and_parameters()
    {
        var parameters = new Argon2Params(Argon2Params.MinMemoryKb, 2, 1, RandomBytes.Next(16));

        var first = Argon2Kdf.DeriveKey("كلمة المرور", parameters);
        var again = Argon2Kdf.DeriveKey("كلمة المرور", parameters);

        Assert.Equal(first, again);
        Assert.Equal(32, first.Length);
    }

    [Fact]
    public void Argon2id_matches_the_published_reference_vector()
    {
        // RFC 9106 §5.3: password 32×0x01, salt 16×0x02, secret 8×0x03, associated data
        // 12×0x04, memory 32 KiB, three passes, four lanes, thirty two byte tag. Pinning it
        // proves the library and the units of every parameter, and the phone side must derive
        // exactly the same bytes for the printed recovery sheet to work across devices.
        var password = CreateFilled(32, 0x01);
        var salt = CreateFilled(16, 0x02);
        var secret = CreateFilled(8, 0x03);
        var associated = CreateFilled(12, 0x04);

        var tag = Argon2Kdf.DeriveRaw(
            password,
            salt,
            memoryKb: 32,
            iterations: 3,
            parallelism: 4,
            length: 32,
            secret,
            associated);

        Assert.Equal(
            "0D640DF58D78766C08C037A34A8B53C9D01EF0452D75B65EB52520E96B01E659",
            Convert.ToHexString(tag));
    }

    [Fact]
    public void Argon2_counts_its_memory_in_kibibytes()
    {
        var salt = RandomBytes.Next(16);

        // The same numbers through both doors must agree, so the product's own parameters are
        // wired to the raw primitive exactly as the reference vector above exercises it.
        var throughParameters = Argon2Kdf.DeriveKey("كلمة المرور", new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, salt));
        var throughRaw = Argon2Kdf.DeriveRaw(
            Encoding.UTF8.GetBytes("كلمة المرور"),
            salt,
            Argon2Params.MinMemoryKb,
            iterations: 1,
            parallelism: 1,
            length: 32);

        Assert.Equal(throughRaw, throughParameters);
        Assert.Equal(32 * 1024, Argon2Params.MinMemoryKb);
    }

    [Fact]
    public void Argon2_gives_different_keys_for_different_salts_and_passwords()
    {
        var parameters = new Argon2Params(Argon2Params.MinMemoryKb, 2, 1, RandomBytes.Next(16));
        var other = parameters.WithFreshSalt();

        Assert.NotEqual(Argon2Kdf.DeriveKey("one", parameters), Argon2Kdf.DeriveKey("two", parameters));
        Assert.NotEqual(Argon2Kdf.DeriveKey("one", parameters), Argon2Kdf.DeriveKey("one", other));
    }

    [Fact]
    public void Argon2_rejects_parameters_below_the_floor()
    {
        var error = Assert.Throws<CryptoException>(
            () => Argon2Kdf.DeriveKey("one", new Argon2Params(1024, 2, 1, RandomBytes.Next(16))));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void AutoTune_keeps_the_full_memory_cost_on_a_fast_machine()
    {
        var tuned = Argon2Kdf.AutoTune(timeProvider: new SteppingClock(TimeSpan.FromMilliseconds(50)));

        Assert.Equal(Argon2Params.DefaultMemoryKb, tuned.MemoryKb);
        Assert.Equal(Argon2Params.DefaultIterations, tuned.Iterations);
        Assert.Equal(Argon2Params.DefaultParallelism, tuned.Parallelism);
        Assert.Equal(Argon2Params.SaltSize, tuned.Salt.Length);
    }

    [Fact]
    public void AutoTune_never_drops_below_thirty_two_megabytes()
    {
        var tuned = Argon2Kdf.AutoTune(timeProvider: new SteppingClock(TimeSpan.FromSeconds(9)));

        Assert.Equal(Argon2Params.MinMemoryKb, tuned.MemoryKb);
        tuned.Validate();
    }

    [Fact]
    public void Null_platform_protector_round_trips_and_refuses_other_entropy()
    {
        var protector = new NullPlatformProtector();
        var secret = RandomBytes.Next(32);
        var entropy = Encoding.UTF8.GetBytes("wakeel.machine");

        var protectedBytes = protector.Protect(secret, entropy);

        Assert.Equal(secret, protector.Unprotect(protectedBytes, entropy));
        var error = Assert.Throws<CryptoException>(
            () => protector.Unprotect(protectedBytes, Encoding.UTF8.GetBytes("other")));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void A_stream_whose_declared_chunk_size_was_rewritten_is_refused()
    {
        var key = RandomBytes.Next(Aead.KeySize);
        var associated = Encoding.UTF8.GetBytes("wakeel.payload");

        using var encrypted = new MemoryStream();
        Aead.EncryptStream(key, new MemoryStream(RandomBytes.Next(9_000)), encrypted, associated, chunkSize: 4096);

        // The five byte header is plain text, but every frame authenticates it.
        var bytes = encrypted.ToArray();
        bytes[1] = 0x00;
        bytes[2] = 0x20;

        using var output = new MemoryStream();
        var error = Assert.Throws<CryptoException>(
            () => Aead.DecryptStream(key, new MemoryStream(bytes), output, associated));
        Assert.True(error.Code is ErrorCode.Tampered or ErrorCode.Corrupt);
    }

    private static byte[] CreateFilled(int length, byte value)
    {
        var buffer = new byte[length];
        Array.Fill(buffer, value);
        return buffer;
    }
}
