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
/// Single window that handles both filter setup and run preview / confirmation.
///
/// Layout is two tabs:
///   - "Targeting" - pick scope (bag / item id / filter / armoury), hit
///     "Build preview". The preview list shows below.
///   - "Run" - shows progress of the in-flight queue.
///
/// We never invoke the executor automatically. The preview must be explicitly
/// confirmed via the "Desynth N items" button after the user has had a chance
/// to read the candidate list.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    private readonly InventoryScanner scanner;
    private readonly DesynthExecutor executor;
    private readonly IFramework framework;
    private readonly Configuration config;

    // UI state - all of the inputs live on the window itself so the user can
    // switch tabs without losing them.
    private int scope; // 0=bag, 1=item id, 2=filter, 3=armoury
    private int bagIndex; // 0..3 for Inventory1..4
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
            MinimumSize = new Vector2(520, 360),
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

        ImGui.TextWrapped("Pick a scope, click Build preview, review the candidate list, then Desynth.");
        ImGui.Spacing();

        ImGui.RadioButton("Entire bag", ref scope, 0); ImGui.SameLine();
        ImGui.RadioButton("Item ID", ref scope, 1); ImGui.SameLine();
        ImGui.RadioButton("Filter", ref scope, 2); ImGui.SameLine();
        ImGui.RadioButton("Armoury", ref scope, 3);

        ImGui.Spacing();
        ImGui.Separator();

        switch (scope)
        {
            case 0:
                ImGui.Combo("Bag", ref bagIndex, new[] { "Bag 1", "Bag 2", "Bag 3", "Bag 4" }, 4);
                break;
            case 1:
                ImGui.InputText("Item ID (Lumina row)", ref itemIdInput, 16);
                ImGui.TextDisabled("Tip: right-click an item in chat -> 'item link' shows its ID in the hover tooltip.");
                break;
            case 2:
                ImGui.SliderInt("Max item level", ref maxIlvl, 1, 999);
                ImGui.SliderInt("Min item level", ref minIlvl, 0, 999);
                ImGui.Checkbox("Exclude HQ", ref excludeHq);
                ImGui.SliderInt("Max spiritbond (0-1000)", ref maxSpiritbond, 0, 1000);
                break;
            case 3:
                ImGui.TextWrapped("Scans every armoury chest slot. Filter further on the Filter tab if you want ilvl caps.");
                break;
        }

        ImGui.Spacing();
        if (ImGui.Button("Build preview"))
            BuildPreviewOnFramework();
        ImGui.SameLine();
        if (ImGui.Button("Clear preview"))
        {
            preview.Clear();
            previewSummary = string.Empty;
        }

        ImGui.Separator();
        DrawPreview();
    }

    private void DrawPreview()
    {
        if (preview.Count == 0)
        {
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
        using var table = ImRaii.Table("bds-preview", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, new Vector2(0, 240));
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
        if (ImGui.SliderInt("Addon wait timeout (ms)", ref timeout, 1000, 10000))
            config.AddonWaitTimeoutMs = timeout;

        ImGui.TextWrapped("Hover-help: the addon wait timeout is how long to wait for SalvageDialog / SelectYesno / SalvageResult to appear before giving up on an item.");
    }

    private void BuildPreviewOnFramework()
    {
        var filter = BuildFilterFromUi();
        if (filter == null) return;

        // Inventory reads have to happen on the framework thread - schedule
        // the work and read the result back on the next UI frame.
        framework.RunOnFrameworkThread(() =>
        {
            var built = scanner.BuildPreview(filter);
            // Apply user-level block/allow list overlays.
            var filtered = built
                .Where(c => !config.ItemBlockList.Contains(c.ItemId))
                .ToList();
            preview = filtered;
            previewSummary = $"{filtered.Count} candidate slot(s) across "
                + $"{filtered.Select(c => c.Container).Distinct().Count()} container(s).";
        });
    }

    private DesynthFilter? BuildFilterFromUi()
    {
        switch (scope)
        {
            case 0:
                return new DesynthFilter
                {
                    Containers = new List<InventoryType> { ScopeBag(bagIndex) },
                    ExcludeHq = excludeHq,
                };
            case 1:
                if (!uint.TryParse(itemIdInput, out var id) || id == 0)
                    return null;
                return new DesynthFilter
                {
                    ItemId = id,
                    Containers = new List<InventoryType>(DesynthFilter.Presets.MainBags),
                    ExcludeHq = excludeHq,
                };
            case 2:
                return new DesynthFilter
                {
                    MaxItemLevel = (ushort)maxIlvl,
                    MinItemLevel = (ushort)minIlvl,
                    ExcludeHq = excludeHq,
                    MaxSpiritbond = (ushort)maxSpiritbond,
                };
            case 3:
                return new DesynthFilter
                {
                    Containers = new List<InventoryType>(DesynthFilter.Presets.ArmouryChest),
                    ExcludeHq = excludeHq,
                };
            default:
                return null;
        }
    }

    private static InventoryType ScopeBag(int i) => i switch
    {
        0 => InventoryType.Inventory1,
        1 => InventoryType.Inventory2,
        2 => InventoryType.Inventory3,
        3 => InventoryType.Inventory4,
        _ => InventoryType.Inventory1,
    };

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
