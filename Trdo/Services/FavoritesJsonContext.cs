using System.Collections.Generic;
using System.Text.Json.Serialization;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// JSON source generation context for FavoriteTrack storage.
/// This ensures JSON serialization works correctly even when the app is trimmed in Release mode.
/// </summary>
[JsonSerializable(typeof(List<FavoriteTrack>))]
[JsonSerializable(typeof(FavoriteTrack))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class FavoritesJsonContext : JsonSerializerContext
{
}
