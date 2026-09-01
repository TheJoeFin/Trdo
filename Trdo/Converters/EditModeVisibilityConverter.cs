using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Trdo.Converters;

/// <summary>
/// Converts a string to Visibility for edit mode detection.
/// Returns Collapsed when the title contains "Edit", Visible otherwise.
/// </summary>
public partial class EditModeVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string title)
        {
            return title.Contains("Edit", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
