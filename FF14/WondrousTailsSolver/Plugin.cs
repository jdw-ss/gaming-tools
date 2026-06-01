using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit;
using WondrousTailsSolver.Services;
using WondrousTailsSolver.UI;

namespace WondrousTailsSolver;

/// <summary>
/// Plugin entry point. Wires Dalamud services, owns the
/// <see cref="AddonWeeklyBingoOverlay"/> lifecycle, and exposes a single
/// /wts toggle command.
///
/// The overlay's actual work happens inside KamiToolKit's
/// <c>AddonController</c> callbacks - this class doesn't need a per-frame
/// tick.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Wondrous Tails Solver";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/wts";

    private readonly Configuration config;
    private readonly BingoStateReader stateReader;
    private readonly AddonWeeklyBingoOverlay overlay;

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // KamiToolKit's Services class is internal and uses [PluginService]
        // injection driven by this single bootstrap call. Must be invoked
        // before any KamiToolKit feature (AddonController, TextNode, ...).
        KamiToolKitLibrary.Initialize(PluginInterface);

        stateReader = new BingoStateReader();
        overlay = new AddonWeeklyBingoOverlay(config, stateReader, Log);

        // AddonController.Enable() asserts main thread.
        Framework.RunOnFrameworkThread(overlay.Enable);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Wondrous Tails probability overlay.",
        });

        Log.Information("WondrousTailsSolver loaded.");
    }

    private void OnCommand(string command, string args)
    {
        config.ShowOverlay = !config.ShowOverlay;
        PluginInterface.SavePluginConfig(config);
        ChatGui.Print($"[WTS] Overlay {(config.ShowOverlay ? "enabled" : "disabled")}.");

        // Force an immediate repaint so the toggle takes effect even when
        // the addon isn't currently firing Update events (e.g. the journal
        // is open and idle).
        Framework.RunOnFrameworkThread(overlay.ForceRefresh);
    }

    public void Dispose()
    {
        try
        {
            CommandManager.RemoveHandler(CommandName);
            overlay.Dispose();
            KamiToolKitLibrary.Dispose();
            PluginInterface.SavePluginConfig(config);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dispose WondrousTailsSolver cleanly");
        }
    }
}
