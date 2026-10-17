using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
namespace TooltipExtras;

internal static class CapturedText
{
    public static byte[] TrimAllaganEdges(byte[] bytes)
    {
        var value = SeString.Parse(bytes);
        var text = value.Payloads.OfType<TextPayload>().ToArray();
        // Audited providers each emit one text payload. Unknown formatting is kept intact.
        if (text.Length != 1 || text[0].Text == null) return bytes;
        var trimmed = text[0].Text!.Trim('\r', '\n');
        if (trimmed == text[0].Text) return bytes;
        text[0].Text = trimmed;
        return value.Encode();
    }
    public static bool SplitItemId(byte[] bytes, out byte[] category, out byte[] id)
    {
        category = id = [];
        var value = SeString.Parse(bytes); var p = value.Payloads;
        if (p.Count < 4 || p[^1] is not UIForegroundPayload { ColorKey: 0 }) return false;
        var start = p.Count - 2;
        while (start >= 0 && p[start] is TextPayload) start--;
        if (start <= 0 || p[start] is not UIForegroundPayload { ColorKey: 3 }) return false;
        var text = string.Concat(p.Skip(start + 1).Take(p.Count - start - 2).Cast<TextPayload>().Select(t => t.Text));
        if (!Regex.IsMatch(text, @"\A   \[(?:[0-9]+(?: - 0x[0-9A-F]+)?|0x[0-9A-F]+)\]\z", RegexOptions.CultureInvariant)) return false;
        category = new SeString(p.Take(start).ToList()).Encode();
        // The split must be an exact suffix of the encoded original, never a guessed replacement.
        var exactPrefix = new SeString(p.Take(start).ToList()).Encode();
        var rawId = new SeString(p.Skip(start).ToList()).Encode();
        if (category.Length == 0 || !bytes.AsSpan().SequenceEqual([..exactPrefix, ..rawId])) { category = []; return false; }
        id = new SeString(new UIForegroundPayload(3), new TextPayload(text.TrimStart(' ')), new UIForegroundPayload(0)).Encode();
        return true;
    }
    public static byte[] TrimPresentationEdges(byte[] bytes)
    {
        var value=SeString.Parse(bytes);var parts=value.Payloads;var changed=false;
        bool Formatting(Payload payload)=>payload is UIForegroundPayload or UIGlowPayload or DalamudLinkPayload
            ||payload is RawPayload raw&&raw.Data.AsSpan().SequenceEqual(RawPayload.LinkTerminator.Data);
        for(var i=0;i<parts.Count;)
        {
            if(Formatting(parts[i])){i++;continue;}
            if(parts[i] is NewLinePayload){parts.RemoveAt(i);changed=true;continue;}
            if(parts[i] is TextPayload text)
            {
                var next=(text.Text??"").TrimStart('\r','\n');
                if(next!=text.Text){text.Text=next;changed=true;}
                if(next.Length==0){i++;continue;}
            }
            break;
        }
        for(var i=parts.Count-1;i>=0;)
        {
            if(Formatting(parts[i])){i--;continue;}
            if(parts[i] is NewLinePayload){parts.RemoveAt(i--);changed=true;continue;}
            if(parts[i] is TextPayload text)
            {
                var next=(text.Text??"").TrimEnd('\r','\n');
                if(next!=text.Text){text.Text=next;changed=true;}
                if(next.Length==0){i--;continue;}
            }
            break;
        }
        return changed?value.Encode():bytes;
    }
    public static byte[] Rows(IEnumerable<byte[]> parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts) { if (part.Length == 0) continue; if (stream.Length > 0) stream.WriteByte(10); stream.Write(part); }
        return stream.ToArray();
    }
}
