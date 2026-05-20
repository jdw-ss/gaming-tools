using System;
using System.Collections.Generic;
using BulkDesynth.Models;
using BulkDesynth.Services;
using BulkDesynth.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace BulkDesynth;

/// <summary>
/// Plugin entry point. Wires Dalamud services, owns the window system, and
/// registers the /bds command for headless usage.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Bulk Desynth";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static ICondition Condition { get; private set; } = null!;

    private readonly Configuration config;
    private readonly InventoryScanner scanner;
    private readonly DesynthExecutor executor;
    private readonly MainWindow mainWindow;
    private readonly WindowSystem windows = new("BulkDesynth");

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        scanner = new InventoryScanner(DataManager, Log);
        executor = new DesynthExecutor(Framework, Condition, Log, config);
        mainWindow = new MainWindow(scanner, executor, Framework, config);

        windows.AddWindow(mainWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMain;

        CommandManager.AddHandler("/bds", new CommandInfo(OnCommand)
        {
            HelpMessage = "Bulk desynth. Subcommands: bag <1-4>, item <id>, stop. Plain /bds opens the window.",
        });

        Log.Information("Bulk Desynth plugin loaded.");
    }

    private void OpenMain()
    {
        mainWindow.IsOpen = true;
    }

    private void OnCommand(string command, string args)
    {
        var parts = (args ?? string.Empty).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var rest = parts.Length > 1 ? parts[1].Trim() : string.Empty;

        switch (sub)
        {
            case "":
                OpenMain();
                return;

            case "stop":
                executor.Stop("user /bds stop");
                ChatGui.Print("[BDS] Run stopped.");
                return;

            case "bag":
                if (!int.TryParse(rest, out var bag) || bag < 1 || bag > 4)
                {
                    ChatGui.PrintError("[BDS] Usage: /bds bag <1-4>");
                    return;
                }
                PreviewAndOpenWindow(new DesynthFilter
                {
                    Containers = new List<InventoryType> { BagFromIndex(bag - 1) },
                    ExcludeHq = true,
                });
                return;

            case "item":
                if (!uint.TryParse(rest, out var itemId) || itemId == 0)
                {
                    ChatGui.PrintError("[BDS] Usage: /bds item <itemId>");
                    return;
                }
                PreviewAndOpenWindow(new DesynthFilter
                {
                    ItemId = itemId,
                    Containers = new List<InventoryType>(DesynthFilter.Presets.MainBags),
                });
                return;

            default:
                ChatGui.PrintError($"[BDS] Unknown subcommand '{sub}'. Try: /bds | /bds bag <n> | /bds item <id> | /bds stop");
                return;
        }
    }

    private void PreviewAndOpenWindow(DesynthFilter filter)
    {
        Framework.RunOnFrameworkThread(() =>
        {
            var built = scanner.BuildPreview(filter);
            ChatGui.Print($"[BDS] Built preview: {built.Count} candidate(s). Open /bds to review and confirm.");
            // Open the window so the user can see the preview before clicking
            // confirm. We intentionally do NOT auto-start the executor from
            // chat - dry-run confirmation is the whole point of the safety
            // model.
            OpenMain();
        });
    }

    private static InventoryType BagFromIndex(int i) => i switch
    {
        0 => InventoryType.Inventory1,
        1 => InventoryType.Inventory2,
        2 => InventoryType.Inventory3,
        3 => InventoryType.Inventory4,
        _ => InventoryType.Inventory1,
    };

    public void Dispose()
    {
        try
        {
            PluginInterface.SavePluginConfig(config);
            CommandManager.RemoveHandler("/bds");
            PluginInterface.UiBuilder.Draw -= windows.Draw;
            PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenMain;
            windows.RemoveAllWindows();
            executor.Dispose();
            mainWindow.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dispose Bulk Desynth cleanly");
        }
    }
}
