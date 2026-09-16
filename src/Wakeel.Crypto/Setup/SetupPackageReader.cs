namespace Wakeel.Crypto;

/// <summary>
/// What the machine reading a setup file already knows. On a brand new machine every field is
/// empty and the file is trusted on first sight; from then on the installation passes back
/// what it pinned, and the same reader turns those expectations into refusals.
/// </summary>
public sealed class SetupExpectations
{
    /// <summary>
    /// The organisation signing key this installation pinned when it was activated. When it is
    /// set, a file carrying any other organisation key is refused rather than examined.
    /// </summary>
    public byte[]? PinnedOrgSigningPub { get; init; }

    /// <summary>The device this installation belongs to; a file for another one is refused.</summary>
    public string? InstalledDeviceId { get; init; }

    /// <summary>The export sequence already applied here; anything older is refused.</summary>
    public long? InstalledExportSeq { get; init; }

    /// <summary>How far ahead of this machine's clock a file may be dated.</summary>
    public TimeSpan MaxFutureSkew { get; init; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Where the decrypted payload is staged while entries are read. The host points this at
    /// the product's own protected folder; otherwise the system temporary folder is used.
    /// </summary>
    public string? StagingDirectory { get; init; }

    /// <summary>A machine that has never been activated: nothing is pinned yet.</summary>
    public static SetupExpectations FirstRun { get; } = new();
}

/// <summary>
/// An opened setup file: its content, the verdicts, and the optional entries, which are
/// decrypted only when they are actually asked for.
/// </summary>
public sealed class SetupPackage : IDisposable
{
    private readonly ContainerReader _reader;
    private readonly ContainerKeySource _key;
    private readonly Dictionary<string, byte[]> _read = new(StringComparer.Ordinal);
    private bool _disposed;

    internal SetupPackage(ContainerReader reader, ContainerKeySource key, SetupContent content, SetupCheckResult checks)
    {
        _reader = reader;
        _key = key;
        Content = content;
        Checks = checks;
    }

    /// <summary>Everything <c>setup.json</c> said, already parsed.</summary>
    public SetupContent Content { get; }

    /// <summary>The verdict of every item the first run screen lists.</summary>
    public SetupCheckResult Checks { get; }

    /// <summary>The signed description of the container itself.</summary>
    public ContainerManifest Manifest => _reader.Manifest;

    /// <summary>The organisation root certificate the file was signed with.</summary>
    public DeviceCertificate Producer => _reader.Manifest.Producer;

    /// <summary>Whether this file may be used to activate or update an installation.</summary>
    public bool IsAcceptable => Checks.IsAcceptable;

    /// <summary>Throws the first refusal, if there is one.</summary>
    public void EnsureAcceptable() => Checks.EnsureAcceptable();

    /// <summary>Whether the file carries the named entry at all.</summary>
    public bool Has(string entryName) =>
        _reader.Manifest.Entries.Any(entry => string.Equals(entry.Name, entryName, StringComparison.Ordinal));

    /// <summary>
    /// Reads one entry, checking its recorded size and hash. Nothing is decrypted until it is
    /// asked for — the first run screen shows its verdicts long before anyone wants the guide —
    /// and each entry is decrypted once, because doing it again would repeat the deliberately
    /// expensive Argon2id work on the package password.
    /// </summary>
    public byte[] Read(string entryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(entryName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_read.TryGetValue(entryName, out var content))
        {
            content = _reader.ReadEntry(entryName, _key);
            _read[entryName] = content;
        }

        return content;
    }

    /// <summary>Reads one optional entry, or returns false when the file does not carry it.</summary>
    public bool TryRead(string entryName, out byte[] content)
    {
        if (!Has(entryName))
        {
            content = [];
            return false;
        }

        content = Read(entryName);
        return true;
    }

    /// <summary>The organisation logo, when the file carries one.</summary>
    public byte[]? ReadLogo() => TryRead(SetupEntryNames.Logo, out var bytes) ? bytes : null;

    /// <summary>The user guide, when the file carries one.</summary>
    public byte[]? ReadGuide() => TryRead(SetupEntryNames.Guide, out var bytes) ? bytes : null;

    /// <summary>The monthly report template, when the file carries one.</summary>
    public byte[]? ReadReportTemplate() =>
        TryRead(SetupEntryNames.ReportTemplate, out var bytes) ? bytes : null;

    /// <summary>
    /// Writes every entry of the file into a folder, hashes checked on the way. It writes
    /// <c>setup.json</c> too, and that carries the device seeds and the office key in the
    /// clear, so the folder must be one only this installation can read.
    /// </summary>
    public void ExtractTo(string directory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _reader.ExtractTo(directory, _key);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
    }
}

/// <summary>
/// The outcome of looking at a setup file: the verdicts always, and the opened package when
/// the file got far enough to have one.
/// </summary>
public sealed class SetupInspection : IDisposable
{
    internal SetupInspection(SetupCheckResult checks, SetupPackage? package)
    {
        Checks = checks;
        Package = package;
    }

    /// <summary>
    /// Every item that could be examined, in display order. A file that fails early — a broken
    /// signature, a wrong password — stops the list there instead of inventing the rest.
    /// </summary>
    public SetupCheckResult Checks { get; }

    /// <summary>The opened file, or null when it could not be opened at all.</summary>
    public SetupPackage? Package { get; }

    public bool IsAcceptable => Package is not null && Checks.IsAcceptable;

    /// <summary>
    /// The opened file, or the refusal that stopped it. The screen shows <see cref="Checks"/>;
    /// the code that actually writes an installation goes through here.
    /// </summary>
    public SetupPackage Require()
    {
        if (Package is { } package && Checks.IsAcceptable)
        {
            return package;
        }

        Checks.EnsureAcceptable();
        throw new CryptoException(ErrorCode.Corrupt, "The setup file could not be opened.");
    }

    public void Dispose() => Package?.Dispose();
}

/// <summary>
/// Reads a <c>.wakeel-setup</c> file. Nothing is decrypted before the organisation signature
/// over the file has been verified, and the organisation key is either the one this machine
/// pinned when it was activated or — on a machine that has never been activated — the one the
/// file itself carries, which is then pinned and demanded of every later file.
/// </summary>
public static class SetupPackageReader
{
    /// <summary>Opens a setup file and insists that every check held.</summary>
    public static SetupPackage Open(
        string path,
        string packagePassword,
        SetupExpectations expectations,
        TimeProvider time)
    {
        var inspection = Inspect(path, packagePassword, expectations, time);
        try
        {
            return inspection.Require();
        }
        catch
        {
            inspection.Dispose();
            throw;
        }
    }

    /// <summary>Opens a setup file from an open stream and insists that every check held.</summary>
    public static SetupPackage Open(
        Stream stream,
        string packagePassword,
        SetupExpectations expectations,
        TimeProvider time,
        bool ownsStream = false)
    {
        var inspection = Inspect(stream, packagePassword, expectations, time, ownsStream);
        try
        {
            return inspection.Require();
        }
        catch
        {
            inspection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Examines a setup file without throwing for anything the first run screen is meant to
    /// display. This is what W02 and W03 call: the whole verdict list comes back, and the file
    /// is only acted upon once the person presses the activation button, which goes through
    /// <see cref="SetupInspection.Require"/>.
    /// </summary>
    public static SetupInspection Inspect(
        string path,
        string packagePassword,
        SetupExpectations expectations,
        TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(expectations);
        ArgumentNullException.ThrowIfNull(time);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Inspect(stream, packagePassword, expectations, time, ownsStream: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>Examines a setup file already open as a stream.</summary>
    public static SetupInspection Inspect(
        Stream stream,
        string packagePassword,
        SetupExpectations expectations,
        TimeProvider time,
        bool ownsStream = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(expectations);
        ArgumentNullException.ThrowIfNull(time);

        Validate(expectations);
        var checks = new SetupCheckList();
        var now = time.GetUtcNow();

        // Step one: the container. Its signature is checked against the certificate it carries
        // before a single payload byte is touched. The certificate chain is deliberately not
        // checked here, because the key that would check it is either pinned by this machine or
        // carried by this very file, and step two is where that decision is made in the open.
        ContainerReader reader;
        try
        {
            reader = ContainerReader.Open(
                stream,
                new ContainerOpenOptions
                {
                    ExpectedKind = ContainerKind.Setup,
                    VerifyCertificateChain = false,
                    Time = time,
                    MaxFutureSkew = expectations.MaxFutureSkew,
                    StagingDirectory = expectations.StagingDirectory,
                },
                ownsStream);
        }
        catch (CryptoException exception)
        {
            // With the chain check off, the only thing a date can mean here is that the file
            // itself is dated ahead of this machine's clock.
            if (exception.Code == ErrorCode.Expired)
            {
                checks.Failed(SetupCheckItem.Package, ErrorCode.FutureDate);
            }
            else
            {
                checks.Failed(SetupCheckItem.Signature, exception.Code);
            }

            // The container reader only takes the stream over once it has an archive to hand it
            // to, so on this path closing it is still this method's job.
            if (ownsStream)
            {
                stream.Dispose();
            }

            return new SetupInspection(checks.Build(), package: null);
        }

        try
        {
            return Examine(reader, packagePassword, expectations, checks, now);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>
    /// What this machine says about itself has to make sense before it is used to judge a file:
    /// a pinned key of the wrong length would otherwise fall through to trusting the file's own
    /// key, and an unbounded window would push the date arithmetic past the end of time.
    /// </summary>
    private static void Validate(SetupExpectations expectations)
    {
        if (expectations.PinnedOrgSigningPub is { } pinned && pinned.Length != DeviceIdentity.PublicKeySize)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The pinned organisation key has the wrong length.");
        }

        if (expectations.MaxFutureSkew < TimeSpan.Zero || expectations.MaxFutureSkew > TimeSpan.FromDays(365))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The accepted clock difference is outside the usable range.");
        }
    }

    private static SetupInspection Examine(
        ContainerReader reader,
        string packagePassword,
        SetupExpectations expectations,
        SetupCheckList checks,
        DateTimeOffset now)
    {
        var producer = reader.Manifest.Producer;

        // Step two: the producer must be the organisation root itself, and the root must hold
        // the private half of the key it publishes. The key the chain is checked against is the
        // pinned one where there is one — so a file from another organisation cannot even reach
        // the payload — and otherwise the file's own, which is trust on first sight.
        if (producer.Body.Kind != DeviceKind.Org)
        {
            checks.Failed(SetupCheckItem.Signature, ErrorCode.BadSignature);
            reader.Dispose();
            return new SetupInspection(checks.Build(), package: null);
        }

        var orgSigningKey = expectations.PinnedOrgSigningPub ?? producer.SigningPublicKey;

        if (!CertificateChain.TryVerify(producer, orgSigningKey, revocations: null, now, out var chainError))
        {
            // A root that carries a key other than the pinned one is the one case the interface
            // has to name differently: the file is perfectly well made, for somebody else.
            if (chainError == ErrorCode.Tampered)
            {
                checks.Ok(SetupCheckItem.Signature);
                checks.Failed(SetupCheckItem.Organisation, ErrorCode.Tampered);
            }
            else
            {
                checks.Failed(SetupCheckItem.Signature, chainError);
            }

            reader.Dispose();
            return new SetupInspection(checks.Build(), package: null);
        }

        checks.Ok(SetupCheckItem.Signature);

        // Step three: only now is anything decrypted, and the password is what decides it.
        var key = ContainerKeySource.FromPassword(PackagePassword.Normalize(packagePassword));
        SetupContent content;
        try
        {
            content = SetupContent.Parse(reader.ReadEntry(SetupEntryNames.Content, key));
        }
        catch (CryptoException exception)
        {
            checks.Failed(SetupCheckItem.Package, exception.Code);
            reader.Dispose();
            return new SetupInspection(checks.Build(), package: null);
        }

        var entryNames = reader.Manifest.Entries.Select(entry => entry.Name).ToArray();
        SetupChecker.Run(checks, content, producer, orgSigningKey, entryNames, expectations, now);

        var result = checks.Build();
        return new SetupInspection(result, new SetupPackage(reader, key, content, result));
    }
}
