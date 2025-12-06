using Microsoft.UI.Xaml.Data;
using System;

namespace Trdo.Converters;

/// <summary>
/// Converts a boolean favorite status to the appropriate star glyph.
/// </summary>
public class BooleanToFavoriteGlyphConverter : IValueConverter
{
    /// <summary>
    /// Filled star glyph (favorited).
    /// </summary>
    private const string FilledStar = "\uE735";

    /// <summary>
    /// Outline star glyph (not favorited).
    /// </summary>
    private const string OutlineStar = "\uE734";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool isFavorited)
        {
            return isFavorited ? FilledStar : OutlineStar;
        }

        return OutlineStar;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
