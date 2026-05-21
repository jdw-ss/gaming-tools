using FFXIVClientStructs.FFXIV.Client.Game;

namespace BulkDesynth.Models;

/// <summary>
/// One concrete inventory slot the plugin intends to desynth. A "candidate"
/// always refers to a specific slot, not a logical item - if you have three
/// copies of the same gear in three different slots, you get three candidates.
/// </summary>
public readonly record struct DesynthCandidate(
    /// <summary>Which inventory container the item lives in (bag 1-4, armoury, etc.).</summary>
    InventoryType Container,
    /// <summary>Zero-based slot index inside the container.</summary>
    short Slot,
    /// <summary>Item row ID (Lumina Item sheet).</summary>
    uint ItemId,
    /// <summary>Whether the slot holds a high-quality copy.</summary>
    bool IsHq,
    /// <summary>Current spiritbond / collectability value, 0-100 (matches the in-game percentage display).</summary>
    ushort Spiritbond,
    /// <summary>Item level - cached so the UI doesn't have to re-resolve every frame.</summary>
    ushort ItemLevel,
    /// <summary>Display name (English / current client locale). For the preview UI.</summary>
    string Name,
    /// <summary>
    /// Desynth recipe level required (from the Item.Desynth sheet column).
    /// Zero means the item is not desynthable at all.
    /// </summary>
    ushort DesynthLevel);
