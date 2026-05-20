using System.Collections.Generic;
using BulkDesynth.Models;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LuminaItem = Lumina.Excel.Sheets.Item;

namespace BulkDesynth.Services;

/// <summary>
/// Reads the player's inventories via <see cref="InventoryManager"/> and
/// turns matching slots into <see cref="DesynthCandidate"/>s.
///
/// Must run on the framework thread - <c>InventoryManager.Instance()</c>
/// returns a pointer into the game's main-thread state and reading it from
/// the task pool is a race.
/// </summary>
public sealed class InventoryScanner
{
    /// <summary>Quest row ID that unlocks desynthesis. Sourced from AutoRetainer's
    /// reference implementation; the quest is "Gone to Pieces" / equivalent.
    /// Stored as uint because the row ID exceeds ushort.MaxValue (65535).</summary>
    private const uint DesynthUnlockQuest = 65688;

    /// <summary>Minimum DoH class level required to use desynthesis at all.</summary>
    private const byte MinDohLevelForDesynth = 30;

    private readonly IDataManager dataManager;
    private readonly IPluginLog log;

    public InventoryScanner(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
    }

    /// <summary>
    /// True iff the player has unlocked desynthesis at all (i.e. completed
    /// the unlock quest). The plugin refuses to do anything if this is false,
    /// because every SalvageItem call would no-op.
    /// </summary>
    public unsafe bool IsDesynthUnlocked()
    {
        return QuestManager.IsQuestComplete(DesynthUnlockQuest);
    }

    /// <summary>
    /// Walk every container in <paramref name="filter"/> and return the slots
    /// whose contents match. Result is ordered by container then slot so the
    /// preview UI displays predictably.
    /// </summary>
    public unsafe List<DesynthCandidate> BuildPreview(DesynthFilter filter)
    {
        var results = new List<DesynthCandidate>(capacity: 32);

        if (!IsDesynthUnlocked())
        {
            log.Warning("Desynthesis is not unlocked on this character - aborting scan.");
            return results;
        }

        var manager = InventoryManager.Instance();
        if (manager == null)
        {
            log.Warning("InventoryManager.Instance() returned null - is the player logged in?");
            return results;
        }

        var playerState = PlayerState.Instance();
        if (playerState == null)
        {
            log.Warning("PlayerState.Instance() returned null - aborting scan.");
            return results;
        }

        var itemSheet = dataManager.GetExcelSheet<LuminaItem>();

        foreach (var container in filter.Containers)
        {
            var inv = manager->GetInventoryContainer(container);
            if (inv == null || !inv->IsLoaded)
                continue;

            for (short slot = 0; slot < inv->Size; slot++)
            {
                var item = inv->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                    continue;

                // Symbolic slots (the crystals/currency pseudo-container) are
                // not real items and have no desynth path - skip silently.
                if (item->IsSymbolic)
                    continue;

                if (!itemSheet.TryGetRow(item->ItemId, out var row))
                    continue;

                // Lumina Item.Desynth is the recipe level for desynthesis.
                // Zero means the item is fundamentally not desynthable.
                var desynthLevel = row.Desynth;
                if (desynthLevel == 0)
                    continue;

                // Per AutoRetainer reference: the relevant DoH skill is
                // determined by Item.ClassJobRepair (e.g. weapons -> BSM,
                // accessories -> GSM). Index into ClassJobLevels by that
                // ClassJob row's ExpArrayIndex.
                if (filter.RespectSkillCap)
                {
                    if (!row.ClassJobRepair.IsValid)
                        continue; // No repair class -> no desynth class.
                    var classJob = row.ClassJobRepair.Value;
                    var idx = classJob.ExpArrayIndex;
                    if (idx < 0)
                        continue;
                    var playerLevel = playerState->ClassJobLevels[idx];
                    if (playerLevel < MinDohLevelForDesynth)
                        continue;
                }

                var isHq = (item->Flags & InventoryItem.ItemFlags.HighQuality) != 0;
                if (filter.ExcludeHq && isHq)
                    continue;

                if (filter.ItemId.HasValue && item->ItemId != filter.ItemId.Value)
                    continue;

                var itemLevel = (ushort)row.LevelItem.RowId;
                if (filter.MaxItemLevel.HasValue && itemLevel > filter.MaxItemLevel.Value)
                    continue;
                if (filter.MinItemLevel.HasValue && itemLevel < filter.MinItemLevel.Value)
                    continue;

                var spiritbond = item->SpiritbondOrCollectability;
                if (filter.MaxSpiritbond.HasValue && spiritbond > filter.MaxSpiritbond.Value)
                    continue;

                results.Add(new DesynthCandidate(
                    Container: container,
                    Slot: slot,
                    ItemId: item->ItemId,
                    IsHq: isHq,
                    Spiritbond: spiritbond,
                    ItemLevel: itemLevel,
                    Name: row.Name.ExtractText(),
                    DesynthLevel: desynthLevel));
            }
        }

        return results;
    }
}
