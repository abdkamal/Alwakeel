using Wakeel.Admin.UI.Data;
using Wakeel.Admin.UI.Services.Account;
using Wakeel.Crypto;

namespace Wakeel.Admin.Tests;

/// <summary>
/// A01 and A02: creating the administrator account with the organisation recovery sheet, signing
/// in, the attempt counter, the doubling temporary lock-out, and recovery by the printed code.
/// </summary>
public class AdminAccountTests : AdminTestContext
{
    [Fact]
    public void Create_MakesTheAccountTheOrganisationAndASheetShownOnce()
    {
        Assert.False(Accounts.IsCreated);

        var sheet = CreateAccount();

        Assert.True(Accounts.IsCreated);
        Assert.True(File.Exists(Paths.KeyFile));
        Assert.True(File.Exists(Paths.DatabaseFile));
        Assert.True(Session.IsSignedIn);
        Assert.Equal("سامي الحاج", Session.AdminName);

        // The sheet carries the twenty-character code in its printed grouping, and the same code as
        // a picture, which is the whole of what «تُعرض مرة واحدة» has to hand over.
        Assert.Equal(RecoveryCode.TotalCharacters + 4, sheet.CodeDisplay.Length);
        Assert.StartsWith("data:image/png;base64,", sheet.QrDataUrl, StringComparison.Ordinal);
        Assert.Equal("هيئة تنمية المناطق الريفية", sheet.OrgName);

        // The organisation now has its keys and its own root certificate, self signed.
        var org = Keys.ReadOrganisation();
        Assert.NotNull(org);
        Assert.Equal("هيئة تنمية المناطق الريفية", org.Name);

        var certificate = Keys.ReadRootCertificate();
        Assert.NotNull(certificate);
        Assert.Equal(org.Id, certificate.Body.DeviceId);
        Assert.Equal(DeviceCertificate.OrgRole, certificate.Body.Role);
        Assert.True(certificate.VerifySignature(Base64Url.Decode(org.SigningPublicKeyText)));
    }

    [Fact]
    public void Create_RefusesASecondOrganisationOnTheSameComputer()
    {
        CreateAccount();

        var again = Accounts.Create(OtherPassword, OtherPassword, "خالد", "هيئة أخرى");

        Assert.Equal(AdminAccountRefusal.AlreadyCreated, again.Refusal);
    }

    [Theory]
    [InlineData("short1!A")]              // under twelve characters
    [InlineData("alllowercaseletters")]   // no case, no digit, no symbol
    public void Create_RefusesAPasswordThatIsTooWeak(string password)
    {
        var result = Accounts.Create(password, password, "سامي", "هيئة");

        Assert.Equal(AdminAccountRefusal.WeakPassword, result.Refusal);
        Assert.False(Accounts.IsCreated);
    }

    [Fact]
    public void Create_RefusesAConfirmationThatDoesNotMatch()
    {
        var result = Accounts.Create(GoodPassword, OtherPassword, "سامي", "هيئة");

        Assert.Equal(AdminAccountRefusal.PasswordMismatch, result.Refusal);
        Assert.False(Accounts.IsCreated);
    }

    [Fact]
    public void SignIn_OpensTheToolWithTheRightPasswordAndRefusesTheWrongOne()
    {
        CreateAccount();
        Accounts.SignOut();
        Assert.False(Session.IsSignedIn);

        var wrong = Accounts.SignIn(OtherPassword);
        Assert.Equal(AdminAccountRefusal.WrongPassword, wrong.Refusal);
        Assert.Equal(Options.MaxAttempts - 1, wrong.AttemptsRemaining);
        Assert.False(Session.IsSignedIn);

        var right = Accounts.SignIn(GoodPassword);
        Assert.True(right.Succeeded);
        Assert.True(Session.IsSignedIn);
        Assert.True(Db.IsOpen);
    }

    [Fact]
    public void SignIn_LocksOutAfterFiveWrongPasswords_AndDoublesEachRound()
    {
        CreateAccount();
        Accounts.SignOut();

        for (var attempt = 1; attempt < Options.MaxAttempts; attempt++)
        {
            Assert.Equal(AdminAccountRefusal.WrongPassword, Accounts.SignIn(OtherPassword).Refusal);
        }

        // The fifth wrong password shuts the door for the first time: thirty seconds.
        var locked = Accounts.SignIn(OtherPassword);
        Assert.Equal(AdminAccountRefusal.LockedOut, locked.Refusal);
        Assert.Equal(AdminOptions.DefaultLockOutSeconds, Accounts.LockedSecondsRemaining);

        // While it runs, even the right password is refused — the wait is the point.
        Assert.Equal(AdminAccountRefusal.LockedOut, Accounts.SignIn(GoodPassword).Refusal);

        Time.Advance(TimeSpan.FromSeconds(AdminOptions.DefaultLockOutSeconds));
        Assert.Equal(0, Accounts.LockedSecondsRemaining);

        // Five more wrong ones, and the next wait is twice as long.
        for (var attempt = 0; attempt < Options.MaxAttempts; attempt++)
        {
            Accounts.SignIn(OtherPassword);
        }

        Assert.Equal(AdminOptions.DefaultLockOutSeconds * 2, Accounts.LockedSecondsRemaining);
    }

    [Fact]
    public void SignIn_ForgetsTheAttemptCounterOnceThePasswordWorks()
    {
        CreateAccount();
        Accounts.SignOut();

        Accounts.SignIn(OtherPassword);
        Accounts.SignIn(OtherPassword);
        Assert.Equal(Options.MaxAttempts - 2, Accounts.AttemptsRemaining);

        Assert.True(Accounts.SignIn(GoodPassword).Succeeded);
        Assert.Equal(Options.MaxAttempts, Accounts.AttemptsRemaining);
    }

    [Fact]
    public void LockOut_SurvivesRestartingTheTool()
    {
        CreateAccount();
        Accounts.SignOut();

        for (var attempt = 0; attempt < Options.MaxAttempts; attempt++)
        {
            Accounts.SignIn(OtherPassword);
        }

        // A second service over the same folder is what a restart looks like from here. The counter
        // and the lock-out live in the key file exactly so that closing the tool does not clear them.
        var restarted = new AdminAccountService(Paths, Db, Keys, Audit, Session, Options, Time);
        Assert.Equal(AdminOptions.DefaultLockOutSeconds, restarted.LockedSecondsRemaining);
        Assert.Equal(AdminAccountRefusal.LockedOut, restarted.SignIn(GoodPassword).Refusal);
    }

    [Fact]
    public void Recover_OpensTheToolWithThePrintedCodeAndSetsANewPassword()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();

        var recovered = Accounts.Recover(sheet.CodeDisplay, OtherPassword, OtherPassword);

        Assert.True(recovered.Succeeded);
        Assert.True(Session.IsSignedIn);

        // The new password works and the old one does not.
        Accounts.SignOut();
        Assert.True(Accounts.SignIn(OtherPassword).Succeeded);
        Accounts.SignOut();
        Assert.Equal(AdminAccountRefusal.WrongPassword, Accounts.SignIn(GoodPassword).Refusal);
    }

    [Fact]
    public void Recover_AcceptsTheCodeWithoutItsDashesAndInLowerCase()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();

        var loose = sheet.CodeDisplay.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

        Assert.True(Accounts.Recover(loose, OtherPassword, OtherPassword).Succeeded);
    }

    [Fact]
    public void Recover_ClearsARunningLockOut()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();

        for (var attempt = 0; attempt < Options.MaxAttempts; attempt++)
        {
            Accounts.SignIn(OtherPassword);
        }

        Assert.True(Accounts.LockedSecondsRemaining > 0);
        Assert.True(Accounts.Recover(sheet.CodeDisplay, OtherPassword, OtherPassword).Succeeded);
        Assert.Equal(0, Accounts.LockedSecondsRemaining);
    }

    [Fact]
    public void Recover_RefusesTextThatIsNotACodeAndACodeThatIsNotThisOrganisation()
    {
        CreateAccount();
        Accounts.SignOut();

        Assert.Equal(
            AdminAccountRefusal.MalformedRecoveryCode,
            Accounts.Recover("ليس رمزًا", OtherPassword, OtherPassword).Refusal);

        var somebodyElses = RecoveryCode.Generate();
        Assert.Equal(
            AdminAccountRefusal.WrongRecoveryCode,
            Accounts.Recover(somebodyElses.Display, OtherPassword, OtherPassword).Refusal);
    }

    [Fact]
    public void Recover_KeepsTheSheetUsableAfterThePasswordChanges()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();

        Assert.True(Accounts.Recover(sheet.CodeDisplay, OtherPassword, OtherPassword).Succeeded);
        Accounts.SignOut();

        // Changing the password re-wraps the same database key; it never re-keys the file, so the
        // printed sheet keeps working — which is what the screen tells the person it does.
        Assert.True(Accounts.Recover(sheet.CodeDisplay, GoodPassword, GoodPassword).Succeeded);
    }

    [Fact]
    public void KeyFile_CarriesTwoWrapsAndNoSecretOfItsOwn()
    {
        var sheet = CreateAccount();

        var keyFile = AdminKeyFile.Load(Paths.KeyFile);
        Assert.NotNull(keyFile.PasswordWrap);
        Assert.NotNull(keyFile.RecoveryWrap);

        // The two names the sign-in screen has to show before any password has worked, and nothing
        // else that is not ciphertext.
        Assert.Equal("سامي الحاج", keyFile.AdminName);
        Assert.Equal("هيئة تنمية المناطق الريفية", keyFile.OrgName);

        // Nothing readable in the file may be, or contain, the password or the printed code.
        var raw = File.ReadAllText(Paths.KeyFile);
        Assert.DoesNotContain(GoodPassword, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(sheet.CodeDisplay, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(
            sheet.CodeDisplay.Replace("-", string.Empty, StringComparison.Ordinal),
            raw,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Create_SucceedsWhenAnInterruptedRunLeftADatabaseWithNoKeyFileBesideIt()
    {
        // Exactly what a crash between «the database is built» and «the key file appears» leaves:
        // a file nobody holds a key to. It must not stop the person from trying again.
        File.WriteAllBytes(Paths.DatabaseFile, RandomBytes.Next(4096));
        Assert.False(Accounts.IsCreated);

        var sheet = CreateAccount();

        Assert.NotEmpty(sheet.CodeDisplay);
        Assert.True(Accounts.IsCreated);

        // The stray file was set aside rather than thrown away.
        Assert.NotEmpty(Directory.GetFiles(Paths.Root, "admin.db.orphaned-*"));
    }

    [Fact]
    public void Create_RefusesInWordsRatherThanThrowingWhenTheFolderCannotBeWrittenTo()
    {
        // A directory where the database file belongs: every write below it fails, and the screen
        // has to hear «تعذّر» rather than an exception it cannot show.
        Directory.CreateDirectory(Paths.DatabaseFile);

        var result = Accounts.Create(GoodPassword, GoodPassword, "سامي الحاج", "هيئة تنمية المناطق الريفية");

        Assert.False(result.Succeeded);
        Assert.Equal(AdminAccountRefusal.StorageFailed, result.Refusal);
        Assert.Null(result.Sheet);
    }

    [Fact]
    public void Sheet_CarriesALabelThatSaysNothingAboutTheCode()
    {
        var sheet = CreateAccount();

        // «20260916/12046» — the day it was issued and a serial, so two sheets can be told apart.
        Assert.Matches(@"^\d{8}/\d{5}$", sheet.SheetNumber);
        Assert.DoesNotContain(
            sheet.CodeDisplay.Replace("-", string.Empty, StringComparison.Ordinal),
            sheet.SheetNumber,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SignIn_SaysTheDataCannotBeReadWhenTheStoredWrapIsNotOneThisBuildUnderstands()
    {
        CreateAccount();
        Accounts.SignOut();
        SpoilStoredWrap(KeyWrapKind.Password);

        // A wrap this build cannot make sense of is not a wrong password: it costs nobody an
        // attempt, and above all it has to come back as an answer rather than as an exception out
        // of the «دخول» button, which would leave A02 dead on screen instead of saying anything.
        var result = Accounts.SignIn(GoodPassword);

        Assert.Equal(AdminAccountRefusal.StorageFailed, result.Refusal);
        Assert.False(Session.IsSignedIn);
    }

    [Fact]
    public void Recover_SaysTheDataCannotBeReadWhenTheStoredWrapIsNotOneThisBuildUnderstands()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();
        SpoilStoredWrap(KeyWrapKind.Recovery);

        var result = Accounts.Recover(sheet.CodeDisplay, OtherPassword, OtherPassword);

        Assert.Equal(AdminAccountRefusal.StorageFailed, result.Refusal);
        Assert.False(Session.IsSignedIn);
    }

    [Fact]
    public void SignIn_SaysTheDataCannotBeReadRatherThanOfferingToCreateASecondAccount()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();
        File.WriteAllText(Paths.KeyFile, "{ not a key file at all");

        // The account file is there, it simply cannot be read. Calling that «no account yet» would
        // send A02 off to the first-run screen, which sees the file, sends the person straight back
        // here, and leaves them watching the screen flicker instead of reading the sentence written
        // for exactly this.
        Assert.True(Accounts.IsCreated);
        Assert.True(Accounts.KeyFileUnreadable);
        Assert.Equal(AdminAccountRefusal.StorageFailed, Accounts.SignIn(GoodPassword).Refusal);
        Assert.Equal(
            AdminAccountRefusal.StorageFailed,
            Accounts.Recover(sheet.CodeDisplay, OtherPassword, OtherPassword).Refusal);
    }

    [Fact]
    public void Recover_LeavesTheOldPasswordWorkingWhenTheToolCannotBeOpened()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();

        AdminSignInResult result;
        using (File.Open(Paths.DatabaseFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Something else on this computer is holding the tool's data open. Writing the new
            // password down anyway would leave the person with neither password working and a
            // screen that only said the data could not be read.
            result = Accounts.Recover(sheet.CodeDisplay, OtherPassword, OtherPassword);
        }

        Assert.Equal(AdminAccountRefusal.StorageFailed, result.Refusal);
        Assert.Equal(AdminAccountRefusal.None, Accounts.SignIn(GoodPassword).Refusal);
    }

    [Fact]
    public void Recover_LeavesNothingOnDiskThatTheOldPasswordStillOpens()
    {
        var sheet = CreateAccount();
        Accounts.SignOut();

        // A wrong password writes the counter back, and that save keeps the previous contents as a
        // backup — so at this point a second file on disk holds the old password's wrap.
        Assert.Equal(AdminAccountRefusal.WrongPassword, Accounts.SignIn(OtherPassword).Refusal);
        Assert.True(File.Exists(Paths.KeyFile + ".bak"));

        Assert.True(Accounts.Recover(sheet.CodeDisplay, OtherPassword, OtherPassword).Succeeded);

        // The password being replaced here is usually one that was forgotten or seen by somebody
        // else. Once it is replaced, nothing anywhere in the tool's key folder may still open the
        // organisation with it — not the key file, and not a backup beside it.
        Assert.False(File.Exists(Paths.KeyFile + ".bak"));

        var passwordWrapsChecked = 0;
        foreach (var path in Directory.GetFiles(Paths.KeysFolder, "*", SearchOption.AllDirectories))
        {
            AdminKeyFile file;
            try
            {
                file = AdminKeyFile.Load(path);
            }
            catch (CryptoException)
            {
                // Not a key file at all, so it opens nothing.
                continue;
            }

            foreach (var wrap in file.DbKeyWraps.Where(candidate => candidate.Kind == KeyWrapKind.Password))
            {
                passwordWrapsChecked++;
                Assert.Throws<CryptoException>(() =>
                    KeyWraps.OpenWithPassword(wrap, GoodPassword, AdminKeyFile.DbKeyContext));
                Assert.NotEmpty(KeyWraps.OpenWithPassword(wrap, OtherPassword, AdminKeyFile.DbKeyContext));
            }
        }

        Assert.Equal(1, passwordWrapsChecked);
    }

    [Fact]
    public void Create_LeavesNoBackupBesideTheBrandNewKeyFile()
    {
        CreateAccount();

        Assert.True(File.Exists(Paths.KeyFile));
        Assert.False(File.Exists(Paths.KeyFile + ".bak"));
    }

    /// <summary>
    /// Rewrites one of the two stored wraps as something this build refuses to open — the state a
    /// half-written save, a hand edit, or a newer build would leave behind.
    /// </summary>
    private void SpoilStoredWrap(KeyWrapKind kind)
    {
        var file = AdminKeyFile.Load(Paths.KeyFile);
        var wrap = file.DbKeyWraps.Find(candidate => candidate.Kind == kind);
        Assert.NotNull(wrap);

        file.SetDbKeyWrap(wrap with { Version = KeyWrap.CurrentVersion + 1 });
        file.Save(Paths.KeyFile, Time);
    }
}
