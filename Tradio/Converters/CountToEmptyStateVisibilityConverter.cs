using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Trdo.Converters;

/// <summary>
/// Converts a count to Visibility. Returns Visible when count is 0, Collapsed otherwise.
/// </summary>
public class CountToEmptyStateVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is int count)
        {
            return count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
