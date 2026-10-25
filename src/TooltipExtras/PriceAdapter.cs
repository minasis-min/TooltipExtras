using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

// Version-specific inverse of PriceInsight 2.11.5.0 ItemPriceTooltip.UpdateItemTooltip.
// Only touches existing owned nodes. Never allocates, unlinks, or destroys native nodes.
internal sealed unsafe class PriceAdapter
{
    public RejectedTextLog? Rejected {get;set;}
    public const uint NodeId = 32612;
    private readonly ContextEpoch context = new();
    private Snapshot? snapshot;
    private byte[] bytes = [];
    private long drawnFrame = -1;
    private LayoutStamp panelLayout;
    private StructureProbe probe;
    private bool allowVisibleSimple;
    private string? pendingIssue;
    private TooltipKey issueKey;
    private long issueFrame=-1;
    private uint unlockId,paintingId;
    public void SetKnownPreviewNodes(uint unlock,uint painting){unlockId=unlock;paintingId=painting;}
    public string Structure => probe.ToString();
    // Explicit read-only diagnostic request on the framework thread. No text,
    // character identity or native pointers are returned, and layout is untouched.
    public object InspectGeometry(AtkUnitBase* addon)
    {
        Probe(addon);
        ushort minimum=0;var measured=false;
        if(addon!=null&&addon->UldManager.NodeListCount<=1024&&addon->RootNode!=null)
        {
            var measuredFooter=NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,2,out _);
            measured=NativeBodyBounds.TryMinimum(addon->UldManager.NodeList,addon->UldManager.NodeListCount,addon->RootNode,measuredFooter,out minimum);
        }
        return new {Status,Captures,Rejections,Geometry=probe,BodyMeasured=measured,BodyMinimum=minimum,PendingRestore,PresentationOnly=snapshot?.PresentationOnly??false,
            RestoreLayoutVerified=snapshot is {} s&&Resolve(addon,out var node,out var root,out var border,out var footer)
                &&Identity(addon,node,root,border,footer,s)&&LayoutMatches(addon,root,border,footer,s)};
    }
    private readonly record struct NodeProbe(bool Present, int Type, bool Visible, ushort Height, float Y,
        uint Previous, uint Next);
    private readonly record struct StructureProbe(int NodeCount, int PriceMatches, bool NativeLookupFound,
        NodeProbe Price, NodeProbe Footer, NodeProbe SimpleTweaks, NodeProbe Hint, bool SameParent,
        int WindowHeight, int WindowComponentHeight, int ComponentRootHeight, int BorderHeight, int AddonRootHeight);
    private static NodeProbe Describe(AtkResNode* node) => node == null ? default :
        new(true, (int)node->Type, node->IsVisible(), node->Height, node->Y,
            node->PrevSiblingNode != null ? node->PrevSiblingNode->NodeId : 0,
            node->NextSiblingNode != null ? node->NextSiblingNode->NodeId : 0);
    private void Probe(AtkUnitBase* addon)
    {
        if (addon == null || addon->UldManager.NodeListCount > 1024) { probe = default; return; }
        var priceNode = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, NodeId, out var matches);
        var simple = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, 0x5354000B, out _);
        var footer = addon->GetNodeById(2);
        var window = addon->WindowNode;
        var root = window != null && window->Component != null ? window->Component->UldManager.RootNode : null;
        probe = new(addon->UldManager.NodeListCount, matches, addon->GetNodeById(NodeId) != null,
            Describe(priceNode), Describe(footer), Describe(simple), Describe(NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,3,out _)),
            priceNode != null && footer != null && priceNode->ParentNode == footer->ParentNode,
            window != null ? window->AtkResNode.Height : -1, window != null ? window->Height : -1,
            root != null ? root->Height : -1,
            root != null && root->PrevSiblingNode != null ? root->PrevSiblingNode->Height : -1,
            addon->RootNode != null ? addon->RootNode->Height : -1);
    }
    private readonly Queue<RejectionSample> rejectionHistory=new();
    private RejectionSample? lastRejection;
    private bool traceEnabled,attemptDormant;
    private TooltipKey attemptKey;
    private long attemptFrame;
    private PriceGeometry attemptShape;
    private ushort attemptMinimum;
    private readonly record struct RejectionSample(long Frame,ulong Item,string Reason,PriceGeometry Shape,bool HintsDormant,ushort BodyMinimum);
    public object RejectionSnapshot()=>rejectionHistory.ToArray();
    public string Status { get; private set; } = "待機中";
    public bool PendingRestore=>snapshot!=null;
    public long RequestsPreserved {get;private set;}
    public long Restores {get;private set;}
    public void Requested(AtkUnitBase* addon,TooltipKey key)
    {
        drawnFrame=-1;
        if(snapshot is {} s&&context.Matches(key,(nint)addon,s.Generation))
        {
            // The audited provider regenerates this append after RequestedUpdate.
            // Keep it hidden instead of expanding the window before native layout.
            RequestsPreserved++;return;
        }
        // A different target must never reuse the previous target's price.
        Clear();
    }
    public int Captures { get; private set; }
    public int Rejections { get; private set; }
    private sealed record Snapshot(nint Addon, nint Node, nint Window, nint ComponentRoot, nint Border, nint Root, nint Footer,
        ushort BeforeHeight, ushort AfterHeight, float BeforeFooter, float AfterFooter, TooltipKey Key, long Generation,bool DormantFooter,bool PresentationOnly=false);

    private bool Resolve(AtkUnitBase* addon, out AtkTextNode* node, out AtkResNode* root, out AtkResNode* border, out AtkResNode* footer)
    {
        node = null; root = border = footer = null;
        if (addon == null || addon->RootNode == null || addon->WindowNode == null || addon->WindowNode->Component == null
            || addon->UldManager.NodeListCount > 1024) return false;
        var res = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, NodeId, out _);
        footer = addon->GetNodeById(2);
        if (res == null || res->Type != NodeType.Text || footer == null) return false;
        node = (AtkTextNode*)res;
        root = addon->WindowNode->Component->UldManager.RootNode;
        if (root == null || root->PrevSiblingNode == null || res->ParentNode != footer->ParentNode) return false;
        border = root->PrevSiblingNode;
        // Also accept the verified hidden Simple Tweaks node retained between price and footer.
        var simple = res->NextSiblingNode != footer
            ? NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, 0x5354000B, out _)
            : null;
                var unlock=unlockId==0?null:NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,unlockId,out _);
        var painting=paintingId==0?null:NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,paintingId,out _);
        return NativeNodeLookup.HasPriceFooterTopology(res, footer, simple, simple != null && simple->IsVisible(), allowVisibleSimple,
            unlock,unlock!=null&&unlock->IsVisible(),painting,painting!=null&&painting->IsVisible());
    }
    private static bool Identity(AtkUnitBase* addon, AtkTextNode* node, AtkResNode* root, AtkResNode* border, AtkResNode* footer, Snapshot s)
        => (nint)addon == s.Addon && (nint)node == s.Node && (nint)addon->WindowNode == s.Window
        && (nint)root == s.ComponentRoot && (nint)border == s.Border && (nint)addon->RootNode == s.Root && (nint)footer == s.Footer;
    private static bool HeightMatches(AtkUnitBase* addon, AtkResNode* root, AtkResNode* border, ushort height)
        => addon->WindowNode->AtkResNode.Height == height && addon->WindowNode->Height == height
        && root->Height == height && border->Height == height && addon->RootNode->Height == height;
    private bool LayoutMatches(AtkUnitBase* addon,AtkResNode* root,AtkResNode* border,AtkResNode* footer,Snapshot s)
        =>s.PresentationOnly||HeightMatches(addon,root,border,panelLayout.Height)&&footer->Y==panelLayout.Footer;
    private static void SetHeight(AtkUnitBase* addon, AtkResNode* root, AtkResNode* border, AtkResNode* footer, ushort height, float y)
    {
        addon->WindowNode->SetHeight(height);
        addon->WindowNode->AtkResNode.SetHeight(height);
        root->SetHeight(height); border->SetHeight(height); addon->RootNode->SetHeight(height);
        footer->SetYFloat(y);
    }
    public void PreDraw(AtkUnitBase* addon, TooltipKey key, long frame, bool diagnostics, bool visibleSimpleAllowed)
    {
        drawnFrame = -1;
        allowVisibleSimple = visibleSimpleAllowed;
        traceEnabled=diagnostics;attemptKey=key;attemptFrame=frame;attemptShape=default;attemptDormant=false;attemptMinimum=0;
        var generation = context.Observe(key, (nint)addon, addon != null && addon->IsVisible);
        if (generation == 0) { Clear(); return; }
        if (snapshot is { } previous && (!context.Matches(previous.Key, previous.Addon, previous.Generation)))
        { snapshot = null; bytes = []; }
        if (diagnostics) Probe(addon);
        if (!Resolve(addon, out var node, out var root, out var border, out var footer)) { Reject("所有ノード構造が未確認（詳細は診断JSONのPriceStructure）"); return; }
        if (!node->AtkResNode.IsVisible())
        {
            if (snapshot is { } s && Identity(addon, node, root, border, footer, s)
                && node->NodeText.AsSpan().SequenceEqual(bytes))
            {
                drawnFrame = frame;
                Status=LayoutMatches(addon,root,border,footer,s)?"価格情報を分離中":"価格情報を分離中 / 配置変更のため高さの復元を保留";
            }
            else { Reject("非表示ノードまたは復元後の状態が変更されたため待機"); }
            return;
        }
        // A visible node is a NEW producer result (including asynchronous price updates).
        snapshot = null; bytes = [];
        var shape = new PriceGeometry(addon->WindowNode->AtkResNode.Height, node->AtkResNode.Height, node->AtkResNode.Y, footer->Y);
        var hintsDormant=PriceFooterState.HintsDormant(addon->UldManager.NodeList,addon->UldManager.NodeListCount,footer);
        attemptShape=shape;attemptDormant=hintsDormant;
        if(!NativeBodyBounds.TryMinimum(addon->UldManager.NodeList,addon->UldManager.NodeListCount,addon->RootNode,footer,out attemptMinimum))
        {Reject("本文より小さい高さへの価格分離を保留");Rejected?.Record("Price Insight",null,Status,node->NodeText.AsSpan());return;}
        if(!PriceCapturePlan.TryCreate(shape,addon->WindowNode->Height,root->Height,border->Height,addon->RootNode->Height,
            attemptMinimum,hintsDormant,out var plan,out var reason))
        {Reject(reason);Rejected?.Record("Price Insight",null,Status,node->NodeText.AsSpan());return;}
        var content = node->NodeText.AsSpan();
        if (content.Length is 0 or > 32768) { Reject("追記テキストの長さが範囲外"); return; }
        bytes = content.ToArray(); // Owned copy; no native text pointer escapes this callback.
        snapshot = new((nint)addon, (nint)node, (nint)addon->WindowNode, (nint)root, (nint)border, (nint)addon->RootNode,
            (nint)footer, shape.Height, plan.Height, shape.FooterY, plan.Footer, key, generation,hintsDormant,plan.PresentationOnly);
        panelLayout=new(plan.Height,plan.Footer);
        if(!plan.PresentationOnly)SetHeight(addon, root, border, footer, plan.Height, plan.Footer);
        node->AtkResNode.ToggleVisibility(false);
        Captures++; drawnFrame = frame; Status = "価格情報を分離中";
    }
    public void FollowLayout(LayoutStamp before,LayoutStamp after)
    {
        if(snapshot is {PresentationOnly:false}&&panelLayout.TryFollow(before,after,out var next))panelLayout=next;
    }
    // Only called after the preview adapter has validated and corrected a native
    // reflow of its already hidden image. Never adopt arbitrary external geometry.
    public void FollowVerifiedPreviewReflow(AtkUnitBase* addon,TooltipKey key,long frame)
    {
        if(snapshot is not {} s||!context.Matches(key,(nint)addon,s.Generation)
            ||!Resolve(addon,out var node,out var root,out var border,out var footer)
            ||!Identity(addon,node,root,border,footer,s)||node->AtkResNode.IsVisible()
            ||!node->NodeText.AsSpan().SequenceEqual(bytes))return;
        var height=addon->WindowNode->AtkResNode.Height;
        if(height<24||!HeightMatches(addon,root,border,height)||!float.IsFinite(footer->Y)||footer->Y<0||footer->Y>height)return;
        panelLayout=new(height,footer->Y);drawnFrame=frame;Status="価格情報を分離中";
    }
    public bool TryPanel(AtkUnitBase* addon, TooltipKey key, long frame, out byte[] content, out Bounds bounds)
    {
        content = []; bounds = default;
        if (drawnFrame != frame || snapshot is not { } s || addon == null || !addon->IsVisible
            || !context.Matches(key, (nint)addon, s.Generation) || !Resolve(addon, out var node, out var root, out var border, out var footer)
            || !Identity(addon, node, root, border, footer, s) || node->AtkResNode.IsVisible()
            || !node->NodeText.AsSpan().SequenceEqual(bytes)) return false;
        content = bytes;
        bounds = new(new Vector2(addon->X, addon->Y), new Vector2(addon->GetScaledWidth(true), addon->GetScaledHeight(true)));
        return bounds.Size.X > 0 && bounds.Size.Y > 0;
    }
    // Only the final native PreDraw attempt participates. Intermediate layout
    // phases and absent provider output must not produce chat notifications.
    public string? PresentationIssue(AtkUnitBase* addon,TooltipKey key,long frame)
    {
        var node=addon==null?null:NativeNodeLookup.FindUnique(addon->UldManager.NodeList,addon->UldManager.NodeListCount,NodeId,out _);
        var captured=snapshot is {} s&&context.Matches(key,(nint)addon,s.Generation)&&(nint)node==s.Node;
        // The provider retains its hidden node even when this item has no price.
        // Only visible source output or a current TE-owned capture can fail.
        if(drawnFrame==frame||node==null||!node->IsVisible()&&!captured)
        {pendingIssue=null;issueFrame=-1;return null;}
        var repeated=pendingIssue==Status&&issueKey==key&&issueFrame>=0&&issueFrame<frame;
        pendingIssue=Status;issueKey=key;issueFrame=frame;
        return repeated?Status:null;
    }
    public bool Restore(AtkUnitBase* liveAddon, TooltipKey liveKey)
    {
        if(snapshot is not {} s)return true;
        if(!context.Matches(liveKey,(nint)liveAddon,s.Generation)){Clear();return true;}
        if(!Resolve(liveAddon,out var node,out var root,out var border,out var footer)||
            !Identity(liveAddon,node,root,border,footer,s))
        {Status="価格復元を保留: 所有ノードが変更";return false;}
        // A visible node is already owned by a newer provider result.
        if(node->AtkResNode.IsVisible()){Clear();return true;}
        if(s.PresentationOnly)
        {
            if(!node->NodeText.AsSpan().SequenceEqual(bytes)){Status="価格復元を保留: 生成内容が変更";return false;}
            // This transfer never resized the tooltip. Undo visibility only;
            // neither cached nor inferred native geometry is written back.
            node->AtkResNode.ToggleVisibility(true);Restores++;Clear();return true;
        }
        if(!HeightMatches(liveAddon,root,border,panelLayout.Height)||footer->Y!=panelLayout.Footer||
            !node->NodeText.AsSpan().SequenceEqual(bytes)||
            !PriceRestorePlan.TryCreate(s.BeforeHeight,s.AfterHeight,s.BeforeFooter,s.AfterFooter,panelLayout,out var plan,s.DormantFooter&&PriceFooterState.HintsDormant(liveAddon->UldManager.NodeList,liveAddon->UldManager.NodeListCount,footer)))
        {Status="価格復元を保留: 配置・内容が変更";return false;}
        var oldY=node->AtkResNode.Y;
        try
        {
            SetHeight(liveAddon,root,border,footer,plan.Height,plan.Footer);
            node->AtkResNode.SetYFloat(plan.NodeY);node->AtkResNode.ToggleVisibility(true);
        }
        catch
        {
            try{SetHeight(liveAddon,root,border,footer,panelLayout.Height,panelLayout.Footer);node->AtkResNode.SetYFloat(oldY);node->AtkResNode.ToggleVisibility(false);}catch{}
            throw;
        }
        Restores++;Clear();return true;
    }
    public void FollowRestoredSimple(LayoutChange change)
    {
        if(snapshot is not {PresentationOnly:false} s||panelLayout!=change.Before)return;
        var delta=change.After.Height-change.Before.Height;
        if(delta<=0||Math.Abs(change.After.Footer-change.Before.Footer-delta)>0.01f||
            !PriceFooterState.Safe(change.Before.Height,change.Before.Footer,s.DormantFooter)||
            !PriceFooterState.Safe(change.After.Height,change.After.Footer,s.DormantFooter))return;
        panelLayout=change.After;
    }
    public void FollowRestoredPreview(LayoutChange change)
    {
        if(snapshot is not {PresentationOnly:false}||panelLayout!=change.Before||!change.TryDelta(out var delta)||delta<=0)return;
        panelLayout=change.After;
    }
    public void Clear() { snapshot = null; bytes = []; drawnFrame = -1;pendingIssue=null;issueFrame=-1; context.Invalidate(); }
    private void Reject(string reason)
    {
        Rejections++;Status=reason;drawnFrame=-1;
        if(!traceEnabled)return;
        var entry=new RejectionSample(attemptFrame,attemptKey.Id,reason,attemptShape,attemptDormant,attemptMinimum);
        var comparable=entry with {Frame=0};
        if(lastRejection==comparable)return;
        lastRejection=comparable;rejectionHistory.Enqueue(entry);
        while(rejectionHistory.Count>32)rejectionHistory.Dequeue();
    }
}
