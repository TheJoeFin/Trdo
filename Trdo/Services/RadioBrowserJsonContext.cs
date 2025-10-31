using System.Collections.Generic;
using System.Text.Json.Serialization;
using Trdo.Models;

namespace Trdo.Services;

/// <summary>
/// JSON source generation context for Radio Browser API responses.
/// This ensures JSON serialization works correctly even when the app is trimmed in Release mode.
/// </summary>
[JsonSerializable(typeof(List<RadioBrowserStation>))]
[JsonSerializable(typeof(RadioBrowserStation))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal partial class RadioBrowserJsonContext : JsonSerializerContext
{
}
