using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace UMapx.Video.Windows.Tests;

internal static class VideoTestImages
{
    internal static Bitmap Pattern(int width, int height, bool alpha)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            bitmap.SetPixel(x, y, Color.FromArgb(alpha ? 40 + (17 * x + 23 * y) % 216 : 255,
                (47 * x + 13 * y) % 256, (11 * x + 53 * y) % 256, (89 * x + 7 * y) % 256));
        return bitmap;
    }

    internal static void Pixel(Color expected, Color actual, int tolerance, bool alpha)
    {
        if (alpha) Assert.InRange(Math.Abs(expected.A - actual.A), 0, tolerance);
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance &&
            Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"Expected RGBA {expected.R},{expected.G},{expected.B},{expected.A}; " +
            $"actual {actual.R},{actual.G},{actual.B},{actual.A}; tolerance {tolerance}.");
    }
}
