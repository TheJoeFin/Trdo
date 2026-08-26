namespace Trdo.Models;

/// <summary>Which quality/order selection a <see cref="QualityFilterChip"/> represents.</summary>
public enum QualityChipKind
{
    Codec,
    Bitrate,
    Sort,
    HideBroken
}

/// <summary>
/// A removable chip for a codec, bitrate, sort or hide-broken pick — the fixed-list counterpart
/// to <see cref="StationFilterOption"/>. <see cref="Kind"/> tells the view model which selection
/// to reset when the chip is removed.
/// </summary>
public sealed record QualityFilterChip(QualityChipKind Kind, string ChipLabel) : IStationFilterChip;
