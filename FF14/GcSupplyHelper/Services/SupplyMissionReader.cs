using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using GcSupplyHelper.Models;

namespace GcSupplyHelper.Services;

/// <summary>
/// Reads today's 11 Grand Company Supply and Provisioning missions
/// from <see cref="AgentGrandCompanySupply"/>. The agent's
/// <c>SupplyProvisioningData</c> pointer is null until the player has
/// opened either the in-game Timers panel or the Personnel Officer
/// supply list at least once in the current session — see the multi-
/// addon refresh hooks in <see cref="Plugin"/>.
///
/// All reads happen on the framework thread (Dalamud guarantees this
/// for <c>IAddonLifecycle</c> callbacks and for the WindowSystem). The
/// last successful snapshot is cached in <see cref="CurrentMissions"/>
/// so the UI can render without re-reading on every frame.
/// </summary>
internal sealed class SupplyMissionReader
{
    /// <summary>
    /// Mapping from the <c>_supplyData[8]</c> array index to the
    /// corresponding <c>ClassJob</c> row id. Crafters in the canonical
    /// order CRP, BSM, ARM, GSM, LTW, WVR, ALC, CUL. The game's array
    /// layout is the same in retail; if a future patch reshuffles this,
    /// the mismatch will surface as e.g. "Carpenter is asking for a
    /// leather item" and the user can flag it — see <c>CLAUDE.md</c>
    /// gotcha "GC Supply class-job index mapping".
    /// </summary>
    private static readonly byte[] SupplyIndexToClassJob =
        [8, 9, 10, 11, 12, 13, 14, 15];

    /// <summary>
    /// Mapping from <c>_provisioningData[3]</c> array index to
    /// <c>ClassJob</c>. Gatherers in the canonical order MIN, BTN, FSH.
    /// </summary>
    private static readonly byte[] ProvisioningIndexToClassJob =
        [16, 17, 18];

    private readonly IPluginLog log;
    private readonly List<DailyMission> cache = new(11);

    public SupplyMissionReader(IPluginLog log) { this.log = log; }

    /// <summary>Snapshot of the most recent successful read. Empty until first refresh.</summary>
    public IReadOnlyList<DailyMission> CurrentMissions => cache;

    /// <summary>UTC timestamp of the last successful read.</summary>
    public DateTime? LastRefreshUtc { get; private set; }

    /// <summary>
    /// Attempt to read today's missions from the agent. Returns
    /// <c>true</c> iff the agent pointer was non-null and at least one
    /// non-empty mission entry was found. Idempotent: calling multiple
    /// times in quick succession is harmless; we just refresh the
    /// cache.
    /// </summary>
    public unsafe bool TryRefresh()
    {
        var agent = AgentGrandCompanySupply.Instance();
        if (agent == null)
        {
            log.Verbose("GcSupplyHelper: AgentGrandCompanySupply not yet instantiated.");
            return false;
        }
        var supply = agent->SupplyProvisioningData;
        if (supply == null)
        {
            log.Verbose("GcSupplyHelper: SupplyProvisioningData pointer is null; open Timers or Personnel Officer to populate.");
            return false;
        }

        var fresh = new List<DailyMission>(11);

        for (var i = 0; i < SupplyIndexToClassJob.Length; i++)
        {
            var entry = supply->SupplyData[i];
            if (entry.ItemId == 0) continue;
            fresh.Add(new DailyMission(
                ItemId: entry.ItemId,
                QuantityRequired: entry.NumRequested,
                ClassJobId: SupplyIndexToClassJob[i],
                IsProvisioning: false));
        }

        for (var i = 0; i < ProvisioningIndexToClassJob.Length; i++)
        {
            var entry = supply->ProvisioningData[i];
            if (entry.ItemId == 0) continue;
            fresh.Add(new DailyMission(
                ItemId: entry.ItemId,
                QuantityRequired: entry.NumRequested,
                ClassJobId: ProvisioningIndexToClassJob[i],
                IsProvisioning: true));
        }

        if (fresh.Count == 0)
        {
            log.Verbose("GcSupplyHelper: agent populated but no missions visible; likely between server resets.");
            return false;
        }

        cache.Clear();
        cache.AddRange(fresh);
        LastRefreshUtc = DateTime.UtcNow;
        log.Information("GcSupplyHelper: loaded {Count} mission(s) from AgentGrandCompanySupply.", fresh.Count);
        return true;
    }

    /// <summary>
    /// Drop the cached missions. Used on logout so the next character
    /// doesn't see stale data from the previous one.
    /// </summary>
    public void Clear()
    {
        cache.Clear();
        LastRefreshUtc = null;
    }
}
