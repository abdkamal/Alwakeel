using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Admin.UI.Text;
using Wakeel.Crypto;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A06 «المكاتب والأجهزة» and A07 «الحسابات والمفاتيح»: the device numbering rules, the
/// certificates the organisation issues, the office key, and the signed list of the devices that
/// are no longer allowed in (AGREEMENT item 24).
/// </summary>
public class AdminDeviceKeyTests : AdminTestContext
{
    [Fact]
    public void Numbering_OffersTheNextFreeNumberAndRefusesOneAlreadyTaken()
    {
        var office = Office();

        Assert.Equal(1, DeviceRegistry.NextDeviceNo(office));
        Add(office, 1, "سامي الحاج", 1);
        Assert.Equal(2, DeviceRegistry.NextDeviceNo(office));

        var refusal = DeviceRegistry.AddDevice(
            office, 1, DeviceRoles.Secretary, "منى العلي", 2, SyncScopes.Full, out var nothing);

        Assert.Equal(DeviceRefusal.DeviceNoTaken, refusal);
        Assert.Null(nothing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-1)]
    public void Numbering_RefusesADeviceNumberOutsideOneToNine(int number)
    {
        var office = Office();

        Assert.Equal(
            DeviceRefusal.DeviceNoOutOfRange,
            DeviceRegistry.AddDevice(office, number, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public void Numbering_RefusesAnEmployeeNumberOutsideOneToNine(int number)
    {
        var office = Office();

        Assert.Equal(
            DeviceRefusal.EmployeeNoOutOfRange,
            DeviceRegistry.AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", number, SyncScopes.Full, out _));
    }

    [Fact]
    public void Numbering_FillsAGapLeftByADeviceThatWasTakenOut()
    {
        var office = Office();
        Add(office, 1, "سامي الحاج", 1);
        var second = Add(office, 2, "منى العلي", 2);
        Add(office, 3, "خالد سعيد", 3);

        Assert.Equal(4, DeviceRegistry.NextDeviceNo(office));
        Assert.Equal(DeviceRefusal.None, DeviceRegistry.RemoveDevice(second));
        Assert.Equal(2, DeviceRegistry.NextDeviceNo(office));
    }

    [Fact]
    public void Numbering_SaysThereIsNoRoomOnceAllNineNumbersAreUsed()
    {
        var office = Office();
        for (var i = 1; i <= 9; i++)
        {
            Add(office, i, $"موظف {i}", ((i - 1) % 9) + 1);
        }

        Assert.Null(DeviceRegistry.NextDeviceNo(office));
        Assert.Equal(9, DeviceRegistry.ListDevices(office).Count);
    }

    [Fact]
    public void Numbering_IsCountedInsideOneOfficeOnly()
    {
        var first = Office();
        var second = SecondOffice();

        Add(first, 1, "سامي الحاج", 1);
        Assert.Equal(DeviceRefusal.None, DeviceRegistry.AddDevice(
            second, 1, DeviceRoles.Manager, "منى العلي", 1, SyncScopes.Full, out var other));
        Assert.NotNull(other);
    }

    [Fact]
    public void ADevice_RefusesARoleOrAScopeThatIsNotOneOfTheOnesTheToolKnows()
    {
        var office = Office();

        Assert.Equal(
            DeviceRefusal.RoleInvalid,
            DeviceRegistry.AddDevice(office, 1, "director", "سامي الحاج", 1, SyncScopes.Full, out _));
        Assert.Equal(
            DeviceRefusal.ScopeInvalid,
            DeviceRegistry.AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, "half", out _));
        Assert.Equal(
            DeviceRefusal.EmployeeNameRequired,
            DeviceRegistry.AddDevice(office, 1, DeviceRoles.Manager, "  ", 1, SyncScopes.Full, out _));
    }

    [Fact]
    public void ADevice_KeepsItsNumberWhileWhoSitsAtItAndWhatItCarriesMayChange()
    {
        var office = Office();
        var device = Add(office, 3, "سامي الحاج", 1);

        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.UpdateDevice(device, DeviceRoles.Custodian, "منى العلي", 4, SyncScopes.Custody));

        var row = DeviceRegistry.FindDevice(device);
        Assert.Equal(3, row!.DeviceNo);
        Assert.Equal(DeviceRoles.Custodian, row.Role);
        Assert.Equal("منى العلي", row.EmployeeName);
        Assert.Equal(4, row.EmployeeNo);
        Assert.Equal(SyncScopes.Custody, row.SyncScope);
    }

    [Fact]
    public void Certificate_IsIssuedTheMomentADeviceIsRegisteredAndCarriesTheOrganisationSignature()
    {
        var office = Office();
        var device = Add(office, 2, "سامي الحاج", 5);

        var certificate = DeviceKeys.ReadCertificate(device);
        var org = Keys.ReadOrganisation();

        Assert.NotNull(certificate);
        Assert.NotNull(org);
        Assert.Equal(org.Id, certificate.Body.OrgId);

        // The certificate names the office by the structure node it sits in, because that is the
        // identifier a setup file carries as its office and the one an installation stores. A
        // certificate that named the offices row instead would fail the reader's own office check.
        Assert.Equal(DeviceRegistry.ReadOffice(office)!.UnitId, certificate.Body.OfficeId);
        Assert.Equal(device, certificate.Body.DeviceId);
        Assert.Equal(2, certificate.Body.DeviceNo);
        Assert.Equal(5, certificate.Body.EmployeeNo);
        Assert.Equal(DeviceRoles.Manager, certificate.Body.Role);
        Assert.Equal(DeviceKind.Pc, certificate.Body.Kind);
        Assert.True(certificate.VerifySignature(OrgKey()));
    }

    [Fact]
    public void Certificate_PassesTheWholeChainCheckAgainstTheOrganisationRoot()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        var certificate = DeviceKeys.ReadCertificate(device);
        var root = Keys.ReadRootCertificate();
        var revocations = DeviceKeys.BuildRevocationList();

        Assert.NotNull(certificate);
        Assert.NotNull(root);
        Assert.NotNull(revocations);
        Assert.True(CertificateChain.TryVerify(
            certificate, OrgKey(), revocations, Time.GetUtcNow(), out _, root));
    }

    [Fact]
    public void Certificate_IsReIssuedWithFreshKeysWhenAnAccountHasToBeRecovered()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);
        var before = DeviceKeys.ReadCertificate(device);

        Time.Advance(TimeSpan.FromDays(3));
        Assert.Equal(KeyRefusal.None, DeviceKeys.ReIssueDevice(device));
        var after = DeviceKeys.ReadCertificate(device);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.NotEqual(before.Body.Ed25519Pub, after.Body.Ed25519Pub);
        Assert.NotEqual(before.Body.X25519Pub, after.Body.X25519Pub);
        Assert.Equal(before.Body.DeviceId, after.Body.DeviceId);
        Assert.True(after.VerifySignature(OrgKey()));
        Assert.True(DeviceKeys.SeedsStillHeld(device));
    }

    [Fact]
    public void Certificate_IsRewrittenWhenTheRoleOrTheEmployeeNumberChanges()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.UpdateDevice(device, DeviceRoles.Custodian, "سامي الحاج", 7, SyncScopes.Custody));

        var certificate = DeviceKeys.ReadCertificate(device);
        Assert.Equal(DeviceRoles.Custodian, certificate!.Body.Role);
        Assert.Equal(7, certificate.Body.EmployeeNo);
        Assert.True(certificate.VerifySignature(OrgKey()));
    }

    [Fact]
    public void Seeds_LeaveTheToolExactlyOnceAndAreGoneAfterwards()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        Assert.True(DeviceKeys.SeedsStillHeld(device));
        Assert.Equal(KeyRefusal.None, DeviceKeys.TakeSeedsForExport(device, out var seeds));
        Assert.NotNull(seeds);
        Assert.False(DeviceKeys.SeedsStillHeld(device));

        Assert.Equal(KeyRefusal.SeedsAlreadyExported, DeviceKeys.TakeSeedsForExport(device, out var again));
        Assert.Null(again);
    }

    [Fact]
    public void TheOfficeKey_IsMadeOnceAndRotatingItCountsUpwards()
    {
        var office = Office();

        Assert.False(DeviceKeys.ReadOfficeKey(office)?.Exists ?? false);
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(office, out var first));
        Assert.Equal(1, first);

        Time.Advance(TimeSpan.FromDays(30));
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(office, out var second));
        Assert.Equal(2, second);

        var info = DeviceKeys.ReadOfficeKey(office);
        Assert.NotNull(info);
        Assert.True(info.Exists);
        Assert.Equal(2, info.Version);
        Assert.Equal(Time.GetUtcNow(), info.RotatedAt);
    }

    [Fact]
    public void TheOfficeKey_TravelsToADeviceSealedToThatDeviceAlone()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        Assert.Equal(KeyRefusal.NoOfficeKey, DeviceKeys.WrapOfficeKeyFor(device, out _));
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(office, out _));
        Assert.Equal(KeyRefusal.None, DeviceKeys.WrapOfficeKeyFor(device, out var wrap));

        Assert.NotEmpty(wrap);
        Assert.Equal(KeyRefusal.None, DeviceKeys.WrapOfficeKeyFor(device, out var again));
        // A wrap is freshly randomised every time, so two wraps of the same key never match.
        Assert.NotEqual(Convert.ToBase64String(wrap), Convert.ToBase64String(again));
    }

    [Fact]
    public void Revoking_ShutsTheDeviceOutAndWritesASignedListTheOrganisationStandsBehind()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(device, out var list));

        Assert.NotNull(list);
        Assert.True(list.VerifySignature(OrgKey()));
        Assert.Equal(Keys.ReadOrganisation()!.Id, list.Body.OrgId);
        Assert.Single(list.Body.Entries);
        Assert.Equal(device, list.Body.Entries[0].DeviceId);
        Assert.True(list.IsRevoked(device, Time.GetUtcNow()));

        var row = DeviceRegistry.FindDevice(device);
        Assert.NotNull(row);
        Assert.True(row.IsRevoked);
        Assert.False(DeviceKeys.SeedsStillHeld(device));
    }

    [Fact]
    public void Revoking_IsRememberedOnDiskAndTheSavedListVerifiesToo()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(device, out _));

        var saved = DeviceKeys.ReadSavedRevocationList();

        Assert.NotNull(saved);
        Assert.True(saved.VerifySignature(OrgKey()));
        Assert.Contains(saved.Body.Entries, e => e.DeviceId == device);
    }

    [Fact]
    public void Revoking_HappensOnceAndTheSecondAttemptIsRefused()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(device, out _));
        Assert.Equal(KeyRefusal.AlreadyRevoked, DeviceKeys.Revoke(device, out var nothing));
        Assert.Null(nothing);
        Assert.Equal(KeyRefusal.DeviceNotFound, DeviceKeys.Revoke("no-such-device", out _));
    }

    [Fact]
    public void Revoking_MakesTheChainCheckTurnTheCertificateAway()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);
        var certificate = DeviceKeys.ReadCertificate(device);
        Assert.NotNull(certificate);

        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(device, out var list));
        Time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(CertificateChain.TryVerify(
            certificate, OrgKey(), list, Time.GetUtcNow(), out _, Keys.ReadRootCertificate()));
    }

    [Fact]
    public void Revoking_GrowsTheOneListRatherThanStartingASecondOne()
    {
        var office = Office();
        var first = Add(office, 1, "سامي الحاج", 1);
        var second = Add(office, 2, "منى العلي", 2);

        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(first, out _));
        Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(second, out var list));

        Assert.NotNull(list);
        Assert.Equal(2, list.Body.Entries.Count);
        Assert.True(list.VerifySignature(OrgKey()));
        Assert.Equal(2, Dashboard.Read().RevokedDevices);
        Assert.Equal(0, Dashboard.Read().RegisteredDevices);
    }

    [Fact]
    public void TheBoard_SaysHowManyDevicesAreActuallyActivated_NotHowManyAreMerelyRegistered()
    {
        // Three devices: one activated, one revoked, one still waiting for its custodian. The board
        // used to call all the non-revoked ones «نشطة», which told the administrator that two desks
        // were working when only one was; it now uses the same words A06 and A07 put on a row.
        var office = Office();
        var activated = Add(office, 1, "سامي الحاج", 1);
        var waiting = Add(office, 2, "منى العلي", 2);
        var revoked = Add(office, 3, "ليلى قاسم", 3);

        Assert.Equal(DeviceRefusal.None, DeviceRegistry.ActivateAccount(activated));
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(revoked, out _));

        var board = Dashboard.Read();

        Assert.Equal(3, board.Devices);
        Assert.Equal(1, board.ActivatedDevices);
        Assert.Equal(1, board.DevicesWaitingActivation);
        Assert.Equal(1, board.RevokedDevices);
        Assert.Equal(2, board.RegisteredDevices);

        Assert.Equal(
            "جهاز مفعّل · جهاز بانتظار التفعيل · جهاز مُلغى",
            AdminAr.Dashboard.DevicesStatus(
                board.ActivatedDevices, board.DevicesWaitingActivation, board.RevokedDevices));

        // The device that is still waiting is exactly the one A06/A07 draw as «بانتظار التفعيل».
        Assert.True(DeviceRegistry.FindDevice(waiting)!.IsPending);
    }

    [Fact]
    public void AnAccount_IsActivatedForTheEmployeeWhoSitsAtTheDevice()
    {
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        Assert.True(DeviceRegistry.FindDevice(device)!.IsPending);
        Assert.Equal(DeviceRefusal.None, DeviceRegistry.ActivateAccount(device));
        Assert.True(DeviceRegistry.FindDevice(device)!.IsActive);
    }

    [Fact]
    public void ADeviceWhoseEmployeeRecordWasNeverWrittenIsShownAsHalfMadeRatherThanAsAManager()
    {
        // Registering a device writes the device and then its account. A run that stopped between
        // the two leaves a device with no employee, no role and no scope; what the screens must
        // never do is fill that in with a default — least of all the most privileged one.
        var office = Office();
        var device = Add(office, 1, "سامي الحاج", 1);

        using (var command = Db.Command("DELETE FROM accounts WHERE device_id = $device;"))
        {
            command.Parameters.AddWithValue("$device", device);
            command.ExecuteNonQuery();
        }

        var half = DeviceRegistry.ListDevices(office).Single();

        Assert.False(half.HasAccount);
        Assert.Equal(string.Empty, half.Role);
        Assert.Equal(string.Empty, half.EmployeeName);
        Assert.Equal(0, half.EmployeeNo);
        Assert.Equal(string.Empty, half.SyncScope);
        Assert.False(half.IsActive);
        Assert.False(half.IsPending);

        // And a whole one still reads as whole.
        var second = Add(office, 2, "منى العلي", 2);
        var whole = DeviceRegistry.ListDevices(office).Single(d => d.Id == second);

        Assert.True(whole.HasAccount);
        Assert.Equal(DeviceRoles.Manager, whole.Role);
        Assert.Equal("منى العلي", whole.EmployeeName);
    }

    private byte[] OrgKey()
    {
        var org = Keys.ReadOrganisation();
        Assert.NotNull(org);
        Assert.True(Base64Url.TryDecode(org.SigningPublicKeyText, out var key));
        return key;
    }

    private string Add(string officeId, int deviceNo, string employeeName, int employeeNo)
    {
        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.AddDevice(
                officeId, deviceNo, DeviceRoles.Manager, employeeName, employeeNo, SyncScopes.Full, out var id));
        Assert.NotNull(id);
        return id;
    }

    private string Office()
    {
        CreateAccount();
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", null, null, out var section);
        Structure.Add(section!, "وحدة الرواتب", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit!, "OF-001"));
        return DeviceRegistry.ListOffices().Single(o => o.OfficeCode == "OF-001").Id;
    }

    private string SecondOffice()
    {
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة الهندسة", null, null, out var department);
        Structure.Add(department!, "قسم التصميم", null, null, out var section);
        Structure.Add(section!, "وحدة الرسم", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit!, "OF-002"));
        return DeviceRegistry.ListOffices().Single(o => o.OfficeCode == "OF-002").Id;
    }
}
