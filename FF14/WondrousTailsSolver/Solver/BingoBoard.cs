using System.Numerics;

namespace WondrousTailsSolver.Solver;

/// <summary>
/// Pure, allocation-free model of the 4x4 Wondrous Tails board.
///
/// Cells are addressed by row-major index 0..15 (row r, column c → 4*r + c).
/// A stamp set is a 16-bit mask where bit i means cell i is stamped.
///
/// "Lines" are the 10 winning patterns the game pays out on: 4 rows,
/// 4 columns, and 2 diagonals. We pre-compute their masks once so
/// completion checks are a single AND + equality.
/// </summary>
internal static class BingoBoard
{
    /// <summary>Side length of the bingo grid.</summary>
    public const int Side = 4;

    /// <summary>Number of cells (16).</summary>
    public const int Cells = Side * Side;

    /// <summary>Maximum stamps a single book can hold (game-imposed).</summary>
    public const int MaxStamps = 9;

    /// <summary>
    /// All 10 line masks: 4 rows, 4 columns, 2 diagonals.
    /// A line is complete iff <c>(placed &amp; mask) == mask</c>.
    /// </summary>
    public static readonly ushort[] LineMasks = BuildLineMasks();

    /// <summary>
    /// Count completed lines on the given stamp mask.
    /// </summary>
    public static int CountLines(ushort placed)
    {
        var n = 0;
        foreach (var line in LineMasks)
            if ((placed & line) == line) n++;
        return n;
    }

    /// <summary>
    /// Number of stamped cells on the given mask. Always 0..16.
    /// </summary>
    public static int StampCount(ushort placed) => BitOperations.PopCount(placed);

    private static ushort[] BuildLineMasks()
    {
        var masks = new ushort[10];
        var i = 0;

        // Rows.
        for (var r = 0; r < Side; r++)
        {
            ushort row = 0;
            for (var c = 0; c < Side; c++)
                row |= (ushort)(1 << (r * Side + c));
            masks[i++] = row;
        }

        // Columns.
        for (var c = 0; c < Side; c++)
        {
            ushort col = 0;
            for (var r = 0; r < Side; r++)
                col |= (ushort)(1 << (r * Side + c));
            masks[i++] = col;
        }

        // Diagonals.
        ushort d1 = 0, d2 = 0;
        for (var k = 0; k < Side; k++)
        {
            d1 |= (ushort)(1 << (k * Side + k));
            d2 |= (ushort)(1 << (k * Side + (Side - 1 - k)));
        }
        masks[i++] = d1;
        masks[i++] = d2;

        return masks;
    }
}
