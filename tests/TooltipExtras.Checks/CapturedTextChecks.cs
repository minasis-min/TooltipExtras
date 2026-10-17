using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static class CapturedTextChecks
{
    public static void Run(Action<bool,string> check)
    {
        byte[] Id(string label,ushort color=3) => new SeString(new TextPayload("手防具"),new UIForegroundPayload(color),new TextPayload(label),new UIForegroundPayload(0)).Encode();
        bool Split(string text,string expected) => CapturedText.SplitItemId(Id(text),out var category,out var id) && SeString.Parse(category).TextValue=="手防具" && SeString.Parse(id).TextValue==expected;
        check(Split("   [42735]","[42735]"),"ID: 元生成の10進IDを保持してカテゴリから分離");
        check(Split("   [0xA6EF]","[0xA6EF]"),"ID: 元設定の16進表記を保持");
        check(Split("   [42735 - 0xA6EF]","[42735 - 0xA6EF]"),"ID: 元設定の両表記を保持し再計算しない");
        check(Split("   [0]","[0]"),"ID: 表示済みの数字をそのまま捕捉");
        check(!CapturedText.SplitItemId(Id("   [42735]",5),out _,out _),"ID: 異なる色の追記を誤除去しない");
        check(!CapturedText.SplitItemId(Id(" [42735]"),out _,out _),"ID: 元処理と異なる空白構造を拒否");
        check(!CapturedText.SplitItemId(Id("   [other]"),out _,out _),"ID: 数字でない括弧を拒否");
        check(!CapturedText.SplitItemId(Id("   [42735] extra"),out _,out _),"ID: 後続の別情報を含むものを拒否");
        check(!CapturedText.SplitItemId(Encoding.UTF8.GetBytes("手防具 [42735]"),out _,out _),"ID: プレーンな括弧文字だけで削除しない");
        var nested=new SeString(new TextPayload("手防具"),new UIForegroundPayload(3),new TextPayload("   [1]"),new UIGlowPayload(2),new UIForegroundPayload(0)).Encode();
        check(!CapturedText.SplitItemId(nested,out _,out _),"ID: 未確認の複合payloadを拒否");
        var noBase=new SeString(new UIForegroundPayload(3),new TextPayload("   [1]"),new UIForegroundPayload(0)).Encode();
        check(!CapturedText.SplitItemId(noBase,out _,out _),"ID: 元カテゴリがない場合は拒否");
        var value=new SeString(new UIForegroundPayload(5),new TextPayload("\nOwned: 2\nLocations:\n      Bag - 2\n"),new UIForegroundPayload(0));
        var normalized=CapturedText.TrimAllaganEdges(value.Encode());
        check(SeString.Parse(normalized).TextValue=="Owned: 2\nLocations:\n      Bag - 2","AT余白: 端の改行だけ除去し行間・保管場所の字下げを維持");
        var parsed=SeString.Parse(normalized);
        check(parsed.Payloads.OfType<UIForegroundPayload>().Select(p=>p.ColorKey).SequenceEqual(new ushort[]{5,0}),"AT余白: 色指定とリセットを保持");
        var already=new SeString(new TextPayload("Sources: shop")).Encode();
        check(ReferenceEquals(already,CapturedText.TrimAllaganEdges(already)),"AT余白: 正規化不要なら描画用配列を再利用");
        var unknown=new SeString(new TextPayload("\nA"),new UIForegroundPayload(3),new TextPayload("\nB")).Encode();
        check(ReferenceEquals(unknown,CapturedText.TrimAllaganEdges(unknown)),"AT余白: 未確認の複数本文構造を勝手に変えない");
        var state=new RelaySnapshot(compact:true);state.Begin(1,11);state.Set(0,normalized);state.Set(1,already);
        check(SeString.Parse(state.Get(1,11)!).TextValue=="Owned: 2\nLocations:\n      Bag - 2\nSources: shop","AT余白: 同じプラグイン内は通常の1改行で結合");
    }
}
