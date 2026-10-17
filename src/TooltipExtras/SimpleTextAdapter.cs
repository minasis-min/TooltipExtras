using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using Dalamud.Hooking;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class SimpleTextAdapter : IDisposable
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private static readonly string[] Names=["FoodStats","MateriaStats","DesynthesisSkill","CollectableRewards","LogosTooltip","TrackFadedRolls","TrackGachaItems","TrackOutfits","GlamItemNames"];
    private static readonly int[][] Fields=[[16],[37,38,39,40,41,42,43,44],[13,35],[13],[13],[13],[13],[13]];
    private readonly IGameInteropProvider interop;
    private readonly Func<bool> allowed,frameworkThread;
    private readonly Func<int,bool> selected;
    private readonly Action<ulong,nint>? observeGenerated;
    private readonly Action<object,Func<bool>,Action<nint,nint>,nint,nint>? captureNames;
    private readonly Func<(ulong Id,nint Addon)> context;
    private readonly Dictionary<int,byte[]> parts=[];
    private readonly Dictionary<int,(int Rank,int SourceOrder)> positions=[];
    private Entry[] entries=[];
    private IList? list;
    private Hook<Generate>? hook;
    private Generate? forward;
    private volatile bool disposed;
    private bool busy,dirty,intercepting;
    private long nextAttempt;
    private ulong item;
    private nint addon;
    private byte[]? combined;
    private unsafe delegate void* Generate(AtkUnitBase* addon,NumberArrayData* numbers,StringArrayData* strings);
    private sealed record Entry(int Index,object Original,object Proxy,Func<bool> Enabled);
    public string Status {get;private set;}="待機中";
    public long Captures {get;private set;}
    public long Rejections {get;private set;}
    public long HookCalls {get;private set;}
    public long ProducerCalls {get;private set;}
    private readonly string[] featureStates=new string[Names.Length];
    public object[] FeatureStates()=>Names.Select((name,i)=>(object)new {Name=name,State=featureStates[i]??"未捕捉"}).ToArray();
    public SimpleTextAdapter(IGameInteropProvider interop,Func<bool> allowed,Func<bool> frameworkThread,Func<int,bool> selected,Func<(ulong,nint)> context,Action<ulong,nint>? observeGenerated=null,Action<object,Func<bool>,Action<nint,nint>,nint,nint>? captureNames=null)
    {this.interop=interop;this.allowed=allowed;this.frameworkThread=frameworkThread;this.selected=selected;this.context=context;this.observeGenerated=observeGenerated;this.captureNames=captureNames;}
    private static object? Field(object owner,string name)
    {
        for(var t=owner.GetType();t!=null;t=t.BaseType)if(t.GetField(name,Flags|BindingFlags.DeclaredOnly) is {} f)return f.GetValue(owner);
        throw new MissingFieldException(owner.GetType().FullName,name);
    }
    public void Refresh(IDalamudPluginInterface pi,bool enabled)
    {
        if(!enabled){if(hook!=null)Dispose();Clear();Status="無効";return;}
        if(hook!=null || Environment.TickCount64<nextAttempt)return;
        nextAttempt=Environment.TickCount64+1000;
        try
        {
            if(!pi.InstalledPlugins.Any(p=>p.InternalName=="SimpleTweaksPlugin"&&p.IsLoaded)){Status="未ロード";return;}
            var a=ForeignPlugin.SimpleAssembly(pi);
            var plugin=a.GetType("SimpleTweaksPlugin.SimpleTweaksPlugin",true)!.GetProperty("Plugin",BindingFlags.Static|BindingFlags.Public)!.GetValue(null)!;
            var manager=((IEnumerable)plugin.GetType().GetProperty("Tweaks")!.GetValue(plugin)!).Cast<object>().Single(t=>t.GetType().FullName=="SimpleTweaksPlugin.Tweaks.TooltipTweaks");
            var foreignHook=Field(manager,"generateItemTooltipHook");
            if(foreignHook==null || foreignHook.GetType().GetProperty("IsEnabled")!.GetValue(foreignHook) is not true){Status="元の生成フック準備待ち";return;}
            var address=(nint)foreignHook.GetType().GetProperty("Address")!.GetValue(foreignHook)!;
            if(address==0)throw new NotSupportedException("生成アドレスが無効です");
            var signature=foreignHook.GetType().GetGenericArguments()[0].GetMethod("Invoke")!;
            var parameters=signature.GetParameters();
            if(signature.ReturnType!=typeof(void*)||parameters.Length!=3||parameters[0].ParameterType.GetElementType()?.FullName!=typeof(AtkUnitBase).FullName||parameters[1].ParameterType.GetElementType()?.FullName!=typeof(NumberArrayData).FullName||parameters[2].ParameterType.GetElementType()?.FullName!=typeof(StringArrayData).FullName)throw new NotSupportedException("元の生成フック署名が未対応");
            var nativeAssembly=parameters[0].ParameterType.GetElementType()!.Assembly;
            var contracts=CompatibilityContract.Load();
            foreach(var group in new[]{"PriceNative","AllaganNative","SimpleTextNative"})
            {
                var abi=CompatibilityContract.Check(contracts,group,(assembly,type)=>assembly=="FFXIVClientStructs"?nativeAssembly.GetType(type):typeof(IDalamudPluginInterface).Assembly.GetType(type));
                if(!abi.Allowed)throw new NotSupportedException("元フックABI: "+abi.Reason);
            }
            list=(IList)Field(manager,"SubTweaks")!;
            var baseType=a.GetType("SimpleTweaksPlugin.Tweaks.TooltipTweaks+SubTweak",true)!;
            var type=SimpleDispatchProxy.Create(baseType);var built=new List<Entry>();
            foreach(var original in list.Cast<object>().ToArray())
            {
                var index=Array.IndexOf(Names,original.GetType().Name);if(index<0)continue;
                if(index==8)
                {
                    var nameAbi=new[]{"SimpleIdNative","NameColorNative"}.Select(group=>CompatibilityContract.Check(contracts,group,(assembly,name)=>assembly=="FFXIVClientStructs"?nativeAssembly.GetType(name):null)).FirstOrDefault(result=>!result.Allowed);
                    if(nameAbi!=null){featureStates[index]="元の名前ノードABI: "+nameAbi.Reason;continue;}
                }
                var invoke=TooltipProxy.Forward(original,baseType.GetMethod("OnGenerateItemTooltip")!);
                var enabledGetter=Expression.Lambda<Func<bool>>(Expression.Property(Expression.Constant(original),"Enabled")).Compile();
                                Action<nint,nint> capture;
                if(index==8)capture=(n,s)=>{ProducerCalls++;if(captureNames==null)invoke(n,s);else{featureStates[index]="専用名前ノードの中継へ転送";captureNames(original,enabledGetter,invoke,n,s);}};
                else capture=(n,s)=>Intercept(index,invoke,(NumberArrayData*)n,(StringArrayData*)s);
                var proxy=Activator.CreateInstance(type,[enabledGetter,capture])!;
                built.Add(new(index,original,proxy,enabledGetter));
            }
            entries=built.ToArray();
            if(entries.Length==0)throw new InvalidOperationException("対応追記が登録されていません");
            disposed=false;hook=interop.HookFromAddress<Generate>(address,Detour);forward=hook.Original;hook.Enable();Status="生成中継に接続 / 捕捉待ち";
        }
        catch(Exception ex){Dispose();Status="生成中継停止: "+ex.GetBaseException().Message;nextAttempt=long.MaxValue;}
    }
    private void* Detour(AtkUnitBase* native,NumberArrayData* numbers,StringArrayData* strings)
    {
        var next=forward ?? throw new InvalidOperationException("元の生成処理がありません");
        var sourceList=list;var producers=entries;
        HookCalls++;
        if(disposed || busy || !allowed() || !frameworkThread())return next(native,numbers,strings);
        var key=context();
        if(key.Id==0 || key.Addon!=(nint)native)return next(native,numbers,strings);
        if(item!=key.Id || addon!=key.Addon){Clear();item=key.Id;addon=key.Addon;}
        busy=true;
        var scope=new ScopedRegistration();
        try
        {
            try { foreach(var entry in producers)if(selected(entry.Index))scope.Replace(sourceList!,entry.Original,entry.Proxy);else Remove(entry.Index); }
            catch(Exception ex){disposed=true;Status="中継停止: "+ex.GetBaseException().Message;}
            var result=next(native,numbers,strings);
            if(context()==key)try{observeGenerated?.Invoke(key.Id,(nint)native);}catch(Exception ex){Status="基準配置の記録失敗: "+ex.GetBaseException().Message;}
            return result;
        }
        finally{try{scope.Dispose();}catch(Exception ex){disposed=true;Status="代理の復元失敗: "+ex.GetBaseException().Message;}busy=false;}
    }
    private static byte[]? Read(StringArrayData* a,int index)
    {
        byte* p=a->StringArray[index];if(p==null)return null;var length=0;while(length<32768&&p[length]!=0)length++;
        if(length==32768)throw new NotSupportedException("追記文字列の長さ上限");return new ReadOnlySpan<byte>(p,length).ToArray();
    }
    private static void Write(StringArrayData* a,int index,byte[]? bytes)
    {if(bytes==null){a->SetValue(index,(byte*)null,false,false,false);return;}var z=new byte[bytes.Length+1];bytes.CopyTo(z,0);a->SetValue(index,z,false,true,false);}
    private void Remove(int index){if(parts.Remove(index))dirty=true;positions.Remove(index);}
    private void Intercept(int index,Action<nint,nint> original,NumberArrayData* numbers,StringArrayData* strings)
    {
        if(disposed||intercepting)
        {
            try{original((nint)numbers,(nint)strings);}catch(Exception ex){Status="元追記処理の例外: "+ex.GetBaseException().Message;}
            return;
        }
        intercepting=true;
        try{Capture(index,original,numbers,strings);}catch(Exception ex){Remove(index);disposed=true;Status="元表示を維持して中継停止: "+ex.GetBaseException().Message;}finally{intercepting=false;}
    }
    private void Capture(int index,Action<nint,nint> original,NumberArrayData* numbers,StringArrayData* strings)
    {
        if(disposed){original((nint)numbers,(nint)strings);return;}
        ProducerCalls++;
        var fields=Fields[index];byte[]?[] before;
        int oldCount=0;
        try
        {
            if(strings==null || strings->StringArray==null || strings->AtkArrayData.Size<=fields.Max() || strings->AtkArrayData.Size>256)throw new NotSupportedException("文字列配列の形状不一致");
            if(index==1 && (numbers==null||numbers->IntArray==null||numbers->AtkArrayData.Size<=21))throw new NotSupportedException("能力値配列の形状不一致");
            before=fields.Select(f=>Read(strings,f)).ToArray();if(index==1){oldCount=numbers->IntArray[21];}
        }
        catch(Exception ex){Remove(index);Status="元表示を維持: "+ex.GetBaseException().Message;original((nint)numbers,(nint)strings);return;}
        try{original((nint)numbers,(nint)strings);}
        catch(Exception ex){Remove(index);Status="元追記処理の例外: "+ex.GetBaseException().Message;disposed=true;return;}
        byte[]?[] after;
        try{after=fields.Select(f=>Read(strings,f)).ToArray();}catch(Exception ex){Remove(index);Status="元表示を維持: "+ex.GetBaseException().Message;return;}
        var newCount=index==1?numbers->IntArray[21]:0;
        featureStates[index]=$"行数 {oldCount} → {newCount}";
        if(index==1&&!SourceStatCount.CanRestore(oldCount,newCount)){Remove(index);Rejections++;Status=featureStates[index]="元表示を維持: 能力値行数の変更が未対応";return;}
        var plan=TextTransferPlan.Create(fields,before,after,index>=3,out var reason,index==1&&oldCount>=0&&oldCount<=8?37+oldCount:int.MaxValue);
        if(plan==null){Remove(index);if(reason.Length>0){Rejections++;Status=featureStates[index]="元表示を維持: "+reason;}return;}
        if(disposed||!allowed()||context()!=(item,addon)){Remove(index);return;}
                var generated=plan.Output(index>=3).ToArray();
        if(index==1)
        {
            var formatted=generated.Select(MateriaPresentation.Extract).ToArray();
            if(formatted.Any(row=>row==null)){Remove(index);Rejections++;Status=featureStates[index]="元表示を維持: 能力値の生成形式が未対応";return;}
            generated=formatted.Select(row=>row!).ToArray();
        }
        var output=generated.Select(CapturedText.TrimPresentationEdges);
        var labels=new[]{"食事・薬品","マテリア能力値","分解スキル","収集品報酬","ロゴスアクション","譜面の製作先・取得状況","ランダム報酬アイテム","衣装セットの取得状況"};
        var body=CapturedText.Rows(new[]{System.Text.Encoding.UTF8.GetBytes(labels[index])}.Concat(output));
        Action<int>? count=index==1?value=>numbers->IntArray[21]=value:null;
        if(!plan.Apply((field,value)=>Write(strings,field,value),field=>Read(strings,field),count,oldCount,newCount,out var failure))
        {Remove(index);Rejections++;Status="元表示へ戻して中継停止: "+failure;disposed=true;return;}
        var position=(Rank:plan.ChangedFields.Min(FieldOrder),SourceOrder:Array.FindIndex(entries,entry=>entry.Index==index));
        if(!positions.TryGetValue(index,out var oldPosition)||oldPosition!=position){positions[index]=position;dirty=true;}
        if(!parts.TryGetValue(index,out var old)||!old.AsSpan().SequenceEqual(body)){parts[index]=body;dirty=true;}
        Captures++;featureStates[index]=$"中継済み / 行数 {oldCount} → {newCount}";Status="食事・能力値等の元処理を中継済み";
    }
    // Follow the native visual groups, not numeric array indices: effects are
    // above description, bonuses follow description, repair/desynthesis is lower.
    private static int FieldOrder(int field)=>field switch {16=>0,13=>1,>=37 and <=44=>2,35=>3,_=>4};
    public byte[]? Get(ulong id,nint address)
    {
        if(!allowed()||id!=item||address!=addon){Clear();return null;}
        foreach(var entry in entries)if(!selected(entry.Index)||!entry.Enabled())Remove(entry.Index);
        if(dirty){combined=parts.Count==0?null:CapturedText.Rows(parts.OrderBy(p=>positions[p.Key].Rank).ThenBy(p=>positions[p.Key].SourceOrder).Select(p=>p.Value));dirty=false;}return combined;
    }
    public void Clear(){parts.Clear();positions.Clear();combined=null;dirty=false;item=0;addon=0;}
    public void Dispose(){disposed=true;hook?.Dispose();hook=null;entries=[];list=null;Clear();}
    public void Reconnect(){Dispose();nextAttempt=0;}
}
