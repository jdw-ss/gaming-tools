using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace BulkDesynth;

/// <summary>
/// Persisted plugin settings. Loaded by Dalamud via
/// <c>IDalamudPluginInterface.GetPluginConfig()</c> and written back with
/// <c>SavePluginConfig()</c>.
///
/// We default to the most conservative behaviour (long delays, dry-run only)
/// because desynth is destructive - an item that's been processed cannot be
/// recovered.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Schema version. Bump if the layout changes incompatibly.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Milliseconds to wait after firing a desynth action before checking
    /// for the result dialog / moving to the next item. Conservative default
    /// (1.2s) leaves room for the cast bar + result animation.
    /// </summary>
    public int InterItemDelayMs { get; set; } = 1200;

    /// <summary>
    /// Max milliseconds to wait for the SalvageDialog or SelectYesno addon
    /// to appear after we kick it off. If the addon never shows up we abort
    /// the current item and move on (we never silently retry).
    /// </summary>
    public int AddonWaitTimeoutMs { get; set; } = 3000;

    /// <summary>
    /// Hard cap on how many items a single run can desynth. Belt-and-braces
    /// against a mis-built preview list nuking an entire bag. Tweak in the
    /// settings tab if you genuinely want a 200-item run.
    /// </summary>
    public int PerRunHardCap { get; set; } = 50;

    /// <summary>
    /// Item IDs the user has explicitly opted in to "yes, desynth this even
    /// though it is HQ / spiritbonded / high ilvl". Empty by default.
    /// </summary>
    public HashSet<uint> ItemAllowList { get; set; } = new();

    /// <summary>
    /// Item IDs that must never be desynth'd, no matter what filter selects
    /// them. Takes precedence over <see cref="ItemAllowList"/>.
    /// </summary>
    public HashSet<uint> ItemBlockList { get; set; } = new();
}
