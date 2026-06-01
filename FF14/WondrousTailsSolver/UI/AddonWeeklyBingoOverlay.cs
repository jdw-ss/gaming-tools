using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using WondrousTailsSolver.Services;
using WondrousTailsSolver.Solver;

namespace WondrousTailsSolver.UI;

/// <summary>
/// Floating ImGui window that displays Wondrous Tails completion
/// probabilities. The window is anchored to the left edge of the in-game
/// <c>WeeklyBingo</c> addon and is only visible while that addon is open.
///
/// v0.1 rendered this inside the addon via a native KamiToolKit
/// <c>AtkTextNode</c>. That clipped the addon's existing title bar at
/// <c>Position = (20, 12)</c> and was unreadable. v0.1.2 replaces it
/// with this ImGui window, which sits cleanly beside the journal and
/// removes the entire KamiToolKit dependency chain (KamiToolKit,
/// SixLabors.ImageSharp, Microsoft.Extensions.ObjectPool).
/// </summary>
internal sealed class AddonWeeklyBingoOverlay : Window, IDisposable
{
    private const string AddonName = "WeeklyBingo";

    /// <summary>Width of the floating window, in unscaled ImGui pixels.</summary>
    private const float WindowWidth = 280f;

    /// <summary>Gap between the right edge of our window and the addon's left edge.</summary>
    private const float MarginFromAddon = 8f;

    private readonly Configuration config;
    private readonly BingoStateReader stateReader;
    private readonly IGameGui gameGui;

    public AddonWeeklyBingoOverlay(
        Configuration config,
        BingoStateReader stateReader,
        IGameGui gameGui)
        : base("Wondrous Tails Odds##wts-overlay",
               ImGuiWindowFlags.NoResize |
               ImGuiWindowFlags.NoCollapse |
               ImGuiWindowFlags.NoFocusOnAppearing |
               ImGuiWindowFlags.NoNav |
               ImGuiWindowFlags.NoSavedSettings |
               ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.stateReader = stateReader;
        this.gameGui = gameGui;

        // We position ourselves every frame from the addon's coordinates,
        // so don't let the user drag-move us and don't restore from any
        // persisted ImGui ini state.
        RespectCloseHotkey = false;
        DisableWindowSounds = true;
        ShowCloseButton = false;
        IsOpen = true;
    }

    /// <summary>
    /// Only draw when the user hasn't disabled the overlay AND the game's
    /// WeeklyBingo addon is currently visible. The pointer check is the
    /// authoritative test for "is the journal on screen right now?" — we
    /// don't track addon lifecycle separately because Dalamud already
    /// renders us on the framework thread, where this read is safe.
    /// </summary>
    public override unsafe bool DrawConditions()
    {
        if (!config.ShowOverlay) return false;
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(AddonName).Address;
        return addon != null && addon->IsVisible;
    }

    /// <summary>
    /// Anchor the window to the left side of the addon. Runs every frame
    /// so the overlay follows when the user drags the journal around.
    /// </summary>
    public override unsafe void PreDraw()
    {
        var addon = (AtkUnitBase*)gameGui.GetAddonByName(AddonName).Address;
        if (addon == null) return;

        var addonX = addon->X;
        var addonY = addon->Y;

        var x = MathF.Max(0f, addonX - (WindowWidth + MarginFromAddon));
        ImGui.SetNextWindowPos(new Vector2(x, addonY));
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, 0f), ImGuiCond.Always);
    }

    public override void Draw()
    {
        var board = stateReader.ReadBoard();
        if (board is null)
        {
            ImGui.TextDisabled("No Wondrous Tails journal held.");
            return;
        }

        var r = LineProbability.Compute(board.Value);
        var stamps = BingoBoard.StampCount(board.Value);
        var inv = CultureInfo.InvariantCulture;

        if (ImGui.BeginTable("##wts-prob", 3, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthStretch, 1.4f);
            ImGui.TableSetupColumn("Current",    ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("Best",       ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableHeadersRow();

            DrawRow("P(\u22651 line)",  r.CurrentOneLine,    LineProbability.OptimalSevenStampOneLine,    inv);
            DrawRow("P(\u22652 lines)", r.CurrentTwoLines,   LineProbability.OptimalSevenStampTwoLines,   inv);
            DrawRow("P(\u22653 lines)", r.CurrentThreeLines, LineProbability.OptimalSevenStampThreeLines, inv);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (r.ShuffleApplicable)
        {
            ImGui.TextDisabled("Shuffle baseline (9 random stamps):");
            ImGui.Text($"  P(1+) {Format(r.ShuffleOneLine, inv)}   " +
                       $"P(2+) {Format(r.ShuffleTwoLines, inv)}   " +
                       $"P(3+) {Format(r.ShuffleThreeLines, inv)}");
            ImGui.Spacing();
        }

        ImGui.TextDisabled($"{stamps} of {BingoBoard.MaxStamps} stamps placed");
        ImGui.TextDisabled($"Max achievable: {r.MaxLineCount} line(s)");
        ImGui.TextDisabled($"Best = optimal {LineProbability.OptimalReshuffleStamps}-stamp target");
    }

    private static void DrawRow(string label, double current, double best, CultureInfo inv)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(label);

        ImGui.TableNextColumn();
        ImGui.TextColored(PickCurrentColor(current, best), Format(current, inv));

        ImGui.TableNextColumn();
        ImGui.TextDisabled(Format(best, inv));
    }

    private static string Format(double p, CultureInfo inv) => p.ToString("P1", inv);

    /// <summary>
    /// Colour the Current value by how close it sits to the optimal
    /// <see cref="LineProbability.OptimalReshuffleStamps"/>-stamp target:
    /// matching or exceeding is green, near is yellow, far below is red,
    /// zero is dark red. A target of 0 (impossible threshold) collapses
    /// to neutral white — no comparison is meaningful.
    /// </summary>
    private static Vector4 PickCurrentColor(double current, double best)
    {
        if (best <= 1e-9) return ColorWhite;
        if (current >= 0.999) return ColorBright;
        if (current >= best - 1e-6) return ColorGreen;
        if (current >= best * 0.8) return ColorYellow;
        if (current > 0.001) return ColorRed;
        return ColorDarkRed;
    }

    public void Dispose()
    {
        // Nothing held that ImGui doesn't reclaim itself; we don't register
        // anything outside the Dalamud WindowSystem.
    }

    // Colour palette. Vector4 = (R, G, B, A), each in [0, 1].
    private static readonly Vector4 ColorWhite   = new(1.00f, 1.00f, 1.00f, 1.00f);
    private static readonly Vector4 ColorBright  = new(1.00f, 0.95f, 0.55f, 1.00f);
    private static readonly Vector4 ColorGreen   = new(0.40f, 0.95f, 0.45f, 1.00f);
    private static readonly Vector4 ColorYellow  = new(0.95f, 0.90f, 0.40f, 1.00f);
    private static readonly Vector4 ColorRed     = new(0.95f, 0.45f, 0.40f, 1.00f);
    private static readonly Vector4 ColorDarkRed = new(0.55f, 0.20f, 0.20f, 1.00f);
}
