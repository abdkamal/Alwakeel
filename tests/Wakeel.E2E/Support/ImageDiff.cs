using System.Drawing;
using System.Drawing.Imaging;

namespace Wakeel.E2E.Support;

/// <summary>Pixel-by-pixel PNG comparison for W08ScreenshotDiff. Uses <c>System.Drawing.Bitmap</c>
/// (available with no extra NuGet package via this project's <c>UseWindowsForms</c> — see
/// Wakeel.E2E.csproj — rather than adding an unpinned image library). No accept/reject threshold is
/// applied here; the spec for this package is to record the percentage only.</summary>
internal static class ImageDiff
{
    /// <summary>A pixel counts as "different" once the summed absolute R+G+B delta exceeds this —
    /// a small allowance for anti-aliasing/sub-pixel font rendering, not a pass/fail bar.</summary>
    private const int ChannelDeltaSumThreshold = 30;

    /// <summary>Compares <paramref name="actualPath"/> against <paramref name="expectedPath"/>
    /// (resizing the expected image to the actual's dimensions first if they differ), writes a diff
    /// image (unchanged pixels black, differing pixels magenta) to <paramref name="diffOutputPath"/>,
    /// and returns the percentage of pixels that differed.</summary>
    public static double CompareAndWriteDiff(string expectedPath, string actualPath, string diffOutputPath)
    {
        using var actual = new Bitmap(actualPath);
        using var expectedOriginal = new Bitmap(expectedPath);

        var expected = expectedOriginal;
        var ownsExpected = false;
        if (expectedOriginal.Width != actual.Width || expectedOriginal.Height != actual.Height)
        {
            expected = new Bitmap(expectedOriginal, new Size(actual.Width, actual.Height));
            ownsExpected = true;
        }

        try
        {
            using var diff = new Bitmap(actual.Width, actual.Height, PixelFormat.Format24bppRgb);
            long diffPixelCount = 0;
            long totalPixelCount = (long)actual.Width * actual.Height;

            for (var y = 0; y < actual.Height; y++)
            {
                for (var x = 0; x < actual.Width; x++)
                {
                    var a = actual.GetPixel(x, y);
                    var e = expected.GetPixel(x, y);
                    var delta = Math.Abs(a.R - e.R) + Math.Abs(a.G - e.G) + Math.Abs(a.B - e.B);

                    if (delta > ChannelDeltaSumThreshold)
                    {
                        diffPixelCount++;
                        diff.SetPixel(x, y, Color.Magenta);
                    }
                    else
                    {
                        diff.SetPixel(x, y, Color.Black);
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(diffOutputPath)!);
            diff.Save(diffOutputPath, ImageFormat.Png);

            return totalPixelCount == 0 ? 0d : diffPixelCount * 100.0 / totalPixelCount;
        }
        finally
        {
            if (ownsExpected)
            {
                expected.Dispose();
            }
        }
    }
}
