namespace TooltipExtras;

// Geometry operations shared by the actual adapters and their state-transition checks.
internal readonly record struct LayoutChange(LayoutStamp Before,LayoutStamp After)
{
    public bool TryDelta(out int delta)
    {
        delta=After.Height-Before.Height;
        return Before.Height>=24&&After.Height>=24&&After.Height<=16000&&
            float.IsFinite(Before.Footer)&&float.IsFinite(After.Footer)&&
            Before.Footer>=0&&Before.Footer<=Before.Height&&After.Footer>=0&&After.Footer<=After.Height&&
            Math.Abs((After.Footer-Before.Footer)-delta)<0.01f;
    }
    public static bool Shift(PreviewGeometry value,int delta,out PreviewGeometry result)
    {
        result=default;var h=value.Window+delta;var s=value.Surface+delta;var r=value.Root+delta;var f=value.Footer+delta;
        if(h is <24 or >16000||s is <24 or >16000||r is <24 or >16000||!float.IsFinite(f)||f<0||f>h)return false;
        result=new((ushort)h,(ushort)s,(ushort)r,f);return true;
    }
}
internal readonly record struct PreviewLayout(PreviewGeometry SourceBefore,PreviewGeometry Generated,PreviewGeometry Expanded,PreviewGeometry Collapsed,int VisiblePriceDelta=0)
{
    public bool TryFollowVisiblePrice(PreviewGeometry underlying,int priceDelta,out PreviewLayout next)
    {
        next=this;
        if(priceDelta<0||VisiblePriceDelta<0||!LayoutChange.Shift(Collapsed,-VisiblePriceDelta,out var bare)||bare!=underlying||
            !LayoutChange.Shift(Collapsed,priceDelta-VisiblePriceDelta,out var compact)||
            !LayoutChange.Shift(Expanded,priceDelta-VisiblePriceDelta,out var expanded))return false;
        next=this with {Collapsed=compact,Expanded=expanded,VisiblePriceDelta=priceDelta};return true;
    }
    public bool TryFollowPriceRestore(LayoutChange change,out PreviewLayout next)
    {
        next=this;
        if(change.Before!=new LayoutStamp(Collapsed.Window,Collapsed.Footer)||!change.TryDelta(out var delta)||delta<=0||
            !LayoutChange.Shift(Collapsed,delta,out var compact)||!LayoutChange.Shift(Expanded,delta,out var expanded))return false;
        next=this with {Collapsed=compact,Expanded=expanded,VisiblePriceDelta=VisiblePriceDelta+delta};return true;
    }
}
internal readonly record struct PriceRestorePlan(ushort Height,float Footer,float NodeY)
{
    public static bool TryCreate(ushort originalBefore,ushort originalAfter,float beforeFooter,float afterFooter,LayoutStamp live,out PriceRestorePlan plan,bool hintsDormant=false)
    {
        plan=default;var delta=originalBefore-originalAfter;var height=live.Height+delta;var footer=live.Footer+delta;
        if(originalBefore>16000||originalAfter<24||delta<=0||!float.IsFinite(beforeFooter)||!float.IsFinite(afterFooter)||!PriceFooterState.Safe(originalBefore,beforeFooter,hintsDormant)||!PriceFooterState.Safe(originalAfter,afterFooter,hintsDormant)||Math.Abs((beforeFooter-afterFooter)-delta)>0.01f||
            live.Height<24||height>16000||!float.IsFinite(live.Footer)||!PriceFooterState.Safe(live.Height,live.Footer,hintsDormant)||
            !float.IsFinite(footer)||!PriceFooterState.Safe((ushort)height,footer,hintsDormant))return false;
        plan=new((ushort)height,footer,live.Height-8);return true;
    }
}
