using System;
using System.Collections.Generic;
using BulkDesynth.Models;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.AtkValueType;

namespace BulkDesynth.Services;

/// <summary>
/// Runs a queued list of <see cref="DesynthCandidate"/>s one at a time on the
/// framework thread.
///
/// Invocation pattern matches the canonical approach used by AutoRetainer /
/// SomethingNeedDoing / ffxiv-bundleoftweaks:
///
///   AgentSalvage.Instance()->SalvageItem(item);
///   AgentSalvage.Instance()->AgentInterface.ReceiveEvent(
///       &retval,
///       params,            // [ Int=0, Bool=1 ]
///       2,                 // event kind = Begin
///       1);                // listener id
///
/// The <c>Bool=1</c> second parameter is what bypasses the SelectYesno
/// confirmation popup. That's also why we pre-filter HQ / spiritbonded items
/// in the scanner - the warning dialog never gets a chance to appear, so
/// we can't lean on it as a user-visible safety net for those cases.
///
/// Pacing is gated by <see cref="ConditionFlag.Occupied39"/> (the game's
/// own busy flag while the desynth cast + animation runs) plus a configurable
/// post-cast cooldown. No addon-callback timing required.
/// </summary>
public sealed class DesynthExecutor : IDisposable
{
    private enum Phase
    {
        Idle,
        FireNext,
        WaitForBusy,        // confirm Occupied39 actually went TRUE (cast started)
        WaitForBusyToClear, // wait until Occupied39 goes FALSE (cast finished)
        PostCastCooldown,
        Done,
    }

    private readonly IFramework framework;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly Configuration config;

    private readonly Queue<DesynthCandidate> queue = new();
    private DesynthCandidate? current;
    private Phase phase = Phase.Idle;
    private DateTime deadline = DateTime.MinValue;

    public int Processed { get; private set; }
    public int Remaining => queue.Count;
    public bool IsRunning => phase != Phase.Idle;
    public DesynthCandidate? CurrentItem => current;

    public DesynthExecutor(IFramework framework, ICondition condition, IPluginLog log, Configuration config)
    {
        this.framework = framework;
        this.condition = condition;
        this.log = log;
        this.config = config;
        framework.Update += OnTick;
    }

    public void Start(IReadOnlyList<DesynthCandidate> items)
    {
        if (IsRunning)
        {
            log.Warning("DesynthExecutor.Start called while already running - ignored.");
            return;
        }

        var cap = Math.Min(items.Count, Math.Max(1, config.PerRunHardCap));
        queue.Clear();
        for (var i = 0; i < cap; i++)
            queue.Enqueue(items[i]);

        Processed = 0;
        current = null;
        phase = Phase.FireNext;
        deadline = DateTime.MinValue;
        log.Information($"Bulk desynth started: {queue.Count} item(s) queued.");
    }

    public void Stop(string reason)
    {
        if (!IsRunning) return;
        log.Information($"Bulk desynth stopped: {reason}. {Processed} processed, {queue.Count} discarded.");
        queue.Clear();
        current = null;
        phase = Phase.Idle;
    }

    private void OnTick(IFramework _)
    {
        if (phase == Phase.Idle) return;

        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            log.Error(ex, "DesynthExecutor.Tick threw - aborting run.");
            Stop("internal error");
        }
    }

    private unsafe void Tick()
    {
        switch (phase)
        {
            case Phase.FireNext:
            {
                // Never fire while the game is already busy on a previous
                // cast - the SalvageItem call would silently no-op.
                if (condition[ConditionFlag.Occupied39] || condition[ConditionFlag.Casting])
                    return;

                if (!queue.TryDequeue(out var next))
                {
                    phase = Phase.Done;
                    return;
                }
                current = next;

                if (!TryGetInventoryItem(current.Value, out var itemPtr))
                {
                    log.Warning($"Slot {current.Value.Slot} of {current.Value.Container} is empty or moved - skipping.");
                    phase = Phase.FireNext;
                    return;
                }

                var agent = AgentSalvage.Instance();
                if (agent == null)
                {
                    Stop("AgentSalvage unavailable");
                    return;
                }

                agent->SalvageItem(itemPtr);

                // Fire Begin. Layout matches the AutoRetainer / SND /
                // bundleoftweaks references verbatim.
                var retval = new AtkValue();
                var p0 = new AtkValue { Type = ValueType.Int, Int = 0 };
                var p1 = new AtkValue { Type = ValueType.Bool, Byte = 1 };
                var args = stackalloc AtkValue[2];
                args[0] = p0;
                args[1] = p1;
                agent->AgentInterface.ReceiveEvent(&retval, args, 2, 1);

                // Give the game up to AddonWaitTimeoutMs to actually enter
                // the Occupied39 state. If it never does, the item rejected
                // the desynth (e.g. skill mismatch slipped through filter).
                deadline = DateTime.UtcNow.AddMilliseconds(config.AddonWaitTimeoutMs);
                phase = Phase.WaitForBusy;
                break;
            }

            case Phase.WaitForBusy:
            {
                if (condition[ConditionFlag.Occupied39])
                {
                    phase = Phase.WaitForBusyToClear;
                    return;
                }
                if (DateTime.UtcNow > deadline)
                {
                    log.Warning($"Desynth never entered busy state for {current?.Name} - assuming rejected, moving on.");
                    Processed++;
                    GoToCooldown();
                }
                break;
            }

            case Phase.WaitForBusyToClear:
            {
                if (!condition[ConditionFlag.Occupied39])
                {
                    Processed++;
                    GoToCooldown();
                }
                // No deadline here - a long desynth cast is fine; the player
                // will see something is happening. If the game gets wedged
                // the user can /bds stop.
                break;
            }

            case Phase.PostCastCooldown:
            {
                if (DateTime.UtcNow < deadline) return;
                phase = queue.Count > 0 ? Phase.FireNext : Phase.Done;
                break;
            }

            case Phase.Done:
            {
                log.Information($"Bulk desynth finished. {Processed} item(s) processed.");
                current = null;
                phase = Phase.Idle;
                break;
            }
        }
    }

    private void GoToCooldown()
    {
        deadline = DateTime.UtcNow.AddMilliseconds(config.InterItemDelayMs);
        phase = Phase.PostCastCooldown;
    }

    /// <summary>
    /// Re-resolve the candidate to a live <see cref="InventoryItem"/> pointer.
    /// We can't cache the pointer between ticks: the inventory backing array
    /// can reshuffle after every successful desynth, so what was slot 5 might
    /// be empty next tick. We always re-read.
    /// </summary>
    private static unsafe bool TryGetInventoryItem(DesynthCandidate candidate, out InventoryItem* item)
    {
        item = null;
        var manager = InventoryManager.Instance();
        if (manager == null) return false;
        var inv = manager->GetInventoryContainer(candidate.Container);
        if (inv == null || !inv->IsLoaded) return false;
        var slot = inv->GetInventorySlot(candidate.Slot);
        if (slot == null || slot->ItemId == 0) return false;
        // Defense in depth: only proceed if the item ID still matches. If the
        // player moved stuff around between preview and run, we abort that
        // slot rather than nuking whatever else landed there.
        if (slot->ItemId != candidate.ItemId) return false;
        item = slot;
        return true;
    }

    public void Dispose()
    {
        framework.Update -= OnTick;
        queue.Clear();
        current = null;
        phase = Phase.Idle;
    }
}
