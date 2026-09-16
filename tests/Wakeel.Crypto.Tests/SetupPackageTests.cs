using System.Text;

namespace Wakeel.Crypto.Tests;

public class SetupPackageTests
{
    [Fact]
    public void A_setup_file_round_trips_with_every_optional_entry()
    {
        using var world = new SetupWorld();
        var logo = RandomBytes.Next(96);
        var guide = Encoding.UTF8.GetBytes("دليل الوكيل");
        var template = RandomBytes.Next(256);
        var content = world.Content(new SetupIncludes(true, true, true));

        var path = world.Write(content, logo, guide, template);

        using var inspection = world.Inspect(path);
        var package = inspection.Require();

        Assert.True(inspection.IsAcceptable);
        Assert.All(package.Checks.Checks, check => Assert.NotEqual(SetupCheckStatus.Failed, check.Status));
        Assert.Equal(Enum.GetValues<SetupCheckItem>().Length, package.Checks.Checks.Count);

        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Signature));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Organisation));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Office));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Device));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Employee));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.OfficeKey));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Logo));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Guide));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.ReportTemplate));
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Revocation));

        Assert.Equal(logo, package.ReadLogo());
        Assert.Equal(guide, package.ReadGuide());
        Assert.Equal(template, package.ReadReportTemplate());

        Assert.Equal(SetupWorld.OrgId, package.Content.Org.Id);
        Assert.Equal(SetupWorld.DeviceId, package.Content.Device.Id);
        Assert.Equal(2, package.Content.Employee.EmployeeNo);
        Assert.True(package.Content.TryGetOfficeKey(out var officeKey));
        Assert.Equal(world.OfficeKey, officeKey);
        Assert.Equal(world.Seeds, package.Content.DeviceSeed);
        Assert.Equal(ContainerKind.Setup, package.Manifest.Type);
        Assert.Equal(PayloadMode.Password, package.Manifest.Mode);
    }

    [Fact]
    public void A_setup_file_round_trips_without_any_optional_entry()
    {
        using var world = new SetupWorld();
        var content = world.Content() with { Revocation = null };

        var path = world.Write(content);

        using var inspection = world.Inspect(path);
        var package = inspection.Require();

        Assert.True(inspection.IsAcceptable);
        Assert.Equal(SetupCheckStatus.Absent, Status(package, SetupCheckItem.Logo));
        Assert.Equal(SetupCheckStatus.Absent, Status(package, SetupCheckItem.Guide));
        Assert.Equal(SetupCheckStatus.Absent, Status(package, SetupCheckItem.ReportTemplate));
        Assert.Equal(SetupCheckStatus.Absent, Status(package, SetupCheckItem.Revocation));
        Assert.Null(package.ReadLogo());
        Assert.Null(package.ReadGuide());
        Assert.Null(package.ReadReportTemplate());
        Assert.Single(package.Manifest.Entries);
        Assert.Equal(SetupEntryNames.Content, package.Manifest.Entries[0].Name);
    }

    [Fact]
    public void The_description_carries_exactly_the_fields_the_format_names()
    {
        using var world = new SetupWorld();
        var json = Encoding.UTF8.GetString(world.Content(new SetupIncludes(true, true, true)).ToCanonicalBytes());

        foreach (var field in new[]
        {
            "\"formatVersion\":", "\"exportedAt\":", "\"exportSeq\":",
            "\"org\":", "\"id\":", "\"name\":", "\"signingPub\":", "\"x25519Pub\":",
            "\"cycleStartDay\":", "\"numberingFormat\":",
            "\"units\":", "\"parentId\":", "\"level\":", "\"headTitle\":", "\"headName\":", "\"officeCode\":",
            "\"office\":", "\"unitId\":",
            "\"device\":", "\"deviceNo\":", "\"role\":", "\"syncScope\":", "\"certificate\":",
            "\"deviceSeed\":", "\"signingSeed\":", "\"agreementSeed\":",
            "\"employee\":", "\"employeeNo\":", "\"jobTitle\":",
            "\"officeKey\":", "\"revocation\":",
            "\"includes\":", "\"logo\":", "\"guide\":", "\"reportTemplate\":",
        })
        {
            Assert.Contains(field, json, StringComparison.Ordinal);
        }

        // Canonical means canonical: members in ordinal order, no whitespace, and the same
        // bytes on any machine, because this is what the organisation signature covers.
        Assert.DoesNotContain(" ", json[..json.IndexOf("\"employee\"", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.StartsWith("{\"device\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_first_machine_learns_the_organisation_key_from_the_file_and_can_pin_it()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        using var first = world.Inspect(path);
        var package = first.Require();

        // Trust on first sight: the key that checked the signature is the key inside the file,
        // and it is the same key the installation is about to store and demand from then on.
        var pinned = package.Producer.SigningPublicKey;
        Assert.Equal(world.Org.SigningPublicKey, pinned);
        Assert.Equal(package.Content.Org.SigningPub, Base64Url.Encode(pinned));

        using var second = world.Inspect(
            path,
            new SetupExpectations
            {
                PinnedOrgSigningPub = pinned,
                InstalledDeviceId = SetupWorld.DeviceId,
                InstalledExportSeq = 1,
            });

        Assert.True(second.IsAcceptable);
    }

    [Fact]
    public void A_file_signed_by_another_organisation_than_the_pinned_one_is_refused()
    {
        using var world = new SetupWorld();
        using var stranger = DeviceIdentity.Generate();
        var path = world.Write(world.Content());

        using var inspection = world.Inspect(
            path,
            new SetupExpectations { PinnedOrgSigningPub = stranger.SigningPublicKey });

        // The file is faultless in itself, which is exactly why it has to be refused by name:
        // it belongs to an organisation this machine is not part of.
        Assert.Null(inspection.Package);
        Assert.Equal(SetupCheckStatus.Ok, Status(inspection.Checks, SetupCheckItem.Signature));
        Assert.Equal(SetupCheckStatus.Failed, Status(inspection.Checks, SetupCheckItem.Organisation));
        Assert.Equal(ErrorCode.Tampered, inspection.Checks.FirstFailure?.Error);
        Assert.Equal(ErrorCode.Tampered, world.OpenError(path, new SetupExpectations { PinnedOrgSigningPub = stranger.SigningPublicKey }));
    }

    [Fact]
    public void A_wrong_package_password_is_refused()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());
        var wrong = PackagePassword.New();

        using var inspection = world.Inspect(path, password: wrong);

        Assert.Null(inspection.Package);
        Assert.Equal(SetupCheckStatus.Ok, Status(inspection.Checks, SetupCheckItem.Signature));
        Assert.Equal(ErrorCode.WrongPassword, inspection.Checks.Find(SetupCheckItem.Package)?.Error);
        Assert.Equal(ErrorCode.WrongPassword, world.OpenError(path, SetupExpectations.FirstRun, wrong));
    }

    [Fact]
    public void The_password_may_be_typed_the_way_it_was_printed()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        // Grouped with dashes, in lower case, the way somebody copies it off the screen.
        var typed = PackagePassword.Display(world.Password).ToLowerInvariant();

        using var inspection = world.Inspect(path, password: typed);

        Assert.True(inspection.IsAcceptable);
    }

    [Fact]
    public void A_tampered_payload_is_refused_before_anything_is_decrypted()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        ZipSurgery.Replace(path, ContainerManifest.PayloadFileName, ZipSurgery.FlipLastByte);

        using var inspection = world.Inspect(path);

        Assert.Null(inspection.Package);
        Assert.Equal(ErrorCode.BadSignature, inspection.Checks.Find(SetupCheckItem.Signature)?.Error);
    }

    [Fact]
    public void A_tampered_manifest_is_refused()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        // The manifest stays valid JSON and keeps naming the same entry, but understates its
        // size: the organisation signature covers the manifest, so the edit cannot survive.
        ZipSurgery.Replace(path, ContainerManifest.FileName, bytes =>
        {
            var text = Encoding.UTF8.GetString(bytes);
            var marker = "\"size\":";
            var start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
            var end = text.IndexOfAny([',', '}'], start);
            return Encoding.UTF8.GetBytes(string.Concat(text.AsSpan(0, start), "1", text.AsSpan(end)));
        });

        using var inspection = world.Inspect(path);

        Assert.Null(inspection.Package);
        Assert.Equal(ErrorCode.BadSignature, inspection.Checks.Find(SetupCheckItem.Signature)?.Error);
    }

    [Fact]
    public void A_file_that_demands_an_absurd_amount_of_work_to_open_is_refused()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        // The cost parameters are read out of a file nobody has trusted yet. Signed or not,
        // a file asking for two terabytes of memory must be refused rather than attempted.
        var entries = ZipSurgery.ReadAll(path);
        var manifest = Encoding.UTF8.GetString(entries[ContainerManifest.FileName])
            .Replace(
                $"\"memoryKb\":{Argon2Params.MinMemoryKb}",
                $"\"memoryKb\":{int.MaxValue}",
                StringComparison.Ordinal);
        entries[ContainerManifest.FileName] = Encoding.UTF8.GetBytes(manifest);
        entries[ContainerManifest.SignatureFileName] = world.Org.Sign(ContainerWriter.SigningInput(
            Sha256.Hash(entries[ContainerManifest.FileName]),
            Sha256.Hash(entries[ContainerManifest.PayloadFileName])));
        ZipSurgery.WriteAll(path, entries);

        using var inspection = world.Inspect(path);

        Assert.Null(inspection.Package);
        Assert.Equal(SetupCheckStatus.Ok, Status(inspection.Checks, SetupCheckItem.Signature));
        Assert.Equal(ErrorCode.Corrupt, inspection.Checks.Find(SetupCheckItem.Package)?.Error);
    }

    [Fact]
    public void A_pinned_key_of_the_wrong_length_is_a_mistake_and_never_a_silent_first_run()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        var error = Assert.Throws<CryptoException>(
            () => world.Inspect(path, new SetupExpectations { PinnedOrgSigningPub = RandomBytes.Next(16) }));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void A_file_prepared_for_another_device_is_refused()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        using var inspection = world.Inspect(
            path,
            new SetupExpectations { InstalledDeviceId = "PC-2", InstalledExportSeq = 1 });

        Assert.Equal(SetupCheckStatus.Failed, Status(inspection.Checks, SetupCheckItem.Device));
        Assert.Equal(ErrorCode.OtherDevice, inspection.Checks.Find(SetupCheckItem.Device)?.Error);
        Assert.False(inspection.IsAcceptable);
        Assert.Equal(
            ErrorCode.OtherDevice,
            world.OpenError(path, new SetupExpectations { InstalledDeviceId = "PC-2" }));
    }

    [Fact]
    public void A_file_older_than_the_one_already_applied_is_refused()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content() with { ExportSeq = 4 });

        using var current = world.Inspect(
            path,
            new SetupExpectations { InstalledDeviceId = SetupWorld.DeviceId, InstalledExportSeq = 4 });
        Assert.True(current.IsAcceptable);

        using var older = world.Inspect(
            path,
            new SetupExpectations { InstalledDeviceId = SetupWorld.DeviceId, InstalledExportSeq = 5 });

        Assert.Equal(ErrorCode.Older, older.Checks.Find(SetupCheckItem.Package)?.Error);
        Assert.False(older.IsAcceptable);
        Assert.Equal(ErrorCode.Older, world.OpenError(path, new SetupExpectations { InstalledExportSeq = 5 }));
    }

    [Fact]
    public void A_file_dated_in_the_future_is_refused()
    {
        using var world = new SetupWorld();
        var ahead = SetupWorld.Now.AddDays(3);
        var path = world.Write(world.Content() with { ExportedAt = ahead }, at: ahead);

        using var inspection = world.Inspect(path);

        Assert.Null(inspection.Package);
        Assert.Equal(ErrorCode.FutureDate, inspection.Checks.Find(SetupCheckItem.Package)?.Error);
        Assert.Equal(ErrorCode.FutureDate, world.OpenError(path, SetupExpectations.FirstRun));
    }

    [Fact]
    public void A_description_dated_in_the_future_inside_a_file_that_is_not_is_refused()
    {
        using var world = new SetupWorld();

        // The container itself is dated now; only the description inside claims a later day,
        // so the refusal has to come from the date check on the description and nowhere else.
        var path = world.WriteRaw(world.Content() with { ExportedAt = SetupWorld.Now.AddDays(3) });

        using var inspection = world.Inspect(path);

        Assert.Equal(SetupCheckStatus.Ok, Status(inspection.Checks, SetupCheckItem.Signature));
        Assert.Equal(ErrorCode.FutureDate, inspection.Checks.Find(SetupCheckItem.Package)?.Error);
        Assert.False(inspection.IsAcceptable);
    }

    [Fact]
    public void The_writer_refuses_to_sign_a_description_it_would_itself_reject()
    {
        using var world = new SetupWorld();
        using var stranger = DeviceIdentity.Generate();

        // Every one of these would be caught by the reader on the other machine; catching them
        // here means an administrator never hands over a file that cannot be used.
        Assert.Equal(ErrorCode.FutureDate, world.WriteError(world.Content() with { ExportedAt = SetupWorld.Now.AddDays(3) }));
        Assert.Equal(ErrorCode.Tampered, world.WriteError(world.Content() with { DeviceSeed = stranger.Export() }));
        Assert.Equal(ErrorCode.Corrupt, world.WriteError(world.Content() with { OfficeKey = Base64Url.Encode(RandomBytes.Next(16)) }));
        Assert.Equal(
            ErrorCode.Corrupt,
            world.WriteError(world.Content() with { Employee = new SetupEmployee("أحمد", 12, "سكرتير") }));
        Assert.Equal(
            ErrorCode.Corrupt,
            world.WriteError(world.Content() with { Office = new SetupOffice("U-NOWHERE", "OF-01") }));
    }

    [Fact]
    public void Seeds_that_are_not_the_private_half_of_the_certified_keys_are_refused()
    {
        using var world = new SetupWorld();
        using var stranger = DeviceIdentity.Generate();

        var path = world.WriteRaw(world.Content() with { DeviceSeed = stranger.Export() });

        using var inspection = world.Inspect(path);

        Assert.Equal(ErrorCode.Tampered, inspection.Checks.Find(SetupCheckItem.Device)?.Error);
        Assert.False(inspection.IsAcceptable);
    }

    [Fact]
    public void An_organisation_section_that_disagrees_with_the_signing_certificate_is_refused()
    {
        using var world = new SetupWorld();
        using var stranger = DeviceIdentity.Generate();

        var content = world.Content();
        var path = world.WriteRaw(content with
        {
            Org = content.Org with { SigningPub = stranger.SigningPublicKeyText },
        });

        using var inspection = world.Inspect(path);

        // The file is signed by the real organisation, but the key the installation would go on
        // to pin is somebody else's: after activation nothing the organisation signed would
        // verify, and everything the holder of that other key signed would.
        Assert.Equal(SetupCheckStatus.Ok, Status(inspection.Checks, SetupCheckItem.Signature));
        Assert.Equal(ErrorCode.Tampered, inspection.Checks.Find(SetupCheckItem.Organisation)?.Error);
        Assert.False(inspection.IsAcceptable);
    }

    [Fact]
    public void An_entry_the_description_never_declared_is_refused()
    {
        using var world = new SetupWorld();

        var path = world.WriteRaw(
            world.Content(),
            ContainerEntrySource.FromBytes(SetupEntryNames.Logo, RandomBytes.Next(32)));

        using var inspection = world.Inspect(path);

        Assert.Equal(ErrorCode.Tampered, inspection.Checks.Find(SetupCheckItem.Logo)?.Error);
        Assert.False(inspection.IsAcceptable);
    }

    [Fact]
    public void An_entry_a_setup_file_has_no_business_carrying_is_refused()
    {
        using var world = new SetupWorld();

        var path = world.WriteRaw(
            world.Content(),
            ContainerEntrySource.FromBytes("extra/payload.bin", RandomBytes.Next(32)));

        using var inspection = world.Inspect(path);

        // A host that extracts the whole file would otherwise drop this into the installation
        // folder, and on a first run there is no pinned key yet to say who put it there.
        Assert.Equal(ErrorCode.Tampered, inspection.Checks.Find(SetupCheckItem.Package)?.Error);
        Assert.False(inspection.IsAcceptable);
    }

    [Fact]
    public void A_revocation_list_travels_with_the_file_and_is_checked_against_the_organisation()
    {
        using var world = new SetupWorld();
        var path = world.Write(world.Content());

        using var inspection = world.Inspect(path);
        var package = inspection.Require();

        var revocations = Assert.IsType<RevocationList>(package.Content.Revocation);
        Assert.Equal(SetupCheckStatus.Ok, Status(package, SetupCheckItem.Revocation));
        Assert.True(revocations.VerifySignature(package.Producer.SigningPublicKey));
        Assert.Equal(SetupWorld.OrgId, revocations.Body.OrgId);
        Assert.True(revocations.IsRevoked(SetupWorld.RevokedDeviceId, SetupWorld.Now));
        Assert.False(revocations.IsRevoked(SetupWorld.DeviceId, SetupWorld.Now));
    }

    [Fact]
    public void A_revocation_list_signed_by_somebody_else_is_refused_and_never_consulted()
    {
        using var world = new SetupWorld();
        using var stranger = DeviceIdentity.Generate();

        var forged = RevocationList.Issue(
            new RevocationListBody(SetupWorld.OrgId, SetupWorld.Now, [new RevocationEntry(SetupWorld.DeviceId, SetupWorld.Now)]),
            stranger);

        var path = world.WriteRaw(world.Content() with { Revocation = forged });

        using var inspection = world.Inspect(path);

        // A list nobody trustworthy signed is a broken list, not a reason to reject the device
        // it happens to name: the device check must not have consulted it at all.
        Assert.Equal(ErrorCode.BadSignature, inspection.Checks.Find(SetupCheckItem.Revocation)?.Error);
        Assert.Equal(SetupCheckStatus.Ok, Status(inspection.Checks, SetupCheckItem.Device));
        Assert.False(inspection.IsAcceptable);
    }

    [Fact]
    public void A_device_the_organisation_has_revoked_is_refused()
    {
        using var world = new SetupWorld();

        var revocations = RevocationList.Issue(
            new RevocationListBody(SetupWorld.OrgId, SetupWorld.Now, [new RevocationEntry(SetupWorld.DeviceId, SetupWorld.Now)]),
            world.Org);

        var path = world.WriteRaw(world.Content() with { Revocation = revocations });

        using var inspection = world.Inspect(path);

        Assert.Equal(SetupCheckStatus.Ok, Status(inspection.Checks, SetupCheckItem.Revocation));
        Assert.Equal(ErrorCode.Revoked, inspection.Checks.Find(SetupCheckItem.Device)?.Error);
        Assert.False(inspection.IsAcceptable);
    }

    [Fact]
    public void Only_the_organisation_itself_may_produce_a_setup_file()
    {
        using var world = new SetupWorld();
        using var computer = DeviceIdentity.Generate();

        var certificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                SetupWorld.OrgId,
                SetupWorld.OfficeUnitId,
                "PC-7",
                7,
                3,
                SetupRoles.Manager,
                DeviceKind.Pc,
                computer.SigningPublicKeyText,
                computer.AgreementPublicKeyText,
                SetupWorld.Now,
                SetupWorld.OrgId),
            world.Org);

        var path = world.Folder.File("by-a-computer.wakeel-setup");
        ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = ContainerKind.Setup,
            Producer = certificate,
            Signer = computer,
            Key = ContainerKeySource.FromPassword(world.Password, world.Kdf),
            Entries = [ContainerEntrySource.FromBytes(SetupEntryNames.Content, world.Content().ToCanonicalBytes())],
            Time = new FixedClock(SetupWorld.Now),
        });

        using var inspection = world.Inspect(path);

        // A computer of the office can sign a container perfectly well; what it cannot do is
        // hand itself an office key, a device certificate and a new structure.
        Assert.Null(inspection.Package);
        Assert.Equal(ErrorCode.BadSignature, inspection.Checks.Find(SetupCheckItem.Signature)?.Error);
    }

    [Fact]
    public void The_organisation_root_may_not_produce_any_other_kind_of_file()
    {
        using var world = new SetupWorld();
        var root = DeviceCertificate.IssueOrgRoot(SetupWorld.OrgId, world.Org, SetupWorld.Now);

        var error = Assert.Throws<CryptoException>(() => ContainerWriter.Write(
            world.Folder.File("packet.wakeel-sync"),
            new ContainerWriteRequest
            {
                Kind = ContainerKind.Sync,
                Producer = root,
                Signer = world.Org,
                Key = ContainerKeySource.OfficeKey(world.OfficeKey),
                Entries = [ContainerEntrySource.FromText("records.json", "{}")],
                Time = new FixedClock(SetupWorld.Now),
            }));
        Assert.Equal(ErrorCode.Corrupt, error.Code);

        // And the reading side refuses it too, even when the file arrives correctly signed:
        // the kind is rewritten inside the signed manifest and signed again by the root itself.
        var path = world.Write(world.Content());
        var entries = ZipSurgery.ReadAll(path);
        var manifest = Encoding.UTF8.GetString(entries[ContainerManifest.FileName])
            .Replace("\"type\":\"setup\"", "\"type\":\"sync\"", StringComparison.Ordinal);
        entries[ContainerManifest.FileName] = Encoding.UTF8.GetBytes(manifest);
        entries[ContainerManifest.SignatureFileName] = world.Org.Sign(ContainerWriter.SigningInput(
            Sha256.Hash(entries[ContainerManifest.FileName]),
            Sha256.Hash(entries[ContainerManifest.PayloadFileName])));
        ZipSurgery.WriteAll(path, entries);

        var readError = Assert.Throws<CryptoException>(() => ContainerReader.Open(path, new ContainerOpenOptions
        {
            ExpectedKind = ContainerKind.Sync,
            OrgSigningPublicKey = world.Org.SigningPublicKey,
            Time = new FixedClock(SetupWorld.Now),
        }));
        Assert.Equal(ErrorCode.BadSignature, readError.Code);
    }

    [Fact]
    public void An_organisation_certificate_for_a_different_key_is_refused_by_the_chain()
    {
        using var org = DeviceIdentity.Generate();
        using var stranger = DeviceIdentity.Generate();
        var root = DeviceCertificate.IssueOrgRoot(SetupWorld.OrgId, org, SetupWorld.Now);

        CertificateChain.Verify(root, org.SigningPublicKey, revocations: null, SetupWorld.Now);

        var error = Assert.Throws<CryptoException>(
            () => CertificateChain.Verify(root, stranger.SigningPublicKey, revocations: null, SetupWorld.Now));
        Assert.Equal(ErrorCode.Tampered, error.Code);

        // A root that claims an office, or that names a device other than the organisation,
        // is not a root at all.
        var withOffice = new DeviceCertificate(root.Body with { OfficeId = "OFFICE-1" }, root.Signature);
        Assert.Equal(
            ErrorCode.Corrupt,
            Assert.Throws<CryptoException>(
                () => CertificateChain.Verify(withOffice, org.SigningPublicKey, revocations: null, SetupWorld.Now)).Code);

        var withOtherDevice = DeviceCertificate.Issue(root.Body with { DeviceId = "PC-1" }, org);
        Assert.Equal(
            ErrorCode.BadSignature,
            Assert.Throws<CryptoException>(
                () => CertificateChain.Verify(withOtherDevice, org.SigningPublicKey, revocations: null, SetupWorld.Now)).Code);
    }

    [Fact]
    public void Two_exports_of_the_same_description_produce_the_same_signed_bytes()
    {
        using var world = new SetupWorld();
        var content = world.Content();

        var first = world.Manifest(world.Write(content, name: "first"), out var firstPayload);
        var second = world.Manifest(world.Write(content, name: "second"), out var secondPayload);

        // Everything that is signed is byte for byte the same, including the organisation's own
        // certificate and signature, so two exports of one description can be compared.
        Assert.Equal(first.Producer, second.Producer);
        Assert.Equal(first.CreatedAt, second.CreatedAt);
        Assert.Equal(first.Type, second.Type);
        Assert.Equal(first.Version, second.Version);
        Assert.Equal(first.Mode, second.Mode);
        Assert.Equal(first.Entries, second.Entries);
        Assert.Null(first.KeySalt);
        Assert.Null(second.KeySalt);

        // The two things that must never repeat: the key derivation salt and, through it and
        // the fresh nonces, every byte of the encrypted payload.
        Assert.NotNull(first.Kdf);
        Assert.NotNull(second.Kdf);
        Assert.Equal(first.Kdf.MemoryKb, second.Kdf.MemoryKb);
        Assert.NotEqual(first.Kdf.Salt, second.Kdf.Salt);
        Assert.NotEqual(firstPayload, secondPayload);
    }

    [Fact]
    public void A_package_password_is_sixteen_unambiguous_characters()
    {
        var password = PackagePassword.New();

        Assert.Equal(PackagePassword.Length, password.Length);
        Assert.True(PackagePassword.IsWellFormed(password));
        Assert.NotEqual(password, PackagePassword.New());
        Assert.All(password, character => Assert.DoesNotContain(character, "ILOU"));

        // The printed form and every way a person may type it back fold to the same characters.
        Assert.Equal("XXXX-XXXX-XXXX-XXXX".Length, PackagePassword.Display(password).Length);
        Assert.Equal(password, PackagePassword.Normalize(PackagePassword.Display(password)));
        Assert.Equal(password, PackagePassword.Normalize(PackagePassword.Display(password).ToLowerInvariant()));
        Assert.Equal("011234", PackagePassword.Normalize("oI-l 2 34"));

        Assert.False(PackagePassword.IsWellFormed("SHORT"));
        Assert.False(PackagePassword.IsWellFormed(null));
        Assert.False(PackagePassword.IsWellFormed(new string('U', PackagePassword.Length)));
    }

    [Fact]
    public void The_writer_insists_on_a_password_this_product_issued()
    {
        using var world = new SetupWorld();

        var error = Assert.Throws<CryptoException>(
            () => world.Write(world.Content(), password: "كلمة مرور قصيرة"));
        Assert.Equal(ErrorCode.Corrupt, error.Code);
    }

    [Fact]
    public void The_whole_file_can_be_written_out_to_a_folder()
    {
        using var world = new SetupWorld();
        var logo = RandomBytes.Next(48);
        var path = world.Write(world.Content(new SetupIncludes(true, false, false)), logo);

        using var inspection = world.Inspect(path);
        var package = inspection.Require();
        var folder = world.Folder.File("extracted");

        package.ExtractTo(folder);

        Assert.Equal(logo, File.ReadAllBytes(Path.Combine(folder, SetupEntryNames.Logo)));
        Assert.Equal(
            package.Content.ToCanonicalBytes(),
            File.ReadAllBytes(Path.Combine(folder, SetupEntryNames.Content)));
    }

    private static SetupCheckStatus Status(SetupPackage package, SetupCheckItem item) =>
        Status(package.Checks, item);

    private static SetupCheckStatus Status(SetupCheckResult checks, SetupCheckItem item)
    {
        var check = checks.Find(item);
        Assert.NotNull(check);
        return check.Status;
    }

    /// <summary>An organisation, one office, one computer account and one employee.</summary>
    private sealed class SetupWorld : IDisposable
    {
        internal const string OrgId = "ORG-1";
        internal const string OfficeUnitId = "U-UNIT";
        internal const string OfficeCode = "OF-01";
        internal const string DeviceId = "PC-1";
        internal const string RevokedDeviceId = "PC-OLD";

        internal static readonly DateTimeOffset Now = new(2026, 9, 16, 9, 0, 0, TimeSpan.Zero);

        private static readonly IReadOnlyList<SetupUnit> Structure =
        [
            new SetupUnit("U-ORG", null, 1, "هيئة الاختبار", "رئيس الهيئة", "سالم العامري", null),
            new SetupUnit("U-DEPT", "U-ORG", 2, "دائرة الشؤون الإدارية", "مدير الدائرة", "خالد الهاشمي", null),
            new SetupUnit("U-SEC", "U-DEPT", 3, "قسم المتابعة", "رئيس القسم", "ليلى المنصوري", null),
            new SetupUnit(OfficeUnitId, "U-SEC", 4, "وحدة السكرتارية", "رئيس الوحدة", "نور السالمي", OfficeCode),
        ];

        private readonly DeviceIdentity _device;

        internal SetupWorld()
        {
            Folder = new TempFolder();
            Org = DeviceIdentity.Generate();
            _device = DeviceIdentity.Generate();
            Seeds = _device.Export();
            OfficeKey = RandomBytes.Next(SetupContent.OfficeKeySize);
            Password = PackagePassword.New();

            // The product's real cost would add a third of a second to every one of these
            // tests; what is being proved here is the format, not Argon2id's price.
            Kdf = new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(Argon2Params.SaltSize));

            Certificate = DeviceCertificate.Issue(
                new DeviceCertificateBody(
                    OrgId,
                    OfficeUnitId,
                    DeviceId,
                    1,
                    2,
                    SetupRoles.Secretary,
                    DeviceKind.Pc,
                    _device.SigningPublicKeyText,
                    _device.AgreementPublicKeyText,
                    Now,
                    OrgId),
                Org);
        }

        internal TempFolder Folder { get; }

        internal DeviceIdentity Org { get; }

        internal DeviceSeeds Seeds { get; }

        internal DeviceCertificate Certificate { get; }

        internal byte[] OfficeKey { get; }

        internal string Password { get; }

        internal Argon2Params Kdf { get; }

        internal SetupContent Content(SetupIncludes? includes = null) =>
            new(
                SetupContent.CurrentFormatVersion,
                Now,
                1,
                new SetupOrg(
                    OrgId,
                    "هيئة الاختبار",
                    Org.SigningPublicKeyText,
                    Org.AgreementPublicKeyText,
                    1,
                    "YYYYMMDD/DESSS"),
                Structure,
                new SetupOffice(OfficeUnitId, OfficeCode),
                new SetupDevice(DeviceId, 1, SetupRoles.Secretary, SetupSyncScopes.Full, Certificate),
                Seeds,
                new SetupEmployee("أحمد الخطيب", 2, "سكرتير"),
                Base64Url.Encode(OfficeKey),
                RevocationList.Issue(
                    new RevocationListBody(OrgId, Now, [new RevocationEntry(RevokedDeviceId, Now)]),
                    Org),
                includes ?? SetupIncludes.None);

        internal string Write(
            SetupContent content,
            byte[]? logo = null,
            byte[]? guide = null,
            byte[]? reportTemplate = null,
            DateTimeOffset? at = null,
            string? password = null,
            string name = "office-device")
        {
            var path = Folder.File(name + ContainerKinds.Extension(ContainerKind.Setup));
            SetupPackageWriter.Write(path, new SetupWriteRequest
            {
                Content = content,
                PackagePassword = password ?? Password,
                OrgIdentity = Org,
                Logo = Factory(logo),
                Guide = Factory(guide),
                ReportTemplate = Factory(reportTemplate),
                Kdf = Kdf,
                Time = new FixedClock(at ?? Now),
            });

            return path;
        }

        /// <summary>
        /// Builds a setup file straight through the container writer, bypassing the checks the
        /// real writer runs. It is how the suite produces the files only a hostile or broken
        /// administration tool could ever make.
        /// </summary>
        internal string WriteRaw(SetupContent content, params ContainerEntrySource[] extra)
        {
            var entries = new List<ContainerEntrySource>
            {
                ContainerEntrySource.FromBytes(SetupEntryNames.Content, content.ToCanonicalBytes()),
            };
            entries.AddRange(extra);

            var path = Folder.File("raw" + ContainerKinds.Extension(ContainerKind.Setup));
            ContainerWriter.Write(path, new ContainerWriteRequest
            {
                Kind = ContainerKind.Setup,
                Producer = DeviceCertificate.IssueOrgRoot(content.Org.Id, Org, Now),
                Signer = Org,
                Key = ContainerKeySource.FromPassword(Password, Kdf),
                Entries = entries,
                Time = new FixedClock(Now),
            });

            return path;
        }

        internal SetupInspection Inspect(
            string path,
            SetupExpectations? expectations = null,
            string? password = null,
            DateTimeOffset? now = null) =>
            SetupPackageReader.Inspect(
                path,
                password ?? Password,
                expectations ?? SetupExpectations.FirstRun,
                new FixedClock(now ?? Now));

        internal ErrorCode OpenError(string path, SetupExpectations expectations, string? password = null) =>
            Assert.Throws<CryptoException>(() => SetupPackageReader.Open(
                path,
                password ?? Password,
                expectations,
                new FixedClock(Now))).Code;

        internal ErrorCode WriteError(SetupContent content) =>
            Assert.Throws<CryptoException>(() => Write(content, name: "refused")).Code;

        internal ContainerManifest Manifest(string path, out byte[] payload)
        {
            var entries = ZipSurgery.ReadAll(path);
            payload = entries[ContainerManifest.PayloadFileName];
            return CanonicalJson.Deserialize<ContainerManifest>(entries[ContainerManifest.FileName]);
        }

        public void Dispose()
        {
            _device.Dispose();
            Org.Dispose();
            Folder.Dispose();
        }

        private static Func<Stream>? Factory(byte[]? content) =>
            content is null ? null : () => new MemoryStream(content, writable: false);
    }
}
