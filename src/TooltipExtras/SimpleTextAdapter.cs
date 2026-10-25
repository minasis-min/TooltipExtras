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
    private readonly IGameInteropProvider interop;
    private readonly Func<bool> allowed,frameworkThread;
    private readonly Func<int,bool> selected;
    private readonly Action<ulong,nint>? observeGenerated;
    private readonly Action<object,Func<bool>,Action<nint,nint>,nint,nint>? captureNames;
    private readonly Func<(ulong Id,nint Addon)> context;
    private readonly Dictionary<int,byte[]> parts=[];
    private readonly Dictionary<int,(int Rank,int SourceOrder)> positions=[];
    private Entry[] entries=[];
    private Func<object,Entry?>? buildEntry;
    private object[] registered=[];
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
    public Func<bool> Exclusive {get;set;}=()=>false;
    public NativeStringSink? Sink {get;set;}
    public RejectedTextLog? Rejected {get;set;}
    public Func<object,Func<bool>,nint,nint,bool>? CaptureNamesExclusive {get;set;}
    private Func<bool> sinkMatches=()=>true;
    public Func<int,bool> Reviewed {get;set;}=_=>true;
    public long WithheldWrites {get;private set;}
    public long OutputFailures {get;private set;}
    public bool ConnectionFailed {get;private set;}
    public string LastRegistrationFailure {get;private set;}="";
    private readonly string[] featureStates=new string[SimpleTextFeatures.All.Count];
    public object[] FeatureStates()=>SimpleTextFeatures.All.Select((feature,i)=>(object)new {Name=feature.Name,State=featureStates[i]??"未捕捉"}).ToArray();
    public SimpleTextAdapter(IGameInteropProvider interop,Func<bool> allowed,Func<bool> frameworkThread,Func<int,bool> selected,Func<(ulong,nint)> context,Action<ulong,nint>? observeGenerated=null,Action<object,Func<bool>,Action<nint,nint>,nint,nint>? captureNames=null)
    {this.interop=interop;this.allowed=allowed;this.frameworkThread=frameworkThread;this.selected=selected;this.context=context;this.observeGenerated=observeGenerated;this.captureNames=captureNames;}
    private static object? Field(object owner,string name)
    {
        for(var t=owner.GetType();t!=null;t=t.BaseType)if(t.GetField(name,Flags|BindingFlags.DeclaredOnly) is {} f)return f.GetValue(owner);
        throw new MissingFieldException(owner.GetType().FullName,name);
    }
    public void Refresh(IDalamudPluginInterface pi,bool enabled)
    {
        if(!enabled){if(hook!=null)Dispose();Clear();ConnectionFailed=false;LastRegistrationFailure="";Status="無効";return;}
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
            if(Exclusive()){sinkMatches=Sink!.VerifyNativeSetter(nativeAssembly);try{Sink.Connect();}catch{OutputFailures++;}}
            list=(IList)Field(manager,"SubTweaks")!;
            var baseType=a.GetType("SimpleTweaksPlugin.Tweaks.TooltipTweaks+SubTweak",true)!;
            var type=SimpleDispatchProxy.Create(baseType);var built=new List<Entry>();
            Entry? BuildEntry(object original)
            {
                var index=SimpleTextFeatures.IndexOf(original.GetType().Name);if(index<0&&!Exclusive())return null;
                if(index<0&&ProducerMethods.IsEmptyInherited(original.GetType().GetMethod("OnGenerateItemTooltip")!,baseType))return null;
                if(index==9&&!Exclusive())return null; // Legacy ID relay continues to own its native path.
                if(index==8&&!Exclusive())
                {
                    var nameAbi=new[]{"SimpleIdNative","NameColorNative"}.Select(group=>CompatibilityContract.Check(contracts,group,(assembly,name)=>assembly=="FFXIVClientStructs"?nativeAssembly.GetType(name):null)).FirstOrDefault(result=>!result.Allowed);
                    if(nameAbi!=null){featureStates[index]="元の名前ノードABI: "+nameAbi.Reason;return null;}
                }
                var invoke=TooltipProxy.Forward(original,baseType.GetMethod("OnGenerateItemTooltip")!);
                var enabledGetter=Expression.Lambda<Func<bool>>(Expression.Property(Expression.Constant(original),"Enabled")).Compile();
                Action<nint,nint> capture;
                if(index<0)capture=(n,s)=>{OutputFailures++;Status="Simple Tweaksの未対応の追記を保留";Rejected?.Record("Simple Tweaks / "+original.GetType().Name,null,Status);};
                else if(index==8)capture=(n,s)=>
                {
                    ProducerCalls++;
                    if(Exclusive())
                    {
                        if(!selected(index))return;
                        if(CaptureNamesExclusive?.Invoke(original,enabledGetter,n,s)==true)featureStates[index]="名前データからTEへ直接転送";
                        else{OutputFailures++;featureStates[index]="名前データの取得を保留";Rejected?.Record(SimpleTextFeatures.All[index].Source,null,featureStates[index]);}
                        return;
                    }
                    if(captureNames==null)invoke(n,s);else{featureStates[index]="専用名前ノードの中継へ転送";captureNames(original,enabledGetter,invoke,n,s);}
                };
                else capture=(n,s)=>Intercept(index,invoke,(NumberArrayData*)n,(StringArrayData*)s);
                var proxy=Activator.CreateInstance(type,[enabledGetter,capture])!;
                return new Entry(index,original,proxy,enabledGetter);
            }
            buildEntry=original=>
            {
                try{return BuildEntry(original);}
                catch(Exception ex) when(Exclusive())
                {
                    RegistrationFailed("追記登録の取得失敗",ex);
                    // Use the already verified base proxy contract to withhold
                    // this producer; do not call an unverified original method.
                    Func<bool> enabled=()=>true;
                    Action<nint,nint> withhold=(n,s)=>{OutputFailures++;Rejected?.Record("Simple Tweaks / "+original.GetType().Name,null,"追記登録を確認できないため保留");};
                    return new Entry(-1,original,Activator.CreateInstance(type,[enabled,withhold])!,enabled);
                }
            };
            foreach(var original in list.Cast<object>().ToArray())if(buildEntry(original) is {} entry)built.Add(entry);
            entries=built.ToArray();registered=list.Cast<object>().ToArray();
            if(entries.Length==0)throw new InvalidOperationException("対応追記が登録されていません");
            disposed=false;hook=interop.HookFromAddress<Generate>(address,Detour);forward=hook.Original;hook.Enable();Status="生成中継に接続 / 捕捉待ち";
        }
        catch(Exception ex){ConnectionFailed=true;Dispose();Status="生成中継停止: "+ex.GetBaseException().Message;nextAttempt=long.MaxValue;}
    }
    private void* Detour(AtkUnitBase* native,NumberArrayData* numbers,StringArrayData* strings)
    {
        var next=forward ?? throw new InvalidOperationException("元の生成処理がありません");
        var sourceList=list;
        HookCalls++;
        if(disposed || busy || !allowed() || !frameworkThread())return next(native,numbers,strings);
        var key=context();
        if(key.Id==0 || key.Addon!=(nint)native)return next(native,numbers,strings);
        if(item!=key.Id || addon!=key.Addon){Clear();item=key.Id;addon=key.Addon;}
        busy=true;
        var scope=new ScopedRegistration();
        try
        {
            try
            {
                SynchronizeEntries(sourceList!);
                foreach(var entry in entries)
                {
                    if(!Exclusive()&&!selected(entry.Index)){Remove(entry.Index);continue;}
                    // A source may remove a sub-tweak between generations. Never
                    // resurrect it, or disable every remaining producer for that.
                    if(sourceList!.Contains(entry.Original))scope.Replace(sourceList,entry.Original,entry.Proxy);
                }
            }
            catch(Exception ex){RegistrationFailed("代理の登録失敗",ex);}
            var result=next(native,numbers,strings);
            if(context()==key)try{observeGenerated?.Invoke(key.Id,(nint)native);}catch(Exception ex){Status="基準配置の記録失敗: "+ex.GetBaseException().Message;}
            return result;
        }
        finally{try{scope.Dispose();}catch(Exception ex){RegistrationFailed("代理の復元失敗",ex);}busy=false;}
    }
    private void RegistrationFailed(string stage,Exception ex)
    {
        ConnectionFailed=true;OutputFailures++;Clear();
        Status=LastRegistrationFailure=stage+": "+ex.GetBaseException().Message;
        Rejected?.Record("Simple Tweaks",null,Status);
        // Keep the managed boundary installed: a transient registration failure
        // must not turn subsequent exclusive generations into native passthrough.
    }
    private void SynchronizeEntries(IList source)
    {
        if(buildEntry==null)return;
        // Compare identity/order without allocating on an unchanged registration.
        var unchanged=source.Count==registered.Length;
        for(var i=0;unchanged&&i<registered.Length;i++)unchanged=ReferenceEquals(source[i],registered[i]);
        if(unchanged)return;
        var updated=new List<Entry>();
        foreach(var value in source)
        {
            var known=Array.Find(entries,e=>ReferenceEquals(e.Original,value)||ReferenceEquals(e.Proxy,value));
            if(known!=null)updated.Add(known);
            else if(buildEntry(value!) is {} entry)updated.Add(entry);
        }
        foreach(var entry in entries)if(!updated.Contains(entry))Remove(entry.Index);
        entries=updated.ToArray();registered=source.Cast<object>().ToArray();
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
        if(Exclusive()&&!selected(index)){Remove(index);if(!Reviewed(index)){OutputFailures++;featureStates[index]="互換性未確認のため追記を保留";Rejected?.Record(SimpleTextFeatures.All[index].Source,null,featureStates[index]);}return;}
        if(Exclusive()&&!sinkMatches()){Remove(index);OutputFailures++;Status="文字列出力先の変更により追記を保留";return;}
        if(disposed||intercepting)
        {
            if(Exclusive()){OutputFailures++;return;}
            try{original((nint)numbers,(nint)strings);}catch(Exception ex){Status="元追記処理の例外: "+ex.GetBaseException().Message;}
            return;
        }
        intercepting=true;
        try{Capture(index,original,numbers,strings);}catch(Exception ex){Remove(index);OutputFailures++;Status="追記の捕捉失敗: "+ex.GetBaseException().Message;if(!Exclusive())disposed=true;Rejected?.Record(SimpleTextFeatures.All[index].Source,null,Status);}finally{intercepting=false;}
    }
    private void Capture(int index,Action<nint,nint> original,NumberArrayData* numbers,StringArrayData* strings)
    {
        if(disposed){if(!Exclusive())original((nint)numbers,(nint)strings);return;}
        ProducerCalls++;
        var fields=SimpleTextFeatures.All[index].Fields;byte[]?[] before;
        int oldCount=0;
        try
        {
            if(strings==null || strings->StringArray==null || strings->AtkArrayData.Size<=fields.Max() || strings->AtkArrayData.Size>256)throw new NotSupportedException("文字列配列の形状不一致");
            if(index==1 && (numbers==null||numbers->IntArray==null||numbers->AtkArrayData.Size<=21))throw new NotSupportedException("能力値配列の形状不一致");
            before=Exclusive()?[]:fields.Select(f=>Read(strings,f)).ToArray();if(index==1){oldCount=numbers->IntArray[21];}
        }
        catch(Exception ex){Remove(index);Status="追記を保留: "+ex.GetBaseException().Message;if(Exclusive())OutputFailures++;else original((nint)numbers,(nint)strings);return;}
        NativeStringSink.Frame? capture=null;var newCount=0;
        try
        {
            if(Exclusive())
            {
                capture=Sink!.Capture(strings,isolated=>
                {
                    if(index==1)
                    {
                        if(numbers->AtkArrayData.Size>256)throw new NotSupportedException("能力値配列の上限");
                        var values=new ReadOnlySpan<int>(numbers->IntArray,numbers->AtkArrayData.Size).ToArray();
                        var copy=*numbers;
                        fixed(int* data=values){copy.IntArray=data;original((nint)(&copy),isolated);newCount=data[21];}
                    }
                    else original((nint)numbers,isolated);
                },SimpleTextFeatures.All[index].Source);
            }
            else {original((nint)numbers,(nint)strings);newCount=index==1?numbers->IntArray[21]:0;}
        }
        catch(Exception ex){Remove(index);OutputFailures++;Status="元追記処理の例外: "+ex.GetBaseException().GetType().Name;Rejected?.Record(SimpleTextFeatures.All[index].Source,null,Status);if(!Exclusive())disposed=true;return;}
        finally{capture?.Dispose();}
        if(capture!=null)
        {
            WithheldWrites+=capture.Writes.Count;
            if(capture.Failure.Length!=0||capture.Writes.Keys.Any(field=>!fields.Contains(field)))
            {Remove(index);OutputFailures++;Status=featureStates[index]="追記を保留: 取得できない出力";Rejected?.Writes(SimpleTextFeatures.All[index].Source,Status,capture.Writes);return;}
        }
        if(capture!=null)before=fields.Select(f=>capture.Before.TryGetValue(f,out var value)?value:Read(strings,f)).ToArray();
        byte[]?[] after;
        try{after=capture==null?fields.Select(f=>Read(strings,f)).ToArray():fields.Select((f,i)=>capture.Writes.TryGetValue(f,out var value)?value:before[i]).ToArray();}catch(Exception ex){Remove(index);Status="追記を保留: "+ex.GetBaseException().Message;return;}
        featureStates[index]=$"行数 {oldCount} → {newCount}";
        if(index==1&&!SourceStatCount.CanRestore(oldCount,newCount)){Remove(index);Rejections++;if(Exclusive())OutputFailures++;Status=featureStates[index]="追記を保留: 能力値行数の変更が未対応";Rejected?.Changed(SimpleTextFeatures.All[index].Source,Status,fields,before,after);return;}
        var plan=TextTransferPlan.Create(fields,before,after,index>=3,out var reason,index==1&&oldCount>=0&&oldCount<=8?37+oldCount:int.MaxValue);
        if(plan==null){Remove(index);if(reason.Length>0){Rejections++;if(Exclusive())OutputFailures++;Status=featureStates[index]="追記を保留: "+reason;Rejected?.Changed(SimpleTextFeatures.All[index].Source,Status,fields,before,after);}return;}
        if(disposed||!allowed()||context()!=(item,addon)){Remove(index);return;}
        var generated=plan.Output(index>=3).ToArray();
        if(index==2&&plan.ChangedFields.Contains(13))
        {
            var slot=Array.IndexOf(fields,13);
            var row=DesynthesisPresentation.Extract(before[slot]!,after[slot]!);
            if(row==null)
            {
                Remove(index);Rejections++;if(Exclusive())OutputFailures++;
                Status=featureStates[index]="追記を保留: 分解スキルの変更行を確認できません";
                Rejected?.Changed(SimpleTextFeatures.All[index].Source,Status,fields,before,after);return;
            }
            generated=plan.ChangedFields.Select((field,i)=>field==13?row:generated[i]).ToArray();
        }
        if(index==1)
        {
            var formatted=generated.Select(MateriaPresentation.Extract).ToArray();
            if(formatted.Any(row=>row==null)){Remove(index);Rejections++;if(Exclusive())OutputFailures++;Status=featureStates[index]="追記を保留: 能力値の生成形式が未対応";Rejected?.Changed(SimpleTextFeatures.All[index].Source,Status,fields,before,after);return;}
            generated=formatted.Select(row=>row!).ToArray();
        }
        var output=generated.Select(CapturedText.TrimPresentationEdges);
        var body=CapturedText.Rows(new[]{System.Text.Encoding.UTF8.GetBytes(SimpleTextFeatures.All[index].Label)}.Concat(output));
        Action<int>? count=index==1?value=>numbers->IntArray[21]=value:null;
        if(capture==null&&!plan.Apply((field,value)=>Write(strings,field,value),field=>Read(strings,field),count,oldCount,newCount,out var failure))
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
    public void Dispose(){disposed=true;hook?.Dispose();hook=null;entries=[];registered=[];buildEntry=null;list=null;Clear();}
    public void Reconnect(){Dispose();ConnectionFailed=false;LastRegistrationFailure="";OutputFailures=0;nextAttempt=0;}
}
