using System.Collections;
using System.Text;
using TooltipExtras;
namespace TooltipExtrasChecks;

internal static unsafe class AllaganChecks
{
    public static void Run(Action<bool, string> check)
    {
        byte[] B(string text) => Encoding.UTF8.GetBytes(text);
        byte[]?[] Before() => [B("Base+prior"), B("Effects"), B("Levels")];
        var state = new RelaySnapshot(); state.Begin(100, 11); state.Set(0, B("Owned"));
        var retained = state.Get(100, 11);
        state.Begin(100, 11);
        check(ReferenceEquals(retained, state.Get(100, 11)), "AT保持: 同じ対象の再更新でproducer未実行でも表示を維持");
        for (var frame = 0; frame < 100; frame++) { state.Begin(100, 11); _ = state.Get(100, 11); }
        check(ReferenceEquals(retained, state.Get(100, 11)), "AT保持: 再更新100回でも1フレームで消えず同じ配列を再利用");
        state.Set(1, B("Sources")); state.Set(0, B("Owned"));
        check(Encoding.UTF8.GetString(state.Get(100, 11)!) == "Owned\n\nSources", "AT保持: 項目別の更新と順序を維持");
        state.Set(0, null);
        check(state.Count == 1 && Encoding.UTF8.GetString(state.Get(100, 11)!) == "Sources", "AT保持: 実際の空結果・無効化は該当項目だけ消す");
        state.Set(1, []);
        check(state.Get(100, 11) == null && state.Count == 0, "AT保持: 全項目の空結果で表示を消す");
        state.Set(0, B("Old")); state.Begin(200, 11);
        check(state.Get(200, 11) == null, "AT保持: 対象切替で旧情報を破棄");
        state.Begin(100, 11);
        check(state.Get(100, 11) == null, "AT保持: A-B-A切替で古い結果を復活させない");
        state.Set(0, B("A")); state.Begin(100, 22);
        check(state.Get(100, 22) == null, "AT保持: Addon再生成で旧情報を破棄");
        state.Set(0, B("A")); state.Clear(); state.Begin(100,22);
        check(state.Get(100, 22) == null, "AT保持: 非表示・無効化後の同じ対象に情報を復活させない");
        state.Set(0, B("A"));
        check(state.Get(200, 22) == null && state.Get(100,22) == null, "AT保持: 別対象からの読出しを拒否して失効");
        state.Begin(100,22); state.Set(0, B("A")); var same = state.Get(100,22); state.Set(0, B("A"));
        check(ReferenceEquals(same, state.Get(100,22)), "AT保持: 同文の再捕捉で描画キャッシュを維持");
        state.Begin(0,22);
        check(state.Get(0,22) == null, "AT保持: 対象IDなしで表示しない");
        state.Begin(100,0);
        check(state.Get(100,0) == null, "AT保持: Addonなしで表示しない");
        var before = Before(); var after = Before(); after[0] = B("Base+prior\nOwned: 2");
        check(AppendDelta.TrySelect(before, after, out var field, out var suffix) && field == 0 && Encoding.UTF8.GetString(suffix) == "\nOwned: 2", "AT: 追加分だけを抽出し先行追記は保持");
        check(Encoding.UTF8.GetString(before[0]!) == "Base+prior", "AT: 境界検査は元バイト列を変更しない");
        after = Before(); after[1] = B("Effects\nSources: test");
        check(AppendDelta.TrySelect(before, after, out field, out _) && field == 1, "AT: Effects欄への追記");
        after = Before(); after[2] = B("Levels\nUses: test");
        check(AppendDelta.TrySelect(before, after, out field, out _) && field == 2, "AT: Levels欄への追記");
        after[0] = B("Base+prior\nOther");
        check(!AppendDelta.TrySelect(before, after, out _, out _), "AT: 複数欄の変更を拒否");
        after = Before(); after[0] = B("Replacement");
        check(!AppendDelta.TrySelect(before, after, out _, out _), "AT: 置換は追記として扱わない");
        after[0] = B("Base");
        check(!AppendDelta.TrySelect(before, after, out _, out _), "AT: 短縮を拒否");
        check(!AppendDelta.TrySelect(before, Before(), out _, out _), "AT: 変更なしは捕捉しない");
        after = Before(); after[0] = null;
        check(!AppendDelta.TrySelect(before, after, out _, out _), "AT: null化を拒否");
        check(!AppendDelta.TrySelect([null], [B("new")], out _, out _), "AT: nullからの生成を拒否");
        check(!AppendDelta.TrySelect([null], [null], out _, out _), "AT: null欄のみなら変更なし");
        check(!AppendDelta.TrySelect(before, [], out _, out _), "AT: 欄数の違いを拒否");
        check(AppendDelta.TrySelect([[]], [B("append")], out _, out suffix) && Encoding.UTF8.GetString(suffix) == "append", "AT: 空文字列への追記");
        byte[] raw = [2, 39, 3, 1, 3, 240]; byte[] whole = [..raw, 10, 2, 19, 2, 3, 3];
        check(AppendDelta.TrySelect([raw], [whole], out _, out suffix) && suffix.SequenceEqual(new byte[]{10,2,19,2,3,3}), "AT: SeStringのバイト境界を再エンコードせず保持");
        var original = new object(); var proxy = new object(); var other = new object(); IList list = new ArrayList { proxy, other };
        AppendDelta.RestoreSlot(list, proxy, original);
        check(ReferenceEquals(list[0], original) && ReferenceEquals(list[1], other) && list.Count == 2, "AT: 解除は自分の登録だけ復元");
        list.Remove(original); AppendDelta.RestoreSlot(list, proxy, original);
        check(list.Count == 1 && ReferenceEquals(list[0], other), "AT: 削除済み登録を復活させない");
        var baseType = typeof(MockTooltipBase); var type = TooltipProxy.Create(baseType); var active = true; var calls = 0;
        var forward = new Action<nint,nint>((n,s) => { if(n==17 && s==29) calls++; });
        var instance = Activator.CreateInstance(type, [null, new Func<bool>(() => active), forward])!;
        var typed = (MockTooltipBase)instance;
        check(typed.IsEnabled, "AT: 動的プロキシの有効状態を転送"); active = false;
        check(!typed.IsEnabled, "AT: 元設定のOFFへ追従");
        typed.OnGenerateItemTooltip((byte*)17, (int*)29);
        check(calls == 1, "AT: 実際のポインター署名から1回転送（参照なし）");
        var invoke = TooltipProxy.Forward(instance, baseType.GetMethod("OnGenerateItemTooltip")!);
        invoke(17,29);
        check(calls == 2, "AT: 元の仮想処理への転送は再帰せず1回実行");
        var fail = new Action<nint,nint>((_,_) => throw new InvalidOperationException("provider"));
        typed = (MockTooltipBase)Activator.CreateInstance(type, [null, new Func<bool>(()=>true), fail])!;
        var threw = false; try { typed.OnGenerateItemTooltip(null,null); } catch(InvalidOperationException e) { threw=e.Message=="provider"; }
        check(threw, "AT: 元処理の例外をTargetInvocationExceptionで包まない");
        check(new Settings().AllaganTools, "AT: 新しい対応元は既定ON");
        check(!System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"AllaganTools\":false}")!.AllaganTools, "AT: 保存済みOFFを維持");
    }
}
public abstract unsafe class MockTooltipBase
{
    public MockTooltipBase(object? logger) { }
    public abstract bool IsEnabled { get; }
    public virtual void OnGenerateItemTooltip(byte* n, int* s) { }
}
