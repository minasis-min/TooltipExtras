using System.Collections;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class SimpleAdapter : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private const uint NodeId = 0x5354000B;
    private readonly Func<bool> allowed, ctrl, frameworkThread;
    private readonly Func<(ulong Id, nint Addon)> context;
    private readonly RelaySnapshot content = new();
    private readonly Action<LayoutChange> restoredLayout;
    private readonly Func<bool> nativeCompatible;
    public bool PendingRestore=>saved!=null;
    private object? subscriber;
    private PropertyInfo? methodProperty;
    private MethodInfo? originalMethod, wrapper;
    private long nextAttempt;
    private bool stopped;
    public Func<bool> Exclusive {get;set;}=()=>false;
    public RejectedTextLog? Rejected {get;set;}
    private Func<uint,SeString>? sourceInfo;
    public long OutputFailures {get;private set;}
    public bool ConnectionFailed {get;private set;}
    private Saved? saved;
    private sealed record Saved(ulong Item, nint Addon, nint Node, nint Surface, nint Footer, SimpleGeometry Before, SimpleGeometry After, byte[] Bytes);
    public string Status { get; private set; } = "待機中";
    public long Captures { get; private set; }
    public long Rejections { get; private set; }
    public SimpleAdapter(Func<bool> allowed, Func<bool> ctrl, Func<(ulong, nint)> context, Func<bool> frameworkThread,Action<LayoutChange> restoredLayout,Func<bool> nativeCompatible)
    { this.allowed = allowed; this.ctrl = ctrl; this.context = context; this.frameworkThread = frameworkThread; this.restoredLayout=restoredLayout;this.nativeCompatible=nativeCompatible; }
    public void Refresh(IDalamudPluginInterface pi, bool enabled)
    {
        if (!enabled) { if (subscriber != null || saved != null) Dispose(); else Clear(); stopped = false; Status = "無効"; return; }
        if (stopped || Environment.TickCount64 < nextAttempt) return;
        nextAttempt = Environment.TickCount64 + 1000;
        try
        {
            if (!pi.InstalledPlugins.Any(p => p.InternalName == "SimpleTweaksPlugin" && p.IsLoaded)) { Dispose(); stopped = false; Status = "未ロード"; return; }
            var a = ForeignPlugin.SimpleAssembly(pi);
            var events = a.GetType("SimpleTweaksPlugin.Events.EventController", true)!;
            var groups = (IDictionary)events.GetProperty("AddonEventSubscribers", Flags)!.GetValue(null)!;
            var found = new List<object>();
            foreach (IDictionary addons in groups.Values)
                if (addons["ItemDetail"] is IEnumerable list)
                    foreach (var entry in list)
                    {
                        var tweak = entry.GetType().GetProperty("Tweak")!.GetValue(entry)!;
                        var method = (MethodInfo)entry.GetType().GetProperty("Method")!.GetValue(entry)!;
                        if (tweak.GetType().FullName == "SimpleTweaksPlugin.Tweaks.Tooltips.AdditionalItemInfo" &&
                            (method.Name == "AfterItemDetailUpdate" || ReferenceEquals(entry, subscriber))) found.Add(entry);
                    }
            if (found.Count != 1) { Dispose(); stopped = false; Status = "専用追加情報のイベント未登録"; return; }
            var sourceTweak = found[0].GetType().GetProperty("Tweak")!.GetValue(found[0])!;
            if (sourceTweak.GetType().GetProperty("Enabled")!.GetValue(sourceTweak) is not true)
            { Dispose(); stopped = false; Status = "元の追加情報が無効"; return; }
            if (ReferenceEquals(found[0], subscriber) && Equals(methodProperty!.GetValue(subscriber), wrapper)) return;
            Dispose(); stopped = false;
            var selected = found[0]; var property = selected.GetType().GetProperty("Method")!;
            var original = (MethodInfo)property.GetValue(selected)!;
            if (original.Name != "AfterItemDetailUpdate" || original.ReturnType != typeof(void) || original.GetParameters().Length != 1
                || original.GetParameters()[0].ParameterType.GetElementType()?.FullName != typeof(AtkUnitBase).FullName) throw new NotSupportedException("専用イベント署名不一致");
            var providerNative = original.GetParameters()[0].ParameterType.GetElementType()!.Assembly;
            var abi = CompatibilityContract.Check(CompatibilityContract.Load(), "PriceNative", (assembly, type) => assembly == "FFXIVClientStructs" ? providerNative.GetType(type) : null);
            if (!abi.Allowed) throw new NotSupportedException("Simple側ノードABI: " + abi.Reason);
            var target = selected.GetType().GetProperty("Tweak")!.GetValue(selected)!;
            sourceInfo=null;
            if(Exclusive())
            {
                var check=CompatibilityContract.Check(CompatibilityContract.Load(),"SimpleExtrasApi",(assembly,type)=>assembly=="SimpleTweaksPlugin"?a.GetType(type):null);
                if(check.Allowed)sourceInfo=target.GetType().GetMethod("GetInfoLines",[typeof(uint)])!.CreateDelegate<Func<uint,SeString>>(target);
            }
            var invoke = PointerCallback.Forward(target, original);
            var replacement = PointerCallback.Create(original.GetParameters()[0].ParameterType, p => Intercept(invoke, p));
            subscriber = selected; methodProperty = property; originalMethod = original; wrapper = replacement;
            property.SetValue(selected, replacement);
            Status = "接続済み / 専用ノード待ち";
        }
        catch (Exception ex) { stopped = true;ConnectionFailed=true; Status = "分離停止: " + ex.GetBaseException().Message; Detach(); }
    }
    private static bool Nodes(AtkUnitBase* addon, out AtkTextNode* row, out AtkResNode* surface, out AtkResNode* footer)
    {
        row = null; surface = footer = null;
        if (addon == null || addon->WindowNode == null || addon->WindowNode->Component == null) return false;
        var res = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, NodeId, out _);
        var manager = &addon->WindowNode->Component->UldManager;
        surface = NativeNodeLookup.FindUnique(manager->NodeList, manager->NodeListCount, 2, out _);
        footer = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, 2, out _);
        if (res == null || res->Type != NodeType.Text || surface == null || footer == null || res->ParentNode != footer->ParentNode) return false;
        row = (AtkTextNode*)res; return true;
    }
    private static SimpleGeometry Geometry(AtkUnitBase* addon, AtkResNode* surface, AtkResNode* footer) => new(addon->WindowNode->AtkResNode.Height, surface->Height, footer->Y);
    private static void Apply(AtkUnitBase* addon, AtkResNode* surface, AtkResNode* footer, SimpleGeometry shape)
    { addon->WindowNode->AtkResNode.SetHeight(shape.Window); surface->SetHeight(shape.Surface); footer->SetYFloat(shape.Footer); }
    private void Intercept(Action<nint> original, nint address)
    {
        if(Exclusive()&&allowed())
        {
            var exclusiveKey=context();content.Begin(exclusiveKey.Id,address);
            if(!frameworkThread()||exclusiveKey.Id==0||exclusiveKey.Id>uint.MaxValue||exclusiveKey.Addon!=address){content.Clear();OutputFailures++;return;}
            if(ctrl())return;
            if(sourceInfo==null){content.Set(0,null);OutputFailures++;Status="追加情報のAPIが未対応のため追記を保留";return;}
            try
            {
                var bytes=sourceInfo((uint)exclusiveKey.Id).Encode();
                if(!allowed()||context()!=exclusiveKey){content.Clear();return;}
                if(bytes.Length>32768){content.Set(0,null);OutputFailures++;return;}
                content.Set(0,bytes);if(bytes.Length>0)Captures++;Status="追加情報のAPIから取得 / 元ノードは生成しません";
            }
            catch(Exception ex){content.Set(0,null);OutputFailures++;Status="追加情報の取得を保留: "+ex.GetBaseException().GetType().Name;Rejected?.Record("Simple Tweaks / AdditionalItemInfo",null,Status);}
            return;
        }
        if (stopped || !allowed() || !frameworkThread()) { original(address); return; }
        var key = context(); var addon = (AtkUnitBase*)address;
        if (key.Id == 0 || key.Addon != address || addon == null) { original(address); return; }
        content.Begin(key.Id, address); saved = null;
        // The node may not exist until the first producer call; only fixed owned window/footer data is read before it.
        var window = addon->WindowNode;
        AtkResNode* surface = null; AtkResNode* footer = null;
        if (window != null && window->Component != null)
        {
            var manager = &window->Component->UldManager;
            surface = NativeNodeLookup.FindUnique(manager->NodeList, manager->NodeListCount, 2, out _);
            footer = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, 2, out _);
        }
        var validBefore = surface != null && footer != null;
        var before = validBefore ? Geometry(addon, surface, footer) : default;
        try { original(address); } // Generates the original SeString and node exactly once.
        catch { content.Clear(); throw; }
        try
        {
            if (!allowed() || context() != key || !validBefore || addon->WindowNode != window) { content.Clear(); return; }
            if (!Nodes(addon, out var row, out var nowSurface, out var nowFooter) || !row->AtkResNode.IsVisible())
            { if (!ctrl()) content.Set(0, null); return; }
            var after = Geometry(addon, nowSurface, nowFooter);
            if (surface != nowSurface || footer != nowFooter || !before.IsAppend(after, row->AtkResNode.Height, row->AtkResNode.Y))
            { Rejections++; content.Set(0, null); Status = "元表示を維持: 専用ノードの高さ・配置が未確認";Rejected?.Record("Simple Tweaks / AdditionalItemInfo",null,Status,row->NodeText.AsSpan()); return; }
            var bytes = row->NodeText.AsSpan();
            if (bytes.Length is 0 or > 32768) { content.Set(0, null); return; }
            var copy = bytes.ToArray();
            saved = new(key.Id, address, (nint)row, (nint)surface, (nint)footer, before, after, copy);
            Apply(addon, surface, footer, before); row->AtkResNode.ToggleVisibility(false);
            content.Set(0, copy); Captures++; Status = "専用追加情報を取得・元ノードを分離済み";
        }
        catch (Exception ex) { content.Clear(); stopped = true; Status = "分離停止: " + ex.GetBaseException().Message; }
    }
    public byte[]? Get(ulong id, nint address) => allowed() ? content.Get(id, address) : null;
    // Presentation may be cleared while restoration is still pending.
    public void Clear()=>content.Clear();
    public void Invalidate(){Clear();saved=null;}
    public bool Restore()
    {
        if(saved is not {} s)return true;
        var key=context();
        if(key.Id!=s.Item||key.Addon!=s.Addon){saved=null;return true;}
        if(!nativeCompatible()){Status="復元保留: ネイティブ互換性なし";return false;}
        var addon=(AtkUnitBase*)key.Addon;
        if(!Nodes(addon,out var row,out var surface,out var footer)||(nint)row!=s.Node||
            (nint)surface!=s.Surface||(nint)footer!=s.Footer)
        {Status="復元保留: 所有ノードが変更";return false;}
        if(row->AtkResNode.IsVisible()||!row->NodeText.AsSpan().SequenceEqual(s.Bytes))
        {saved=null;return true;} // A newer provider result supersedes our hidden output.
        var current=Geometry(addon,surface,footer);
        var root=addon->WindowNode->Component->UldManager.RootNode;
        if(root==null||root->PrevSiblingNode==null||addon->RootNode==null||
            addon->WindowNode->Height!=current.Window||root->Height!=current.Window||
            root->PrevSiblingNode->Height!=current.Window||addon->RootNode->Height!=current.Window)
        {Status="復元保留: 背景高さの確定待ち";return false;}
        var price=NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,PriceAdapter.NodeId,out var matches);
        if(matches>1){Status="復元保留: 価格ノード重複";return false;}
        PriceGeometry? visiblePrice=null;
        if(price!=null&&price->IsVisible())
        {
            if(price->Type!=NodeType.Text||price->ParentNode!=footer->ParentNode){Status="復元保留: 価格構造が変更";return false;}
            visiblePrice=new(current.Window,price->Height,price->Y,current.Footer);
        }
        if(!SimpleRestorePlan.TryCreate(s.Before,s.After,current,row->AtkResNode.Height,row->AtkResNode.Y,visiblePrice,out var plan))
        {Status="復元保留: 配置が変更";return false;}
        var oldPriceY=price==null?0:price->Y;
        void ApplyFrame(SimpleGeometry layout)
        {
            Apply(addon,surface,footer,layout);
            addon->WindowNode->SetHeight(layout.Window);root->SetHeight(layout.Window);
            root->PrevSiblingNode->SetHeight(layout.Window);addon->RootNode->SetHeight(layout.Window);
        }
        try
        {
            ApplyFrame(plan.Layout);
            if(plan.PriceY is {} y)price->SetYFloat(y);
            row->AtkResNode.ToggleVisibility(true);
        }
        catch
        {
            try{ApplyFrame(current);if(plan.PriceY!=null)price->SetYFloat(oldPriceY);row->AtkResNode.ToggleVisibility(false);}catch{}
            throw;
        }
        saved=null;
        restoredLayout(new(new(current.Window,current.Footer),new(plan.Layout.Window,plan.Layout.Footer)));
        Status="元の専用追加情報へ復元済み";return true;
    }
    private void Detach()
    {
        if (subscriber != null && Equals(methodProperty!.GetValue(subscriber), wrapper)) methodProperty.SetValue(subscriber, originalMethod);
        subscriber = null; methodProperty = null; wrapper = originalMethod = null;sourceInfo=null;
    }
    public void Dispose()
    {
        stopped = true;
        var cleanup = new Cleanup(); cleanup.Try("simple callback", Detach); cleanup.Try("simple restore",()=>{Restore();}); cleanup.Try("simple cache", Clear); cleanup.ThrowIfFailed();
    }
    public void Reconnect() { Dispose(); stopped = false;ConnectionFailed=false;OutputFailures=0; nextAttempt = 0; }
}
