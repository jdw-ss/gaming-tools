using System.Collections.Generic;

namespace GcSupplyHelper.Models;

/// <summary>
/// Where a leaf material comes from. Used purely for UI labelling — the
/// plugin doesn't take any action based on the source.
/// </summary>
internal enum MaterialSource
{
    /// <summary>
    /// The item has a non-null <c>Lumina.Excel.Sheets.Item.GatheringItem</c>
    /// reference: harvested by Miner, Botanist, or Fisher.
    /// </summary>
    Gathered,

    /// <summary>
    /// The item has no recipe and isn't gathered — typically a vendor
    /// purchase (NPC, GC seal shop, etc.). v0.1 doesn't try to identify
    /// the specific vendor; that's an open backlog item.
    /// </summary>
    Vendor,

    /// <summary>
    /// Defensive bucket. An intermediate crafted item shouldn't appear
    /// in the leaf aggregate because <see cref="Services.RecipeWalker"/>
    /// recurses through crafted items; this value only surfaces if the
    /// cycle guard fires and treats a node as a forced leaf.
    /// </summary>
    Crafted,

    /// <summary>
    /// Neither gathered nor vendor-bought as far as we can tell. Probably
    /// a quest reward, dungeon drop, or special-event item.
    /// </summary>
    Unknown,
}

/// <summary>
/// One row in the aggregate shopping list: a leaf material the player
/// needs to acquire to complete some subset of today's missions, plus
/// the cross-class accounting that lets the UI render a "Needed by"
/// badge.
/// </summary>
internal sealed class MaterialRequirement
{
    public required uint ItemId { get; init; }
    public required string ItemName { get; init; }
    public required ushort IconId { get; init; }
    public required MaterialSource Source { get; init; }

    /// <summary>
    /// Running total across every contributing mission. Mutable so the
    /// aggregator can add to it on each recursion-leaf hit without
    /// reallocating the record.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Set of class-job row ids that contributed at least one unit of
    /// this material. <c>HashSet</c> rather than <c>List</c> so the same
    /// class adding the material multiple times (via different
    /// intermediate recipes that share an ingredient) doesn't duplicate.
    /// </summary>
    public HashSet<byte> NeededByClassJobs { get; } = new();
}
