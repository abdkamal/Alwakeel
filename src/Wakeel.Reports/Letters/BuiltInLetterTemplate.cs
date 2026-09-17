using System.Reflection;
using Wakeel.Core.Services.Correspondence;

namespace Wakeel.Reports.Letters;

/// <inheritdoc cref="IBuiltInLetterTemplate"/>
/// <remarks>
/// The file embedded here is <c>docs/templates/correspondence/نموذج-المراسلة.docx</c> itself — the
/// reference template the owner approved, embedded from its own path rather than copied into the
/// project, so the two can never drift apart. It carries the nine marks and the fixed phrases and
/// nothing else: an organisation's own letterhead arrives in the setup file and replaces it.
/// </remarks>
public sealed class BuiltInLetterTemplate : IBuiltInLetterTemplate
{
    /// <summary>The name the bytes are embedded under.</summary>
    internal const string ResourceName = "Wakeel.Reports.Letters.default-letter-template.docx";

    private static readonly Lazy<byte[]> Bytes = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <inheritdoc />
    public byte[] Read() => (byte[])Bytes.Value.Clone();

    /// <summary>The embedded template, without copying it; for readers that only look.</summary>
    public static ReadOnlySpan<byte> Content => Bytes.Value;

    private static byte[] Load()
    {
        var assembly = typeof(BuiltInLetterTemplate).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                "The built-in letter template is missing from the assembly.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
