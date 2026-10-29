using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TooltipExtras;

// Discovery uses the source registration and exact assembly, not module names.
// An unfamiliar name is executable only if its complete same-assembly operation
// graph matches a reviewed role after replacing that producer's declaring name.
internal sealed class DailyRoutineIdentity(Assembly assembly,bool action,IReadOnlyList<DailyRoutineIdentity.Profile>? profiles=null)
{
    internal sealed record Profile(int Role,bool Action,string SHA256);
    private readonly IReadOnlyList<Profile> profiles=profiles??Load();
    private readonly Dictionary<MethodInfo,int> cache=[];
    public int Role(Delegate callback)=>Role(callback.Method);
    public int Role(MethodInfo method)
    {
        if(method.DeclaringType?.Assembly!=assembly)return -1;
        if(cache.TryGetValue(method,out var role))return role;
        role=DailyRoutineText.ProducerIndex(method.DeclaringType.FullName);
        if(role<0)
        {
            try
            {
                var hash=Fingerprint(method);
                var matches=profiles.Where(p=>p.Action==action&&p.SHA256==hash).Select(p=>p.Role).Distinct().ToArray();
                if(matches.Length==1&&matches[0] is >=0 and <=3)role=matches[0];
            }
            catch { role=-1; } // Unsupported IL/large graphs stay isolated, never guessed.
        }
        if(cache.Count>=256)cache.Clear();
        cache[method]=role;return role;
    }
    internal static string Fingerprint(MethodInfo method)
    {
        var name=method.DeclaringType?.FullName??throw new NotSupportedException("DR formatter owner");
        var pattern=new Regex(Regex.Escape(name)+@"(?=$|[+.,:;|<>()\[\]&*])",RegexOptions.CultureInvariant);
        return MethodCompatibility.CallTree(method,text=>pattern.Replace(text,_=>"@producer"));
    }
    private static Profile[] Load()=>JsonSerializer.Deserialize<Profile[]>(typeof(DailyRoutineIdentity).Assembly.GetManifestResourceStream(
        "TooltipExtras.DailyFormatterProfiles.json")??throw new InvalidOperationException("DR処理の識別情報がありません"))!;
}
