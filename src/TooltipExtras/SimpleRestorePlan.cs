namespace TooltipExtras;

internal readonly record struct SimpleRestorePlan(SimpleGeometry Layout,float? PriceY)
{
    public static bool TryCreate(SimpleGeometry before,SimpleGeometry after,SimpleGeometry live,
        ushort rowHeight,float rowY,PriceGeometry? visiblePrice,out SimpleRestorePlan plan)
    {
        plan=default;
        if(!before.IsAppend(after,rowHeight,rowY)||live.Surface!=live.Window||
            !PriceFooterState.Safe(before.Window,before.Footer,true)||!PriceFooterState.Safe(after.Window,after.Footer,true)||
            !PriceFooterState.Safe(live.Window,live.Footer,true))return false;
        if(live==before){plan=new(after,null);return true;}
        if(visiblePrice is not {} price||price.Height!=live.Window||price.FooterY!=live.Footer||
            !price.TryRemove(out var height,out var footer)||new SimpleGeometry(height,height,footer)!=before)return false;
        var expanded=live.Window+rowHeight;var expandedFooter=live.Footer+rowHeight;
        if(expanded>16000||!float.IsFinite(expandedFooter)||expandedFooter<0||expandedFooter>16000)return false;
        plan=new(new((ushort)expanded,(ushort)expanded,expandedFooter),price.NodeY+rowHeight);return true;
    }
}