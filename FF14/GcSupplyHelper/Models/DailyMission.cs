namespace GcSupplyHelper.Models;

/// <summary>
/// One of the player's 11 daily Grand Company turn-ins for a given game
/// day. Sourced from <c>AgentGrandCompanySupply</c> at runtime — see
/// <see cref="Services.SupplyMissionReader"/>.
/// </summary>
/// <param name="ItemId">
/// Lumina <c>Item</c> row id of the item to turn in. The finished item,
/// not its ingredients — recipe expansion happens in
/// <see cref="Services.RecipeWalker"/>.
/// </param>
/// <param name="QuantityRequired">
/// Number of copies of <paramref name="ItemId"/> the mission asks for.
/// Currently always 3 in retail, but the game struct exposes it as a
/// byte so we propagate it faithfully.
/// </param>
/// <param name="ClassJobId">
/// Lumina <c>ClassJob</c> row id for the class this mission belongs to.
/// CRP=8, BSM=9, ARM=10, GSM=11, LTW=12, WVR=13, ALC=14, CUL=15,
/// MIN=16, BTN=17, FSH=18. Cross-checked against the item's recipe (or
/// gathering job) rather than trusted from the agent's array index
/// alone — see Gaming Tools/CLAUDE.md "Class-job index mapping" gotcha.
/// </param>
/// <param name="IsProvisioning">
/// True for the three Provisioning Mission entries (read from the
/// agent's <c>_provisioningData[3]</c> array); false for the eight
/// Supply Mission entries from <c>_supplyData[8]</c>. Purely for
/// labelling in the per-mission view.
/// </param>
internal readonly record struct DailyMission(
    uint ItemId,
    byte QuantityRequired,
    byte ClassJobId,
    bool IsProvisioning);
