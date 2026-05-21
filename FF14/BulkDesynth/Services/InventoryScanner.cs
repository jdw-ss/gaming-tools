using System.Collections.Generic;
using BulkDesynth.Models;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
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

        // Build the internal -> visual bag/slot map once for the whole scan.
        // The lookup respects whatever drag-rearrangement the player has done
        // in their inventory window (SortaKinda etc.). Falls back to (0, 0)
        // sentinel for slots we can't resolve.
        var visualMap = BuildInternalToVisualMap();

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

                // Resolve the name once so the case-insensitive name filter
                // and the candidate's display string both reuse it.
                var name = row.Name.ExtractText();
                if (!string.IsNullOrEmpty(filter.NameContains)
                    && name.IndexOf(filter.NameContains, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Look up the visual position. Only meaningful for main bags
                // (Inventory1..4) - armoury slots aren't in the InventorySorter
                // and stay at (0, 0).
                byte visualBag = 0;
                byte visualSlot = 0;
                if (visualMap.TryGetValue((container, slot), out var v))
                {
                    visualBag = v.bag;
                    visualSlot = v.slot;
                }

                results.Add(new DesynthCandidate(
                    Container: container,
                    Slot: slot,
                    ItemId: item->ItemId,
                    IsHq: isHq,
                    Spiritbond: spiritbond,
                    ItemLevel: itemLevel,
                    Name: name,
                    DesynthLevel: desynthLevel,
                    VisualBag: visualBag,
                    VisualSlot: visualSlot));
            }
        }

        return results;
    }

    /// <summary>
    /// Walk <c>ItemOrderModule.InventorySorter->Items</c> once and return a
    /// map from internal (<see cref="InventoryType"/>, slot) to the visual
    /// (bag 1-4, slot 0-34) position the player sees in their customized
    /// inventory window.
    ///
    /// Cross-referenced from SimpleTweaksPlugin/Tweaks/EquipFromHotbar.cs:
    /// <c>sorter->Items[i]</c> is indexed by VISUAL position; each entry's
    /// <c>Page</c> and <c>Slot</c> point at where the item actually lives
    /// internally. So we invert the relationship by iterating.
    /// </summary>
    private static unsafe Dictionary<(InventoryType, short), (byte bag, byte slot)> BuildInternalToVisualMap()
    {
        var map = new Dictionary<(InventoryType, short), (byte, byte)>(capacity: 140);

        var module = ItemOrderModule.Instance();
        if (module == null) return map;
        var sorter = module->InventorySorter;
        if (sorter == null) return map;
        var itemsPerPage = sorter->ItemsPerPage;
        if (itemsPerPage <= 0) return map;

        var total = sorter->Items.LongCount;
        for (var i = 0L; i < total; i++)
        {
            var entry = sorter->Items[i].Value;
            if (entry == null) continue;

            // entry->Page is an offset 0..3 from InventoryType.Inventory1.
            var internalContainer = InventoryType.Inventory1 + entry->Page;
            var internalSlot = (short)entry->Slot;

            // Visual position is the linear index into Items, broken into
            // (bag, slotInBag). Bag is 1-indexed for the display layer.
            var visualBag = (byte)(i / itemsPerPage + 1);
            var visualSlot = (byte)(i % itemsPerPage);

            map[(internalContainer, internalSlot)] = (visualBag, visualSlot);
        }

        return map;
    }
}
