using TooltipExtras;
namespace TooltipExtrasChecks;
internal static class PreviewWaitChecks
{
    public static void Run(Action<bool,string> check)
    {
        var w=new PreviewWait();
        check(!w.ShouldSuppress(0),"画像待ち: 待機していない描画は抑止しない");
        w.Begin(1,10,1000);
        check(w.ShouldSuppress(1000)&&w.ShouldSuppress(1099),"画像待ち: 開始から100ms未満だけ描画抑止");
        w.Begin(1,10,1099);
        check(!w.ShouldSuppress(1100),"画像待ち: 同じ対象の再生成で100ms期限を延長しない");
        check(w.Timeouts==1&&!w.ShouldSuppress(1110)&&w.Timeouts==1,"画像待ち: 期限超過後は本文を優先し超過を重複計上しない");
        w.Begin(2,10,1200);
        check(w.ShouldSuppress(1200),"画像待ち: 対象変更は新しい期限で開始");
        w.Complete(1208);
        check(!w.ShouldSuppress(1209)&&w.LastWaitMs==8,"画像待ち: 配置確定後は画像の準備を待たず本文を表示");
        w.Begin(2,10,1301);
        check(!w.ShouldSuppress(1301),"画像待ち: 確定後の同じ対象の更新でも元の期限を維持");
        w.Clear();w.Begin(2,10,1400);
        check(w.ShouldSuppress(1400),"画像待ち: ホバー終了後の再表示は別の待機として開始");
        w.Clear();check(!w.ShouldSuppress(1401),"画像待ち: 無効化・非表示で抑止を解除");
        w.Begin(3,10,1500);
        check(!w.ShouldSuppress(1700),"画像待ち: 低FPSでもフレーム数ではなく経過時間で期限判定");
        w.Begin(3,11,1800);check(w.ShouldSuppress(1800),"画像待ち: Addon変更は旧待機を引き継がない");
    }
}
