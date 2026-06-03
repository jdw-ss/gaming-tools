using System.Collections.Generic;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace GcSupplyHelper.Services;

/// <summary>
/// Walks the player's inventory containers (bags, crystal page, and both
/// the standard and premium saddlebags) and reports how many of a given
/// item are currently held. HQ and NQ counts are summed — the user
/// confirmed this is the desired behaviour because Grand Company
/// turn-ins accept either quality.
///
/// Reads happen on demand from the framework thread (via
/// <see cref="MainWindow"/>'s Draw and the route-URL button handler).
/// The traversal is cheap — ~9 containers × ~35 slots × ~30 materials,
/// done in microseconds — so we don't bother caching across frames.
///
/// Saddlebag containers report <c>IsLoaded == false</c> until the
/// player has interacted with the saddlebag at least once this session
/// (or always for accounts without the premium subscription, in the
/// premium case). Unloaded containers are silently skipped.
/// </summary>
internal sealed class InventoryReader
{
    /// <summary>
    /// The set of containers we sum across. Equipped gear, armory chest,
    /// gearset items, and retainer bellies are deliberately excluded —
    /// retainer support is a stretch goal filed in <c>IDEAS.md</c>.
    /// </summary>
    private static readonly InventoryType[] ContainersToWalk =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.Crystals,
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
    ];

    /// <summary>
    /// FFXIV's legacy HQ encoding: the lower 6 digits of <c>ItemId</c>
    /// are the base item id, plus 1,000,000 when high-quality. Some
    /// FFXIVClientStructs versions normalise this away; others don't.
    /// Checking both forms is robust without depending on which version
    /// of the bindings is loaded.
    /// </summary>
    private const uint HqOffset = 1_000_000;

    private readonly IPluginLog log;

    public InventoryReader(IPluginLog log) { this.log = log; }

    /// <summary>
    /// Total quantity of <paramref name="itemId"/> across all walked
    /// containers, summing HQ + NQ. Returns 0 if the inventory manager
    /// isn't initialised (e.g. between login and the world server
    /// finishing the inventory sync).
    /// </summary>
    public unsafe int GetCount(uint itemId)
    {
        var im = InventoryManager.Instance();
        if (im == null) return 0;

        var total = 0;
        foreach (var type in ContainersToWalk)
        {
            var container = im->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) continue;
                var rawId = slot->ItemId;
                if (rawId == itemId || rawId == itemId + HqOffset)
                    total += slot->Quantity;
            }
        }
        return total;
    }

    /// <summary>
    /// Convenience: compute counts for many items in a single pass over
    /// the inventory. Faster than calling <see cref="GetCount"/> N times
    /// when N is large, because each call to <see cref="GetCount"/>
    /// re-walks every container.
    /// </summary>
    public unsafe Dictionary<uint, int> GetCounts(IReadOnlyCollection<uint> itemIds)
    {
        var result = new Dictionary<uint, int>(itemIds.Count);
        foreach (var id in itemIds) result[id] = 0;

        var im = InventoryManager.Instance();
        if (im == null) return result;

        foreach (var type in ContainersToWalk)
        {
            var container = im->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded) continue;
            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null) continue;
                var rawId = slot->ItemId;
                var baseId = rawId >= HqOffset ? rawId - HqOffset : rawId;
                if (result.ContainsKey(baseId))
                    result[baseId] += slot->Quantity;
            }
        }
        return result;
    }
}
