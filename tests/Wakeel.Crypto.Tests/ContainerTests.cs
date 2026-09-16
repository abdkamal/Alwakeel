using System.Text;

namespace Wakeel.Crypto.Tests;

public class ContainerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ContainerKind.Setup, ".wakeel-setup")]
    [InlineData(ContainerKind.Sync, ".wakeel-sync")]
    [InlineData(ContainerKind.Msg, ".wakeel-msg")]
    [InlineData(ContainerKind.Transfer, ".wakeel-transfer")]
    [InlineData(ContainerKind.Inventory, ".wakeel-inventory")]
    [InlineData(ContainerKind.Phone, ".wakeel-phone")]
    [InlineData(ContainerKind.Backup, ".wakeel-backup")]
    public void Every_kind_has_its_own_extension(ContainerKind kind, string extension)
    {
        Assert.Equal(extension, ContainerKinds.Extension(kind));
        Assert.Equal(kind, ContainerKinds.FromPath("packet" + extension));
        Assert.Equal(kind, ContainerKinds.FromPath(extension));
    }

    [Fact]
    public void An_unknown_extension_is_refused()
    {
        var error = Assert.Throws<CryptoException>(() => ContainerKinds.FromPath("packet.zip"));
        Assert.Equal(ErrorCode.UnknownKind, error.Code);
    }

    [Fact]
    public void An_office_key_container_round_trips()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);
        var payload = Encoding.UTF8.GetBytes("سجلات من تاريخ إلى تاريخ");

        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(officeKey),
            ContainerEntrySource.FromBytes("records.json", payload));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Sync));
        var entries = reader.ReadEntries(ContainerKeySource.OfficeKey(officeKey));

        Assert.Equal(ContainerKind.Sync, reader.Manifest.Type);
        Assert.Equal(PayloadMode.OfficeKey, reader.Manifest.Mode);
        Assert.Equal(Now, reader.Manifest.CreatedAt);
        Assert.Equal(payload, entries["records.json"]);
        Assert.Equal(payload.Length, reader.Manifest.Entries[0].Size);
        Assert.Equal(Sha256.HashHex(payload), reader.Manifest.Entries[0].Sha256);
    }

    [Fact]
    public void A_wrong_office_key_cannot_open_the_payload()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Sync));

        var error = Assert.Throws<CryptoException>(
            () => reader.ReadEntries(ContainerKeySource.OfficeKey(RandomBytes.Next(32))));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void A_password_container_round_trips_and_refuses_a_wrong_password()
    {
        using var world = new ContainerWorld();
        var kdf = new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(16));
        var payload = Encoding.UTF8.GetBytes("ملف الإعداد");

        var path = world.Write(
            ContainerKind.Setup,
            ContainerKeySource.FromPassword("كلمة مرور الحزمة", kdf),
            ContainerEntrySource.FromBytes("setup.json", payload));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Setup));

        Assert.Equal(PayloadMode.Password, reader.Manifest.Mode);
        Assert.NotNull(reader.Manifest.Kdf);
        Assert.Equal(payload, reader.ReadEntry("setup.json", ContainerKeySource.FromPassword("كلمة مرور الحزمة")));

        var error = Assert.Throws<CryptoException>(
            () => reader.ReadEntries(ContainerKeySource.FromPassword("كلمة أخرى")));
        Assert.Equal(ErrorCode.WrongPassword, error.Code);
    }

    [Fact]
    public void A_sealed_container_opens_only_for_its_recipient()
    {
        using var world = new ContainerWorld();
        using var recipient = DeviceIdentity.Generate();
        using var stranger = DeviceIdentity.Generate();
        var payload = Encoding.UTF8.GetBytes("مراسلة معتمدة");

        var path = world.Write(
            ContainerKind.Msg,
            ContainerKeySource.SealFor(recipient.AgreementPublicKey),
            ContainerEntrySource.FromBytes("message.json", payload));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Msg));

        Assert.Equal(PayloadMode.SealedFor, reader.Manifest.Mode);
        Assert.False(string.IsNullOrEmpty(reader.Manifest.SealedKey));
        Assert.Equal(payload, reader.ReadEntry("message.json", ContainerKeySource.ForRecipient(recipient)));

        var error = Assert.Throws<CryptoException>(
            () => reader.ReadEntries(ContainerKeySource.ForRecipient(stranger)));
        Assert.Equal(ErrorCode.WrongPassword, error.Code);
    }

    [Fact]
    public void A_session_key_container_round_trips()
    {
        using var world = new ContainerWorld();
        var sessionKey = world.Pki.Pc.Agree(world.Pki.Phone.AgreementPublicKey, "wakeel.phone.session");
        var payload = RandomBytes.Next(5000);

        var path = world.Write(
            ContainerKind.Phone,
            ContainerKeySource.SessionKey(sessionKey),
            ContainerEntrySource.FromBytes("to-phone-1.bin", payload));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Phone));
        var phoneSide = world.Pki.Phone.Agree(world.Pki.Pc.AgreementPublicKey, "wakeel.phone.session");

        Assert.Equal(payload, reader.ReadEntry("to-phone-1.bin", ContainerKeySource.SessionKey(phoneSide)));
    }

    [Fact]
    public void Opening_a_container_with_the_wrong_mode_is_refused()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(officeKey),
            ContainerEntrySource.FromText("records.json", "{}"));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Sync));

        var error = Assert.Throws<CryptoException>(
            () => reader.ReadEntries(ContainerKeySource.FromPassword("anything")));
        Assert.Equal(ErrorCode.UnknownKind, error.Code);
    }

    [Fact]
    public void A_container_of_another_kind_is_refused()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        var error = Assert.Throws<CryptoException>(
            () => ContainerReader.Open(path, world.Options(ContainerKind.Backup)));
        Assert.Equal(ErrorCode.UnknownKind, error.Code);
    }

    [Fact]
    public void A_tampered_manifest_is_refused_before_anything_is_decrypted()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        ZipSurgery.Replace(path, ContainerManifest.FileName, bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes).Replace("\"deviceId\":\"PC-1\"", "\"deviceId\":\"PC-9\"", StringComparison.Ordinal);
            return Encoding.UTF8.GetBytes(text);
        });

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void A_tampered_payload_is_refused_before_anything_is_decrypted()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromBytes("records.json", RandomBytes.Next(4000)));

        ZipSurgery.Replace(path, ContainerManifest.PayloadFileName, ZipSurgery.FlipLastByte);

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void A_tampered_signature_is_refused()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        ZipSurgery.Replace(path, ContainerManifest.SignatureFileName, ZipSurgery.FlipLastByte);

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void A_payload_replaced_and_re_signed_still_fails_its_integrity_check()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(officeKey),
            ContainerEntrySource.FromBytes("records.json", RandomBytes.Next(4000)));

        var entries = ZipSurgery.ReadAll(path);
        entries[ContainerManifest.PayloadFileName] = ZipSurgery.FlipLastByte(entries[ContainerManifest.PayloadFileName]);
        var signingInput = ContainerWriter.SigningInput(
            Sha256.Hash(entries[ContainerManifest.FileName]),
            Sha256.Hash(entries[ContainerManifest.PayloadFileName]));
        entries[ContainerManifest.SignatureFileName] = world.Pki.Pc.Sign(signingInput);
        ZipSurgery.WriteAll(path, entries);

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Sync));

        var error = Assert.Throws<CryptoException>(() => reader.ReadEntries(ContainerKeySource.OfficeKey(officeKey)));
        Assert.Equal(ErrorCode.Tampered, error.Code);
    }

    [Fact]
    public void A_missing_part_is_reported_as_corrupt()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        var entries = ZipSurgery.ReadAll(path);
        entries.Remove(ContainerManifest.SignatureFileName);
        ZipSurgery.WriteAll(path, entries);

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_file_that_is_not_a_container_is_reported_as_corrupt()
    {
        using var world = new ContainerWorld();
        var path = world.Folder.File("not-a-container.wakeel-sync");
        File.WriteAllText(path, "hello");

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_container_dated_more_than_a_day_ahead_is_refused()
    {
        using var world = new ContainerWorld(Now.AddDays(3));
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        var options = new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Sync,
            OrgSigningPublicKey = world.Pki.Org.SigningPublicKey,
            Time = new FixedClock(Now),
        };

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, options));
        Assert.Equal(ErrorCode.Expired, error.Code);
    }

    [Fact]
    public void A_container_from_a_revoked_device_is_refused()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        var options = new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Sync,
            OrgSigningPublicKey = world.Pki.Org.SigningPublicKey,
            Revocations = world.Pki.Revoke(TestPki.PcDeviceId, Now.AddHours(-1)),
            Time = new FixedClock(Now),
        };

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, options));
        Assert.Equal(ErrorCode.Revoked, error.Code);
    }

    [Fact]
    public void A_container_produced_by_a_phone_verifies_through_the_computer_certificate()
    {
        using var world = new ContainerWorld();
        var sessionKey = RandomBytes.Next(32);

        var path = world.Folder.File("from-phone-1.wakeel-phone");
        ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = ContainerKind.Phone,
            Producer = world.Pki.PhoneCertificate,
            Signer = world.Pki.Phone,
            Key = ContainerKeySource.SessionKey(sessionKey),
            Entries = [ContainerEntrySource.FromText("expenses.json", "[]")],
            Time = new FixedClock(Now),
        });

        var options = new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Phone,
            OrgSigningPublicKey = world.Pki.Org.SigningPublicKey,
            IssuerCertificate = world.Pki.PcCertificate,
            Time = new FixedClock(Now),
        };

        using var reader = ContainerReader.Open(path, options);

        Assert.Equal("[]", Encoding.UTF8.GetString(reader.ReadEntry("expenses.json", ContainerKeySource.SessionKey(sessionKey))));
    }

    [Fact]
    public void Signing_with_keys_that_do_not_match_the_certificate_is_refused()
    {
        using var world = new ContainerWorld();
        using var stranger = DeviceIdentity.Generate();

        var error = Assert.Throws<CryptoException>(() => ContainerWriter.Write(
            world.Folder.File("bad.wakeel-sync"),
            new ContainerWriteRequest
            {
                Kind = ContainerKind.Sync,
                Producer = world.Pki.PcCertificate,
                Signer = stranger,
                Key = ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
                Entries = [ContainerEntrySource.FromText("records.json", "{}")],
                Time = new FixedClock(Now),
            }));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void A_large_payload_streams_to_disk_and_comes_back_unchanged()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);
        var big = RandomBytes.Next(3_000_000);
        var small = Encoding.UTF8.GetBytes("فهرس");

        var path = world.Write(
            ContainerKind.Backup,
            ContainerKeySource.OfficeKey(officeKey),
            ContainerEntrySource.FromBytes("database.bin", big),
            ContainerEntrySource.FromBytes("index.json", small));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Backup));
        var target = Path.Combine(world.Folder.Path, "extracted");
        reader.ExtractTo(target, ContainerKeySource.OfficeKey(officeKey));

        Assert.Equal(2, reader.Manifest.Entries.Count);
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(target, "database.bin")));
        Assert.Equal(small, File.ReadAllBytes(Path.Combine(target, "index.json")));
    }

    [Fact]
    public void A_container_must_carry_at_least_one_entry()
    {
        using var world = new ContainerWorld();

        var error = Assert.Throws<CryptoException>(() => ContainerWriter.Write(
            world.Folder.File("empty.wakeel-sync"),
            new ContainerWriteRequest
            {
                Kind = ContainerKind.Sync,
                Producer = world.Pki.PcCertificate,
                Signer = world.Pki.Pc,
                Key = ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
                Entries = [],
                Time = new FixedClock(Now),
            }));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void Opening_a_container_without_the_organisation_key_is_refused()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        var options = new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Sync,
            Time = new FixedClock(Now),
        };

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, options));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void Skipping_the_certificate_check_has_to_be_asked_for_in_so_many_words()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(officeKey),
            ContainerEntrySource.FromText("records.json", "{}"));

        var options = new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Sync,
            VerifyCertificateChain = false,
            Time = new FixedClock(Now),
        };

        using var reader = ContainerReader.Open(path, options);

        Assert.Equal(ContainerKind.Sync, reader.Manifest.Type);
        Assert.Equal("{}", Encoding.UTF8.GetString(reader.ReadEntry("records.json", ContainerKeySource.OfficeKey(officeKey))));
    }

    [Fact]
    public void A_container_from_a_certificate_nobody_issued_is_refused()
    {
        using var world = new ContainerWorld();
        using var impostor = DeviceIdentity.Generate();

        // The impostor writes its own certificate, names the real organisation as its issuer
        // and signs the container with the matching keys: every self contained check passes.
        var selfIssued = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                TestPki.OrgId,
                TestPki.OfficeId,
                "PC-99",
                1,
                2,
                "secretary",
                DeviceKind.Pc,
                impostor.SigningPublicKeyText,
                impostor.AgreementPublicKeyText,
                Now,
                TestPki.OrgId),
            impostor);

        var path = world.Folder.File("forged.wakeel-sync");
        ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = ContainerKind.Sync,
            Producer = selfIssued,
            Signer = impostor,
            Key = ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            Entries = [ContainerEntrySource.FromText("records.json", "{}")],
            Time = new FixedClock(Now),
        });

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.BadSignature, error.Code);
    }

    [Fact]
    public void A_manifest_that_names_a_path_outside_the_folder_is_refused()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        ReplaceManifest(path, world, bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes)
                .Replace("\"name\":\"records.json\"", "\"name\":\"..\\\\outside\\\\evil.txt\"", StringComparison.Ordinal);
            return Encoding.UTF8.GetBytes(text);
        });

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_manifest_larger_than_the_product_ever_writes_is_refused_before_it_is_parsed()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        // A field the schema would silently ignore, padded past the bound: if the size were
        // only checked after the whole entry is in memory, this would still parse and pass
        // every later check, so the refusal has to come from the bound itself.
        var entries = ZipSurgery.ReadAll(path);
        var text = Encoding.UTF8.GetString(entries[ContainerManifest.FileName]);
        var padded = text.Insert(1, "\"padding\":\"" + new string('a', 2 * 1024 * 1024) + "\",");
        entries[ContainerManifest.FileName] = Encoding.UTF8.GetBytes(padded);
        ZipSurgery.WriteAll(path, entries);

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_signature_larger_than_the_product_ever_writes_is_refused_before_it_is_used()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        var entries = ZipSurgery.ReadAll(path);
        entries[ContainerManifest.SignatureFileName] = new byte[2 * 1024 * 1024];
        ZipSurgery.WriteAll(path, entries);

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_manifest_with_an_unreadable_date_is_reported_as_damaged()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        ReplaceManifest(path, world, bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes);
            var start = text.IndexOf("\"createdAt\":\"", StringComparison.Ordinal) + "\"createdAt\":\"".Length;
            var end = text.IndexOf('"', start);
            return Encoding.UTF8.GetBytes(text[..start] + "not-a-date" + text[end..]);
        });

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_manifest_whose_producer_has_no_contents_is_reported_as_damaged()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        ReplaceManifest(path, world, _ => Encoding.UTF8.GetBytes(
            "{\"type\":\"sync\",\"version\":1,\"mode\":\"officeKey\",\"createdAt\":\"2026-09-15T12:00:00.000Z\","
            + "\"producer\":{\"signature\":\"AA\"},"
            + "\"entries\":[{\"name\":\"records.json\",\"size\":2,\"sha256\":\"aa\"}]}"));

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_manifest_that_lists_nothing_is_reported_as_damaged()
    {
        using var world = new ContainerWorld();
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            ContainerEntrySource.FromText("records.json", "{}"));

        ReplaceManifest(path, world, bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes);
            var start = text.IndexOf("\"entries\":[", StringComparison.Ordinal);
            var end = text.IndexOf(']', start);
            return Encoding.UTF8.GetBytes(text[..(start + "\"entries\":[".Length)] + text[end..]);
        });

        var error = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, world.Options(ContainerKind.Sync)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void An_entry_name_that_climbs_out_of_the_folder_is_refused_when_it_is_written()
    {
        var error = Assert.Throws<CryptoException>(
            () => ContainerEntrySource.FromText("..\\outside\\evil.txt", "x"));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
        Assert.False(ContainerEntrySource.IsAcceptableName("/etc/passwd"));
        Assert.False(ContainerEntrySource.IsAcceptableName("c:evil.txt"));
        Assert.True(ContainerEntrySource.IsAcceptableName("vault/ab/file.bin"));
    }

    [Fact]
    public void An_entry_name_that_is_not_a_legal_windows_file_name_is_refused()
    {
        // Legal as a relative path, but not as an actual Windows file or folder name: a
        // wildcard character, a trailing slash, a trailing dot or space, and a reserved
        // DOS device name. ContainerReader.ExtractTo hands the name straight to FileStream,
        // so any of these must be caught while the manifest is validated, not left to raise
        // a raw IOException from the file system.
        Assert.False(ContainerEntrySource.IsAcceptableName("a*b.txt"));
        Assert.False(ContainerEntrySource.IsAcceptableName("a?b.txt"));
        Assert.False(ContainerEntrySource.IsAcceptableName("vault/ab/"));
        Assert.False(ContainerEntrySource.IsAcceptableName("trailing-dot."));
        Assert.False(ContainerEntrySource.IsAcceptableName("trailing-space "));
        Assert.False(ContainerEntrySource.IsAcceptableName("CON"));
        Assert.False(ContainerEntrySource.IsAcceptableName("CON.txt"));
        Assert.False(ContainerEntrySource.IsAcceptableName("con.txt"));
        Assert.False(ContainerEntrySource.IsAcceptableName("vault/LPT1.bin"));
        Assert.True(ContainerEntrySource.IsAcceptableName("connections.txt"));
        Assert.True(ContainerEntrySource.IsAcceptableName("vault/ab/file.bin"));
    }

    [Fact]
    public void Every_character_the_platform_refuses_in_a_file_name_is_refused_in_an_entry_name()
    {
        // The rule is not a list somebody typed out once, it is the platform's own list: on a
        // system whose list is longer, the extra characters are refused as well. Only the two
        // separators are exempt, and each of those has a rule of its own above.
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            var name = "vault/a" + character + "b.bin";
            if (character == '/')
            {
                Assert.True(ContainerEntrySource.IsAcceptableName(name));
                continue;
            }

            Assert.False(ContainerEntrySource.IsAcceptableName(name));
        }
    }

    [Fact]
    public void Two_office_key_containers_never_share_a_content_key()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);

        var firstPath = world.Folder.File("first.wakeel-sync");
        var secondPath = world.Folder.File("second.wakeel-sync");
        ContainerWriter.Write(firstPath, new ContainerWriteRequest
        {
            Kind = ContainerKind.Sync,
            Producer = world.Pki.PcCertificate,
            Signer = world.Pki.Pc,
            Key = ContainerKeySource.OfficeKey(officeKey),
            Entries = [ContainerEntrySource.FromText("records.json", "{}")],
            Time = new FixedClock(Now),
        });
        ContainerWriter.Write(secondPath, new ContainerWriteRequest
        {
            Kind = ContainerKind.Sync,
            Producer = world.Pki.PcCertificate,
            Signer = world.Pki.Pc,
            Key = ContainerKeySource.OfficeKey(officeKey),
            Entries = [ContainerEntrySource.FromText("records.json", "{}")],
            Time = new FixedClock(Now),
        });

        using var firstReader = ContainerReader.Open(firstPath, world.Options(ContainerKind.Sync));
        using var secondReader = ContainerReader.Open(secondPath, world.Options(ContainerKind.Sync));

        // Every packet must carry its own random salt, and no two packets from the same
        // office key may derive the same AES-256-GCM content key.
        Assert.NotNull(firstReader.Manifest.KeySalt);
        Assert.NotNull(secondReader.Manifest.KeySalt);
        Assert.False(firstReader.Manifest.KeySalt!.AsSpan().SequenceEqual(secondReader.Manifest.KeySalt!));

        // Both still round trip with the plain office key.
        Assert.Equal("{}", Encoding.UTF8.GetString(
            firstReader.ReadEntry("records.json", ContainerKeySource.OfficeKey(officeKey))));
        Assert.Equal("{}", Encoding.UTF8.GetString(
            secondReader.ReadEntry("records.json", ContainerKeySource.OfficeKey(officeKey))));
    }

    [Fact]
    public void A_container_missing_its_key_salt_is_reported_as_damaged()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(officeKey),
            ContainerEntrySource.FromText("records.json", "{}"));

        ReplaceManifest(path, world, bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes);
            const string key = "\"keySalt\":\"";
            var start = text.IndexOf(key, StringComparison.Ordinal);
            var closingQuote = text.IndexOf('"', start + key.Length);
            return Encoding.UTF8.GetBytes(text[..start] + "\"keySalt\":null" + text[(closingQuote + 1)..]);
        });

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Sync));
        var error = Assert.Throws<CryptoException>(
            () => reader.ReadEntry("records.json", ContainerKeySource.OfficeKey(officeKey)));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void The_manifest_it_returns_matches_what_a_reader_parses_back()
    {
        // ContainerWriter.Write returns the in-memory manifest before it is serialised. The
        // canonical JSON encoding of an instant only carries millisecond precision, so the
        // returned object must already reflect that truncation or it silently disagrees with
        // what a reader parses back from the signed bytes.
        using var world = new ContainerWorld();
        var subMillisecond = Now.AddTicks(1234);
        var path = world.Folder.File("sub-ms.wakeel-sync");

        var manifest = ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = ContainerKind.Sync,
            Producer = world.Pki.PcCertificate,
            Signer = world.Pki.Pc,
            Key = ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            Entries = [ContainerEntrySource.FromText("records.json", "{}")],
            Time = new FixedClock(subMillisecond),
        });

        using var reader = ContainerReader.Open(path, new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Sync,
            OrgSigningPublicKey = world.Pki.Org.SigningPublicKey,
            Time = new FixedClock(subMillisecond),
        });

        Assert.Equal(reader.Manifest.CreatedAt, manifest.CreatedAt);
        Assert.NotEqual(subMillisecond, manifest.CreatedAt);
    }

    [Fact]
    public void An_entry_larger_than_the_manifest_declares_is_refused_without_buffering_it_whole()
    {
        using var world = new ContainerWorld();
        var officeKey = RandomBytes.Next(32);
        var payload = Encoding.UTF8.GetBytes("this content is longer than the manifest will admit to");
        var path = world.Write(
            ContainerKind.Sync,
            ContainerKeySource.OfficeKey(officeKey),
            ContainerEntrySource.FromBytes("records.json", payload));

        // The signed manifest is edited to understate the entry's size, the way a hostile inner
        // zip that lies about a small size while actually expanding to something huge would.
        // The size check inside CheckEntry would eventually catch this too, but only after the
        // whole oversized entry has been copied into memory or onto disk; the fix is supposed to
        // stop the copy as soon as the declared limit is passed.
        ReplaceManifest(path, world, bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes);
            return Encoding.UTF8.GetBytes(text.Replace(
                $"\"size\":{payload.Length}",
                "\"size\":1",
                StringComparison.Ordinal));
        });

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Sync));

        var readEntryError = Assert.Throws<CryptoException>(
            () => reader.ReadEntry("records.json", ContainerKeySource.OfficeKey(officeKey)));
        Assert.Equal(ErrorCode.Tampered, readEntryError.Code);

        // ExtractTo carries its own copy of the same limit, and it is the path that writes to
        // the disk rather than to memory, so it is asserted here too: a change that dropped the
        // limit on one of the two paths would otherwise go unnoticed.
        var extractError = Assert.Throws<CryptoException>(
            () => reader.ExtractTo(world.Folder.File("out"), ContainerKeySource.OfficeKey(officeKey)));
        Assert.Equal(ErrorCode.Tampered, extractError.Code);
    }

    [Fact]
    public void The_administrator_opens_a_password_backup_without_the_password()
    {
        using var world = new ContainerWorld();
        using var stranger = DeviceIdentity.Generate();
        var kdf = new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(16));
        var payload = Encoding.UTF8.GetBytes("القاعدة كاملة");

        var path = world.Write(
            ContainerKind.Backup,
            ContainerKeySource.FromPassword("كلمة مرور النسخة", kdf),
            ContainerEntrySource.FromBytes("database.bin", payload));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Backup));

        Assert.False(string.IsNullOrEmpty(reader.Manifest.AdminSealedKey));
        Assert.Equal(payload, reader.ReadEntry("database.bin", ContainerKeySource.ForAdmin(world.Pki.Org)));

        var error = Assert.Throws<CryptoException>(
            () => reader.ReadEntries(ContainerKeySource.ForAdmin(stranger)));
        Assert.Equal(ErrorCode.WrongPassword, error.Code);
    }

    [Fact]
    public void The_administrator_opens_a_sealed_message_without_the_recipient_device()
    {
        using var world = new ContainerWorld();
        using var recipient = DeviceIdentity.Generate();
        var payload = Encoding.UTF8.GetBytes("مراسلة معتمدة");

        var path = world.Write(
            ContainerKind.Msg,
            ContainerKeySource.SealFor(recipient.AgreementPublicKey),
            ContainerEntrySource.FromBytes("message.json", payload));

        using var reader = ContainerReader.Open(path, world.Options(ContainerKind.Msg));

        Assert.Equal(payload, reader.ReadEntry("message.json", ContainerKeySource.ForAdmin(world.Pki.Org)));
        Assert.Equal(payload, reader.ReadEntry("message.json", ContainerKeySource.ForRecipient(recipient)));
    }

    [Fact]
    public void A_backup_without_a_copy_for_the_administrator_is_refused()
    {
        using var world = new ContainerWorld();

        var error = Assert.Throws<CryptoException>(() => ContainerWriter.Write(
            world.Folder.File("no-admin.wakeel-backup"),
            new ContainerWriteRequest
            {
                Kind = ContainerKind.Backup,
                Producer = world.Pki.PcCertificate,
                Signer = world.Pki.Pc,
                Key = ContainerKeySource.FromPassword("كلمة مرور النسخة"),
                Entries = [ContainerEntrySource.FromText("database.bin", "x")],
                Time = new FixedClock(Now),
            }));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
        Assert.False(File.Exists(world.Folder.File("no-admin.wakeel-backup")));
        Assert.False(File.Exists(world.Folder.File("no-admin.wakeel-backup.tmp")));
    }

    [Fact]
    public void A_sync_packet_carries_no_copy_for_the_administrator_when_none_is_offered()
    {
        using var world = new ContainerWorld();
        var path = world.Folder.File("plain.wakeel-sync");
        var manifest = ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = ContainerKind.Sync,
            Producer = world.Pki.PcCertificate,
            Signer = world.Pki.Pc,
            Key = ContainerKeySource.OfficeKey(RandomBytes.Next(32)),
            Entries = [ContainerEntrySource.FromText("records.json", "{}")],
            Time = new FixedClock(Now),
        });

        Assert.Null(manifest.AdminSealedKey);
    }

    [Fact]
    public void The_payload_is_staged_where_the_caller_asks_and_nothing_is_left_behind()
    {
        using var world = new ContainerWorld();
        var staging = Path.Combine(world.Folder.Path, "staging");
        var officeKey = RandomBytes.Next(32);
        var payload = RandomBytes.Next(300_000);

        var path = world.Folder.File("staged.wakeel-sync");
        ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = ContainerKind.Sync,
            Producer = world.Pki.PcCertificate,
            Signer = world.Pki.Pc,
            Key = ContainerKeySource.OfficeKey(officeKey),
            Entries = [ContainerEntrySource.FromBytes("records.json", payload)],
            StagingDirectory = staging,
            Time = new FixedClock(Now),
        });

        var options = new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Sync,
            OrgSigningPublicKey = world.Pki.Org.SigningPublicKey,
            StagingDirectory = staging,
            Time = new FixedClock(Now),
        };

        using (var reader = ContainerReader.Open(path, options))
        {
            Assert.Equal(payload, reader.ReadEntry("records.json", ContainerKeySource.OfficeKey(officeKey)));
        }

        Assert.Empty(Directory.GetFiles(staging));
    }

    /// <summary>Rewrites the manifest and re-signs it, so a test isolates one single check.</summary>
    private static void ReplaceManifest(string path, ContainerWorld world, Func<byte[], byte[]> transform)
    {
        var entries = ZipSurgery.ReadAll(path);
        entries[ContainerManifest.FileName] = transform(entries[ContainerManifest.FileName]);
        entries[ContainerManifest.SignatureFileName] = world.Pki.Pc.Sign(ContainerWriter.SigningInput(
            Sha256.Hash(entries[ContainerManifest.FileName]),
            Sha256.Hash(entries[ContainerManifest.PayloadFileName])));
        ZipSurgery.WriteAll(path, entries);
    }

    private sealed class ContainerWorld : IDisposable
    {
        private readonly DateTimeOffset _createdAt;

        public ContainerWorld(DateTimeOffset? createdAt = null)
        {
            _createdAt = createdAt ?? Now;
            Folder = new TempFolder();
            Pki = TestPki.Create();
        }

        public TempFolder Folder { get; }

        public TestPki Pki { get; }

        public string Write(ContainerKind kind, ContainerKeySource key, params ContainerEntrySource[] entries)
        {
            var path = Folder.File("packet" + ContainerKinds.Extension(kind));
            ContainerWriter.Write(path, new ContainerWriteRequest
            {
                Kind = kind,
                Producer = Pki.PcCertificate,
                Signer = Pki.Pc,
                Key = key,
                Entries = entries,
                OrgAgreementPublicKey = Pki.Org.AgreementPublicKey,
                Time = new FixedClock(_createdAt),
            });
            return path;
        }

        public ContainerOpenOptions Options(ContainerKind kind) => new()
        {
            ExpectedKind = kind,
            OrgSigningPublicKey = Pki.Org.SigningPublicKey,
            Time = new FixedClock(Now),
        };

        public void Dispose()
        {
            Pki.Dispose();
            Folder.Dispose();
        }
    }
}
