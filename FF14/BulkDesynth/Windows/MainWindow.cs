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
/// Single window with three tabs:
///   - "Targeting" - tick which containers to scan, optionally narrow with
///     filter parameters, hit Build preview, then Desynth.
///   - "Run" - shows progress of the in-flight queue.
///   - "Settings" - runtime pacing knobs.
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
    // common case (armoury is opt-in since gear in there is more likely
    // to be valuable).
    private readonly bool[] bagSelected = { true, true, true, true };
    private bool armourySelected;

    // Filter parameters. All optional / AND'd together with the bag set.
    private string nameInput = string.Empty;
    private string itemIdInput = string.Empty;
    private int maxIlvl = 999;
    private int minIlvl;
    private bool excludeHq = true;
    private int maxSpiritbond = 1000;

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
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(1200, 900),
        };
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("bds-tabs");
        if (!tabs) return;

        DrawTargetingTab();
        DrawRunTab();
        DrawSettingsTab();
    }

    private void DrawTargetingTab()
    {
        using var tab = ImRaii.TabItem("Targeting");
        if (!tab) return;

        // --- Bag selection -----------------------------------------------
        ImGui.TextDisabled("Scan these containers:");
        ImGui.Checkbox("Bag 1", ref bagSelected[0]); ImGui.SameLine();
        ImGui.Checkbox("Bag 2", ref bagSelected[1]); ImGui.SameLine();
        ImGui.Checkbox("Bag 3", ref bagSelected[2]); ImGui.SameLine();
        ImGui.Checkbox("Bag 4", ref bagSelected[3]); ImGui.SameLine();
        ImGui.Checkbox("Armoury chest", ref armourySelected);

        ImGui.Spacing();
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

        // --- Filters -----------------------------------------------------
        ImGui.TextDisabled("Filters (all optional - empty fields mean no constraint):");

        ImGui.InputTextWithHint("Item name contains", "e.g. Hempen", ref nameInput, 64);
        ImGui.InputTextWithHint("Item ID (exact, advanced)", "Lumina row id, optional", ref itemIdInput, 16);

        ImGui.SliderInt("Min item level", ref minIlvl, 0, 999);
        ImGui.SliderInt("Max item level", ref maxIlvl, 1, 999);
        ImGui.Checkbox("Exclude HQ", ref excludeHq);
        ImGui.SliderInt("Max spiritbond (0-1000)", ref maxSpiritbond, 0, 1000);

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
        DrawPreview();
    }

    private void DrawPreview()
    {
        if (preview.Count == 0)
        {
            if (!string.IsNullOrEmpty(previewSummary))
                ImGui.TextDisabled(previewSummary);
            else
                ImGui.TextDisabled("No preview built yet.");
            return;
        }

        ImGui.Text(previewSummary);
        ImGui.Spacing();

        if (executor.IsRunning)
        {
            ImGui.TextDisabled("A run is already in progress - check the Run tab.");
        }
        else
        {
            var label = $"Desynth {preview.Count} item(s)";
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
        using var table = ImRaii.Table("bds-preview", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 260));
        if (!table) return;

        ImGui.TableSetupColumn("Container");
        ImGui.TableSetupColumn("Slot");
        ImGui.TableSetupColumn("Item");
        ImGui.TableSetupColumn("iLvl");
        ImGui.TableSetupColumn("Flags");
        ImGui.TableHeadersRow();

        foreach (var c in preview)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.Text(c.Container.ToString());
            ImGui.TableNextColumn(); ImGui.Text(c.Slot.ToString());
            ImGui.TableNextColumn(); ImGui.Text(c.Name);
            ImGui.TableNextColumn(); ImGui.Text(c.ItemLevel.ToString());
            ImGui.TableNextColumn(); ImGui.Text((c.IsHq ? "HQ " : "") + $"SB:{c.Spiritbond}");
        }
    }

    private void DrawRunTab()
    {
        using var tab = ImRaii.TabItem("Run");
        if (!tab) return;

        if (!executor.IsRunning)
        {
            ImGui.TextWrapped("Nothing running. Build a preview on the Targeting tab and click 'Desynth' to start.");
            ImGui.TextDisabled($"Last run processed {executor.Processed} item(s).");
            return;
        }

        ImGui.Text($"Processed: {executor.Processed}");
        ImGui.Text($"Remaining: {executor.Remaining}");
        if (executor.CurrentItem is { } cur)
            ImGui.Text($"Current: {cur.Name} (slot {cur.Slot} of {cur.Container})");

        ImGui.Spacing();
        if (ImGui.Button("Stop run"))
            executor.Stop("user requested stop");
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

        ImGui.TextWrapped("Cast-start timeout: how long to wait for the game's Occupied39 flag to go high after firing a desynth. If the cast never starts within this window the executor logs a warning and moves on.");
    }

    private void BuildPreviewOnFramework()
    {
        var filter = BuildFilterFromUi();
        if (filter == null) return;

        framework.RunOnFrameworkThread(() =>
        {
            var built = scanner.BuildPreview(filter);
            // Apply user-level block list overlay (allow-list isn't enforced
            // here - the filter UI already gives full control over what
            // enters the preview).
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

        // Clamp min <= max so a misaligned slider pair doesn't drop everything.
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
