using AlphaTab;
using AlphaSkia;
using AlphaTab.Importer;
using AlphaTab.Rendering;

namespace ThermalMusicPrinter;

internal sealed record RenderedScore(
    byte[] Png,
    byte[] Pixels,
    int Width,
    int Height,
    AlphaSkiaColorType PixelFormat);

internal static class MusicScoreRenderer
{
    public static RenderedScore Render(string inputPath, int width, int rotateRows = 0)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Score file was not found.", inputPath);
        }

        var settings = new Settings();
        settings.Core.Engine = "skia";
        if (settings.Display.Resources.ElementFonts.Get(NotationElement.ScoreTitle) is { } titleFont)
        {
            settings.Display.Resources.ElementFonts.Set(NotationElement.ScoreTitle, titleFont.WithSize(16));
        }
        var score = Path.GetExtension(inputPath).Equals(".alphatex", StringComparison.OrdinalIgnoreCase)
            ? ScoreLoader.LoadAlphaTex(File.ReadAllText(inputPath), settings)
            : ScoreLoader.LoadScoreFromBytes(File.ReadAllBytes(inputPath), settings);
        if (score.Tracks.Count == 0)
        {
            throw new InvalidDataException("The score contains no tracks.");
        }
        if (rotateRows > 0)
        {
            settings.Display.LayoutMode = LayoutMode.Horizontal;
            foreach (var staff in score.Tracks.SelectMany(track => track.Staves))
            {
                staff.ShowStandardNotation = true;
                staff.ShowTablature = rotateRows == 2;
            }
        }


        var renderer = new ScoreRenderer(settings) { Width = width };
        var partialImages = new List<RenderFinishedEventArgs>();
        var totalWidth = 0;
        var totalHeight = 0;

        renderer.PartialLayoutFinished.On(result => renderer.RenderResult(result.Id));
        renderer.PartialRenderFinished.On(result => partialImages.Add(result));
        renderer.RenderFinished.On(result =>
        {
            totalWidth = (int)Math.Ceiling(result.TotalWidth);
            totalHeight = (int)Math.Ceiling(result.TotalHeight);
        });

        var trackIndices = score.Tracks.Select(track => (double)track.Index).ToList();
        renderer.RenderScore(score, trackIndices);

        if (totalWidth <= 0 || totalHeight <= 0 || partialImages.Count == 0)
        {
            throw new InvalidDataException($"alphaTab produced no score image (tracks={score.Tracks.Count}, images={partialImages.Count}, size={totalWidth}×{totalHeight}).");
        }

        using var canvas = new AlphaSkiaCanvas();
        canvas.BeginRender(totalWidth, totalHeight);
        canvas.Color = AlphaSkiaCanvas.RgbaToColor(255, 255, 255, 255);
        canvas.FillRect(0, 0, totalWidth, totalHeight);

        foreach (var result in partialImages)
        {
            if (result.RenderResult is not AlphaTab.Platform.Skia.AlphaSkiaBridge.AlphaSkiaImage image)
            {
                throw new InvalidDataException("alphaTab returned an unsupported image type.");
            }

            canvas.DrawImage(
                image.Image,
                (float)result.X,
                (float)result.Y,
                (float)result.Width,
                (float)result.Height);
        }

        using var fullImage = canvas.EndRender()
            ?? throw new InvalidDataException("alphaSkia could not combine the rendered score image.");

        var png = fullImage.ToPng()
            ?? throw new InvalidDataException("alphaSkia could not encode the rendered score as PNG.");
        var pixels = fullImage.ReadPixels()
            ?? throw new InvalidDataException("alphaSkia could not read the rendered score pixels.");

        var scoreImage = new RenderedScore(png, pixels, fullImage.Width, fullImage.Height, AlphaSkiaCanvas.ColorType);
        return rotateRows == 0 ? scoreImage : RotateForRoll(scoreImage, width);
    }
    private static RenderedScore RotateForRoll(RenderedScore source, int paperWidth)
    {
        var scale = paperWidth / (double)source.Height;
        var outputHeight = (int)Math.Ceiling(source.Width * scale);
        if (outputHeight > ushort.MaxValue)
        {
            throw new InvalidDataException("Rotated score exceeds the MXW01 65535-row limit.");
        }

        var outputPixels = new byte[paperWidth * outputHeight * 4];
        for (var outputY = 0; outputY < outputHeight; outputY++)
        {
            var sourceX = Math.Min(source.Width - 1, (int)(outputY / scale));
            for (var outputX = 0; outputX < paperWidth; outputX++)
            {
                var sourceY = source.Height - 1 - Math.Min(source.Height - 1, (int)(outputX / scale));
                var sourceOffset = (sourceY * source.Width + sourceX) * 4;
                var outputOffset = (outputY * paperWidth + outputX) * 4;
                Buffer.BlockCopy(source.Pixels, sourceOffset, outputPixels, outputOffset, 4);
            }
        }

        using var image = AlphaSkiaImage.FromPixels(paperWidth, outputHeight, outputPixels)
            ?? throw new InvalidDataException("alphaSkia could not create the rotated score image.");
        var png = image.ToPng()
            ?? throw new InvalidDataException("alphaSkia could not encode the rotated score as PNG.");
        return new RenderedScore(png, outputPixels, paperWidth, outputHeight, source.PixelFormat);
    }
}
