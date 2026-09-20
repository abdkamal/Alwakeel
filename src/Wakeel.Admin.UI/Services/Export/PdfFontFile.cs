using System.Buffers.Binary;
using System.Reflection;

namespace Wakeel.Admin.UI.Services.Export;

/// <summary>
/// The few facts about a TrueType file that a PDF needs in order to draw with it: which glyph a
/// character is, how wide each glyph is, and the handful of measurements a font descriptor carries.
/// </summary>
/// <remarks>
/// The tool writes the provisional guide itself (A08), on a computer that may have no Arabic
/// typeface installed at all, so the face travels inside the PDF. Everything here is read-only
/// parsing of a file the tool ships with; a face it cannot make sense of is reported as unusable and
/// the guide falls back to a page without words rather than to a broken file.
/// </remarks>
public sealed class PdfFontFile
{
    /// <summary>The embedded resource the tool ships.</summary>
    private const string ResourceName = "Wakeel.Admin.UI.Assets.NotoSansArabic-Regular.ttf";

    /// <summary>The PostScript name written into the PDF font dictionaries.</summary>
    public const string FontName = "NotoSansArabic";

    private static PdfFontFile? _shipped;

    private readonly Dictionary<int, ushort> _glyphs;
    private readonly ushort[] _advances;
    private readonly int _unitsPerEm;

    private PdfFontFile(
        byte[] bytes,
        Dictionary<int, ushort> glyphs,
        ushort[] advances,
        int unitsPerEm,
        short xMin,
        short yMin,
        short xMax,
        short yMax,
        short ascender,
        short descender)
    {
        Bytes = bytes;
        _glyphs = glyphs;
        _advances = advances;
        _unitsPerEm = unitsPerEm;
        BBox = [Scale(xMin), Scale(yMin), Scale(xMax), Scale(yMax)];
        Ascent = Scale(ascender);
        Descent = Scale(descender);
    }

    /// <summary>The whole font file, as it is embedded in the PDF.</summary>
    public byte[] Bytes { get; }

    /// <summary>The font's bounding box, in the thousandths of an em a PDF counts in.</summary>
    public int[] BBox { get; }

    /// <summary>How far the tallest letters rise, in thousandths of an em.</summary>
    public int Ascent { get; }

    /// <summary>How far the lowest letters fall, in thousandths of an em; negative.</summary>
    public int Descent { get; }

    /// <summary>The face the tool ships, or null on a build where it cannot be read.</summary>
    public static PdfFontFile? Shipped()
    {
        if (_shipped is not null)
        {
            return _shipped;
        }

        try
        {
            using var stream = typeof(PdfFontFile).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                return null;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _shipped = Parse(memory.ToArray());
            return _shipped;
        }
        catch (Exception exception) when (exception is IOException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>The glyph that draws this character, or zero when the face has none.</summary>
    public ushort GlyphOf(char character) => _glyphs.GetValueOrDefault(character, (ushort)0);

    /// <summary>How wide a glyph is, in thousandths of an em.</summary>
    public int WidthOf(ushort glyph)
    {
        if (_advances.Length == 0)
        {
            return 500;
        }

        var advance = glyph < _advances.Length ? _advances[glyph] : _advances[^1];
        return Scale((short)Math.Min(advance, (ushort)short.MaxValue));
    }

    /// <summary>How wide a run of already-shaped glyphs is, at one point of type size.</summary>
    public double WidthOf(string visualText)
    {
        var total = 0d;
        foreach (var character in visualText)
        {
            // A character with no glyph is not drawn, so it takes no room either; measuring it would
            // push the whole line away from the margin it is aligned to.
            var glyph = GlyphOf(character);
            if (glyph != 0)
            {
                total += WidthOf(glyph) / 1000d;
            }
        }

        return total;
    }

    /// <summary>Reads a font file. Returns null for anything this small parser cannot make sense of.</summary>
    public static PdfFontFile? Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            if (bytes.Length < 12)
            {
                return null;
            }

            var tableCount = ReadUInt16(bytes, 4);
            var tables = new Dictionary<string, (int Offset, int Length)>(StringComparer.Ordinal);
            for (var index = 0; index < tableCount; index++)
            {
                var record = 12 + (16 * index);
                if (record + 16 > bytes.Length)
                {
                    return null;
                }

                var tag = System.Text.Encoding.ASCII.GetString(bytes, record, 4);
                tables[tag] = ((int)ReadUInt32(bytes, record + 8), (int)ReadUInt32(bytes, record + 12));
            }

            if (!tables.TryGetValue("head", out var head)
                || !tables.TryGetValue("hhea", out var hhea)
                || !tables.TryGetValue("hmtx", out var hmtx)
                || !tables.TryGetValue("cmap", out var cmap))
            {
                return null;
            }

            var unitsPerEm = ReadUInt16(bytes, head.Offset + 18);
            if (unitsPerEm == 0)
            {
                return null;
            }

            var metrics = ReadUInt16(bytes, hhea.Offset + 34);
            var advances = new ushort[metrics];
            for (var index = 0; index < metrics; index++)
            {
                advances[index] = ReadUInt16(bytes, hmtx.Offset + (4 * index));
            }

            var glyphs = ReadCharacterMap(bytes, cmap.Offset);
            if (glyphs is null || glyphs.Count == 0)
            {
                return null;
            }

            return new PdfFontFile(
                bytes,
                glyphs,
                advances,
                unitsPerEm,
                ReadInt16(bytes, head.Offset + 36),
                ReadInt16(bytes, head.Offset + 38),
                ReadInt16(bytes, head.Offset + 40),
                ReadInt16(bytes, head.Offset + 42),
                ReadInt16(bytes, hhea.Offset + 4),
                ReadInt16(bytes, hhea.Offset + 6));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or ArgumentException)
        {
            // A face this parser cannot follow is not an error a person should ever meet: the guide
            // is written without words instead, and every other part of the export is unaffected.
            return null;
        }
    }

    /// <summary>
    /// The character-to-glyph table. Only the two subtables a modern face actually carries are read:
    /// the Windows Unicode ones, in format 4 (the basic plane) and format 12 (everything).
    /// </summary>
    private static Dictionary<int, ushort>? ReadCharacterMap(byte[] bytes, int offset)
    {
        var subtables = ReadUInt16(bytes, offset + 2);
        var best = -1;
        var bestScore = -1;

        for (var index = 0; index < subtables; index++)
        {
            var record = offset + 4 + (8 * index);
            var platform = ReadUInt16(bytes, record);
            var encoding = ReadUInt16(bytes, record + 2);
            var subtable = offset + (int)ReadUInt32(bytes, record + 4);
            var format = ReadUInt16(bytes, subtable);

            var score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 3,
                (0, _, 12) => 2,
                (3, 1, 4) => 1,
                (0, _, 4) => 1,
                _ => -1,
            };

            if (score > bestScore)
            {
                bestScore = score;
                best = subtable;
            }
        }

        if (best < 0)
        {
            return null;
        }

        return ReadUInt16(bytes, best) switch
        {
            4 => ReadFormat4(bytes, best),
            12 => ReadFormat12(bytes, best),
            _ => null,
        };
    }

    private static Dictionary<int, ushort> ReadFormat4(byte[] bytes, int subtable)
    {
        var map = new Dictionary<int, ushort>();
        var segments = ReadUInt16(bytes, subtable + 6) / 2;
        var ends = subtable + 14;
        var starts = ends + (segments * 2) + 2;
        var deltas = starts + (segments * 2);
        var ranges = deltas + (segments * 2);

        for (var segment = 0; segment < segments; segment++)
        {
            var end = ReadUInt16(bytes, ends + (segment * 2));
            var start = ReadUInt16(bytes, starts + (segment * 2));
            var delta = ReadInt16(bytes, deltas + (segment * 2));
            var rangeOffset = ReadUInt16(bytes, ranges + (segment * 2));

            if (start > end)
            {
                continue;
            }

            for (var code = (int)start; code <= end && code != 0xFFFF; code++)
            {
                ushort glyph;
                if (rangeOffset == 0)
                {
                    glyph = (ushort)((code + delta) & 0xFFFF);
                }
                else
                {
                    var at = ranges + (segment * 2) + rangeOffset + ((code - start) * 2);
                    if (at + 1 >= bytes.Length)
                    {
                        continue;
                    }

                    glyph = ReadUInt16(bytes, at);
                    if (glyph != 0)
                    {
                        glyph = (ushort)((glyph + delta) & 0xFFFF);
                    }
                }

                if (glyph != 0)
                {
                    map[code] = glyph;
                }
            }
        }

        return map;
    }

    private static Dictionary<int, ushort> ReadFormat12(byte[] bytes, int subtable)
    {
        var map = new Dictionary<int, ushort>();
        var groups = (int)ReadUInt32(bytes, subtable + 12);

        for (var group = 0; group < groups; group++)
        {
            var record = subtable + 16 + (12 * group);
            if (record + 12 > bytes.Length)
            {
                break;
            }

            var start = (int)ReadUInt32(bytes, record);
            var end = (int)ReadUInt32(bytes, record + 4);
            var glyph = (int)ReadUInt32(bytes, record + 8);

            // Only the basic plane is ever asked for here, and a crafted group that spans millions
            // of code points must not be walked.
            end = Math.Min(end, 0xFFFF);
            for (var code = start; code <= end; code++)
            {
                map[code] = (ushort)(glyph + (code - start));
            }
        }

        return map;
    }

    private int Scale(short value) => (int)Math.Round(value * 1000d / _unitsPerEm, MidpointRounding.AwayFromZero);

    private static ushort ReadUInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));

    private static short ReadInt16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(offset, 2));

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
}
