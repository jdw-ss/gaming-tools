using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BulkDesynth.Models;
using BulkDesynth.Services;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;

namespace BulkDesynth.Windows;

/// <summary>
/// Single Targeting tab (plus a Settings tab) covering everything:
///   - container toggles + quick-select buttons
///   - inline horizontal filter rows (name+id, ilvl min/max, spiritbond+HQ)
///   - preview table that shrinks live as items are processed during a run
///   - run status (processed / remaining / current / stop) folded in below
///     the preview so there's no need to tab-switch mid-run
///
/// We never invoke the executor automatically. The preview must be explicitly
/// confirmed via the red "Desynth N items" button.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly InventoryScanner scanner;
    private readonly DesynthExecutor executor;
    private readonly IFramework framework;
    private readonly Configuration config;

    // Container toggles. Default to "every main bag" because that's the
    // common case (armoury is opt-in since gear there is more likely to
    // be valuable).
    private readonly bool[] bagSelected = { true, true, true, true };
    private bool armourySelected;

    // Filter parameters. All optional / AND'd together with the bag set.
    private string nameInput = string.Empty;
    private string itemIdInput = string.Empty;
    private int minIlvl;
    private int maxIlvl = 999;
    private bool excludeHq = true;
    private int maxSpiritbond = 100;

    // Static preview built by "Build preview". While a run is in progress
    // the table actually renders executor.RemainingItems instead, so the
    // user sees rows disappear as each item is processed.
    private List<DesynthCandidate> preview = new();
    private string previewSummary = string.Empty;

    public MainWindow(InventoryScanner scanner, DesynthExecutor executor, IFramework framework, Configuration config)
        : base("Bulk Desynth##bds-main", ImGuiWindowFlags.None)
    {
        this.scanner = scanner;
        this.executor = executor;
        this.framework = framework;
        this.config = config;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 460),
            MaximumSize = new Vector2(1200, 900),
        };
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("bds-tabs");
        if (!tabs) return;

        DrawTargetingTab();
        DrawSettingsTab();
    }

    private void DrawTargetingTab()
    {
        using var tab = ImRaii.TabItem("Targeting");
        if (!tab) return;

        // --- Container toggles -------------------------------------------
        ImGui.TextDisabled("Scan these containers:");
        ImGui.Checkbox("Bag 1", ref bagSelected[0]); ImGui.SameLine();
        ImGui.Checkbox("Bag 2", ref bagSelected[1]); ImGui.SameLine();
        ImGui.Checkbox("Bag 3", ref bagSelected[2]); ImGui.SameLine();
        ImGui.Checkbox("Bag 4", ref bagSelected[3]); ImGui.SameLine();
        ImGui.Checkbox("Armoury chest", ref armourySelected);

        if (ImGui.SmallButton("All bags"))
        {
            for (var i = 0; i < bagSelected.Length; i++) bagSelected[i] = true;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("No bags"))
        {
            for (var i = 0; i < bagSelected.Length; i++) bagSelected[i] = false;
            armourySelected = false;
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Bags + armoury"))
        {
            for (var i = 0; i < bagSelected.Length; i++) bagSelected[i] = true;
            armourySelected = true;
        }

        ImGui.Separator();

        // --- Filters (3 rows of horizontal pairs) ------------------------
        ImGui.TextDisabled("Filters (all optional - empty fields mean no constraint):");

        // Row 1: name (left, wider) + item id (right, narrow)
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Name");
        ImGui.SameLine();
        ImGui.PushItemWidth(200);
        ImGui.InputTextWithHint("##bds-name", "e.g. Hempen", ref nameInput, 64);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Item ID");
        ImGui.SameLine();
        ImGui.PushItemWidth(100);
        ImGui.InputTextWithHint("##bds-id", "optional", ref itemIdInput, 16);
        ImGui.PopItemWidth();

        // Row 2: min ilvl + max ilvl (typed numbers, not sliders)
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Min ilvl");
        ImGui.SameLine();
        ImGui.PushItemWidth(80);
        ImGui.InputInt("##bds-minilvl", ref minIlvl, 0, 0);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Max ilvl");
        ImGui.SameLine();
        ImGui.PushItemWidth(80);
        ImGui.InputInt("##bds-maxilvl", ref maxIlvl, 0, 0);
        ImGui.PopItemWidth();

        // Row 3: spiritbond cap + HQ checkbox
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Max spiritbond %");
        ImGui.SameLine();
        ImGui.PushItemWidth(80);
        ImGui.InputInt("##bds-sb", ref maxSpiritbond, 0, 0);
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.Checkbox("Exclude HQ", ref excludeHq);

        // Clamp inputs to sane ranges on every frame (cheap; happens after
        // typing whether or not Enter was pressed).
        if (minIlvl < 0) minIlvl = 0;
        if (maxIlvl < 1) maxIlvl = 1;
        if (minIlvl > 999) minIlvl = 999;
        if (maxIlvl > 999) maxIlvl = 999;
        if (maxSpiritbond < 0) maxSpiritbond = 0;
        if (maxSpiritbond > 100) maxSpiritbond = 100;

        ImGui.Spacing();
        if (ImGui.Button("Build preview"))
            BuildPreviewOnFramework();
        ImGui.SameLine();
        if (ImGui.Button("Clear preview"))
        {
            preview.Clear();
            previewSummary = string.Empty;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(building a preview never modifies anything)");

        ImGui.Separator();
        DrawPreviewAndRun();
    }

    private void DrawPreviewAndRun()
    {
        // While a run is in progress the table reflects executor state -
        // each successful desynth removes a row. When idle, show the
        // user's last-built preview.
        var running = executor.IsRunning;
        IReadOnlyList<DesynthCandidate> rows = running ? executor.RemainingItems : preview;

        // --- Top strip: status / actions ---------------------------------
        if (running)
        {
            ImGui.Text($"Processed: {executor.Processed}");
            ImGui.SameLine();
            ImGui.Text($"Remaining: {executor.Remaining}");
            ImGui.SameLine();
            if (ImGui.Button("Stop run"))
                executor.Stop("user requested stop");
            if (executor.CurrentItem is { } cur)
                ImGui.TextDisabled($"Current: {cur.Name} ({FormatLocation(cur)})");
            else
                ImGui.TextDisabled("Current: (waiting for next item)");
        }
        else if (rows.Count == 0)
        {
            if (!string.IsNullOrEmpty(previewSummary))
                ImGui.TextDisabled(previewSummary);
            else
                ImGui.TextDisabled("No preview built yet.");
            return;
        }
        else
        {
            ImGui.Text(previewSummary);
            var label = $"Desynth {rows.Count} item(s)";
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.55f, 0.15f, 0.15f, 1f)))
            using (ImRaii.PushColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.20f, 0.20f, 1f)))
            {
                if (ImGui.Button(label))
                    executor.Start(preview);
            }
            ImGui.SameLine();
            ImGui.TextDisabled("(cannot be undone)");
        }

        ImGui.Spacing();
        using var table = ImRaii.Table("bds-preview", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 260));
        if (!table) return;

        ImGui.TableSetupColumn("Location");
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("iLvl");
        ImGui.TableSetupColumn("Flags");
        ImGui.TableHeadersRow();

        foreach (var c in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text(FormatLocation(c));
            ImGui.TableNextColumn(); ImGui.Text(c.Name);
            ImGui.TableNextColumn(); ImGui.Text(c.ItemLevel.ToString());
            ImGui.TableNextColumn(); ImGui.Text((c.IsHq ? "HQ " : "") + $"SB:{c.Spiritbond}");
        }
    }

    /// <summary>
    /// Render-time formatting of an inventory slot in player-visible terms.
    /// For main bags this is "Bag {N} ({row},{col})" matching what the player
    /// sees on screen (5 cols x 7 rows per page). For armoury and any other
    /// container where the visual lookup failed, falls back to the internal
    /// container name + slot index.
    /// </summary>
    private static string FormatLocation(DesynthCandidate c)
    {
        if (c.VisualBag == 0)
            return $"{c.Container} slot {c.Slot}";
        var row = c.VisualSlot / 5 + 1;
        var col = c.VisualSlot % 5 + 1;
        return $"Bag {c.VisualBag} ({row},{col})";
    }

    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem("Settings");
        if (!tab) return;

        var cap = config.PerRunHardCap;
        if (ImGui.SliderInt("Per-run hard cap", ref cap, 1, 200))
            config.PerRunHardCap = cap;

        var delay = config.InterItemDelayMs;
        if (ImGui.SliderInt("Delay between items (ms)", ref delay, 500, 5000))
            config.InterItemDelayMs = delay;

        var timeout = config.AddonWaitTimeoutMs;
        if (ImGui.SliderInt("Cast-start timeout (ms)", ref timeout, 1000, 10000))
            config.AddonWaitTimeoutMs = timeout;

        ImGui.TextWrapped("Cast-start timeout: how long to wait for the game's Occupied39 flag to go high after firing a desynth. If the cast never starts within this window the executor logs a warning and moves on. Bump this if you ever see items get skipped under network lag.");
    }

    private void BuildPreviewOnFramework()
    {
        var filter = BuildFilterFromUi();
        if (filter == null) return;

        framework.RunOnFrameworkThread(() =>
        {
            var built = scanner.BuildPreview(filter);
            var filtered = built
                .Where(c => !config.ItemBlockList.Contains(c.ItemId))
                .ToList();
            preview = filtered;
            previewSummary = filtered.Count == 0
                ? "No matching slots found."
                : $"{filtered.Count} candidate slot(s) across "
                  + $"{filtered.Select(c => c.Container).Distinct().Count()} container(s).";
        });
    }

    private DesynthFilter? BuildFilterFromUi()
    {
        var containers = new List<InventoryType>();
        if (bagSelected[0]) containers.Add(InventoryType.Inventory1);
        if (bagSelected[1]) containers.Add(InventoryType.Inventory2);
        if (bagSelected[2]) containers.Add(InventoryType.Inventory3);
        if (bagSelected[3]) containers.Add(InventoryType.Inventory4);
        if (armourySelected) containers.AddRange(DesynthFilter.Presets.ArmouryChest);

        if (containers.Count == 0)
        {
            previewSummary = "Pick at least one container.";
            preview.Clear();
            return null;
        }

        uint? itemId = null;
        if (!string.IsNullOrWhiteSpace(itemIdInput)
            && uint.TryParse(itemIdInput.Trim(), out var parsed) && parsed > 0)
        {
            itemId = parsed;
        }

        // Clamp min <= max so a swapped pair doesn't drop everything.
        var lo = (ushort)Math.Min(minIlvl, maxIlvl);
        var hi = (ushort)Math.Max(minIlvl, maxIlvl);

        return new DesynthFilter
        {
            Containers = containers,
            ItemId = itemId,
            NameContains = string.IsNullOrWhiteSpace(nameInput) ? null : nameInput.Trim(),
            MinItemLevel = lo,
            MaxItemLevel = hi,
            ExcludeHq = excludeHq,
            MaxSpiritbond = (ushort)maxSpiritbond,
        };
    }

    public void Dispose() { }
}

/// <summary>
/// Tiny RAII wrappers around ImGui begin/end calls. Dalamud ships its own
/// equivalents under <c>Dalamud.Interface.Utility.Raii</c>; we roll a minimal
/// version here so we don't pin to a specific namespace path that has shifted
/// between API levels.
/// </summary>
internal static class ImRaii
{
    public ref struct EndScope
    {
        private readonly Action? endAction;
        public readonly bool Success;
        public EndScope(bool success, Action? endAction)
        {
            this.Success = success;
            this.endAction = endAction;
        }
        public static implicit operator bool(EndScope s) => s.Success;
        public void Dispose()
        {
            if (Success) endAction?.Invoke();
        }
    }

    public static EndScope TabBar(string id)
    {
        var ok = ImGui.BeginTabBar(id);
        return new EndScope(ok, ImGui.EndTabBar);
    }

    public static EndScope TabItem(string label)
    {
        var ok = ImGui.BeginTabItem(label);
        return new EndScope(ok, ImGui.EndTabItem);
    }

    public static EndScope Table(string id, int cols, ImGuiTableFlags flags, Vector2 outerSize)
    {
        var ok = ImGui.BeginTable(id, cols, flags, outerSize);
        return new EndScope(ok, ImGui.EndTable);
    }

    public static EndScope PushColor(ImGuiCol idx, Vector4 col)
    {
        ImGui.PushStyleColor(idx, col);
        return new EndScope(true, () => ImGui.PopStyleColor());
    }
}
