using System.Collections.Generic;
using Dalamud.Plugin.Services;
using GcSupplyHelper.Models;
using LuminaItem = Lumina.Excel.Sheets.Item;
using LuminaRecipe = Lumina.Excel.Sheets.Recipe;
using LuminaGatheringItem = Lumina.Excel.Sheets.GatheringItem;

namespace GcSupplyHelper.Services;

/// <summary>
/// Production <see cref="IRecipeDataSource"/> built from Lumina Excel
/// sheets. Eager-builds two lookup tables on construction:
///
/// <list type="bullet">
///   <item><c>recipesByResultItem</c> — every Recipe row keyed by its
///   produced <c>ItemResult.RowId</c>. ~2,700 rows; one pass to build.</item>
///   <item><c>gatheredItemIds</c> — set of item ids that appear in
///   <c>GatheringItem.Item</c>. Slightly broader than "node-droppable"
///   but matches the user-facing "is this gathered" question well.</item>
/// </list>
///
/// Item display lookups (name + icon) are lazy and per-call cached;
/// the Item sheet has ~30,000 rows and we only ever need names for the
/// 11 turn-ins and however many leaves their recipes expand to.
/// </summary>
internal sealed class LuminaRecipeDataSource : IRecipeDataSource
{
    private readonly IDataManager dataManager;
    private readonly IPluginLog log;
    private readonly Dictionary<uint, RecipeData> recipesByResultItem;
    private readonly HashSet<uint> gatheredItemIds;
    private readonly Dictionary<uint, (string Name, ushort IconId)> displayCache = new();

    public LuminaRecipeDataSource(IDataManager dataManager, IPluginLog log)
    {
        this.dataManager = dataManager;
        this.log = log;
        recipesByResultItem = BuildRecipeLookup();
        gatheredItemIds = BuildGatheredSet();
        log.Information(
            "GcSupplyHelper: indexed {Recipes} recipes and {Gathers} gathered items.",
            recipesByResultItem.Count, gatheredItemIds.Count);
    }

    public bool IsGathered(uint itemId) => gatheredItemIds.Contains(itemId);

    public RecipeData? GetRecipe(uint itemId)
        => recipesByResultItem.TryGetValue(itemId, out var recipe) ? recipe : null;

    public bool TryGetItemDisplay(uint itemId, out string name, out ushort iconId)
    {
        if (displayCache.TryGetValue(itemId, out var cached))
        {
            name = cached.Name;
            iconId = cached.IconId;
            return true;
        }

        var sheet = dataManager.GetExcelSheet<LuminaItem>();
        if (!sheet.TryGetRow(itemId, out var row))
        {
            name = string.Empty;
            iconId = 0;
            return false;
        }

        name = row.Name.ExtractText();
        iconId = row.Icon;
        displayCache[itemId] = (name, iconId);
        return true;
    }

    public MaterialSource ClassifyLeaf(uint itemId)
    {
        if (gatheredItemIds.Contains(itemId)) return MaterialSource.Gathered;
        if (recipesByResultItem.ContainsKey(itemId)) return MaterialSource.Crafted;

        // No recipe, not gathered. Fall back to vendor as the most
        // common case for GC-supply ingredients (shards, crystals,
        // base reagents). Anything we genuinely can't classify lands
        // as Unknown.
        var sheet = dataManager.GetExcelSheet<LuminaItem>();
        if (!sheet.TryGetRow(itemId, out var row)) return MaterialSource.Unknown;

        // Item.ItemSearchCategory.RowId != 0 means the item appears in
        // the in-game Market Board / vendor search UI in some capacity.
        // A weak signal but better than nothing. The proper vendor map
        // is a v0.2 enhancement.
        return row.ItemSearchCategory.RowId != 0
            ? MaterialSource.Vendor
            : MaterialSource.Unknown;
    }

    private Dictionary<uint, RecipeData> BuildRecipeLookup()
    {
        var sheet = dataManager.GetExcelSheet<LuminaRecipe>();
        var map = new Dictionary<uint, RecipeData>(sheet.Count);
        foreach (var recipe in sheet)
        {
            var resultId = recipe.ItemResult.RowId;
            if (resultId == 0) continue;

            var ingredients = new List<RecipeIngredient>(8);
            for (var i = 0; i < recipe.Ingredient.Count; i++)
            {
                var ingredientItem = recipe.Ingredient[i];
                var amount = recipe.AmountIngredient[i];
                if (ingredientItem.RowId == 0 || amount == 0) continue;
                ingredients.Add(new RecipeIngredient(ingredientItem.RowId, amount));
            }
            if (ingredients.Count == 0) continue;

            // Some items have multiple recipes (one per crafting class).
            // The ingredient list and output amount are normally
            // identical across them, so first-write-wins is fine.
            if (!map.ContainsKey(resultId))
            {
                map[resultId] = new RecipeData(
                    AmountResult: recipe.AmountResult,
                    Ingredients: ingredients);
            }
        }
        return map;
    }

    private HashSet<uint> BuildGatheredSet()
    {
        var sheet = dataManager.GetExcelSheet<LuminaGatheringItem>();
        var set = new HashSet<uint>();
        foreach (var entry in sheet)
        {
            // GatheringItem.Item is typed as a RowRef<Item> in the
            // strongly-typed schema and as a plain integer reference in
            // the generic schema; either way, RowId is what we want.
            var itemRowId = (uint)entry.Item.RowId;
            if (itemRowId != 0) set.Add(itemRowId);
        }
        return set;
    }
}
