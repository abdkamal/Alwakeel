namespace Wakeel.Crypto;

/// <summary>
/// Turns a parsed <see cref="SetupContent"/> into the list of verdicts the first run screen
/// shows. Every rule lives here exactly once, so the administration tool cannot write a file
/// that the agent would then refuse: the writer runs the same checks before it signs.
/// </summary>
internal static class SetupChecker
{
    /// <summary>
    /// Examines everything the file says about itself. The signature item is not decided here;
    /// it belongs to whoever verified the container and is recorded by the caller.
    /// </summary>
    internal static void Run(
        SetupCheckList checks,
        SetupContent content,
        DeviceCertificate producer,
        ReadOnlySpan<byte> orgSigningKey,
        IReadOnlyCollection<string> entryNames,
        SetupExpectations expectations,
        DateTimeOffset now)
    {
        var skew = expectations.MaxFutureSkew;

        checks.Record(SetupCheckItem.Package, CheckPackage(content, entryNames, expectations, now, skew));
        checks.Record(SetupCheckItem.Organisation, CheckOrganisation(content, producer, orgSigningKey));
        checks.Record(SetupCheckItem.Office, CheckOffice(content));

        // The revocation list is judged before the device, because it is the device check that
        // consults it: a list that is not properly signed must be ignored there rather than
        // becoming a reason to reject a device it never mentioned.
        var revocationError = CheckRevocation(content, orgSigningKey, now, skew);
        var revocations = revocationError is null ? content.Revocation : null;

        checks.Record(SetupCheckItem.Device, CheckDevice(content, orgSigningKey, revocations, expectations, now));
        checks.Record(SetupCheckItem.Employee, CheckEmployee(content));
        checks.Record(SetupCheckItem.OfficeKey, content.TryGetOfficeKey(out _) ? null : ErrorCode.Corrupt);

        Optional(checks, SetupCheckItem.Logo, content.Includes.Logo, entryNames, SetupEntryNames.Logo);
        Optional(checks, SetupCheckItem.Guide, content.Includes.Guide, entryNames, SetupEntryNames.Guide);
        Optional(
            checks,
            SetupCheckItem.ReportTemplate,
            content.Includes.ReportTemplate,
            entryNames,
            SetupEntryNames.ReportTemplate);

        if (content.Revocation is null)
        {
            checks.Absent(SetupCheckItem.Revocation);
        }
        else
        {
            checks.Record(SetupCheckItem.Revocation, revocationError);
        }
    }

    private static ErrorCode? CheckPackage(
        SetupContent content,
        IReadOnlyCollection<string> entryNames,
        SetupExpectations expectations,
        DateTimeOffset now,
        TimeSpan skew)
    {
        if (content.FormatVersion != SetupContent.CurrentFormatVersion)
        {
            return ErrorCode.UnknownKind;
        }

        if (content.ExportSeq < 1)
        {
            return ErrorCode.Corrupt;
        }

        // A setup file carries these four names and nothing else. Anything extra would be
        // written into the installation folder by a host that extracts the file wholesale,
        // and on a first run there is no pinned key yet to say who put it there.
        foreach (var name in entryNames)
        {
            if (name is not (SetupEntryNames.Content
                or SetupEntryNames.Logo
                or SetupEntryNames.Guide
                or SetupEntryNames.ReportTemplate))
            {
                return ErrorCode.Tampered;
            }
        }

        if (content.ExportedAt > now + skew)
        {
            return ErrorCode.FutureDate;
        }

        // An installation refuses to go backwards: a file the administrator exported before the
        // one already applied here would undo a structure change or reinstate a revoked device.
        if (expectations.InstalledExportSeq is { } installed && content.ExportSeq < installed)
        {
            return ErrorCode.Older;
        }

        return null;
    }

    private static ErrorCode? CheckOrganisation(
        SetupContent content,
        DeviceCertificate producer,
        ReadOnlySpan<byte> orgSigningKey)
    {
        var org = content.Org;
        if (string.IsNullOrWhiteSpace(org.Id)
            || string.IsNullOrWhiteSpace(org.Name)
            || string.IsNullOrWhiteSpace(org.NumberingFormat))
        {
            return ErrorCode.Corrupt;
        }

        if (org.CycleStartDay < SetupOrg.MinCycleStartDay || org.CycleStartDay > SetupOrg.MaxCycleStartDay)
        {
            return ErrorCode.Corrupt;
        }

        if (!Base64Url.TryDecode(org.SigningPub, out var signing) || signing.Length != DeviceIdentity.PublicKeySize
            || !Base64Url.TryDecode(org.X25519Pub, out var agreement) || agreement.Length != DeviceIdentity.PublicKeySize)
        {
            return ErrorCode.Corrupt;
        }

        // The keys inside the payload and the certificate outside it describe one organisation
        // or the file is not what it claims: the certificate is what the signature was checked
        // against, and these fields are what the installation will store and trust from now on.
        if (!string.Equals(org.Id, producer.Body.OrgId, StringComparison.Ordinal)
            || !string.Equals(org.X25519Pub, producer.Body.X25519Pub, StringComparison.Ordinal)
            || !signing.AsSpan().SequenceEqual(orgSigningKey))
        {
            return ErrorCode.Tampered;
        }

        return null;
    }

    private static ErrorCode? CheckOffice(SetupContent content)
    {
        var units = content.Units;
        if (units.Count == 0)
        {
            return ErrorCode.Corrupt;
        }

        var byId = new Dictionary<string, SetupUnit>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            if (unit is null
                || string.IsNullOrWhiteSpace(unit.Id)
                || string.IsNullOrWhiteSpace(unit.Name)
                || unit.Level < SetupUnit.TopLevel
                || unit.Level > SetupUnit.DeepestLevel
                || !byId.TryAdd(unit.Id, unit))
            {
                return ErrorCode.Corrupt;
            }
        }

        foreach (var unit in units)
        {
            if (unit.Level == SetupUnit.TopLevel)
            {
                // The organisation itself is the only node without a parent.
                if (!string.IsNullOrEmpty(unit.ParentId))
                {
                    return ErrorCode.Corrupt;
                }

                continue;
            }

            // Every other node hangs from exactly one node of the level above it, which makes
            // the four levels real and makes a cycle impossible to express at all.
            if (unit.ParentId is null
                || !byId.TryGetValue(unit.ParentId, out var parent)
                || parent.Level != unit.Level - 1)
            {
                return ErrorCode.Corrupt;
            }
        }

        var office = content.Office;
        if (string.IsNullOrWhiteSpace(office.UnitId)
            || string.IsNullOrWhiteSpace(office.OfficeCode)
            || !byId.TryGetValue(office.UnitId, out var host)
            || !string.Equals(host.OfficeCode, office.OfficeCode, StringComparison.Ordinal))
        {
            return ErrorCode.Corrupt;
        }

        // The certificate was issued for one office; the file must be describing that office.
        var certificate = content.Device.Certificate;
        if (certificate?.Body is null)
        {
            return ErrorCode.Corrupt;
        }

        return string.Equals(certificate.Body.OfficeId, office.UnitId, StringComparison.Ordinal)
            ? null
            : ErrorCode.Tampered;
    }

    private static ErrorCode? CheckDevice(
        SetupContent content,
        ReadOnlySpan<byte> orgSigningKey,
        RevocationList? revocations,
        SetupExpectations expectations,
        DateTimeOffset now)
    {
        var device = content.Device;
        if (string.IsNullOrWhiteSpace(device.Id))
        {
            return ErrorCode.Corrupt;
        }

        // The headline verdict when an installation is updated: a file meant for the machine
        // next door must not be applied here, however well signed it is.
        if (!string.IsNullOrEmpty(expectations.InstalledDeviceId)
            && !string.Equals(device.Id, expectations.InstalledDeviceId, StringComparison.Ordinal))
        {
            return ErrorCode.OtherDevice;
        }

        if (device.DeviceNo < SetupDevice.MinDeviceNo
            || device.DeviceNo > SetupDevice.MaxDeviceNo
            || !SetupRoles.IsKnown(device.Role)
            || !SetupSyncScopes.IsKnown(device.SyncScope))
        {
            return ErrorCode.Corrupt;
        }

        var certificate = device.Certificate;
        if (certificate?.Body is null)
        {
            return ErrorCode.Corrupt;
        }

        if (!CertificateChain.TryVerify(certificate, orgSigningKey, revocations, now, out var error))
        {
            return error;
        }

        if (certificate.Body.Kind != DeviceKind.Pc)
        {
            return ErrorCode.Corrupt;
        }

        if (!string.Equals(certificate.Body.DeviceId, device.Id, StringComparison.Ordinal)
            || certificate.Body.DeviceNo != device.DeviceNo
            || !string.Equals(certificate.Body.Role, device.Role, StringComparison.Ordinal)
            || !string.Equals(certificate.Body.OrgId, content.Org.Id, StringComparison.Ordinal))
        {
            return ErrorCode.Tampered;
        }

        return CheckSeeds(content.DeviceSeed, certificate);
    }

    /// <summary>
    /// The seeds must be the private halves of the very keys the certificate names. Without
    /// this an installation could be activated from a file whose certificate it can never use
    /// to sign anything, and the damage would only surface at the first exchange with another
    /// device, long after the first run screen said everything was in order.
    /// </summary>
    private static ErrorCode? CheckSeeds(DeviceSeeds seeds, DeviceCertificate certificate)
    {
        try
        {
            using var identity = DeviceIdentity.Import(seeds);
            return string.Equals(identity.SigningPublicKeyText, certificate.Body.Ed25519Pub, StringComparison.Ordinal)
                && string.Equals(identity.AgreementPublicKeyText, certificate.Body.X25519Pub, StringComparison.Ordinal)
                ? null
                : ErrorCode.Tampered;
        }
        catch (CryptoException exception)
        {
            return exception.Code;
        }
    }

    private static ErrorCode? CheckEmployee(SetupContent content)
    {
        var employee = content.Employee;
        if (string.IsNullOrWhiteSpace(employee.Name)
            || string.IsNullOrWhiteSpace(employee.JobTitle)
            || employee.EmployeeNo < SetupEmployee.MinEmployeeNo
            || employee.EmployeeNo > SetupEmployee.MaxEmployeeNo)
        {
            return ErrorCode.Corrupt;
        }

        var certificate = content.Device.Certificate;
        if (certificate?.Body is null)
        {
            return ErrorCode.Corrupt;
        }

        // The employee number rides inside the official number of every letter this
        // installation issues, and the certificate is where the organisation fixed it.
        return certificate.Body.EmployeeNo == employee.EmployeeNo ? null : ErrorCode.Tampered;
    }

    private static ErrorCode? CheckRevocation(
        SetupContent content,
        ReadOnlySpan<byte> orgSigningKey,
        DateTimeOffset now,
        TimeSpan skew)
    {
        if (content.Revocation is not { } revocations)
        {
            return null;
        }

        if (revocations.Body is null || revocations.Body.Entries is null)
        {
            return ErrorCode.Corrupt;
        }

        foreach (var entry in revocations.Body.Entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.DeviceId))
            {
                return ErrorCode.Corrupt;
            }
        }

        if (!revocations.VerifySignature(orgSigningKey))
        {
            return ErrorCode.BadSignature;
        }

        if (!string.Equals(revocations.Body.OrgId, content.Org.Id, StringComparison.Ordinal))
        {
            return ErrorCode.BadSignature;
        }

        return revocations.Body.IssuedAt > now + skew ? ErrorCode.FutureDate : null;
    }

    /// <summary>
    /// An optional entry either was declared and is there, or was not declared and is not
    /// there. Anything else means the file was taken apart after it was described.
    /// </summary>
    private static void Optional(
        SetupCheckList checks,
        SetupCheckItem item,
        bool declared,
        IReadOnlyCollection<string> entryNames,
        string entryName)
    {
        var present = entryNames.Contains(entryName);
        if (declared && present)
        {
            checks.Ok(item);
        }
        else if (!declared && !present)
        {
            checks.Absent(item);
        }
        else
        {
            checks.Failed(item, ErrorCode.Tampered);
        }
    }
}
