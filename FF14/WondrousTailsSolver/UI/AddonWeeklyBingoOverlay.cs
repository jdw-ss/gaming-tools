using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Classes;
using KamiToolKit.Controllers;
using KamiToolKit.Nodes;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using WondrousTailsSolver.Services;
using WondrousTailsSolver.Solver;

namespace WondrousTailsSolver.UI;

/// <summary>
/// Owns the lifecycle of a single <see cref="TextNode"/> attached to the
/// game's <c>WeeklyBingo</c> addon. The node displays the row/column
/// completion probabilities for the player's current Wondrous Tails board.
///
/// We attach on the addon's PostSetup, refresh on PostRefresh + PostUpdate
/// (which fires whenever the addon's "requested update" runs — covers the
/// player placing a sticker, shuffling, etc.), and detach on PreFinalize.
///
/// KamiToolKit's <see cref="AddonController{T}"/> handles event
/// (un)registration and main-thread assertions for us; we just plug in
/// the callbacks.
/// </summary>
internal sealed class AddonWeeklyBingoOverlay : IDisposable
{
    /// <summary>Internal addon name as used by the game's addon table.</summary>
    private const string AddonName = "WeeklyBingo";

    /// <summary>
    /// Above this fraction of the shuffle baseline we paint green
    /// (current trajectory is better than the long-run average).
    /// </summary>
    private const double GreenRatio = 1.05;

    /// <summary>
    /// Below this fraction of the shuffle baseline we paint red
    /// (current trajectory is noticeably worse than the baseline).
    /// </summary>
    private const double RedRatio = 0.95;

    /// <summary>
    /// A probability this close to certainty paints "bright" regardless
    /// of the shuffle baseline. Mirrors the original plugin's behaviour
    /// of glowing the line counts that are effectively locked in.
    /// </summary>
    private const double BrightThreshold = 0.999;

    private readonly Configuration config;
    private readonly BingoStateReader stateReader;
    private readonly IPluginLog log;

    private AddonController<AddonWeeklyBingo>? controller;
    private TextNode? overlayText;
    private bool attachFailed;

    public AddonWeeklyBingoOverlay(Configuration config, BingoStateReader stateReader, IPluginLog log)
    {
        this.config = config;
        this.stateReader = stateReader;
        this.log = log;
    }

    /// <summary>
    /// Begin listening for the WeeklyBingo addon. Safe to call once at
    /// plugin construction; the controller covers all subsequent
    /// open/close cycles.
    ///
    /// Marked unsafe because converting the OnSetup/OnFinalize/OnRefresh/
    /// OnUpdate method groups to KamiToolKit's
    /// <c>AddonControllerEvent</c> delegate (which takes a <c>T*</c>)
    /// counts as pointer use under the C# language rules.
    /// </summary>
    public unsafe void Enable()
    {
        if (controller is not null) return;

        controller = new AddonController<AddonWeeklyBingo>
        {
            AddonName = AddonName,
            OnSetup = OnSetup,
            OnFinalize = OnFinalize,
            OnRefresh = OnRefresh,
            OnUpdate = OnUpdate,
        };
        controller.Enable();
    }

    /// <summary>
    /// Force the overlay text to repaint with the latest state. Called
    /// when the user toggles the overlay via /wts so the change takes
    /// effect without waiting for the next addon Update.
    /// </summary>
    public void ForceRefresh() => RefreshText();

    private unsafe void OnSetup(AddonWeeklyBingo* addon)
    {
        try
        {
            overlayText = new TextNode
            {
                Size = new Vector2(420f, 28f),
                Position = new Vector2(20f, 12f),
                FontSize = 14,
                AlignmentType = AlignmentType.Left,
                TextColor = ColorWhite,
                TextOutlineColor = ColorOutline,
                String = default,
                IsVisible = true,
            };
            overlayText.AddTextFlags(TextFlags.Edge);
            overlayText.AttachNode((AtkUnitBase*)addon);
            attachFailed = false;
            RefreshText();
        }
        catch (Exception ex)
        {
            attachFailed = true;
            log.Error(ex, "WondrousTailsSolver: failed to attach overlay node to WeeklyBingo. Overlay disabled for this open of the addon.");
            DisposeOverlayNode();
        }
    }

    private unsafe void OnFinalize(AddonWeeklyBingo* _)
    {
        DisposeOverlayNode();
    }

    private unsafe void OnRefresh(AddonWeeklyBingo* _) => RefreshText();

    private unsafe void OnUpdate(AddonWeeklyBingo* _) => RefreshText();

    private void RefreshText()
    {
        if (overlayText is null || attachFailed) return;

        if (!config.ShowOverlay)
        {
            overlayText.IsVisible = false;
            return;
        }

        var board = stateReader.ReadBoard();
        if (board is null)
        {
            // Addon can be inspected without a journal; render nothing in
            // that case rather than leaving stale text from a previous open.
            overlayText.IsVisible = false;
            return;
        }

        var result = LineProbability.Compute(board.Value);
        overlayText.TextColor = PickHeadlineColour(result);
        overlayText.String = FormatOverlay(result);
        overlayText.IsVisible = true;
    }

    /// <summary>
    /// Build the overlay text. Three lines, one per completion threshold,
    /// with the current probability and (when meaningful) the shuffle
    /// baseline for comparison. Plain text — colouring is applied to the
    /// whole node based on the most informative threshold.
    /// </summary>
    private static ReadOnlySeString FormatOverlay(LineProbability.Result r)
    {
        var inv = CultureInfo.InvariantCulture;
        var builder = new SeStringBuilder();

        builder.Append("Wondrous Tails: ");
        builder.Append($"P(1+)={r.CurrentOneLine.ToString("P1", inv)}");
        builder.Append("  ");
        builder.Append($"P(2+)={r.CurrentTwoLines.ToString("P1", inv)}");
        builder.Append("  ");
        builder.Append($"P(3+)={r.CurrentThreeLines.ToString("P1", inv)}");

        if (r.ShuffleApplicable)
        {
            builder.Append("   |   Shuffle baseline: ");
            builder.Append($"{r.ShuffleOneLine.ToString("P1", inv)} / ");
            builder.Append($"{r.ShuffleTwoLines.ToString("P1", inv)} / ");
            builder.Append($"{r.ShuffleThreeLines.ToString("P1", inv)}");
        }

        return builder.ToReadOnlySeString();
    }

    /// <summary>
    /// Pick a single colour for the whole overlay based on the most
    /// generous (P≥1) threshold. The colour answers "am I trending above
    /// or below the long-run average?" at a glance; the actual numbers
    /// remain in the text for anyone who wants the detail.
    /// </summary>
    private static Vector4 PickHeadlineColour(LineProbability.Result r)
    {
        if (r.CurrentOneLine >= BrightThreshold) return ColorBright;
        if (!r.ShuffleApplicable) return ColorWhite;

        // Avoid divide-by-zero on a degenerate baseline (shouldn't happen
        // in practice — the shuffle baseline always has some line in 9 of 16).
        if (r.ShuffleOneLine <= 0.0) return ColorWhite;

        var ratio = r.CurrentOneLine / r.ShuffleOneLine;
        if (ratio >= GreenRatio) return ColorGreen;
        if (ratio >= RedRatio) return ColorYellow;
        if (r.CurrentOneLine > 0.001) return ColorRed;
        return ColorDarkRed;
    }

    private void DisposeOverlayNode()
    {
        if (overlayText is null) return;
        try
        {
            overlayText.Dispose();
        }
        catch (Exception ex)
        {
            log.Warning(ex, "WondrousTailsSolver: overlay node Dispose threw; continuing.");
        }
        overlayText = null;
    }

    public void Dispose()
    {
        DisposeOverlayNode();
        controller?.Dispose();
        controller = null;
    }

    // Colour palette. Vector4 = (R, G, B, A), each in [0, 1].
    private static readonly Vector4 ColorWhite   = new(1.00f, 1.00f, 1.00f, 1.00f);
    private static readonly Vector4 ColorOutline = new(0.00f, 0.00f, 0.00f, 1.00f);
    private static readonly Vector4 ColorBright  = new(1.00f, 0.95f, 0.55f, 1.00f);
    private static readonly Vector4 ColorGreen   = new(0.40f, 0.95f, 0.45f, 1.00f);
    private static readonly Vector4 ColorYellow  = new(0.95f, 0.90f, 0.40f, 1.00f);
    private static readonly Vector4 ColorRed     = new(0.95f, 0.45f, 0.40f, 1.00f);
    private static readonly Vector4 ColorDarkRed = new(0.55f, 0.20f, 0.20f, 1.00f);
}
