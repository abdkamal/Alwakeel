using System.Runtime.InteropServices;
using Microsoft.Win32;
using Wakeel.Core.Services;

namespace Wakeel.Desktop.Services;

/// <summary>
/// The Windows half of <see cref="IWordProbe"/>: answers whether Word is installed and, when it
/// is, which version — the "Word (موجود/غير موجود مع الإصدار)" card of the health center (W12).
/// </summary>
/// <remarks>
/// <para>
/// The check is made against the registry, not by starting Word. ARCHITECTURE.md §9 talks to Word
/// through late-bound COM when a letter is actually being produced; doing that here would launch a
/// full copy of Word every time the health center refreshed, which on an office PC is several
/// seconds and a visible window. The registry answers the same question in microseconds.
/// </para>
/// <para>
/// Two keys are consulted, in order: <c>Word.Application\CurVer</c> gives the installed version's
/// ProgID (for example <c>Word.Application.16</c>), which is present for every Word install back to
/// Office 2007; <c>Word.Application\CLSID</c> is the fallback that proves Word is registered even
/// when <c>CurVer</c> is missing, in which case the card shows "متوفر" with no version rather than
/// guessing one. The numeric part of the ProgID is mapped to the year people know ("Office 2016 أو
/// أحدث" for 16) rather than shown raw, because a number like "16" is not a version anyone
/// recognises and AGREEMENT item 15 forbids putting codes in front of the user.
/// </para>
/// <para>
/// The registry is a Windows API, so the whole body is guarded for the platform and every failure
/// (a locked hive, a policy-restricted read) answers "not installed" rather than throwing into the
/// health center — <see cref="HealthService"/> would catch it anyway, but a probe that reports
/// cleanly gives the same card without the exception.
/// </para>
/// </remarks>
public sealed class WindowsWordProbe : IWordProbe
{
    /// <summary>The ProgID whose presence means "Word is installed".</summary>
    private const string ProgId = "Word.Application";

    public Task<WordInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Detect());
    }

    private static WordInfo Detect()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return WordInfo.Missing;
        }

        try
        {
            using var progIdKey = Registry.ClassesRoot.OpenSubKey(ProgId);
            if (progIdKey is null)
            {
                return WordInfo.Missing;
            }

            using var curVer = Registry.ClassesRoot.OpenSubKey($@"{ProgId}\CurVer");
            var versionProgId = curVer?.GetValue(null) as string;
            var version = DescribeVersion(versionProgId);
            if (version is not null)
            {
                return new WordInfo(true, version);
            }

            using var clsid = Registry.ClassesRoot.OpenSubKey($@"{ProgId}\CLSID");
            return clsid is null ? WordInfo.Missing : new WordInfo(true, null);
        }
        catch (Exception)
        {
            // A registry read that fails is indistinguishable, for the user, from Word not being
            // there: either way the card says Word is unavailable and printing falls back.
            return WordInfo.Missing;
        }
    }

    /// <summary>
    /// Turns <c>Word.Application.16</c> into the Office release people recognise. An unknown or
    /// unparseable suffix returns <c>null</c>, so the card says "متوفر" rather than showing a
    /// number that means nothing (AGREEMENT item 15).
    /// </summary>
    internal static string? DescribeVersion(string? versionProgId)
    {
        if (string.IsNullOrWhiteSpace(versionProgId))
        {
            return null;
        }

        var lastDot = versionProgId.LastIndexOf('.');
        if (lastDot < 0 || lastDot == versionProgId.Length - 1)
        {
            return null;
        }

        var suffix = versionProgId[(lastDot + 1)..];
        if (!int.TryParse(suffix, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var major))
        {
            return null;
        }

        return major switch
        {
            >= 16 => "Office 2016 أو أحدث",
            15 => "Office 2013",
            14 => "Office 2010",
            12 => "Office 2007",
            _ => null,
        };
    }
}
