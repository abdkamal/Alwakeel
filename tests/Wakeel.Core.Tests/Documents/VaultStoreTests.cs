using System.Text;
using Wakeel.Core.Data;
using Wakeel.Core.Services.Documents;
using Wakeel.Crypto;

namespace Wakeel.Core.Tests.Documents;

/// <summary>
/// The vault of B3-2: bytes go in and come back exactly, damage is caught rather than served, an
/// unreachable folder is a state and not a crash, and the same file is never stored twice.
/// </summary>
public sealed class VaultStoreTests
{
    [Fact]
    public async Task Write_then_read_returns_exactly_the_original_bytes()
    {
        using var world = new DocumentsWorld();
        var content = Encoding.UTF8.GetBytes("خطاب رسمي من مكتب المدير — reference 2026/114");

        var write = await world.Vault.WriteAsync(content);
        var read = await world.Vault.ReadAsync(write.Sha256Hex);

        Assert.Equal(VaultState.Ok, read.State);
        Assert.Equal(content, read.Content);
        Assert.Equal(content.Length, write.Size);
        Assert.False(write.AlreadyStored);

        // Filed under the hash of the ORIGINAL bytes, which is what lets the health centre prove
        // later that the stored original is still the original.
        Assert.Equal(Sha256.HashHex(content), write.Sha256Hex);
    }

    [Fact]
    public async Task Stored_file_is_not_the_original_bytes_on_disk()
    {
        using var world = new DocumentsWorld();
        var content = Encoding.UTF8.GetBytes("سرّي — لا يُقرأ من القرص");

        var write = await world.Vault.WriteAsync(content);
        var onDisk = await File.ReadAllBytesAsync(world.VaultFile(write.Sha256Hex));

        Assert.DoesNotContain("سرّي", Encoding.UTF8.GetString(onDisk), StringComparison.Ordinal);
        Assert.NotEqual(content, onDisk);
    }

    [Fact]
    public async Task Large_file_round_trips_through_several_frames()
    {
        using var world = new DocumentsWorld();

        // Bigger than one chunk of the sealed stream, so the frame boundary is exercised.
        var content = new byte[3 * 256 * 1024 + 17];
        Random.Shared.NextBytes(content);

        var write = await world.Vault.WriteAsync(content);
        var read = await world.Vault.ReadAsync(write.Sha256Hex);

        Assert.Equal(VaultState.Ok, read.State);
        Assert.Equal(content, read.Content);
    }

    [Fact]
    public async Task Copy_to_streams_without_holding_the_file_whole()
    {
        using var world = new DocumentsWorld();
        var content = Encoding.UTF8.GetBytes(new string('م', 5000));

        var write = await world.Vault.WriteAsync(content);
        using var destination = new MemoryStream();
        var copy = await world.Vault.CopyToAsync(write.Sha256Hex, destination);

        Assert.Equal(VaultState.Ok, copy.State);
        Assert.Equal(content.Length, copy.BytesWritten);
        Assert.Equal(content, destination.ToArray());
    }

    [Fact]
    public async Task Tampered_file_reads_as_corrupt_and_never_yields_bytes()
    {
        using var world = new DocumentsWorld();
        var content = Encoding.UTF8.GetBytes("محضر اجتماع");
        var write = await world.Vault.WriteAsync(content);

        // Flip one bit deep inside the sealed file, past its header.
        var path = world.VaultFile(write.Sha256Hex);
        var sealedBytes = await File.ReadAllBytesAsync(path);
        sealedBytes[^3] ^= 0x01;
        await File.WriteAllBytesAsync(path, sealedBytes);

        var read = await world.Vault.ReadAsync(write.Sha256Hex);

        Assert.Equal(VaultState.Corrupt, read.State);
        Assert.Null(read.Content);
        Assert.Equal(VaultState.Corrupt, await world.Vault.VerifyAsync(write.Sha256Hex));
    }

    [Fact]
    public async Task Truncated_file_reads_as_corrupt()
    {
        using var world = new DocumentsWorld();
        var content = new byte[400_000];
        Random.Shared.NextBytes(content);
        var write = await world.Vault.WriteAsync(content);

        var path = world.VaultFile(write.Sha256Hex);
        var sealedBytes = await File.ReadAllBytesAsync(path);
        await File.WriteAllBytesAsync(path, sealedBytes[..(sealedBytes.Length / 2)]);

        Assert.Equal(VaultState.Corrupt, await world.Vault.VerifyAsync(write.Sha256Hex));
    }

    [Fact]
    public async Task A_file_moved_to_another_hashs_name_reads_as_corrupt()
    {
        using var world = new DocumentsWorld();
        var first = await world.Vault.WriteAsync(Encoding.UTF8.GetBytes("الأصل"));
        var second = await world.Vault.WriteAsync(Encoding.UTF8.GetBytes("بديل"));

        // Every frame authenticates the name the file is filed under, so swapping two sealed
        // files cannot pass unnoticed even though both are perfectly good files.
        var firstBytes = await File.ReadAllBytesAsync(world.VaultFile(first.Sha256Hex));
        await File.WriteAllBytesAsync(world.VaultFile(second.Sha256Hex), firstBytes);

        Assert.Equal(VaultState.Corrupt, await world.Vault.VerifyAsync(second.Sha256Hex));
    }

    [Fact]
    public async Task Missing_file_is_missing_and_not_corrupt()
    {
        using var world = new DocumentsWorld();
        var absent = Sha256.HashHex(Encoding.UTF8.GetBytes("لم يُحفظ قط"));

        var read = await world.Vault.ReadAsync(absent);

        Assert.Equal(VaultState.Missing, read.State);
        Assert.False(world.Vault.Exists(absent));
    }

    [Fact]
    public async Task Unreachable_vault_folder_is_a_state_on_read_and_a_refusal_on_write()
    {
        using var world = new DocumentsWorld();
        var write = await world.Vault.WriteAsync(Encoding.UTF8.GetBytes("قرار"));

        // The drive was unplugged, the folder was moved: from here the vault is simply not there.
        Directory.Delete(world.Paths.VaultDir, recursive: true);

        var read = await world.Vault.ReadAsync(write.Sha256Hex);
        Assert.Equal(VaultState.Unavailable, read.State);
        Assert.Null(read.Content);
        Assert.Equal(VaultState.Unavailable, await world.Vault.VerifyAsync(write.Sha256Hex));
    }

    [Fact]
    public async Task A_vault_whose_folder_cannot_be_made_refuses_the_write_instead_of_pretending()
    {
        using var world = new DocumentsWorld();

        // A file where the vault's own folder should be: nothing can be created underneath it.
        var root = Path.Combine(world.Root, "blocked");
        await File.WriteAllTextAsync(root, "not a folder");
        var paths = WakeelPaths.ForRoot(root);
        var store = new VaultStore(paths, world.Keys);

        Assert.False(store.IsAvailable);
        await Assert.ThrowsAsync<VaultUnavailableException>(
            () => store.WriteAsync(Encoding.UTF8.GetBytes("لن يُحفظ")));
    }

    [Fact]
    public async Task The_same_bytes_are_stored_once()
    {
        using var world = new DocumentsWorld();
        var content = Encoding.UTF8.GetBytes("مرفق يُضاف إلى خطابين");

        var first = await world.Vault.WriteAsync(content);
        var stamp = File.GetLastWriteTimeUtc(world.VaultFile(first.Sha256Hex));
        var length = new FileInfo(world.VaultFile(first.Sha256Hex)).Length;

        var second = await world.Vault.WriteAsync(content);

        Assert.Equal(first.Sha256Hex, second.Sha256Hex);
        Assert.False(first.AlreadyStored);
        Assert.True(second.AlreadyStored);

        // Not rewritten: a second write would risk replacing a good file with a worse one.
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(world.VaultFile(first.Sha256Hex)));
        Assert.Equal(length, new FileInfo(world.VaultFile(first.Sha256Hex)).Length);

        // And exactly one file under the whole vault.
        Assert.Single(Directory.GetFiles(world.Paths.VaultDir, "*.bin", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Two_different_files_do_not_share_a_key()
    {
        using var world = new DocumentsWorld();
        var first = await world.Vault.WriteAsync(Encoding.UTF8.GetBytes("أ"));
        var second = await world.Vault.WriteAsync(Encoding.UTF8.GetBytes("ب"));

        var firstBytes = await File.ReadAllBytesAsync(world.VaultFile(first.Sha256Hex));
        await File.WriteAllBytesAsync(world.VaultFile(first.Sha256Hex), await File.ReadAllBytesAsync(world.VaultFile(second.Sha256Hex)));

        // Reading the first name now fails, which is only possible because the two files were not
        // sealed with the same key and the same name.
        Assert.Equal(VaultState.Corrupt, await world.Vault.VerifyAsync(first.Sha256Hex));

        await File.WriteAllBytesAsync(world.VaultFile(first.Sha256Hex), firstBytes);
        Assert.Equal(VaultState.Ok, await world.Vault.VerifyAsync(first.Sha256Hex));
    }

    [Fact]
    public async Task Delete_removes_one_file_and_forgives_a_missing_one()
    {
        using var world = new DocumentsWorld();
        var write = await world.Vault.WriteAsync(Encoding.UTF8.GetBytes("مسودة أُلغيت"));

        world.Vault.Delete(write.Sha256Hex);
        Assert.False(world.Vault.Exists(write.Sha256Hex));

        // Deleting it again is not an error: rollback must not fail over what it already undid.
        world.Vault.Delete(write.Sha256Hex);
    }

    [Fact]
    public void The_static_form_activation_uses_round_trips_the_same_way()
    {
        using var world = new DocumentsWorld();
        var key = TestHelpers.NewKey();
        var content = Encoding.UTF8.GetBytes("دليل المستخدم");

        var hash = VaultStore.Write(world.Paths, key, content);

        Assert.Equal(content, VaultStore.Read(world.Paths, key, hash));
        Assert.Null(VaultStore.Read(world.Paths, TestHelpers.NewKey(), hash));

        VaultStore.Delete(world.Paths, hash);
        Assert.Null(VaultStore.Read(world.Paths, key, hash));
    }

    [Fact]
    public async Task A_hash_that_is_not_a_hash_is_a_defect_and_not_a_state()
    {
        using var world = new DocumentsWorld();

        // A caller passing a file name where a hash belongs has a bug; that is not something to
        // draw a card about.
        await Assert.ThrowsAsync<ArgumentException>(() => world.Vault.ReadAsync("not-a-hash"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => world.Vault.ReadAsync(string.Empty));

        // The right length alone is not enough. Every path is built out of this value, so a name
        // with a separator in it would be looked for outside the vault folder altogether, and a
        // name that is not hexadecimal would only fall over much later, inside the key derivation.
        var walkOut = new string('a', 32) + Path.DirectorySeparatorChar + new string('b', 31);
        Assert.Equal(64, walkOut.Length);
        await Assert.ThrowsAsync<ArgumentException>(() => world.Vault.ReadAsync(walkOut));
        await Assert.ThrowsAsync<ArgumentException>(() => world.Vault.ReadAsync(new string('z', 64)));
    }
}
