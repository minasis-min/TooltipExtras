using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
namespace TooltipExtras;

internal static class DesynthesisPresentation
{
    private sealed record Row(byte[] Bytes,ushort Color,ushort Glow);
    // The reviewed formatter changes one existing skill row in Description.
    // Other rows belong to the game or other providers, even if the formatter
    // writes their bytes back along with its own change.
    public static byte[]? Extract(byte[] before,byte[] after)
    {
        var original=Rows(before);var generated=Rows(after);
        if(original==null||generated==null||original.Count!=generated.Count)return null;
        Row? changed=null;
        for(var i=0;i<original.Count;i++)
        {
            if(original[i].Bytes.AsSpan().SequenceEqual(generated[i].Bytes))continue;
            if(changed!=null)return null; // Never guess among multiple changed rows.
            changed=generated[i];
        }
        if(changed==null||SeString.Parse(changed.Bytes).TextValue.Length==0)return null;
        var result=new List<Payload>();
        if(changed.Color!=0)result.Add(new UIForegroundPayload(changed.Color));
        if(changed.Glow!=0)result.Add(new UIGlowPayload(changed.Glow));
        result.AddRange(SeString.Parse(changed.Bytes).Payloads);
        // A row can inherit formatting from earlier description rows. Give the
        // isolated row that context, and end it before the next panel section.
        result.Add(new UIForegroundPayload(0));result.Add(new UIGlowPayload(0));
        return new SeString(result).Encode();
    }
    private static List<Row>? Rows(byte[] bytes)
    {
        if(bytes.Length>32768)return null;
        var parsed=SeString.Parse(bytes);
        if(!parsed.Encode().AsSpan().SequenceEqual(bytes))return null;
        var rows=new List<Row>();var parts=new List<Payload>();
        ushort color=0,glow=0,startColor=0,startGlow=0;
        void End()
        {
            rows.Add(new(new SeString(parts).Encode(),startColor,startGlow));
            parts=[];startColor=color;startGlow=glow;
        }
        foreach(var payload in parsed.Payloads)
        {
            if(payload is NewLinePayload){End();}
            else if(payload is TextPayload text)
            {
                var lines=(text.Text??"").Split('\n');
                for(var i=0;i<lines.Length;i++)
                {
                    if(i>0){End();if(rows.Count>128)return null;}
                    if(lines[i].Length>0)parts.Add(new TextPayload(lines[i]));
                }
            }
            else
            {
                parts.Add(payload);
                if(payload is UIForegroundPayload foreground)color=foreground.ColorKey;
                if(payload is UIGlowPayload outline)glow=outline.ColorKey;
            }
            if(rows.Count>128)return null;
        }
        End();return rows;
    }
}
