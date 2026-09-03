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

    public static string UiUri(string stem) => $"avares://Mono.Control/Assets/icons/ui/{stem}.svg";
    public static string GenreUri(string stem) => $"avares://Mono.Control/Assets/icons/genres/{stem}.svg";

    public static IImage? Ui(string stem) => Get(UiUri(stem));

    public static IImage? Genre(string stem)
    {
        var key = stem.StartsWith("genre-", StringComparison.Ordinal) ? stem : "genre-" + stem;
        return Get(GenreUri(key)) ?? Get(UiUri(key));
    }

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
