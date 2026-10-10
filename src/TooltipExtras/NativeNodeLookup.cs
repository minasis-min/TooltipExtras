using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal static unsafe class NativeNodeLookup
{
    // Runtime-added nodes are resolved from the same current draw list the producer uses.
    // Only the active tooltip is inspected; addresses are never retained here.
    public static AtkResNode* FindUnique(AtkResNode** nodes, int count, uint id, out int matches)
    {
        matches = 0;
        if (nodes == null || count is < 1 or > 1024) return null;
        AtkResNode* found = null;
        for (var i = 0; i < count; i++)
        {
            var candidate = nodes[i];
            if (candidate == null || candidate->NodeId != id) continue;
            matches++;
            found = candidate;
        }
        return matches == 1 ? found : null;
    }
    public static bool HasPriceFooterTopology(AtkResNode* price, AtkResNode* footer,
        AtkResNode* simple, bool simpleVisible, bool allowVisibleSimple = false)
    {
        if (price == null || footer == null || price->NodeId != 32612 || price->Type != NodeType.Text
            || footer->NodeId != 2 || price->ParentNode == null || price->ParentNode != footer->ParentNode)
            return false;
        if (price->NextSiblingNode == footer && footer->PrevSiblingNode == price) return true;
        // Simple Tweaks retains a hidden node, or places its visible row physically above price.
        // For the pinned visible layout, Simple Tweaks uses H-10 and Price Insight uses H-8.
        // No Simple Tweaks geometry is modified; a different ordering is rejected.
        // The caller resolves this unique node from the live draw list before passing it.
        return simple != null && simple->NodeId == 0x5354000B && simple->Type == NodeType.Text
            && (!simpleVisible || allowVisibleSimple && simple->Height > 0
                && float.IsFinite(simple->Y) && float.IsFinite(price->Y)
                && Math.Abs(simple->Y - (price->Y - simple->Height - 2)) <= 0.01f)
            && simple->ParentNode == footer->ParentNode
            && price->NextSiblingNode == simple && simple->PrevSiblingNode == price
            && simple->NextSiblingNode == footer && footer->PrevSiblingNode == simple;
    }
}
