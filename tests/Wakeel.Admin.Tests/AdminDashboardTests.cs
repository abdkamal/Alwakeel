using System.Globalization;
using Wakeel.Admin.UI.Services;
using Wakeel.Admin.UI.Text;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A03: the dashboard counts what is actually in the tool's data, including the tables admin-2 and
/// admin-3 will fill — so it reads zeroes today and real numbers the moment they write a row,
/// without any change here.
/// </summary>
public class AdminDashboardTests : AdminTestContext
{
    [Fact]
    public void ANewOrganisation_ShowsItsNameAndNothingElseYet()
    {
        CreateAccount();

        var board = Dashboard.Read();

        Assert.Equal("هيئة تنمية المناطق الريفية", board.OrgName);
        Assert.True(board.IsEmpty);
        Assert.Equal(0, board.Departments);
        Assert.Empty(board.Branches);
        Assert.Empty(board.Alerts);
        Assert.Null(board.LastExportAt);
    }

    [Fact]
    public void OrgLine_SaysTheSameAsTheBoardWithoutReadingTheRest()
    {
        CreateAccount();
        Seed();

        var board = Dashboard.Read();
        var line = Dashboard.ReadOrgLine();

        // The shell re-reads this on every navigation, so it has its own narrow query; the two must
        // never drift apart.
        Assert.Equal(board.OrgName, line.OrgName);
        Assert.Equal(board.Departments, line.Departments);
        Assert.Equal(board.Sections, line.Sections);
        Assert.Equal(board.Units, line.Units);

        Accounts.SignOut();
        Assert.Equal(AdminOrgLine.Empty, Dashboard.ReadOrgLine());
    }

    [Fact]
    public void ALockedTool_ShowsAnEmptyBoardRatherThanFailing()
    {
        CreateAccount();
        Accounts.SignOut();

        var board = Dashboard.Read();

        Assert.Equal(AdminDashboard.Empty, board);
    }

    [Fact]
    public void Counts_ComeFromTheTablesThemselves()
    {
        CreateAccount();
        Seed();

        var board = Dashboard.Read();

        Assert.Equal(2, board.Departments);
        Assert.Equal(2, board.Sections);
        Assert.Equal(1, board.Units);
        Assert.Equal(2, board.Offices);
        Assert.Equal(1, board.ActivatedOffices);
        Assert.Equal(1, board.OfficesWaiting);
        Assert.Equal(2, board.Devices);
        Assert.Equal(1, board.RevokedDevices);
        Assert.Equal(1, board.RegisteredDevices);
    }

    [Fact]
    public void TheStructureSummary_HasOneLinePerDepartment()
    {
        CreateAccount();
        Seed();

        var board = Dashboard.Read();

        Assert.Equal(2, board.Branches.Count);

        var planning = Assert.Single(board.Branches, branch => branch.Name == "دائرة التخطيط");
        Assert.Equal(1, planning.Sections);
        Assert.Equal(1, planning.Units);
        Assert.Equal(1, planning.Offices);
        Assert.Equal(0, planning.OfficesWaiting);

        var finance = Assert.Single(board.Branches, branch => branch.Name == "دائرة المالية");
        Assert.Equal(1, finance.Offices);
        Assert.Equal(1, finance.OfficesWaiting);
    }

    [Fact]
    public void Alerts_NameTheOfficeWithoutASetupFile_TheUndistributedChangeAndTheRevokedDevice()
    {
        CreateAccount();
        Seed();

        var alerts = Dashboard.Read().Alerts;

        var waiting = Assert.Single(alerts, alert => alert.Kind == AdminAlertKind.OfficeWithoutSetup);
        Assert.Equal("مكتب دائرة المالية", waiting.Title);

        var pending = Assert.Single(alerts, alert => alert.Kind == AdminAlertKind.UndistributedChanges);
        Assert.Equal("أُضيف قسم جديد", pending.Detail);

        Assert.Single(alerts, alert => alert.Kind == AdminAlertKind.RevokedDevice);
    }

    [Fact]
    public void AlertsSummary_NamesOnlyTheKindsThatAreActuallyThere()
    {
        // A board whose only alert is a revoked device says so, and says nothing whatever about
        // setup files — the line under «تنبيهات» has to be true of the alerts it sits above.
        var revokedOnly = AdminAr.Dashboard.AlertsSummary(0, 0, 1);
        Assert.Equal("جهاز مُلغى", revokedOnly);
        Assert.DoesNotContain("الإعداد", revokedOnly, StringComparison.Ordinal);

        var changesOnly = AdminAr.Dashboard.AlertsSummary(0, 3, 0);
        Assert.Contains("بانتظار التوزيع", changesOnly, StringComparison.Ordinal);
        Assert.DoesNotContain("الإعداد", changesOnly, StringComparison.Ordinal);

        var all = AdminAr.Dashboard.AlertsSummary(2, 1, 1);
        Assert.Contains("مكتبان بانتظار ملف الإعداد", all, StringComparison.Ordinal);
        Assert.Contains("تغيير بانتظار التوزيع", all, StringComparison.Ordinal);
        Assert.Contains("جهاز مُلغى", all, StringComparison.Ordinal);

        Assert.Equal(string.Empty, AdminAr.Dashboard.AlertsSummary(0, 0, 0));
    }

    [Fact]
    public void RevokedDevices_KeepsTheAdjectiveAgreeingWithTheNounTheNumberPicked()
    {
        // Three to ten takes the broken plural «أجهزة», and only there does «مُلغاة» belong with it;
        // eleven and up goes back to the singular noun, so the adjective has to go back with it.
        Assert.Equal("جهاز مُلغى", AdminAr.Dashboard.RevokedDevices(1));
        Assert.Equal("جهازان مُلغيان", AdminAr.Dashboard.RevokedDevices(2));
        // The figure inside the sentence is written the way the rest of the tool's prose writes one.
        Assert.Equal("4 أجهزة مُلغاة", AdminAr.Dashboard.RevokedDevices(4));
        Assert.Equal("11 جهازًا مُلغى", AdminAr.Dashboard.RevokedDevices(11));

        Assert.Contains("11 جهازًا مُلغى", AdminAr.Dashboard.AlertsSummary(0, 0, 11), StringComparison.Ordinal);
        Assert.Contains("11 جهازًا مُلغى", AdminAr.Dashboard.DevicesStatus(2, 0, 11), StringComparison.Ordinal);

        // And the adjective on the working side agrees the same way, so «جهازان مفعّلة» cannot happen.
        Assert.Equal("جهاز مفعّل", AdminAr.Counting.ActivatedDevices(1));
        Assert.Equal("جهازان مفعّلان", AdminAr.Counting.ActivatedDevices(2));
        Assert.Equal("3 أجهزة مفعّلة", AdminAr.Counting.ActivatedDevices(3));
        Assert.Equal("11 جهازًا مفعّلًا", AdminAr.Counting.ActivatedDevices(11));
        Assert.StartsWith("جهازان مفعّلان", AdminAr.Dashboard.DevicesStatus(2, 0, 11), StringComparison.Ordinal);
    }

    [Fact]
    public void Counting_WritesTheFigureInsideASentenceTheWayTheRestOfTheProseDoes()
    {
        // A06 draws «3 أجهزة في هذا المكتب» above rows that read «الجهاز 1/2/3»; a sentence that
        // mixed the two shapes of figure would read as two voices.
        Assert.Equal("3 أجهزة", AdminAr.Counting.Devices(3));
        Assert.Equal("8 تغييرات", AdminAr.Counting.Changes(8));
        Assert.Equal("12 مكتبًا", AdminAr.Counting.Offices(12));
        Assert.Equal("لا دوائر", AdminAr.Counting.Departments(0));
    }

    [Fact]
    public void AlertsNoneDesc_SaysNothingAboutOfficesOnAToolThatHasNone()
    {
        CreateAccount();

        var board = Dashboard.Read();

        Assert.True(board.IsEmpty);
        Assert.Equal(0, board.AlertCount);
        Assert.DoesNotContain("المكاتب", AdminAr.Dashboard.AlertsNoneDesc(board.IsEmpty), StringComparison.Ordinal);
        Assert.Contains("كل المكاتب", AdminAr.Dashboard.AlertsNoneDesc(false), StringComparison.Ordinal);
    }

    [Fact]
    public void AlertCounts_AreTheWholeTruthEvenWhenOnlyAFewLinesAreDrawn()
    {
        CreateAccount();
        Seed();
        SeedManyWaitingOffices(24);

        var board = Dashboard.Read();

        // The panel draws a handful of lines, but what it says in words has to agree with the card
        // above it: twenty-five offices are waiting, and twenty-five is what both of them say.
        Assert.Equal(25, board.OfficesWaiting);
        Assert.Equal(1, board.PendingChanges);
        Assert.Equal(1, board.RevokedDevices);
        Assert.Equal(27, board.AlertCount);
        Assert.True(board.Alerts.Count(alert => alert.Kind == AdminAlertKind.OfficeWithoutSetup) < 25);

        Assert.Contains(
            AdminAr.Counting.Offices(25),
            AdminAr.Dashboard.AlertsSummary(board.OfficesWaiting, board.PendingChanges, board.RevokedDevices),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADisabledUnit_TakesItsOfficesAndTheirDevicesOffTheBoard()
    {
        CreateAccount();
        Seed();

        // THE RULE the whole tool follows: an office counts only while the unit it hangs from and
        // every unit above it is in service. Switching off the finance department takes its office
        // — and the «waiting for a setup file» alert that office was raising — off the board with
        // it, so the cards and the alerts can never name a branch the structure summary no longer
        // draws. The rows stay in the tables; only the counting stops.
        Disable("u-fin");

        var board = Dashboard.Read();

        Assert.Equal(1, board.Departments);
        Assert.Equal(1, board.Offices);
        Assert.Equal(1, board.ActivatedOffices);
        Assert.Equal(0, board.OfficesWaiting);
        Assert.DoesNotContain(board.Alerts, alert => alert.Kind == AdminAlertKind.OfficeWithoutSetup);
        Assert.Single(board.Branches);
        Assert.Equal(2, board.AlertCount);

        // The planning department still carries its two devices, one of them revoked.
        Assert.Equal(2, board.Devices);
        Assert.Equal(1, board.RevokedDevices);

        // Switching off the department above them takes the devices too, and with them the revoked
        // device alert and the export that was made for one of them.
        Disable("u-plan");
        var emptied = Dashboard.Read();

        Assert.Equal(0, emptied.Offices);
        Assert.Equal(0, emptied.Devices);
        Assert.Equal(0, emptied.RevokedDevices);
        Assert.DoesNotContain(emptied.Alerts, alert => alert.Kind == AdminAlertKind.RevokedDevice);
        Assert.Null(emptied.LastExportAt);
    }

    [Fact]
    public void ADisabledDepartment_TakesWhatHangsTwoLayersBelowItAsWell()
    {
        CreateAccount();
        Seed();

        var at = Time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        Db.Execute(
            """
            INSERT INTO offices(id, unit_id, name, office_code, created_at, updated_at)
            VALUES ('o-deep', 'u-plan-u', 'مكتب وحدة المشاريع', 'PRJ', $at, $at);
            """,
            ("$at", at));

        Assert.Equal(3, Dashboard.Read().Offices);

        // The office hangs from the unit, which hangs from the section, which hangs from the
        // department being switched off. The whole chain above an office is what decides.
        Disable("u-plan");

        Assert.Equal(1, Dashboard.Read().Offices);
    }

    /// <summary>Switches a unit out of service, the way A05 will.</summary>
    private void Disable(string unitId) => Db.Execute(
        "UPDATE org_units SET disabled_at = $at WHERE id = $id;",
        ("$at", Time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)),
        ("$id", unitId));

    /// <summary>Offices with no setup file, more of them than the alerts panel ever draws.</summary>
    private void SeedManyWaitingOffices(int count)
    {
        var at = Time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        for (var index = 0; index < count; index++)
        {
            Db.Execute(
                """
                INSERT INTO offices(id, unit_id, name, office_code, created_at, updated_at)
                VALUES ($id, 'u-fin', $name, $code, $at, $at);
                """,
                ("$id", $"o-many-{index}"),
                ("$name", $"مكتب فرعي {index}"),
                ("$code", $"M{index:D2}"),
                ("$at", at));
        }
    }

    [Fact]
    public void LastExport_IsTheMostRecentOneAndNamesItsOffice()
    {
        CreateAccount();
        Seed();

        var board = Dashboard.Read();

        Assert.NotNull(board.LastExportAt);
        Assert.Equal("مكتب دائرة التخطيط", board.LastExportOffice);
    }

    /// <summary>
    /// The rows admin-2 and admin-3 will write, written here by hand: two departments, a section, a
    /// unit, two offices (one still waiting for its setup file), two devices (one revoked), one
    /// export and one undistributed change.
    /// </summary>
    private void Seed()
    {
        var at = Time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

        void Unit(string id, string? parent, string level, string name) => Db.Execute(
            """
            INSERT INTO org_units(id, parent_id, level, name, sort_order, created_at, updated_at)
            VALUES ($id, $parent, $level, $name, 0, $at, $at);
            """,
            ("$id", id), ("$parent", parent), ("$level", level), ("$name", name), ("$at", at));

        Unit("u-org", null, "org", "هيئة تنمية المناطق الريفية");
        Unit("u-plan", "u-org", "department", "دائرة التخطيط");
        Unit("u-fin", "u-org", "department", "دائرة المالية");
        Unit("u-plan-s", "u-plan", "section", "قسم الدراسات");
        Unit("u-fin-s", "u-fin", "section", "قسم الحسابات");
        Unit("u-plan-u", "u-plan-s", "unit", "وحدة المشاريع");

        Db.Execute(
            """
            INSERT INTO offices(id, unit_id, name, office_code, activated_at, created_at, updated_at)
            VALUES ('o-plan', 'u-plan', 'مكتب دائرة التخطيط', 'PLN', $at, $at, $at);
            """,
            ("$at", at));

        Db.Execute(
            """
            INSERT INTO offices(id, unit_id, name, office_code, created_at, updated_at)
            VALUES ('o-fin', 'u-fin', 'مكتب دائرة المالية', 'FIN', $at, $at);
            """,
            ("$at", at));

        Db.Execute(
            """
            INSERT INTO devices(id, office_id, device_no, ed25519_pub, x25519_pub, issued_at, created_at, updated_at)
            VALUES ('d-1', 'o-plan', 1, 'pub-1', 'x-1', $at, $at, $at);
            """,
            ("$at", at));

        Db.Execute(
            """
            INSERT INTO devices(id, office_id, device_no, ed25519_pub, x25519_pub, issued_at, revoked_at, created_at, updated_at)
            VALUES ('d-2', 'o-plan', 2, 'pub-2', 'x-2', $at, $at, $at, $at);
            """,
            ("$at", at));

        Db.Execute(
            """
            INSERT INTO setup_exports(id, device_id, version, exported_at, file_name, includes)
            VALUES ('e-1', 'd-1', 1, $at, 'PLN-1.wakeel-setup', '{}');
            """,
            ("$at", at));

        Db.Execute(
            """
            INSERT INTO pending_changes(id, entity_type, entity_id, summary_ar, created_at)
            VALUES ('p-1', 'org_unit', 'u-fin-s', 'أُضيف قسم جديد', $at);
            """,
            ("$at", at));
    }
}
