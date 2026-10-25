namespace TooltipExtras;

// Plan geometry independently of owning/copying the provider's current text.
// A visibility-only plan never owns a native height or footer adjustment.
internal readonly record struct PriceCapturePlan(ushort Height,float Footer,bool PresentationOnly)
{
    public static bool TryCreate(PriceGeometry shape,ushort component,ushort root,ushort border,ushort addonRoot,
        ushort minimum,bool hintsDormant,out PriceCapturePlan plan,out string reason)
    {
        plan=default;
        reason="高さ・配置が対応形状と不一致。他追記との干渉があり得るため変更しません";
        if(component==shape.Height&&root==shape.Height&&border==shape.Height&&addonRoot==shape.Height
            &&PriceFooterState.TryRemove(shape,hintsDormant,out var height,out var footer))
        {
            if(minimum<24||height<minimum){reason="本文より小さい高さへの価格分離を保留";return false;}
            plan=new(height,footer,false);return true;
        }
        if(!PriceFooterState.CanHideWithoutResize(shape,component,root,border,addonRoot,minimum,hintsDormant))return false;
        plan=new(shape.Height,shape.FooterY,true);return true;
    }
}
