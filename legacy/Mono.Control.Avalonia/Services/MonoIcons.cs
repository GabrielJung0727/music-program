using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace Mono.Control.Services;

/// <summary>avares SVG → SvgImage. stem 예: nav-home, transport-play.</summary>
public static class MonoIcons
{
    private static readonly ConcurrentDictionary<string, IImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsDark => Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;

    public static string UiUri(string stem, bool? dark = null)
    {
        var isDark = dark ?? IsDark;
        return isDark
            ? $"avares://Mono.Control/Assets/icons/dark/ui/{stem}.svg"
            : $"avares://Mono.Control/Assets/icons/ui/{stem}.svg";
    }

    public static string GenreUri(string stem, bool? dark = null)
    {
        var isDark = dark ?? IsDark;
        return isDark
            ? $"avares://Mono.Control/Assets/icons/dark/genres/{stem}.svg"
            : $"avares://Mono.Control/Assets/icons/genres/{stem}.svg";
    }

    public static IImage? Ui(string stem) => Get(UiUri(stem)) ?? Get(UiUri(stem, false));

    public static IImage? Genre(string stem)
    {
        var key = stem.StartsWith("genre-", StringComparison.Ordinal) ? stem : "genre-" + stem;
        return Get(GenreUri(key)) ?? Get(UiUri(key)) ?? Get(GenreUri(key, false)) ?? Get(UiUri(key, false));
    }

    public static void ClearCache() => Cache.Clear();

    public static IImage? Get(string avaresUri)
    {
        return Cache.GetOrAdd(avaresUri, uri =>
        {
            try
            {
                var src = SvgSource.Load(uri, baseUri: null);
                return src is null ? null : new SvgImage { Source = src };
            }
            catch
            {
                return null;
            }
        });
    }
}

public sealed class SvgAssetConverter : IValueConverter
{
    public static readonly SvgAssetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string stem || string.IsNullOrWhiteSpace(stem)) return null;
        var folder = parameter as string ?? "ui";
        return folder == "genres" ? MonoIcons.Genre(stem) : MonoIcons.Ui(stem);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
