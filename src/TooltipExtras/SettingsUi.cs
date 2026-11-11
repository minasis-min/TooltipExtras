using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using MinasisUI;
using MinasisUI.Workspace;
namespace TooltipExtras;

public sealed unsafe partial class Plugin
{
    private readonly WindowSystem settingsWindows = new("TooltipExtras.Settings");
    private readonly WorkspaceUi settingsWorkspace;
    private readonly WorkspaceWindow settingsWindow;
    private readonly WorkspaceFonts settingsFonts;
    private readonly WorkspaceUi[] settingsSurfaces;
    private readonly Action settingsDrawWindows,settingsDetail,settingsScroll,settingsRejected,settingsRetrySave,settingsStandardDetail;
    private int settingsPage;
    private bool drawingMinasis,settingsChanged;
    private long nextDiagnosticRows,nextRejectedRows;
    private string cardPage="",cardTab="",cardId="",cardTitle="";
    private readonly List<string> diagnosticRows=[];
    private RejectedTextLog.Entry[] rejectedRows=[];
    private readonly List<string[]> rejectedDisplay=[];
    private bool resetStandardScroll;
    private long? pendingUiRevision;
    private long uiSaveTicket;
    private static readonly UiChoice[] PanelSides = [new("below", "下"), new("above", "上")];

    private void DrawSettings()
    {
        drawingMinasis=settings.UseMinasisUI;settingsChanged=false;
        settingsWorkspace.Controls.Scale=settingsWorkspace.Flow.Scale=ImGuiHelpers.GlobalScale;
        try
        {
            if(drawingMinasis)
            {
                settingsWindow.IsOpen=true;
                settingsFonts.Draw(settingsSurfaces,settingsDrawWindows);
                open=settingsWindow.IsOpen;
            }
            else
            {
                ImGui.SetNextWindowSize(new Vector2(780,650)*ImGuiHelpers.GlobalScale,ImGuiCond.FirstUseEver);
                ImGui.SetNextWindowSizeConstraints(WorkspaceWindow.MinimumLogicalSize*ImGuiHelpers.GlobalScale,new(float.MaxValue));
                var visible=ImGui.Begin("Tooltip Extras###TooltipExtras.StandardSettings",ref open,ImGuiWindowFlags.NoSavedSettings);
                try{if(visible)DrawStandardSettings();}finally{ImGui.End();}
            }
        }
        finally
        {
            if(!open)
            {
                settingsWorkspace.IsOpen=false;settingsWindow.IsOpen=false;
                diagnosticRows.Clear();rejectedRows=[];rejectedDisplay.Clear();nextDiagnosticRows=nextRejectedRows=0;
            }
            if(settingsChanged)QueueSettings();
        }
    }
    private void DrawStandardSettings()
    {
        var page=settingsWorkspace.Page;
        ImGui.SetNextItemWidth(-1);
        if(ImGui.Combo("##category",ref page,SettingsNavigation.PageLabels))
        {settingsWorkspace.SelectPage(page);resetStandardScroll=true;}
        var tabs=SettingsNavigation.TabLabels(page);
        if(tabs.Length>1)
        {
            var tab=settingsWorkspace.Tab;ImGui.SetNextItemWidth(-1);
            if(ImGui.Combo("##section",ref tab,tabs)){settingsWorkspace.SelectPage(page,tab);resetStandardScroll=true;}
        }
        DrawUiSelector();ImGui.Separator();DrawSettingsSaveStatus();
        settingsWorkspace.PageScrollPane("standard-content",settingsStandardDetail);
    }
    private void DrawStandardDetail()
    {
        if(resetStandardScroll){ImGui.SetScrollY(0);resetStandardScroll=false;}
        DrawSettingsDetail();
    }
    private void DrawUiSelector()
    {
        var useMinasis=settings.UseMinasisUI;
        bool changed;
        if(drawingMinasis)changed=settingsWorkspace.Controls.UiStyleSelector("ui-style",ref useMinasis);
        else
        {
            var style=useMinasis?0:1;ImGui.TextUnformatted("UI");ImGui.SetNextItemWidth(-1);
            changed=ImGui.Combo("##ui-style",ref style,"MinasisUI\0Dalamud標準UI\0");useMinasis=style==0;
        }
        if(changed){settings.UseMinasisUI=useMinasis;settingsChanged=true;}
    }
    private void DrawSettingsContent()=>settingsWorkspace.PageScrollPane("settings-content",settingsScroll);
    private void DrawSettingsSaveStatus()
    {
        settingsWorkspace.PersistenceStatus("workspace-save");
        if(!store.CanSave)Notice(store.Message,NoticeKind.Warning);
        else if(!settingsWorkspace.Persistence.HasUnsavedChanges&&autosave.Status.Contains("失敗"))
            settingsWorkspace.Controls.SaveStatus("settings-save",UiSavePhase.Failed,autosave.Status,settingsRetrySave);
    }
    private void DrawSettingsCards()
    {
        DrawSettingsSaveStatus();
        var state=settingsWorkspace.State;
        if(cardPage!=state.PageId||cardTab!=state.TabId)
        {cardPage=state.PageId;cardTab=state.TabId;cardId="settings-"+cardPage+"-"+cardTab;cardTitle=SettingsNavigation.Title(state);}
        settingsWorkspace.Card(cardId,cardTitle,100,settingsDetail);
    }
    private void DrawSettingsDetail()
    {
        settingsPage=settingsWorkspace.Page;
        switch(settingsPage){case 0:DrawGeneralSettings();break;case 1:DrawDisplaySettings();break;case 2:DrawEnabledSettings();break;case 3:DrawDiagnosticSettings();break;}
    }
    private void QueueSettings()
    {
        var ticket=autosave.Queue(SerializeSettings(),Environment.TickCount64);
        if(pendingUiRevision!=null)uiSaveTicket=ticket;
    }
    private void RequestUiSave(WorkspaceSaveRequest request)
    {
        settings.UiPreferences=request.Preferences;pendingUiRevision=request.Revision;QueueSettings();
    }
    private void CompleteUiSave()
    {
        if(pendingUiRevision is not {} revision||autosave.CompletedRevision<uiSaveTicket)return;
        pendingUiRevision=null;
        if(store.CanSave&&autosave.Status.StartsWith("保存しました",StringComparison.Ordinal))settingsWorkspace.Persistence.AcknowledgeSaved(revision);
        else settingsWorkspace.Persistence.ReportSaveFailure(revision,autosave.Status);
    }
    private void DisposeSettingsUi()
    {
        var cleanup=new Cleanup();
        try
        {
            cleanup.Try("settings windows",settingsWindows.RemoveAllWindows);
            cleanup.Try("settings workspace",settingsWorkspace.Dispose);
        }
        finally
        {
            settingsWorkspace.Persistence.SaveRequested-=RequestUiSave;
            settingsWorkspace.SidebarAfterNavigation=null;
            diagnosticRows.Clear();rejectedRows=[];rejectedDisplay.Clear();
        }
        cleanup.ThrowIfFailed();
    }
    private void DrawGeneralSettings()
    {
        var enabled = settings.Enabled;
        if (Checkbox("enabled", "機能を有効にする", ref enabled)) { settings.Enabled = enabled; Restore(); bridge.Clear(); }
        Help("追加情報パネルをまとめてON／OFFします。");
        var exclusive=settings.ExclusiveOutput;
        if(Checkbox("exclusive-output","対応する追加情報をTEだけに表示",ref exclusive))
        {Restore();settings.ExclusiveOutput=exclusive;refreshPending=true;}
        Help("DR・AT・Item Vendor Location・Simple Tweaksの対応する文字情報は、取得できない場合も元へ戻さず追記を保留します。TEを無効にすると元プラグインの表示に戻ります。");
        Help("画像・専用ノード・その他のプラグインは従来の分離方法を使用します。");
        var items = settings.Items;
        if (Checkbox("items", "アイテムの追加情報", ref items)) { settings.Items = items; Restore(); bridge.Clear(TooltipKind.Item); }
        Help("アイテムにカーソルを合わせたときの追加情報です。");
        var actions = settings.Actions;
        if (Checkbox("actions", "アクションの追加情報", ref actions)) { settings.Actions = actions; bridge.Clear(TooltipKind.Action); }
        Help("マウント・ミニオンなどの追加情報です。");
        var names = settings.ShowSourceNames;
        if (Checkbox("source-names", "引用元プラグイン名を表示", ref names)) settings.ShowSourceNames = names;
        Help("追加情報の上に引用元の名前を表示します。");
        if(drawingMinasis)DrawUiSelector();
    }
    private void DrawDisplaySettings()
    {
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
        if (settingsWorkspace.State.TabId == "details") { DrawSimpleOptions(); return; }
        var priceEnabled = settings.PriceInsight;
        if (Checkbox("price-insight", "Price Insight", ref priceEnabled)) { RestorePriceOnly(); settings.PriceInsight = priceEnabled; }
        var glamourEnabled = settings.GlamourLog;
        if (Checkbox("glamour-log", "Glamour Log", ref glamourEnabled)) { settings.GlamourLog = glamourEnabled; glamour.Clear(); }
        var vendorEnabled = settings.VendorLocation;
        if (Checkbox("vendor-location", "Item Vendor Location", ref vendorEnabled)) { settings.VendorLocation = vendorEnabled; TryVendor(vendor.Dispose); }
        var simpleEnabled = settings.SimpleExtras;
        if (Checkbox("simple-extras", "Simple Tweaks", ref simpleEnabled)) { Restore(); settings.SimpleExtras = simpleEnabled; TrySimple(simple.Reconnect); TrySimple(simpleId.Reconnect); TrySimple(simpleActionId.Reconnect); }
        var atEnabled = settings.AllaganTools;
        if (Checkbox("allagan-tools", "Allagan Tools", ref atEnabled)) { settings.AllaganTools = atEnabled; TryAllagan(allagan.Reconnect); }
        var dailyEnabled=settings.DailyRoutines;
        if(Checkbox("daily-routines","Daily Routines",ref dailyEnabled)){settings.DailyRoutines=dailyEnabled;daily.Reconnect();dailyActions.Reconnect();}
        Help("商店・マーケット・分解・IDなどの追加情報を表示します。元の機能も有効にしてください。");
        var patchEnabled = settings.PatchInfo;
        if (Checkbox("patch", "Which Patch Was That?", ref patchEnabled)) settings.PatchInfo = patchEnabled;
    }
    private void DrawSimpleOptions()
    {
        foreach(var feature in SimpleTextFeatures.All) if(feature.Change!=null)
            SimpleOption(feature.OptionId,feature.OptionLabel,feature.Selected(settings),feature.Change!);
        SimpleOption("simple-unlock-image","アンロック可能アイテムのプレビュー",settings.SimpleUnlockPreview,static(s,v)=>s.SimpleUnlockPreview=v);
        SimpleOption("simple-painting-image","絵画プレビュー",settings.SimplePaintingPreview,static(s,v)=>s.SimplePaintingPreview=v);
    }
    private void SimpleOption(string id,string label,bool value,Action<Settings,bool> change)
    {
        if(Checkbox(id,label,ref value)){Restore();change(settings,value);simpleText.Clear();}
    }
    private void DrawDiagnosticSettings()
    {
        switch(settingsWorkspace.State.TabId)
        {
            case "rejected": settingsWorkspace.Controls.Collapsing("rejected-text","分離できなかった文字列",settingsRejected); break;
            case "files": DrawDiagnosticFiles(); break;
            default:
                RefreshDiagnosticRows();
                foreach(var row in diagnosticRows) Text(row);
                if(faulted) Notice("例外により停止しています。再ロードが必要です: " + failure, NoticeKind.Error);
                break;
        }
    }
    private void RefreshDiagnosticRows()
    {
        var now=Environment.TickCount64;
        if(now<nextDiagnosticRows)return;
        nextDiagnosticRows=now+250;diagnosticRows.Clear();
        foreach (var row in compatibility.Rows) diagnosticRows.Add(row);
        diagnosticRows.Add("Price Insight DLL: " + (compatibility.PriceProviderDetected ? "ロード検出" : "未ロード"));
        diagnosticRows.Add("Price Insight: " + PriceDisplayStatus());
        diagnosticRows.Add("Item Vendor Location DLL: " + (compatibility.VendorProviderDetected ? "ロード検出" : "未ロード"));
        diagnosticRows.Add("Item Vendor Location: " + VendorDisplayStatus());
        diagnosticRows.Add("Glamour Log: " + (compatibility.GlamourLogAvailable ? "ロード検出 / " : "未ロード / ") + glamour.Status);
        diagnosticRows.Add("Allagan Tools: " + allagan.Status);
        diagnosticRows.Add("Daily Routines: "+daily.Status);
        diagnosticRows.Add("DRの機能別判定: "+daily.FeatureStatus);
        diagnosticRows.Add("DRの追記欄: "+daily.FieldStatus);
        diagnosticRows.Add("TE専用の出力: "+(settings.ExclusiveOutput?"有効":"無効")+" / "+presentationStatus);
        diagnosticRows.Add($"書込み前の分離: DR {daily.WithheldOperations} / AT {allagan.WithheldWrites} / Simple {simpleText.WithheldWrites}");
        diagnosticRows.Add("Daily Routines アクション: "+dailyActions.Status);
        diagnosticRows.Add("DRアクションの機能別判定: "+dailyActions.FeatureStatus);
        if (allaganFailure.Length > 0) diagnosticRows.Add("AT中継: " + allaganFailure);
        diagnosticRows.Add("Simple Tweaks: " + simple.Status);
        diagnosticRows.Add("SimpleアイテムID: " + simpleId.Status);
        diagnosticRows.Add("共通文字列中継: "+stringSink.Status);
        diagnosticRows.Add("Simple文字中継: "+simpleText.Status);
        if(simpleText.LastRegistrationFailure.Length>0)diagnosticRows.Add("Simple登録: "+simpleText.LastRegistrationFailure);
        diagnosticRows.Add("ミラプリ名: "+glamourNames.Status);
        diagnosticRows.Add("名前の色: "+compatibility.State("NameColorNative").Reason+" / "+compatibility.State("NameColorApi").Reason);
        foreach(var feature in SimpleTextFeatures.All)diagnosticRows.Add(feature.Group+": "+compatibility.State(feature.Group).Reason);
        diagnosticRows.Add("アンロック画像: "+unlockPreview.Status);
        diagnosticRows.Add("絵画画像: "+paintingPreview.Status);
        diagnosticRows.Add("SimpleアクションID: "+simpleActionId.Status);
        diagnosticRows.Add("共通パネル: " + compatibility.State("Panel").Reason);
    }
    private void DrawDiagnosticFiles()
    {
        var collect = settings.Diagnostics;
        if (Checkbox("diagnostics-enabled", "診断を収集", ref collect)) { settings.Diagnostics = collect; if (!collect) diagnostics.Clear(); }
        Help("通常利用ではOFF推奨です。");
        if (Button("save-diagnostics", "診断を保存", saveTask == null))
        {
            SaveDiagnostics();
        }
        Help("診断JSONは直近10ファイルを保持します。保存後、古い診断ファイルを自動で削除します。");
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
            ? "現在の対象: 分離成立（診断更新時点）"
            : "現在の対象: 分離未成立 / 最終処理: " + price.Status;
    }
    private void DrawRejectedText()
    {
        var enabled=rejectedText.Enabled;
        if(Checkbox("rejected-text-enabled","未分離の文字列を記録",ref enabled,persist:false)){rejectedText.Enabled=enabled;rejectedRows=[];rejectedDisplay.Clear();nextRejectedRows=0;}
        Help("ONにしてから再現してください。直近20件、各1024文字まで記録し、診断JSONにも含めます。同じ内容は1件にまとめ、発生回数を表示します。OFFまたは再ロードで消去します。");
        Help("ツールチップ内の名前なども記録される場合があります。共有前に内容を確認してください。");
        Help("文字列を取得する前に処理を止めた場合は理由だけを表示します。表示内容は元の欄全体を含む場合があります。");
        var now=Environment.TickCount64;
        if(now>=nextRejectedRows)
        {
            rejectedRows=rejectedText.Snapshot();nextRejectedRows=now+250;rejectedDisplay.Clear();
            foreach(var entry in rejectedRows)rejectedDisplay.Add([
                entry.Source+(entry.Field is {} field?$" / 欄 {field}":"")+$" / {entry.Count}回",
                "理由: "+entry.Reason,entry.Text??"（文字列未取得）"]);
        }
        var entries=rejectedRows;
        if(settingsWorkspace.Flow.GuardedAction("clear-rejected-text","記録を消去",entries.Length>0)){rejectedText.Clear();rejectedRows=entries=[];rejectedDisplay.Clear();nextRejectedRows=0;}
        if(entries.Length==0){Text("記録はありません。");return;}
        foreach(var row in rejectedDisplay)
        {
            if(drawingMinasis)settingsWorkspace.SeparatorLine();else ImGui.Separator();
            foreach(var line in row)Text(line);
        }
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
    private bool Checkbox(string id,string label,ref bool value,bool persist=true)
    {
        bool changed;
        if(drawingMinasis)changed=settingsWorkspace.Controls.Checkbox(id,label,ref value);
        else
        {
            changed=ImGui.Checkbox("##"+id,ref value);ImGui.SameLine();ImGui.TextWrapped(label);
            if(ImGui.IsItemClicked()){value=!value;changed=true;}
        }
        settingsChanged|=changed&&persist;return changed;
    }
    private bool InputFloat(string id,string label,ref float value,float step,float min,float max)
    {
        bool changed;
        if(drawingMinasis)changed=settingsWorkspace.Controls.InputFloat(id,label,ref value,step,min:min,max:max);
        else
        {
            ImGui.TextWrapped(label);ImGui.SetNextItemWidth(-1);
            changed=ImGui.InputFloat("##"+id,ref value,step,step*10);
            if(changed)value=float.IsFinite(value)?Math.Clamp(value,min,max):min;
        }
        settingsChanged|=changed;return changed;
    }
    private bool Dropdown(string id,string label,ref int value,IReadOnlyList<UiChoice> choices)
    {
        bool changed;
        if(drawingMinasis)changed=settingsWorkspace.Controls.Dropdown(id,label,ref value,choices);
        else {ImGui.TextWrapped(label);ImGui.SetNextItemWidth(-1);changed=ImGui.Combo("##"+id,ref value,"下\0上\0");}
        settingsChanged|=changed;return changed;
    }
    private void Text(string text){if(drawingMinasis)settingsWorkspace.Flow.Text(text);else ImGui.TextWrapped(text);}
    private void Notice(string text,NoticeKind kind){if(drawingMinasis)settingsWorkspace.Controls.Notice(text,kind);else ImGui.TextWrapped(text);}
    private bool Button(string id,string label,bool enabled)
    {
        if(drawingMinasis)return settingsWorkspace.Flow.Command(id,label,enabled:enabled);
        ImGui.BeginDisabled(!enabled);try{return ImGui.Button(label+"###"+id);}finally{ImGui.EndDisabled();}
    }
    private void Help(string text){if(drawingMinasis)settingsWorkspace.Flow.Muted(text);else {ImGui.PushStyleColor(ImGuiCol.Text,ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);try{ImGui.TextWrapped(text);}finally{ImGui.PopStyleColor();}}}
}
