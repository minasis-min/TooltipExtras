using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

public sealed unsafe partial class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface pi;
    private readonly ICommandManager commands;
    private readonly IAddonLifecycle lifecycle;
    private readonly IFramework framework;
    private readonly IGameGui gui;
    private readonly IClientState client;
    private readonly ICondition conditions;
    private readonly SettingsStore store;
    private readonly MinasisUI.UiHeadingFonts headingFonts;
    private readonly Settings settings;
    private readonly SettingsAutosave autosave;
    private readonly Compatibility compatibility = new();
    private readonly GlamourLogAdapter glamour;
    private readonly PatchAdapter patchInfo = new();
    private readonly PriceAdapter price = new();
    private readonly VendorAdapter vendor;
    private readonly Diagnostics diagnostics = new();
    private readonly CooperativeBridge bridge;
    private static readonly TooltipKind[] Kinds = [TooltipKind.Item, TooltipKind.Action];
    private long frame;
    private uint territory;
    private bool open, disposed, refreshPending, faulted;
    private string failure = "", saveStatus = "";
    private Task<string>? saveTask;
    private static readonly AddonEvent[] Events = [AddonEvent.PreRequestedUpdate, AddonEvent.PostRequestedUpdate, AddonEvent.PreDraw, AddonEvent.PreHide, AddonEvent.PreFinalize];
    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IAddonLifecycle lifecycle,
        IFramework framework, IGameGui gui, IClientState client, ICondition conditions, IDataManager data)
    {
        this.pi = pi; this.commands = commands; this.lifecycle = lifecycle; this.framework = framework;
        this.gui = gui; this.client = client; this.conditions = conditions;
        var rollback = new List<(string Name, Action Undo)> { ("UI", settingsUi.Dispose) };
        try
        {
            headingFonts = new(pi.UiBuilder.FontAtlas);
            rollback.Add(("heading fonts", headingFonts.Dispose));
            headingFonts.Apply(settingsUi);
            store = new(pi.ConfigDirectory.FullName, pi.AssemblyLocation.DirectoryName ?? throw new IOException("DLL配置先不明"));
            settings = store.Load(); autosave = new(json => store.Save(json)); rollback.Add(("settings", autosave.Flush));
            if (store.NeedsMigration) autosave.Queue(SerializeSettings(), Environment.TickCount64);
            compatibility.Refresh(pi);
            settingsSidebar = DrawSettingsSidebar; settingsDetail = DrawSettingsDetail;
            glamour = new(pi, data);
            vendor = new(() => Available && settings.Items && settings.VendorLocation && compatibility.NativeAllowed, () => (gui.HoveredItem, (nint)Addon(TooltipKind.Item)));
            rollback.Add(("vendor", vendor.Dispose));
            bridge = new(pi, () => framework.IsInFrameworkUpdateThread && Available && compatibility.NativeAllowed, () => settings.PatchInfo);
            rollback.Add(("IPC", bridge.Dispose));
            if (!commands.AddHandler("/tooltipextras", new CommandInfo((_, _) => Open()) { HelpMessage = "Tooltip Extras 設定・対応状態" }))
                throw new InvalidOperationException("/tooltipextras は登録済みです。");
            rollback.Add(("command", () => commands.RemoveHandler("/tooltipextras")));
            foreach (var e in Events)
            {
                lifecycle.RegisterListener(e, OnAddon);
                rollback.Add(("lifecycle " + e, () => lifecycle.UnregisterListener(e, OnAddon)));
            }
            framework.Update += Update; rollback.Add(("Update", () => framework.Update -= Update));
            pi.UiBuilder.Draw += Draw; rollback.Add(("Draw", () => pi.UiBuilder.Draw -= Draw));
            pi.UiBuilder.OpenConfigUi += Open; rollback.Add(("OpenConfigUi", () => pi.UiBuilder.OpenConfigUi -= Open));
            pi.UiBuilder.OpenMainUi += Open; rollback.Add(("OpenMainUi", () => pi.UiBuilder.OpenMainUi -= Open));
            pi.ActivePluginsChanged += PluginsChanged; rollback.Add(("ActivePluginsChanged", () => pi.ActivePluginsChanged -= PluginsChanged));
        }
        catch (Exception original)
        {
            disposed = true;
            var cleanup = new Cleanup();
            for (var i = rollback.Count - 1; i >= 0; i--) cleanup.Try(rollback[i].Name, rollback[i].Undo);
            try { cleanup.ThrowIfFailed(); }
            catch (Exception ex) { throw new AggregateException("Initialization and rollback failed", original, ex); }
            throw;
        }
    }
    private bool GameAvailable => client.IsLoggedIn && !gui.GameUiHidden && !conditions[ConditionFlag.BetweenAreas] && !conditions[ConditionFlag.BetweenAreas51];
    private bool Available => !disposed && !faulted && settings.Enabled && GameAvailable;
    private TooltipKey Key(TooltipKind kind) => kind == TooltipKind.Item ? new(kind, gui.HoveredItem)
        : new(kind, gui.HoveredAction.ActionId, (int)gui.HoveredAction.DetailKind);
    private AtkUnitBase* Addon(TooltipKind kind) => (AtkUnitBase*)gui.GetAddonByName(kind == TooltipKind.Item ? "ItemDetail" : "ActionDetail").Address;
    private void Open() { if (!disposed) open = true; }
    private void PluginsChanged(IActivePluginsChangedEventArgs _) => refreshPending = true;
    private void Restore() { if (compatibility.NativeAllowed) price.Restore(Addon(TooltipKind.Item), Key(TooltipKind.Item)); else price.Clear(); }
    private void Update(IFramework _)
    {
        if (disposed) return;
        frame++;
        autosave.Tick(Environment.TickCount64);
        vendor.Refresh(pi, Available && settings.Items && settings.VendorLocation && compatibility.NativeAllowed);
        if (refreshPending) { refreshPending = false; vendor.Dispose(); Restore(); bridge.Clear(); compatibility.Refresh(pi); glamour.Clear(); vendor.Clear(); }
        if (territory != client.TerritoryType) { territory = client.TerritoryType; Restore(); bridge.Clear(); glamour.Clear(); vendor.Clear(); }
        if (!Available || !settings.Items || !settings.PriceInsight || !compatibility.PriceAllowed) Restore();
        if (!Available) bridge.Clear();
        else if (compatibility.NativeAllowed)
        {
            foreach (var kind in Kinds)
            {
                var native = Addon(kind);
                bridge.Observe(Key(kind), (nint)native, native != null && native->IsVisible);
            }
        }
        if (saveTask is { IsCompletedSuccessfully: true }) { saveStatus = saveTask.Result; saveTask = null; }
    }
    private void Fail(Exception ex)
    {
        faulted = true; failure = ex.GetType().Name + ": " + ex.Message;
        try { Restore(); } catch { price.Clear(); }
        bridge.Clear();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var cleanup = new Cleanup();
        cleanup.Try("vendor restore", vendor.Dispose);
        cleanup.Try("price restore", Restore);
        foreach (var e in Events) cleanup.Try("lifecycle " + e, () => lifecycle.UnregisterListener(e, OnAddon));
        cleanup.Try("Update", () => framework.Update -= Update);
        cleanup.Try("Draw", () => pi.UiBuilder.Draw -= Draw);
        cleanup.Try("OpenConfigUi", () => pi.UiBuilder.OpenConfigUi -= Open);
        cleanup.Try("OpenMainUi", () => pi.UiBuilder.OpenMainUi -= Open);
        cleanup.Try("ActivePluginsChanged", () => pi.ActivePluginsChanged -= PluginsChanged);
        cleanup.Try("command", () => commands.RemoveHandler("/tooltipextras"));
        cleanup.Try("IPC", bridge.Dispose);
        cleanup.Try("UI", settingsUi.Dispose);
        cleanup.Try("heading fonts", headingFonts.Dispose);
        cleanup.Try("settings", autosave.Flush);
        cleanup.ThrowIfFailed();
    }
}
