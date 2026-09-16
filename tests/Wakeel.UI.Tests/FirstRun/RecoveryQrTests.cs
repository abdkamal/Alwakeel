using QRCoder;
using Wakeel.Crypto;
using Wakeel.UI.Services.Account;

namespace Wakeel.UI.Tests.FirstRun;

/// <summary>
/// W04 prints the recovery code as a square; W07 reads that square back out of a photograph. These
/// prove the two halves agree, with the operating system's picture decoding — which is the Windows
/// host's job and cannot run here — replaced by pixels this test draws itself.
/// </summary>
public class RecoveryQrTests
{
    [Fact]
    public void The_sheet_draws_the_code_as_a_picture_a_page_can_show()
    {
        var code = RecoveryCode.Generate();

        var url = RecoveryQr.ToDataUrl(code);

        Assert.StartsWith("data:image/png;base64,", url, StringComparison.Ordinal);
        Assert.True(RecoveryQr.ToPng(code).Length > 0);
    }

    [Fact]
    public void A_picture_of_the_printed_square_gives_the_code_back_exactly()
    {
        var code = RecoveryCode.Generate();
        var reader = new QrImageReader(new DrawnQr(code.Display));

        var read = reader.TryRead([1, 2, 3]);

        Assert.Equal(code.Display, read);
        Assert.True(RecoveryCode.TryParse(read, out var parsed));
        Assert.Equal(code.Display, parsed!.Display);
    }

    [Fact]
    public void A_picture_with_no_square_in_it_is_simply_no_code()
    {
        var reader = new QrImageReader(new BlankPage());

        Assert.Null(reader.TryRead([1, 2, 3]));
    }

    [Fact]
    public void A_host_that_cannot_read_pictures_says_so_instead_of_failing()
    {
        var reader = new QrImageReader(new NoImagePixels());

        Assert.False(reader.IsAvailable);
        Assert.Null(reader.TryRead([1, 2, 3]));
    }

    /// <summary>Stands in for the host's imaging stack: hands back the QR square already drawn.</summary>
    private sealed class DrawnQr(string text) : IImagePixels
    {
        private const int Scale = 8;
        private const int Quiet = 4;

        public bool TryDecode(ReadOnlySpan<byte> file, out byte[] rgb24, out int width, out int height)
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            var matrix = data.ModuleMatrix;
            var modules = matrix.Count;

            width = (modules + (Quiet * 2)) * Scale;
            height = width;
            rgb24 = new byte[width * height * 3];
            Array.Fill(rgb24, (byte)255);

            for (var row = 0; row < modules; row++)
            {
                for (var column = 0; column < modules; column++)
                {
                    if (!matrix[row][column])
                    {
                        continue;
                    }

                    for (var y = 0; y < Scale; y++)
                    {
                        var pixelRow = ((row + Quiet) * Scale) + y;
                        for (var x = 0; x < Scale; x++)
                        {
                            var pixelColumn = ((column + Quiet) * Scale) + x;
                            var offset = ((pixelRow * width) + pixelColumn) * 3;
                            rgb24[offset] = 0;
                            rgb24[offset + 1] = 0;
                            rgb24[offset + 2] = 0;
                        }
                    }
                }
            }

            return true;
        }
    }

    /// <summary>A readable picture with nothing in it — a photograph of the wrong page.</summary>
    private sealed class BlankPage : IImagePixels
    {
        public bool TryDecode(ReadOnlySpan<byte> file, out byte[] rgb24, out int width, out int height)
        {
            width = 120;
            height = 120;
            rgb24 = new byte[width * height * 3];
            Array.Fill(rgb24, (byte)255);
            return true;
        }
    }
}
