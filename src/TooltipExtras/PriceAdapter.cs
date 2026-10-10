using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

// Version-specific inverse of PriceInsight 2.11.5.0 ItemPriceTooltip.UpdateItemTooltip.
// Only touches existing owned nodes. Never allocates, unlinks, or destroys native nodes.
internal sealed unsafe class PriceAdapter
{
    public const uint NodeId = 32612;
    private readonly ContextEpoch context = new();
    private Snapshot? snapshot;
    private byte[] bytes = [];
    private long drawnFrame = -1;
    private StructureProbe probe;
    private bool allowVisibleSimple;
    public string Structure => probe.ToString();
    private readonly record struct NodeProbe(bool Present, int Type, bool Visible, ushort Height, float Y,
        uint Previous, uint Next);
    private readonly record struct StructureProbe(int NodeCount, int PriceMatches, bool NativeLookupFound,
        NodeProbe Price, NodeProbe Footer, NodeProbe SimpleTweaks, bool SameParent,
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
            Describe(priceNode), Describe(footer), Describe(simple),
            priceNode != null && footer != null && priceNode->ParentNode == footer->ParentNode,
            window != null ? window->AtkResNode.Height : -1, window != null ? window->Height : -1,
            root != null ? root->Height : -1,
            root != null && root->PrevSiblingNode != null ? root->PrevSiblingNode->Height : -1,
            addon->RootNode != null ? addon->RootNode->Height : -1);
    }
    public string Status { get; private set; } = "待機中";
    public int Captures { get; private set; }
    public int Rejections { get; private set; }
    private sealed record Snapshot(nint Addon, nint Node, nint Window, nint ComponentRoot, nint Border, nint Root, nint Footer,
        ushort BeforeHeight, ushort AfterHeight, float BeforeFooter, float AfterFooter, TooltipKey Key, long Generation);

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
        return NativeNodeLookup.HasPriceFooterTopology(res, footer, simple, simple != null && simple->IsVisible(), allowVisibleSimple);
    }
    private static bool Identity(AtkUnitBase* addon, AtkTextNode* node, AtkResNode* root, AtkResNode* border, AtkResNode* footer, Snapshot s)
        => (nint)addon == s.Addon && (nint)node == s.Node && (nint)addon->WindowNode == s.Window
        && (nint)root == s.ComponentRoot && (nint)border == s.Border && (nint)addon->RootNode == s.Root && (nint)footer == s.Footer;
    private static bool HeightMatches(AtkUnitBase* addon, AtkResNode* root, AtkResNode* border, ushort height)
        => addon->WindowNode->AtkResNode.Height == height && addon->WindowNode->Height == height
        && root->Height == height && border->Height == height && addon->RootNode->Height == height;
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
        var generation = context.Observe(key, (nint)addon, addon != null && addon->IsVisible);
        if (generation == 0) { Clear(); return; }
        if (snapshot is { } previous && (!context.Matches(previous.Key, previous.Addon, previous.Generation)))
        { snapshot = null; bytes = []; }
        if (diagnostics) Probe(addon);
        if (!Resolve(addon, out var node, out var root, out var border, out var footer)) { Reject("所有ノード構造が未確認（詳細は診断JSONのPriceStructure）"); return; }
        if (!node->AtkResNode.IsVisible())
        {
            if (snapshot is { } s && Identity(addon, node, root, border, footer, s) && HeightMatches(addon, root, border, s.AfterHeight)
                && footer->Y == s.AfterFooter && node->NodeText.AsSpan().SequenceEqual(bytes))
            { drawnFrame = frame; Status = "価格情報を分離中"; }
            else { Reject("非表示ノードまたは復元後の状態が変更されたため待機"); }
            return;
        }
        // A visible node is a NEW producer result (including asynchronous price updates).
        snapshot = null; bytes = [];
        var shape = new PriceGeometry(addon->WindowNode->AtkResNode.Height, node->AtkResNode.Height, node->AtkResNode.Y, footer->Y);
        if (!shape.TryRemove(out var height, out var footerY) || !HeightMatches(addon, root, border, shape.Height))
        { Reject("高さ・配置が対応形状と不一致。他追記との干渉があり得るため変更しません"); return; }
        var content = node->NodeText.AsSpan();
        if (content.Length is 0 or > 32768) { Reject("追記テキストの長さが範囲外"); return; }
        bytes = content.ToArray(); // Owned copy; no native text pointer escapes this callback.
        snapshot = new((nint)addon, (nint)node, (nint)addon->WindowNode, (nint)root, (nint)border, (nint)addon->RootNode,
            (nint)footer, shape.Height, height, shape.FooterY, footerY, key, generation);
        SetHeight(addon, root, border, footer, height, footerY);
        node->AtkResNode.ToggleVisibility(false);
        Captures++; drawnFrame = frame; Status = "価格情報を分離中";
    }
    public bool TryPanel(AtkUnitBase* addon, TooltipKey key, long frame, out byte[] content, out Bounds bounds)
    {
        content = []; bounds = default;
        if (drawnFrame != frame || snapshot is not { } s || addon == null || !addon->IsVisible
            || !context.Matches(key, (nint)addon, s.Generation) || !Resolve(addon, out var node, out var root, out var border, out var footer)
            || !Identity(addon, node, root, border, footer, s) || node->AtkResNode.IsVisible()
            || !HeightMatches(addon, root, border, s.AfterHeight) || footer->Y != s.AfterFooter) return false;
        content = bytes;
        bounds = new(new Vector2(addon->X, addon->Y), new Vector2(addon->GetScaledWidth(true), addon->GetScaledHeight(true)));
        return bounds.Size.X > 0 && bounds.Size.Y > 0;
    }
    public void Restore(AtkUnitBase* liveAddon, TooltipKey liveKey)
    {
        // Resolve current native ownership before touching any stored address. Finalize uses Clear, never Restore.
        if (snapshot is { } s && context.Matches(liveKey, (nint)liveAddon, s.Generation)
            && Resolve(liveAddon, out var node, out var root, out var border, out var footer)
            && Identity(liveAddon, node, root, border, footer, s) && !node->AtkResNode.IsVisible()
            && HeightMatches(liveAddon, root, border, s.AfterHeight) && footer->Y == s.AfterFooter
            && node->NodeText.AsSpan().SequenceEqual(bytes))
        {
            SetHeight(liveAddon, root, border, footer, s.BeforeHeight, s.BeforeFooter);
            node->AtkResNode.ToggleVisibility(true);
        }
        Clear();
    }
    public void Clear() { snapshot = null; bytes = []; drawnFrame = -1; context.Invalidate(); }
    private void Reject(string reason) { Rejections++; Status = reason; drawnFrame = -1; }
}
