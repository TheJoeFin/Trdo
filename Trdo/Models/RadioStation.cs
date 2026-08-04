using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Trdo.Models;

public partial class RadioStation : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _streamUrl = string.Empty;
    private string? _homepage;
    private string? _faviconUrl;
    // Default to 100% so stations loaded from older data (no volume field) are not silent.
    private double _volume = 1.0;
    private double? _bufferLevel;
    private double? _songPopupDelaySeconds;
    private string? _stationUuid;
    private string? _tags;
    private string? _country;
    private string? _countryCode;
    private string? _language;
    private string? _codec;
    private int? _bitrate;
    private DateTimeOffset? _dateAdded;
    private DateTimeOffset? _metadataRefreshedUtc;
    private string? _groupId;
    private bool _isSelectedStation;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Stable identifier for this station. The layout file and the saved selection both
    /// reference stations by this rather than by list position, which is what lets folders,
    /// collapsing and view sorts move a station around without losing track of it.
    /// <para>
    /// Deliberately <em>not</em> initialised at construction: an empty value is how
    /// <see cref="Services.StationIdentityPolicy"/> recognises a station loaded from a
    /// pre-2.0 file - or from a build that dropped the field - and needing a fresh id
    /// stamped and persisted.
    /// </para>
    /// </summary>
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

    public required string Name
    {
        get => _name;
        set
        {
            if (value == _name) return;
            _name = value;
            OnPropertyChanged();
        }
    }

    public required string StreamUrl
    {
        get => _streamUrl;
        set
        {
            if (value == _streamUrl) return;
            _streamUrl = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The homepage URL of the radio station, if available.
    /// </summary>
    public string? Homepage
    {
        get => _homepage;
        set
        {
            if (value == _homepage) return;
            _homepage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The URL to the station's favicon/logo image, if available.
    /// </summary>
    public string? FaviconUrl
    {
        get => _faviconUrl;
        set
        {
            if (value == _faviconUrl) return;
            _faviconUrl = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Per-station playback volume as a fraction where 1.0 == 100% of the stream
    /// volume. Values above 1.0 (up to 2.0) amplify the stream on the LibVLC engine.
    /// </summary>
    public double Volume
    {
        get => _volume;
        set
        {
            value = System.Math.Clamp(value, 0, 2);
            if (value == _volume) return;
            _volume = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Per-station override for the buffer level (0-3), or <c>null</c> to follow
    /// the app-wide buffer setting. Stations that stream fine at the default do
    /// not need to pay for a station that only behaves with a large buffer.
    /// <para>
    /// This is a floor, exactly like the global setting: the watchdog's transient
    /// auto-bump still stacks on top when it detects stutter. Stations saved
    /// before this existed have no value and so follow the global setting.
    /// </para>
    /// </summary>
    public double? BufferLevel
    {
        get => _bufferLevel;
        set
        {
            double? clamped = value is null ? null : System.Math.Clamp(value.Value, 0, 3);
            if (clamped == _bufferLevel) return;
            _bufferLevel = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Per-station override for how long to wait after a metadata change before showing
    /// the song change popup, in seconds, or <c>null</c> to follow the app-wide setting.
    /// <para>
    /// Stations differ in how far ahead of the audio their encoder announces a track, so
    /// this is a per-station property in practice: a delay that lines the popup up on one
    /// station makes it late on another. Stations saved before this existed have no value
    /// and so follow the global setting.
    /// </para>
    /// </summary>
    public double? SongPopupDelaySeconds
    {
        get => _songPopupDelaySeconds;
        set
        {
            double? clamped = value is null
                ? null
                : Services.SongChangeAnnouncementPolicy.ClampDelay(value.Value);
            if (clamped == _songPopupDelaySeconds) return;
            _songPopupDelaySeconds = clamped;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The Radio Browser station uuid this station was added from, or <c>null</c> for a
    /// manually entered or imported station. Kept as the join key for a later refresh.
    /// </summary>
    public string? StationUuid
    {
        get => _stationUuid;
        set
        {
            if (value == _stationUuid) return;
            _stationUuid = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The station's genre tags as Radio Browser returns them: a raw comma-separated
    /// string, stored verbatim rather than split, so nothing is lost if their formatting
    /// changes. Use <see cref="TagList"/> or <see cref="PrimaryGenre"/> to read it.
    /// </summary>
    public string? Tags
    {
        get => _tags;
        set
        {
            if (value == _tags) return;
            _tags = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TagList));
            OnPropertyChanged(nameof(PrimaryGenre));
        }
    }

    /// <summary>The station's country, if known. Used for grouping and sorting.</summary>
    public string? Country
    {
        get => _country;
        set
        {
            if (value == _country) return;
            _country = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The ISO country code, if known.</summary>
    public string? CountryCode
    {
        get => _countryCode;
        set
        {
            if (value == _countryCode) return;
            _countryCode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The station's broadcast language, if known.</summary>
    public string? Language
    {
        get => _language;
        set
        {
            if (value == _language) return;
            _language = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The stream's audio codec as reported by the directory, if known.</summary>
    public string? Codec
    {
        get => _codec;
        set
        {
            if (value == _codec) return;
            _codec = value;
            OnPropertyChanged();
        }
    }

    /// <summary>The stream's bitrate in kbps as reported by the directory, if known.</summary>
    public int? Bitrate
    {
        get => _bitrate;
        set
        {
            if (value == _bitrate) return;
            _bitrate = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// When the user added this station, or <c>null</c> for stations saved before this
    /// existed.
    /// <para>
    /// This is stamped locally and cannot come from the directory: Radio Browser's
    /// <c>lastchangetime</c> describes when <em>their</em> record changed, which has
    /// nothing to do with when this user added the station. Stations with no value sort
    /// last under "Recently added" and, because the sort is stable, keep their manual
    /// order among themselves - a reasonable stand-in for "oldest first".
    /// </para>
    /// </summary>
    public DateTimeOffset? DateAdded
    {
        get => _dateAdded;
        set
        {
            if (value == _dateAdded) return;
            _dateAdded = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// When this station's details were last looked up from the directory, or <c>null</c>
    /// if never. Lets a batch refresh skip entries that are already current instead of
    /// re-requesting the whole list.
    /// </summary>
    public DateTimeOffset? MetadataRefreshedUtc
    {
        get => _metadataRefreshedUtc;
        set
        {
            if (value == _metadataRefreshedUtc) return;
            _metadataRefreshedUtc = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The id of the group this station currently sits in, or <c>null</c> when it is at the
    /// top level.
    /// <para>
    /// View state only, and deliberately not serialised: the authoritative record of
    /// grouping lives in the layout file, because <c>stations.json</c> has to stay readable
    /// by pre-2.0 builds. Set by <see cref="Services.StationLayoutPolicy"/> when the
    /// display list is built.
    /// </para>
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

    /// <summary>
    /// True when this is the station the player is pointed at. Bound directly by the list
    /// row so the selection highlight survives virtualisation, collapsing and sorting.
    /// View state only, not serialised.
    /// </summary>
    [JsonIgnore]
    public bool IsSelectedStation
    {
        get => _isSelectedStation;
        set
        {
            if (value == _isSelectedStation) return;
            _isSelectedStation = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// The station's tags split and trimmed, or an empty list when it has none.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> TagList
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_tags))
                return [];

            List<string> parsed = [];
            foreach (string part in _tags.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    parsed.Add(trimmed);
            }
            return parsed;
        }
    }

    /// <summary>
    /// The first tag, used as the station's genre for grouping and sorting, or <c>null</c>
    /// when it has no tags. Radio Browser lists the most representative tag first.
    /// </summary>
    [JsonIgnore]
    public string? PrimaryGenre
    {
        get
        {
            IReadOnlyList<string> tags = TagList;
            return tags.Count > 0 ? tags[0] : null;
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
