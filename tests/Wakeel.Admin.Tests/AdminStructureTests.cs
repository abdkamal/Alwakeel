using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Services.Devices;
using Wakeel.Admin.UI.Services.Organisation;
using Wakeel.Admin.UI.Services.Structure;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A05 «الهيكلية»: the four fixed layers, the rules that keep a tree a tree, and what marking a
/// node as an office does to the rest of the tool.
/// </summary>
public class AdminStructureTests : AdminTestContext
{
    [Fact]
    public void Root_IsTheOrganisationItselfAndIsMadeOnce()
    {
        CreateAccount(orgName: "هيئة تنمية المناطق الريفية");

        var first = Structure.EnsureRoot();
        var second = Structure.EnsureRoot();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(OrgLevel.Org, first.Level);
        Assert.Null(first.ParentId);
        Assert.Equal("هيئة تنمية المناطق الريفية", first.Name);
        Assert.Single(Structure.ReadAll(), node => node.Level == OrgLevel.Org);
    }

    [Fact]
    public void Root_FollowsTheOrganisationWhenA04RenamesIt()
    {
        CreateAccount();
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);

        Assert.Equal(
            AdminOrgRefusal.None,
            Org.Save("هيئة الموانئ", cycleStartDay: 1, numberingFormat: "YYYYMMDD/DESSS"));
        Structure.SyncRootName();

        Assert.Equal("هيئة الموانئ", Structure.ReadOne(root.Id)?.Name);
    }

    [Fact]
    public void Add_DecidesTheLevelFromTheParentSoTheFourLayersCannotBeGotWrong()
    {
        var root = Root();

        Assert.Equal(StructureRefusal.None, Structure.Add(root, "دائرة الشؤون الإدارية", null, null, out var department));
        Assert.NotNull(department);
        Assert.Equal(OrgLevel.Department, Structure.ReadOne(department)!.Level);

        Assert.Equal(StructureRefusal.None, Structure.Add(department, "قسم الحسابات", null, null, out var section));
        Assert.NotNull(section);
        Assert.Equal(OrgLevel.Section, Structure.ReadOne(section)!.Level);

        Assert.Equal(StructureRefusal.None, Structure.Add(section, "وحدة الرواتب", null, null, out var unit));
        Assert.NotNull(unit);
        Assert.Equal(OrgLevel.Unit, Structure.ReadOne(unit)!.Level);
    }

    [Fact]
    public void Add_RefusesAFifthLayerUnderneathAUnit()
    {
        var unit = Unit(out _, out _);

        var refusal = Structure.Add(unit, "شعبة جديدة", null, null, out var nothing);

        Assert.Equal(StructureRefusal.WrongLevel, refusal);
        Assert.Null(nothing);
    }

    [Fact]
    public void Add_RefusesAnEmptyNameAndARepeatedNameAmongTheSameBrothers()
    {
        var root = Root();
        Assert.Equal(StructureRefusal.NameRequired, Structure.Add(root, "   ", null, null, out _));
        Assert.Equal(StructureRefusal.None, Structure.Add(root, "دائرة المالية", null, null, out var first));
        Assert.Equal(StructureRefusal.DuplicateName, Structure.Add(root, " دائرة المالية ", null, null, out var twin));

        Assert.NotNull(first);
        Assert.Null(twin);
    }

    [Fact]
    public void Add_LetsTheSameNameStandUnderTwoDifferentParents()
    {
        var root = Root();
        Structure.Add(root, "دائرة المالية", null, null, out var a);
        Structure.Add(root, "دائرة الهندسة", null, null, out var b);

        Assert.Equal(StructureRefusal.None, Structure.Add(a!, "قسم المتابعة", null, null, out _));
        Assert.Equal(StructureRefusal.None, Structure.Add(b!, "قسم المتابعة", null, null, out _));
    }

    [Fact]
    public void Update_KeepsTheNameRuleAndStoresWhoHeadsTheNode()
    {
        var root = Root();
        Structure.Add(root, "دائرة المالية", null, null, out var a);
        Structure.Add(root, "دائرة الهندسة", null, null, out var b);

        Assert.Equal(StructureRefusal.DuplicateName, Structure.Update(b!, "دائرة المالية", null, null));
        Assert.Equal(StructureRefusal.None, Structure.Update(b!, "دائرة الهندسة", "منى العلي", "مدير الدائرة"));

        var node = Structure.ReadOne(b!);
        Assert.Equal("منى العلي", node!.HeadName);
        Assert.Equal("مدير الدائرة", node.HeadTitle);
    }

    [Fact]
    public void Move_RefusesToPutABranchInsideItselfOrUnderTheWrongLayer()
    {
        var root = Root();
        Structure.Add(root, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", null, null, out var section);
        Structure.Add(section!, "وحدة الرواتب", null, null, out var unit);

        // The layer rule is what actually keeps a branch out of itself: the only place a node may
        // land is under the layer above it, and everything inside its own branch is below it. The
        // loop guard behind it is belt and braces, and A05's parent chooser offers neither.
        Assert.Equal(StructureRefusal.WrongLevel, Structure.Move(department!, section!));
        Assert.Equal(StructureRefusal.WrongLevel, Structure.Move(department!, department!));
        Assert.Equal(StructureRefusal.WrongLevel, Structure.Move(section!, unit!));
        Assert.Equal(StructureRefusal.WrongLevel, Structure.Move(unit!, unit!));
        Assert.True(Structure.IsDescendantOf(unit!, department!));
        Assert.False(Structure.IsDescendantOf(department!, unit!));
        Assert.Equal(section, Structure.ReadOne(unit!)!.ParentId);
    }

    [Fact]
    public void Move_CarriesASectionToAnotherDepartmentAndRefusesToLandOnATakenName()
    {
        var root = Root();
        Structure.Add(root, "دائرة المالية", null, null, out var from);
        Structure.Add(root, "دائرة الهندسة", null, null, out var to);
        Structure.Add(from!, "قسم الحسابات", null, null, out var section);
        Structure.Add(to!, "قسم الحسابات", null, null, out var twin);

        Assert.Equal(StructureRefusal.DuplicateName, Structure.Move(section!, to!));

        Assert.Equal(StructureRefusal.None, Structure.Update(twin!, "قسم التصميم", null, null));
        Assert.Equal(StructureRefusal.None, Structure.Move(section!, to!));
        Assert.Equal(to, Structure.ReadOne(section!)!.ParentId);
    }

    [Fact]
    public void Disable_TakesTheWholeBranchOutOfServiceAndEnablingPutsItBack()
    {
        var unit = Unit(out var department, out _);

        Assert.Equal(StructureRefusal.None, Structure.Disable(department));

        var all = Structure.ReadAll();
        var unitNode = all.Single(n => n.Id == unit);
        Assert.True(Structure.ReadOne(department)!.IsDisabled);
        Assert.False(unitNode.IsDisabled);
        Assert.True(Structure.IsOutOfService(all, unitNode));

        Assert.Equal(StructureRefusal.None, Structure.Enable(department));
        var back = Structure.ReadAll();
        Assert.False(Structure.IsOutOfService(back, back.Single(n => n.Id == unit)));
    }

    [Fact]
    public void Delete_RefusesANodeThatStillHasSomethingHangingFromIt()
    {
        Unit(out var department, out var section);

        Assert.Equal(StructureRefusal.HasChildren, Structure.Delete(department));
        Assert.Equal(StructureRefusal.HasChildren, Structure.Delete(section));
    }

    [Fact]
    public void Delete_RefusesAUnitThatStillHasADeviceThatHasNotBeenShutOut()
    {
        var office = OfficeWithDevice(out var unit, out var deviceId);

        Assert.Equal(StructureRefusal.HasActiveDevices, Structure.Delete(unit));
        Assert.Equal(StructureRefusal.HasActiveDevices, Structure.ClearOffice(unit));

        Assert.Equal(1, DeviceRegistry.ReadOffice(office)!.ActiveDevices);
        Assert.NotNull(deviceId);
    }

    [Fact]
    public void Delete_LetsAnEmptyUnitGo()
    {
        var unit = Unit(out _, out _);

        Assert.Equal(StructureRefusal.None, Structure.Delete(unit));
        Assert.Null(Structure.ReadOne(unit));
    }

    [Fact]
    public void Office_TakesAnInventoryCodeThatNoOtherOfficeMayCarry()
    {
        var root = Root();
        Structure.Add(root, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", null, null, out var section);
        Structure.Add(section!, "وحدة الرواتب", null, null, out var unitA);
        Structure.Add(section!, "وحدة المخازن", null, null, out var unitB);

        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unitA!, "OF-001"));
        Assert.Equal(StructureRefusal.OfficeCodeTaken, Structure.SetOffice(unitB!, "OF-001"));
        Assert.Equal(StructureRefusal.OfficeCodeTaken, Structure.SetOffice(unitB!, "  "));
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unitB!, "OF-002"));

        // The same office may change its own code without colliding with itself.
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unitA!, "OF-001"));

        var codes = DeviceRegistry.ListOffices().Select(o => o.OfficeCode).OrderBy(c => c, StringComparer.Ordinal);
        Assert.Equal(["OF-001", "OF-002"], codes);
    }

    [Fact]
    public void Office_MayBeADepartmentOrASectionButNeverTheOrganisation()
    {
        var root = Root();
        Structure.Add(root, "دائرة المالية", null, null, out var department);
        Structure.Add(department!, "قسم الحسابات", null, null, out var section);

        Assert.Equal(StructureRefusal.WrongLevel, Structure.SetOffice(root, "OF-000"));
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(department!, "OF-010"));
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(section!, "OF-011"));
    }

    [Fact]
    public void Office_ClearingOneThatIsEmptyTakesItOffTheOfficeList()
    {
        var unit = Unit(out _, out _);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit, "OF-050"));
        Assert.Single(DeviceRegistry.ListOffices());

        Assert.Equal(StructureRefusal.None, Structure.ClearOffice(unit));
        Assert.Empty(DeviceRegistry.ListOffices());
        Assert.False(Structure.ReadOne(unit)!.IsOffice);
    }

    [Fact]
    public void EveryChange_QueuesOneThingWaitingToBeDistributed()
    {
        var before = Pending.OpenCount;
        var unit = Unit(out _, out _);
        Structure.SetOffice(unit, "OF-070");

        Assert.True(Pending.OpenCount > before);
    }

    [Fact]
    public void TheDashboard_CountsTheStructureAndItsDevicesStraightOutOfTheTables()
    {
        var office = OfficeWithDevice(out _, out _);

        var board = Dashboard.Read();

        Assert.Equal(1, board.Departments);
        Assert.Equal(1, board.Sections);
        Assert.Equal(1, board.Units);
        Assert.Equal(1, board.Offices);
        Assert.Equal(1, board.Devices);
        Assert.Equal(0, board.ActivatedOffices);
        Assert.Equal(1, board.OfficesWaiting);
        Assert.False(board.IsEmpty);
        Assert.Contains(board.Alerts, a => a.Kind == AdminAlertKind.OfficeWithoutSetup);
        Assert.NotNull(DeviceRegistry.ReadOffice(office));
    }

    [Fact]
    public void TheDashboard_StopsCountingABranchThatHasBeenSwitchedOff()
    {
        OfficeWithDevice(out var unit, out _);
        var department = Structure.ReadOne(Structure.ReadOne(unit)!.ParentId!)!.ParentId!;

        Assert.Equal(StructureRefusal.None, Structure.Disable(department));
        var board = Dashboard.Read();

        Assert.Equal(0, board.Offices);
        Assert.Equal(0, board.Devices);
    }

    [Fact]
    public void WhatIsWrittenDownAgreesWithTheLayerItIsAbout()
    {
        // هيئة، دائرة and وحدة are feminine nouns and قسم is masculine, so the verb in front of the
        // layer has to change with it. These lines are what the waiting list shows and what the
        // operations log keeps, so a wrong agreement is read by the person every day.
        var root = Root();
        Structure.Add(root, "دائرة الشؤون الإدارية", null, null, out var department);
        Structure.Add(department!, "قسم الصادر والوارد", null, null, out var section);
        Structure.Add(section!, "وحدة الأرشيف المركزي", null, null, out var unit);

        var waiting = Pending.Open().Select(change => change.SummaryAr).ToList();

        Assert.Contains(waiting, line => line.StartsWith("أُضيفت دائرة «دائرة الشؤون الإدارية»", StringComparison.Ordinal));
        Assert.Contains(waiting, line => line.StartsWith("أُضيف قسم «قسم الصادر والوارد»", StringComparison.Ordinal));
        Assert.Contains(waiting, line => line.StartsWith("أُضيفت وحدة «وحدة الأرشيف المركزي»", StringComparison.Ordinal));

        Assert.Equal(StructureRefusal.None, Structure.Disable(section!));
        Assert.Equal(StructureRefusal.None, Structure.Disable(unit!));

        waiting = Pending.Open().Select(change => change.SummaryAr).ToList();

        Assert.Contains(waiting, line => line.StartsWith("أُوقف قسم", StringComparison.Ordinal));
        Assert.Contains(waiting, line => line.StartsWith("أُوقفت وحدة", StringComparison.Ordinal));

        // And the two the tree does not reach in this run, straight from the wording itself.
        Assert.StartsWith("عُدّل قسم", AdminAr.Structure.Log.Updated(OrgLevel.Section, "قسم"), StringComparison.Ordinal);
        Assert.StartsWith("عُدّلت دائرة", AdminAr.Structure.Log.Updated(OrgLevel.Department, "دائرة"), StringComparison.Ordinal);
        Assert.StartsWith("حُذف قسم", AdminAr.Structure.Log.Deleted(OrgLevel.Section, "قسم"), StringComparison.Ordinal);
        Assert.StartsWith("حُذفت هيئة", AdminAr.Structure.Log.Deleted(OrgLevel.Org, "هيئة"), StringComparison.Ordinal);
    }

    private string Root()
    {
        CreateAccount();
        var root = Structure.EnsureRoot();
        Assert.NotNull(root);
        return root.Id;
    }

    private string Unit(out string department, out string section)
    {
        var root = Root();
        Structure.Add(root, "دائرة المالية", null, null, out var d);
        Structure.Add(d!, "قسم الحسابات", null, null, out var s);
        Structure.Add(s!, "وحدة الرواتب", null, null, out var u);
        department = d!;
        section = s!;
        return u!;
    }

    private string OfficeWithDevice(out string unit, out string deviceId)
    {
        unit = Unit(out _, out _);
        Assert.Equal(StructureRefusal.None, Structure.SetOffice(unit, "OF-100"));
        var office = DeviceRegistry.ListOffices().Single().Id;
        Assert.Equal(
            DeviceRefusal.None,
            DeviceRegistry.AddDevice(office, 1, DeviceRoles.Manager, "سامي الحاج", 1, SyncScopes.Full, out var id));
        Assert.NotNull(id);
        deviceId = id;
        return office;
    }
}
