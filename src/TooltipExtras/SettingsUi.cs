using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using MinasisUI;
namespace TooltipExtras;

public sealed unsafe partial class Plugin
{
    private readonly Ui settingsUi = new();
    private readonly Action settingsSidebar, settingsDetail;
    private int settingsPage;
    private bool drawingMinasis, settingsChanged;
    private static readonly UiChoice[] PanelSides = [new("below", "下"), new("above", "上")];
    private void DrawSettings()
    {
        settingsUi.Scale = ImGuiHelpers.GlobalScale;
        drawingMinasis = settings.UseMinasisUI; settingsChanged = false;
        using var theme = drawingMinasis ? settingsUi.PushTheme() : null;
        ImGui.SetNextWindowSize(new Vector2(780, 600) * settingsUi.Scale, ImGuiCond.FirstUseEver);
        settingsUi.ConstrainNextTwoColumnWindow(sidebarWidth: 180, minimumDetailWidth: 360, minimumHeight: 380);
        var visible = ImGui.Begin("Tooltip Extras###TooltipExtras.Settings", ref open, ImGuiWindowFlags.NoSavedSettings);
        try
        {
            if (visible) { if (drawingMinasis) settingsUi.TwoColumn("settings-layout", settingsSidebar, settingsDetail, sidebarWidth: 180); else DrawStandardLayout(); }
        }
        finally
        {
            ImGui.End();
            if (settingsChanged) autosave.Queue(SerializeSettings(), Environment.TickCount64);
        }
    }
    private void DrawSettingsSidebar()
    {
        if (drawingMinasis) settingsUi.SidebarHeader("Tooltip Extras", "Ver " + PluginVersion);
        else { ImGui.TextUnformatted("Tooltip Extras"); ImGui.TextUnformatted("Ver " + PluginVersion); ImGui.Separator(); }
        Nav("general", "基本", 0, FontAwesomeIcon.Cog);
        Nav("display", "表示", 1, FontAwesomeIcon.Eye);
        Nav("enabled", "有効", 2, FontAwesomeIcon.Check);
        Nav("diagnostics", "Debug / 診断", 3, FontAwesomeIcon.InfoCircle);
        var remaining = ImGui.GetContentRegionAvail().Y;
        if (remaining > 100 * settingsUi.Scale) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + remaining - 100 * settingsUi.Scale);
        var useMinasis = settings.UseMinasisUI;
        bool changed;
        if (drawingMinasis) changed = settingsUi.UiStyleSelector("ui-style", ref useMinasis);
        else
        {
            ImGui.Separator(); ImGui.TextUnformatted("UI"); var mode = useMinasis ? 0 : 1; ImGui.SetNextItemWidth(-1);
            changed = ImGui.Combo("##ui-style", ref mode, "MinasisUI\0Dalamud標準UI\0"); useMinasis = mode == 0;
        }
        if (changed) { settings.UseMinasisUI = useMinasis; settingsChanged = true; }
    }
    private void Nav(string id, string label, int page, FontAwesomeIcon icon)
    {
        if (drawingMinasis ? settingsUi.NavigationItem(id, label, settingsPage == page, icon.ToIconString(), UiBuilder.IconFont)
            : ImGui.Selectable(label + "###" + id, settingsPage == page)) settingsPage = page;
    }
    private void DrawSettingsDetail()
    {
        switch (settingsPage) { case 0: DrawGeneralSettings(); break; case 1: DrawDisplaySettings(); break; case 2: DrawEnabledSettings(); break; case 3: DrawDiagnosticSettings(); break; }
    }
    private void DrawGeneralSettings()
    {
        Heading("基本");
        var enabled = settings.Enabled;
        if (Checkbox("enabled", "機能を有効にする", ref enabled)) { settings.Enabled = enabled; Restore(); bridge.Clear(); }
        Help("追加情報パネルをまとめてON／OFFします。");
        var items = settings.Items;
        if (Checkbox("items", "アイテムの追加情報", ref items)) { settings.Items = items; Restore(); bridge.Clear(TooltipKind.Item); }
        Help("アイテムにカーソルを合わせたときの追加情報です。");
        var actions = settings.Actions;
        if (Checkbox("actions", "アクションの追加情報", ref actions)) { settings.Actions = actions; bridge.Clear(TooltipKind.Action); }
        Help("マウント・ミニオンなどの追加情報です。");
        var names = settings.ShowSourceNames;
        if (Checkbox("source-names", "引用元プラグイン名を表示", ref names)) settings.ShowSourceNames = names;
        Help("追加情報の上に引用元の名前を表示します。");
    }
    private void DrawDisplaySettings()
    {
        Heading("表示");
        var side = (int)settings.Side;
        if (Dropdown("panel-side", "表示側", ref side, PanelSides)) settings.Side = (PanelSide)side;
        var font = settings.FontSize;
        if (InputFloat("font-size", "文字サイズ (px)", ref font, 1, 10, 40)) settings.FontSize = font;
        var gap = settings.Gap;
        if (InputFloat("gap", "元ツールチップとの間隔 (px)", ref gap, 1, 0, 80)) settings.Gap = gap;
        var x = settings.OffsetX;
        if (InputFloat("offset-x", "横位置の調整 (px)", ref x, 1, -1000, 1000)) settings.OffsetX = x;
        var y = settings.OffsetY;
        if (InputFloat("offset-y", "外側への位置調整 (px)", ref y, 1, -80, 1000)) settings.OffsetY = y;
    }
    private void DrawEnabledSettings()
    {
        var priceEnabled = settings.PriceInsight;
        if (Checkbox("price-insight", "Price Insight", ref priceEnabled)) { RestorePriceOnly(); settings.PriceInsight = priceEnabled; }
        var glamourEnabled = settings.GlamourLog;
        if (Checkbox("glamour-log", "Glamour Log", ref glamourEnabled)) { settings.GlamourLog = glamourEnabled; glamour.Clear(); }
        var vendorEnabled = settings.VendorLocation;
        if (Checkbox("vendor-location", "Item Vendor Location", ref vendorEnabled)) { settings.VendorLocation = vendorEnabled; TryVendor(vendor.Dispose); }
        var simpleEnabled = settings.SimpleExtras;
        if (Checkbox("simple-extras", "Simple Tweaks", ref simpleEnabled)) { Restore(); settings.SimpleExtras = simpleEnabled; TrySimple(simple.Reconnect); TrySimple(simpleId.Reconnect); }
        var atEnabled = settings.AllaganTools;
        if (Checkbox("allagan-tools", "Allagan Tools", ref atEnabled)) { settings.AllaganTools = atEnabled; TryAllagan(allagan.Reconnect); }
        Heading("表示する情報");
        SimpleOption("simple-food","食事・薬品の詳細ステータス",settings.SimpleFood,v=>settings.SimpleFood=v);
        SimpleOption("simple-materia-stats","マテリアの合計・増分",settings.SimpleMateriaStats,v=>settings.SimpleMateriaStats=v);
        SimpleOption("simple-desynth","分解スキル",settings.SimpleDesynth,v=>settings.SimpleDesynth=v);
        SimpleOption("simple-collectables","収集品の納品報酬",settings.SimpleCollectables,v=>settings.SimpleCollectables=v);
        SimpleOption("simple-logos","ロゴスアクションの入手情報",settings.SimpleLogos,v=>settings.SimpleLogos=v);
        SimpleOption("simple-faded-rolls","譜面の製作先・取得状況",settings.SimpleFadedRolls,v=>settings.SimpleFadedRolls=v);
        SimpleOption("simple-gacha","ランダム報酬アイテムの取得状況",settings.SimpleGacha,v=>settings.SimpleGacha=v);
        SimpleOption("simple-glamour-name","アイテム名・ミラプリ名",settings.SimpleGlamourName,v=>settings.SimpleGlamourName=v);
        SimpleOption("simple-outfits","衣装セットの取得状況",settings.SimpleOutfits,v=>settings.SimpleOutfits=v);
        SimpleOption("simple-unlock-image","アンロック可能アイテムのプレビュー",settings.SimpleUnlockPreview,v=>settings.SimpleUnlockPreview=v);
        SimpleOption("simple-painting-image","絵画プレビュー",settings.SimplePaintingPreview,v=>settings.SimplePaintingPreview=v);
        var patchEnabled = settings.PatchInfo;
        if (Checkbox("patch", "Which Patch Was That?", ref patchEnabled)) settings.PatchInfo = patchEnabled;
    }
    private void SimpleOption(string id,string label,bool value,Action<bool> change)
    {
        if(Checkbox(id,label,ref value)){Restore();change(value);simpleText.Clear();}
    }
    private void DrawDiagnosticSettings()
    {
        Heading("Debug / 診断");
        foreach (var row in compatibility.Rows) Text(row);
        Text("Price Insight DLL: " + (compatibility.PriceProviderDetected ? "ロード検出" : "未ロード"));
        Text("Price Insight: " + PriceDisplayStatus());
        Text("Item Vendor Location DLL: " + (compatibility.VendorProviderDetected ? "ロード検出" : "未ロード"));
        Text("Item Vendor Location: " + VendorDisplayStatus());
        Text("Glamour Log: " + (compatibility.GlamourLogAvailable ? "ロード検出 / " : "未ロード / ") + glamour.Status);
        Text("Allagan Tools: " + allagan.Status);
        if (allaganFailure.Length > 0) Text("AT中継: " + allaganFailure);
        Text("Simple Tweaks: " + simple.Status);
        Text("SimpleアイテムID: " + simpleId.Status);
        Text("Simple文字中継: "+simpleText.Status);
        Text("ミラプリ名: "+glamourNames.Status);
        Text("名前の色: "+compatibility.State("NameColorNative").Reason+" / "+compatibility.State("NameColorApi").Reason);
        foreach(var feature in new[]{"SimpleFood","SimpleMateriaStats","SimpleDesynth","SimpleCollectables","SimpleLogos","SimpleFadedRolls","SimpleGacha","SimpleOutfits","SimpleGlamourName"})Text(feature+": "+compatibility.State(feature).Reason);
        Text("アンロック画像: "+unlockPreview.Status);
        Text("絵画画像: "+paintingPreview.Status);
        Text("未対応: SimpleのアクションID");
        Text("共通パネル: " + compatibility.State("Panel").Reason);
        if (faulted) Notice("例外により停止しています。再ロードが必要です: " + failure, NoticeKind.Error);
        var collect = settings.Diagnostics;
        if (Checkbox("diagnostics-enabled", "診断を収集", ref collect)) { settings.Diagnostics = collect; if (!collect) diagnostics.Clear(); }
        Help("通常利用ではOFF推奨です。");
        if (Button("save-diagnostics", "診断を保存", saveTask == null))
        {
            SaveDiagnostics();
        }
        Text("設定保存先: " + store.Path);
        if (saveStatus.Length > 0) Text(saveStatus);
        if (!store.CanSave) Notice(store.Message, NoticeKind.Warning);
        if (autosave.Status.Contains("失敗")) Notice(autosave.Status, NoticeKind.Error);
    }
    private string PriceDisplayStatus()
    {
        if (!settings.Enabled || !settings.Items || !settings.PriceInsight) return "無効 / 最終処理: " + price.Status;
        if (priceFailure.Length > 0) return "価格分離だけ停止 / " + priceFailure;
        if (!compatibility.PriceAllowed) return "分離停止 / " + compatibility.PriceReason;
        if (!Available) return "待機 / ゲーム画面が利用できません";
        var addon = Addon(TooltipKind.Item);
        if (addon == null || !addon->IsVisible) return "対象未表示 / 最終処理: " + price.Status;
        return TryPricePanel(addon, Key(TooltipKind.Item), frame, out _, out _)
            ? "現在の対象: 分離成立（この描画時点）"
            : "現在の対象: 分離未成立 / 最終処理: " + price.Status;
    }
    private string VendorDisplayStatus()
    {
        if (!settings.Enabled || !settings.Items || !settings.VendorLocation) return "無効 / 最終処理: " + vendor.Status;
        if (vendorFailure.Length > 0) return "販売情報だけ停止 / " + vendorFailure;
        if (!compatibility.VendorAllowed) return "分離停止 / " + compatibility.VendorReason + " / " + vendor.Status;
        if (!compatibility.VendorProviderDetected) return "分離停止 / 未ロード / " + vendor.Status;
        var addon = Addon(TooltipKind.Item);
        var captured = Available && addon != null && addon->IsVisible && vendor.HasContent(Key(TooltipKind.Item).Id, (nint)addon);
        return (captured ? "現在の対象: 取得・復元済み（コールバック時点） / " : "現在の対象: 分離未確認 / ") + vendor.Status;
    }
    private void DrawStandardLayout()
    {
        var left = ImGui.BeginChild("sidebar", new Vector2(180 * settingsUi.Scale, 0), false, ImGuiWindowFlags.AlwaysUseWindowPadding);
        try { if (left) settingsSidebar(); } finally { ImGui.EndChild(); }
        ImGui.SameLine();
        var right = ImGui.BeginChild("detail", Vector2.Zero, false, ImGuiWindowFlags.AlwaysUseWindowPadding);
        try { if (right) settingsDetail(); } finally { ImGui.EndChild(); }
    }
    private bool Checkbox(string id, string label, ref bool value)
    {
        var changed = drawingMinasis ? settingsUi.Checkbox(id, label, ref value) : ImGui.Checkbox(label + "###" + id, ref value);
        settingsChanged |= changed; return changed;
    }
    private bool InputFloat(string id, string label, ref float value, float step, float min, float max)
    {
        bool changed;
        if (drawingMinasis) changed = settingsUi.InputFloat(id, label, ref value, step, min: min, max: max);
        else { changed = ImGui.InputFloat(label + "###" + id, ref value, step, step * 10); if (changed) value = float.IsFinite(value) ? Math.Clamp(value, min, max) : min; }
        settingsChanged |= changed; return changed;
    }
    private bool Dropdown(string id, string label, ref int value, IReadOnlyList<UiChoice> choices)
    {
        var changed = drawingMinasis ? settingsUi.Dropdown(id, label, ref value, choices) : ImGui.Combo(label + "###" + id, ref value, "下\0上\0");
        settingsChanged |= changed; return changed;
    }
    private void Heading(string text) { if (drawingMinasis) settingsUi.Heading(text); else ImGui.TextUnformatted(text); }
    private void Text(string text) { if (drawingMinasis) settingsUi.Text(text); else ImGui.TextWrapped(text); }
    private void Notice(string text, NoticeKind kind) { if (drawingMinasis) settingsUi.Notice(text, kind); else ImGui.TextWrapped(text); }
    private bool Button(string id, string label, bool enabled)
    {
        if (drawingMinasis) return settingsUi.Button(id, label, enabled: enabled);
        ImGui.BeginDisabled(!enabled);
        try { return ImGui.Button(label + "###" + id); } finally { ImGui.EndDisabled(); }
    }
    private void Help(string text)
    {
        if (drawingMinasis) settingsUi.Muted(text);
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
            try { ImGui.TextWrapped(text); } finally { ImGui.PopStyleColor(); }
        }
    }
}
