using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace ACadInspector.Converters;

public sealed class IconResourceKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var application = Application.Current;
        if (application is null)
        {
            return null;
        }

        if (application.TryGetResource(key, application.ActualThemeVariant, out var resource) &&
            resource is Geometry geometry)
        {
            return geometry;
        }

        if (application.TryGetResource(key, ThemeVariant.Light, out resource) &&
            resource is Geometry lightGeometry)
        {
            return lightGeometry;
        }

        if (application.TryGetResource(key, ThemeVariant.Dark, out resource) &&
            resource is Geometry darkGeometry)
        {
            return darkGeometry;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
