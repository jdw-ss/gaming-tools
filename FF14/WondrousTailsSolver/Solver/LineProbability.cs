using System;

namespace WondrousTailsSolver.Solver;

/// <summary>
/// Combinatorial probabilities for Wondrous Tails line completion.
///
/// "Current" probabilities assume the player keeps their placed stamps
/// and earns the remaining stamps (up to <see cref="BingoBoard.MaxStamps"/>)
/// uniformly at random on the unstamped cells. This is the correct marginal:
/// the duty-to-cell assignment is uniform random and independent of which
/// duties the player happens to clear, so each k-subset of unstamped cells
/// is equally likely to be where the next k stamps land.
///
/// "Shuffle" probabilities are the empty-board baseline: 9 stamps placed
/// uniformly at random across all 16 cells. They're shown only when the
/// player still has a meaningful shuffle decision to make
/// (<see cref="ShuffleVisibleStampThreshold"/>). The baseline answers
/// "is my current board above or below the long-run average?"
///
/// All probabilities are computed by exact enumeration. The largest
/// search space is C(16, 9) = 11,440 boards, which runs in well under
/// a millisecond on cold cache.
/// </summary>
internal static class LineProbability
{
    /// <summary>
    /// The shuffle baseline is only shown when at most this many stamps
    /// have been placed. Beyond it the player is too committed for the
    /// comparison to inform a real decision.
    /// </summary>
    public const int ShuffleVisibleStampThreshold = 7;

    /// <summary>
    /// Result of a probability calculation. Probabilities are in [0, 1].
    /// </summary>
    public readonly struct Result
    {
        /// <summary>P(≥1 completed line) given current placement.</summary>
        public double CurrentOneLine { get; init; }
        /// <summary>P(≥2 completed lines) given current placement.</summary>
        public double CurrentTwoLines { get; init; }
        /// <summary>P(≥3 completed lines) given current placement.</summary>
        public double CurrentThreeLines { get; init; }

        /// <summary>Whether the shuffle baseline is meaningful for this board.</summary>
        public bool ShuffleApplicable { get; init; }
        /// <summary>P(≥1 line) on a freshly shuffled board with 9 stamps.</summary>
        public double ShuffleOneLine { get; init; }
        /// <summary>P(≥2 lines) on a freshly shuffled board with 9 stamps.</summary>
        public double ShuffleTwoLines { get; init; }
        /// <summary>P(≥3 lines) on a freshly shuffled board with 9 stamps.</summary>
        public double ShuffleThreeLines { get; init; }

        /// <summary>
        /// Maximum number of completed lines achievable from this board if
        /// the player gets to place every remaining stamp optimally — i.e.
        /// the upper bound across every possible draw outcome. Derived from
        /// the same enumeration as the "Current" probabilities; the
        /// "Best" properties below collapse this to per-threshold 0/1.
        /// </summary>
        public int MaxLineCount { get; init; }

        /// <summary>P(≥1 line) under perfect remaining-stamp placement: 1 if achievable, 0 otherwise.</summary>
        public double BestOneLine => MaxLineCount >= 1 ? 1.0 : 0.0;
        /// <summary>P(≥2 lines) under perfect remaining-stamp placement: 1 if achievable, 0 otherwise.</summary>
        public double BestTwoLines => MaxLineCount >= 2 ? 1.0 : 0.0;
        /// <summary>P(≥3 lines) under perfect remaining-stamp placement: 1 if achievable, 0 otherwise.</summary>
        public double BestThreeLines => MaxLineCount >= 3 ? 1.0 : 0.0;
    }

    /// <summary>
    /// Compute current and shuffle probabilities for the given placed mask.
    /// </summary>
    /// <param name="placed">16-bit stamp mask. Bits beyond 16 are ignored.</param>
    public static Result Compute(ushort placed)
    {
        var stamps = BingoBoard.StampCount(placed);
        var remaining = Math.Max(0, BingoBoard.MaxStamps - stamps);

        // Current: distribute `remaining` additional stamps uniformly over
        // the unstamped cells of `placed`.
        var current = EnumerateLineDistribution(placed, remaining);

        // Shuffle baseline: 9 stamps distributed over all 16 cells from scratch.
        var shuffleApplicable = stamps <= ShuffleVisibleStampThreshold;
        var shuffle = shuffleApplicable
            ? EnumerateLineDistribution(placed: 0, addStamps: BingoBoard.MaxStamps)
            : default;

        return new Result
        {
            CurrentOneLine = current.AtLeast(1),
            CurrentTwoLines = current.AtLeast(2),
            CurrentThreeLines = current.AtLeast(3),
            ShuffleApplicable = shuffleApplicable,
            ShuffleOneLine = shuffle.AtLeast(1),
            ShuffleTwoLines = shuffle.AtLeast(2),
            ShuffleThreeLines = shuffle.AtLeast(3),
            MaxLineCount = current.MaxObservedLines,
        };
    }

    /// <summary>
    /// Bucketed line-count distribution over all ways to place
    /// <paramref name="addStamps"/> additional stamps on cells currently
    /// unset in <paramref name="placed"/>.
    /// </summary>
    private static LineDistribution EnumerateLineDistribution(ushort placed, int addStamps)
    {
        // Collect the bit positions that are still unstamped.
        Span<int> openBits = stackalloc int[BingoBoard.Cells];
        var openCount = 0;
        for (var i = 0; i < BingoBoard.Cells; i++)
            if ((placed & (1 << i)) == 0)
                openBits[openCount++] = i;

        var dist = new LineDistribution();

        // Edge cases first: nothing to add, or impossible request.
        if (addStamps <= 0)
        {
            dist.Add(BingoBoard.CountLines(placed));
            return dist;
        }
        if (addStamps > openCount)
        {
            // Shouldn't happen in practice (the game caps stamps), but be
            // defensive - treat as "no additional placement possible".
            dist.Add(BingoBoard.CountLines(placed));
            return dist;
        }

        // Walk every k-subset of the open bits via combinatorial recursion.
        Span<int> chosen = stackalloc int[addStamps];
        Recurse(openBits[..openCount], addStamps, 0, chosen, 0, placed, ref dist);
        return dist;
    }

    /// <summary>
    /// Recursively enumerate every k-subset of <paramref name="open"/> and
    /// tally the completed-line count of the resulting board.
    /// </summary>
    private static void Recurse(
        ReadOnlySpan<int> open,
        int needed,
        int startIndex,
        Span<int> chosen,
        int chosenCount,
        ushort placed,
        ref LineDistribution dist)
    {
        if (chosenCount == needed)
        {
            ushort board = placed;
            for (var i = 0; i < chosenCount; i++)
                board |= (ushort)(1 << chosen[i]);
            dist.Add(BingoBoard.CountLines(board));
            return;
        }

        var remainingToPick = needed - chosenCount;
        var lastValidStart = open.Length - remainingToPick;
        for (var i = startIndex; i <= lastValidStart; i++)
        {
            chosen[chosenCount] = open[i];
            Recurse(open, needed, i + 1, chosen, chosenCount + 1, placed, ref dist);
        }
    }

    /// <summary>
    /// Histogram over completed-line counts (0..10 possible, indexed
    /// directly). Tracks the total combinations seen so probabilities can
    /// be derived lazily.
    /// </summary>
    private struct LineDistribution
    {
        // 11 buckets covers 0 through 10 lines (the absolute maximum on a
        // 4x4 with all 10 lines complete).
        private long b0, b1, b2, b3, b4, b5, b6, b7, b8, b9, b10;
        private long total;
        private int maxObserved;

        /// <summary>
        /// Highest line count seen across every enumerated board. This is
        /// what "Best" / max-achievable reports: the upper bound under
        /// perfect remaining-stamp placement.
        /// </summary>
        public int MaxObservedLines => maxObserved;

        public void Add(int lineCount)
        {
            total++;
            if (lineCount > maxObserved) maxObserved = lineCount;
            switch (lineCount)
            {
                case 0: b0++; break;
                case 1: b1++; break;
                case 2: b2++; break;
                case 3: b3++; break;
                case 4: b4++; break;
                case 5: b5++; break;
                case 6: b6++; break;
                case 7: b7++; break;
                case 8: b8++; break;
                case 9: b9++; break;
                default: b10++; break;
            }
        }

        /// <summary>
        /// P(line count ≥ <paramref name="threshold"/>). Returns 0 for an
        /// empty distribution.
        /// </summary>
        public double AtLeast(int threshold)
        {
            if (total == 0) return 0.0;
            long atOrAbove = threshold switch
            {
                <= 0 => total,
                1 => total - b0,
                2 => total - b0 - b1,
                3 => total - b0 - b1 - b2,
                4 => b4 + b5 + b6 + b7 + b8 + b9 + b10,
                5 => b5 + b6 + b7 + b8 + b9 + b10,
                6 => b6 + b7 + b8 + b9 + b10,
                7 => b7 + b8 + b9 + b10,
                8 => b8 + b9 + b10,
                9 => b9 + b10,
                _ => b10,
            };
            return (double)atOrAbove / total;
        }
    }
}
