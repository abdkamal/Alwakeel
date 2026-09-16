namespace Wakeel.Crypto;

/// <summary>Everything the administration tool hands over to produce one setup file.</summary>
public sealed class SetupWriteRequest
{
    /// <summary>What <c>setup.json</c> will say.</summary>
    public required SetupContent Content { get; init; }

    /// <summary>The sixteen character password shown once on the last step of the export.</summary>
    public required string PackagePassword { get; init; }

    /// <summary>The organisation's own keys. They sign the file and nothing else does.</summary>
    public required DeviceIdentity OrgIdentity { get; init; }

    /// <summary>
    /// The optional entries, each opened only if <see cref="SetupContent.Includes"/> declares
    /// it. They are handed over as factories so a guide of several megabytes is streamed into
    /// the staged payload rather than held in memory alongside it.
    /// </summary>
    public Func<Stream>? Logo { get; init; }

    public Func<Stream>? Guide { get; init; }

    public Func<Stream>? ReportTemplate { get; init; }

    /// <summary>
    /// Argon2id cost for the package password. The default is the product's normal cost; a
    /// weaker one exists only so the test suite does not spend a minute proving a round trip.
    /// </summary>
    public Argon2Params? Kdf { get; init; }

    /// <summary>Where the payload is staged in the clear while the file is built.</summary>
    public string? StagingDirectory { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

/// <summary>
/// Produces a <c>.wakeel-setup</c> file: a password protected <see cref="ContainerKind.Setup"/>
/// container signed by the organisation root. Before it signs anything it runs the very checks
/// the agent will run when it opens the file, so the administration tool cannot hand a person a
/// file that the other machine would then refuse.
/// </summary>
public static class SetupPackageWriter
{
    /// <summary>Writes the file to a path, atomically.</summary>
    public static ContainerManifest Write(string path, SetupWriteRequest request)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(request);

        var sources = Prepare(request, out var producer, out var password);
        return ContainerWriter.Write(path, BuildRequest(request, producer, password, sources));
    }

    /// <summary>Writes the file into an open stream.</summary>
    public static ContainerManifest Write(Stream output, SetupWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(request);

        var sources = Prepare(request, out var producer, out var password);
        return ContainerWriter.Write(output, BuildRequest(request, producer, password, sources));
    }

    /// <summary>
    /// The organisation root certificate a setup file carries. It is derived from the content
    /// rather than from the clock, so exporting the same content twice produces the same
    /// certificate and the same signed bytes for everything except the fresh salt and nonces.
    /// </summary>
    public static DeviceCertificate RootCertificate(SetupContent content, DeviceIdentity orgIdentity)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(orgIdentity);
        return DeviceCertificate.IssueOrgRoot(content.Org.Id, orgIdentity, content.ExportedAt);
    }

    private static ContainerWriteRequest BuildRequest(
        SetupWriteRequest request,
        DeviceCertificate producer,
        string password,
        IReadOnlyList<ContainerEntrySource> sources) =>
        new()
        {
            Kind = ContainerKind.Setup,
            Producer = producer,
            Signer = request.OrgIdentity,
            Key = ContainerKeySource.FromPassword(password, request.Kdf),
            Entries = sources,
            StagingDirectory = request.StagingDirectory,
            Time = request.Time,
        };

    private static IReadOnlyList<ContainerEntrySource> Prepare(
        SetupWriteRequest request,
        out DeviceCertificate producer,
        out string password)
    {
        ArgumentNullException.ThrowIfNull(request.OrgIdentity);

        var content = request.Content
            ?? throw new CryptoException(ErrorCode.Corrupt, "A setup file needs its description.");

        password = PackagePassword.Require(request.PackagePassword);

        if (content.Org is null
            || content.Units is null
            || content.Office is null
            || content.Device is null
            || content.DeviceSeed is null
            || content.Employee is null
            || content.Includes is null)
        {
            throw new CryptoException(ErrorCode.Corrupt, "The setup description is missing one of its parts.");
        }

        if (string.IsNullOrWhiteSpace(content.Org.Id))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The setup description does not name its organisation.");
        }

        // The signing identity and the keys written into the payload have to be the same
        // organisation, or the agent would pin a key whose private half nobody holds.
        if (!string.Equals(content.Org.SigningPub, request.OrgIdentity.SigningPublicKeyText, StringComparison.Ordinal)
            || !string.Equals(content.Org.X25519Pub, request.OrgIdentity.AgreementPublicKeyText, StringComparison.Ordinal))
        {
            throw new CryptoException(ErrorCode.Corrupt, "The organisation keys do not match the description being signed.");
        }

        producer = RootCertificate(content, request.OrgIdentity);

        var sources = new List<ContainerEntrySource>
        {
            ContainerEntrySource.FromBytes(SetupEntryNames.Content, content.ToCanonicalBytes()),
        };

        Add(sources, SetupEntryNames.Logo, content.Includes.Logo, request.Logo);
        Add(sources, SetupEntryNames.Guide, content.Includes.Guide, request.Guide);
        Add(sources, SetupEntryNames.ReportTemplate, content.Includes.ReportTemplate, request.ReportTemplate);

        Verify(content, producer, request, [.. sources.Select(source => source.Name)]);
        return sources;
    }

    private static void Add(
        List<ContainerEntrySource> sources,
        string entryName,
        bool declared,
        Func<Stream>? open)
    {
        if (declared == (open is null))
        {
            throw new CryptoException(
                ErrorCode.Corrupt,
                "The setup description and the files handed over do not agree on what the file carries.");
        }

        if (open is not null)
        {
            sources.Add(ContainerEntrySource.FromStream(entryName, open));
        }
    }

    /// <summary>
    /// Runs the reader's own rules over the content before it is signed. The clock of the
    /// export is the yardstick, so a file dated exactly now is fine and one dated ahead of the
    /// machine that is producing it is caught here instead of at the far end.
    /// </summary>
    private static void Verify(
        SetupContent content,
        DeviceCertificate producer,
        SetupWriteRequest request,
        IReadOnlyCollection<string> entryNames)
    {
        var checks = new SetupCheckList();
        checks.Ok(SetupCheckItem.Signature);
        SetupChecker.Run(
            checks,
            content,
            producer,
            request.OrgIdentity.SigningPublicKey,
            entryNames,
            SetupExpectations.FirstRun,
            request.Time.GetUtcNow());

        checks.Build().EnsureAcceptable();
    }
}
