using System.Reflection;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static unsafe class SimpleChecks
{
    public static void Run(Action<bool,string> check)
    {
        check(ProducerMethods.IsEmptyInherited(typeof(Passive).GetMethod(nameof(Passive.Generate))!,typeof(EmptyBase)),"Simple: 共通の空メソッドを継承する専用API・画像機能は失敗として扱わない");
        check(!ProducerMethods.IsEmptyInherited(typeof(Writer).GetMethod(nameof(Writer.Generate))!,typeof(EmptyBase)),"Simple: 独自の生成処理を持つ未知機能を空メソッドとして通過させない");
        check(new Settings().SimpleHotkeys&&!System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"SimpleHotkeys\":false}")!.SimpleHotkeys,"Simple: 操作ヒントの既定ONと保存済みOFFを保持");
        byte[] B(string text)=>System.Text.Encoding.UTF8.GetBytes(text);
        var native=B("description\nskill: 690.00 [job]\n[MARKET]\npending");
        var formatted=B("description\nskill: 690 (431) [job]\n[MARKET]\npending");
        var skill=DesynthesisPresentation.Extract(native,formatted);
        check(skill!=null&&Dalamud.Game.Text.SeStringHandling.SeString.Parse(skill).TextValue=="skill: 690 (431) [job]","分解: 変更行だけを抽出し説明文・他プラグインの市場情報をコピーしない");
        var colored=new Dalamud.Game.Text.SeStringHandling.SeString(
            new Dalamud.Game.Text.SeStringHandling.Payloads.UIForegroundPayload(5),new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload("description"),
            Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload.Payload,new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload("skill: 690 (431)"),
            new Dalamud.Game.Text.SeStringHandling.Payloads.UIForegroundPayload(0),Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload.Payload,
            new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload("[MARKET] pending")).Encode();
        var oldColored=new Dalamud.Game.Text.SeStringHandling.SeString(
            new Dalamud.Game.Text.SeStringHandling.Payloads.UIForegroundPayload(5),new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload("description"),
            Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload.Payload,new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload("skill: 690.00"),
            new Dalamud.Game.Text.SeStringHandling.Payloads.UIForegroundPayload(0),Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload.Payload,
            new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload("[MARKET] pending")).Encode();
        var inherited=DesynthesisPresentation.Extract(oldColored,colored);
        var inheritedText=inherited==null?null:Dalamud.Game.Text.SeStringHandling.SeString.Parse(inherited);
        check(inheritedText?.TextValue=="skill: 690 (431)"&&inheritedText.Payloads.OfType<Dalamud.Game.Text.SeStringHandling.Payloads.UIForegroundPayload>().First().ColorKey==5,"分解: SeString改行と前行から引き継ぐ色を保持");
        check(DesynthesisPresentation.Extract(native,native)==null,"分解: 変更のない説明を追加情報として表示しない");
        check(DesynthesisPresentation.Extract(native,B("other description\nskill: 690 (431) [job]\n[MARKET]\npending"))==null,"分解: 本文まで変更された複数行を推測で取り込まない");
        check(DesynthesisPresentation.Extract(native,B("description\nskill: 690 (431) [job]"))==null,"分解: 行の削除・追加が混ざる形式を保留");
        check(DesynthesisPresentation.Extract(B("description\nskill\ntail"),B("description\n\ntail"))==null,"分解: 空になった行を追加情報として表示しない");
        check(DesynthesisPresentation.Extract(new byte[32769],formatted)==null,"分解: 文字列上限を超えるデータを拒否");
        check(DesynthesisPresentation.Extract(B(string.Join("\n",Enumerable.Repeat("line",130))),B("skill"))==null,"分解: 行数上限を超える説明を拒否");
        var before = new SimpleGeometry(500,500,480);
        check(before.IsAppend(new(518,518,498),18,490), "Simple: 元生成処理の高さ・余白を正確に逆算");
        check(!before.IsAppend(new(520,520,498),18,490), "Simple: 別の高さ変化は復元しない");
        check(!before.IsAppend(new(518,517,498),18,490), "Simple: 背景ノードの高さ不一致を拒否");
        check(!before.IsAppend(new(518,518,499),18,490), "Simple: フッター変位の不一致を拒否");
        check(!before.IsAppend(new(518,518,498),18,489), "Simple: 未対応の追記位置を拒否");
        check(!before.IsAppend(new(500,500,480),0,490), "Simple: 空高さを拒否");
        check(!before.IsAppend(new(518,518,498),18,float.NaN), "Simple: 不正座標を拒否");
        check(!new SimpleGeometry(15999,15999,15980).IsAppend(new(16017,16017,15998),18,15989), "Simple: 高さ上限を超える補正を拒否");
        var calls=0; var method=PointerCallback.Create(typeof(byte*), p=>{if(p==31) calls++;});
        method.Invoke(new object(), [Pointer.Box((void*)31,typeof(byte*))]);
        check(calls==1 && method.IsStatic && method.GetParameters()[0].ParameterType==typeof(byte*), "Simple: 実インスタンスを替えず既存MethodInfo経由で1回転送（参照なし）");
        var owner=new Original();var forward=PointerCallback.Forward(owner,typeof(Original).GetMethod("After",BindingFlags.Instance|BindingFlags.NonPublic)!);
        method=PointerCallback.Create(typeof(byte*),forward); method.Invoke(owner,[Pointer.Box((void*)31,typeof(byte*))]);
        check(owner.Calls==1, "Simple: 元のprivateイベントメソッドへ1回転送");
        check(new Settings().SimpleExtras && !System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"SimpleExtras\":false}")!.SimpleExtras,"Simple: 新規既定ONと保存済みOFFを保持");
        var state=new RelaySnapshot();state.Begin(100,11);state.Set(0,[65]);var cached=state.Get(100,11);state.Begin(100,11);
        check(ReferenceEquals(cached,state.Get(100,11)),"Simple: Ctrlで生成を伴わない同一対象の更新でも取得済み表示を保持");
    }
    private unsafe class Original
    {
        public int Calls;
        private void After(byte* p){ if(p==(byte*)31) Calls++; }
    }
    private class EmptyBase{public virtual void Generate(){}}
    private sealed class Passive:EmptyBase{}
    private sealed class Writer:EmptyBase{public int Calls;public override void Generate(){Calls++;}}
}
