using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using Windows.Foundation;

namespace Trdo.Controls;

/// <summary>
/// Lays children out left to right, wrapping to a new row when the next child will not fit.
/// <para>
/// WinUI ships no wrapping panel, and the alternatives do not fit: <c>UniformGridLayout</c>
/// sizes every cell alike, which is wrong for filter chips whose width follows their text, and a
/// horizontal StackPanel would run a long filter set off the side of a 320px window.
/// </para>
/// </summary>
public sealed partial class WrapPanel : Panel
{
    /// <summary>Horizontal gap between children on the same row.</summary>
    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    /// <summary>Vertical gap between rows.</summary>
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing),
        typeof(double),
        typeof(WrapPanel),
        new PropertyMetadata(0d, OnLayoutPropertyChanged));

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((WrapPanel)d).InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // An unconstrained width would put everything on one row, which is never what a wrap
        // panel is for; treat it as "as narrow as the widest child".
        double lineLimit = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;

        double lineWidth = 0;
        double lineHeight = 0;
        double totalWidth = 0;
        double totalHeight = 0;

        foreach (UIElement child in Children)
        {
            child.Measure(new Size(lineLimit, double.PositiveInfinity));
            Size desired = child.DesiredSize;

            double advance = lineWidth == 0 ? desired.Width : lineWidth + ColumnSpacing + desired.Width;

            if (advance > lineLimit && lineWidth > 0)
            {
                // Close the current row and start the child on the next one.
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + RowSpacing;
                lineWidth = desired.Width;
                lineHeight = desired.Height;
                continue;
            }

            lineWidth = advance;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        double y = 0;
        double lineHeight = 0;

        foreach (UIElement child in Children)
        {
            Size desired = child.DesiredSize;

            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + RowSpacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));

            x += desired.Width + ColumnSpacing;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        return finalSize;
    }
}
