using System.Security.Cryptography;
using System.Text;

namespace Wakeel.UI.Services.Account;

/// <summary>
/// The setup file names organisations, units, offices and devices with opaque text identifiers,
/// while every identifier column of the database is a <see cref="Guid"/> (DATA-MODEL.md §0). This
/// is the one place the two meet.
/// </summary>
/// <remarks>
/// An identifier that already is a GUID — what the administration tool writes — is kept exactly as
/// it is, so the same node carries the same identity in the tool, in every office's database and in
/// every packet exchanged between them. Anything else is folded through SHA-256 into a stable GUID,
/// which keeps the mapping identical on every machine that ever reads the same file, so two offices
/// of one organisation still agree on the identity of a shared unit.
/// </remarks>
public static class SetupIds
{
    private const string Label = "wakeel.setup.id|v1|";

    /// <summary>The database identifier for one identifier written in a setup file.</summary>
    public static Guid ToGuid(string? value)
    {
        if (Guid.TryParse(value, out var parsed))
        {
            return parsed;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Label + (value ?? string.Empty)));
        return new Guid(hash.AsSpan(0, 16));
    }
}
