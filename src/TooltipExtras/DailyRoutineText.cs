using System.Text;
namespace TooltipExtras;

internal static class DailyRoutineText
{
    public const int ShopSellingPriceFlag=524288;
    public static bool EmptyShopCandidate(bool reviewedNpc,bool arrayEmpty,bool nodeEmpty,bool nodeVisible,bool groupVisible,int flags)
        =>reviewedNpc&&arrayEmpty&&(nodeEmpty||!nodeVisible)&&(groupVisible||(flags&ShopSellingPriceFlag)!=0);
    private static readonly byte[] LineBreak=Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload.Payload.Encode();
    public static int TrailingLines(ReadOnlySpan<byte> text)
    {
        var count=0;
        while(!text.IsEmpty)
        {
            if(text.EndsWith(LineBreak)){count++;text=text[..^LineBreak.Length];}
            else if(text[^1]==10){count++;text=text[..^1];}
            else if(text[^1] is 13 or 32 or 9)text=text[..^1];
            else break;
        }
        return count;
    }
    private static readonly byte[][] SkillColors=new uint[]{0,32,45}.Select(color=>new Lumina.Text.SeStringBuilder().PushColorType(color).ToArray()).ToArray();
    private static readonly byte[] SkillReset=new Lumina.Text.SeStringBuilder().PopColorType().ToArray();
    internal static readonly string[] Producers = [
        "DailyRoutines.ModulesPublic.Interface.AutoShowItemNPCShopInfo.AutoShowItemNPCShopInfo",
        "DailyRoutines.ModulesPublic.Interface.BetterMarketBoard",
        "DailyRoutines.ModulesPublic.Interface.OptimizedSalvage",
        "DailyRoutines.ModulesPublic.Interface.AutoDisplayIDInformation",
        "DailyRoutines.ModulesPublic.Interface.AutoPreviewColorsInDye"
    ];
    public static bool Known(IEnumerable<Delegate> callbacks, out string reason,bool action=false)
    {
        var found=new HashSet<string>();
        foreach(var callback in callbacks)
        {
            var type=callback.Method.DeclaringType?.FullName??"";
            var index=Array.IndexOf(Producers,type);
            var method=action?"OnActionTooltip":index<2?"OnItemTooltipUpdate":"OnItemTooltip";
            if(index<0||action&&index!=3||callback.Method.Name!=method||!found.Add(type))
            {reason="未対応または重複した登録: "+type+"."+callback.Method.Name;return false;}
        }
        reason=found.Count==0?"対象機能の登録待ち":"";
        return found.Count>0;
    }
    public static int[] Fields(IEnumerable<Delegate> callbacks,bool action=false)
    {
        if(action)return [1];
        var types=callbacks.Select(d=>d.Method.DeclaringType?.FullName).ToHashSet();
        return new[]{(Field:2,Enabled:types.Contains(Producers[3])),
            (Field:13,Enabled:types.Contains(Producers[0])||types.Contains(Producers[1])||types.Contains(Producers[2])),
            (Field:35,Enabled:types.Contains(Producers[2]))}.Where(x=>x.Enabled).Select(x=>x.Field).ToArray();
    }
    public static int RestoreFlags(int flags,IReadOnlyList<int> fields,IReadOnlyList<byte[]?> before)
    {
        for(var i=0;i<fields.Count;i++)if(fields[i]==13&&before[i] is {Length:0})return flags&~2;
        return flags;
    }
    public static bool ChangedVisible(int field,ReadOnlySpan<byte> incoming,ReadOnlySpan<byte> generated,int flags,bool action=false)
        =>!incoming.SequenceEqual(generated)&&(action||field switch {13=>(flags&2)!=0,35=>(flags&64)!=0,_=>true});
    public static byte[]? Extract(int field,byte[] baseline,byte[] generated)
    {
        if(generated.Length==0||baseline.AsSpan().SequenceEqual(generated))return null;
        if(generated.AsSpan().StartsWith(baseline))return Suffix(field,generated,baseline.Length);
        // The audited salvage formatter changes spaces in the native ability rows
        // to line breaks. Match complete SeString tokens with only whitespace
        // ignored, preserving every glyph, number and formatting payload.
        if(field!=35)return null;
        var before=Tokens(baseline);var after=Tokens(generated);
        if(before==null||after==null||before.Count==0||after.Count<=before.Count)return null;
        for(var i=0;i<before.Count;i++)if(!before[i].Matches(after[i]))return null;
        return Suffix(field,generated,after[before.Count-1].End);
    }
    private static byte[]? Suffix(int field,byte[] generated,int end)
    {
        var suffix=generated.AsSpan(end);
        if(field==35)
        {
            while(!suffix.IsEmpty&&suffix[0] is 32 or 9)suffix=suffix[1..];
            if(suffix.IsEmpty||suffix[0]!=10&&!suffix.StartsWith(Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload.Payload.Encode()))return null;
        }
        return Trim(generated.AsSpan(end).ToArray());
    }
    // Only the reviewed OptimizedSalvage callback contributes field 35. It
    // reconstructs the standard prefix from Addon 1361, which can differ from
    // the native prefix. Identify its own localized line and exact color/number
    // shape instead of weakening the general prefix comparison.
    public static byte[]? ExtractSalvage(byte[] baseline,byte[] generated,string label)
    {
        if(string.IsNullOrWhiteSpace(label)||label.Length>256||baseline.Length==0)return null;
        var marker=Encoding.UTF8.GetBytes(label);
        if(baseline.AsSpan().IndexOf(marker)>=0)return null; // Never reuse an already contaminated baseline.
        var start=generated.AsSpan().IndexOf(marker);
        if(start<=0||generated.AsSpan(start+marker.Length).IndexOf(marker)>=0)return null;
        var prefix=generated.AsSpan(0,start);
        if(prefix[^1]!=10&&!prefix.EndsWith(LineBreak))return null;
        var parsed=Dalamud.Game.Text.SeStringHandling.SeString.Parse(generated);
        if(!parsed.Encode().AsSpan().SequenceEqual(generated))return null;
        var tail=generated.AsSpan(start+marker.Length);
        return ValidSkill(tail)?generated.AsSpan(start).ToArray():null;
    }
    private static bool ValidSkill(ReadOnlySpan<byte> tail)
    {
        byte[]? color=null;foreach(var bytes in SkillColors)if(tail.StartsWith(bytes)){color=bytes;break;}
        if(color==null)return false;
        tail=tail[color.Length..];
        var reset=tail.IndexOf(SkillReset);
        if(reset is <4 or >12)return false;
        var number=tail[..reset];var point=number.IndexOf((byte)'.');
        if(point<1||point!=number.Length-3)return false;
        for(var i=0;i<number.Length;i++)if(i!=point&&(number[i]<'0'||number[i]>'9'))return false;
        var note=tail[(reset+SkillReset.Length)..];
        if(!note.IsEmpty)
        {
            var text=Dalamud.Game.Text.SeStringHandling.SeString.Parse(note.ToArray());
            if(text.Payloads.Any(p=>p is not Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload))return false;
            var value=text.TextValue;
            if(!value.StartsWith(" ［",StringComparison.Ordinal)||!value.EndsWith("］",StringComparison.Ordinal)||value.Contains('\n')||value.Contains('\r'))return false;
        }
        return true;
    }
    private static byte[]? Trim(byte[] bytes){var output=CapturedText.TrimPresentationEdges(bytes);return output.Length==0?null:output;}
    public static bool SameFlowContent(byte[] expected,byte[] actual)
    {
        if(expected.AsSpan().SequenceEqual(actual))return true;
        var left=FlowCursor.Create(expected);var right=FlowCursor.Create(actual);
        if(left==null||right==null)return false;
        while(left.Next(out var a)){if(!right.Next(out var b)||!a.Matches(b))return false;}
        return !right.Next(out _)&&left.Complete(expected.Length)&&right.Complete(actual.Length);
    }
    public static bool PresentationMatches(byte[] baseline,byte[] generated,byte[] displayed)
        =>SameFlowContent(baseline,displayed)||SameFlowContent(generated,displayed);
    internal sealed record NonGearPattern(string Suggested,string JobOpen,string JobClose,string Current)
    {
        public static NonGearPattern? Parse(string macro)
        {
            const string start="<string(lstr1)><br>",number="<num(lnum2)>.00",job="<sheet(ClassJob,lnum3,0)>",end="<string(lstr4)>";
            if(macro.Length>4096||!macro.StartsWith(start,StringComparison.Ordinal)||!macro.EndsWith(end,StringComparison.Ordinal))return null;
            var n=macro.IndexOf(number,start.Length,StringComparison.Ordinal);var j=macro.IndexOf(job,StringComparison.Ordinal);
            var br=macro.LastIndexOf("<br>",StringComparison.Ordinal);
            if(n<=start.Length||j<n+number.Length||br<j+job.Length)return null;
            var result=new NonGearPattern(macro[start.Length..n],macro[(n+number.Length)..j],macro[(j+job.Length)..br],macro[(br+4)..^end.Length]);
            return new[]{result.Suggested,result.JobOpen,result.JobClose,result.Current}.All(s=>s.Length is >0 and <=256&&!s.Contains('<')&&!s.Contains('>')&&!s.Contains('\n')&&!s.Contains('\r'))?result:null;
        }
    }
    // The audited non-gear salvage Contribute operation rebuilds Description
    // from the source Item.Description, then adds these two template lines.
    // Use that formatter's completed prefix instead of a possibly stale cache.
    public static bool SplitNonGear(byte[] generated,NonGearPattern pattern,out byte[] native,out byte[] extra)
    {
        native=extra=[];
        var marker=Encoding.UTF8.GetBytes(pattern.Suggested);var start=generated.AsSpan().IndexOf(marker);
        if(start<0||generated.AsSpan(start+marker.Length).IndexOf(marker)>=0)return false;
        var prefix=generated.AsSpan(0,start);var nativeEnd=prefix.Length;
        if(prefix.EndsWith(LineBreak))nativeEnd-=LineBreak.Length;
        else if(!prefix.IsEmpty&&prefix[^1]==10)nativeEnd--;
        else return false;
        var value=Dalamud.Game.Text.SeStringHandling.SeString.Parse(generated);
        if(!value.Encode().AsSpan().SequenceEqual(generated))return false;
        ReadOnlySpan<byte> tail=generated.AsSpan(start+marker.Length);var line=LineEnd(tail);
        if(line<0)return false;
        var suggested=Dalamud.Game.Text.SeStringHandling.SeString.Parse(tail[..line].ToArray());
        if(suggested.Payloads.Any(p=>p is not Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload))return false;
        var text=suggested.TextValue;var point=text.IndexOf(".00",StringComparison.Ordinal);
        if(point is <1 or >8||text[..point].Any(c=>c<'0'||c>'9'))return false;
        var job=text[(point+3)..];
        if(!job.StartsWith(pattern.JobOpen,StringComparison.Ordinal)||!job.EndsWith(pattern.JobClose,StringComparison.Ordinal)
            ||job.Length<=pattern.JobOpen.Length+pattern.JobClose.Length||job.Length>128)return false;
        tail=SkipLine(tail[line..]);var label=Encoding.UTF8.GetBytes(pattern.Current);
        if(!tail.StartsWith(label))return false;
        var skillLine=LineEnd(tail);if(skillLine<0)skillLine=tail.Length;
        if(!ValidSkill(tail[label.Length..skillLine]))return false;
        native=generated.AsSpan(0,nativeEnd).ToArray();extra=Trim(generated.AsSpan(start).ToArray())??[];
        return extra.Length>0;
    }
    private static int LineEnd(ReadOnlySpan<byte> text)
    {
        var a=text.IndexOf((byte)10);var b=text.IndexOf(LineBreak);
        return a<0?b:b<0?a:Math.Min(a,b);
    }
    private static ReadOnlySpan<byte> SkipLine(ReadOnlySpan<byte> text)=>text.StartsWith(LineBreak)?text[LineBreak.Length..]:text[1..];
    private readonly record struct FlowToken(int Scalar,byte[]? Payload,int End)
    {
        public bool Matches(FlowToken other)=>Payload==null?other.Payload==null&&Scalar==other.Scalar:
            other.Payload!=null&&Payload.AsSpan().SequenceEqual(other.Payload);
    }
    private sealed class FlowCursor(List<Dalamud.Game.Text.SeStringHandling.Payload> payloads)
    {
        private int index,textIndex,offset;
        public static FlowCursor? Create(byte[] bytes)
        {
            var value=Dalamud.Game.Text.SeStringHandling.SeString.Parse(bytes);
            return value.Encode().AsSpan().SequenceEqual(bytes)?new(value.Payloads):null;
        }
        public bool Next(out FlowToken token)
        {
            while(index<payloads.Count)
            {
                var payload=payloads[index];
                if(payload is Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload text)
                {
                    var content=text.Text??"";
                    while(textIndex<content.Length)
                    {
                        Rune.DecodeFromUtf16(content.AsSpan(textIndex),out var rune,out var consumed);
                        textIndex+=consumed;offset+=rune.Utf8SequenceLength;
                        if(!Rune.IsWhiteSpace(rune)){token=new(rune.Value,null,offset);return true;}
                    }
                    index++;textIndex=0;
                }
                else
                {
                    index++;var raw=payload.Encode();offset+=raw.Length;
                    if(payload is not Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload){token=new(0,raw,offset);return true;}
                }
            }
            token=default;return false;
        }
        public bool Complete(int length)=>offset==length;
    }
    private static List<FlowToken>? Tokens(byte[] bytes)
    {
        var cursor=FlowCursor.Create(bytes);if(cursor==null)return null;
        var result=new List<FlowToken>();
        while(cursor.Next(out var token))result.Add(token);
        return cursor.Complete(bytes.Length)?result:null;
    }
}
