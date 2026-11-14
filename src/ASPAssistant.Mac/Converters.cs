// Added for the macOS port on 2026-11-14; see NOTICE.
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ASPAssistant.Mac;

/// <summary>
/// Resolves a <c>data/</c>-relative icon path (e.g. <c>icons/operators/阿米娅.png</c>)
/// to a cached bitmap.
/// </summary>
public sealed class IconPathConverter : IValueConverter
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new(StringComparer.Ordinal);
    private static string _dataDir = Path.Combine(AppContext.BaseDirectory, "data");

    public static void SetDataDirectory(string dataDir) => _dataDir = dataDir;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0) return null;
        return Cache.GetOrAdd(path, Load);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Bitmap? Load(string path)
    {
        try
        {
            var full = Path.Combine(_dataDir, path.Replace('\\', '/'));
            return File.Exists(full) ? new Bitmap(full) : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>1..6 → Ⅰ..Ⅵ (falls back to the number for other tiers).</summary>
public sealed class TierRomanConverter : IValueConverter
{
    private static readonly string[] Labels = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int tier && tier >= 1 && tier <= 6 ? Labels[tier] : value?.ToString() ?? "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Joins a list of covenant names with " · ".</summary>
public sealed class JoinConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            IEnumerable<string> items => string.Join(" · ", items.Where(s => !string.IsNullOrEmpty(s))),
            _ => value?.ToString() ?? ""
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True when the current run's Ban list contains this operator name.</summary>
public sealed class BannedNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string name && AppState.Current is { } state && state.BanVm.IsBanned(name);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True for a non-zero number, used to show badges.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Maps a display name to <c>data/icons/&lt;parameter&gt;/&lt;name&gt;.png</c>
/// (parameter: "operators" or "equipment").
/// </summary>
public sealed class NameToIconConverter : IValueConverter
{
    private readonly IconPathConverter _inner = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || name.Length == 0 || parameter is not string folder) return null;
        return _inner.Convert($"icons/{folder}/{name}.png", targetType, parameter, culture);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
