namespace Wakeel.Crypto.Tests;

public class KeyWrapTests
{
    private static Argon2Params CheapKdf() => new(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(16));

    [Fact]
    public void A_password_wrap_gives_the_key_back()
    {
        var dbKey = RandomBytes.Next(32);
        var wrap = KeyWraps.FromPassword("كلمة مرور طويلة", dbKey, CheapKdf(), KeyWraps.DbKeyContext);

        Assert.Equal(KeyWrapKind.Password, wrap.Kind);
        Assert.Equal(dbKey, KeyWraps.OpenWithPassword(wrap, "كلمة مرور طويلة", KeyWraps.DbKeyContext));
    }

    [Fact]
    public void A_wrap_whose_two_salts_disagree_is_reported_as_damaged()
    {
        var wrap = KeyWraps.FromPassword("secret", RandomBytes.Next(32), CheapKdf(), KeyWraps.DbKeyContext);
        var edited = wrap with { Salt = RandomBytes.Next(16) };

        var error = Assert.Throws<CryptoException>(
            () => KeyWraps.OpenWithPassword(edited, "secret", KeyWraps.DbKeyContext));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
        Assert.Equal(wrap.Kdf!.Salt, wrap.Salt);
    }

    [Fact]
    public void A_wrong_password_is_reported_as_a_wrong_password()
    {
        var wrap = KeyWraps.FromPassword("correct", RandomBytes.Next(32), CheapKdf(), KeyWraps.DbKeyContext);

        var error = Assert.Throws<CryptoException>(
            () => KeyWraps.OpenWithPassword(wrap, "incorrect", KeyWraps.DbKeyContext));
        Assert.Equal(ErrorCode.WrongPassword, error.Code);
    }

    [Fact]
    public void A_wrap_of_the_database_key_does_not_open_as_the_vault_key()
    {
        var wrap = KeyWraps.FromPassword("secret", RandomBytes.Next(32), CheapKdf(), KeyWraps.DbKeyContext);

        var error = Assert.Throws<CryptoException>(
            () => KeyWraps.OpenWithPassword(wrap, "secret", KeyWraps.VaultKeyContext));
        Assert.Equal(ErrorCode.WrongPassword, error.Code);
    }

    [Fact]
    public void A_recovery_wrap_opens_with_the_printed_code()
    {
        var code = RecoveryCode.Generate();
        var vaultKey = RandomBytes.Next(32);
        var wrap = KeyWraps.FromRecoveryCode(code, vaultKey, CheapKdf(), KeyWraps.VaultKeyContext);

        var retyped = RecoveryCode.Parse(code.Display.ToLowerInvariant());

        Assert.Equal(KeyWrapKind.Recovery, wrap.Kind);
        Assert.Equal(vaultKey, KeyWraps.OpenWithRecoveryCode(wrap, retyped, KeyWraps.VaultKeyContext));
    }

    [Fact]
    public void Another_recovery_code_does_not_open_the_wrap()
    {
        var wrap = KeyWraps.FromRecoveryCode(RecoveryCode.Generate(), RandomBytes.Next(32), CheapKdf(), KeyWraps.VaultKeyContext);

        var error = Assert.Throws<CryptoException>(
            () => KeyWraps.OpenWithRecoveryCode(wrap, RecoveryCode.Generate(), KeyWraps.VaultKeyContext));
        Assert.Equal(ErrorCode.WrongPassword, error.Code);
    }

    [Fact]
    public void A_machine_wrap_opens_through_the_platform_protector()
    {
        var protector = new NullPlatformProtector();
        var dbKey = RandomBytes.Next(32);

        var wrap = KeyWraps.FromMachine(protector, dbKey, KeyWraps.DbKeyContext);

        Assert.Equal(KeyWrapKind.Machine, wrap.Kind);
        Assert.Null(wrap.Kdf);
        Assert.Equal(dbKey, KeyWraps.OpenWithMachine(wrap, protector, KeyWraps.DbKeyContext));
    }

    [Fact]
    public void An_admin_wrap_opens_only_with_the_organisation_keys()
    {
        using var org = DeviceIdentity.Generate();
        using var stranger = DeviceIdentity.Generate();
        var dbKey = RandomBytes.Next(32);

        var wrap = KeyWraps.ForAdmin(org.AgreementPublicKey, dbKey, KeyWraps.DbKeyContext);

        Assert.Equal(dbKey, KeyWraps.OpenAsAdmin(wrap, org, KeyWraps.DbKeyContext));

        // KeyWraps.OpenAsAdmin must report the same code as ContainerKeySource.OpenContentKey
        // does for the identical situation (an admin recovery copy opened with the wrong keys),
        // so a caller maps one code to one user-facing message everywhere.
        var error = Assert.Throws<CryptoException>(() => KeyWraps.OpenAsAdmin(wrap, stranger, KeyWraps.DbKeyContext));
        Assert.Equal(ErrorCode.WrongPassword, error.Code);
    }

    [Fact]
    public void A_wrap_cannot_be_opened_the_wrong_way()
    {
        var wrap = KeyWraps.FromMachine(new NullPlatformProtector(), RandomBytes.Next(32), KeyWraps.DbKeyContext);

        var error = Assert.Throws<CryptoException>(
            () => KeyWraps.OpenWithPassword(wrap, "anything", KeyWraps.DbKeyContext));
        Assert.Equal(ErrorCode.UnknownKind, error.Code);
    }

    [Fact]
    public void A_wrap_survives_the_canonical_json_round_trip()
    {
        var dbKey = RandomBytes.Next(32);
        var wrap = KeyWraps.FromPassword("secret", dbKey, CheapKdf(), KeyWraps.DbKeyContext);

        var restored = CanonicalJson.Deserialize<KeyWrap>(CanonicalJson.SerializeToUtf8Bytes(wrap));

        Assert.Equal(wrap, restored);
        Assert.Equal(dbKey, KeyWraps.OpenWithPassword(restored, "secret", KeyWraps.DbKeyContext));
    }
}
