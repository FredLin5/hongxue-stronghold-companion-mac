using System.Text.Json;
using SkiaSharp;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>Matches the theme-specific shop control, independently of operator names and card colours.</summary>
public sealed class GameShopThemeDetector
{
    private readonly (GameShopTheme Theme, byte[] Pixels)[] _references;

    public GameShopThemeDetector(string referencePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(referencePath));
        var references = document.RootElement.GetProperty("references");
        _references = Enum.GetValues<GameShopTheme>()
            .Select(theme => (theme, references.GetProperty(theme.ToString()).GetBytesFromBase64())).ToArray();
        if (_references.Any(reference => reference.Pixels.Length != 24 * 40))
            throw new InvalidDataException("模式主题参考图尺寸无效");
    }

    /// <summary>Per-theme match distance (lower is better) for diagnostics and threshold tuning.</summary>
    public IReadOnlyList<(GameShopTheme Theme, double Score)> Score(byte[] screenshot)
    {
        using var image = Raster.Decode(screenshot);
        if (image is null) return [];
        var scores = _references.Select(reference => (reference.Theme, Score: double.PositiveInfinity)).ToArray();
        foreach (var scale in Scales)
        {
            var pixels = Fingerprint(image, scale);
            for (int theme = 0; theme < _references.Length; theme++)
            {
                int difference = 0;
                for (int i = 0; i < pixels.Length; i++)
                    difference += Math.Abs(pixels[i] - _references[theme].Pixels[i]);
                scores[theme].Score = Math.Min(scores[theme].Score, (double)difference / pixels.Length);
            }
        }
        return scores.OrderBy(s => s.Score).ToList();
    }

    /// <summary>UI scaling factors the control may be rendered at.</summary>
    private static readonly double[] Scales = [1.0, 0.85, 1.1, 1.2, 1.3, 0.75, 1.4, 1.5, 1.65, 1.75, 1.9, 2.0, 2.2];

    /// <param name="maxScore">
    /// Absolute match distance a theme must stay under. The Windows build keeps 22 (1080p references);
    /// the macOS PlayCover path passes a looser value because 4K frames downscaled to 1080p blur the
    /// fingerprint (measured: correct theme ≈25, runner-up ≈39, gap still wide).
    /// </param>
    public GameShopTheme? Detect(byte[] screenshot, double maxScore = 22)
    {
        using var image = Raster.Decode(screenshot);
        if (image is null) return null;
        var scores = _references.Select(reference => (reference.Theme, Score: double.PositiveInfinity)).ToArray();
        // UI scaling changes the size of the control, while its bottom-right anchor stays fixed.
        foreach (var scale in Scales)
        {
            var pixels = Fingerprint(image, scale);
            for (int theme = 0; theme < _references.Length; theme++)
            {
                int difference = 0;
                for (int i = 0; i < pixels.Length; i++)
                    difference += Math.Abs(pixels[i] - _references[theme].Pixels[i]);
                scores[theme].Score = Math.Min(scores[theme].Score, (double)difference / pixels.Length);
            }
        }
        var ranked = scores.OrderBy(score => score.Score).ToArray();
        return ranked[0].Score <= maxScore && ranked[1].Score - ranked[0].Score >= 8 ? ranked[0].Theme : null;
    }

    private static byte[] Fingerprint(Raster image, double scale)
    {
        using var sample = Raster.Create(24, 40);
        int left = (int)(image.Width - image.Width * 0.12 * scale);
        int top = (int)(image.Height - image.Height * 0.3 * scale);
        sample.Draw(image, new SKRect(0, 0, 24, 40),
            new SKRect(left, top, image.Width, image.Height));

        var luminance = new double[24 * 40];
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 24; x++)
            {
                var colour = sample.GetPixel(x, y);
                luminance[y * 24 + x] = (colour.R * 299 + colour.G * 587 + colour.B * 114) / 1000.0;
            }
        double mean = luminance.Average();
        double deviation = Math.Max(1, Math.Sqrt(luminance.Average(value => (value - mean) * (value - mean))));
        return luminance.Select(value => (byte)Math.Clamp(Math.Round((value - mean) / deviation * 40 + 128), 0, 255)).ToArray();
    }
}
