using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace WondrousTailsSolver.Services;

/// <summary>
/// Thin adapter that pulls the current Wondrous Tails stamp mask out of
/// <see cref="PlayerState"/>.
///
/// Returns <c>null</c> when the player doesn't currently hold a Wondrous
/// Tails journal (the addon can be open for inspection from an NPC even
/// when the player hasn't picked one up — we just hide the overlay in
/// that case).
///
/// All access goes through the public partial methods on
/// <see cref="PlayerState"/> rather than the raw <c>WeeklyBingoStickers</c>
/// field. That field is <c>private</c> on the C# binding and its bit
/// layout has changed between game patches in the past; the public
/// <c>IsWeeklyBingoStickerPlaced(int)</c> accessor is the stable surface.
/// </summary>
internal sealed class BingoStateReader
{
    /// <summary>
    /// Snapshot the current bingo board as a 16-bit stamp mask, or
    /// <c>null</c> if the journal isn't held.
    /// </summary>
    public unsafe ushort? ReadBoard()
    {
        var state = PlayerState.Instance();
        if (state == null) return null;
        if (!state->HasWeeklyBingoJournal) return null;

        ushort mask = 0;
        for (var i = 0; i < Solver.BingoBoard.Cells; i++)
        {
            if (state->IsWeeklyBingoStickerPlaced(i))
                mask |= (ushort)(1 << i);
        }
        return mask;
    }
}
