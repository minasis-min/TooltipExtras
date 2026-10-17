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
    private readonly IPluginLog log;
    private string lastCompatibilityLog = "";
    private string lastPriceStatus = "", lastVendorStatus = "", lastGlamourStatus = "";
    private string priceFailure = "";
    private string vendorFailure = "";
    private readonly MinasisUI.UiHeadingFonts headingFonts;
    private readonly Settings settings;
    private readonly SettingsAutosave autosave;
    private readonly Compatibility compatibility = new();
    private readonly GlamourLogAdapter glamour;
    private readonly PatchAdapter patchInfo = new();
    private readonly PriceAdapter price = new();
    private readonly VendorAdapter vendor;
    private readonly AllaganAdapter allagan;
    private readonly Action refreshAllagan;
    private readonly SimpleAdapter simple;
    private readonly Action refreshSimple;
    private readonly SimpleIdAdapter simpleId;
    private readonly SimpleTextAdapter simpleText;
    private readonly SimpleGlamourNameAdapter glamourNames;
    private readonly Action glamourNamesPreDraw;
    private string lastGlamourNameStatus="";
    private readonly SimplePreviewAdapter unlockPreview,paintingPreview;


    private readonly Action refreshSimpleText,refreshUnlockPreview,refreshPaintingPreview,unlockPreviewPreDraw,paintingPreviewPreDraw;
    private string lastSimpleTextStatus="",lastUnlockStatus="",lastPaintingStatus="";
    private readonly Action refreshSimpleId;
    private string lastSimpleIdStatus = "";
    private string lastSimpleStatus = "";
    private string lastAllaganStatus = "", allaganFailure = "";
    private readonly Diagnostics diagnostics = new();
    private readonly RetryBatch reconnect;
    private readonly CooperativeBridge bridge;
    private static readonly TooltipKind[] Kinds = [TooltipKind.Item, TooltipKind.Action];
    private long frame;
    private uint territory;
    private bool open, disposed, refreshPending, faulted;
    private string failure = "", saveStatus = "";
    private Task<string>? saveTask;
    private static readonly AddonEvent[] Events = [AddonEvent.PreRequestedUpdate, AddonEvent.PostRequestedUpdate, AddonEvent.PreUpdate, AddonEvent.PostUpdate, AddonEvent.PreDraw, AddonEvent.PostDraw, AddonEvent.PreHide, AddonEvent.PreFinalize];
    public Plugin(IDalamudPluginInterface pi, ICommandManager commands, IAddonLifecycle lifecycle,
        IFramework framework, IGameGui gui, IClientState client, ICondition conditions, IDataManager data, IPluginLog log, IKeyState keyState, IGameInteropProvider interop, ITextureProvider textures)
    {
        this.pi = pi; this.commands = commands; this.lifecycle = lifecycle; this.framework = framework;
        this.gui = gui; this.client = client; this.conditions = conditions; this.log = log;
        var rollback = new List<(string Name, Action Undo)> { ("UI", settingsUi.Dispose) };
        try
        {
            headingFonts = new(pi.UiBuilder.FontAtlas);
            rollback.Add(("heading fonts", headingFonts.Dispose));
            headingFonts.Apply(settingsUi);
            store = new(pi.ConfigDirectory.FullName, pi.AssemblyLocation.DirectoryName ?? throw new IOException("DLL配置先不明"));
            settings = store.Load(); autosave = new(json => store.Save(json)); rollback.Add(("settings", autosave.Flush));
            if (store.NeedsMigration) autosave.Queue(SerializeSettings(), Environment.TickCount64);
            RefreshCompatibility();
            settingsSidebar = DrawSettingsSidebar; settingsDetail = DrawSettingsDetail;
            glamour = new(pi, data);
            vendor = new(() => Available && settings.Items && settings.VendorLocation && compatibility.VendorAllowed && vendorFailure.Length == 0, () => (gui.HoveredItem, (nint)Addon(TooltipKind.Item)));
            rollback.Add(("vendor", vendor.Dispose));
            allagan = new(() => Available && settings.Items && settings.AllaganTools && compatibility.AllaganNativeAllowed,
                () => (gui.HoveredItem, (nint)Addon(TooltipKind.Item)), () => framework.IsInFrameworkUpdateThread);
            rollback.Add(("Allagan", allagan.Dispose));
            refreshAllagan = () => allagan.Refresh(pi, settings.Enabled && settings.Items && settings.AllaganTools && compatibility.AllaganNativeAllowed);
            simple = new(() => Available && settings.Items && settings.SimpleExtras && compatibility.SimpleRelayAllowed,
                () => keyState[Dalamud.Game.ClientState.Keys.VirtualKey.CONTROL], () => (gui.HoveredItem, (nint)Addon(TooltipKind.Item)), () => framework.IsInFrameworkUpdateThread,price.FollowRestoredSimple,()=>compatibility.PriceNativeAllowed);
            rollback.Add(("Simple", simple.Dispose));
            refreshSimple = () => simple.Refresh(pi, settings.Enabled && settings.Items && settings.SimpleExtras && compatibility.SimpleRelayAllowed);
            simpleId = new(() => Available && settings.Items && settings.SimpleExtras && compatibility.SimpleIdAllowed,
                () => (gui.HoveredItem, (nint)Addon(TooltipKind.Item)), () => framework.IsInFrameworkUpdateThread);
            rollback.Add(("Simple ID", simpleId.Dispose));
            refreshSimpleId = () => simpleId.Refresh(pi, settings.Enabled && settings.Items && settings.SimpleExtras && compatibility.SimpleIdAllowed);
            glamourNames=new(()=>Available&&settings.Items&&settings.SimpleExtras&&settings.SimpleGlamourName&&compatibility.SimpleGlamourNameAllowed,
                ()=> (gui.HoveredItem,(nint)Addon(TooltipKind.Item)));
            rollback.Add(("Simple glamour names",glamourNames.Dispose));
            glamourNamesPreDraw=glamourNames.PreDraw;
            simpleText=new(interop,()=>Available&&settings.Items&&settings.SimpleExtras&&compatibility.SimpleTextAllowed,
                ()=>framework.IsInFrameworkUpdateThread,SimpleTextSelected,()=> (gui.HoveredItem,(nint)Addon(TooltipKind.Item)),ObservePreviewBase,glamourNames.Capture);
            rollback.Add(("Simple text",simpleText.Dispose));
            refreshSimpleText=()=>simpleText.Refresh(pi,settings.Enabled&&settings.Items&&settings.SimpleExtras&&compatibility.SimpleTextAllowed);
            unlockPreview=new("UnlockablePreviews",textures,()=>Available&&settings.Items&&settings.SimpleExtras&&settings.SimpleUnlockPreview&&compatibility.UnlockPreviewAllowed,
                ()=>framework.IsInFrameworkUpdateThread,()=> (gui.HoveredItem,(nint)Addon(TooltipKind.Item)),price.FollowRestoredPreview);
            paintingPreview=new("PaintingPreview",textures,()=>Available&&settings.Items&&settings.SimpleExtras&&settings.SimplePaintingPreview&&compatibility.PaintingPreviewAllowed,
                ()=>framework.IsInFrameworkUpdateThread,()=> (gui.HoveredItem,(nint)Addon(TooltipKind.Item)),price.FollowRestoredPreview);
            unlockPreviewPreDraw=()=>unlockPreview.PreDraw(Addon(TooltipKind.Item),gui.HoveredItem);
            paintingPreviewPreDraw=()=>paintingPreview.PreDraw(Addon(TooltipKind.Item),gui.HoveredItem);
            rollback.Add(("unlock preview",unlockPreview.Dispose));rollback.Add(("painting preview",paintingPreview.Dispose));
            refreshUnlockPreview=()=>unlockPreview.Refresh(pi,settings.Enabled&&settings.Items&&settings.SimpleExtras&&settings.SimpleUnlockPreview&&compatibility.UnlockPreviewAllowed);
            refreshPaintingPreview=()=>paintingPreview.Refresh(pi,settings.Enabled&&settings.Items&&settings.SimpleExtras&&settings.SimplePaintingPreview&&compatibility.PaintingPreviewAllowed);
            bridge = new(pi, () => framework.IsInFrameworkUpdateThread && Available && compatibility.NativeAllowed, () => settings.PatchInfo);
            rollback.Add(("IPC", bridge.Dispose));
            reconnect=new(("vendor",vendor.Dispose),("native restoration",Restore),("IPC state",bridge.Clear),
                ("compatibility",RefreshCompatibility),("provider output",()=>{glamour.Clear();vendor.Clear();}),
                ("Allagan",allagan.Reconnect),("Simple",simple.Reconnect),("Simple ID",simpleId.Reconnect),
                ("Simple text",simpleText.Reconnect),("unlock preview",unlockPreview.Reconnect),("painting preview",paintingPreview.Reconnect));
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
    private bool Available => !disposed && !faulted && settings.Enabled && compatibility.NativeAllowed && GameAvailable;
    private TooltipKey Key(TooltipKind kind) => kind == TooltipKind.Item ? new(kind, gui.HoveredItem)
        : new(kind, gui.HoveredAction.ActionId, (int)gui.HoveredAction.DetailKind);
    private AtkUnitBase* Addon(TooltipKind kind) => (AtkUnitBase*)gui.GetAddonByName(kind == TooltipKind.Item ? "ItemDetail" : "ActionDetail").Address;
    private void Open() { if (!disposed) open = true; }
    private void PluginsChanged(IActivePluginsChangedEventArgs _) => refreshPending = true;
    private LayoutStamp? CurrentLayout()
    {
        if(!compatibility.PriceNativeAllowed)return null;
        var a=Addon(TooltipKind.Item);var f=a==null?null:a->GetNodeById(2);
        return a==null||a->WindowNode==null||f==null?null:new(a->WindowNode->AtkResNode.Height,f->Y);
    }
    private void RestorePriceOnly()
    {
        if(!compatibility.PriceNativeAllowed){price.Clear();return;}
        var before=CurrentLayout();
        if(!price.Restore(Addon(TooltipKind.Item),Key(TooltipKind.Item)))return;
        if(before is {} b&&CurrentLayout() is {} after&&b!=after)
        {var change=new LayoutChange(b,after);paintingPreview.FollowPriceRestore(change);unlockPreview.FollowPriceRestore(change);}
    }
    private void RestorePreview(SimplePreviewAdapter preview,bool showImage)
    {
        if(showImage)preview.Restore();else {var key=Key(TooltipKind.Item);preview.Requested(key.Id,(nint)Addon(TooltipKind.Item));}
    }
    private void ObservePreviewBase(ulong item,nint addon)
    {
        if(compatibility.UnlockPreviewAllowed&&settings.SimpleUnlockPreview)unlockPreview.ObserveBase(item,addon);
        if(compatibility.PaintingPreviewAllowed&&settings.SimplePaintingPreview)paintingPreview.ObserveBase(item,addon);
    }
    private void Restore() => Restore(true);
    private void Restore(bool restoreNames)
    {
        var cleanup=new Cleanup();
        if(restoreNames)cleanup.Try("glamour names",glamourNames.Restore);
        if(restoreNames)cleanup.Try("Simple restore",()=>{simple.Restore();});
        if(restoreNames){cleanup.Try("painting preview",()=>RestorePreview(paintingPreview,true));cleanup.Try("unlock preview",()=>RestorePreview(unlockPreview,true));}
        else {cleanup.Try("painting preview update",()=>RestorePreview(paintingPreview,false));cleanup.Try("unlock preview update",()=>RestorePreview(unlockPreview,false));}
        if(!restoreNames&&settings.PriceInsight&&compatibility.PriceAllowed&&priceFailure.Length==0)
            cleanup.Try("price update",()=>price.Requested(Addon(TooltipKind.Item),Key(TooltipKind.Item)));
        else cleanup.Try("price",RestorePriceOnly);
        if(restoreNames)cleanup.Try("Simple restore after layout",()=>{simple.Restore();});
        cleanup.ThrowIfFailed();
    }
    private void Update(IFramework _)
    {
        if (disposed) return;
        frame++;
        autosave.Tick(Environment.TickCount64);
        if(refreshPending){refreshPending=false;reconnect.Request();}
        reconnect.Tick(Environment.TickCount64,ReconnectFailed);
        RefreshVendor();
        TryAllagan(refreshAllagan);
        TrySimple(refreshSimple);
        TrySimple(refreshSimpleId);
        TrySimple(refreshSimpleText); TrySimple(refreshUnlockPreview); TrySimple(refreshPaintingPreview);

        if (territory != client.TerritoryType) { territory = client.TerritoryType; simpleText.Clear(); simple.Clear(); simpleId.Clear(); allagan.Clear(); Restore(); bridge.Clear(); glamour.Clear(); vendor.Clear(); }
        if (!Available) Restore();
        else if (!settings.Items || !settings.PriceInsight || !compatibility.PriceAllowed) RestorePriceOnly();
        if (!Available) bridge.Clear();
        else if (compatibility.NativeAllowed)
        {
            foreach (var kind in Kinds)
            {
                var native = Addon(kind);
                bridge.Observe(Key(kind), (nint)native, native != null && native->IsVisible);
            }
        }
        LogFeatureStatus("ミラプリ名",glamourNames.Status,ref lastGlamourNameStatus);
        LogFeatureStatus("価格分離", price.Status, ref lastPriceStatus);
        LogFeatureStatus("販売情報", vendor.Status, ref lastVendorStatus);
        LogFeatureStatus("収納情報", glamour.Status, ref lastGlamourStatus);
        LogFeatureStatus("AT中継", allagan.Status, ref lastAllaganStatus);
        LogFeatureStatus("専用追加情報", simple.Status, ref lastSimpleStatus);
        LogFeatureStatus("アイテムID", simpleId.Status, ref lastSimpleIdStatus);
        LogFeatureStatus("文字追記",simpleText.Status,ref lastSimpleTextStatus);
        LogFeatureStatus("アンロック画像",unlockPreview.Status,ref lastUnlockStatus);
        LogFeatureStatus("絵画画像",paintingPreview.Status,ref lastPaintingStatus);
        if (saveTask is { IsCompletedSuccessfully: true }) { saveStatus = saveTask.Result; saveTask = null; }
    }
    private void ReconnectFailed(string step,Exception ex)
        =>log.Debug("Tooltip Extras再接続を再試行予定 ({Step}): {Reason}",step,ex.GetBaseException().Message);
    private bool SimpleTextSelected(int index)=>compatibility.SimpleTextFeatureAllowed(index)&&(index switch {0=>settings.SimpleFood,1=>settings.SimpleMateriaStats,2=>settings.SimpleDesynth,3=>settings.SimpleCollectables,4=>settings.SimpleLogos,5=>settings.SimpleFadedRolls,6=>settings.SimpleGacha,7=>settings.SimpleOutfits,8=>settings.SimpleGlamourName&&compatibility.SimpleGlamourNameAllowed,_=>false});
    private void TrySimple(Action action)
    {
        try { action(); }
        catch (Exception ex) { log.Debug("Tooltip Extras専用追加情報の接続・復元失敗: {Reason}", ex.GetBaseException().Message); }
    }
    private void TryAllagan(Action action)
    {
        try { action(); allaganFailure = ""; }
        catch (Exception ex)
        {
            var reason = ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message;
            if (reason != allaganFailure) log.Debug("Tooltip Extras AT中継だけ停止: {Reason}", reason);
            allaganFailure = reason;
        }
    }
    private void RefreshVendor()
    {
        try { vendor.Refresh(pi, Available && settings.Items && settings.VendorLocation && compatibility.VendorAllowed && vendorFailure.Length == 0); }
        catch (Exception ex) { VendorError(ex); }
    }
    private void VendorError(Exception ex)
    {
        var reason = ex.GetType().Name + ": " + ex.Message;
        if (reason != vendorFailure) log.Debug("Tooltip Extras販売情報だけを停止: {Reason}", reason);
        vendorFailure = reason;
    }
    private void TryVendor(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            VendorError(ex);
        }
    }
    private void LogFeatureStatus(string feature, string status, ref string last)
    {
        if (status == last) return;
        last = status;
        log.Debug("Tooltip Extras {Feature}: {Status}", feature, status);
    }
    private void StopPrice(Exception ex)
    {
        priceFailure = ex.GetType().Name + ": " + ex.Message;
        try { RestorePriceOnly(); } catch (Exception restore) { priceFailure += " / 復元失敗: " + restore.Message; }
        log.Debug("Tooltip Extras価格分離だけを停止: {Reason}", priceFailure);
    }
    private void RefreshCompatibility()
    {
        compatibility.Refresh(pi);
        var text = string.Join("\n", compatibility.Rows);
        if (text == lastCompatibilityLog) return;
        lastCompatibilityLog = text;
        log.Debug("Tooltip Extras互換性: {Details}", text);
    }
    private void Fail(Exception ex)
    {
        faulted = true; failure = ex.GetType().Name + ": " + ex.Message;
        try { Restore(); } catch { /* Retain pending restoration for a later safe retry. */ }
        bridge.Clear();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var cleanup = new Cleanup();
        cleanup.Try("price restore", Restore);
        cleanup.Try("Simple text",simpleText.Dispose);
        cleanup.Try("Simple glamour names",glamourNames.Dispose);
        cleanup.Try("unlock preview",unlockPreview.Dispose);cleanup.Try("painting preview",paintingPreview.Dispose);
        cleanup.Try("Simple ID restore", simpleId.Dispose);
        cleanup.Try("Simple restore", simple.Dispose);
        cleanup.Try("Allagan restore", allagan.Dispose);
        cleanup.Try("vendor restore", vendor.Dispose);

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
