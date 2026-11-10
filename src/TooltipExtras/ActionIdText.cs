using System.Globalization;
using System.Text.RegularExpressions;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
namespace TooltipExtras;

// Exact suffix emitted by the reviewed Simple Show ID action formatter. Never
// remove arbitrary bracketed numbers, native category formatting or other text.
internal static class ActionIdText
{
    private static readonly byte[] Open=new Lumina.Text.SeStringBuilder().PushColorType(3).ToArray();
    private static readonly byte[] Close=new Lumina.Text.SeStringBuilder().PopColorType().ToArray();
    private static readonly byte[] NewLine=NewLinePayload.Payload.Encode();
    private static readonly Regex Form=new(@"\A\[(?:(?<original>[0-9]{1,10})→)?(?:(?<decimal>[0-9]{1,10})(?: - (?<hex>0x[0-9A-F]{1,8}))?|(?<hex>0x[0-9A-F]{1,8}))\]\z",RegexOptions.CultureInvariant);
    public static bool IsCandidate(ReadOnlySpan<byte> bytes)=>bytes.Length<=32768&&bytes.IndexOf(Open)>=0&&bytes.EndsWith(Close);
    public static bool Split(byte[] full,out byte[] category,out byte[] id)
    {
        category=id=[];
        var bytes=full.AsSpan();
        if(bytes.Length is 0 or >32768||!bytes.EndsWith(Close))return false;
        var start=bytes.IndexOf(Open);
        if(start<0||bytes.Length<start+Open.Length+Close.Length||bytes[(start+Open.Length)..].IndexOf(Open)>=0)return false;
        var body=bytes.Slice(start+Open.Length,bytes.Length-start-Open.Length-Close.Length);
        if(body.Length is 0 or >64)return false;
        var value=SeString.Parse(body.ToArray());
        if(value.Payloads.Any(p=>p is not TextPayload)||!value.Encode().AsSpan().SequenceEqual(body))return false;
        var match=Form.Match(value.TextValue);if(!match.Success)return false;
        uint number=0;
        if(match.Groups["decimal"].Success&&!uint.TryParse(match.Groups["decimal"].Value,NumberStyles.None,CultureInfo.InvariantCulture,out number))return false;
        if(match.Groups["hex"].Success)
        {
            if(!uint.TryParse(match.Groups["hex"].Value.AsSpan(2),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out var hex))return false;
            if(match.Groups["decimal"].Success&&hex!=number)return false;
            number=hex;
        }
        if(match.Groups["original"].Success)
        {
            if(!uint.TryParse(match.Groups["original"].Value,NumberStyles.None,CultureInfo.InvariantCulture,out var original)||original==number)return false;
            if(match.Groups["decimal"].Success&&match.Groups["hex"].Success)return false;
        }
        var prefix=bytes[..start];
        if(!prefix.IsEmpty)
        {
            if(prefix[^1]==32)prefix=prefix[..^1];
            else if(prefix.EndsWith(NewLine))prefix=prefix[..^NewLine.Length];
            else if(prefix[^1]==10)prefix=prefix[..^1];
            else return false;
        }
        var native=SeString.Parse(prefix.ToArray());
        if(native.Payloads.Count>1||native.Payloads.Any(p=>p is not TextPayload)||!native.Encode().AsSpan().SequenceEqual(prefix))return false;
        category=prefix.ToArray();id=bytes[start..].ToArray();return true;
    }
}
