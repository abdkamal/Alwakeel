using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Export;
using Wakeel.Admin.UI.Services.Keys;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Crypto;

namespace Wakeel.Admin.Tests;

/// <summary>Builds a whole organisation into a folder, so the host can be driven against real data.</summary>
public class HostSeed
{
    [Fact]
    public void Build()
    {
        var root = Environment.GetEnvironmentVariable("WAKEEL_SEED");
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        var paths = AdminPaths.ForRoot(root);
        paths.EnsureDirectories();

        var time = TimeProvider.System;
        using var db = new AdminDb(paths);
        var session = new AdminSession();
        var audit = new AdminAuditService(db, time);
        var keys = new AdminKeyService(db, time);
        var options = new AdminOptions
        {
            Kdf = new Argon2Params(Argon2Params.MinMemoryKb, 1, 1, RandomBytes.Next(Argon2Params.SaltSize)),
            MachineName = "PLN-PC-01",
        };

        var accounts = new AdminAccountService(paths, db, keys, audit, session, options, time);
        var result = accounts.Create("Wakeel!2026#Admin", "Wakeel!2026#Admin", "سامي الحاج", "هيئة تنمية المناطق الريفية");
        Assert.True(result.Succeeded);

        var pending = new AdminPendingChanges(db, time);
        var org = new AdminOrgService(db, audit, session, pending, time);
        var structure = new AdminStructureService(db, audit, session, pending, time);
        var deviceKeys = new AdminDeviceKeyService(db, keys, audit, session, pending, paths, time);
        var devices = new AdminDeviceService(db, deviceKeys, audit, session, pending, time);
        var exports = new SetupExportService(db, keys, org, structure, devices, deviceKeys, audit, session, paths, time);

        var rootNode = structure.EnsureRoot();
        Assert.NotNull(rootNode);
        structure.Add(rootNode.Id, "دائرة الشؤون الإدارية", "مدير الدائرة", "أحمد الخطيب", out var department);
        structure.Add(department!, "قسم الصادر والوارد", "رئيس القسم", "منى العلي", out var section);
        structure.Add(section!, "وحدة الأرشيف", null, null, out var unit);
        Assert.Equal(StructureRefusal.None, structure.SetOffice(unit!, "INV-1042"));

        structure.Add(rootNode.Id, "دائرة الموارد البشرية", "مدير الدائرة", "خالد سعيد", out var second);
        structure.Add(second!, "قسم التوظيف", null, null, out var secondSection);
        structure.Add(secondSection!, "وحدة الملفات", null, null, out var secondUnit);
        Assert.Equal(StructureRefusal.None, structure.SetOffice(secondUnit!, "INV-1043"));

        var office = devices.ListOffices().Single(candidate => candidate.OfficeCode == "INV-1042");
        Assert.Equal(KeyRefusal.None, deviceKeys.IssueOrRotateOfficeKey(office.Id, out _));

        Add(devices, office.Id, 1, DeviceRoles.Manager, "أحمد الخطيب", 1);
        var secretary = Add(devices, office.Id, 2, DeviceRoles.Secretary, "منى العلي", 2);
        Add(devices, office.Id, 3, DeviceRoles.Custodian, "خالد سعيد", 3);

        var other = devices.ListOffices().Single(candidate => candidate.OfficeCode == "INV-1043");
        Assert.Equal(KeyRefusal.None, deviceKeys.IssueOrRotateOfficeKey(other.Id, out _));
        Add(devices, other.Id, 1, DeviceRoles.Manager, "سارة منصور", 1);

        // One device already has its file, so A08 can be seen on both a first file and a second one,
        // and A10 has something recorded as «آخر تصدير».
        Assert.Equal(
            ExportRefusal.None,
            exports.Export(secretary, SetupExportOptions.All, PackagePassword.New(), out _));

        File.WriteAllText(Path.Combine(root, "seeded.txt"), "ok");
    }

    private static string Add(
        AdminDeviceService devices,
        string officeId,
        int deviceNo,
        string role,
        string employee,
        int employeeNo)
    {
        Assert.Equal(
            DeviceRefusal.None,
            devices.AddDevice(officeId, deviceNo, role, employee, employeeNo, SyncScopes.Full, out var device));
        Assert.NotNull(device);
        devices.ActivateAccount(device);
        return device;
    }
}
