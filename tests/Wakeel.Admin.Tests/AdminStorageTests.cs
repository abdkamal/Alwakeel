using Microsoft.Data.Sqlite;
using Wakeel.Admin.UI.Data;
using Wakeel.Core.Data;
using Wakeel.Crypto;

namespace Wakeel.Admin.Tests;

/// <summary>
/// <c>admin.db</c> itself: the schema of DATA-MODEL.md §13, and the fact that the file is worth
/// nothing to somebody who has it but not the key.
/// </summary>
public class AdminStorageTests : AdminTestContext
{
    [Fact]
    public void Database_CannotBeOpenedWithoutTheKey()
    {
        CreateAccount();
        Db.Close();

        // The right key opens it; any other one does not get as far as a single row.
        var wrongKey = RandomBytes.Next(32);
        Assert.Throws<SqliteException>(() => DbConnectionFactory.Open(Paths.DatabaseFile, wrongKey));
    }

    [Fact]
    public void Database_IsNotReadableAsPlainTextOnDisk()
    {
        CreateAccount();
        Db.Close();

        // Nothing an ordinary file reader can find in it names the organisation, and it is not even
        // a database file as far as anything that has not been given the key is concerned.
        var bytes = File.ReadAllBytes(Paths.DatabaseFile);
        var asText = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("هيئة تنمية المناطق الريفية", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("SQLite format 3", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("audit_log", asText, StringComparison.Ordinal);
    }

    [Fact]
    public void Schema_CarriesEveryTableOfTheAdminDataModel()
    {
        CreateAccount();

        string[] expected =
        [
            "org", "admin_account", "org_units", "offices", "devices",
            "accounts", "setup_exports", "pending_changes", "audit_log",
        ];

        foreach (var table in expected)
        {
            var found = Db.ScalarText(
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;",
                ("$name", table));
            Assert.Equal(table, found);
        }

        Assert.Equal(1, AdminSchemaMigrator.GetCurrentVersion(Db.Connection));
    }

    [Fact]
    public void Migrate_IsSafeToRunAgainOnAnAlreadyMigratedFile()
    {
        CreateAccount();

        AdminSchemaMigrator.Migrate(Db.Connection);
        AdminSchemaMigrator.Migrate(Db.Connection);

        Assert.Equal(1, AdminSchemaMigrator.GetCurrentVersion(Db.Connection));
        Assert.Equal(1, Db.Scalar("SELECT COUNT(*) FROM schema_versions;"));
    }

    [Fact]
    public void OrgPrivateSeeds_AreSealedInTheRowAndOnlyOpenThroughTheDatabaseKey()
    {
        CreateAccount();

        using var command = Db.Command("SELECT sealed_seeds FROM org LIMIT 1;");
        var sealedSeeds = Assert.IsType<byte[]>(command.ExecuteScalar());

        // What is stored is ciphertext: the public key it certifies is not in it in the clear.
        var org = Keys.ReadOrganisation();
        Assert.NotNull(org);
        var asText = System.Text.Encoding.UTF8.GetString(sealedSeeds);
        Assert.DoesNotContain(org.SigningPublicKeyText, asText, StringComparison.Ordinal);

        // Opened, they are the very keys the root certificate names.
        using var identity = Keys.OpenOrgIdentity();
        Assert.Equal(org.SigningPublicKeyText, identity.SigningPublicKeyText);
        Assert.Equal(org.AgreementPublicKeyText, identity.AgreementPublicKeyText);
    }

    [Fact]
    public void SignOut_ClosesTheDatabaseSoNothingCanBeReadAfterwards()
    {
        CreateAccount();
        Assert.True(Db.IsOpen);

        Accounts.SignOut();

        Assert.False(Db.IsOpen);
        Assert.False(Session.IsSignedIn);
        Assert.Throws<InvalidOperationException>(() => Db.Connection);
    }

    [Fact]
    public void Paths_PutEverythingUnderOneFolderThatDataFolderCanMove()
    {
        var paths = AdminPaths.ForRoot(Path.Combine(Path.GetTempPath(), "wakeel-admin-paths"));

        Assert.Equal(Path.Combine(paths.Root, "admin.db"), paths.DatabaseFile);
        Assert.Equal(Path.Combine(paths.Root, "keys", "admin.key"), paths.KeyFile);
        Assert.Equal(Path.Combine(paths.Root, "exports"), paths.ExportsFolder);
        Assert.Equal(Path.Combine(paths.Root, "staging"), paths.StagingFolder);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogsFolder);
        Assert.EndsWith("WakeelAdmin", AdminPaths.ProgramDataRoot, StringComparison.Ordinal);
    }
}
