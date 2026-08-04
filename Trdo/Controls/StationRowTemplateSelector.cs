using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Trdo.Models;

namespace Trdo.Controls;

/// <summary>
/// Chooses the template for a station list row. The list holds the model objects themselves,
/// so the row kind is simply the item's type.
/// </summary>
public sealed partial class StationRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? StationTemplate { get; set; }
    public DataTemplate? GroupTemplate { get; set; }
    public DataTemplate? DividerTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        StationGroup => GroupTemplate,
        StationDivider => DividerTemplate,
        _ => StationTemplate
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}
