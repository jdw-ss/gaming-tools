using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace GcSupplyHelper;

/// <summary>
/// Persisted user settings. Loaded by Dalamud via
/// <c>IDalamudPluginInterface.GetPluginConfig()</c> and written back
/// with <c>SavePluginConfig()</c>.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Schema version. Bump if the layout changes incompatibly.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// The set of <c>ClassJob</c> row ids whose missions appear in the
    /// per-mission and aggregate views. Defaults to all 11 DoH/DoL
    /// classes; the Settings tab exposes checkboxes to hide classes
    /// the player isn't levelling.
    ///
    /// CRP=8, BSM=9, ARM=10, GSM=11, LTW=12, WVR=13, ALC=14, CUL=15,
    /// MIN=16, BTN=17, FSH=18.
    /// </summary>
    public HashSet<uint> EnabledClassJobIds { get; set; } =
        new() { 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };

    /// <summary>
    /// When true, the plugin attempts a refresh on login. Usually the
    /// agent isn't populated yet at that point so this is best-effort
    /// — the addon hooks for Timers / Personnel Officer do the real work.
    /// </summary>
    public bool AutoRefreshOnLogin { get; set; } = true;
}
