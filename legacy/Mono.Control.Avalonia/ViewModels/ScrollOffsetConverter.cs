using Avalonia;
using Avalonia.Data.Converters;
using System.Globalization;

namespace Mono.Control.ViewModels;

/// <summary>ScrollViewer.Offset(Vector) ↔ 세로 Y(double).</summary>
public sealed class ScrollOffsetConverter : IValueConverter
{
    public static readonly ScrollOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double y)
            return new Vector(0, y);
        return new Vector(0, 0);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Vector v)
            return v.Y;
        return 0d;
    }
}
