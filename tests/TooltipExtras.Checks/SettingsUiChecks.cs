using System.Text.Json;
using MinasisUI.Workspace;
using TooltipExtras;
namespace TooltipExtrasChecks;

internal static class SettingsUiChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var legacy=JsonSerializer.Deserialize<Settings>("""
            {"Version":1,"Enabled":false,"UseMinasisUI":false,"DailyRoutines":false,
             "SimpleFood":false,"Side":1,"FontSize":23,"Gap":11,"OffsetX":-43,"OffsetY":17}
            """)!;
        legacy.Normalize();
        check(!legacy.Enabled&&!legacy.UseMinasisUI&&!legacy.DailyRoutines&&!legacy.SimpleFood
            &&legacy.Side==PanelSide.Above&&legacy.FontSize==23&&legacy.Gap==11&&legacy.OffsetX==-43&&legacy.OffsetY==17,
            "UI移行: 旧Version1の明示値とDalamudUI選択を変えず読み込む");
        check(legacy.UiPreferences is {SidebarCollapsed:false}&&legacy.UiPreferences.ColumnWidths.Count==0,
            "UI移行: 旧設定に存在しないWorkspace状態だけを補う");
        legacy.UiPreferences=new(){SidebarCollapsed=true,ColumnWidths=new(){{"test",240}}};
        var roundtrip=JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(legacy))!;
        check(roundtrip.UiPreferences.SidebarCollapsed&&roundtrip.UiPreferences.ColumnWidths["test"]==240&&!roundtrip.UseMinasisUI,
            "UI移行: Workspace状態と機能値を既存の設定JSONへ一緒に保存・復元");
        var nullPreferences=JsonSerializer.Deserialize<Settings>("{\"UiPreferences\":null}")!;nullPreferences.Normalize();
        check(nullPreferences.UiPreferences!=null,"UI移行: nullのWorkspace状態を補正して既存設定を壊さない");
        var definition=SettingsNavigation.Definition("test");
        check(definition.Pages.Count==4&&definition.Pages.Select(p=>p.Id).Distinct().Count()==4,
            "UI移行: 基本・表示・有効機能・診断の安定IDを維持");
        check(definition.Pages.Take(2).All(p=>p.Tabs.Count==0)&&definition.Pages.Skip(2).All(p=>p.Tabs.Count is >=2 and <=5),
            "UI移行: 不要なタブ列を作らず有効機能2群・診断3群へ整理");
        check(WorkspaceWindow.MinimumLogicalSize==new System.Numerics.Vector2(450,600),
            "UI移行: 共通Windowの最小サイズ450×600を利用");
        using var started=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var saves=new List<string>();
        var save=new SettingsAutosave(json=>{if(json=="old"){started.Set();release.Wait();}saves.Add(json);return "保存しました";});
        var old=save.Queue("old",0);save.Tick(300);
        check(started.Wait(5000),"UI保存: 古いスナップショットの保存中に新しい変更を受け付ける");
        var latest=save.Queue("latest",301);release.Set();
        check(SpinWait.SpinUntil(()=>{save.Tick(302);return save.CompletedRevision==old;},5000)&&save.CompletedRevision<latest,
            "UI保存: 古い書込み完了で新しいWorkspace変更を保存済みにしない");
        save.Flush();
        check(save.CompletedRevision==latest&&saves.SequenceEqual(new[]{"old","latest"}),
            "UI保存: 新しい変更の書込み完了と終了時Flushの世代が一致");
        var failed=new SettingsAutosave(_=>"保存失敗: test");var failedTicket=failed.Queue("settings",0);failed.Flush();
        check(failed.CompletedRevision==failedTicket&&failed.Status.StartsWith("保存失敗"),
            "UI保存: 失敗した完了通知は成功扱いせず理由を維持");
    }
}
