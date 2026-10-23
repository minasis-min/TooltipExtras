using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class AllaganAdapter : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly int[] Fields = [13, 16, 23];
    private static readonly string[] Names = ["AmountOwnedTooltip", "SourceInformationTooltip", "UseInformationTooltip"];
    private static readonly string[] ExtraNames=["CofferLootTooltip","CuratedListTooltip","DisplayMarketPriceTooltip","DisplayUnlockTooltip","GlamourReadySetTooltip","IngredientPatchTooltip","LocationDisplayTooltip"];
    private readonly string?[] sectionNames=new string?[32];
    private readonly Func<bool> allowed;
    private readonly Func<(ulong Id, nint Addon)> context;
    private readonly Func<bool> frameworkThread;
    private readonly List<(IList List, object? Original, object Proxy)> slots = [];
    private readonly RelaySnapshot snapshot = new(compact: true, sectionCount:32);
    public Func<bool> Exclusive {get;set;}=()=>false;
    public NativeStringSink? Sink {get;set;}
    public RejectedTextLog? Rejected {get;set;}
    private Func<bool> sinkMatches=()=>true;
    public long WithheldWrites {get;private set;}
    public long OutputFailures {get;private set;}
    public bool ConnectionFailed {get;private set;}
    private readonly DecorationTransfer decoration = new();
    private IList? dispatch;
    private object? first, last;
    private int dispatchDepth;
    private bool stopped, busy, requested, generationFailed;
    private ulong item;
    private nint addon, strings;
    private long nextAttempt;
    public string Status { get; private set; } = "待機中";
    public long Captures { get; private set; }
    public long Rejections { get; private set; }
    public long Requests { get; private set; }
    public long Callbacks { get; private set; }
    public long OutsideRequests { get; private set; }
    public long DecorationsRemoved { get; private set; }
    public bool DecorationsConnected => first != null && last != null;
    public int Sections => snapshot.Count;
    public int Connections => slots.Count;
    public AllaganAdapter(Func<bool> allowed, Func<(ulong, nint)> context, Func<bool> frameworkThread)
    { this.allowed = allowed; this.context = context; this.frameworkThread = frameworkThread; }

    private static object? Field(object owner, string name)
    {
        for (var type = owner.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Flags | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(owner);
        throw new MissingFieldException(owner.GetType().FullName, name);
    }
    public void Refresh(IDalamudPluginInterface pi, bool enabled)
    {
        if (!enabled) { if (slots.Count > 0) Dispose(); stopped = false;ConnectionFailed=false; Clear(); Status = "無効"; return; }
        if (stopped || Environment.TickCount64 < nextAttempt) return;
        nextAttempt = Environment.TickCount64 + 1000;
        try
        {
            var installed = pi.InstalledPlugins.Where(p => p.InternalName == "InventoryTools" && p.IsLoaded).ToArray();
            if (installed.Length != 1) { if (slots.Count > 0) Dispose(); Status = "未ロード、または複数ロード"; return; }
            // Current Dalamud ExposedPlugin holds LocalPlugin; inspect only this selected plugin.
            var localField = installed[0].GetType().GetFields(Flags).Single(f => f.FieldType.FullName == "Dalamud.Plugin.Internal.Types.LocalPlugin");
            var local = localField.GetValue(installed[0])!;
            var plugin = Field(local, "instance");
            if (plugin?.GetType().FullName != "InventoryTools.InventoryToolsPlugin") { Status = "インスタンス準備待ち"; return; }
            if (plugin.GetType().GetProperty("IsStarted", Flags)?.GetValue(plugin) is not true) { Status = "初期化待ち"; return; }
            var hostProperty = plugin.GetType().GetProperty("Host", Flags) ?? throw new MissingMemberException("HostedPlugin.Host");
            var host = hostProperty.GetValue(plugin)!;
            var services = (IServiceProvider)hostProperty.PropertyType.GetProperty("Services")!.GetValue(host)!;
            var loadContext = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
            var common = loadContext.Assemblies.Single(a => a.GetName().Name == "CriticalCommonLib");
            var service = services.GetService(common.GetType("CriticalCommonLib.Services.ITooltipService", true)!) ?? throw new InvalidOperationException("TooltipService未登録");
            var list = (IList)Field(service, "_tooltipTweaks")!;
            if (slots.Count > 0)
            {
                if (slots.All(s => ReferenceEquals(s.List, list) && list.Cast<object>().Any(p => ReferenceEquals(p, s.Proxy)))
                    && (first == null || DispatchIntact())) return;
                Dispose();
            }
            var baseType = common.GetType("CriticalCommonLib.Services.TooltipService+TooltipTweak", true)!;
            var check = CompatibilityContract.Check(CompatibilityContract.Load(), "AllaganProvider", (assembly, type) =>
                assembly == "CriticalCommonLib" ? common.GetType(type) : assembly == "InventoryTools" ? plugin.GetType().Assembly.GetType(type) : null);
            if (!check.Allowed) throw new NotSupportedException(check.Reason);
            var selected = Names.Select(name => list.Cast<object>().Single(t => t.GetType().FullName == "InventoryTools.Tooltips." + name)).ToArray();
            var callback = baseType.GetMethod("OnGenerateItemTooltip")!;
            var pointerTypes = callback.GetParameters().Select(p => p.ParameterType.GetElementType()!.FullName).ToArray();
            var providerNative = callback.GetParameters()[1].ParameterType.GetElementType()!.Assembly;
            var abi = CompatibilityContract.Check(CompatibilityContract.Load(), "AllaganNative", (assembly, type) => assembly == "FFXIVClientStructs" ? providerNative.GetType(type) : null);
            if (!abi.Allowed) throw new NotSupportedException("AT側配列ABI: " + abi.Reason);
            if (!pointerTypes.SequenceEqual(new[] { typeof(NumberArrayData).FullName, typeof(StringArrayData).FullName })) throw new NotSupportedException("配列型不一致");
            var proxyType = TooltipProxy.Create(baseType);
            if(Exclusive())
            {
                var exclusiveContracts=CompatibilityContract.Load();
                var exclusiveCheck=CompatibilityContract.Check(exclusiveContracts,"AllaganExclusiveProvider",(assembly,type)=>assembly=="InventoryTools"?plugin.GetType().Assembly.GetType(type):null);
                if(exclusiveCheck.Allowed){sinkMatches=Sink!.VerifyNativeSetter(providerNative);try{Sink.Connect();}catch{OutputFailures++;}}
                var all=list.Cast<object>().Where(t=>t.GetType().Assembly==plugin.GetType().Assembly&&t.GetType().Namespace=="InventoryTools.Tooltips"&&baseType.IsInstanceOfType(t)).ToArray();
                if(all.Length>32)throw new NotSupportedException("AT追記の登録数上限");
                for(var i=0;i<all.Length;i++)
                {
                    var index=i;var original=all[i];var name=original.GetType().Name;
                    var decorate=name is "HeaderTextTooltip" or "FooterTextTooltip";
                    var extra=ExtraNames.Contains(name)&&CompatibilityContract.Check(exclusiveContracts.Where(e=>e.Type==original.GetType().FullName),
                        "AllaganExtraProvider",(assembly,type)=>assembly=="InventoryTools"?plugin.GetType().Assembly.GetType(type):null).Allowed;
                    var reviewed=exclusiveCheck.Allowed&&(Names.Contains(name)||decorate||extra);
                    sectionNames[index]=name;
                    var readExclusiveEnabled=Expression.Lambda<Func<bool>>(Expression.Property(Expression.Constant(original),"IsEnabled")).Compile();
                    var invoke=TooltipProxy.Forward(original,callback);
                    Action<nint,nint> capture=(n,s)=>CaptureExclusive(index,reviewed,decorate,invoke,n,s);
                    var proxy=Activator.CreateInstance(proxyType,[Field(original,"_logger"),new Func<bool>(()=>{var value=readExclusiveEnabled();if(!value)snapshot.Set(index,null);return value;}),capture])!;
                    slots.Add((list,original,proxy));list[list.IndexOf(original)]=proxy;
                }
                stopped=false;Status=exclusiveCheck.Allowed?"TE専用の出力に接続 / 捕捉待ち":"ATの追記を保留: 出力処理の互換性未確認";
                return;
            }
            for (var i = 0; i < selected.Length; i++)
            {
                var index = i; var original = selected[i];
                var readEnabled = Expression.Lambda<Func<bool>>(Expression.Property(Expression.Constant(original), "IsEnabled")).Compile();
                Func<bool> enabledGetter = () =>
                {
                    var enabled = readEnabled();
                    if (!enabled) snapshot.Set(index, null);
                    return enabled;
                };
                var invoke = TooltipProxy.Forward(original, callback);
                Action<nint, nint> capture = (numbers, array) => Intercept(index, invoke, numbers, array);
                var proxy = Activator.CreateInstance(proxyType, [Field(original, "_logger"), enabledGetter, capture])!;
                var position = list.IndexOf(original);
                if (position < 0) throw new InvalidOperationException("追記登録が変更されました");
                slots.Add((list, original, proxy)); // Rollback can restore even if a later insertion fails.
                list[position] = proxy;
            }
            // These are separate AT callbacks, not a string header added by TE.
            var decorations = new[] { "HeaderTextTooltip", "FooterTextTooltip" }
                .Select(name => list.Cast<object>().SingleOrDefault(t => t.GetType().FullName == "InventoryTools.Tooltips." + name)).ToArray();
            if (decorations.All(t => t != null))
            {
                foreach (var original in decorations)
                {
                    var readDecorationEnabled = Expression.Lambda<Func<bool>>(Expression.Property(Expression.Constant(original), "IsEnabled")).Compile();
                    var invoke = TooltipProxy.Forward(original!, callback);
                    Action<nint, nint> observe = (numbers, array) => ObserveDecoration(invoke, numbers, array);
                    var proxy = Activator.CreateInstance(proxyType, [Field(original!, "_logger"), readDecorationEnabled, observe])!;
                    slots.Add((list, original, proxy));
                    list[list.IndexOf(original)] = proxy;
                }
                var logger = Field(selected[0], "_logger");
                first = Activator.CreateInstance(proxyType, [logger, new Func<bool>(() => true), new Action<nint, nint>((_, array) => StartDispatch(array))])!;
                last = Activator.CreateInstance(proxyType, [logger, new Func<bool>(() => true), new Action<nint, nint>((_, array) => FinishDispatch(array))])!;
                dispatch = list;
                slots.Add((list, null, first)); list.Insert(0, first);
                slots.Add((list, null, last)); list.Add(last);
            }
            stopped = false; Status = "接続済み / 捕捉待ち";
        }
        catch (Exception ex)
        {
            ConnectionFailed=true;
            stopped = true;
            Status = "中継停止: " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message;
            Detach();
        }
    }
    public void Begin(ulong id, nint address, nint array)
    {
        // Only the borrowing window resets here. The display lifetime is the target.
        snapshot.Begin(id, address); item = id; addon = address; strings = array;
        generationFailed = false; Requests++;
        requested = allowed() && frameworkThread() && id != 0 && address != 0 && array != 0;
    }
    public void End() { requested = false; strings = 0; decoration.Clear(); dispatchDepth = 0; }
    public void Clear()
    {
        snapshot.Clear(); decoration.Clear(); dispatchDepth = 0; requested = false; generationFailed = false;
        item = 0; addon = strings = 0;
    }
    public byte[]? Get(ulong id, nint address)
    {
        if (!allowed() || id != item || address != addon) { Clear(); return null; }
        return snapshot.Get(id, address);
    }
    private static byte[]? Read(StringArrayData* array, int field)
    {
        byte* text = array->StringArray[field];
        if (text == null) return null;
        var length = 0;
        while (length < 32768 && text[length] != 0) length++;
        if (length == 32768) throw new NotSupportedException("文字列上限超過");
        return new ReadOnlySpan<byte>(text, length).ToArray();
    }
    private static byte[]?[] Snapshot(StringArrayData* array) => Fields.Select(field => Read(array, field)).ToArray();
    private void CaptureExclusive(int section,bool reviewed,bool decorate,Action<nint,nint> original,nint numbers,nint address)
    {
        Callbacks++;snapshot.Set(section,null);
        if(!reviewed){OutputFailures++;Status="ATの未対応の追記を保留（元欄には追加しません）";Rejected?.Record("Allagan Tools / "+(sectionNames[section]??"不明"),null,Status);return;}
        if(!CanDecorate(address)){OutsideRequests++;if(allowed()&&context().Id!=0)OutputFailures++;return;}
        if(!sinkMatches()){OutputFailures++;Status="ATの文字列出力先が変更されたため追記を保留";return;}
        var key=context();busy=true;
        try
        {
            var capture=Sink!.Capture((StringArrayData*)address,isolated=>original(numbers,isolated),"Allagan Tools");
            WithheldWrites+=capture.Writes.Count;
            if(capture.Failure.Length!=0){OutputFailures++;Status="ATの追加情報を取得できません";return;}
            if(!allowed()||context()!=key)return;
            if(decorate){if(capture.Writes.Count>0)DecorationsRemoved++;return;}
            var output=new List<byte[]>();
            foreach(var (field,generated) in capture.Writes)
            {
                if(!Fields.Contains(field)||generated==null||!capture.Before.TryGetValue(field,out var baseline)
                    ||!generated.AsSpan().StartsWith(baseline??[])){OutputFailures++;Rejected?.Record("Allagan Tools",field,"追記境界または出力欄が未対応です",generated);continue;}
                var suffix=CapturedText.TrimAllaganEdges(generated.AsSpan(baseline?.Length??0).ToArray());
                if(suffix.Length>0)output.Add(suffix);
            }
            if(output.Count>0){snapshot.Set(section,CapturedText.Rows(output));Captures++;Status="ATの追加情報をTE専用の出力へ分離済み";}
        }
        catch(Exception ex){OutputFailures++;Status="ATの追加情報を保留: "+ex.GetBaseException().GetType().Name;Rejected?.Record("Allagan Tools",null,Status);}
        finally{busy=false;}
    }
    private bool CanDecorate(nint address)
    {
        if (stopped || busy || generationFailed || !requested || !frameworkThread() || !allowed() || address != strings || address == 0) return false;
        var key = context(); var array = (StringArrayData*)address;
        return key.Id == item && key.Addon == addon && array->StringArray != null && array->AtkArrayData.Size > 23 && array->AtkArrayData.Size <= 256;
    }
    private bool DispatchIntact() => dispatch is { Count: >= 2 } && ReferenceEquals(dispatch[0], first)
        && ReferenceEquals(dispatch[dispatch.Count - 1], last)
        && slots.All(s => ReferenceEquals(s.List, dispatch) && dispatch.Cast<object>().Any(p => ReferenceEquals(p, s.Proxy)));
    private void StartDispatch(nint address)
    {
        decoration.Clear(); dispatchDepth++;
        try { if (dispatchDepth == 1 && CanDecorate(address) && DispatchIntact()) decoration.Begin(Snapshot((StringArrayData*)address)); }
        catch { decoration.Clear(); }
    }
    private void ObserveDecoration(Action<nint, nint> original, nint numbers, nint address)
    {
        byte[]?[]? before = null;
        try { if (dispatchDepth == 1 && CanDecorate(address)) before = Snapshot((StringArrayData*)address); }
        catch { decoration.Clear(); }
        var wasBusy = busy; busy = true;
        try { original(numbers, address); }
        catch { decoration.Clear(); throw; }
        finally { busy = wasBusy; }
        try
        {
            if (before != null && dispatchDepth == 1 && CanDecorate(address)) decoration.Observe(before, Snapshot((StringArrayData*)address));
            else decoration.Clear();
        }
        catch { decoration.Clear(); }
    }
    private void FinishDispatch(nint address)
    {
        try
        {
            if (dispatchDepth != 1 || !CanDecorate(address) || !DispatchIntact()) return;
            var array = (StringArrayData*)address;
            var plan = decoration.Finish(Fields, Snapshot(array));
            if (plan == null) return;
            if (plan.Apply((field, bytes) => array->SetValue(field, [..bytes!, (byte)0], false, true, false),
                field => Read(array, field), null, 0, 0, out var failure, out var rollback)) DecorationsRemoved++;
            else { Reject("AT見出しの復元失敗: " + failure); if (!rollback) stopped = true; }
        }
        catch (Exception ex) { Reject(ex.GetBaseException().GetType().Name); }
        finally { decoration.Clear(); dispatchDepth = Math.Max(0, dispatchDepth - 1); }
    }
    private void Reject(string reason)
    {
        Rejections++; Status = "元表示を維持: " + reason;
        // Earlier successful sections were already removed from native fields. Keep them visible.
        generationFailed = true;
    }
    private void Intercept(int section, Action<nint, nint> original, nint numbers, nint address)
    {
        Callbacks++;
        if (stopped || busy || generationFailed || !requested || !frameworkThread() || !allowed() || address != strings)
        {
            OutsideRequests++;
            snapshot.Set(section, null); // Native forwarding must not also retain the same section on TE.
            original(numbers, address); return;
        }
        var key = context();
        if (key.Id != item || key.Addon != addon) { snapshot.Clear(); original(numbers, address); return; }
        var array = (StringArrayData*)address;
        byte[]?[] before;
        try
        {
            if (array == null || array->StringArray == null || array->AtkArrayData.Size <= 23 || array->AtkArrayData.Size > 256) throw new NotSupportedException("配列形状不一致");
            before = Snapshot(array);
        }
        catch (Exception ex) { snapshot.Set(section, null); Reject(ex.GetBaseException().Message); original(numbers, address); return; }
        busy = true;
        try
        {
            try { original(numbers, address); } // Exactly once; retain the provider's own failure behavior.
            catch { snapshot.Set(section, null); decoration.Clear(); generationFailed = true; throw; }
            try
            {
                var after = Snapshot(array);
                if (before.Zip(after).All(p => p.First == null ? p.Second == null : p.Second != null && p.First.AsSpan().SequenceEqual(p.Second)))
                { snapshot.Set(section, null); return; }
                if (!AppendDelta.TrySelect(before, after, out var changed, out var suffix)) { snapshot.Set(section, null); Reject("追記境界が一致しません");Rejected?.Changed("Allagan Tools",Status,Fields,before,after); return; }
                key = context();
                if (!allowed() || key.Id != item || key.Addon != addon) { Reject("対象が変わりました"); return; }
                var restore = new byte[before[changed]!.Length + 1]; before[changed]!.CopyTo(restore, 0);
                try
                {
                    array->SetValue(Fields[changed], restore, false, true, false);
                    var restored = Read(array, Fields[changed]);
                    if (restored == null || !restored.AsSpan().SequenceEqual(before[changed])) throw new InvalidOperationException("復元値不一致");
                }
                catch (Exception restoreError)
                {
                    // If a write fails, try to retain the exact provider result in native output.
                    try
                    {
                        var fallback = new byte[after[changed]!.Length + 1]; after[changed]!.CopyTo(fallback, 0);
                        array->SetValue(Fields[changed], fallback, false, true, false);
                    }
                    catch { /* Stop all later writes; do not retry the provider callback. */ }
                    snapshot.Set(section, null); Reject("元本文の復元失敗: " + restoreError.GetBaseException().Message);Rejected?.Record("Allagan Tools",Fields[changed],Status,after[changed]); stopped = true; return;
                }
                snapshot.Set(section, CapturedText.TrimAllaganEdges(suffix)); Captures++; Status = "追記を取得・元欄を復元済み";
            }
            catch (Exception ex) { Reject(ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message); }
        }
        finally { busy = false; }
    }
    private void Detach()
    {
        var cleanup = new Cleanup();
        foreach (var slot in slots.ToArray()) cleanup.Try("AT slot", () =>
        {
            if (slot.Original != null) AppendDelta.RestoreSlot(slot.List, slot.Proxy, slot.Original);
            else for (var i = slot.List.Count - 1; i >= 0; i--) if (ReferenceEquals(slot.List[i], slot.Proxy)) slot.List.RemoveAt(i);
            slots.Remove(slot);
        });
        cleanup.ThrowIfFailed();
        dispatch = null; first = last = null;
    }
    public void Dispose()
    {
        stopped = true; Clear(); Detach();
    }
    public void Reconnect()
    {
        Dispose(); stopped = false;ConnectionFailed=false;OutputFailures=0; nextAttempt = 0; Status = "再接続待ち";
    }
}
