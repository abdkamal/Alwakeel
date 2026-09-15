namespace Wakeel.Crypto.Tests;

public class InstallationKeyFileTests
{
    private static Argon2Params CheapKdf() => new(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(16));

    [Fact]
    public void An_installation_file_round_trips_through_disk()
    {
        using var folder = new TempFolder();
        var clock = FixedClock.At(2026, 9, 15);
        var path = folder.File("installation.key");

        var dbKey = RandomBytes.Next(32);
        var vaultKey = RandomBytes.Next(32);
        var code = RecoveryCode.Generate();

        var file = InstallationKeyFile.Create(clock);
        file.SetDbKeyWrap(KeyWraps.FromPassword("secret", dbKey, CheapKdf(), KeyWraps.DbKeyContext));
        file.SetDbKeyWrap(KeyWraps.FromRecoveryCode(code, dbKey, CheapKdf(), KeyWraps.DbKeyContext));
        file.SetVaultKeyWrap(KeyWraps.FromPassword("secret", vaultKey, CheapKdf(), KeyWraps.VaultKeyContext));
        file.Save(path, clock);

        var loaded = InstallationKeyFile.Load(path);

        Assert.Equal(InstallationKeyFile.CurrentVersion, loaded.Version);
        Assert.Equal(2, loaded.DbKeyWraps.Count);
        Assert.Single(loaded.VaultKeyWraps);
        Assert.Equal(dbKey, KeyWraps.OpenWithPassword(loaded.FindDbKeyWrap(KeyWrapKind.Password)!, "secret", KeyWraps.DbKeyContext));
        Assert.Equal(dbKey, KeyWraps.OpenWithRecoveryCode(loaded.FindDbKeyWrap(KeyWrapKind.Recovery)!, code, KeyWraps.DbKeyContext));
        Assert.Equal(vaultKey, KeyWraps.OpenWithPassword(loaded.FindVaultKeyWrap(KeyWrapKind.Password)!, "secret", KeyWraps.VaultKeyContext));
    }

    [Fact]
    public void Saving_twice_leaves_a_backup_copy_and_no_temporary_file()
    {
        using var folder = new TempFolder();
        var clock = FixedClock.At(2026, 9, 15);
        var path = folder.File("installation.key");

        var file = InstallationKeyFile.Create(clock);
        file.SetDbKeyWrap(KeyWraps.FromMachine(new NullPlatformProtector(), RandomBytes.Next(32), KeyWraps.DbKeyContext));
        file.Save(path, clock);
        var firstBytes = File.ReadAllBytes(path);

        clock.Advance(TimeSpan.FromMinutes(5));
        file.SetVaultKeyWrap(KeyWraps.FromMachine(new NullPlatformProtector(), RandomBytes.Next(32), KeyWraps.VaultKeyContext));
        file.Save(path, clock);

        Assert.True(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(firstBytes, File.ReadAllBytes(path + ".bak"));
        Assert.NotEqual(firstBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void The_device_seeds_are_stored_under_the_database_key()
    {
        using var folder = new TempFolder();
        var clock = FixedClock.At(2026, 9, 15);
        var path = folder.File("installation.key");

        var dbKey = RandomBytes.Next(32);
        using var device = DeviceIdentity.Generate();
        var seeds = device.Export();

        var file = InstallationKeyFile.Create(clock);
        file.SetDeviceSeeds(dbKey, seeds);
        file.Save(path, clock);

        var loaded = InstallationKeyFile.Load(path);
        Assert.True(loaded.HasDeviceSeeds);

        var restored = loaded.ReadDeviceSeeds(dbKey);
        Assert.Equal(seeds, restored);

        using var reborn = DeviceIdentity.Import(restored);
        Assert.Equal(device.SigningPublicKeyText, reborn.SigningPublicKeyText);
        Assert.Equal(device.AgreementPublicKeyText, reborn.AgreementPublicKeyText);
    }

    [Fact]
    public void Another_database_key_cannot_read_the_device_seeds()
    {
        using var device = DeviceIdentity.Generate();
        var file = InstallationKeyFile.Create(FixedClock.At(2026, 9, 15));
        file.SetDeviceSeeds(RandomBytes.Next(32), device.Export());

        var error = Assert.Throws<CryptoException>(() => file.ReadDeviceSeeds(RandomBytes.Next(32)));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void A_missing_file_is_reported_as_corrupt()
    {
        using var folder = new TempFolder();

        var error = Assert.Throws<CryptoException>(() => InstallationKeyFile.Load(folder.File("absent.key")));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_file_from_a_newer_version_is_refused()
    {
        using var folder = new TempFolder();
        var path = folder.File("installation.key");
        File.WriteAllText(path, """{"version":99,"createdAt":"2026-09-15T00:00:00.000Z","updatedAt":"2026-09-15T00:00:00.000Z","dbKeyWraps":[],"vaultKeyWraps":[]}""");

        var error = Assert.Throws<CryptoException>(() => InstallationKeyFile.Load(path));
        Assert.Equal(ErrorCode.UnknownKind, error.Code);
    }

    [Fact]
    public void Replacing_a_wrap_of_the_same_kind_keeps_one_copy()
    {
        var file = InstallationKeyFile.Create(FixedClock.At(2026, 9, 15));
        var newKey = RandomBytes.Next(32);

        file.SetDbKeyWrap(KeyWraps.FromPassword("old", RandomBytes.Next(32), CheapKdf(), KeyWraps.DbKeyContext));
        file.SetDbKeyWrap(KeyWraps.FromPassword("new", newKey, CheapKdf(), KeyWraps.DbKeyContext));

        Assert.Single(file.DbKeyWraps);
        Assert.Equal(newKey, KeyWraps.OpenWithPassword(file.DbKeyWraps[0], "new", KeyWraps.DbKeyContext));
    }
}
