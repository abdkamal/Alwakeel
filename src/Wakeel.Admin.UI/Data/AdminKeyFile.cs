using System.Text.Json.Serialization;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Data;

/// <summary>
/// The contents of <c>keys\admin.key</c>: the wrapped copies of the <c>admin.db</c> key — one that
/// the administrator password opens and one that the printed organisation recovery sheet opens —
/// plus the little the sign-in screen has to know before any password has worked.
/// </summary>
/// <remarks>
/// <para>
/// The attempt counter and the temporary lock-out live here rather than in the database on purpose:
/// they have to be read and written while the password is still wrong, and at that moment the
/// database cannot be opened at all. Nothing in this file is a secret — the wraps are ciphertext,
/// and a counter tells nobody anything — so the file being readable costs nothing, while a counter
/// that could be erased by simply restarting the tool would make the lock-out theatre.
/// </para>
/// <para>
/// Same shape and the same save discipline as <c>InstallationKeyFile</c> on the الوكيل side (write
/// to a temporary file, then move it into place), so an interrupted write can never leave the
/// organisation without its key. The previous contents are kept beside it as <c>.bak</c> only while
/// the wraps are unchanged — see <see cref="Save(string, TimeProvider, bool)"/>.
/// </para>
/// </remarks>
public sealed class AdminKeyFile
{
    /// <summary>The only format version this build writes or reads.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Associated-data label of every wrap of the admin database key.</summary>
    public const string DbKeyContext = "wakeel.admin.dbkey";

    /// <summary>Format version of the file on disk.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>When the administrator account was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When anything in this file last changed.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The name the sign-in screen greets, which is not a secret.</summary>
    public string AdminName { get; set; } = string.Empty;

    /// <summary>
    /// The organisation's name, kept here as well as in the database so the sign-in screen can put
    /// it above the password field — at that moment the database has not been opened yet. It is
    /// printed on every setup file and letter the organisation sends out, so it is no secret.
    /// </summary>
    public string OrgName { get; set; } = string.Empty;

    /// <summary>Every wrapped copy of the database key, one per way of opening it.</summary>
    public List<KeyWrap> DbKeyWraps { get; set; } = [];

    /// <summary>Wrong passwords in a row since the last one that worked.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>How many temporary lock-outs have already been served; each one lasts twice as long.</summary>
    public int LockOutRound { get; set; }

    /// <summary>When the current temporary lock-out ends, or null when there is none.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// Temporary lock-outs that have been served but not yet written into the operations log. A
    /// lock-out happens while the password is still wrong, so the database is shut and nothing can
    /// be recorded at that moment; the count waits here until a password or a recovery code opens
    /// the database, and the rows are written then. Nothing here is a secret — it says how many
    /// times the door was shut, not by whom or with what.
    /// </summary>
    public int LockOutsToReport { get; set; }

    /// <summary>Whether the recovery sheet has ever been replaced, which A02 mentions on recovery.</summary>
    public DateTimeOffset? RecoveryIssuedAt { get; set; }

    /// <summary>The password wrap, when one has been written.</summary>
    [JsonIgnore]
    public KeyWrap? PasswordWrap => DbKeyWraps.Find(wrap => wrap.Kind == KeyWrapKind.Password);

    /// <summary>The recovery-sheet wrap, when one has been written.</summary>
    [JsonIgnore]
    public KeyWrap? RecoveryWrap => DbKeyWraps.Find(wrap => wrap.Kind == KeyWrapKind.Recovery);

    /// <summary>A fresh file for an account that is about to be created.</summary>
    public static AdminKeyFile Create(TimeProvider? timeProvider = null)
    {
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        return new AdminKeyFile { CreatedAt = now, UpdatedAt = now };
    }

    /// <summary>Whether an administrator account already exists on this machine.</summary>
    public static bool Exists(string path) => File.Exists(path);

    /// <summary>Reads the file, refusing a format this build does not understand.</summary>
    public static AdminKeyFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The administration key file is missing.");
        }

        var file = CanonicalJson.Deserialize<AdminKeyFile>(File.ReadAllBytes(path));
        if (file.Version != CurrentVersion)
        {
            throw new CryptoException(ErrorCode.UnknownKind, "The administration key file uses an unsupported format version.");
        }

        return file;
    }

    /// <summary>
    /// Writes the file through a temporary copy, so an interrupted write can never leave the
    /// organisation without its key.
    /// </summary>
    /// <param name="path">Where <c>admin.key</c> lives.</param>
    /// <param name="timeProvider">The clock that stamps <see cref="UpdatedAt"/>.</param>
    /// <param name="keepBackup">
    /// Whether the previous contents may be left beside the new file as <c>.bak</c>. True for a save
    /// that only moves a counter, where a backup is pure insurance. It MUST be false whenever the
    /// wraps themselves changed: a backup then holds a superseded way of opening the very same
    /// database key, so a password that was just replaced — usually because it was forgotten or
    /// seen by somebody — would keep opening the organisation from the old file forever. A save
    /// that does not keep a backup also removes any backup an earlier save left behind, so no
    /// retired wrap survives anywhere on disk.
    /// </param>
    public void Save(string path, TimeProvider? timeProvider = null, bool keepBackup = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow();

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = CanonicalJson.SerializeToUtf8Bytes(this);
        var temporary = path + ".tmp";
        var backup = path + ".bak";

        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (keepBackup && File.Exists(path))
        {
            File.Copy(path, backup, overwrite: true);
        }

        File.Move(temporary, path, overwrite: true);

        if (!keepBackup)
        {
            OverwriteThenDeleteBackup(backup, bytes);
        }
    }

    /// <summary>
    /// Makes a backup left by an earlier save harmless and then removes it. It is done after the new
    /// file is in place: losing the backup is nothing, while losing the key file would be the
    /// organisation.
    /// </summary>
    /// <remarks>
    /// The delete is best effort — a backup held open for a moment by a scanner or a backup agent
    /// cannot be removed — so the retired contents are overwritten with the current ones first. A
    /// backup that survives then carries the wraps in force rather than a password that was just
    /// replaced, and the whole security promise no longer rests on a delete that may fail silently.
    /// </remarks>
    private static void OverwriteThenDeleteBackup(string backup, byte[] current)
    {
        try
        {
            if (File.Exists(backup))
            {
                File.WriteAllBytes(backup, current);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done here; the delete below is tried all the same.
        }

        try
        {
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file another program is holding open cannot be removed right now. The next save of
            // a changed wrap set tries again, and the caller checks for it too.
        }
    }

    /// <summary>Replaces the wrap of one kind, keeping the others.</summary>
    public void SetDbKeyWrap(KeyWrap wrap)
    {
        ArgumentNullException.ThrowIfNull(wrap);
        DbKeyWraps.RemoveAll(existing => existing.Kind == wrap.Kind);
        DbKeyWraps.Add(wrap);
        DbKeyWraps.Sort((left, right) => left.Kind.CompareTo(right.Kind));
    }
}
