using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BulkDesynth.Models;

/// <summary>
/// Declarative description of "which slots should I queue?". Built by the UI
/// (or chat-command parser) and consumed by <c>InventoryScanner.BuildPreview</c>.
///
/// A scan walks the containers in <see cref="Containers"/> and keeps the slot
/// iff every populated predicate matches. An unset predicate means "no
/// constraint" - e.g. <see cref="ItemId"/> null means "any item".
/// </summary>
public sealed class DesynthFilter
{
    /// <summary>
    /// Which containers to walk. Defaults to the four main bags. Use
    /// <see cref="Presets.ArmouryChest"/> for the armoury slots.
    /// </summary>
    public List<InventoryType> Containers { get; init; } = new(Presets.MainBags);

    /// <summary>If set, only items with this row ID pass.</summary>
    public uint? ItemId { get; init; }

    /// <summary>If set, only items at or below this item level pass.</summary>
    public ushort? MaxItemLevel { get; init; }

    /// <summary>If set, only items at or above this item level pass.</summary>
    public ushort? MinItemLevel { get; init; }

    /// <summary>If true, HQ items are excluded.</summary>
    public bool ExcludeHq { get; init; }

    /// <summary>If set, only items with spiritbond at or below this value pass (0-1000 scale).</summary>
    public ushort? MaxSpiritbond { get; init; }

    /// <summary>If true, items whose <c>DesynthLevel</c> exceeds the player's DoH skill cap are filtered out.</summary>
    public bool RespectSkillCap { get; init; } = true;

    public static class Presets
    {
        public static readonly InventoryType[] MainBags =
        {
            InventoryType.Inventory1,
            InventoryType.Inventory2,
            InventoryType.Inventory3,
            InventoryType.Inventory4,
        };

        public static readonly InventoryType[] ArmouryChest =
        {
            InventoryType.ArmoryMainHand,
            InventoryType.ArmoryOffHand,
            InventoryType.ArmoryHead,
            InventoryType.ArmoryBody,
            InventoryType.ArmoryHands,
            InventoryType.ArmoryLegs,
            InventoryType.ArmoryFeets,
            InventoryType.ArmoryEar,
            InventoryType.ArmoryNeck,
            InventoryType.ArmoryWrist,
            InventoryType.ArmoryRings,
        };
    }
}
