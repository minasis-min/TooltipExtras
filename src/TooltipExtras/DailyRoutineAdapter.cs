using System.Collections;
using System.Reflection;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Text.ReadOnly;
namespace TooltipExtras;

internal sealed unsafe class DailyRoutineAdapter : IDisposable
{
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    private readonly IAddonLifecycle lifecycle;
    private readonly IFramework framework;
    private readonly Func<bool> allowed;
    private readonly Func<(ulong Id,nint Addon)> context;
    private readonly bool action;
    private readonly Func<int> detailKind;
    private int capturedDetailKind;
    private string AddonName=>action?"ActionDetail":"ItemDetail";
    private string CallbackName=>action?"OnActionDetailUpdate":"OnItemDetailUpdate";
    private readonly CompatibilityEntry[] contracts=CompatibilityContract.Load();
    private ListenerRelay? relay;
    private object? manager;
    private object? hostService;
    private FieldInfo? methods, originals, lastItem;
    private Type? callbackType;
    private Assembly? producerAssembly;
    private MethodInfo? refreshMethod;
    public string RefreshStatus {get;private set;}="未確認";
    private readonly Dictionary<string,string> featureStates=[];
    public string FeatureStatus {get;private set;}="未確認";
    private Func<string,string>? sourceLanguage;
    private Func<string>? sourceColon;
    private string? nonGearMacro;
    private DailyRoutineText.NonGearPattern? nonGearPattern;
    private DailyRoutineCallbacks? callbacks;
    private byte[]? sourceDescription;
    private IReadOnlyDictionary<int,byte[]>? sourceBaselines;
    public Func<bool> Exclusive {get;set;}=()=>false;
    public RejectedTextLog? Rejected {get;set;}
    public long WithheldOperations {get;private set;}
    public long OutputFailures {get;private set;}
    public bool ConnectionFailed=>failed&&relay==null;
    private object? capturedRegistrations;
    private IAddonLifecycle.AddonEventDelegate? original;
    private bool stopped=true,busy,failed,hostChecked;
    private long nextAttempt;
    private ulong item;
    private nint addon;
    private byte[]? content;
    private readonly Dictionary<int,string> blockedFields=[];
    private readonly RequestedTransfer requested=new();
    private (ulong Item,int Kind) blockedTarget;
    public string Status {get;private set;}="待機中";
    public long Calls {get;private set;}
    public long Captures {get;private set;}
    public long Rejections {get;private set;}
    public long EmptyShopFixes {get;private set;}
    public long RebuiltDescriptions {get;private set;}
    public long DeferredPresentations {get;private set;}
    public long SourceFirstCaptures {get;private set;}
    public string FieldStatus {get;private set;}="未捕捉";
    public DailyRoutineLayout.Geometry Layout {get;private set;}
    public DailyRoutineLayout.LiveGeometry? LastDraw {get;private set;}
    private long observedCapture;
    public void ObserveLayout()
    {
        if(observedCapture==Captures)return;
        var shape=InspectLayout(false);
        if(shape!=null){LastDraw=shape;observedCapture=Captures;}
    }
    public DailyRoutineLayout.LiveGeometry? InspectLayout(bool measure=true)
    {
        if(action||stopped||!allowed()||relay?.Active is not true||!framework.IsInFrameworkUpdateThread)return null;
        var key=context();
        return item!=0&&item==key.Id&&addon==key.Addon?DailyRoutineLayout.Inspect(key.Addon,measure):null;
    }
    public DailyRoutineAdapter(IAddonLifecycle lifecycle,IFramework framework,Func<bool> allowed,Func<(ulong,nint)> context)
        :this(lifecycle,framework,allowed,context,TooltipKind.Item,()=>0){}
    public DailyRoutineAdapter(IAddonLifecycle lifecycle,IFramework framework,Func<bool> allowed,Func<(ulong,nint)> context,TooltipKind kind,Func<int> detailKind)
    {this.lifecycle=lifecycle;this.framework=framework;this.allowed=allowed;this.context=context;action=kind==TooltipKind.Action;this.detailKind=detailKind;}
    private static object Field(object owner,string name)=>owner.GetType().GetField(name,Flags)!.GetValue(owner)!;
    public void Refresh(IDalamudPluginInterface pi,bool enabled)
    {
        if(!enabled){if(relay!=null)Dispose();Clear();Status="無効";return;}
        if(relay!=null){if(!relay.Active){Dispose();Status="元の登録解除を検出";}else return;}
        if(failed||Environment.TickCount64<nextAttempt||!framework.IsInFrameworkUpdateThread)return;
        nextAttempt=Environment.TickCount64+1000;
        try
        {
            if(!pi.InstalledPlugins.Any(p=>p.InternalName=="DailyRoutines"&&p.IsLoaded)){Status="未ロード";return;}
            Assembly? omen=null,modules=null,core=null;
            Type? Resolve(string assembly,string name)=>assembly switch {
                "Dalamud"=>typeof(IDalamudPluginInterface).Assembly.GetType(name),
                "FFXIVClientStructs"=>typeof(AtkUnitBase).Assembly.GetType(name),
                "Lumina"=>typeof(ReadOnlySeString).Assembly.GetType(name),
                "OmenTools"=>omen?.GetType(name),"DailyRoutines.ModulesPublic"=>modules?.GetType(name),"DailyRoutines"=>core?.GetType(name),_=>null};
            if(!hostChecked)
            {
                foreach(var group in new[]{"AllaganNative","DailyRelayHost","DailyRelayNative"})
                {var result=CompatibilityContract.Check(contracts,group,Resolve);if(!result.Allowed)throw new NotSupportedException(result.Reason);}
                if(!action){var result=CompatibilityContract.Check(contracts,"DailyLayoutNative",Resolve);if(!result.Allowed)throw new NotSupportedException(result.Reason);}
                hostChecked=true;
            }
            var service=Field(lifecycle,"addonLifecycleService");
            if(Field(service,"isInvokingListeners") is true){Status="イベント終了待ち";return;}
            var events=(IDictionary)service.GetType().GetProperty("EventListeners",Flags)!.GetValue(service)!;
            if(events[AddonEvent.PreRequestedUpdate] is not IDictionary names||names[AddonName] is not IEnumerable set)
            {Status="元のイベント登録待ち";return;}
            var candidates=set.Cast<object>().Where(entry=>entry.GetType().GetProperty("IsRequestedToClear",Flags)!.GetValue(entry) is false&&entry.GetType().GetProperty("FunctionDelegate")!.GetValue(entry) is Delegate d&&d.Target?.GetType().FullName=="OmenTools.OmenService.TooltipManager"&&d.Target.GetType().Assembly.GetName().Name=="OmenTools"&&d.Method.Name==CallbackName&&HasReviewedProducer(d.Target)).ToArray();
            if(candidates.Length!=1){Status="元のイベント登録が一意になるまで待機";return;}
            var source=candidates[0];
            original=(IAddonLifecycle.AddonEventDelegate)source.GetType().GetProperty("FunctionDelegate")!.GetValue(source)!;
            manager=original.Target!;
            callbacks=null;nonGearMacro=null;nonGearPattern=null;
            omen=manager.GetType().Assembly;
            methods=manager.GetType().GetField("methodsCollection",Flags)!;
            originals=manager.GetType().GetField(action?"actionOriginalTexts":"itemOriginalTexts",Flags)!;
            if(originals.FieldType!=typeof(ReadOnlySeString[]))throw new NotSupportedException("元本文の文字列型が未対応です");
            lastItem=manager.GetType().GetField(action?"lastActionInfo":"lastItemInfo",Flags)!;
            callbackType=omen.GetType("OmenTools.OmenService.TooltipManager+"+(action?"Action":"Item")+"TooltipUpdateDelegate",true)!;
            var producers=Callbacks().Where(d=>DailyRoutineText.Producers.Contains(d.Method.DeclaringType?.FullName)).Select(d=>d.Method.DeclaringType!.Assembly).Distinct().ToArray();
            if(producers.Length!=1){Status="対象機能の登録待ち";return;}
            modules=producers[0];
            var provider=CompatibilityContract.Check(contracts,action?"DailyActionProvider":"DailyItemProvider",Resolve);
            if(!provider.Allowed)throw new NotSupportedException(provider.Reason);
            foreach(var group in new[]{"DailyObservationProvider",action?"DailyActionObservationProvider":"DailyItemObservationProvider"})
            {var check=CompatibilityContract.Check(contracts,group,Resolve);if(!check.Allowed)throw new NotSupportedException(check.Reason);}
            // Refresh after detach is optional. A changed refresh implementation
            // must not disable the independently checked text interception path.
            var refresh=CompatibilityContract.Check(contracts,action?"DailyActionRefreshProvider":"DailyItemRefreshProvider",Resolve);
            refreshMethod=refresh.Allowed?manager.GetType().GetMethod(action?"TriggerActionDetailUpdate":"TriggerItemDetailUpdate"):null;
            RefreshStatus=refreshMethod!=null?"確認済みの再更新通知を使用":"通常のアイテム切替で再更新 / "+refresh.Reason;
            producerAssembly=modules;
            sourceLanguage=null;sourceColon=null;featureStates.Clear();FeatureStatus="対象機能の登録待ち";
            var salvage=action?null:modules.GetType(DailyRoutineText.Producers[2])?.GetMethod("OnItemTooltip",Flags|BindingFlags.Static);
            if(salvage!=null)
            {
                try
                {
                    // A changed localized formatter can only withhold its own
                    // extraction. It must not disable NPC/market interception.
                    var get=MethodCompatibility.Calls(salvage).OfType<MethodInfo>().FirstOrDefault(m=>m.DeclaringType?.FullName=="DailyRoutines.Manager.LanguageManager"&&m.Name=="Get"&&m.GetParameters().Length==1);
                    core=get?.DeclaringType?.Assembly;
                    var check=CompatibilityContract.Check(contracts,"DailySalvageProvider",Resolve);
                    if(!check.Allowed||get==null)throw new NotSupportedException(check.Reason);
                    sourceLanguage=get.CreateDelegate<Func<string,string>>();
                    sourceColon=omen.GetType("OmenTools.Info.Game.Data.UITexts")!.GetProperty("Colon")!.GetMethod!.CreateDelegate<Func<string>>();
                }
                catch(Exception ex){sourceLanguage=null;sourceColon=null;featureStates["分解情報の文言"]="抽出を保留 / "+ex.GetBaseException().Message;}
            }
            var dictionary=(IDictionary)methods.GetValue(manager)!;
            callbacks=new(dictionary,callbackType,producerAssembly,action,callback=>
            {
                var group=DailyFormatterCompatibility.Group(callback.Method,action);
                var activeGroups=Callbacks().Select(d=>DailyFormatterCompatibility.Group(d.Method,action)).ToHashSet();
                foreach(var stale in featureStates.Keys.Where(name=>name!="分解情報の文言"&&!activeGroups.Contains(name)).ToArray())featureStates.Remove(stale);
                var check=DailyFormatterCompatibility.Check(contracts,callback.Method,action,Resolve);
                var executable=check.Allowed&&Array.IndexOf(DailyRoutineText.Producers,callback.Method.DeclaringType?.FullName) is >=0 and <=3;
                featureStates[group]=executable?"署名と出力命令を確認して分離":check.Allowed?"文字以外の処理は保留":check.Reason;
                FeatureStatus=string.Join(" / ",featureStates.Select(pair=>pair.Key+": "+pair.Value));
                if(Exclusive())return new DailyRoutineObservation(dictionary,callbackType,callback,null,Exclusive,RouteExclusive,action,executable)
                    {Rejected=(field,reason,text)=>Rejected?.Record("Daily Routines / "+callback.Method.DeclaringType?.Name,field,reason,text)};
                if(!check.Allowed)return null;
                if(action)return null;
                return Array.IndexOf(DailyRoutineText.Producers,callback.Method.DeclaringType?.FullName) switch {
                    0 or 1=>new(dictionary,callbackType,callback,(operation,text)=>operation==2?CapturedText.TrimPresentationEdges(text):null),
                    2 when sourceLanguage!=null=>new(dictionary,callbackType,callback,SeparateNonGear),_=>null};
            },Exclusive);
            var constructor=source.GetType().GetConstructors(Flags).Single();
            var proxy=constructor.Invoke([AddonEvent.PreRequestedUpdate,AddonName,new IAddonLifecycle.AddonEventDelegate(Invoke)]);
            stopped=false;
            relay=new(set,source,proxy);
            blockedFields.Clear();
            hostService=service;
            Status="接続済み / 捕捉待ち";
        }
        catch(Exception ex){stopped=true;failed=true;Clear();Status="DR分離停止: "+ex.GetBaseException().Message;}
    }
    private DailyRoutineText.NonGearPattern? NonGearPattern()
    {
        var macro=sourceLanguage!("OptimizedSalvage-MacroString-NonGearDescription");
        if(macro!=nonGearMacro){nonGearMacro=macro;nonGearPattern=DailyRoutineText.NonGearPattern.Parse(macro);}
        return nonGearPattern;
    }
    private byte[]? SeparateNonGear(int operation,byte[] generated)
    {
        if(operation!=0||sourceLanguage==null||sourceDescription==null)return null;
        return NonGearPattern() is {} pattern&&DailyRoutineText.SplitNonGear(generated,pattern,out var native,out var extra)
            &&DailyRoutineText.SameFlowContent(native,sourceDescription)?extra:null;
    }
    private byte[]? RouteExclusive(int field,int operation,byte[] text)
    {
        if(text.Length==0)return null; // Includes the NPC module's empty shop row.
        byte[]? output=null;
        if(operation==2)output=CapturedText.TrimPresentationEdges(text);
        else if(operation==0)
        {
            if(field==13&&sourceLanguage!=null&&NonGearPattern() is {} pattern&&DailyRoutineText.SplitNonGear(text,pattern,out _,out var extra))output=extra;
            if(output==null&&sourceBaselines?.GetValueOrDefault(field) is {} baseline)output=DailyRoutineText.Extract(field,baseline,text);
            if(output==null&&field==35&&sourceLanguage!=null&&sourceColon!=null)
                output=DailyRoutineText.ExtractSalvage(sourceBaselines?.GetValueOrDefault(field)??[],text,sourceLanguage("OptimizedSalvage-Level-Current")+sourceColon());
        }
        if(output is not {Length:>0})OutputFailures++;
        return output;
    }
    private void Invoke(AddonEvent type,AddonArgs args)
    {
        var source=original!;
        // Original registration remains untouched in the DR scoped service. An
        // unregister marks it immediately, even before host deferred removal runs.
        if(relay?.Active is not true)return;
        if(stopped||busy||!allowed()||!framework.IsInFrameworkUpdateThread){if(stopped)Clear();if(!Exclusive()||!allowed())source(type,args);return;}
        var key=context();
        if(type!=AddonEvent.PreRequestedUpdate||args is not AddonRequestedUpdateArgs update||key.Id==0||key.Addon!=args.Addon.Address)
        {source(type,args);return;}
        var target=(key.Id,action?detailKind():0);
        if(blockedTarget!=target){blockedFields.Clear();blockedTarget=target;}
        Clear();Calls++;busy=true;
        try
        {
            bool known;DailyRoutineCallbacks.Snapshot? snapshot=null;
            try{known=callbacks!=null&&callbacks.TrySnapshot(out snapshot,out _);if(!known)Status="元表示を維持: 対象登録を確認できません";}
            catch(Exception ex){known=false;Status="登録確認失敗: "+ex.GetBaseException().Message;}
            if(!known&&Exclusive()){OutputFailures++;Status="DRの追記を保留: 登録を確認できません";Rejected?.Record("Daily Routines",null,Status);return;}
            var sourceRegistrations=snapshot?.Registrations;
            var fields=snapshot?.Fields??[];
            var observed=new Dictionary<int,byte[]>();
            StringArrayData* observedArray=null;
            if(known)try
            {
                var incoming=(StringArrayData**)update.StringArrayData;
                observedArray=incoming==null?null:incoming[(int)(action?StringArrayType.ActionDetail:StringArrayType.ItemDetail)];
                if(observedArray==null||observedArray->StringArray==null||observedArray->AtkArrayData.Size>256||fields.Any(field=>field>=observedArray->AtkArrayData.Size))known=false;
                else foreach(var field in fields)observed[field]=Read(observedArray,field);
            }
            catch{known=false;Status="元表示を維持: 生成前の文字列を確認できません";}
            // All source collectors, settings, remote queries and formatters run once.
            byte[]? nonGearContribution=null;
            var sourceExtras=new List<byte[]>();
            if(known&&(!action||Exclusive()))
            {
                var observers=snapshot!.Observers;var scopes=new IDisposable?[observers.Length];
                sourceDescription=observed.GetValueOrDefault(13);
                sourceBaselines=observed;
                try
                {
                    // Nested list replacements are unwound in reverse order.
                    // An unavailable scope leaves that source callback untouched.
                    for(var i=0;i<observers.Length;i++)
                        try{scopes[i]=observers[i].Begin(key.Id);}
                        catch{Status="DR生成内容の観測を保留";}
                    if(Exclusive()&&scopes.Any(scope=>scope==null)){OutputFailures++;Status="DRの追記を保留: 出力先を確保できません";return;}
                    source(type,args);
                    for(var i=0;i<observers.Length;i++)
                    {
                        if(scopes[i]==null)continue;
                        if(ReferenceEquals(observers[i],snapshot.Salvage))nonGearContribution=observers[i].Description;
                        if(observers[i].SeparatedDescription is {} extra)sourceExtras.Add(extra);
                        if(Exclusive()){sourceExtras.AddRange(observers[i].Outputs);WithheldOperations+=observers[i].Withheld;if(observers[i].Failed)OutputFailures++;}
                    }
                }
                finally
                {
                    sourceDescription=null;sourceBaselines=null;
                    Exception? cleanupFailure=null;
                    for(var i=scopes.Length-1;i>=0;i--)try{scopes[i]?.Dispose();}catch(Exception ex){cleanupFailure??=ex;}
                    if(cleanupFailure!=null)throw cleanupFailure;
                }
            }
            else source(type,args);
            if(!known||stopped||relay?.Active is not true||!allowed()||context()!=key)return;
            try
            {
                if(!ReferenceEquals(sourceRegistrations,Registrations())){Status="元表示を維持: 生成中に登録が変更されました";return;}
                if(lastItem!.GetValue(manager) is not System.Runtime.CompilerServices.ITuple identity||identity.Length!=2){Status="元表示を維持: 対象識別の形式が未対応";return;}
                if(action){if(identity[1] is not uint actionId||key.Id!=actionId||Convert.ToInt32(identity[0])!=detailKind())return;}
                else if(identity[0] is not uint id||key.Id!=id+Convert.ToUInt64(identity[1])){Status="元表示を維持: 生成元と表示対象が不一致";return;}
                // These operations have already been diverted from native output.
                // Publish their owned current-target text independently of any
                // later description/layout transfer, which can safely defer.
                var published=sourceExtras.Count>0;
                if(published){Publish(key.Id,key.Addon,detailKind(),sourceRegistrations!,CapturedText.Rows(sourceExtras),true);SourceFirstCaptures++;Status="DRの追加情報を直接分離済み";}
                if(Exclusive())
                {
                    FieldStatus="書込み前に追加命令をTEへ転送";
                    Status=published?"TE専用の出力へ分離済み":"TE専用の出力 / 追加情報なし、または取得保留";
                    return; // Never restore cached native text or run native layout changes.
                }
                var baseTexts=(ReadOnlySeString[])originals!.GetValue(manager)!;
                var arrays=(StringArrayData**)update.StringArrayData;
                var strings=arrays==null?null:arrays[(int)(action?StringArrayType.ActionDetail:StringArrayType.ItemDetail)];
                if(fields.Length==0){Status="文字追記なし";return;}
                if(strings==null||strings!=observedArray||strings->StringArray==null||strings->AtkArrayData.Size<=fields.Max()||strings->AtkArrayData.Size>256||baseTexts.Length<=fields.Max())return;
                var allNumbers=(NumberArrayData**)update.NumberArrayData;
                var numbers=action||allNumbers==null?null:allNumbers[(int)NumberArrayType.ItemDetail];
                if(!action&&(numbers==null||numbers->IntArray==null||numbers->AtkArrayData.Size<=5||numbers->AtkArrayData.Size>256))return;
                var oldFlags=action?0:numbers->IntArray[5];
                var selected=new List<int>();var before=new List<byte[]?>();var after=new List<byte[]?>();var output=new List<byte[]>();var fieldStates=new List<string>();
                foreach(var field in fields)
                {
                    if(blockedFields.TryGetValue(field,out var blocked)){fieldStates.Add(field+": 元表示を維持 / "+blocked);continue;}
                    var generatedSpan=ReadSpan(strings,field);
                    var incoming=observed.GetValueOrDefault(field);
                    var rebuiltCandidate=field==13&&sourceLanguage!=null&&nonGearContribution!=null&&generatedSpan.StartsWith(nonGearContribution);
                    if(incoming==null||!rebuiltCandidate&&!DailyRoutineText.ChangedVisible(field,incoming,generatedSpan,oldFlags,action))
                    {fieldStates.Add(field+": 生成変更なし/非表示");continue;}
                    var generated=generatedSpan.ToArray();
                    if(baseTexts[field].Data.Length>32768)throw new NotSupportedException("元本文長の上限");
                    var baseline=baseTexts[field].Data.Span.ToArray();
                    // Ignore dormant array slots and unchanged values. A provider
                    // registered for a field does not prove it wrote that field on
                    // this item (e.g. gear vs non-gear salvage, or disabled ID).
                    byte[]? extra=null;
                    if(rebuiltCandidate)
                    {
                        if(NonGearPattern() is {} pattern&&DailyRoutineText.SplitNonGear(generated,pattern,out var rebuilt,out var added))
                        {if(!baseline.AsSpan().SequenceEqual(rebuilt))RebuiltDescriptions++;baseline=rebuilt;extra=added;}
                    }
                    if(!(extra!=null&&(oldFlags&2)!=0)&&!DailyRoutineText.ChangedVisible(field,incoming,generated,oldFlags,action))continue;
                    extra??=DailyRoutineText.Extract(field,baseline,generated);
                    if(extra==null&&field==35&&sourceLanguage!=null&&sourceColon!=null)
                    {
                        var label=sourceLanguage("OptimizedSalvage-Level-Current");
                        if(!string.IsNullOrWhiteSpace(label))extra=DailyRoutineText.ExtractSalvage(baseline,generated,label+sourceColon());
                    }
                    if(extra is not {Length:>0})
                    {if(generated.Length>0&&!baseline.AsSpan().SequenceEqual(generated)){Rejections++;fieldStates.Add(field+": 元表示を維持（境界未確定）");Rejected?.Record("Daily Routines",field,"追記境界が未確定です",generated);}continue;}
                    selected.Add(field);before.Add(baseline);after.Add(generated);output.Add(extra);
                }
                FieldStatus=fieldStates.Count==0?"追加情報なし":string.Join(" / ",fieldStates);
                var reviewedShop=snapshot!.Shop;
                if(selected.Count==0&&!reviewedShop&&sourceExtras.Count==0){Status="追加情報なし、または元本文との境界が未確定";return;}
                var registrations=Registrations();
                void Transfer()
                {
                if(stopped||relay?.Active is not true||!allowed()||context()!=key||!ReferenceEquals(registrations,Registrations()))
                {Status="元表示を維持: 描画確定前に対象または登録が変わりました";return;}
                var completed=new List<byte[]>();
                var descriptionEnd=0;
                for(var i=0;i<selected.Count;i++)
                {
                    var field=selected[i];
                    // Each field is a separate transfer, like the existing AT
                    // sections. A failed ability layout must not cancel MARKET.
                    Action<bool>? layout;byte[] forward,rollback;
                    try
                    {
                        if(!Read(strings,field).AsSpan().SequenceEqual(after[i]))
                            throw new DeferredTooltipPresentationException("描画確定前に生成本文が変わりました");
                        forward=Terminate(before[i]!);rollback=Terminate(after[i]!);
                        layout=action?null:DailyRoutineLayout.Prepare(key.Addon,numbers,strings,[field],[before[i]],geometry=>Layout=geometry,[after[i]]);
                    }
                    catch(Exception ex)
                    {
                        Rejections++;var reason=ex.GetBaseException().Message;
                        if(ex is DeferredTooltipPresentationException)DeferredPresentations++;else blockedFields[field]=reason;
                        fieldStates.Add(field+": 元表示を維持 / "+reason);Rejected?.Record("Daily Routines",field,reason,after[i]);continue;
                    }
                    var flagsBefore=action?0:numbers->IntArray[5];
                    var flagsAfter=action?0:DailyRoutineText.RestoreFlags(flagsBefore,[field],[before[i]]);
                    var plan=TextTransferPlan.Create([field],[before[i]],[after[i]],false,out _)!;
                    Action<int>? writeFlags=action?null:value=>{
                        numbers->IntArray[5]=value;
                        if(numbers->IntArray[5]!=value)throw new InvalidOperationException("表示フラグの復元不一致");};
                    if(!plan.Apply((_,value)=>Write(strings,field,ReferenceEquals(value,before[i])?forward:rollback),
                        _=>Read(strings,field),writeFlags,flagsAfter,flagsBefore,out var failure,out var reverted,layout))
                    {
                        Rejections++;blockedFields[field]=failure;fieldStates.Add(field+": 元表示を維持 / "+failure);Rejected?.Record("Daily Routines",field,failure,after[i]);
                        if(!reverted){failed=stopped=true;break;}
                        continue;
                    }
                    completed.Add(output[i]);if(field<=13)descriptionEnd=completed.Count;fieldStates.Add(field+": 分離");
                }
                if(sourceExtras.Count>0)
                {completed.InsertRange(descriptionEnd,sourceExtras);fieldStates.Add("13: 生成元の追記を直接分離");}
                var fixedShop=false;
                if(reviewedShop&&!stopped)
                {
                    Action<bool>? emptyLayout=null;
                    try{emptyLayout=DailyRoutineLayout.PrepareEmptyShop(key.Addon,numbers,strings,true);}
                    catch(Exception ex){Rejections++;fieldStates.Add("63: 元表示を維持 / "+ex.GetBaseException().Message);}
                    if(emptyLayout!=null)
                    {
                        var flagsBefore=numbers->IntArray[5];
                        void WriteFlags(int value){numbers->IntArray[5]=value;if(numbers->IntArray[5]!=value)throw new InvalidOperationException("販売価格フラグの書戻し不一致");}
                        if(TextTransferPlan.FlagsOnly().Apply((_,_)=>throw new InvalidOperationException(),_=>throw new InvalidOperationException(),WriteFlags,
                            flagsBefore&~DailyRoutineText.ShopSellingPriceFlag,flagsBefore,out var failure,out var reverted,emptyLayout))
                        {fixedShop=true;EmptyShopFixes++;fieldStates.Add("63: 空の販売価格欄を非表示");}
                        else {Rejections++;fieldStates.Add("63: 元表示を維持 / "+failure);if(!reverted)failed=stopped=true;}
                    }
                }
                FieldStatus=string.Join(" / ",fieldStates);
                if(completed.Count==0){Status=stopped?"DR分離停止: 復元未確認":fixedShop?"空の販売価格欄を調整済み":"対象欄は元表示を維持";return;}
                if(!published||completed.Count!=sourceExtras.Count)
                    Publish(key.Id,key.Addon,detailKind(),registrations!,CapturedText.Rows(completed),!published);
                Status=stopped?"分離済みの情報を保持 / 後続処理停止":blockedFields.Count>0?"DRの追加情報を分離済み / 一部は元表示を維持":"DRの追加情報を分離済み";
                }
                if(action)Transfer();
                else
                {
                    requested.Stage(new(key.Id,key.Addon,(nint)strings,(nint)numbers),Transfer);
                    Status="元の描画更新の完了待ち";
                }
            }
            catch(Exception ex){Rejections++;failed=stopped=true;Status="DR分離停止: "+ex.GetBaseException().Message;}
        }
        finally{busy=false;}
    }
    public void CompleteRequested(AddonArgs args)
    {
        if(!requested.Pending)return;
        if(stopped||busy||!allowed()||!framework.IsInFrameworkUpdateThread||args is not AddonRequestedUpdateArgs update)
        {requested.Clear();return;}
        var key=context();
        if(key.Addon!=args.Addon.Address){requested.Clear();return;}
        var arrays=(StringArrayData**)update.StringArrayData;
        var allNumbers=(NumberArrayData**)update.NumberArrayData;
        var strings=arrays==null?null:arrays[(int)StringArrayType.ItemDetail];
        var numbers=allNumbers==null?null:allNumbers[(int)NumberArrayType.ItemDetail];
        busy=true;
        try
        {
            if(!requested.Complete(new(key.Id,key.Addon,(nint)strings,(nint)numbers)))
                Status="元表示を維持: 更新対象または配列が変わりました";
        }
        catch(Exception ex){Rejections++;failed=stopped=true;Status="DR分離停止: "+ex.GetBaseException().Message;}
        finally{busy=false;}
    }
    private object? Registrations()=>callbacks!=null?callbacks.Current:((IDictionary)methods!.GetValue(manager)!)[callbackType!];
    private bool HasReviewedProducer(object target)
    {
        if(target.GetType().GetField("methodsCollection",Flags)?.GetValue(target) is not IDictionary dictionary)return false;
        foreach(DictionaryEntry entry in dictionary)
            if(entry.Key is Type type&&type.FullName=="OmenTools.OmenService.TooltipManager+"+(action?"Action":"Item")+"TooltipUpdateDelegate"&&entry.Value is IEnumerable list)
                return list.Cast<Delegate>().Any(d=>d.Method.DeclaringType?.Assembly.GetName().Name=="DailyRoutines.ModulesPublic"&&DailyRoutineText.Producers.Contains(d.Method.DeclaringType.FullName));
        return false;
    }
    private IEnumerable<Delegate> Callbacks()=>Registrations() is IEnumerable list?list.Cast<Delegate>():[];
    private void Publish(ulong id,nint address,int kind,object registrations,byte[] text,bool count)
    {item=id;addon=address;capturedDetailKind=kind;capturedRegistrations=registrations;content=text;if(count)Captures++;}
    private static byte[] Read(StringArrayData* strings,int field)=>ReadSpan(strings,field).ToArray();
    private static ReadOnlySpan<byte> ReadSpan(StringArrayData* strings,int field)
    {
        byte* p=strings->StringArray[field];if(p==null)return [];
        var length=0;while(length<32768&&p[length]!=0)length++;
        if(length==32768)throw new NotSupportedException("本文長の上限");return new ReadOnlySpan<byte>(p,length);
    }
    private static void Write(StringArrayData* strings,int field,byte[] value)
    {strings->SetValue(field,value,false,true,false);}
    private static byte[] Terminate(byte[] value){var result=new byte[value.Length+1];value.CopyTo(result,0);return result;}
    public byte[]? Get(ulong id,nint address)
    {if(!allowed()||relay?.Active is not true||item!=id||addon!=address||capturedDetailKind!=detailKind()||!ReferenceEquals(capturedRegistrations,Registrations()))Clear();return content;}
    public void Clear(){requested.Clear();content=null;item=0;addon=0;capturedRegistrations=null;}
    public void Dispose()
    {
        stopped=true;Clear();
        var pending=relay;
        if(pending==null)return;
        var pendingManager=manager;
        var pendingRefresh=refreshMethod;
        var pendingService=hostService;
        void Detach()
        {
            // Never mutate the HashSet while the host dispatch enumerates it.
            // A retained callback stays in forwarding mode until this tick runs.
            if(pendingService!=null&&Field(pendingService,"isInvokingListeners") is true)
            {_=framework.RunOnTick(Detach,delayTicks:1);return;}
            var active=pending.Active;pending.Dispose();
            if(ReferenceEquals(relay,pending)){relay=null;refreshMethod=null;callbacks=null;sourceLanguage=null;sourceColon=null;nonGearMacro=null;nonGearPattern=null;}
            if(active)try{if(pendingManager!=null)pendingRefresh?.Invoke(pendingManager,null);}catch{/* Display regenerates on the next natural update. */}
        }
        if(framework.IsInFrameworkUpdateThread)Detach();else _=framework.RunOnFrameworkThread(Detach);
    }
    public void Reconnect(){Dispose();blockedFields.Clear();failed=false;OutputFailures=0;nextAttempt=0;}
}
