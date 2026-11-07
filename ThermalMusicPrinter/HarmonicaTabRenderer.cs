using System.Text;
using AlphaSkia;

namespace ThermalMusicPrinter;

internal static class HarmonicaTabRenderer
{
    private const int HorizontalPadding = 16;
    private const int VerticalPadding = 20;
    private const int NoteCellWidth = 86;
    private const int NoteRowHeight = 42;
    private const float TitleFontSize = 22;
    private const float NoteFontSize = 22;
    private const float LegendFontSize = 14;
    private static readonly string[] Legend = ["N = blow", "-N = draw", "'=bend 1/2"];

    public static RenderedScore Render(string inputPath, int width)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Harmonica tab file was not found.", inputPath);
        }

        var (title, sourceRows) = Parse(File.ReadAllLines(inputPath, Encoding.UTF8));
        var columnsPerRow = Math.Max(1, (width - HorizontalPadding * 2) / NoteCellWidth);
        var rows = WrapRows(sourceRows, columnsPerRow);
        var headerHeight = (title is null ? 0 : 30) + Legend.Length * 18 + 12;
        var height = VerticalPadding * 2 + headerHeight + rows.Count * NoteRowHeight;

        AlphaSkiaCanvas.SwitchToOperatingSystemFonts();
        using var titleStyle = new AlphaSkiaTextStyle(["Menlo", "Courier New", "monospace"], 600, false);
        using var noteStyle = new AlphaSkiaTextStyle(["Menlo", "Courier New", "monospace"], 500, false);
        using var legendStyle = new AlphaSkiaTextStyle(["Menlo", "Courier New", "monospace"], 400, false);
        using var canvas = new AlphaSkiaCanvas();
        canvas.BeginRender(width, height);
        canvas.Color = AlphaSkiaCanvas.RgbaToColor(255, 255, 255, 255);
        canvas.FillRect(0, 0, width, height);
        canvas.Color = AlphaSkiaCanvas.RgbaToColor(0, 0, 0, 255);

        var y = VerticalPadding;
        if (title is not null)
        {
            EnsureFits(canvas, title, titleStyle, TitleFontSize, width - HorizontalPadding * 2, "title");
            canvas.FillText(title, titleStyle, TitleFontSize, width / 2f, y, AlphaSkiaTextAlign.Center, AlphaSkiaTextBaseline.Top);
            y += 30;
        }

        foreach (var line in Legend)
        {
            EnsureFits(canvas, line, legendStyle, LegendFontSize, width - HorizontalPadding * 2, "notation legend");
            canvas.FillText(line, legendStyle, LegendFontSize, width / 2f, y, AlphaSkiaTextAlign.Center, AlphaSkiaTextBaseline.Top);
            y += 18;
        }
        y += 12;

        foreach (var row in rows)
        {
            var firstCenter = (width - row.Count * NoteCellWidth) / 2f + NoteCellWidth / 2f;
            for (var index = 0; index < row.Count; index++)
            {
                var symbol = row[index];
                EnsureFits(canvas, symbol, noteStyle, NoteFontSize, NoteCellWidth, "tab symbol");
                canvas.FillText(
                    symbol,
                    noteStyle,
                    NoteFontSize,
                    firstCenter + index * NoteCellWidth,
                    y,
                    AlphaSkiaTextAlign.Center,
                    AlphaSkiaTextBaseline.Top);
            }
            y += NoteRowHeight;
        }

        using var image = canvas.EndRender()
            ?? throw new InvalidDataException("alphaSkia could not render the harmonica tab.");
        var png = image.ToPng()
            ?? throw new InvalidDataException("alphaSkia could not encode the harmonica tab as PNG.");
        var pixels = image.ReadPixels()
            ?? throw new InvalidDataException("alphaSkia could not read the harmonica tab pixels.");
        return new RenderedScore(png, pixels, image.Width, image.Height, AlphaSkiaCanvas.ColorType);
    }

    private static (string? Title, List<List<string>> Rows) Parse(string[] lines)
    {
        string? title = null;
        var rows = new List<List<string>>();
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                if (title is not null)
                {
                    throw new InvalidDataException("A harmonica tab can contain only one '# title' line.");
                }
                title = line[2..].Trim();
                if (title.Length == 0)
                {
                    throw new InvalidDataException($"The title on line {lineIndex + 1} is empty.");
                }
                continue;
            }

            var row = new List<string>();
            foreach (var symbol in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (symbol != "|" && !IsValidSymbol(symbol))
                {
                    throw new InvalidDataException($"Invalid harmonica tab symbol '{symbol}' on line {lineIndex + 1}.");
                }
                row.Add(symbol);
            }
            if (row.Count > 0)
            {
                rows.Add(row);
            }
        }

        if (rows.Count == 0)
        {
            throw new InvalidDataException("Harmonica tab contains no notes.");
        }
        return (title, rows);
    }

    private static bool IsValidSymbol(string symbol)
    {
        var span = symbol.AsSpan();
        var index = span.Length > 0 && span[0] == '-' ? 1 : 0;
        var digitStart = index;
        while (index < span.Length && char.IsAsciiDigit(span[index]))
        {
            index++;
        }
        if (index == digitStart)
        {
            return false;
        }

        var bendCount = span.Length - index;
        if (bendCount > 3)
        {
            return false;
        }
        for (var bendIndex = index; bendIndex < span.Length; bendIndex++)
        {
            if (span[bendIndex] != '\'')
            {
                return false;
            }
        }

        var holeText = span[digitStart..index];
        if (int.TryParse(holeText, out var hole) && hole is >= 1 and <= 10)
        {
            return bendCount == 0 || holeText.Length <= 2;
        }
        if (bendCount != 0 || holeText.Length < 2)
        {
            return false;
        }

        Span<bool> seen = stackalloc bool[10];
        seen.Clear();
        foreach (var digit in holeText)
        {
            if (digit is < '1' or > '9' || seen[digit - '0'])
            {
                return false;
            }
            seen[digit - '0'] = true;
        }
        return true;
    }

    private static List<List<string>> WrapRows(List<List<string>> sourceRows, int columnsPerRow)
    {
        var result = new List<List<string>>();
        foreach (var sourceRow in sourceRows)
        {
            for (var offset = 0; offset < sourceRow.Count; offset += columnsPerRow)
            {
                result.Add(sourceRow.GetRange(offset, Math.Min(columnsPerRow, sourceRow.Count - offset)));
            }
        }
        return result;
    }

    private static void EnsureFits(
        AlphaSkiaCanvas canvas,
        string text,
        AlphaSkiaTextStyle style,
        float fontSize,
        float maximumWidth,
        string description)
    {
        using var metrics = canvas.MeasureText(text, style, fontSize, AlphaSkiaTextAlign.Center, AlphaSkiaTextBaseline.Top);
        if (metrics.Width > maximumWidth)
        {
            throw new InvalidDataException($"The {description} '{text}' is too wide to render at {fontSize}px.");
        }
    }
}
