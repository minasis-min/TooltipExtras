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
    private static bool ImageAbovePrice(AtkResNode* image,AtkResNode* price)
        =>image->Height is >0 and <=2048&&float.IsFinite(image->Y)&&image->Y>=0&&float.IsFinite(price->Y)
        &&image->Y+image->Height<=price->Y;    public static bool HasPriceFooterTopology(AtkResNode* price, AtkResNode* footer,
        AtkResNode* simple, bool simpleVisible, bool allowVisibleSimple = false, AtkResNode* unlock = null, bool unlockVisible = true, AtkResNode* painting = null, bool paintingVisible = true)
    {
        if (price == null || footer == null || price->NodeId != 32612 || price->Type != NodeType.Text
            || footer->NodeId != 2 || price->ParentNode == null || price->ParentNode != footer->ParentNode)
            return false;
        if (price->NextSiblingNode == footer && footer->PrevSiblingNode == price) return true;
        var current=price->NextSiblingNode;
        var previous=price;
        var seenSimple=false;var seenUnlock=false;var seenPainting=false;
        for(var i=0;i<3&&current!=null&&current!=footer;i++)
        {
            if(current->ParentNode!=footer->ParentNode||current->PrevSiblingNode!=previous)return false;
            if(current==simple&&!seenSimple&&simple->NodeId==0x5354000B&&simple->Type==NodeType.Text)
            {
                if(simpleVisible&&!(allowVisibleSimple&&simple->Height>0&&float.IsFinite(simple->Y)&&float.IsFinite(price->Y)
                    &&Math.Abs(simple->Y-(price->Y-simple->Height-2))<=0.01f))return false;
                seenSimple=true;
            }
            else if(current==unlock&&!seenUnlock&&unlock->Type==NodeType.Image&&(!unlockVisible||ImageAbovePrice(unlock,price)))seenUnlock=true;
            else if(current==painting&&!seenPainting&&painting->NodeId==0x5354000A&&painting->Type==NodeType.Image&&(!paintingVisible||ImageAbovePrice(painting,price)))seenPainting=true;
            else return false;
            previous=current;current=current->NextSiblingNode;
        }
        return current==footer&&footer->PrevSiblingNode==previous;
    }
}
