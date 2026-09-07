using System.Globalization;
using Avalonia.Data.Converters;

namespace Pixelscreens.Controls;

/// <summary>Two-way int-to-bool converter for radio-button navigation bound to a selected index.</summary>
public static class TabConverters
{
    public static readonly IValueConverter Is = new IndexIsConverter();

    private sealed class IndexIsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is int i && int.TryParse(parameter?.ToString(), out var p) && i == p;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true && int.TryParse(parameter?.ToString(), out var p)) return p;
            return Avalonia.Data.BindingOperations.DoNothing;
        }
    }
}
