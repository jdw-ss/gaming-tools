using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GcSupplyHelper.Models;

namespace GcSupplyHelper.Services;

/// <summary>
/// Encodes an aggregate material list into a URL that points at the
/// `ffxiv-achievement-tracker` site's <c>/gc-supply-route</c> page,
/// where the actual route planning happens. The wire format is a
/// versioned JSON document, base64-url-safe-encoded, in the URL hash
/// fragment.
///
/// Wire format (v=1):
/// <code>
/// {
///   "v": 1,
///   "items": [
///     { "id": 12345, "qty": 14, "c": [9, 10, 11], "h": 3 },
///     ...
///   ]
/// }
/// </code>
/// where <c>id</c> is the Lumina Item RowId, <c>qty</c> the aggregate
/// quantity, <c>c</c> the ClassJob row ids that contributed to it, and
/// <c>h</c> (added in v0.1.4, optional, default 0) the count already
/// present in the player's inventory at the moment the URL was built.
/// The TypeScript decoder treats <c>h</c> as optional and falls back
/// to 0 when it's missing or zero (the JSON encoder omits zero values
/// via <see cref="JsonIgnoreCondition.WhenWritingDefault"/> to keep the
/// URL compact when the player has nothing on hand).
///
/// The hash fragment is chosen over a query string for two small wins:
/// hash fragments don't get sent to the server in HTTP requests (no
/// Firebase Hosting access-log noise), and they don't end up in any
/// referer headers. The TypeScript side decodes via
/// <c>window.location.hash</c> in <c>app/gc-supply-route/lib.ts</c>.
/// </summary>
internal static class RouteUrlBuilder
{
    /// <summary>
    /// Wire-format version. Bumping requires matching the decoder in
    /// <c>ffxiv-achievement-tracker/app/gc-supply-route/lib.ts</c>.
    /// </summary>
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    /// <summary>
    /// Build the route-planner URL from an aggregate plus an optional
    /// snapshot of how many of each item the player already has on hand
    /// (used by the web page as the starting value of its editable Have
    /// column). Items with no contributing classes (defensive — shouldn't
    /// happen given how the aggregate is built) are dropped. Zero have-
    /// counts are encoded as a missing field, not <c>"h": 0</c>, to keep
    /// the URL short for fresh starts.
    /// </summary>
    public static string Build(
        string baseUrl,
        IEnumerable<MaterialRequirement> aggregate,
        IReadOnlyDictionary<uint, int>? haveByItemId = null)
    {
        var payload = new RouteRequest
        {
            Version = CurrentVersion,
            Items = aggregate
                .Where(m => m.Quantity > 0)
                .Select(m => new RouteRequestItem
                {
                    Id = m.ItemId,
                    Qty = m.Quantity,
                    // int[] not byte[]: System.Text.Json serialises byte[]
                    // as a base64 string, which would break the TS-side
                    // decoder's `number[]` contract. int is fine — ClassJob
                    // IDs fit easily.
                    Classes = m.NeededByClassJobs.OrderBy(c => c).Select(c => (int)c).ToArray(),
                    Have = haveByItemId is not null && haveByItemId.TryGetValue(m.ItemId, out var h)
                        ? h
                        : 0,
                })
                .ToArray(),
        };

        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        var b64Url = Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        return $"{baseUrl.TrimEnd('/')}/gc-supply-route/#r={b64Url}";
    }
}

/// <summary>
/// Wire-format payload root. Names are short on the wire to keep the
/// URL compact for boards with ~30 leaf materials.
/// </summary>
internal sealed class RouteRequest
{
    [JsonPropertyName("v")] public int Version { get; set; }
    [JsonPropertyName("items")] public RouteRequestItem[] Items { get; set; } = [];
}

/// <summary>
/// One aggregate leaf on the wire.
/// </summary>
internal sealed class RouteRequestItem
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("qty")] public int Qty { get; set; }
    [JsonPropertyName("c")] public int[] Classes { get; set; } = [];

    /// <summary>
    /// HQ + NQ inventory snapshot at the moment the URL was built (see
    /// <see cref="InventoryReader"/>). Omitted from the wire JSON when 0
    /// via <see cref="JsonIgnoreCondition.WhenWritingDefault"/>, so fresh
    /// runs don't pay for the field.
    /// </summary>
    [JsonPropertyName("h")] public int Have { get; set; }
}
