using AlphaSkia;

namespace ThermalMusicPrinter;

internal static class MonochromeRasterEncoder
{
    private const int BlackThreshold = 192;

    public static byte[] Encode(RenderedScore score)
    {
        if (score.Width <= 0 || score.Width % 8 != 0 || score.Height <= 0 || score.Pixels.Length != score.Width * score.Height * 4)
        {
            throw new InvalidDataException("The rendered score has invalid pixel dimensions.");
        }

        var rowBytes = score.Width / 8;
        var raster = new byte[rowBytes * score.Height];
        for (var y = 0; y < score.Height; y++)
        {
            for (var x = 0; x < score.Width; x++)
            {
                var sourceIndex = (y * score.Width + x) * 4;
                var first = score.Pixels[sourceIndex];
                var green = (int)score.Pixels[sourceIndex + 1];
                var third = score.Pixels[sourceIndex + 2];
                var alpha = score.Pixels[sourceIndex + 3];

                var red = (int)(score.PixelFormat == AlphaSkiaColorType.Bgra888 ? third : first);
                var blue = (int)(score.PixelFormat == AlphaSkiaColorType.Bgra888 ? first : third);
                red = CompositeOnWhite(red, alpha);
                green = CompositeOnWhite(green, alpha);
                blue = CompositeOnWhite(blue, alpha);

                var luminance = (red * 299 + green * 587 + blue * 114) / 1000;
                if (luminance < BlackThreshold)
                {
                    // MXW01 packs pixels left-to-right, least-significant bit first.
                    raster[y * rowBytes + x / 8] |= (byte)(1 << (x % 8));
                }
            }
        }

        return raster;
    }

    private static int CompositeOnWhite(int color, byte alpha) =>
        (color * alpha + 255 * (255 - alpha) + 127) / 255;
}
