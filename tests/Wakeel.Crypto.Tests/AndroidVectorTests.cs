using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wakeel.Crypto.Tests;

/// <summary>
/// Writes the interoperability vectors the Android core reads. Everything the phone has to
/// produce or accept byte for byte is generated here, from fixed keys, so a change on either
/// side that would break the cable shows up as a failing test instead of as a phone that
/// silently stops exchanging anything.
///
/// The files land in <c>android/core/src/test/resources/vectors/</c>; the Android tests read
/// them and write their own samples back into <c>android/core/build/vectors-out/</c>, which a
/// later .NET test reads in turn.
/// </summary>
public sealed class AndroidVectorTests
{
    private const string OrgId = "ORG-VECTOR";
    private const string OfficeId = "OFFICE-VECTOR";
    private const string PcDeviceId = "PC-VECTOR";
    private const string PhoneDeviceId = "PHONE-VECTOR";
    private const string RevokedDeviceId = "PHONE-REVOKED";

    /// <summary>The label the two sides derive the short code invitation key with.</summary>
    private const string InvitationKeyLabel = "wakeel.pairing.invite|v1|";

    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InvitedAt = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RequestedAt = new(2026, 9, 17, 9, 1, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 10, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void AndroidVectorsAreWritten()
    {
        var directory = VectorDirectory();
        Directory.CreateDirectory(directory);

        using var org = DeviceIdentity.Import(Seeds(0x11, 0x12));
        using var pc = DeviceIdentity.Import(Seeds(0x21, 0x22));
        using var phone = DeviceIdentity.Import(Seeds(0x31, 0x32));

        var officeKey = Filled(0x41);
        var sessionKey = Filled(0x51);
        var token = new PairingToken(Filled(0x61));

        var orgRoot = DeviceCertificate.IssueOrgRoot(OrgId, org, IssuedAt);
        var pcCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId, OfficeId, PcDeviceId, 1, 2, "secretary", DeviceKind.Pc,
                pc.SigningPublicKeyText, pc.AgreementPublicKeyText, IssuedAt, OrgId),
            org);
        var phoneCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId, OfficeId, PhoneDeviceId, 1, 2, "secretary", DeviceKind.Phone,
                phone.SigningPublicKeyText, phone.AgreementPublicKeyText, IssuedAt, PcDeviceId),
            pc);
        var revokedCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId, OfficeId, RevokedDeviceId, 3, 4, "custodian", DeviceKind.Phone,
                phone.SigningPublicKeyText, phone.AgreementPublicKeyText, IssuedAt, PcDeviceId),
            pc);

        var revocations = RevocationList.Issue(
            new RevocationListBody(OrgId, IssuedAt, [new RevocationEntry(RevokedDeviceId, IssuedAt)]),
            org);
        var emptyRevocations = RevocationList.Empty(OrgId, IssuedAt, org);

        WriteKeys(directory, org, pc, phone, officeKey, sessionKey, token);
        WriteCanonicalSamples(directory);
        WritePrimitives(directory, sessionKey, officeKey);
        WriteCertificates(directory, orgRoot, pcCertificate, phoneCertificate, revokedCertificate, revocations, emptyRevocations);
        var invitation = WritePairing(directory, org, pc, phone, pcCertificate, officeKey, sessionKey, token);
        WriteContainer(directory, pc, pcCertificate, sessionKey);
        WriteInvitationContainer(directory, pc, pcCertificate, invitation);

        // The suite must never pass while leaving an empty folder behind.
        Assert.True(File.Exists(Path.Combine(directory, "keys.json")));
        Assert.True(File.Exists(Path.Combine(directory, "container", "to-phone-1.wakeel-phone")));
    }

    /// <summary>Reads back what the Android tests produced, when they have run at least once.</summary>
    [Fact]
    public void AndroidProducedSamplesAreAccepted()
    {
        var produced = Path.Combine(RepositoryRoot(), "android", "core", "build", "vectors-out");
        if (!Directory.Exists(produced))
        {
            // The Android side has not run on this machine yet; that is reported by its own
            // build, and this test has nothing to check.
            return;
        }

        using var org = DeviceIdentity.Import(Seeds(0x11, 0x12));
        using var pc = DeviceIdentity.Import(Seeds(0x21, 0x22));
        var sessionKey = Filled(0x51);

        var pcCertificate = DeviceCertificate.Issue(
            new DeviceCertificateBody(
                OrgId, OfficeId, PcDeviceId, 1, 2, "secretary", DeviceKind.Pc,
                pc.SigningPublicKeyText, pc.AgreementPublicKeyText, IssuedAt, OrgId),
            org);

        var container = Path.Combine(produced, "container", "from-phone-1.wakeel-phone");
        if (File.Exists(container))
        {
            using var reader = ContainerReader.Open(
                container,
                new ContainerOpenOptions
                {
                    ExpectedKind = ContainerKind.Phone,
                    OrgSigningPublicKey = org.SigningPublicKey,
                    IssuerCertificate = pcCertificate,
                    Time = new FixedClock(Now),
                });

            var entries = reader.ReadEntries(ContainerKeySource.SessionKey(sessionKey));
            Assert.True(entries.ContainsKey("package.json"));
        }

        var request = Path.Combine(produced, "pairing", "request.json");
        if (File.Exists(request))
        {
            var parsed = CanonicalJson.Deserialize<PairingRequest>(File.ReadAllBytes(request));
            Assert.True(parsed.VerifySignature());
            Assert.True(parsed.VerifyToken(Filled(0x61)));
        }
    }

    private static void WriteKeys(
        string directory,
        DeviceIdentity org,
        DeviceIdentity pc,
        DeviceIdentity phone,
        byte[] officeKey,
        byte[] sessionKey,
        PairingToken token)
    {
        var keys = new
        {
            orgId = OrgId,
            officeId = OfficeId,
            pcDeviceId = PcDeviceId,
            phoneDeviceId = PhoneDeviceId,
            revokedDeviceId = RevokedDeviceId,
            issuedAt = CanonicalJson.Serialize(IssuedAt).Trim('"'),
            now = CanonicalJson.Serialize(Now).Trim('"'),
            org = Describe(org, 0x11, 0x12),
            pc = Describe(pc, 0x21, 0x22),
            phone = Describe(phone, 0x31, 0x32),
            officeKey = Base64Url.Encode(officeKey),
            sessionKey = Base64Url.Encode(sessionKey),
            pairingToken = token.Text,
            pairingShortCode = token.ShortCode,
        };

        File.WriteAllText(Path.Combine(directory, "keys.json"), JsonSerializer.Serialize(keys, Pretty), new UTF8Encoding(false));
    }

    private static object Describe(DeviceIdentity identity, byte signing, byte agreement) => new
    {
        signingSeed = Base64Url.Encode(Filled(signing)),
        agreementSeed = Base64Url.Encode(Filled(agreement)),
        ed25519Pub = identity.SigningPublicKeyText,
        x25519Pub = identity.AgreementPublicKeyText,
    };

    /// <summary>
    /// The canonical form of a handful of awkward structures: Arabic text, a mixed line, the
    /// escapes, nested objects whose members arrive out of order, and numbers.
    /// </summary>
    private static void WriteCanonicalSamples(string directory)
    {
        string[] inputs =
        [
            """{"b":1,"a":2}""",
            """{"Z":"z","a":"a","A":"A","_":"_"}""",
            """{"subject":"طلب صيانة مبنى الدائرة","number":"20260917/1101"}""",
            """{"text":"سطر\nفيه \"اقتباس\" و\\ ومسافة\tجدولة"}""",
            """{"nested":{"y":[3,2,1],"x":{"b":false,"a":true}},"n":null}""",
            """{"amount":12345678901,"zero":0,"negative":-7}""",
            """{"list":[{"b":1,"a":2},{"d":"د","c":"ج"}]}""",
            """{"empty":{},"emptyList":[],"blank":""}""",
        ];

        var samples = inputs.Select((input, index) => new
        {
            name = $"sample-{index + 1:00}",
            input,
            canonical = Encoding.UTF8.GetString(CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes(input))),
        }).ToArray();

        File.WriteAllText(
            Path.Combine(directory, "canonical-json.json"),
            JsonSerializer.Serialize(samples, Pretty),
            new UTF8Encoding(false));
    }

    /// <summary>Fixed answers for the primitives, so a wrong derivation is caught at once.</summary>
    private static void WritePrimitives(string directory, byte[] sessionKey, byte[] officeKey)
    {
        var message = Encoding.UTF8.GetBytes("الوكيل — interoperability vector");
        var salt = Filled(0x71, 16);

        var sealedBuffer = Aead.Encrypt(sessionKey, message, "wakeel.vector");
        using var stream = new MemoryStream();
        using (var input = new MemoryStream(message))
        {
            Aead.EncryptStream(sessionKey, input, stream, Encoding.UTF8.GetBytes("wakeel.vector.stream"), 1024);
        }

        var primitives = new
        {
            sha256OfMessageHex = Sha256.HashHex(message),
            messageBase64Url = Base64Url.Encode(message),
            hkdf = new
            {
                saltBase64Url = Base64Url.Encode(salt),
                info = "wakeel.container|phone",
                outputBase64Url = Base64Url.Encode(Hkdf.DeriveKey(sessionKey, 32, salt, "wakeel.container|phone")),
            },
            hkdfNoSalt = new
            {
                info = "wakeel.vector|no-salt",
                outputBase64Url = Base64Url.Encode(Hkdf.DeriveKey(officeKey, 64, "wakeel.vector|no-salt")),
            },
            aeadBuffer = new
            {
                associatedData = "wakeel.vector",
                sealedBase64Url = Base64Url.Encode(sealedBuffer),
            },
            aeadStream = new
            {
                associatedData = "wakeel.vector.stream",
                chunkSize = 1024,
                sealedBase64Url = Base64Url.Encode(stream.ToArray()),
            },
            shortCodeOfPairingToken = PairingCodes.ShortCode(Filled(0x61)),
            tokenProof = PairingCodes.TokenProof(Filled(0x61), PhoneDeviceId),
        };

        File.WriteAllText(
            Path.Combine(directory, "primitives.json"),
            JsonSerializer.Serialize(primitives, Pretty),
            new UTF8Encoding(false));
    }

    private static void WriteCertificates(
        string directory,
        DeviceCertificate orgRoot,
        DeviceCertificate pcCertificate,
        DeviceCertificate phoneCertificate,
        DeviceCertificate revokedCertificate,
        RevocationList revocations,
        RevocationList emptyRevocations)
    {
        var folder = Path.Combine(directory, "certificates");
        Directory.CreateDirectory(folder);

        WriteCanonical(Path.Combine(folder, "org-root.json"), orgRoot);
        WriteCanonical(Path.Combine(folder, "pc.json"), pcCertificate);
        WriteCanonical(Path.Combine(folder, "phone.json"), phoneCertificate);
        WriteCanonical(Path.Combine(folder, "phone-revoked.json"), revokedCertificate);
        WriteCanonical(Path.Combine(folder, "revocations.json"), revocations);
        WriteCanonical(Path.Combine(folder, "revocations-empty.json"), emptyRevocations);
    }

    private static PairingQrPayload WritePairing(
        string directory,
        DeviceIdentity org,
        DeviceIdentity pc,
        DeviceIdentity phone,
        DeviceCertificate pcCertificate,
        byte[] officeKey,
        byte[] sessionKey,
        PairingToken token)
    {
        var folder = Path.Combine(directory, "pairing");
        Directory.CreateDirectory(folder);

        var invitation = PairingQrPayload.Create(
            OrgId, OfficeId, PcDeviceId, pc.AgreementPublicKey, token, new FixedClock(InvitedAt));

        File.WriteAllText(Path.Combine(folder, "qr.txt"), invitation.ToBase64Url(), new UTF8Encoding(false));
        WriteCanonical(Path.Combine(folder, "qr.json"), invitation);

        var request = PairingRequest.Create(
            new PairingRequestBody(
                OrgId,
                OfficeId,
                PcDeviceId,
                PhoneDeviceId,
                phone.SigningPublicKeyText,
                phone.AgreementPublicKeyText,
                PairingCodes.TokenProof(token.Value, PhoneDeviceId),
                RequestedAt),
            phone);
        WriteCanonical(Path.Combine(folder, "request.json"), request);

        var accept = PairingAccept.Issue(
            request,
            invitation,
            pcCertificate,
            pc,
            phoneDeviceNo: 1,
            phoneEmployeeNo: 2,
            phoneRole: "secretary",
            sessionKey,
            officeKey,
            new FixedClock(RequestedAt.AddMinutes(1)));
        WriteCanonical(Path.Combine(folder, "accept.json"), accept);

        File.WriteAllText(
            Path.Combine(folder, "expected.json"),
            JsonSerializer.Serialize(
                new
                {
                    shortCode = invitation.ShortCode(),
                    tokenProof = request.Body.TokenProof,
                    requestSignature = request.Signature,
                    orgEd25519Pub = org.SigningPublicKeyText,
                    sessionKeyBase64Url = Base64Url.Encode(sessionKey),
                    officeKeyBase64Url = Base64Url.Encode(officeKey),
                    invitationFileName = $"pairing-{invitation.ShortCode()}.wakeel-phone",
                },
                Pretty),
            new UTF8Encoding(false));

        return invitation;
    }

    /// <summary>A complete small phone packet, with its plain contents beside it.</summary>
    private static void WriteContainer(
        string directory,
        DeviceIdentity pc,
        DeviceCertificate pcCertificate,
        byte[] sessionKey)
    {
        var folder = Path.Combine(directory, "container");
        Directory.CreateDirectory(folder);

        var body = """
            {"seq":1,"createdAt":"2026-09-17T09:05:00.000Z","tables":{"tasks":[{"id":"T-1","title":"مراجعة كتاب التزويد","status":"open","updated_at":"2026-09-17T09:00:00.000Z"}],"settings":[{"key":"meeting_reminder_minutes","value":"15","updated_at":"2026-09-17T09:00:00.000Z"}]}}
            """;
        var bodyBytes = CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes(body));
        var fileBytes = Encoding.UTF8.GetBytes("ملف مثبّت للعمل دون اتصال");

        var path = Path.Combine(folder, "to-phone-1.wakeel-phone");
        var manifest = ContainerWriter.Write(
            path,
            new ContainerWriteRequest
            {
                Kind = ContainerKind.Phone,
                Producer = pcCertificate,
                Signer = pc,
                Key = ContainerKeySource.SessionKey(sessionKey),
                Entries =
                [
                    ContainerEntrySource.FromBytes("package.json", bodyBytes),
                    ContainerEntrySource.FromBytes("files/pinned-1.txt", fileBytes),
                ],
                Time = new FixedClock(Now),
            });

        File.WriteAllText(
            Path.Combine(folder, "to-phone-1.expected.json"),
            JsonSerializer.Serialize(
                new
                {
                    kind = "phone",
                    mode = "session",
                    createdAt = CanonicalJson.Serialize(manifest.CreatedAt).Trim('"'),
                    entries = manifest.Entries.Select(e => new { e.Name, e.Size, e.Sha256 }).ToArray(),
                    packageJson = Encoding.UTF8.GetString(bodyBytes),
                    pinnedFileBase64Url = Base64Url.Encode(fileBytes),
                    manifestCanonical = Encoding.UTF8.GetString(CanonicalJson.SerializeToUtf8Bytes(manifest)),
                },
                Pretty),
            new UTF8Encoding(false));
    }

    /// <summary>
    /// The invitation the computer leaves in the folder for the person who types the six digit
    /// code instead of scanning: the same payload the image carries, inside a phone container
    /// whose key is derived from the code itself.
    /// </summary>
    private static void WriteInvitationContainer(
        string directory,
        DeviceIdentity pc,
        DeviceCertificate pcCertificate,
        PairingQrPayload invitation)
    {
        var folder = Path.Combine(directory, "pairing");
        Directory.CreateDirectory(folder);

        var code = invitation.ShortCode();
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(InvitationKeyLabel + code));

        ContainerWriter.Write(
            Path.Combine(folder, $"pairing-{code}.wakeel-phone"),
            new ContainerWriteRequest
            {
                Kind = ContainerKind.Phone,
                Producer = pcCertificate,
                Signer = pc,
                Key = ContainerKeySource.SessionKey(key),
                Entries = [ContainerEntrySource.FromBytes("invitation.json", CanonicalJson.SerializeToUtf8Bytes(invitation))],
                Time = new FixedClock(InvitedAt),
            });
    }

    private static void WriteCanonical<T>(string path, T value) =>
        File.WriteAllBytes(path, CanonicalJson.SerializeToUtf8Bytes(value));

    private static DeviceSeeds Seeds(byte signing, byte agreement) => new(Filled(signing), Filled(agreement));

    private static byte[] Filled(byte value, int size = 32)
    {
        var buffer = new byte[size];
        for (var index = 0; index < size; index++)
        {
            // A constant byte would hide an offset mistake, so each position differs.
            buffer[index] = (byte)(value + index);
        }

        return buffer;
    }

    private static string VectorDirectory() =>
        Path.Combine(RepositoryRoot(), "android", "core", "src", "test", "resources", "vectors");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Wakeel.Crypto"))
                && File.Exists(Path.Combine(directory.FullName, "Wakeel.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be found from the test output folder.");
    }
}
