using Dalamud.Game.Text.SeStringHandling;
using System.Text;
namespace TooltipExtras;

// Debug-only, bounded, opt-in memory. Exported only by explicit diagnostic save.
internal sealed class RejectedTextLog
{
    internal const int Capacity=20,TextLimit=1024;
    internal sealed record Entry(string Source,int? Field,string Reason,string? Text,int Count);
    private sealed record Stored(Entry Entry,ulong Hash,int Length);
    private readonly object gate=new();
    private readonly List<Stored> entries=[];
    private volatile bool enabled;
    public bool Enabled
    {
        get=>enabled;
        set{lock(gate){enabled=value;if(!value)entries.Clear();}}
    }
    public Entry[] Snapshot(){lock(gate)return entries.Select(e=>e.Entry).Reverse().ToArray();}
    public void Clear(){lock(gate)entries.Clear();}
    public void Record(string source,int? field,string reason,ReadOnlySpan<byte> bytes=default)
    {
        if(!enabled)return;
        // Compare bounded source bytes before parsing SeString on repeated failures.
        var limited=bytes[..Math.Min(bytes.Length,32768)];
        ulong hash=14695981039346656037UL;
        foreach(var b in limited){hash^=b;hash*=1099511628211UL;}
        source=Limit(source,80);reason=Limit(reason,240);var length=bytes.Length;
        lock(gate)
        {
            if(!enabled)return;
            var index=entries.FindIndex(e=>e.Hash==hash&&e.Length==length&&e.Entry.Source==source&&e.Entry.Field==field&&e.Entry.Reason==reason);
            if(index>=0)
            {
                var entry=entries[index];entries.RemoveAt(index);entries.Add(entry with{Entry=entry.Entry with{Count=Math.Min(int.MaxValue-1,entry.Entry.Count)+1}});
                return;
            }
            string? text=null;
            if(!bytes.IsEmpty)
            {
                if(bytes.Length>32768)text="（文字列が長すぎるため内容を記録できません）";
                else try{text=Limit(SeString.Parse(bytes.ToArray()).TextValue,TextLimit);}catch{text="（文字列を解釈できません）";}
            }
            if(entries.Count==Capacity)entries.RemoveAt(0);
            entries.Add(new(new(source,field,reason,text,1),hash,bytes.Length));
        }
    }
    public void Writes(string source,string reason,IReadOnlyDictionary<int,byte[]?> writes)
    {
        if(!enabled)return;
        if(writes.Count==0){Record(source,null,reason);return;}
        var count=0;
        foreach(var (field,bytes) in writes){Record(source,field,reason,bytes);if(++count==Capacity)break;}
    }
    public void Changed(string source,string reason,int[] fields,byte[]?[] before,byte[]?[] after)
    {
        if(!enabled)return;
        for(var i=0;i<Math.Min(fields.Length,Math.Min(before.Length,after.Length));i++)
            if(!(before[i]??[]).AsSpan().SequenceEqual(after[i]??[]))Record(source,fields[i],reason,after[i]);
    }
    private static string Limit(string text,int max)
    {
        var clean=new StringBuilder(Math.Min(text.Length,max)+1);
        foreach(var rune in text.EnumerateRunes())
        {
            if(Rune.IsControl(rune)&&rune.Value is not (10 or 9))continue;
            if(clean.Length+rune.Utf16SequenceLength>max-1){clean.Append('…');break;}
            clean.Append(rune.ToString());
        }
        return clean.ToString();
    }
}
