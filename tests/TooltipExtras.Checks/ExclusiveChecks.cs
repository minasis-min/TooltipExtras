using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FFXIVClientStructs.FFXIV.Component.GUI;
using InteropGenerator.Runtime;
using TooltipExtras;
using static TooltipExtrasChecks.DailyRoutineChecks;
namespace TooltipExtrasChecks;

internal static unsafe class ExclusiveChecks
{
    public static void Run(Action<bool,string> check)
    {
        byte[] B(string text)=>Encoding.UTF8.GetBytes(text);
        var calls=0;var forwarded=0;
        (bool Read,bool Managed,bool Suppress) forwardedFlags=default;
        StringArrayData.Delegates.SetValue forward=(a,f,v,r,m,s)=>{forwarded++;forwardedFlags=(r,m,s);};
        using var sink=new NativeStringSink(forward);
        StringArrayData native=default,other=default;
        var values=stackalloc CStringPointer[64];
        native.StringArray=values;native.AtkArrayData.Size=64;
        var plain=B("native\0");var extra=B("native\nextra\0");
        fixed(byte* p=plain,e=extra)
        {
            values[13]=p;
            sink.SetValue(&native,13,e,false,true,false);
            check(forwarded==1,"TE専用: スコープ外の書込みは元処理へ1回転送");
            using(var scope=sink.Begin((nint)(&native)))
            {
                sink.SetValue(&native,13,e,false,true,false);
                check(forwarded==1&&Encoding.UTF8.GetString(scope.Writes[13]!)=="native\nextra"
                    &&Encoding.UTF8.GetString(scope.Before[13]!)=="native"&&values[13].Value==p,"TE専用: 実際の追加文字列を取得して元の配列とポインターを変更しない");
                sink.SetValue(&other,13,e,false,true,false);
                check(forwarded==2,"TE専用: 別の配列の書込みは阻止しない");
                var address=(nint)(&native);var text=(nint)e;
                Task.Run(()=>sink.SetValue((StringArrayData*)address,13,(byte*)text,false,true,false)).GetAwaiter().GetResult();
                check(forwarded==3,"TE専用: 別スレッドの書込みは阻止しない");
                var nestedRejected=false;try{using var nested=sink.Begin(address);}catch(InvalidOperationException){nestedRejected=true;}
                check(nestedRejected,"TE専用: 入れ子の捕捉で別の所有範囲を横取りしない");
                sink.SetValue(&native,300,e,false,true,false);
                check(scope.Failure.Length>0&&forwarded==3,"TE専用: 不正な追記欄は元配列へ流さず取得失敗を記録");
            }
            // Exercise the generated unmanaged ABI, including its bool arguments.
            NativeStringSink.SetStringNative detour=sink.SetValue;
            var pointer=(delegate* unmanaged<StringArrayData*,int,CStringPointer,bool,bool,bool,void>)Marshal.GetFunctionPointerForDelegate(detour);
            using(var scope=sink.Begin((nint)(&native)))
            {
                pointer(&native,13,e,false,true,false);
                check(scope.Writes[13]!.AsSpan().SequenceEqual(B("native\nextra"))&&forwarded==3,"TE専用: 実際のunmanaged署名を通っても文字列とboolのABIが一致");
            }
            GC.KeepAlive(detour);
            try{using var scope=sink.Begin((nint)(&native));throw new InvalidOperationException();}catch(InvalidOperationException){}
            sink.SetValue(&native,13,e,false,true,false);
            check(forwarded==4,"TE専用: formatter例外後は通常の書込みへ戻る");
            var scope2=sink.Begin((nint)(&native));sink.Dispose();scope2.Dispose();
            var disposedRejected=false;try{using var scope=sink.Begin((nint)(&native));}catch(InvalidOperationException){disposedRejected=true;}
            check(disposedRejected,"TE専用: 実行中の解除はスコープ終了時に完了し再利用しない");
            using var isolatedSink=new NativeStringSink(forward);
            NativeStringSink.SetStringNative isolatedDetour=isolatedSink.SetValue;
            var dirtyFlags=(delegate* unmanaged<StringArrayData*,int,CStringPointer,uint,uint,uint,void>)Marshal.GetFunctionPointerForDelegate(isolatedDetour);
            dirtyFlags(&native,13,e,0xffffff00,0xffffff01,0xffffff00);
            check(forwardedFlags==(false,true,false),"TE専用: native boolの上位ビットを無視して1byteの値を元処理へ正しく転送");
            GC.KeepAlive(isolatedDetour);
            var real=(nint)(&native);var first=(nint)e;var sawOwnWrite=false;
            var result=isolatedSink.Capture(&native,isolated=>
            {
                var array=(StringArrayData*)isolated;
                isolatedSink.SetValue(array,13,(byte*)first,false,true,false);
                sawOwnWrite=isolated!=real&&Encoding.UTF8.GetString(new ReadOnlySpan<byte>(array->StringArray[13].Value,12))=="native\nextra";
                var second=B("native\nextra\nsecond\0");fixed(byte* secondText=second)isolatedSink.SetValue(array,13,secondText,false,true,false);
            });
            check(sawOwnWrite&&values[13].Value==p&&result.Before[13]!.AsSpan().SequenceEqual(B("native"))
                &&result.Writes[13]!.AsSpan().SequenceEqual(B("native\nextra\nsecond"))&&result.Array==0&&result.Pins.Count==0,
                "TE専用: private配列内で連続書込みと読戻しを行い、実配列を変えず一時ポインターとpinを解放");
            var view=new BorrowedTooltip(typeof(FakeItemTooltip));var originalTooltip=new FakeItemTooltip(&native,null);
            var borrowed=view.Capture(isolatedSink,originalTooltip,copy=>
            {
                var tooltip=(FakeItemTooltip)copy;isolatedSink.SetValue(tooltip.Array,13,(byte*)first,false,true,false);
                check(tooltip.Array!=(StringArrayData*)real&&tooltip.Array->StringArray[13].Value!=((StringArrayData*)real)->StringArray[13].Value,
                    "販売情報: 元のtooltipオブジェクトの代わりに一時配列のビューでformatterを実行");
            });
            check(originalTooltip.Array==&native&&values[13].Value==p&&borrowed.Writes.Count==1,"販売情報: 元オブジェクトと文字列を一度も書き換えず出力を取得");
            var captureLog=new RejectedTextLog{Enabled=true};isolatedSink.Rejected=captureLog;var failures=0;
            try{isolatedSink.Capture(&native,isolated=>{failures++;isolatedSink.SetValue((StringArrayData*)isolated,13,(byte*)first,false,true,false);throw new InvalidOperationException();},"probe");}catch(InvalidOperationException){}
            check(failures==1&&captureLog.Snapshot().Single().Text=="native\nextra"&&values[13].Value==p,
                "未分離履歴: formatter例外時の部分出力を記録し、元文字列と呼出し回数を維持");
            check(CompatibilityContract.Value(typeof(FakeItemTooltip),"body",".ctor").Contains("FakeItemTooltip"),"互換性: 一時ビューに使う実際のコンストラクター本体を検査");
        }
        IDictionary map=new ConcurrentDictionary<Type,ImmutableList<Delegate>>();
        ObservationCallback callback=(ObservationKind kind,uint id,ref List<ObservedModification> list)=>
        {calls++;list.Add(new(){Target=13,Type=2,Text=new(B("[MARKET]\nprice"))});list.Add(new(){Target=63,Type=0,Text=new(Array.Empty<byte>())});};
        var registered=ImmutableList.Create<Delegate>(callback);map[typeof(ObservationCallback)]=registered;
        var observer=new DailyRoutineObservation(map,typeof(ObservationCallback),callback,null,()=>true,(field,op,text)=>field==13&&op==2?text:null);
        var existing=new ObservedModification{Target=2,Type=2,Text=new(B("foreign"))};var mods=new List<ObservedModification>{existing};var incoming=mods;
        using(observer.Begin(7))((ObservationCallback)((ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(calls==1&&ReferenceEquals(incoming,mods)&&mods.Count==1&&ReferenceEquals(mods[0],existing),"DR専用: 元処理は1回だけ実行し、先行する別の追加命令をそのまま保持");
        check(observer.Withheld==2&&observer.Outputs.Count==1&&!observer.Failed&&ReferenceEquals(map[typeof(ObservationCallback)],registered),"DR専用: MARKETと空Shop操作を実配列へ適用せず出力と登録を正しく返す");
        var rejected=new DailyRoutineObservation(map,typeof(ObservationCallback),callback,null,()=>true,(_,_,_)=>throw new InvalidOperationException());
        using(rejected.Begin(7))((ObservationCallback)((ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(rejected.Failed&&rejected.Outputs.Count==0&&mods.Count==1&&ReferenceEquals(mods,incoming),"DR専用: 取得失敗でも元の命令リストへ追記を戻さない");
        ObservationCallback replaces=(ObservationKind kind,uint id,ref List<ObservedModification> list)=>{calls++;list=[new(){Target=35,Type=0,Text=new(B("skill"))}];};
        map[typeof(ObservationCallback)]=ImmutableList.Create<Delegate>(replaces);
        var replacement=new DailyRoutineObservation(map,typeof(ObservationCallback),replaces,null,()=>true,(_,_,text)=>text);
        using(replacement.Begin(7))((ObservationCallback)((ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(ReferenceEquals(mods,incoming)&&replacement.Outputs.Count==1,"DR専用: formatterがrefリストを交換しても元リストを復元");
        ObservationCallback fails=(ObservationKind kind,uint id,ref List<ObservedModification> list)=>{list.Add(new(){Target=13,Type=2,Text=new(B("partial"))});throw new InvalidOperationException("formatter");};
        map[typeof(ObservationCallback)]=ImmutableList.Create<Delegate>(fails);
        var failing=new DailyRoutineObservation(map,typeof(ObservationCallback),fails,null,()=>true,(_,_,text)=>text);var threw=false;
        try{using(failing.Begin(7))((ObservationCallback)((ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);}catch(InvalidOperationException){threw=true;}
        check(threw&&failing.Failed&&ReferenceEquals(mods,incoming)&&mods.Count==1&&failing.Outputs.Count==0,"DR専用: 元処理の例外でも部分追記を流さず元の例外を保持");
        map[typeof(ObservationCallback)]=registered;
        using(observer.Begin(8))((ObservationCallback)((ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(observer.Failed&&observer.Outputs.Count==0&&mods.Count==1,"DR専用: 遅れた別アイテムの追加情報を描画せず元にも流さない");
        var previousCalls=calls;var unsupported=new DailyRoutineObservation(map,typeof(ObservationCallback),callback,null,()=>true,(_,_,text)=>text,executeExclusive:false);
        using(unsupported.Begin(7))((ObservationCallback)((ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(unsupported.Failed&&calls==previousCalls&&mods.Count==1,"DR専用: 未確認の表示処理は呼び出さず他のプラグイン機能の登録を保持");
        var unsafeCalls=0;
        ObservationCallback incompatible=(ObservationKind kind,uint id,ref List<ObservedModification> list)=>{unsafeCalls++;throw new InvalidOperationException();};
        var combined=ImmutableList.Create<Delegate>(incompatible,callback);map[typeof(ObservationCallback)]=combined;
        var isolatedFailure=new DailyRoutineObservation(map,typeof(ObservationCallback),incompatible,null,()=>true,(_,_,text)=>text,executeExclusive:false);
        using(isolatedFailure.Begin(7))using(observer.Begin(7))
            foreach(var source in (ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)
                ((ObservationCallback)source)(ObservationKind.Normal,7,ref mods);
        check(unsafeCalls==0&&isolatedFailure.Failed&&observer.Outputs.Count==1&&ReferenceEquals(mods,incoming)
            &&ReferenceEquals(map[typeof(ObservationCallback)],combined),
            "DR更新: 非互換の機能だけを保留し、他機能の分離と元登録を維持");
        map[typeof(ObservationCallback)]=registered;
        var drLog=new RejectedTextLog{Enabled=true};var unclassified=new DailyRoutineObservation(map,typeof(ObservationCallback),callback,null,()=>true,(_,_,_)=>null)
            {Rejected=(field,reason,text)=>drLog.Record("DR probe",field,reason,text)};
        using(unclassified.Begin(7))((ObservationCallback)((ImmutableList<Delegate>)map[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(drLog.Snapshot().Single().Text=="[MARKET]\nprice"&&ReferenceEquals(mods,incoming)&&mods.Count==1,
            "未分離履歴: DRの未対応文字列を記録し、元の命令リストへ戻さない");
        var notices=new OutputNotices();var printed=0;
        var hookLock=new System.Threading.Lock();var hookMap=new System.Collections.Hashtable();
        var ownership=new StringHookOwnership(hookLock,hookMap,o=>(Dalamud.Hooking.IDalamudHook)o);
        var own=new FakeStringHook(123);hookMap[1]=own;
        var lockRejected=false;try{ownership.Verify(123,own);}catch(InvalidOperationException){lockRejected=true;}
        check(lockRejected,"専用出力: ホストの登録ロック外では一時データを渡さない");
        using(ownership.Enter()){
            ownership.Verify(123,own);
            hookMap[2]=new FakeStringHook(456);ownership.Verify(123,own);
            var foreignHook=new FakeStringHook(123){IsEnabled=false};hookMap[3]=foreignHook;
            var rejectedHook=false;try{ownership.Verify(123,own);}catch(NotSupportedException){rejectedHook=true;}
            check(rejectedHook,"専用出力: 同じ出力先への他の登録は無効中でも拒否し、別アドレスは許可");
            hookMap.Remove(3);hookMap.Remove(1);
            var lost=false;try{ownership.Verify(123,own);}catch(NotSupportedException){lost=true;}
            check(lost,"専用出力: 自身のフック登録を失った場合はformatterを呼ばない");
        }
        for(var i=0;i<100;i++)notices.Report("DR",()=>printed++);
        notices.Report("panel",()=>printed++);
        check(printed==2,"通知: 高速切替・継続失敗100回でも同種の通知は1回");
        check(new Settings().ExclusiveOutput&&System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"ExclusiveOutput\":false}")!.ExclusiveOutput==false,"設定: 専用出力の既定ONと保存済みOFFを保持");
    }
}
public unsafe class FakeTooltipBase
{
    private readonly StringArrayData* _stringArrayData;
    protected readonly NumberArrayData* NumberArrayData;
    protected FakeTooltipBase(StringArrayData* strings,NumberArrayData* numbers){_stringArrayData=strings;NumberArrayData=numbers;}
    public StringArrayData* Array=>_stringArrayData;
}
public sealed unsafe class FakeItemTooltip:FakeTooltipBase
{
    public FakeItemTooltip(StringArrayData* strings,NumberArrayData* numbers):base(strings,numbers){}
}
internal sealed class FakeStringHook(nint address):Dalamud.Hooking.IDalamudHook
{
    public nint Address=>address;
    public bool IsEnabled{get;set;}=true;
    public bool IsDisposed{get;set;}
    public string BackendName=>"test";
    public void Enable()=>IsEnabled=true;
    public void Disable()=>IsEnabled=false;
    public void Dispose()=>IsDisposed=true;
}
