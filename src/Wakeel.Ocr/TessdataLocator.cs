using Wakeel.Core.Data;

namespace Wakeel.Ocr;

/// <summary>
/// Finds the files Tesseract reads with. There is no single right place for them: an installed
/// الوكيل ships them beside the executable, an office that added them later drops them into the
/// models folder the setup watches (AGREEMENT item 30), and a developer or a test has them in the
/// repository's <c>tools\tessdata</c>. All three are looked at, in that order, and the first
/// folder that holds both languages wins.
/// </summary>
/// <remarks>
/// Nothing here throws when the files are absent: that is the «قراءة النصوص غير مهيّأة» state, and
/// an installation in it still stores every document and simply does not read its text.
/// </remarks>
public static class TessdataLocator
{
    /// <summary>The folder name Tesseract expects, wherever it sits.</summary>
    public const string FolderName = "tessdata";

    /// <summary>The two languages الوكيل reads — Arabic first, because the office writes Arabic.</summary>
    public const string Languages = "ara+eng";

    /// <summary>The files a usable folder has to hold.</summary>
    public static IReadOnlyList<string> RequiredFiles { get; } = ["ara.traineddata", "eng.traineddata"];

    /// <summary>The file that marks the top of the source tree, so the walk up stops at it.</summary>
    private const string SolutionFileName = "Wakeel.slnx";

    /// <summary>
    /// The folder to hand Tesseract, or <c>null</c> when this machine has none.
    /// </summary>
    /// <param name="paths">The installation, whose models folder is looked in second.</param>
    public static string? Find(WakeelPaths? paths = null)
    {
        foreach (var candidate in Candidates(paths))
        {
            if (IsUsable(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Whether a folder holds both languages.</summary>
    public static bool IsUsable(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        try
        {
            return RequiredFiles.All(file => File.Exists(Path.Combine(folder, file)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// A short, stable name for the files in a folder — their names, sizes and last-written times,
    /// hashed down to a few characters. Stored beside a document's read text so that swapping the
    /// files (AGREEMENT item 30) can be noticed and the affected documents read again.
    /// </summary>
    /// <remarks>
    /// The time is in the hash and not only the size because two builds of the same language —
    /// the fast set and the best set, or one version of either — are routinely different files,
    /// and two of them happening to be the same number of bytes is not far-fetched. Without the
    /// time such a swap would leave every document carrying text nobody can reproduce.
    /// </remarks>
    public static string? Fingerprint(string? folder)
    {
        if (!IsUsable(folder))
        {
            return null;
        }

        try
        {
            var parts = RequiredFiles
                .Select(file => new FileInfo(Path.Combine(folder!, file)))
                .Select(info => info.Name
                    + ":" + info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ":" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var joined = string.Join('|', parts);
            return Wakeel.Crypto.Sha256.HashHex(System.Text.Encoding.UTF8.GetBytes(joined))[..12];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Every place the files may be, in the order they are preferred.</summary>
    private static IEnumerable<string> Candidates(WakeelPaths? paths)
    {
        // 1. Beside the running application — where an installed الوكيل keeps them.
        var appFolder = AppContext.BaseDirectory;
        yield return Path.Combine(appFolder, FolderName);

        // 2. The installation's models folder, which the setup watches (AGREEMENT item 30).
        if (paths is not null)
        {
            yield return Path.Combine(paths.ModelsDir, FolderName);
            yield return paths.ModelsDir;
        }

        // 3. The repository's tools folder, walking up from the running assembly — this is how a
        //    test and a developer's build find them without a copy per project. The walk only
        //    offers a folder that sits in the source tree itself, next to the solution file: an
        //    installed الوكيل under C:\Program Files\Wakeel\ must never end up reading its model
        //    files out of C:\tools\tessdata, a folder anything on the machine can create.
        var directory = new DirectoryInfo(appFolder);
        for (var depth = 0; depth < 8 && directory is not null; depth++)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                yield return Path.Combine(directory.FullName, "tools", FolderName);
            }

            directory = directory.Parent;
        }
    }
}
