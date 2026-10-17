using System.Collections;
using System.Reflection;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed class PanelImage(ISharedImmediateTexture texture,float width,float height)
{
    public readonly ISharedImmediateTexture Texture=texture;
    public readonly float Width=width,Height=height;
}
internal sealed unsafe class SimplePreviewAdapter : IDisposable
{
    private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private readonly string name;
    private readonly ITextureProvider textures;
    private readonly Func<bool> allowed,frameworkThread;
    private readonly Func<(ulong Id,nint Addon)> context;
    private readonly Action<LayoutChange>? restoredLayout;
    private object? subscriber,target;
    private PropertyInfo? property;
    private MethodInfo? originalMethod,wrapper;
    private uint nodeId;
    private bool stopped,ready,hidden,rowHidden,pendingLayout;
    private long nextAttempt;
    public PreviewWait Wait {get;}=new();
    private Saved? saved;
    private PreviewGeometry? lastBase;
    public PreviewGeometry? BaseGeometry=>lastBase;
    private PanelImage? image;
    private string path="";
    private sealed record Saved(ulong Item,nint Addon,nint Row,nint Surface,nint Footer,nint Root,nint Window,PreviewLayout Layout)
    {
        public PreviewGeometry Before=>Layout.Collapsed;
        public PreviewGeometry After=>Layout.Expanded;
        public PreviewGeometry Generated=>Layout.Generated;
    }
    public string Status {get;private set;}="待機中";
    public long Captures {get;private set;}
    public long Reflows {get;private set;}
    public long RequestsPreserved {get;private set;}
    public string LayoutProbe {get;private set;}="未捕捉";
    public uint KnownNodeId=>nodeId;
    public string GeometryProbe {get;private set;}="未捕捉";
    public SimplePreviewAdapter(string name,ITextureProvider textures,Func<bool> allowed,Func<bool> frameworkThread,Func<(ulong,nint)> context,Action<LayoutChange>? restoredLayout=null)
    {this.name=name;this.textures=textures;this.allowed=allowed;this.frameworkThread=frameworkThread;this.context=context;this.restoredLayout=restoredLayout;}
    public void Refresh(IDalamudPluginInterface pi,bool enabled)
    {
        if(!enabled){if(subscriber!=null)Dispose();else Clear();stopped=false;Status="無効";return;}
        if(stopped||Environment.TickCount64<nextAttempt)return;nextAttempt=Environment.TickCount64+1000;
        try
        {
            if(!pi.InstalledPlugins.Any(p=>p.InternalName=="SimpleTweaksPlugin"&&p.IsLoaded)){Dispose();stopped=false;Status="未ロード";return;}
            var a=ForeignPlugin.SimpleAssembly(pi);
            var events=a.GetType("SimpleTweaksPlugin.Events.EventController",true)!;
            var groups=(IDictionary)events.GetProperty("AddonEventSubscribers",Flags)!.GetValue(null)!;var found=new List<object>();
            foreach(IDictionary addons in groups.Values)if(addons["ItemDetail"] is IEnumerable entries)foreach(var entry in entries)
            {
                var tweak=entry.GetType().GetProperty("Tweak")!.GetValue(entry)!;
                if(tweak.GetType().FullName=="SimpleTweaksPlugin.Tweaks.Tooltips."+name)found.Add(entry);
            }
            if(found.Count!=1){Dispose();stopped=false;Status="元プレビューのイベント未登録";return;}
            var selected=found[0];var source=selected.GetType().GetProperty("Tweak")!.GetValue(selected)!;
            if(source.GetType().GetProperty("Enabled")!.GetValue(source) is not true){Dispose();stopped=false;Status="元機能が無効";return;}
            if(ReferenceEquals(selected,subscriber)&&Equals(property!.GetValue(subscriber),wrapper))return;
            Dispose();stopped=false;
            var p=selected.GetType().GetProperty("Method")!;var original=(MethodInfo)p.GetValue(selected)!;
            if(original.Name!="AfterItemDetailUpdate"||original.GetParameters().Length!=1||original.GetParameters()[0].ParameterType.GetElementType()?.FullName!=typeof(AtkUnitBase).FullName)throw new NotSupportedException("画像生成イベント署名不一致");
            nodeId=name=="PaintingPreview"?0x5354000Au:0u;
            var nativeAssembly=original.GetParameters()[0].ParameterType.GetElementType()!.Assembly;
            var contracts=CompatibilityContract.Load();
            foreach(var group in new[]{"PriceNative","PreviewNative","BodyBounds"})
            {
                var abi=CompatibilityContract.Check(contracts,group,(assembly,type)=>assembly=="FFXIVClientStructs"?nativeAssembly.GetType(type):typeof(IDalamudPluginInterface).Assembly.GetType(type));
                if(!abi.Allowed)throw new NotSupportedException("元画像ABI: "+abi.Reason);
            }
            var invoke=PointerCallback.Forward(source,original);var replacement=PointerCallback.Create(original.GetParameters()[0].ParameterType,address=>Intercept(invoke,address));
            subscriber=selected;target=source;property=p;originalMethod=original;wrapper=replacement;property.SetValue(selected,replacement);Status="画像生成へ接続 / 待機";
        }
        catch(Exception ex){stopped=true;Status="画像分離停止: "+ex.GetBaseException().Message;Detach();}
    }
    public void ObserveBase(ulong item,nint address)
    {
        if(stopped||subscriber==null||!allowed()||!frameworkThread()||context()!=(item,address))return;
        var a=(AtkUnitBase*)address;
        if(!Nodes(a,KnownNodeId,out _,out var surface,out var footer))return;
        var geometry=Geometry(a,surface,footer);
        if(geometry.Window<24||geometry.Window>16000)return;
        // Diagnostic only: native layout can still belong to the previous item here.
        lastBase=geometry;

    }    private uint Id()
    {
        if(nodeId!=0)return nodeId;
        var custom=target!.GetType().Assembly.GetType("SimpleTweaksPlugin.Utility.CustomNodes",true)!;
        var ids=(IDictionary)custom.GetField("NodeIds",Flags)!.GetValue(null)!;
        return ids[name+"#0"] is uint id?nodeId=id:0;
    }
    private static bool Nodes(AtkUnitBase* a,uint id,out AtkImageNode* row,out AtkResNode* surface,out AtkResNode* footer)
    {
        row=null;surface=footer=null;if(a==null||a->WindowNode==null||a->WindowNode->Component==null||a->RootNode==null)return false;
        var manager=&a->WindowNode->Component->UldManager;
        surface=NativeNodeLookup.FindUnique(manager->NodeList,manager->NodeListCount,2,out _);
        footer=NativeNodeLookup.FindUnique(a->UldManager.NodeList,a->UldManager.NodeListCount,2,out _);
        var n=NativeNodeLookup.FindUnique(a->UldManager.NodeList,a->UldManager.NodeListCount,id,out _);
        if(n!=null&&n->Type==NodeType.Image)row=(AtkImageNode*)n;
        return surface!=null&&footer!=null;
    }
    private static PreviewGeometry Geometry(AtkUnitBase* a,AtkResNode* surface,AtkResNode* footer)=>new(a->WindowNode->AtkResNode.Height,surface->Height,a->RootNode->Height,footer->Y);
    private static void Apply(AtkUnitBase* a,AtkResNode* surface,AtkResNode* footer,PreviewGeometry value)
        {
        a->WindowNode->SetHeight(value.Window);a->WindowNode->AtkResNode.SetHeight(value.Window);
        var root=a->WindowNode->Component->UldManager.RootNode;
        root->SetHeight(value.Window);root->PrevSiblingNode->SetHeight(value.Window);
        surface->SetHeight(value.Surface);a->RootNode->SetHeight(value.Root);footer->SetYFloat(value.Footer);
    }
    private static bool CompletedFrame(AtkUnitBase* a,ushort height)
    {
        var root=a->WindowNode->Component->UldManager.RootNode;
        return root!=null&&root->PrevSiblingNode!=null&&a->WindowNode->Height==height
            &&root->Height==height&&root->PrevSiblingNode->Height==height;
    }
    private void Intercept(Action<nint> original,nint address)
    {
        if(stopped||!allowed()||!frameworkThread()){original(address);return;}
        var key=context();var a=(AtkUnitBase*)address;
        if(key.Id==0||key.Addon!=address){original(address);return;}
        var valid=Nodes(a,Id(),out _,out var surface,out var footer);var before=valid?Geometry(a,surface,footer):default;
        original(address);
        try
        {
            if(!valid||context()!=key||!allowed()||!Nodes(a,Id(),out var row,out var nowSurface,out var nowFooter)||surface!=nowSurface||footer!=nowFooter||row==null||!row->AtkResNode.IsVisible()){Clear();return;}
            var after=Geometry(a,surface,footer);
            GeometryProbe=$"Before={before}; After={after}; RowY={row->AtkResNode.Y}; RowHeight={row->AtkResNode.Height}; SameParent={row->AtkResNode.ParentNode==footer->ParentNode}";
            var res=&row->AtkResNode;
            if(!after.IsGeneratedAfter(before,res->Y,res->Height,name=="PaintingPreview"?8:16)||res->Width==0||res->Width>2048||res->ParentNode!=footer->ParentNode)
            {Clear();Status="元画像を維持: プレビュー配置が未確認";return;}
            var resolved=target!.GetType().GetField("lastImage",Flags)!.GetValue(target);
            var next=name=="PaintingPreview"&&resolved is int number?$"ui/icon/{number/1000*1000:000000}/{number:000000}.tex":resolved as string;
            if(string.IsNullOrEmpty(next)||!next.StartsWith("ui/icon/",StringComparison.Ordinal)||!next.EndsWith(".tex",StringComparison.Ordinal)){Clear();Status="元画像を維持: 解決済み画像情報がありません";return;}
            if(path!=next){path=next;image=new(textures.GetFromGame(next),res->Width*a->Scale,res->Height*a->Scale);ready=false;}
            else if(image!=null&&(image.Width!=res->Width*a->Scale||image.Height!=res->Height*a->Scale))image=new(image.Texture,res->Width*a->Scale,res->Height*a->Scale);
            saved=new(key.Id,address,(nint)row,(nint)surface,(nint)footer,(nint)a->RootNode,(nint)a->WindowNode,new(before,after,after,before));hidden=false;
            // Hide the provider image before any draw, independently of TE texture readiness.
            if(row->AtkResNode.IsVisible())row->AtkResNode.ToggleVisibility(false);rowHidden=true;pendingLayout=true;
            Wait.Begin(key.Id,address,Environment.TickCount64);
            Status="元画像を非表示 / 配置の確定待ち";
            // Collapse geometry only at PreDraw, after component/background heights agree.
        }
        catch(Exception ex){stopped=true;Status="画像分離停止: "+ex.GetBaseException().Message;}
    }
    public void PreDraw(AtkUnitBase* a,ulong item)
    {
        if(stopped)return;

        if(!allowed()||saved is not {} s||s.Item!=item||s.Addon!=(nint)a)return;
        if(!Match(a,s,out var row,out var surface,out var footer))
        {pendingLayout=false;Wait.Complete(Environment.TickCount64);Status="画像分離停止: 画像ノードが変更されました";return;}
        // A later producer may have shown the same owned node again.
        if(row->AtkResNode.IsVisible())row->AtkResNode.ToggleVisibility(false);rowHidden=true;
        var current=Geometry(a,surface,footer);
        var completed=CompletedFrame(a,current.Window);
        if(hidden&&completed&&current==s.Before){pendingLayout=false;Wait.Complete(Environment.TickCount64);return;}
        if(hidden&&completed&&UnwrapPrice(a,footer,current,out var priceBase,out _,out var visibleDelta)&&
            s.Layout.TryFollowVisiblePrice(priceBase,visibleDelta,out var tracked)&&tracked.Collapsed==current)
        {
            saved=s with {Layout=tracked};Reflows++;pendingLayout=false;Wait.Complete(Environment.TickCount64);
            Status="価格の再生成に合わせて分離配置を更新";return;
        }
        var wasHidden=hidden;
        if(!completed||!UnwrapPrice(a,footer,current,out var underlying,out var priceRow,out var priceDelta)||!underlying.TryCollapseImage(s.Generated,s.Layout.SourceBefore,row->AtkResNode.Y,row->AtkResNode.Height,name=="PaintingPreview"?8:16,name=="PaintingPreview",out var collapsed))
        {
            LayoutProbe=$"Generated={s.Generated}; Current={current}; Target={s.Before}; RowY={row->AtkResNode.Y}; RowHeight={row->AtkResNode.Height}; Hidden={hidden}; Completed={completed}";
            pendingLayout=true;Status="元画像は非表示 / 生成後の配置が未対応";return;
        }
        if(!NativeBodyBounds.TryMinimum(a->UldManager.NodeList,a->UldManager.NodeListCount,a->RootNode,footer,out var minimum))
        {pendingLayout=true;Status="元画像は非表示 / 本文範囲の確認待ち";return;}
        var previewMargin=name=="PaintingPreview"?8:16;collapsed=NativeBodyBounds.Fit(collapsed,minimum,previewMargin);
        if(collapsed.Window>=s.Generated.Window)
        {pendingLayout=true;Status="元画像は非表示 / 本文を保護して縮小待ち";return;}
        if(!LayoutChange.Shift(collapsed,priceDelta,out collapsed))
        {pendingLayout=true;Status="元画像は非表示 / 価格を含む配置の確認待ち";return;}
        LayoutProbe=$"Generated={s.Generated}; Current={current}; Collapsed={collapsed}; NativeMinimum={minimum}; RemovedPreviewMargin={previewMargin}; VisiblePriceDelta={priceDelta}; WasHidden={wasHidden}";
        s=s with {Layout=s.Layout with {Expanded=current,Collapsed=collapsed,VisiblePriceDelta=priceDelta}};saved=s;
        try
        {
            if(priceRow!=null)priceRow->SetYFloat(priceRow->Y+s.Before.Window-current.Window); Apply(a,surface,footer,s.Before);row->AtkResNode.ToggleVisibility(false);hidden=rowHidden=true;pendingLayout=false;Wait.Complete(Environment.TickCount64);
            if(wasHidden)Reflows++;else Captures++;
            Status=ready?"元のプレビュー画像を分離済み":"配置分離済み / TE画像の準備待ち";
        }
        catch(Exception ex)
        {
            try{if(priceRow!=null)priceRow->SetYFloat(underlying.Window-8);Apply(a,surface,footer,s.After);row->AtkResNode.ToggleVisibility(true);}catch{}
            stopped=true;hidden=rowHidden=pendingLayout=false;Wait.Complete(Environment.TickCount64);Status="元画像の配置を復元して停止: "+ex.GetBaseException().Message;
        }
    }    private bool Match(AtkUnitBase* a,Saved s,out AtkImageNode* row,out AtkResNode* surface,out AtkResNode* footer)
        => Nodes(a,Id(),out row,out surface,out footer)&&row!=null&&(nint)row==s.Row&&(nint)surface==s.Surface&&(nint)footer==s.Footer&&(nint)a->RootNode==s.Root&&(nint)a->WindowNode==s.Window;
    public PanelImage? Get(ulong item,nint addon)
    {
        if(!allowed()||saved is not {} s||s.Item!=item||s.Addon!=addon||image==null)return null;
        ready=image.Texture.TryGetWrap(out _,out var error);
        if(error!=null)Status="画像は非表示 / TE画像の準備に失敗";
        if(ready&&hidden&&!pendingLayout)Status="元のプレビュー画像を分離済み"; return ready&&rowHidden?image:null;
    }
    public bool SuppressDraw(ulong item,nint addon,long now)
        =>!stopped&&allowed()&&pendingLayout&&saved is {} s&&s.Item==item&&s.Addon==addon&&Wait.ShouldSuppress(now);
    // Peel only a currently visible, exactly validated price append. It remains
    // visible when price separation is OFF and must move along with image removal.
    private static bool UnwrapPrice(AtkUnitBase* a,AtkResNode* footer,PreviewGeometry current,out PreviewGeometry underlying,out AtkResNode* price,out int delta)
    {
        underlying=current;price=null;delta=0;
        var node=NativeNodeLookup.FindUnique(a->UldManager.NodeList,a->UldManager.NodeListCount,PriceAdapter.NodeId,out var matches);
        if(matches>1)return false;
        if(node==null||!node->IsVisible())return true;
        if(node->Type!=NodeType.Text||node->ParentNode!=footer->ParentNode||
            current.Surface!=current.Window||current.Root!=current.Window||
            !new PriceGeometry(current.Window,node->Height,node->Y,current.Footer).TryRemove(out var height,out var y)||y<0||y>height)return false;
        delta=current.Window-height;underlying=new(height,height,height,y);price=node;return true;
    }
    public void FollowPriceRestore(LayoutChange change)
    {
        if(!hidden||saved is not {} s||!s.Layout.TryFollowPriceRestore(change,out var next))return;
        var key=context();
        if(key!=(s.Item,s.Addon)||!Match((AtkUnitBase*)key.Addon,s,out var row,out var surface,out var footer)||
            row->AtkResNode.IsVisible()||Geometry((AtkUnitBase*)key.Addon,surface,footer)!=next.Collapsed||
            !CompletedFrame((AtkUnitBase*)key.Addon,next.Collapsed.Window))return;
        saved=s with {Layout=next};Status="価格の復元に合わせて分離配置を更新";
    }
    public void Requested(ulong item,nint addon)
    {
        if(saved is {} s&&s.Item==item&&s.Addon==addon)
        {
            // The source overwrites image geometry at PostRequestedUpdate. Keeping
            // the existing hidden image avoids a temporary expanded window here.
            RequestsPreserved++;return;
        }
        else {Restore();Clear();}
    }
    public void Restore()=>Restore(true);
    private void Restore(bool showImage)
    {
        if(saved is {} s&&rowHidden)
        {
            var key=context();
            if(key!=(s.Item,s.Addon)){Clear();return;}
            if(!Match((AtkUnitBase*)key.Addon,s,out var row,out var surface,out var footer))
            {Status="画像復元を保留: 所有ノードが変更";return;}
            var a=(AtkUnitBase*)key.Addon;var current=Geometry(a,surface,footer);
            if(hidden&&current!=s.Before&&UnwrapPrice(a,footer,current,out var underlying,out _,out var priceDelta)&&
                s.Layout.TryFollowVisiblePrice(underlying,priceDelta,out var tracked)&&tracked.Collapsed==current)
            {s=s with {Layout=tracked};saved=s;}
            if(hidden)
            {
                if(current!=s.Before||!CompletedFrame(a,s.Before.Window)||
                    !UnwrapPrice(a,footer,current,out _,out var priceRow,out _))
                {Status="画像復元を保留: 配置が変更";return;}
                var oldY=priceRow==null?0:priceRow->Y;
                try
                {
                    if(priceRow!=null)priceRow->SetYFloat(oldY+s.After.Window-current.Window);
                    Apply(a,surface,footer,s.After);
                    restoredLayout?.Invoke(new(new(current.Window,current.Footer),new(s.After.Window,s.After.Footer)));
                }
                catch
                {
                    try{Apply(a,surface,footer,current);if(priceRow!=null)priceRow->SetYFloat(oldY);}catch{}
                    throw;
                }
            }
            if(!row->AtkResNode.IsVisible())row->AtkResNode.ToggleVisibility(true);
        }
        hidden=pendingLayout=false;
        rowHidden=false;saved=null;Wait.Clear();
    }
    public void Clear(){saved=null;image=null;path="";ready=hidden=rowHidden=pendingLayout=false;Wait.Clear();}
    private void Detach(){if(subscriber!=null&&Equals(property!.GetValue(subscriber),wrapper))property.SetValue(subscriber,originalMethod);subscriber=target=null;property=null;originalMethod=wrapper=null;}
    public void Dispose(){stopped=true;var cleanup=new Cleanup();cleanup.Try("preview event",Detach);cleanup.Try("preview restore",Restore);cleanup.Try("preview cache",Clear);cleanup.ThrowIfFailed();}
    public void Reconnect(){Dispose();stopped=false;nextAttempt=0;}
}
