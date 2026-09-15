using Wakeel.Core.Data;

namespace Wakeel.Core.Tests;

public sealed class WakeelPathsTests
{
    [Fact]
    public void ForRoot_LayoutMatchesArchitectureSection2()
    {
        var root = Path.Combine(Path.GetTempPath(), "wakeel-paths-tests", Guid.NewGuid().ToString("N"));
        var paths = WakeelPaths.ForRoot(root);

        Assert.Equal(root, paths.Root);
        Assert.Equal(Path.Combine(root, "data"), paths.DataDir);
        Assert.Equal(Path.Combine(root, "data", "wakeel.db"), paths.DbPath);
        Assert.Equal(Path.Combine(root, "vault"), paths.VaultDir);
        Assert.Equal(Path.Combine(root, "keys"), paths.KeysDir);
        Assert.Equal(Path.Combine(root, "keys", "installation.key"), paths.InstallationKeyPath);
        Assert.Equal(Path.Combine(root, "models"), paths.ModelsDir);
        Assert.Equal(Path.Combine(root, "packages"), paths.PackagesDir);
        Assert.Equal(Path.Combine(root, "packages", "outbox"), paths.PackagesOutboxDir);
        Assert.Equal(Path.Combine(root, "packages", "inbox"), paths.PackagesInboxDir);
        Assert.Equal(Path.Combine(root, "backups"), paths.BackupsDir);
        Assert.Equal(Path.Combine(root, "logs"), paths.LogsDir);
    }

    [Fact]
    public void EnsureDirectories_CreatesEveryDirectoryInTheLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "wakeel-paths-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = WakeelPaths.ForRoot(root);
            paths.EnsureDirectories();

            Assert.True(Directory.Exists(paths.DataDir));
            Assert.True(Directory.Exists(paths.VaultDir));
            Assert.True(Directory.Exists(paths.KeysDir));
            Assert.True(Directory.Exists(paths.ModelsDir));
            Assert.True(Directory.Exists(paths.PackagesOutboxDir));
            Assert.True(Directory.Exists(paths.PackagesInboxDir));
            Assert.True(Directory.Exists(paths.BackupsDir));
            Assert.True(Directory.Exists(paths.LogsDir));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("ab12cd34ef", "ab", "ab12cd34ef.bin")]
    [InlineData("00ff", "00", "00ff.bin")]
    public void VaultFilePath_ShardsUnderFirstTwoHexCharacters(string sha256Hex, string shard, string fileName)
    {
        var paths = WakeelPaths.ForRoot(Path.Combine(Path.GetTempPath(), "wakeel-paths-tests"));
        var expected = Path.Combine(paths.VaultDir, shard, fileName);
        Assert.Equal(expected, paths.VaultFilePath(sha256Hex));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void VaultFilePath_TooShortHex_Throws(string sha256Hex)
    {
        var paths = WakeelPaths.ForRoot(Path.Combine(Path.GetTempPath(), "wakeel-paths-tests"));
        Assert.Throws<ArgumentException>(() => paths.VaultFilePath(sha256Hex));
    }

    [Fact]
    public void Default_UsesProgramDataWakeelRoot()
    {
        var expectedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Wakeel");
        Assert.Equal(expectedRoot, WakeelPaths.Default().Root);
    }
}
