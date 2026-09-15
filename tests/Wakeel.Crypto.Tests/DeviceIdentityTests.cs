using System.Text;

namespace Wakeel.Crypto.Tests;

public class DeviceIdentityTests
{
    [Fact]
    public void A_generated_identity_has_two_public_keys_of_thirty_two_bytes()
    {
        using var device = DeviceIdentity.Generate();

        Assert.Equal(DeviceIdentity.PublicKeySize, device.SigningPublicKey.Length);
        Assert.Equal(DeviceIdentity.PublicKeySize, device.AgreementPublicKey.Length);
        Assert.Equal(device.SigningPublicKey, Base64Url.Decode(device.SigningPublicKeyText));
        Assert.Equal(device.AgreementPublicKey, Base64Url.Decode(device.AgreementPublicKeyText));
    }

    [Fact]
    public void Exported_seeds_rebuild_the_same_identity()
    {
        using var device = DeviceIdentity.Generate();
        var seeds = device.Export();

        using var restored = DeviceIdentity.Import(seeds);

        Assert.Equal(device.SigningPublicKey, restored.SigningPublicKey);
        Assert.Equal(device.AgreementPublicKey, restored.AgreementPublicKey);
        Assert.Equal(DeviceSeeds.SeedSize, seeds.SigningSeed.Length);
        Assert.Equal(DeviceSeeds.SeedSize, seeds.AgreementSeed.Length);
    }

    [Fact]
    public void Seeds_of_the_wrong_length_are_refused()
    {
        var error = Assert.Throws<CryptoException>(
            () => DeviceIdentity.Import(new DeviceSeeds(RandomBytes.Next(16), RandomBytes.Next(32))));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_signature_verifies_and_a_changed_message_does_not()
    {
        using var device = DeviceIdentity.Generate();
        var message = Encoding.UTF8.GetBytes("كتاب صادر معتمد");

        var signature = device.Sign(message);

        Assert.Equal(DeviceIdentity.SignatureSize, signature.Length);
        Assert.True(DeviceIdentity.Verify(device.SigningPublicKey, message, signature));
        Assert.False(DeviceIdentity.Verify(device.SigningPublicKey, Encoding.UTF8.GetBytes("كتاب آخر"), signature));
    }

    [Fact]
    public void Another_device_cannot_verify_a_signature_as_its_own()
    {
        using var first = DeviceIdentity.Generate();
        using var second = DeviceIdentity.Generate();
        var message = RandomBytes.Next(64);

        var signature = first.Sign(message);

        Assert.False(DeviceIdentity.Verify(second.SigningPublicKey, message, signature));
    }

    [Fact]
    public void A_malformed_public_key_or_signature_verifies_as_false()
    {
        using var device = DeviceIdentity.Generate();
        var message = RandomBytes.Next(32);
        var signature = device.Sign(message);

        Assert.False(DeviceIdentity.Verify(RandomBytes.Next(8), message, signature));
        Assert.False(DeviceIdentity.Verify(device.SigningPublicKey, message, RandomBytes.Next(8)));
        Assert.False(DeviceIdentity.Verify("not base64url!!", message, Base64Url.Encode(signature)));
    }

    [Fact]
    public void Both_sides_of_an_agreement_derive_the_same_session_key()
    {
        using var pc = DeviceIdentity.Generate();
        using var phone = DeviceIdentity.Generate();

        var fromPc = pc.Agree(phone.AgreementPublicKey, "wakeel.phone.session");
        var fromPhone = phone.Agree(pc.AgreementPublicKey, "wakeel.phone.session");

        Assert.Equal(fromPc, fromPhone);
        Assert.Equal(32, fromPc.Length);
    }

    [Fact]
    public void A_different_context_label_gives_a_different_session_key()
    {
        using var pc = DeviceIdentity.Generate();
        using var phone = DeviceIdentity.Generate();

        Assert.NotEqual(
            pc.Agree(phone.AgreementPublicKey, "wakeel.one"),
            pc.Agree(phone.AgreementPublicKey, "wakeel.two"));
    }

    [Fact]
    public void A_third_device_does_not_reach_the_same_session_key()
    {
        using var pc = DeviceIdentity.Generate();
        using var phone = DeviceIdentity.Generate();
        using var stranger = DeviceIdentity.Generate();

        Assert.NotEqual(
            pc.Agree(phone.AgreementPublicKey, "wakeel.phone.session"),
            stranger.Agree(phone.AgreementPublicKey, "wakeel.phone.session"));
    }

    [Fact]
    public void A_sealed_box_opens_only_for_its_recipient()
    {
        using var recipient = DeviceIdentity.Generate();
        using var stranger = DeviceIdentity.Generate();
        var payload = Encoding.UTF8.GetBytes("مفتاح المكتب");

        var box = DeviceIdentity.SealFor(recipient.AgreementPublicKey, payload, "wakeel.test.seal");

        Assert.Equal(payload, recipient.Open(box, "wakeel.test.seal"));
        Assert.Throws<CryptoException>(() => stranger.Open(box, "wakeel.test.seal"));
    }

    [Fact]
    public void A_sealed_box_refuses_a_different_context_label()
    {
        using var recipient = DeviceIdentity.Generate();
        var box = DeviceIdentity.SealFor(recipient.AgreementPublicKey, RandomBytes.Next(32), "wakeel.one");

        var error = Assert.Throws<CryptoException>(() => recipient.Open(box, "wakeel.two"));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void A_sealed_box_refuses_a_flipped_bit()
    {
        using var recipient = DeviceIdentity.Generate();
        var box = DeviceIdentity.SealFor(recipient.AgreementPublicKey, RandomBytes.Next(64), "wakeel.test.seal");
        box[^1] ^= 0x01;

        var error = Assert.Throws<CryptoException>(() => recipient.Open(box, "wakeel.test.seal"));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void Sealing_twice_produces_different_bytes()
    {
        using var recipient = DeviceIdentity.Generate();
        var payload = RandomBytes.Next(32);

        var first = DeviceIdentity.SealFor(recipient.AgreementPublicKey, payload, "wakeel.test.seal");
        var second = DeviceIdentity.SealFor(recipient.AgreementPublicKey, payload, "wakeel.test.seal");

        Assert.NotEqual(Convert.ToHexString(first), Convert.ToHexString(second));
        Assert.Equal(payload, recipient.Open(first, "wakeel.test.seal"));
        Assert.Equal(payload, recipient.Open(second, "wakeel.test.seal"));
    }

    [Fact]
    public void A_recipient_key_of_the_wrong_length_is_refused()
    {
        var error = Assert.Throws<CryptoException>(
            () => DeviceIdentity.SealFor(RandomBytes.Next(16), RandomBytes.Next(32), "wakeel.test.seal"));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }
}
