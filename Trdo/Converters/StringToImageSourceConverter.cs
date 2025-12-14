using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;

namespace Trdo.Converters;

/// <summary>
/// Converts a string URL to a BitmapImage, returning null for invalid or empty URLs.
/// </summary>
public class StringToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string urlString || string.IsNullOrWhiteSpace(urlString))
            return null;

        try
        {
            if (Uri.TryCreate(urlString, UriKind.Absolute, out Uri? uri))
            {
                return new BitmapImage(uri);
            }
        }
        catch
        {
            // If URI creation fails, return null
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
