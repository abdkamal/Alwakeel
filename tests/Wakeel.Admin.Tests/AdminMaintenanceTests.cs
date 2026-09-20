using Wakeel.Admin.UI.Services.Audit;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Distribution;
using Wakeel.Admin.UI.Services.Export;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Maintenance;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Admin.UI.Text;
using Wakeel.Crypto;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A09 «الصيانة», A10 «توزيع التحديثات» and A11 «سجل العمليات»: opening the product's own files with
/// the organisation key, telling the offices what changed, and reading the log back without ever
/// carrying a secret out of it.
/// </summary>
public class AdminMaintenanceTests : AdminTestContext
{
    // ── A09 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Maintenance_OpensABackupWrittenByTheProductAndSaysWhatIsInIt()
    {
        var world = World();
        var backup = WriteBackup(world);

        var refusal = Maintenance.OpenFile(backup, out var report);

        Assert.Equal(MaintenanceRefusal.None, refusal);
        Assert.NotNull(report);
        Assert.True(report.Ok);
        Assert.Equal(AdminAr.Maintenance.KindName(ContainerKind.Backup), report.Title);

        // The signature held, the organisation is this one, and one item was really opened with the
        // administrator's own copy of the key rather than merely listed.
        Assert.Contains(report.Findings, finding => finding.Label == AdminAr.Maintenance.CheckSignature);
        Assert.Contains(report.Findings, finding => finding.Value == "هيئة تنمية المناطق الريفية");
        Assert.Contains(
            report.Findings,
            finding => finding.Label == AdminAr.Maintenance.CheckContent && finding.Ok);
        Assert.Equal(2, report.Items.Count);
    }

    [Fact]
    public void Maintenance_OpensACorrespondenceTheSameWay()
    {
        var world = World();
        var message = WriteMessage(world);

        Assert.Equal(MaintenanceRefusal.None, Maintenance.OpenFile(message, out var report));
        Assert.NotNull(report);
        Assert.True(report.Ok);
        Assert.Equal(AdminAr.Maintenance.KindName(ContainerKind.Msg), report.Title);
    }

    [Fact]
    public void Maintenance_RefusesAFileThatIsNotOursAndOneThatIsNotThere()
    {
        World();

        var stray = Path.Combine(Paths.Root, "notes.txt");
        File.WriteAllText(stray, "لا شيء");

        Assert.Equal(MaintenanceRefusal.NotOurs, Maintenance.OpenFile(stray, out _));
        Assert.Equal(
            MaintenanceRefusal.NotFound,
            Maintenance.OpenFile(Path.Combine(Paths.Root, "nothing.wakeel-backup"), out _));
    }

    [Fact]
    public void Maintenance_SaysSoWhenAFileOfTheRightNameIsDamaged()
    {
        var world = World();
        var backup = WriteBackup(world);

        var bytes = File.ReadAllBytes(backup);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(backup, bytes);

        Assert.Equal(MaintenanceRefusal.Unreadable, Maintenance.OpenFile(backup, out var report));
        Assert.Null(report);
    }

    [Fact]
    public void Maintenance_LooksOverAFolderAndCountsWhatItFound()
    {
        var world = World();
        var folder = Path.Combine(Paths.Root, "copy");
        Directory.CreateDirectory(folder);
        File.Copy(WriteBackup(world), Path.Combine(folder, "office.wakeel-backup"));
        File.WriteAllBytes(Path.Combine(folder, "broken.wakeel-msg"), [1, 2, 3, 4]);

        Assert.Equal(MaintenanceRefusal.None, Maintenance.OpenFolder(folder, out var report));
        Assert.NotNull(report);
        Assert.False(report.Ok);
        Assert.Equal(2, report.Items.Count);
        Assert.Contains(
            report.Findings,
            finding => finding.Label == AdminAr.Maintenance.CheckDamaged && !finding.Ok);
    }

    [Fact]
    public void Maintenance_RecoveringAnAccountIssuesANewFileOnNewKeys()
    {
        var world = World();
        Assert.Equal(ExportRefusal.None, Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out _));
        var before = DeviceKeys.ReadCertificate(world.DeviceId);

        var refusal = Maintenance.RecoverAccount(world.DeviceId, Password, out var file);

        Assert.Equal(MaintenanceRefusal.None, refusal);
        Assert.NotNull(file);
        Assert.True(File.Exists(file.FilePath));

        var after = DeviceKeys.ReadCertificate(world.DeviceId);
        Assert.NotEqual(before!.Body.Ed25519Pub, after!.Body.Ed25519Pub);
        Assert.Contains(Audit.Recent(), entry => entry.Action == "account_recovered");
    }

    [Fact]
    public void Maintenance_WritesEveryOpeningToTheLogWithoutASecretInIt()
    {
        var world = World();
        Maintenance.OpenFile(WriteBackup(world), out _);

        var entry = Assert.Single(Audit.Recent(), row => row.Action == "maintenance_file_opened");
        Assert.Contains("wakeel-backup", entry.SummaryAr, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, entry.SummaryAr, StringComparison.Ordinal);
    }

    // ── A10 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Distribution_ReachesOnlyTheOfficeAChangeBelongsTo()
    {
        var world = World();
        var second = SecondOffice();
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(second, out _));
        Register(second, 1, "خالد سعيد", 1, DeviceRoles.Manager);

        // Everything so far has been sent out; the only thing waiting is a device of the second office.
        Distribution.Distribute();
        Assert.Empty(Distribution.Waiting());

        Register(second, 2, "ليلى حداد", 2, DeviceRoles.Secretary);

        var targets = Distribution.Targets();
        var target = Assert.Single(targets);
        Assert.Equal(second, target.OfficeId);

        var run = Distribution.Distribute();
        Assert.Equal(2, run.Files.Count);
        Assert.All(run.Files, file => Assert.Equal("OF-002", file.OfficeCode));
        Assert.Empty(Distribution.Waiting());
        Assert.DoesNotContain(run.Files, file => file.OfficeCode == "OF-001");
        Assert.NotNull(world);
    }

    [Fact]
    public void Distribution_ReachesEveryOfficeWhenTheStructureItselfChanged()
    {
        World();
        var second = SecondOffice();
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(second, out _));
        Register(second, 1, "خالد سعيد", 1, DeviceRoles.Manager);
        Distribution.Distribute();

        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة جديدة", null, null, out _);

        var targets = Distribution.Targets();
        Assert.Equal(2, targets.Count);

        var run = Distribution.Distribute();
        Assert.Equal(2, run.Files.Count);
        Assert.Empty(Distribution.Waiting());
    }

    [Fact]
    public void Distribution_GivesEveryFileItsOwnPasswordAndOpensWithNoneOfTheOthers()
    {
        World();
        Register(Office(), 3, "خالد سعيد", 3, DeviceRoles.Custodian);

        var run = Distribution.Distribute();

        Assert.Equal(2, run.Files.Count);
        Assert.Equal(2, run.Files.Select(file => file.PackagePassword).Distinct(StringComparer.Ordinal).Count());

        foreach (var file in run.Files)
        {
            using var package = SetupPackageReader.Open(
                file.FilePath,
                file.PackagePassword,
                SetupExpectations.FirstRun(Path.Combine(Paths.StagingFolder, "read")),
                Time);

            Assert.True(package.IsAcceptable);
            Assert.Equal(file.DeviceNo, package.Content.Device.DeviceNo);
        }
    }

    [Fact]
    public void Distribution_SaysWhyADeviceCouldNotBeGivenAFile()
    {
        var world = World();
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(world.DeviceId, out _));

        var targets = Distribution.Targets();
        var target = Assert.Single(targets);
        Assert.Empty(target.Devices);
        Assert.Equal(AdminAr.Distribution.BlockedRevoked, Assert.Single(target.Blocked).Reason);

        var run = Distribution.Distribute();
        Assert.Empty(run.Files);
        Assert.Single(run.Failures);
        Assert.NotEmpty(Distribution.Waiting());
    }

    // ── A11 ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AuditLog_FindsARowByWhatIsWrittenInItWhateverTheShapeOfTheLetters()
    {
        var world = World();
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(world.DeviceId, out _));

        Assert.NotEmpty(AuditLog.Read("إلغاء"));
        Assert.NotEmpty(AuditLog.Read("الغاء"));
        Assert.NotEmpty(AuditLog.Read("سامي"));
        Assert.Empty(AuditLog.Read("لا شيء من هذا القبيل"));
    }

    [Fact]
    public void AuditLog_WritesTheFourColumnsOnTheScreenAndNoSecret()
    {
        var world = World();
        Assert.Equal(ExportRefusal.None, Exports.Export(world.DeviceId, SetupExportOptions.All, Password, out var result));

        var csv = AdminAuditQuery.ToCsv(AuditLog.Read());
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("﻿", csv, StringComparison.Ordinal);
        Assert.Contains(AdminAr.Audit.ColumnWho, lines[0], StringComparison.Ordinal);
        Assert.Contains(AdminAr.Audit.ColumnWhen, lines[0], StringComparison.Ordinal);
        Assert.Contains(AdminAr.Audit.ColumnWhat, lines[0], StringComparison.Ordinal);
        Assert.Contains(result!.FileName, csv, StringComparison.Ordinal);

        // Not one of the things a log must never carry, and not the technical token either.
        Assert.DoesNotContain(Password, csv, StringComparison.Ordinal);
        Assert.DoesNotContain("setup_exported", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("sealed", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditLog_QuotesEveryFieldAndDisarmsALineThatCouldBeReadAsAFormula()
    {
        CreateAccount();
        Audit.Write("سامي الحاج", "org_updated", "=SUM(A1:A9) \"اختبار\"");

        var csv = AdminAuditQuery.ToCsv(AuditLog.Read());

        Assert.Contains("\"'=SUM(A1:A9) \"\"اختبار\"\"\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditLog_CountsEveryRowEvenWhileTheSearchNarrowsWhatIsShown()
    {
        var world = World();
        Assert.Equal(KeyRefusal.None, DeviceKeys.Revoke(world.DeviceId, out _));

        var all = AuditLog.Read();
        var narrowed = AuditLog.Read("إلغاء");

        Assert.Equal(all.Count, AuditLog.Count);
        Assert.True(narrowed.Count < all.Count);
    }

    /// <summary>A package password of the sixteen characters this product issues.</summary>
    private const string Password = "K7M2-P9QR-3TVW-X4YZ";

    private sealed record Built(string OfficeId, string OfficeUnitId, string DeviceId);

    private string? _officeId;

    private Built World()
    {
        CreateAccount();
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", null, null, out var section);
        Structure.Add(section!, "وحدة الرواتب", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit!, "OF-001"));

        var office = DeviceRegistry.ListOffices().Single(candidate => candidate.OfficeCode == "OF-001");
        _officeId = office.Id;
        Assert.Equal(KeyRefusal.None, DeviceKeys.IssueOrRotateOfficeKey(office.Id, out _));

        var device = Register(office.Id, 2, "منى العلي", 4, DeviceRoles.Secretary);
        return new Built(office.Id, office.UnitId, device);
    }

    private string Office() => _officeId!;

    private string SecondOffice()
    {
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        Structure.Add(root.Id, "دائرة الهندسة", null, null, out var department);
        Structure.Add(department!, "قسم التصميم", null, null, out var section);
        Structure.Add(section!, "وحدة الرسم", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit!, "OF-002"));
        return DeviceRegistry.ListOffices().Single(candidate => candidate.OfficeCode == "OF-002").Id;
    }

    private string Register(string officeId, int deviceNo, string employee, int employeeNo, string role)
    {
        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.AddDevice(officeId, deviceNo, role, employee, employeeNo, SyncScopes.Full, out var device));
        Assert.NotNull(device);
        return device;
    }

    /// <summary>
    /// A backup as a الوكيل installation would write one: signed by the device's own certificate,
    /// locked with the office key, and carrying the administrator's copy of the content key — which
    /// is the only reason the tool can open it at all.
    /// </summary>
    private string WriteBackup(Built world) => WriteContainer(world, ContainerKind.Backup);

    private string WriteMessage(Built world) => WriteContainer(world, ContainerKind.Msg);

    private string WriteContainer(Built world, ContainerKind kind)
    {
        var certificate = DeviceKeys.ReadCertificate(world.DeviceId);
        Assert.NotNull(certificate);

        Assert.Equal(KeyRefusal.None, DeviceKeys.ReadOfficeKeyForExport(world.OfficeId, out var officeKey));
        using var orgIdentity = Keys.OpenOrgIdentity();

        // The device's own keys have never left the tool at this point, so the file is signed with a
        // fresh identity certified for the same device — which is exactly what an installation holds.
        using var deviceIdentity = DeviceIdentity.Generate();
        var org = Keys.ReadOrganisation();
        Assert.NotNull(org);

        var signed = DeviceCertificate.Issue(
            certificate.Body with
            {
                Ed25519Pub = deviceIdentity.SigningPublicKeyText,
                X25519Pub = deviceIdentity.AgreementPublicKeyText,
            },
            orgIdentity);

        var path = Path.Combine(Paths.Root, $"office{ContainerKinds.Extension(kind)}");
        ContainerWriter.Write(path, new ContainerWriteRequest
        {
            Kind = kind,
            Producer = signed,
            Signer = deviceIdentity,
            Key = ContainerKeySource.OfficeKey(officeKey),
            OrgAgreementPublicKey = Base64Url.Decode(org.AgreementPublicKeyText),
            Entries =
            [
                ContainerEntrySource.FromBytes("data.json", System.Text.Encoding.UTF8.GetBytes("{\"ok\":true}")),
                ContainerEntrySource.FromBytes("notes.txt", System.Text.Encoding.UTF8.GetBytes("ملاحظة")),
            ],
            StagingDirectory = Paths.StagingFolder,
            Time = Time,
        });

        return path;
    }
}
