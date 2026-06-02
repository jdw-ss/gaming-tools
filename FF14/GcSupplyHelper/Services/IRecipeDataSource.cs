using System.Collections.Generic;
using GcSupplyHelper.Models;

namespace GcSupplyHelper.Services;

/// <summary>
/// Data the <see cref="RecipeWalker"/> needs to traverse a recipe tree
/// and classify leaves. Production code implements this against the
/// Lumina <c>Item</c> / <c>Recipe</c> / <c>RecipeLookup</c> sheets via
/// <see cref="LuminaRecipeDataSource"/>; tests implement it with
/// hand-crafted fixtures so the walker can be exercised off-tree
/// without any Dalamud surface available.
/// </summary>
internal interface IRecipeDataSource
{
    /// <summary>
    /// True iff the item is a raw gathered material (Botanist, Miner,
    /// Fisher node drop). Returns <c>false</c> for items with no
    /// gathering record even if they also lack a recipe; the caller
    /// should fall back to <see cref="GetRecipe"/> to decide.
    /// </summary>
    bool IsGathered(uint itemId);

    /// <summary>
    /// The recipe used to craft <paramref name="itemId"/>, or
    /// <c>null</c> if the item has no recipe. Returning a non-null
    /// recipe takes precedence over <see cref="IsGathered"/> — the
    /// walker recurses through any item that has a recipe.
    /// </summary>
    RecipeData? GetRecipe(uint itemId);

    /// <summary>
    /// Display name + icon row id for the item. Used both for the
    /// per-mission header (the turn-in item) and for leaf rendering in
    /// the aggregate table.
    /// </summary>
    bool TryGetItemDisplay(uint itemId, out string name, out ushort iconId);

    /// <summary>
    /// Best-effort classification of the source of a leaf material.
    /// Implementations should answer <see cref="MaterialSource.Gathered"/>
    /// when <see cref="IsGathered"/> would return true; the other
    /// classifications are heuristics and may improve in later versions.
    /// </summary>
    MaterialSource ClassifyLeaf(uint itemId);
}

/// <summary>
/// One ingredient of a recipe: the item to consume and how many of it
/// per single craft.
/// </summary>
internal readonly record struct RecipeIngredient(uint ItemId, uint Amount);

/// <summary>
/// A recipe's outcome and ingredient list, pruned of the empty slots
/// (Recipe.ItemIngredient0..7 sometimes have trailing zeroes).
/// </summary>
internal readonly record struct RecipeData(
    uint AmountResult,
    IReadOnlyList<RecipeIngredient> Ingredients);
