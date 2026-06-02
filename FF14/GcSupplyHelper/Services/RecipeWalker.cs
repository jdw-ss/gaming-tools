using System;
using System.Collections.Generic;
using GcSupplyHelper.Models;

namespace GcSupplyHelper.Services;

/// <summary>
/// Recursively expands one or more <see cref="DailyMission"/> turn-ins
/// into the union of raw materials needed to complete them all. Each
/// material is annotated with the set of classes that contributed to
/// it (so the aggregate view can render "Needed by: BSM + ARM + GSM").
///
/// Pure logic — no Dalamud / Lumina dependency. Its only collaborator
/// is <see cref="IRecipeDataSource"/>, which the production plugin
/// fulfils with Lumina sheets and the test fulfils with hand-crafted
/// fixtures.
///
/// Cycle handling: a per-traversal <c>pathAncestors</c> set blocks the
/// pathological case of a recipe that lists itself as an ingredient.
/// In real Lumina data this never happens, but the guard costs almost
/// nothing and keeps a future data-shape change from stack-overflowing
/// the plugin.
/// </summary>
internal sealed class RecipeWalker
{
    private readonly IRecipeDataSource source;
    private readonly Action<string>? warn;

    /// <summary>
    /// Construct a walker.
    /// </summary>
    /// <param name="source">Lumina-backed or test-fake data source.</param>
    /// <param name="warn">
    /// Optional sink for diagnostic messages (cycle detected, recipe
    /// failed to look up, etc.). Production code passes
    /// <c>Plugin.Log.Warning</c>; tests can pass <c>null</c>.
    /// </param>
    public RecipeWalker(IRecipeDataSource source, Action<string>? warn = null)
    {
        this.source = source;
        this.warn = warn;
    }

    /// <summary>
    /// Expand a list of missions into their union of raw materials.
    /// The returned dictionary is keyed by leaf <c>ItemId</c>; each
    /// <see cref="MaterialRequirement"/>'s <c>Quantity</c> is the sum
    /// across all missions that need it.
    /// </summary>
    public IReadOnlyDictionary<uint, MaterialRequirement> ComputeRawMaterials(
        IEnumerable<DailyMission> missions)
    {
        var accumulator = new Dictionary<uint, MaterialRequirement>();
        foreach (var mission in missions)
        {
            var pathAncestors = new HashSet<uint>();
            WalkRecipe(
                itemId: mission.ItemId,
                quantityNeeded: mission.QuantityRequired,
                classJobId: mission.ClassJobId,
                accumulator: accumulator,
                pathAncestors: pathAncestors);
        }
        return accumulator;
    }

    private void WalkRecipe(
        uint itemId,
        int quantityNeeded,
        byte classJobId,
        Dictionary<uint, MaterialRequirement> accumulator,
        HashSet<uint> pathAncestors)
    {
        if (quantityNeeded <= 0) return;

        // Cycle guard: if this item is somewhere in our current ancestor
        // chain, we're about to loop. Log and treat as a leaf.
        if (pathAncestors.Contains(itemId))
        {
            warn?.Invoke($"Recipe cycle detected at item {itemId}; treating as leaf.");
            AddLeaf(itemId, quantityNeeded, classJobId, accumulator);
            return;
        }

        // Recipe takes precedence over gathered: a few items (e.g. Fire
        // Cluster aetherially-condensed crystals in some content) have
        // both records. The recipe path gives the user a craftable
        // alternative; if the player would rather gather, they already
        // know how. This matches the user's stated "raw materials"
        // intent for the canonical path.
        var recipe = source.GetRecipe(itemId);
        if (recipe is null)
        {
            AddLeaf(itemId, quantityNeeded, classJobId, accumulator);
            return;
        }

        // ceil(quantityNeeded / amountPerCraft) batches. Defensive
        // clamp on amountPerCraft so a malformed recipe row (yielding 0
        // per craft) doesn't divide-by-zero.
        var amountPerCraft = (int)Math.Max(1u, recipe.Value.AmountResult);
        var craftCount = (quantityNeeded + amountPerCraft - 1) / amountPerCraft;

        pathAncestors.Add(itemId);
        try
        {
            foreach (var ingredient in recipe.Value.Ingredients)
            {
                if (ingredient.ItemId == 0 || ingredient.Amount == 0) continue;
                var totalIngredient = checked(craftCount * (int)ingredient.Amount);
                WalkRecipe(
                    itemId: ingredient.ItemId,
                    quantityNeeded: totalIngredient,
                    classJobId: classJobId,
                    accumulator: accumulator,
                    pathAncestors: pathAncestors);
            }
        }
        finally
        {
            pathAncestors.Remove(itemId);
        }
    }

    private void AddLeaf(
        uint itemId,
        int quantity,
        byte classJobId,
        Dictionary<uint, MaterialRequirement> accumulator)
    {
        if (!accumulator.TryGetValue(itemId, out var requirement))
        {
            var hasDisplay = source.TryGetItemDisplay(itemId, out var name, out var iconId);
            requirement = new MaterialRequirement
            {
                ItemId = itemId,
                ItemName = hasDisplay ? name : $"#{itemId}",
                IconId = hasDisplay ? iconId : (ushort)0,
                Source = source.ClassifyLeaf(itemId),
            };
            accumulator[itemId] = requirement;
        }
        requirement.Quantity += quantity;
        requirement.NeededByClassJobs.Add(classJobId);
    }
}
