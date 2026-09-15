using System.Text;

namespace Wakeel.Crypto;

/// <summary>
/// Every Ed25519 signature the product makes is taken over a labelled buffer, so a signature
/// produced for one kind of structure can never be replayed as a signature for another kind.
/// The label is a constant ASCII prefix followed by the canonical bytes of the structure.
/// </summary>
internal static class DomainSeparation
{
    /// <summary>Label of a device certificate body.</summary>
    internal const string Certificate = "wakeel.cert|v1";

    /// <summary>Label of a revocation list body.</summary>
    internal const string RevocationList = "wakeel.revocations|v1";

    /// <summary>Label of a pairing request body.</summary>
    internal const string PairingRequest = "wakeel.pairing.request|v1";

    /// <summary>Label of a pairing accept body.</summary>
    internal const string PairingAccept = "wakeel.pairing.accept|v1";

    /// <summary>Label of the two container hashes.</summary>
    internal const string Container = "wakeel.container.sig|v1";

    /// <summary>Returns <c>label || body</c>, the exact bytes a signature covers.</summary>
    internal static byte[] Wrap(string label, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.UTF8.GetBytes(label);
        var buffer = new byte[prefix.Length + body.Length];
        prefix.CopyTo(buffer, 0);
        body.CopyTo(buffer.AsSpan(prefix.Length));
        return buffer;
    }
}
