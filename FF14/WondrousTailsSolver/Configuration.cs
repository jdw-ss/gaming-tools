using System;
using Dalamud.Configuration;

namespace WondrousTailsSolver;

/// <summary>
/// Persisted user settings. Loaded by Dalamud via
/// <c>IDalamudPluginInterface.GetPluginConfig()</c> and written back with
/// <c>SavePluginConfig()</c>.
///
/// v0.1 only has one toggle (overlay on/off). Kept as a real
/// <see cref="IPluginConfiguration"/> so future fields don't require a
/// schema migration.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>Schema version. Bump if the layout changes incompatibly.</summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Whether the in-journal overlay is drawn. The /wts command flips
    /// this and saves. Defaults to on - the overlay is the entire point
    /// of the plugin.
    /// </summary>
    public bool ShowOverlay { get; set; } = true;
}
