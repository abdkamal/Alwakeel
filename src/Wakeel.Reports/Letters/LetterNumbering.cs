using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Wakeel.Reports.Letters;

/// <summary>
/// The two numbering definitions a letter body needs — one that counts and one that bullets —
/// added to the generated document rather than demanded of the template.
/// </summary>
/// <remarks>
/// A template written by an office may carry its own numbered lists with their own ids, so the
/// two definitions added here are given ids above everything already in the file and the
/// template's own lists are never touched. Both are right-aligned at level zero: an Arabic list
/// counts from the right.
/// </remarks>
internal static class LetterNumbering
{
    /// <summary>The numbering ids a letter's lists refer to.</summary>
    /// <param name="Decimal">The id of «1. 2. 3.».</param>
    /// <param name="Bullet">The id of «•».</param>
    internal sealed record Ids(int Decimal, int Bullet);

    /// <summary>
    /// Returns the letter's numbering ids, adding the definitions the first time it is asked.
    /// </summary>
    /// <param name="document">The open letter.</param>
    public static Ids? Ensure(WordprocessingDocument document)
    {
        var main = document.MainDocumentPart;
        if (main is null)
        {
            return null;
        }

        var part = main.NumberingDefinitionsPart ?? main.AddNewPart<NumberingDefinitionsPart>();
        part.Numbering ??= new Numbering();
        var numbering = part.Numbering;

        var existing = numbering.Elements<NumberingInstance>().ToList();

        // Asked twice — a template with two body marks — the same two definitions are reused
        // rather than piled up.
        if (Reuse(numbering, existing) is { } already)
        {
            return already;
        }

        var abstractBase = numbering.Elements<AbstractNum>()
            .Select(a => (int?)(a.AbstractNumberId?.Value ?? 0))
            .DefaultIfEmpty(-1)
            .Max() ?? -1;
        var numberBase = existing
            .Select(n => (int?)(n.NumberID?.Value ?? 0))
            .DefaultIfEmpty(0)
            .Max() ?? 0;

        var decimalAbstract = abstractBase + 1;
        var bulletAbstract = abstractBase + 2;
        var decimalNumber = numberBase + 1;
        var bulletNumber = numberBase + 2;

        // Every abstract definition must precede every instance — and every picture bullet the
        // office's template may carry must precede every abstract definition, or Word calls the
        // letter damaged and offers to repair it. So the two definitions go after the last
        // picture bullet when there is one, and at the head when there is none, and the two
        // instances are appended at the tail.
        var lastPicture = numbering.Elements<NumberingPictureBullet>().LastOrDefault();
        if (lastPicture is null)
        {
            numbering.PrependChild(Abstract(bulletAbstract, NumberFormatValues.Bullet, BulletCharacter));
            numbering.PrependChild(Abstract(decimalAbstract, NumberFormatValues.Decimal, "%1."));
        }
        else
        {
            numbering.InsertAfter(Abstract(decimalAbstract, NumberFormatValues.Decimal, "%1."), lastPicture);
            numbering.InsertAfter(
                Abstract(bulletAbstract, NumberFormatValues.Bullet, BulletCharacter),
                numbering.Elements<AbstractNum>().First(a => a.AbstractNumberId?.Value == decimalAbstract));
        }

        numbering.AppendChild(Instance(decimalNumber, decimalAbstract));
        numbering.AppendChild(Instance(bulletNumber, bulletAbstract));

        part.Numbering.Save();
        return new Ids(decimalNumber, bulletNumber);
    }

    /// <summary>The name that marks a definition as one of الوكيل's own.</summary>
    private const string NamePrefix = "wakeel-letter-";

    /// <summary>
    /// The marker of a bulleted item, as Word itself writes it: the character at F0B7 in the
    /// Symbol font, which is the position that font gives the round bullet. Asking Word to look
    /// the ordinary bullet character up in a symbol-encoded font is what prints something else.
    /// </summary>
    private const string BulletCharacter = "";

    private static Ids? Reuse(Numbering numbering, List<NumberingInstance> instances)
    {
        int? IdFor(NumberFormatValues format)
        {
            foreach (var instance in instances)
            {
                var abstractId = instance.AbstractNumId?.Val?.Value;
                if (abstractId is not { } id || instance.NumberID?.Value is not { } numberId)
                {
                    continue;
                }

                var definition = numbering.Elements<AbstractNum>()
                    .FirstOrDefault(a => a.AbstractNumberId?.Value == id);
                var name = definition?.AbstractNumDefinitionName?.Val?.Value;
                if (name is null || !name.StartsWith(NamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (definition!.Elements<Level>().FirstOrDefault()?.NumberingFormat?.Val?.Value == format)
                {
                    return numberId;
                }
            }

            return null;
        }

        var counted = IdFor(NumberFormatValues.Decimal);
        var bulleted = IdFor(NumberFormatValues.Bullet);
        return counted is { } c && bulleted is { } b ? new Ids(c, b) : null;
    }

    private static AbstractNum Abstract(int id, NumberFormatValues format, string levelText)
    {
        var level = new Level
        {
            LevelIndex = 0,
            StartNumberingValue = new StartNumberingValue { Val = 1 },
            NumberingFormat = new NumberingFormat { Val = format },
            LevelText = new LevelText { Val = levelText },
            LevelJustification = new LevelJustification { Val = LevelJustificationValues.Right },
            PreviousParagraphProperties = new PreviousParagraphProperties(
                new Indentation
                {
                    Start = "720",
                    Hanging = "360",
                }),
        };

        if (format == NumberFormatValues.Bullet)
        {
            level.NumberingSymbolRunProperties = new NumberingSymbolRunProperties(
                new RunFonts { Ascii = "Symbol", HighAnsi = "Symbol", Hint = FontTypeHintValues.Default });
        }

        return new AbstractNum(new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }, level)
        {
            AbstractNumberId = id,
            AbstractNumDefinitionName = new AbstractNumDefinitionName
            {
                Val = NamePrefix + id.ToString(CultureInfo.InvariantCulture),
            },
        };
    }

    private static NumberingInstance Instance(int id, int abstractId) =>
        new(new AbstractNumId { Val = abstractId }) { NumberID = id };
}
