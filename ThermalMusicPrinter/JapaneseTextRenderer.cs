using System.Text;
using AlphaSkia;

namespace ThermalMusicPrinter;

internal static class JapaneseTextRenderer
{
    private const float FontSize = 28;
    private const float LineSpacing = 48;
    private const int HorizontalPadding = 32;
    private const int VerticalPadding = 32;

    public static RenderedScore Render(string inputPath, int width)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Text file was not found.", inputPath);
        }

        var lines = File.ReadAllLines(inputPath, Encoding.UTF8);
        if (lines.Length == 0 || lines.All(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Text file contains no printable lines.");
        }

        AlphaSkiaCanvas.SwitchToOperatingSystemFonts();
        using var textStyle = new AlphaSkiaTextStyle(
            ["Hiragino Mincho ProN", "Hiragino Sans", "Arial Unicode MS"],
            400,
            false);
        var height = VerticalPadding * 2 + (int)Math.Ceiling(lines.Length * LineSpacing);

        using var canvas = new AlphaSkiaCanvas();
        canvas.BeginRender(width, height);
        canvas.Color = AlphaSkiaCanvas.RgbaToColor(255, 255, 255, 255);
        canvas.FillRect(0, 0, width, height);
        canvas.Color = AlphaSkiaCanvas.RgbaToColor(0, 0, 0, 255);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            using var metrics = canvas.MeasureText(
                line,
                textStyle,
                FontSize,
                AlphaSkiaTextAlign.Center,
                AlphaSkiaTextBaseline.Top);
            if (metrics.Width > width - HorizontalPadding * 2)
            {
                throw new InvalidDataException($"Text line {index + 1} is too wide for {width} pixels at {FontSize}px.");
            }

            canvas.FillText(
                line,
                textStyle,
                FontSize,
                width / 2f,
                VerticalPadding + index * LineSpacing,
                AlphaSkiaTextAlign.Center,
                AlphaSkiaTextBaseline.Top);
        }

        using var image = canvas.EndRender()
            ?? throw new InvalidDataException("alphaSkia could not render the text image.");
        var png = image.ToPng()
            ?? throw new InvalidDataException("alphaSkia could not encode the text preview as PNG.");
        var pixels = image.ReadPixels()
            ?? throw new InvalidDataException("alphaSkia could not read the text image pixels.");
        return new RenderedScore(png, pixels, image.Width, image.Height, AlphaSkiaCanvas.ColorType);
    }
}
