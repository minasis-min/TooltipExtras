using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
namespace TooltipExtras;
internal static class GlamourNameText
{
    public static bool Matches(byte[] item,byte[] glamour,byte[] combined)
    {
        if(item.Length==0||glamour.Length==0||combined.Length>32768)return false;
        var expected=new SeString((Payload[])[..SeString.Parse(item).Payloads,NewLinePayload.Payload,..SeString.Parse(glamour).Payloads]).Encode();
        return expected.AsSpan().SequenceEqual(combined);
    }
    // Colors are the byte-for-byte RGBA values sampled from the source text
    // nodes, never a rarity/job palette chosen by TE.
    public static byte[] WithColors(byte[] item,byte[] glamour,uint itemRgba,uint glamourRgba)
    {
        var builder=new Lumina.Text.SeStringBuilder();
        builder.PushColorRgba((byte)itemRgba,(byte)(itemRgba>>8),(byte)(itemRgba>>16),(byte)(itemRgba>>24));
        builder.Append((Lumina.Text.ReadOnly.ReadOnlySeStringSpan)item.AsSpan());builder.PopColor();
        builder.Append((Lumina.Text.ReadOnly.ReadOnlySeStringSpan)NewLinePayload.Payload.Encode().AsSpan());
        builder.PushColorRgba((byte)glamourRgba,(byte)(glamourRgba>>8),(byte)(glamourRgba>>16),(byte)(glamourRgba>>24));
        builder.Append((Lumina.Text.ReadOnly.ReadOnlySeStringSpan)glamour.AsSpan());builder.PopColor();
        return builder.ToArray();
    }
}
