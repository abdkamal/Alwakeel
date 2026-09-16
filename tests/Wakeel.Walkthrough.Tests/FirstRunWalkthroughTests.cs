using System.Text;
using Microsoft.EntityFrameworkCore;
using Wakeel.Core.Data;
using Wakeel.Core.Data.Entities;
using Wakeel.Core.Services;
using Wakeel.Crypto;
using Wakeel.UI.Services.Account;

namespace Wakeel.Walkthrough.Tests;

/// <summary>
/// The whole first run of one machine, in the order a person lives it: the administrator's file
/// arrives (W02), it is checked (W03), the installation is built and a recovery sheet printed (W04),
/// the person signs in (W05), the machine locks itself and is opened again (W06), a forgotten
/// password is replaced from the sheet (W07). Every step looks at what actually landed on the disk
/// and in the database, not merely at what the service returned.
/// </summary>
public sealed class FirstRunWalkthroughTests
{
    [Fact]
    public async Task Whole_first_run_from_an_empty_machine_to_a_recovered_account()
    {
        using var world = new FirstRunWorld();

        // ---- W02: the file the administrator handed over -------------------------------------
        var exported = world.Export();
        Assert.True(File.Exists(exported));
        Assert.False(world.Login.IsActivated);

        var staged = await world.ChooseAsync(exported);
        Assert.EndsWith(SetupInspectionService.Extension, staged.FileName, StringComparison.Ordinal);
        Assert.True(staged.Size > 0);
        Assert.StartsWith(world.Paths.Root, staged.Path, StringComparison.Ordinal);

        // ---- W03: every line of the check list holds ------------------------------------------
        world.Inspection.Inspect(world.PackagePasswordText);
        Assert.Null(world.Inspection.RefusalMessage);
        Assert.True(world.Inspection.IsAcceptable);
        Assert.DoesNotContain(world.Inspection.Lines, line => line.Status == SetupCheckStatus.Failed);
        Assert.Contains(world.Inspection.Lines, line => line.Item == SetupCheckItem.Signature);
        Assert.Contains(world.Inspection.Lines, line => line.Item == SetupCheckItem.OfficeKey);

        var summary = Assert.IsType<SetupSummary>(world.Inspection.Summary);
        Assert.Equal(FirstRunWorld.OrgName, summary.OrgName);
        Assert.Equal(FirstRunWorld.OfficeName, summary.OfficeName);
        Assert.Equal(FirstRunWorld.OfficeCode, summary.OfficeCode);
        Assert.Equal(1, summary.DeviceNo);
        Assert.Equal(FirstRunWorld.EmployeeName, summary.EmployeeName);
        Assert.NotNull(summary.LogoDataUrl);

        // ---- W04: the recovery sheet, then the installation -----------------------------------
        Assert.True(world.Activation.CanActivate);
        var sheet = world.Activation.Begin();
        Assert.Same(sheet, world.Activation.Begin());
        Assert.True(RecoveryCode.TryParse(sheet.CodeDisplay, out _));
        Assert.StartsWith("data:image/png;base64,", sheet.QrDataUrl, StringComparison.Ordinal);
        Assert.Equal(FirstRunWorld.EmployeeName, sheet.EmployeeName);
        var recoveryCode = sheet.CodeDisplay;

        await world.Activation.ActivateAsync(FirstRunWorld.FirstPassword);

        Assert.True(File.Exists(world.Paths.InstallationKeyPath));
        Assert.True(File.Exists(world.Paths.DbPath));
        Assert.True(world.Login.IsActivated);
        Assert.Null(world.Activation.Sheet);

        // The staged payload carried the device's own private seeds and the office key in the clear.
        Assert.False(Directory.Exists(world.Inspection.StagingDirectory));

        var keyFile = InstallationKeyFile.Load(world.Paths.InstallationKeyPath);
        foreach (var kind in new[] { KeyWrapKind.Password, KeyWrapKind.Recovery, KeyWrapKind.Machine, KeyWrapKind.Admin })
        {
            Assert.NotNull(keyFile.FindDbKeyWrap(kind));
            Assert.NotNull(keyFile.FindVaultKeyWrap(kind));
        }

        // The identity rows the setup file described.
        Assert.True(world.Session.IsOpen);
        var db = world.Session.Db;
        var installation = await db.Installation.AsNoTracking().SingleAsync();
        Assert.Equal(FirstRunWorld.OrgName, installation.OrgName);
        Assert.Equal(FirstRunWorld.OfficeCode, installation.OfficeCode);
        Assert.Equal(FirstRunWorld.EmployeeName, installation.EmployeeName);
        Assert.Equal(InstallationRole.Secretary, installation.Role);
        Assert.Equal(1, installation.DeviceNo);

        Assert.Equal(4, await db.OrgUnits.CountAsync());
        Assert.Equal(1, await db.Devices.CountAsync());

        var autoLock = await new InstallationService(db, new SystemClock(world.Time)).GetAutoLockMinutesAsync();
        Assert.Equal(InstallationService.DefaultAutoLockMinutes, autoLock);

        // The logo, the guide and both templates travelled with the file and were stored.
        Assert.NotNull(installation.LogoDocumentId);
        var settings = await db.Settings.AsNoTracking().ToListAsync();
        Assert.Contains(settings, s => s.Key == AccountSettingKeys.GuideDocumentId);
        Assert.Contains(settings, s => s.Key == AccountSettingKeys.ReportTemplateDocumentId);
        Assert.Contains(settings, s => s.Key == AccountSettingKeys.LetterTemplateDocumentId);
        Assert.Contains(settings, s => s.Key == AccountSettingKeys.SetupExportSeq && s.Value == "1");
        Assert.Equal(4, await db.Documents.CountAsync());

        await AssertActivationAuditAsync(world);

        // ---- W05: signing in ------------------------------------------------------------------
        world.Session.SignOut();
        Assert.False(world.Session.IsOpen);

        var profile = world.Login.Profile();
        Assert.True(profile.HasIdentity);
        Assert.Equal(FirstRunWorld.EmployeeName, profile.EmployeeName);
        Assert.Equal(FirstRunWorld.OfficeName, profile.OfficeName);
        Assert.NotNull(profile.LogoDataUrl);

        var wrong = await world.Login.SignInAsync("كلمة-مرور-ليست-الصحيحة");
        Assert.Equal(SignInOutcome.WrongPassword, wrong.Outcome);
        Assert.Equal(1, wrong.Attempt);
        Assert.Equal(world.Options.MaxAttempts - 1, wrong.Remaining);
        Assert.False(world.Session.IsOpen);

        var good = await world.Login.SignInAsync(FirstRunWorld.FirstPassword);
        Assert.True(good.Succeeded);
        Assert.True(world.Session.IsOpen);
        Assert.Equal(0, world.Login.Profile().FailedAttempts);

        await AssertAuditAsync(world, "account.sign_in");
        await AssertAuditAsync(world, "account.sign_in_failed");

        // ---- W06: the machine locks itself, and is opened again --------------------------------
        world.Session.MarkActivity();
        Assert.False(world.Lock.LockIfIdle());

        world.Time.Advance(TimeSpan.FromMinutes(InstallationService.DefaultAutoLockMinutes + 1));
        Assert.True(world.Lock.LockIfIdle());
        Assert.True(world.Session.IsLocked);

        var badUnlock = await world.Lock.UnlockAsync("ليست كلمة المرور");
        Assert.Equal(SignInOutcome.WrongPassword, badUnlock.Outcome);
        Assert.True(world.Session.IsLocked);

        var unlocked = await world.Lock.UnlockAsync(FirstRunWorld.FirstPassword);
        Assert.True(unlocked.Succeeded);
        Assert.True(world.Session.IsOpen);

        await AssertAuditAsync(world, "account.lock");
        await AssertAuditAsync(world, "account.unlock");

        // ---- W07: the password is forgotten and replaced from the sheet ------------------------
        world.Session.SignOut();
        Assert.Equal(RecoveryOutcome.CodeWrong, world.Recovery.Verify(RecoveryCode.Generate().Display));
        Assert.Equal(RecoveryOutcome.CodeIncomplete, world.Recovery.Verify("لا شيء"));
        Assert.Equal(RecoveryOutcome.Success, world.Recovery.Verify(recoveryCode));

        var recovered = await world.Recovery.RecoverAsync(recoveryCode, FirstRunWorld.SecondPassword, issueNewSheet: true);
        Assert.True(recovered.Succeeded);
        var newSheet = Assert.IsType<RecoverySheet>(recovered.NewSheet);
        Assert.NotEqual(recoveryCode, newSheet.CodeDisplay);
        Assert.True(world.Session.IsOpen);

        // The forgotten password stops working the moment the new one is set.
        world.Session.SignOut();
        var stale = await world.Login.SignInAsync(FirstRunWorld.FirstPassword);
        Assert.Equal(SignInOutcome.WrongPassword, stale.Outcome);

        var withNew = await world.Login.SignInAsync(FirstRunWorld.SecondPassword);
        Assert.True(withNew.Succeeded);

        await AssertAuditAsync(world, "account.recover");

        // ---- Nothing secret was ever written down in the clear ---------------------------------
        AssertNoSecretsOnDisk(world, recoveryCode, newSheet.CodeDisplay);
    }

    [Fact]
    public async Task A_wrong_package_password_refuses_the_file_and_names_no_technical_reason()
    {
        using var world = new FirstRunWorld();
        await world.ChooseAsync(world.Export());

        world.Inspection.Inspect(PackagePassword.New());

        Assert.False(world.Inspection.IsAcceptable);
        Assert.False(world.Activation.CanActivate);
        var message = Assert.IsType<string>(world.Inspection.RefusalMessage);
        AssertArabicAndPlain(message);
        Assert.False(File.Exists(world.Paths.InstallationKeyPath));
    }

    [Fact]
    public async Task A_file_signed_by_another_organisation_is_refused()
    {
        using var world = new FirstRunWorld();
        await world.ChooseAsync(world.Export());

        using var stranger = DeviceIdentity.Generate();
        world.Inspection.Inspect(
            world.PackagePasswordText,
            new SetupExpectations { PinnedOrgSigningPub = stranger.SigningPublicKey });

        Assert.False(world.Inspection.IsAcceptable);
        AssertArabicAndPlain(Assert.IsType<string>(world.Inspection.RefusalMessage));
    }

    [Fact]
    public async Task A_file_meant_for_another_machine_is_refused()
    {
        using var world = new FirstRunWorld();
        await world.ChooseAsync(world.Export());

        world.Inspection.Inspect(
            world.PackagePasswordText,
            new SetupExpectations { InstalledDeviceId = "PC-9", InstalledExportSeq = 1 });

        Assert.False(world.Inspection.IsAcceptable);
        AssertArabicAndPlain(Assert.IsType<string>(world.Inspection.RefusalMessage));

        // The card still names the organisation and the device the file was meant for — that is how
        // the person recognises it as somebody else's file — while nothing sealed inside it opens.
        var summary = Assert.IsType<SetupSummary>(world.Inspection.Summary);
        Assert.Equal(FirstRunWorld.OrgName, summary.OrgName);
        Assert.Equal(1, summary.DeviceNo);
        Assert.NotNull(summary.LogoDataUrl);
        Assert.False(world.Activation.CanActivate);
    }

    [Fact]
    public async Task A_file_older_than_the_one_already_installed_is_refused()
    {
        using var world = new FirstRunWorld();
        await world.ChooseAsync(world.Export(world.Content(exportSeq: 2)));

        world.Inspection.Inspect(
            world.PackagePasswordText,
            new SetupExpectations { InstalledDeviceId = FirstRunWorld.DeviceId, InstalledExportSeq = 5 });

        Assert.False(world.Inspection.IsAcceptable);
        AssertArabicAndPlain(Assert.IsType<string>(world.Inspection.RefusalMessage));
    }

    [Fact]
    public async Task A_file_dated_in_the_future_is_refused()
    {
        using var world = new FirstRunWorld();
        var ahead = FirstRunWorld.Start.AddDays(30);
        await world.ChooseAsync(world.Export(world.Content(exportedAt: ahead), at: ahead));

        world.Inspection.Inspect(world.PackagePasswordText);

        Assert.False(world.Inspection.IsAcceptable);
        AssertArabicAndPlain(Assert.IsType<string>(world.Inspection.RefusalMessage));
    }

    [Fact]
    public void Anything_that_is_not_a_setup_file_is_turned_away_before_it_is_even_read()
    {
        Assert.NotNull(SetupInspectionService.Reject("صورة.png", 1024));
        Assert.NotNull(SetupInspectionService.Reject(
            "office" + SetupInspectionService.Extension,
            SetupInspectionService.MaxFileBytes + 1));
        Assert.Null(SetupInspectionService.Reject("office" + SetupInspectionService.Extension, 4096));
    }

    [Fact]
    public async Task Five_wrong_passwords_in_a_row_stop_the_screen_accepting_any_for_a_while()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);
        world.Session.SignOut();

        SignInResult last = new() { Outcome = SignInOutcome.WrongPassword };
        for (var attempt = 1; attempt <= world.Options.MaxAttempts; attempt++)
        {
            last = await world.Login.SignInAsync("ليست كلمة المرور");
        }

        Assert.Equal(SignInOutcome.LockedOut, last.Outcome);
        Assert.True(last.LockMinutes > 0);
        Assert.NotNull(world.Login.LockedUntil());

        // Even the right password is refused while the lock-out stands.
        var duringLockOut = await world.Login.SignInAsync(FirstRunWorld.FirstPassword);
        Assert.Equal(SignInOutcome.LockedOut, duringLockOut.Outcome);
        AssertArabicAndPlain(Assert.IsType<string>(duringLockOut.Message));

        // And accepted again once it has passed.
        world.Time.Advance(TimeSpan.FromMinutes(world.Options.LockOutMinutes + 1));
        Assert.Null(world.Login.LockedUntil());
        Assert.True((await world.Login.SignInAsync(FirstRunWorld.FirstPassword)).Succeeded);

        // The wrong attempts nobody could write down at the time land in the log as one line.
        await AssertAuditAsync(world, "account.sign_in_failed");
    }

    [Fact]
    public async Task Recovery_without_a_new_sheet_keeps_the_old_code_working()
    {
        using var world = new FirstRunWorld();
        var code = await ActivateAsync(world);
        world.Session.SignOut();

        var result = await world.Recovery.RecoverAsync(code, FirstRunWorld.SecondPassword, issueNewSheet: false);

        Assert.True(result.Succeeded);
        Assert.Null(result.NewSheet);
        world.Session.SignOut();
        Assert.Equal(RecoveryOutcome.Success, world.Recovery.Verify(code));
    }

    [Fact]
    public async Task Recovery_refuses_a_new_password_that_is_too_weak()
    {
        using var world = new FirstRunWorld();
        var code = await ActivateAsync(world);
        world.Session.SignOut();

        var result = await world.Recovery.RecoverAsync(code, "123", issueNewSheet: false);

        Assert.Equal(RecoveryOutcome.PasswordRejected, result.Outcome);
        AssertArabicAndPlain(Assert.IsType<string>(result.Message));
        Assert.True((await world.Login.SignInAsync(FirstRunWorld.FirstPassword)).Succeeded);
    }

    [Fact]
    public async Task Signing_in_before_anything_was_activated_says_so_in_plain_words()
    {
        using var world = new FirstRunWorld();

        var result = await world.Login.SignInAsync(FirstRunWorld.FirstPassword);

        Assert.Equal(SignInOutcome.KeysMissing, result.Outcome);
        AssertArabicAndPlain(Assert.IsType<string>(result.Message));
        Assert.False(world.Login.Profile().HasIdentity);
    }

    [Fact]
    public async Task A_second_activation_is_refused_and_leaves_the_working_installation_untouched()
    {
        using var world = new FirstRunWorld();
        var code = await ActivateAsync(world);
        var keyFileBefore = await File.ReadAllBytesAsync(world.Paths.InstallationKeyPath);
        world.Session.SignOut();

        // A second setup file for the same machine, checked and accepted exactly as the first was.
        await world.ChooseAsync(world.Export(world.Content(exportSeq: 2), name: "second"));
        world.Inspection.Inspect(world.PackagePasswordText);
        Assert.True(world.Inspection.IsAcceptable);
        world.Activation.Begin();

        var result = await world.Activation.ActivateAsync("كلمة-مرور-أخرى-تمامًا-2026");

        Assert.Equal(ActivationOutcome.AlreadyActivated, result.Outcome);
        AssertArabicAndPlain(Assert.IsType<string>(result.Message));

        // Not one byte of the key file moved, so the account that was here still opens with the
        // password and the sheet it was given.
        Assert.Equal(keyFileBefore, await File.ReadAllBytesAsync(world.Paths.InstallationKeyPath));
        Assert.True((await world.Login.SignInAsync(FirstRunWorld.FirstPassword)).Succeeded);
        world.Session.SignOut();
        Assert.Equal(RecoveryOutcome.Success, world.Recovery.Verify(code));
    }

    /// <summary>
    /// An activation that cannot finish must leave the machine exactly as it found it. Half an
    /// installation — a key file with no database behind it — would be the worst of both worlds: the
    /// first-run screens would say the machine is already set up and the sign-in screen would never
    /// open anything, with no way out from inside الوكيل.
    /// </summary>
    [Fact]
    public async Task An_activation_that_cannot_finish_leaves_the_machine_as_it_found_it()
    {
        using var world = new FirstRunWorld();
        await world.ChooseAsync(world.Export());
        world.Inspection.Inspect(world.PackagePasswordText);
        Assert.True(world.Inspection.IsAcceptable);
        var sheet = world.Activation.Begin();

        // Something is standing exactly where the database has to be created, so the installation
        // fails after the key file has already been written.
        Directory.CreateDirectory(world.Paths.DbPath);

        await Assert.ThrowsAnyAsync<Exception>(() => world.Activation.ActivateAsync(FirstRunWorld.FirstPassword));

        // Nothing the attempt wrote outlives it.
        Assert.False(File.Exists(world.Paths.InstallationKeyPath));
        Assert.False(File.Exists(world.Paths.InstallationKeyPath + ".tmp"));
        Assert.False(File.Exists(world.Paths.InstallationKeyPath + ".bak"));
        Assert.False(world.Login.IsActivated);
        Assert.False(world.Activation.AlreadyActivated);

        Directory.Delete(world.Paths.DbPath, recursive: true);

        // The same file, the same sheet and the same code activate the machine on the second try.
        Assert.True(world.Activation.CanActivate);
        Assert.Same(sheet, world.Activation.Begin());
        var result = await world.Activation.ActivateAsync(FirstRunWorld.FirstPassword);

        Assert.True(result.Succeeded);
        Assert.True(world.Login.IsActivated);
        world.Session.SignOut();
        Assert.True((await world.Login.SignInAsync(FirstRunWorld.FirstPassword)).Succeeded);
        world.Session.SignOut();
        Assert.Equal(RecoveryOutcome.Success, world.Recovery.Verify(sheet.CodeDisplay));
    }

    /// <summary>
    /// Recovery replaces the password wrap and — when a new sheet is asked for — the recovery wrap
    /// as well. If that were written before the installation had been proved openable, a database
    /// that refuses to open would leave the old password dead and the replacement code, which nobody
    /// ever saw, buried in the key file. Nothing may be written until the database has opened.
    /// </summary>
    [Fact]
    public async Task A_recovery_that_cannot_open_the_installation_changes_no_secret()
    {
        using var world = new FirstRunWorld();
        var code = await ActivateAsync(world);
        world.Session.SignOut();

        var keyFileBefore = await File.ReadAllBytesAsync(world.Paths.InstallationKeyPath);
        var database = await File.ReadAllBytesAsync(world.Paths.DbPath);

        // The installation cannot be opened at the moment the sheet is used.
        File.Delete(world.Paths.DbPath);
        Directory.CreateDirectory(world.Paths.DbPath);

        var result = await world.Recovery.RecoverAsync(code, FirstRunWorld.SecondPassword, issueNewSheet: true);

        Directory.Delete(world.Paths.DbPath, recursive: true);
        await File.WriteAllBytesAsync(world.Paths.DbPath, database);

        if (result.Succeeded || result.Outcome == RecoveryOutcome.RecoveredNotSignedIn)
        {
            // If the new secrets did reach the disk, the sheet carrying the replacement code comes
            // back with them; it is never written and then thrown away.
            Assert.NotNull(result.NewSheet);
            return;
        }

        Assert.Equal(RecoveryOutcome.DatabaseUnreadable, result.Outcome);
        AssertArabicAndPlain(Assert.IsType<string>(result.Message));

        // Not one byte of the key file moved, so the password that was in force is still in force and
        // the sheet in the person's hands still opens the installation.
        Assert.Equal(keyFileBefore, await File.ReadAllBytesAsync(world.Paths.InstallationKeyPath));
        Assert.True((await world.Login.SignInAsync(FirstRunWorld.FirstPassword)).Succeeded);
        world.Session.SignOut();
        Assert.Equal(RecoveryOutcome.Success, world.Recovery.Verify(code));
    }

    /// <summary>
    /// The printed sheet is a secret like the password and is tried against the same key file, so it
    /// waits out the same pause instead of being the one place where guessing costs nothing.
    /// </summary>
    [Fact]
    public async Task Wrong_recovery_codes_count_towards_the_same_wait_as_wrong_passwords()
    {
        using var world = new FirstRunWorld();
        var code = await ActivateAsync(world);
        world.Session.SignOut();

        for (var attempt = 0; attempt < world.Options.MaxAttempts; attempt++)
        {
            Assert.Equal(RecoveryOutcome.CodeWrong, world.Recovery.Verify(RecoveryCode.Generate().Display));
        }

        // The wait now stands, and it stands for the right code and the sign-in screen alike.
        Assert.NotNull(world.Login.LockedUntil());
        Assert.Equal(RecoveryOutcome.LockedOut, world.Recovery.Verify(code));
        AssertArabicAndPlain(world.Recovery.Explain(RecoveryOutcome.LockedOut));

        var refused = await world.Recovery.RecoverAsync(code, FirstRunWorld.SecondPassword, issueNewSheet: false);
        Assert.Equal(RecoveryOutcome.LockedOut, refused.Outcome);

        // Checking the very same text again costs nothing, so a re-render never spends an attempt.
        var wrong = RecoveryCode.Generate().Display;
        world.Time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(RecoveryOutcome.CodeWrong, world.Recovery.Verify(wrong));
        var after = world.Login.Profile().FailedAttempts;
        Assert.Equal(RecoveryOutcome.CodeWrong, world.Recovery.Verify(wrong));
        Assert.Equal(after, world.Login.Profile().FailedAttempts);

        // Once the wait is over the sheet opens the installation as it always did.
        Assert.Equal(RecoveryOutcome.Success, world.Recovery.Verify(code));
        var recovered = await world.Recovery.RecoverAsync(code, FirstRunWorld.SecondPassword, issueNewSheet: false);
        Assert.True(recovered.Succeeded);
    }

    [Fact]
    public async Task An_installed_machine_judges_a_new_file_against_what_it_pinned()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);

        // What the product itself supplies once there is an installation — not something a test
        // invented — is what makes the last two refusal states reachable at all.
        var expectations = await world.Inspection.ExpectationsAsync();
        Assert.NotNull(expectations.PinnedOrgSigningPub);
        Assert.Equal(FirstRunWorld.DeviceId, expectations.InstalledDeviceId);
        Assert.Equal(1, expectations.InstalledExportSeq);

        // A newer file for this very machine still passes.
        await world.ChooseAsync(world.Export(world.Content(exportSeq: 3), name: "newer"));
        world.Inspection.Inspect(world.PackagePasswordText, await world.Inspection.ExpectationsAsync());
        Assert.True(world.Inspection.IsAcceptable);

        // A file for the machine next door does not.
        await world.ChooseAsync(world.Export(
            world.ContentForAnotherDevice("PC-9", exportSeq: 3),
            name: "other-machine"));
        world.Inspection.Inspect(world.PackagePasswordText, await world.Inspection.ExpectationsAsync());
        Assert.False(world.Inspection.IsAcceptable);
        AssertArabicAndPlain(Assert.IsType<string>(world.Inspection.RefusalMessage));

        // Neither does one older than what is already applied here.
        await world.ChooseAsync(world.Export(world.Content(exportSeq: 1), name: "older"));
        var pinned = await world.Inspection.ExpectationsAsync();
        world.Inspection.Inspect(
            world.PackagePasswordText,
            new SetupExpectations
            {
                PinnedOrgSigningPub = pinned.PinnedOrgSigningPub,
                InstalledDeviceId = pinned.InstalledDeviceId,
                InstalledExportSeq = 5,
                StagingDirectory = world.Inspection.StagingDirectory,
            });
        Assert.False(world.Inspection.IsAcceptable);
        AssertArabicAndPlain(Assert.IsType<string>(world.Inspection.RefusalMessage));
    }

    [Fact]
    public async Task A_damaged_key_file_is_a_message_on_the_screen_and_not_a_crash()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);
        world.Session.SignOut();

        // What a half-finished copy or a failing disk leaves behind: a key file that still loads,
        // but whose stored wrap no longer says how it was derived.
        var damaged = await File.ReadAllTextAsync(world.Paths.InstallationKeyPath);
        await File.WriteAllTextAsync(
            world.Paths.InstallationKeyPath,
            damaged.Replace("\"kdf\"", "\"kdfGone\"", StringComparison.Ordinal));

        var result = await world.Login.SignInAsync(FirstRunWorld.FirstPassword);

        Assert.NotEqual(SignInOutcome.Success, result.Outcome);
        AssertArabicAndPlain(Assert.IsType<string>(result.Message));
        Assert.False(world.Session.IsOpen);

        // The recovery dialog runs the same check straight out of a keystroke handler, so it must
        // not throw there either.
        Assert.NotEqual(RecoveryOutcome.Success, world.Recovery.Verify(RecoveryCode.Generate().Display));
    }

    [Fact]
    public async Task The_lock_overlay_counts_wrong_passwords_and_stops_accepting_them()
    {
        using var world = new FirstRunWorld();
        await ActivateAsync(world);
        world.Session.MarkActivity();
        world.Time.Advance(TimeSpan.FromMinutes(InstallationService.DefaultAutoLockMinutes + 1));
        Assert.True(world.Lock.LockIfIdle());

        for (var attempt = 1; attempt < world.Options.MaxAttempts; attempt++)
        {
            var wrong = await world.Lock.UnlockAsync("ليست كلمة المرور");
            Assert.Equal(SignInOutcome.WrongPassword, wrong.Outcome);
            Assert.Equal(attempt, wrong.Attempt);
        }

        var last = await world.Lock.UnlockAsync("ليست كلمة المرور");
        Assert.Equal(SignInOutcome.LockedOut, last.Outcome);
        AssertArabicAndPlain(Assert.IsType<string>(last.Message));

        // While the lock-out stands even the right password is refused — on this screen exactly as
        // on the sign-in screen.
        var refused = await world.Lock.UnlockAsync(FirstRunWorld.FirstPassword);
        Assert.Equal(SignInOutcome.LockedOut, refused.Outcome);
        Assert.True(world.Session.IsLocked);

        world.Time.Advance(TimeSpan.FromMinutes(world.Options.LockOutMinutes + 1));
        var opened = await world.Lock.UnlockAsync(FirstRunWorld.FirstPassword);
        Assert.True(opened.Succeeded);
        Assert.True(world.Session.IsOpen);
        Assert.Equal(0, world.Login.Profile().FailedAttempts);

        // The guesses that could not be logged while the database was shut are written now.
        await AssertAuditAsync(world, "account.sign_in_failed");
    }

    /// <summary>Runs W02–W04 and hands back the recovery code the sheet showed.</summary>
    private static async Task<string> ActivateAsync(FirstRunWorld world)
    {
        await world.ChooseAsync(world.Export());
        world.Inspection.Inspect(world.PackagePasswordText);
        Assert.True(world.Inspection.IsAcceptable);

        var sheet = world.Activation.Begin();
        await world.Activation.ActivateAsync(FirstRunWorld.FirstPassword);
        return sheet.CodeDisplay;
    }

    private static async Task AssertActivationAuditAsync(FirstRunWorld world)
    {
        var entries = await world.Session.Db.AuditLog.AsNoTracking().ToListAsync();
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            AssertArabicAndPlain(entry.SummaryAr);
        }
    }

    private static async Task AssertAuditAsync(FirstRunWorld world, string action)
    {
        var entry = await world.Session.Db.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Action == action);

        Assert.NotNull(entry);
        Assert.Equal(FirstRunWorld.EmployeeName, entry!.Actor);
        AssertArabicAndPlain(entry.SummaryAr);
    }

    /// <summary>
    /// What the person reads must be an Arabic sentence, not a code and not a borrowed English word
    /// (AGREEMENT item 15). A stray Latin letter anywhere in a message is the failure this catches.
    /// </summary>
    private static void AssertArabicAndPlain(string message)
    {
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.Contains(message, char.IsLetter);
        Assert.DoesNotContain(message, c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');

        foreach (var banned in new[] { "خادم", "منفذ", "إنترنت", "شبكة الإنترنت" })
        {
            Assert.DoesNotContain(banned, message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Sweeps every byte the installation wrote — the database, the key file, the vault, the sealed
    /// sign-in card, the log folder — for the account password and the recovery codes. None of them
    /// may appear anywhere, in any of the three encodings a careless writer could leak them in.
    /// </summary>
    private static void AssertNoSecretsOnDisk(FirstRunWorld world, params string[] codes)
    {
        var secrets = new List<string>(codes)
        {
            FirstRunWorld.FirstPassword,
            FirstRunWorld.SecondPassword,
            world.PackagePasswordText,
        };

        var files = world.AllFiles().ToList();
        Assert.NotEmpty(files);

        foreach (var (path, bytes) in files)
        {
            foreach (var secret in secrets)
            {
                Assert.False(
                    Contains(bytes, Encoding.UTF8.GetBytes(secret)),
                    $"{Path.GetFileName(path)} holds a secret in plain UTF-8.");
                Assert.False(
                    Contains(bytes, Encoding.Unicode.GetBytes(secret)),
                    $"{Path.GetFileName(path)} holds a secret in plain UTF-16.");
                Assert.False(
                    Contains(bytes, Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret)))),
                    $"{Path.GetFileName(path)} holds a secret that was only base64-ed.");
            }
        }

        var log = world.LogText();
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, log, StringComparison.Ordinal);
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length)
        {
            return false;
        }

        return haystack.AsSpan().IndexOf(needle) >= 0;
    }
}
