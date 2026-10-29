using System.Reflection;
namespace TooltipExtras;

// Formatter bodies are not the native write boundary: their typed instruction
// lists are isolated and validated at runtime. Keep native dispatch/layout
// contracts strict, and admit only the registered formatter's required signature.
internal static class DailyFormatterCompatibility
{
    public static CompatibilityResult Check(IEnumerable<CompatibilityEntry> contracts,MethodInfo method,bool action,Func<string,string,Type?> resolve,int? role=null)
    {
        var group=Group(method,action,role);
        var identity=role??DailyRoutineText.ProducerIndex(method.DeclaringType?.FullName);
        var entries=contracts.Where(entry=>entry.Group==group).Select(entry=>
            entry.Kind=="signature"&&identity>=0&&DailyRoutineText.ProducerIndex(entry.Type)==identity
                ?entry with {Type=method.DeclaringType!.FullName!,Expected=entry.Expected.Replace(
                    entry.Type+"."+entry.Member+"(",method.DeclaringType!.FullName+"."+entry.Member+"(",StringComparison.Ordinal)}
                :entry).ToArray();
        if(!entries.Any(entry=>entry.Kind=="signature"&&entry.Expected==MethodCompatibility.Member(method)))
            return new(false,"登録された生成処理の署名が未対応です: "+group);
        return CompatibilityContract.Check(entries,group,resolve);
    }
    public static string Group(MethodInfo method,bool action,int? role=null)
        =>(role??DailyRoutineText.ProducerIndex(method.DeclaringType?.FullName)) switch {
            0 when !action=>"DailyNpcFormatter",
            1 when !action=>"DailyMarketFormatter",
            2 when !action=>"DailySalvageFormatter",
            3=>action?"DailyIdActionFormatter":"DailyIdItemFormatter",
            4 when !action=>"DailyDyeFormatter",
            _=>"UnsupportedDailyFormatter"
        };
}
