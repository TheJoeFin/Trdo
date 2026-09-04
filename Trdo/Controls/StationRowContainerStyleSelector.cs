using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trdo.Models;

namespace Trdo.Controls;

/// <summary>
/// Chooses the container style for a station list row.
/// <para>
/// Folders and dividers need their own metrics - a divider in particular has to be able to
/// shrink below a normal row's height, or a break in the list ends up taller than the stations
/// it separates. The station style stays exactly as it was so an ungrouped list is unchanged.
/// </para>
/// </summary>
public sealed partial class StationRowContainerStyleSelector : StyleSelector
{
    public Style? StationStyle { get; set; }
    public Style? GroupStyle { get; set; }
    public Style? DividerStyle { get; set; }

    protected override Style? SelectStyleCore(object item, DependencyObject container) => item switch
    {
        StationGroup => GroupStyle,
        StationDivider => DividerStyle,
        _ => StationStyle
    };
}
