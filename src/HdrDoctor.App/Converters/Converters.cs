using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace HdrDoctor.App.Converters;

/// <summary>
/// Turns a severity brush key into the brush the current theme defines for it.
/// </summary>
/// <remarks>
/// Resolved through the resource system rather than hard-coded so the palette
/// follows light and dark mode.
/// </remarks>
public sealed class ResourceKeyToBrushConverter : IValueConverter
{
    public static ResourceKeyToBrushConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key)
        {
            return Brushes.Gray;
        }

        var app = Avalonia.Application.Current;
        if (app is null)
        {
            return Brushes.Gray;
        }

        var theme = app.ActualThemeVariant;
        return app.TryGetResource(key, theme, out var resource) && resource is IBrush brush
            ? brush
            : Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
