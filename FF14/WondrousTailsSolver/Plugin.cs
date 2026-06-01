using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using WondrousTailsSolver.Services;
using WondrousTailsSolver.UI;

namespace WondrousTailsSolver;

/// <summary>
/// Plugin entry point. Wires Dalamud services, owns the
/// <see cref="AddonWeeklyBingoOverlay"/> ImGui window, and exposes a
/// single /wts toggle command.
///
/// v0.1.2 swapped the original KamiToolKit native-node overlay for a
/// Dalamud ImGui Window anchored to the WeeklyBingo addon. No more
/// KamiToolKit bootstrap, no per-frame tick of our own, no extra
/// shipped DLLs — the WindowSystem owns the rendering and the window's
/// DrawConditions checks the addon visibility every frame.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Wondrous Tails Odds";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;

    private const string CommandName = "/wts";

    private readonly Configuration config;
    private readonly BingoStateReader stateReader;
    private readonly AddonWeeklyBingoOverlay overlay;
    private readonly WindowSystem windows = new("WondrousTailsOdds");

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        stateReader = new BingoStateReader();
        overlay = new AddonWeeklyBingoOverlay(config, stateReader, GameGui);

        windows.AddWindow(overlay);
        PluginInterface.UiBuilder.Draw += windows.Draw;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Wondrous Tails probability overlay.",
        });

        Log.Information("WondrousTailsOdds loaded.");
    }

    private void OnCommand(string command, string args)
    {
        config.ShowOverlay = !config.ShowOverlay;
        PluginInterface.SavePluginConfig(config);
        ChatGui.Print($"[WTS] Overlay {(config.ShowOverlay ? "enabled" : "disabled")}.");
    }

    public void Dispose()
    {
        try
        {
            CommandManager.RemoveHandler(CommandName);
            PluginInterface.UiBuilder.Draw -= windows.Draw;
            windows.RemoveAllWindows();
            overlay.Dispose();
            PluginInterface.SavePluginConfig(config);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dispose WondrousTailsOdds cleanly");
        }
    }
}
