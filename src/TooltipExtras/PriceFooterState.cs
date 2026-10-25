using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

// A hidden HQ/NQ hint can retain its previous Y on normal items.
// Invert only the provider's append, without inventing a new footer position.
internal static unsafe class PriceFooterState
{
    public static bool HintsDormant(AtkResNode** nodes,int count,AtkResNode* footer)
    {
        var hint=NativeNodeLookup.FindUnique(nodes,count,3,out _);
        return footer!=null&&hint!=null&&hint->ParentNode==footer&&(hint->NodeFlags&NodeFlags.Visible)==0;
    }
    public static bool Safe(ushort height,float footer,bool hintsDormant)
        =>height is >=24 and <=16000&&float.IsFinite(footer)&&footer>=0&&footer<=16000&&(footer<=height||hintsDormant);
    public static bool TryRemove(PriceGeometry shape,bool hintsDormant,out ushort height,out float footer)
        =>shape.TryRemove(out height,out footer)&&Safe(height,footer,hintsDormant);
    // Native/preview reflow can restore the body while the owned append keeps
    // a position from an expanded layout, including far below the new window.
    // Once the native body fits, copy/hide the owned text without inferring or
    // subtracting any height from its stale position.
    public static bool CanHideWithoutResize(PriceGeometry shape,ushort component,ushort root,ushort border,ushort addonRoot,ushort bodyMinimum,bool hintsDormant=false)
        =>shape.Height is >=24 and <=16000&&component==shape.Height&&root==shape.Height&&border==shape.Height
            &&addonRoot>=shape.Height&&addonRoot<=16000&&bodyMinimum>=24&&bodyMinimum<=shape.Height
            &&shape.NodeHeight is >8 and <=8192&&float.IsFinite(shape.NodeY)&&shape.NodeY>=0
            &&shape.NodeY+shape.NodeHeight<=16000
            &&Safe(shape.Height,shape.FooterY,hintsDormant)&&(hintsDormant||shape.FooterY<=shape.NodeY);
}
