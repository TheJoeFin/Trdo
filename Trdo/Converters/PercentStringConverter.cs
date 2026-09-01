using Microsoft.UI.Xaml.Data;
using System;

namespace Trdo.Converters;

/// <summary>
/// Converts a numeric value (already expressed as a percentage) to a display
/// string such as "100%". Used for the volume slider thumb tooltip and label.
/// </summary>
public partial class PercentStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double number = value switch
        {
            double d => d,
            int i => i,
            _ => 0
        };

        return $"{Math.Round(number)}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
