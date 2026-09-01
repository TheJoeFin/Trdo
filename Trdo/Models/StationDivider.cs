using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Trdo.Models;

/// <summary>
/// A break in the station list: a thin rule, optionally with a caption.
/// <para>
/// Purely positional - it marks a boundary between the stations above and below it and holds
/// nothing itself. That is also why it disappears under a view sort: once the user is not the
/// one deciding the order, a divider has nothing left to mean.
/// </para>
/// </summary>
public sealed partial class StationDivider : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string? _label;
    private string? _groupId;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stable identifier, used to reference this divider from the layout file.</summary>
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

    /// <summary>
    /// The caption shown in the middle of the rule, or <c>null</c> for a bare line. Blank and
    /// null mean the same thing; both render as a plain rule.
    /// </summary>
    public string? Label
    {
        get => _label;
        set
        {
            string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized == _label) return;
            _label = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasLabel));
        }
    }

    /// <summary>True when this divider has a caption to draw.</summary>
    [JsonIgnore]
    public bool HasLabel => !string.IsNullOrEmpty(_label);

    /// <summary>
    /// The id of the group this divider sits in, or <c>null</c> at the top level. View state,
    /// set when the display list is built; the layout file is the authority.
    /// </summary>
    [JsonIgnore]
    public string? GroupId
    {
        get => _groupId;
        set
        {
            if (value == _groupId) return;
            _groupId = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
