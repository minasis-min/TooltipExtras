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
}