using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Trdo.Converters;

/// <summary>
/// Converts a boolean value to Visibility. Returns Visible when true, Collapsed when false.
/// Use ConverterParameter="Invert" to invert the logic.
/// </summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            // Check if we should invert the result
            bool invert = parameter is string paramString && 
                          paramString.Equals("Invert", StringComparison.OrdinalIgnoreCase);
            
            if (invert)
            {
                boolValue = !boolValue;
            }
            
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
