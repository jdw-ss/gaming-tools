using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using GcSupplyHelper.Models;
using GcSupplyHelper.Services;

namespace GcSupplyHelper.Windows;

/// <summary>
/// The plugin's only window. Three tabs:
/// <list type="bullet">
///   <item><b>Per Mission</b> — one collapsing block per enabled
///   mission with the leaf-level material list.</item>
///   <item><b>Aggregate</b> — single flat table summing leaf materials
///   across every enabled mission, with class chips on shared rows.</item>
///   <item><b>Settings</b> — class-job filter checkboxes, auto-refresh
///   toggle, manual Refresh, last-refresh timestamp.</item>
/// </list>
/// </summary>
internal sealed class MainWindow : Window, IDisposable
{
    /// <summary>
    /// Canonical class display names keyed by ClassJob row id. The
    /// plugin doesn't bother loading these from Lumina — they're
    /// English-only here and matched against the user's ClassJob ids
    /// in <see cref="Configuration.EnabledClassJobIds"/>.
    /// </summary>
    private static readonly Dictionary<byte, string> ClassNames = new()
    {
        [8] = "Carpenter", [9] = "Blacksmith", [10] = "Armorer", [11] = "Goldsmith",
        [12] = "Leatherworker", [13] = "Weaver", [14] = "Alchemist", [15] = "Culinarian",
        [16] = "Miner", [17] = "Botanist", [18] = "Fisher",
    };

    /// <summary>Three-letter chips used in the Aggregate "Needed by" column.</summary>
    private static readonly Dictionary<byte, string> ClassAbbreviations = new()
    {
        [8] = "CRP", [9] = "BSM", [10] = "ARM", [11] = "GSM",
        [12] = "LTW", [13] = "WVR", [14] = "ALC", [15] = "CUL",
        [16] = "MIN", [17] = "BTN", [18] = "FSH",
    };

    private static readonly Vector4 ColorGathered = new(0.40f, 0.95f, 0.45f, 1.00f);
    private static readonly Vector4 ColorVendor   = new(0.95f, 0.90f, 0.40f, 1.00f);
    private static readonly Vector4 ColorUnknown  = new(0.65f, 0.65f, 0.65f, 1.00f);
    private static readonly Vector4 ColorCrafted  = new(0.95f, 0.45f, 0.40f, 1.00f);

    private const float IconSize = 20f;

    private readonly Configuration config;
    private readonly SupplyMissionReader missionReader;
    private readonly IRecipeDataSource dataSource;
    private readonly RecipeWalker walker;
    private readonly ITextureProvider textureProvider;
    private readonly Action saveConfig;
    private readonly Action manualRefresh;

    // Cached computed views. Invalidated when missions change or the
    // class-job filter changes.
    private List<DailyMission> cachedFilteredMissions = new();
    private List<MaterialRequirement> cachedAggregate = new();
    private Dictionary<uint, List<MaterialRequirement>> cachedPerMission = new();
    private DateTime? lastComputedFromUtc;

    public MainWindow(
        Configuration config,
        SupplyMissionReader missionReader,
        IRecipeDataSource dataSource,
        RecipeWalker walker,
        ITextureProvider textureProvider,
        Action saveConfig,
        Action manualRefresh)
        : base("GC Supply Helper##gcs-main",
               ImGuiWindowFlags.None)
    {
        this.config = config;
        this.missionReader = missionReader;
        this.dataSource = dataSource;
        this.walker = walker;
        this.textureProvider = textureProvider;
        this.saveConfig = saveConfig;
        this.manualRefresh = manualRefresh;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540f, 360f),
            MaximumSize = new Vector2(2000f, 2000f),
        };
        Size = new Vector2(680f, 520f);
        SizeCondition = ImGuiCond.FirstUseEver;

        RespectCloseHotkey = true;
        ShowCloseButton = true;
    }

    public void Dispose() { }

    public override void Draw()
    {
        EnsureCacheCurrent();

        if (ImGui.BeginTabBar("##gcs-tabs"))
        {
            if (ImGui.BeginTabItem("Per Mission"))
            {
                DrawPerMissionTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Aggregate"))
            {
                DrawAggregateTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    /// <summary>
    /// Force a recompute next frame. The plugin calls this when
    /// <see cref="SupplyMissionReader.TryRefresh"/> succeeds or when
    /// the settings tab toggles a class.
    /// </summary>
    public void InvalidateCache() => lastComputedFromUtc = null;

    private void EnsureCacheCurrent()
    {
        if (lastComputedFromUtc == missionReader.LastRefreshUtc && cachedFilteredMissions.Count > 0)
            return;

        cachedFilteredMissions = missionReader.CurrentMissions
            .Where(m => config.EnabledClassJobIds.Contains(m.ClassJobId))
            .OrderBy(m => m.ClassJobId)
            .ToList();

        cachedPerMission = new Dictionary<uint, List<MaterialRequirement>>();
        foreach (var mission in cachedFilteredMissions)
        {
            var materials = walker.ComputeRawMaterials(new[] { mission });
            cachedPerMission[mission.ItemId] = materials.Values
                .OrderByDescending(m => m.Quantity)
                .ThenBy(m => m.ItemName)
                .ToList();
        }

        var aggregated = walker.ComputeRawMaterials(cachedFilteredMissions);
        cachedAggregate = aggregated.Values
            .OrderByDescending(m => m.Quantity)
            .ThenBy(m => m.ItemName)
            .ToList();

        lastComputedFromUtc = missionReader.LastRefreshUtc;
    }

    private void DrawPerMissionTab()
    {
        if (missionReader.CurrentMissions.Count == 0)
        {
            DrawEmptyState();
            return;
        }
        if (cachedFilteredMissions.Count == 0)
        {
            ImGui.TextWrapped("No missions match the current class filter. Re-enable classes in the Settings tab.");
            return;
        }

        foreach (var mission in cachedFilteredMissions)
        {
            var className = ClassNames.GetValueOrDefault(mission.ClassJobId, $"Class #{mission.ClassJobId}");
            var hasName = dataSource.TryGetItemDisplay(mission.ItemId, out var itemName, out var iconId);
            var displayName = hasName ? itemName : $"Item #{mission.ItemId}";
            var missionTypeLabel = mission.IsProvisioning ? "Provisioning" : "Supply";

            ImGui.PushID((int)mission.ItemId);
            try
            {
                DrawIcon(iconId);
                ImGui.SameLine();
                if (ImGui.CollapsingHeader(
                    $"{className} — {displayName} ×{mission.QuantityRequired} ({missionTypeLabel})",
                    ImGuiTreeNodeFlags.DefaultOpen))
                {
                    DrawMaterialList(cachedPerMission[mission.ItemId], indent: true);
                }
            }
            finally
            {
                ImGui.PopID();
            }
        }
    }

    private void DrawAggregateTab()
    {
        if (missionReader.CurrentMissions.Count == 0)
        {
            DrawEmptyState();
            return;
        }
        if (cachedAggregate.Count == 0)
        {
            ImGui.TextWrapped("Nothing to gather — the enabled missions either have no raw materials or the class filter excludes everything.");
            return;
        }

        ImGui.TextDisabled($"Combined shopping list across {cachedFilteredMissions.Count} mission(s)");
        ImGui.Spacing();

        // Reserve vertical space below the table for the "Plan route on web" button.
        // A ScrollY table with no outer_size eats every remaining pixel of the parent,
        // which pushes the footer button off-screen — see GcSupplyHelper v0.1.3 fix.
        var footerHeight = ImGui.GetFrameHeightWithSpacing()
                           + ImGui.GetStyle().ItemSpacing.Y * 2f
                           + 4f;
        if (!ImGui.BeginTable("##gcs-aggregate", 5,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                new Vector2(0f, -footerHeight)))
            return;

        ImGui.TableSetupColumn(string.Empty, ImGuiTableColumnFlags.WidthFixed, IconSize + 4f);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch, 2.5f);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("Needed by", ImGuiTableColumnFlags.WidthStretch, 2.0f);
        ImGui.TableHeadersRow();

        foreach (var material in cachedAggregate)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawIcon(material.IconId);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(material.ItemName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(material.Quantity.ToString());
            ImGui.TableNextColumn();
            DrawSourceLabel(material.Source);
            ImGui.TableNextColumn();
            DrawNeededByChips(material.NeededByClassJobs);
        }

        ImGui.EndTable();

        // v0.1.2: plan-route-on-web handoff. Encodes the aggregate as a
        // base64-url-safe JSON payload in a URL hash fragment and opens
        // the ffxiv-achievement-tracker site's /gc-supply-route page in
        // the default browser. See ADR-0002 for why this lives on the
        // web rather than as an in-game ImGui Route tab.
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Plan route on web \u2197"))
        {
            var url = RouteUrlBuilder.Build(Plugin.WebsiteBaseUrl, cachedAggregate);
            Dalamud.Utility.Util.OpenLink(url);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Opens the route planner in your default browser.");
    }

    private void DrawSettingsTab()
    {
        ImGui.TextDisabled("Show missions for these classes:");
        ImGui.Spacing();

        var changed = false;
        // Two columns of checkboxes, crafters left and gatherers right, for compactness.
        if (ImGui.BeginTable("##gcs-class-filter", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextDisabled("Crafters");
            foreach (var id in new byte[] { 8, 9, 10, 11, 12, 13, 14, 15 })
                changed |= DrawClassCheckbox(id);

            ImGui.TableNextColumn();
            ImGui.TextDisabled("Gatherers");
            foreach (var id in new byte[] { 16, 17, 18 })
                changed |= DrawClassCheckbox(id);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var autoRefresh = config.AutoRefreshOnLogin;
        if (ImGui.Checkbox("Try to refresh on login", ref autoRefresh))
        {
            config.AutoRefreshOnLogin = autoRefresh;
            saveConfig();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(Empirically the agent is usually still empty at login; the addon hooks do the real work.)");

        ImGui.Spacing();

        if (ImGui.Button("Refresh now"))
            manualRefresh();
        ImGui.SameLine();
        if (missionReader.LastRefreshUtc is { } stamp)
            ImGui.TextDisabled($"Last refresh: {stamp:HH:mm:ss} UTC ({missionReader.CurrentMissions.Count} missions cached)");
        else
            ImGui.TextDisabled("Last refresh: never");

        if (changed)
        {
            saveConfig();
            InvalidateCache();
        }
    }

    private bool DrawClassCheckbox(byte classJobId)
    {
        var enabled = config.EnabledClassJobIds.Contains(classJobId);
        var label = ClassNames.GetValueOrDefault(classJobId, $"Class #{classJobId}");
        if (ImGui.Checkbox(label, ref enabled))
        {
            if (enabled) config.EnabledClassJobIds.Add(classJobId);
            else config.EnabledClassJobIds.Remove(classJobId);
            return true;
        }
        return false;
    }

    private void DrawEmptyState()
    {
        ImGui.TextWrapped("Today's missions aren't loaded yet.");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Talk to your Grand Company Personnel Officer and open the Supply / Provisioning list. " +
            "The plugin reads today's missions the moment that window opens, and caches them for the rest of the session.");
        ImGui.Spacing();
        ImGui.TextDisabled(
            "(The in-game Timers panel shows the missions too, but it reads from a different memory buffer " +
            "than the plugin uses. v0.2 will lift that constraint.)");
        ImGui.Spacing();
        if (ImGui.Button("Refresh now"))
            manualRefresh();
    }

    private void DrawMaterialList(IReadOnlyList<MaterialRequirement> materials, bool indent)
    {
        if (materials.Count == 0)
        {
            ImGui.TextDisabled("  (no expanded materials — turn-in item is already a leaf)");
            return;
        }
        if (indent) ImGui.Indent(IconSize);
        foreach (var material in materials)
        {
            DrawIcon(material.IconId);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{material.ItemName}");
            ImGui.SameLine();
            ImGui.TextDisabled($"  ×{material.Quantity}");
            ImGui.SameLine();
            DrawSourceLabel(material.Source);
        }
        if (indent) ImGui.Unindent(IconSize);
    }

    private void DrawIcon(uint iconId)
    {
        if (iconId == 0)
        {
            ImGui.Dummy(new Vector2(IconSize, IconSize));
            return;
        }
        var shared = textureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        if (shared.TryGetWrap(out var wrap, out _))
            ImGui.Image(wrap.Handle, new Vector2(IconSize, IconSize));
        else
            ImGui.Dummy(new Vector2(IconSize, IconSize));
    }

    private static void DrawSourceLabel(MaterialSource source)
    {
        var (text, color) = source switch
        {
            MaterialSource.Gathered => ("Gathered", ColorGathered),
            MaterialSource.Vendor   => ("Vendor",   ColorVendor),
            MaterialSource.Crafted  => ("Crafted?", ColorCrafted),
            _                       => ("Unknown",  ColorUnknown),
        };
        ImGui.TextColored(color, text);
    }

    private static void DrawNeededByChips(HashSet<byte> classJobs)
    {
        if (classJobs.Count == 0)
        {
            ImGui.TextDisabled("—");
            return;
        }
        var labels = classJobs
            .OrderBy(id => id)
            .Select(id => ClassAbbreviations.GetValueOrDefault(id, $"#{id}"));
        ImGui.TextUnformatted(string.Join(", ", labels));
    }
}
