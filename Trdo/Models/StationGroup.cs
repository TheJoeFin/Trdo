using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Trdo.Models;

/// <summary>
/// A collapsible folder of stations.
/// <para>
/// Groups hold stations and dividers but never other groups. One level is enough for
/// organising a station list, and it keeps the whole feature tractable: the display is a
/// depth-first walk of a two-level tree, drag-and-drop reparenting is a single top-to-bottom
/// scan, and "a folder inside a folder inside a folder" simply cannot be reached.
/// </para>
/// </summary>
public sealed partial class StationGroup : INotifyPropertyChanged
{
    /// <summary>Segoe Fluent chevron pointing down (expanded).</summary>
    private const string ExpandedGlyph = "";

    /// <summary>Segoe Fluent chevron pointing right (collapsed).</summary>
    private const string CollapsedGlyph = "";

    private string _id = string.Empty;
    private string _name = string.Empty;
    private bool _isExpanded = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable identifier, used to reference this group from the layout file.</summary>
    public string Id
    {
        get => _id;
        set
        {
            if (value == _id) return;
            _id = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The folder's name, as shown on its header row.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (value == _name) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the folder's contents are shown. Persisted, so a folder the user collapsed
    /// stays collapsed across restarts.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (value == _isExpanded) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChevronGlyph));
        }
    }

    /// <summary>
    /// The folder's contents in display order: <see cref="RadioStation"/> and
    /// <see cref="StationDivider"/> instances, never another <see cref="StationGroup"/>.
    /// </summary>
    [JsonIgnore]
    public List<object> Children { get; } = [];

    /// <summary>
    /// True for a folder synthesized by "Group by" rather than one the user created. A virtual
    /// folder exists only in memory for the current view: expanding or collapsing it is never
    /// written to the layout file, and it never appears in the "move to group" menu.
    /// </summary>
    [JsonIgnore]
    public bool IsVirtual { get; init; }

    /// <summary>The chevron to draw on the header row.</summary>
    [JsonIgnore]
    public string ChevronGlyph => _isExpanded ? ExpandedGlyph : CollapsedGlyph;

    /// <summary>
    /// How many stations the folder holds, for the header row's count badge. Dividers are not
    /// counted - the number is meant to answer "how much is hidden in here".
    /// </summary>
    [JsonIgnore]
    public int StationCount
    {
        get
        {
            int count = 0;
            foreach (object child in Children)
            {
                if (child is RadioStation)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Raises change notification for the computed members that depend on
    /// <see cref="Children"/>. Called after the contents are rebuilt, since a plain
    /// <see cref="List{T}"/> cannot announce its own changes.
    /// </summary>
    public void NotifyChildrenChanged()
    {
        OnPropertyChanged(nameof(StationCount));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
