using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Wakeel.Core.Data;
using Wakeel.Crypto;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// The little that the sign-in screen has to know before anything is unlocked: whose account this
/// is, which office it belongs to, the organisation logo it shows, and the attempt counter that
/// survives a restart.
/// </summary>
/// <remarks>
/// None of this can live in the database, because W05 draws it while the database is still locked,
/// and none of it may lie around in the clear either — the name of an office and the person working
/// in it is not a secret like a key, but it is nobody's business on a shared machine. So the whole
/// card is sealed with the platform's own machine-bound protection, the same one behind the machine
/// wrap, and a machine that can no longer open it simply shows the product's own branding and asks
/// for the password anyway.
/// </remarks>
public sealed class SignInProfile
{
    /// <summary>Format version of the sealed card.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    public string OrgName { get; set; } = string.Empty;

    public string OfficeName { get; set; } = string.Empty;

    public string OfficeCode { get; set; } = string.Empty;

    public string EmployeeName { get; set; } = string.Empty;

    public string JobTitle { get; set; } = string.Empty;

    public int EmployeeNo { get; set; }

    public int DeviceNo { get; set; }

    /// <summary>The organisation logo exactly as the setup file carried it, or null when it carried none.</summary>
    public byte[]? Logo { get; set; }

    /// <summary>Consecutive wrong passwords since the last successful sign in.</summary>
    public int FailedAttempts { get; set; }

    /// <summary>While this is in the future the sign-in screen refuses to try at all.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    /// <summary>
    /// How many temporary lock-outs have already been served without a successful sign in between
    /// them. Each one doubles the wait, so somebody guessing at the keyboard is stopped for longer
    /// every round, while the person who simply mistyped once starts from zero again.
    /// </summary>
    public int LockRounds { get; set; }

    /// <summary>
    /// Wrong passwords that have not yet been written to the audit log, because the audit log lives
    /// inside the database and a wrong password never opens it. They are written as one entry the
    /// next time the database does open.
    /// </summary>
    public int UnloggedFailures { get; set; }

    /// <summary>When the recovery sheet currently in the person's hands was issued.</summary>
    public DateTimeOffset? RecoveryIssuedAt { get; set; }

    /// <summary>When somebody last signed in successfully.</summary>
    public DateTimeOffset? LastSignInAt { get; set; }

    /// <summary>The organisation logo as a data URL, for the sign-in and lock screens.</summary>
    [JsonIgnore]
    public string? LogoDataUrl =>
        Logo is { Length: > 0 } bytes ? "data:image/png;base64," + Convert.ToBase64String(bytes) : null;

    /// <summary>Whether this card carries enough to greet the person by name.</summary>
    [JsonIgnore]
    public bool HasIdentity => !string.IsNullOrWhiteSpace(EmployeeName);

    /// <summary>
    /// Set when this card was not on the machine — or could no longer be opened — and a blank one was
    /// handed out in its place. On a machine that has already been activated that is not an ordinary
    /// state: the counter it carried has just started again at zero, which is precisely what somebody
    /// working their way through passwords would want, so the wait such a machine serves does not
    /// start from the shortest one.
    /// </summary>
    [JsonIgnore]
    public bool Missing { get; set; }
}

/// <summary>Reads and writes the sealed sign-in card next to the installation's keys.</summary>
public sealed class SignInProfileStore
{
    private const string EntropyLabel = "wakeel.signin-profile.v1";
    private const string FileName = "session.dat";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private readonly WakeelPaths _paths;
    private readonly IPlatformProtector _protector;

    public SignInProfileStore(WakeelPaths paths, IPlatformProtector protector)
    {
        _paths = paths;
        _protector = protector;
    }

    /// <summary>Where the card lives.</summary>
    public string Path => System.IO.Path.Combine(_paths.KeysDir, FileName);

    /// <summary>The card, or an empty one when this machine has none or can no longer open it.</summary>
    public SignInProfile Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new SignInProfile { Missing = true };
            }

            var plain = _protector.Unprotect(File.ReadAllBytes(Path), Entropy);
            var profile = JsonSerializer.Deserialize<SignInProfile>(plain, Json);
            return profile is { Version: SignInProfile.CurrentVersion }
                ? profile
                : new SignInProfile { Missing = true };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or JsonException or CryptoException)
        {
            // A card this machine cannot open is not a failure: the sign-in screen falls back to the
            // product's own branding and the password still decides everything.
            return new SignInProfile { Missing = true };
        }
    }

    /// <summary>Seals the card and writes it, replacing whatever was there.</summary>
    public void Save(SignInProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Version = SignInProfile.CurrentVersion;

        Directory.CreateDirectory(_paths.KeysDir);
        var sealedBytes = _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(profile, Json), Entropy);

        var temporary = Path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(sealedBytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, Path, overwrite: true);
    }

    /// <summary>Loads the card, lets the caller change it, and writes it back.</summary>
    public SignInProfile Update(Action<SignInProfile> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var profile = Load();
        change(profile);
        Save(profile);
        return profile;
    }

    private static byte[] Entropy => Encoding.UTF8.GetBytes(EntropyLabel);
}
