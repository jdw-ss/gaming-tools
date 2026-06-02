using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GcSupplyHelper.Services;
using GcSupplyHelper.Windows;

namespace GcSupplyHelper;

/// <summary>
/// Plugin entry point. Wires Dalamud services, builds the Lumina-
/// backed data source + recipe walker, owns the
/// <see cref="MainWindow"/>, and hooks the in-game addons that
/// populate the GC Supply agent on open.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "GC Supply Helper";

    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string CommandName = "/gcs";

    /// <summary>
    /// Addons whose PostSetup event is known (or strongly suspected) to
    /// coincide with the game populating <c>AgentGrandCompanySupply
    /// -&gt; SupplyProvisioningData</c>. The Personnel Officer's
    /// <c>GrandCompanySupplyList</c> is confirmed; the Timers addon name
    /// is one of the other three candidates and gets verified on first
    /// in-game test per the plan's "Identify the Timers addon name" step.
    /// All four listeners are registered — only the matching ones will
    /// ever fire, and <see cref="SupplyMissionReader.TryRefresh"/> is
    /// idempotent so multiple successful triggers are harmless.
    /// </summary>
    private static readonly string[] RefreshTriggerAddons =
    [
        "GrandCompanySupplyList",
        "ContentsInfo",
        "ContentsInfoDetail",
        "ContentsTimerSetting",
    ];

    private readonly Configuration config;
    private readonly SupplyMissionReader missionReader;
    private readonly LuminaRecipeDataSource dataSource;
    private readonly RecipeWalker walker;
    private readonly MainWindow mainWindow;
    private readonly WindowSystem windows = new("GcSupplyHelper");

    public Plugin()
    {
        config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        missionReader = new SupplyMissionReader(Log);
        dataSource = new LuminaRecipeDataSource(DataManager, Log);
        // Lambda wrap so the IPluginLog.Warning method-group's many
        // overloads don't collide with Action<string>'s signature.
        walker = new RecipeWalker(dataSource, msg => Log.Warning(msg));

        mainWindow = new MainWindow(
            config,
            missionReader,
            dataSource,
            walker,
            TextureProvider,
            saveConfig: SaveConfig,
            manualRefresh: () => Framework.RunOnFrameworkThread(RefreshFromAnyTrigger));

        windows.AddWindow(mainWindow);
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi += OpenMain;

        foreach (var addonName in RefreshTriggerAddons)
            AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addonName, OnRefreshTriggerAddon);

        ClientState.Login += OnLogin;
        ClientState.Logout += OnLogout;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the GC Supply Helper window.",
        });

        // If the plugin loaded mid-session and the agent's already
        // populated (the most common case for /xldevplugins reload),
        // grab the data immediately rather than waiting for the next
        // addon open.
        Framework.RunOnFrameworkThread(RefreshFromAnyTrigger);

        Log.Information("GcSupplyHelper loaded.");
    }

    private void OnCommand(string command, string args) => OpenMain();

    private void OpenMain()
    {
        mainWindow.IsOpen = true;
    }

    private void OnRefreshTriggerAddon(AddonEvent type, AddonArgs args)
    {
        // Already on the framework thread (IAddonLifecycle guarantees it).
        if (missionReader.TryRefresh())
            mainWindow.InvalidateCache();
    }

    private void OnLogin()
    {
        if (!config.AutoRefreshOnLogin) return;
        Framework.RunOnFrameworkThread(RefreshFromAnyTrigger);
    }

    private void OnLogout(int type, int code)
    {
        // Previous-character data is stale for the next character.
        missionReader.Clear();
        mainWindow.InvalidateCache();
    }

    private void RefreshFromAnyTrigger()
    {
        if (missionReader.TryRefresh())
            mainWindow.InvalidateCache();
    }

    private void SaveConfig() => PluginInterface.SavePluginConfig(config);

    public void Dispose()
    {
        try
        {
            CommandManager.RemoveHandler(CommandName);
            ClientState.Login -= OnLogin;
            ClientState.Logout -= OnLogout;
            foreach (var addonName in RefreshTriggerAddons)
                AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, addonName, OnRefreshTriggerAddon);

            PluginInterface.UiBuilder.Draw -= windows.Draw;
            PluginInterface.UiBuilder.OpenMainUi -= OpenMain;
            PluginInterface.UiBuilder.OpenConfigUi -= OpenMain;
            windows.RemoveAllWindows();
            mainWindow.Dispose();
            SaveConfig();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dispose GcSupplyHelper cleanly.");
        }
    }
}
