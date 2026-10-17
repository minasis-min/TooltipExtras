using System.Reflection;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static unsafe class SimpleChecks
{
    public static void Run(Action<bool,string> check)
    {
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
}
