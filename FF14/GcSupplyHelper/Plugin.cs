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
    /// Production hostname of the `ffxiv-achievement-tracker` site,
    /// where the route planner page lives at <c>/gc-supply-route</c>.
    /// Hardcoded for v0.1.2; if the tracker ever moves to a custom
    /// domain this bumps with the plugin. v0.1.3 idea: surface this in
    /// Configuration so users can point at a local dev server.
    /// </summary>
    internal const string WebsiteBaseUrl = "https://gaming-data-projects-491421.web.app";

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

        // v0.1.0 registered listeners against a fixed candidate list
        // (GrandCompanySupplyList + three guesses for the Timers panel).
        // Empirical testing in-game confirmed that *only* the Personnel
        // Officer's Supply List populates AgentGrandCompanySupply — the
        // Timers panel renders from UIState.GCSupply directly without
        // touching the agent. Rather than maintain a guess list, we
        // register one catch-all PostSetup listener and rely on
        // SupplyMissionReader.TryRefresh's null-check fast path
        // (one pointer comparison per fired event). Cheap, future-proof,
        // and catches any addon name we don't know about today.
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, OnAnyAddonPostSetup);

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

    private void OnAnyAddonPostSetup(AddonEvent type, AddonArgs args)
    {
        // Already on the framework thread (IAddonLifecycle guarantees it).
        // TryRefresh short-circuits on a null agent pointer, so this is a
        // no-op for addons that don't populate AgentGrandCompanySupply
        // (i.e. nearly all of them). Successful triggers update the cache.
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
            AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, OnAnyAddonPostSetup);

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
