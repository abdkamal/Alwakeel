using System.Security.Cryptography;
using System.Globalization;
using Wakeel.Admin.UI.Data;
using Wakeel.Crypto;

namespace Wakeel.Admin.UI.Services;

/// <summary>The organisation as the tool knows it, without any of its private material.</summary>
/// <param name="Id">The organisation identifier, which every certificate and setup file names.</param>
/// <param name="Name">The full official name.</param>
/// <param name="SigningPublicKeyText">The organisation Ed25519 public key, base64url.</param>
/// <param name="AgreementPublicKeyText">The organisation X25519 public key, base64url.</param>
/// <param name="CycleStartDay">The day of the month the financial cycle starts on.</param>
/// <param name="NumberingFormat">The official-number format in force.</param>
/// <param name="CreatedAt">When the organisation was created in this tool.</param>
public sealed record AdminOrgInfo(
    string Id,
    string Name,
    string SigningPublicKeyText,
    string AgreementPublicKeyText,
    int CycleStartDay,
    string NumberingFormat,
    DateTimeOffset CreatedAt);

/// <summary>
/// The organisation's own keys: the Ed25519 pair that signs every setup file, revocation list and
/// certificate, and the X25519 pair that opens the administration wrap of an installation.
/// </summary>
/// <remarks>
/// <para>
/// They are generated once, at A01, and they never leave this tool. The private seeds are sealed
/// with the <c>admin.db</c> key before they are written to the <c>org</c> row, so the only way to
/// them is through the administrator password or the printed organisation recovery sheet — the two
/// things that open that key.
/// </para>
/// <para>
/// Alongside them the organisation root certificate is issued and stored (ARCHITECTURE.md §3): a
/// self-signed certificate whose device identifier is the organisation identifier. It exists so
/// that the one thing the organisation signs directly — the setup file — travels through the same
/// certificate and manifest model as everything else, and so a computer that has already pinned the
/// organisation key can tell at a glance whether a file is from the same organisation.
/// </para>
/// </remarks>
public sealed class AdminKeyService
{
    /// <summary>The default official-number format (AGREEMENT item 5).</summary>
    public const string DefaultNumberingFormat = "YYYYMMDD/DESSS";

    private readonly AdminDb _db;
    private readonly TimeProvider _time;

    public AdminKeyService(AdminDb db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    /// <summary>Whether the organisation row has been created yet.</summary>
    public bool OrgExists => _db.IsOpen && _db.Scalar("SELECT COUNT(*) FROM org;") > 0;

    /// <summary>
    /// Generates the organisation keys, issues the organisation root certificate, and writes the
    /// single <c>org</c> row. Called once, from A01, inside the same open database the account was
    /// just created for.
    /// </summary>
    /// <param name="orgName">The full official name of the organisation.</param>
    /// <returns>What the rest of the tool needs to know about the organisation.</returns>
    public AdminOrgInfo CreateOrganisation(string orgName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orgName);

        if (OrgExists)
        {
            throw new InvalidOperationException("This tool already carries an organisation.");
        }

        var now = _time.GetUtcNow();
        var orgId = Guid.CreateVersion7().ToString();

        using var identity = DeviceIdentity.Generate();
        var certificate = DeviceCertificate.IssueOrgRoot(orgId, identity, now);

        // The private seeds are sealed before they touch the file, and the plaintext copy is dropped
        // as soon as the row is written.
        var seeds = identity.Export();
        var plainSeeds = CanonicalJson.SerializeToUtf8Bytes(seeds);
        byte[] sealedSeeds;
        try
        {
            sealedSeeds = _db.Seal(plainSeeds);
        }
        finally
        {
            Array.Clear(plainSeeds);

            // Export() handed back the organisation's own two private seeds — the most valuable
            // secret this tool ever holds. Disposing the identity wipes its copy, not this one, so
            // the exported arrays are wiped here in the same breath as the serialized bytes.
            CryptographicOperations.ZeroMemory(seeds.SigningSeed);
            CryptographicOperations.ZeroMemory(seeds.AgreementSeed);
        }

        _db.Execute(
            """
            INSERT INTO org(
                id, name, cycle_start_day, numbering_format,
                ed25519_pub, x25519_pub, sealed_seeds, root_certificate, created_at, updated_at)
            VALUES ($id, $name, 1, $format, $ed, $x, $seeds, $certificate, $at, $at);
            """,
            ("$id", orgId),
            ("$name", orgName.Trim()),
            ("$format", DefaultNumberingFormat),
            ("$ed", identity.SigningPublicKeyText),
            ("$x", identity.AgreementPublicKeyText),
            ("$seeds", sealedSeeds),
            ("$certificate", CanonicalJson.Serialize(certificate)),
            ("$at", now.ToString("O", CultureInfo.InvariantCulture)));

        return new AdminOrgInfo(
            orgId,
            orgName.Trim(),
            identity.SigningPublicKeyText,
            identity.AgreementPublicKeyText,
            1,
            DefaultNumberingFormat,
            now);
    }

    /// <summary>The organisation as it stands, or null before A01 has run.</summary>
    public AdminOrgInfo? ReadOrganisation()
    {
        if (!_db.IsOpen)
        {
            return null;
        }

        using var command = _db.Command(
            "SELECT id, name, ed25519_pub, x25519_pub, cycle_start_day, numbering_format, created_at FROM org LIMIT 1;");
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new AdminOrgInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetString(5),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    /// <summary>
    /// The organisation root certificate, as it was issued. Everything that has to verify a setup
    /// file or a device certificate starts here.
    /// </summary>
    public DeviceCertificate? ReadRootCertificate()
    {
        var text = _db.IsOpen ? _db.ScalarText("SELECT root_certificate FROM org LIMIT 1;") : null;
        return text is null ? null : CanonicalJson.Deserialize<DeviceCertificate>(text);
    }

    /// <summary>
    /// Opens the organisation keys for one piece of work. The caller must dispose the identity as
    /// soon as it is done with it, which wipes the private material from memory; nothing in this
    /// tool keeps the organisation keys open between actions.
    /// </summary>
    /// <exception cref="InvalidOperationException">No organisation has been created yet.</exception>
    public DeviceIdentity OpenOrgIdentity()
    {
        if (!_db.IsOpen)
        {
            throw new InvalidOperationException("The administration database is not open.");
        }

        using var command = _db.Command("SELECT sealed_seeds FROM org LIMIT 1;");
        if (command.ExecuteScalar() is not byte[] sealedSeeds)
        {
            throw new InvalidOperationException("This tool does not carry an organisation yet.");
        }

        var plain = _db.Unseal(sealedSeeds);
        try
        {
            var seeds = CanonicalJson.Deserialize<DeviceSeeds>(plain);
            return DeviceIdentity.Import(seeds);
        }
        finally
        {
            Array.Clear(plain);
        }
    }
}
