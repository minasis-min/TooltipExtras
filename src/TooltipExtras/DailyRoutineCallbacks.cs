using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
namespace TooltipExtras;

// Immutable registration identity is also the metadata cache key. Read it at
// each ownership boundary, but classify/build observers only when it changes.
internal sealed class DailyRoutineCallbacks(IDictionary methods,Type callbackType,Assembly assembly,bool action,Func<Delegate,DailyRoutineObservation?> create,Func<bool>? allowUnknown=null,Func<Delegate,int>? identify=null)
{
    internal sealed record Snapshot(ImmutableList<Delegate> Registrations,int[] Fields,bool Shop,DailyRoutineObservation[] Observers,DailyRoutineObservation? Salvage);
    private readonly ConcurrentDictionary<Type,ImmutableList<Delegate>> methods=methods as ConcurrentDictionary<Type,ImmutableList<Delegate>>??throw new NotSupportedException("DR registration dictionary");
    private ImmutableList<Delegate>? previous;
    private Snapshot? cached;
    private bool previousAllowUnknown;
    private string reason="対象機能の登録待ち";
    public object? Current=>methods.TryGetValue(callbackType,out var value)?value:null;
    public bool TrySnapshot(out Snapshot? snapshot,out string failure)
    {
        var current=Current as ImmutableList<Delegate>;
        var isolated=allowUnknown?.Invoke()==true;
        if(!ReferenceEquals(current,previous)||previousAllowUnknown!=isolated)
        {
            previous=current;previousAllowUnknown=isolated;cached=null;
            var callbacks=current?.ToArray()??[];
            if(callbacks.Length>256){snapshot=null;failure=reason="登録数が上限を超えました";return false;}
            int Role(Delegate d)=>identify?.Invoke(d)??DailyRoutineText.ProducerIndex(d.Method.DeclaringType?.FullName);
            var known=DailyRoutineText.Known(callbacks,out reason,action,Role);
            // Unknown DR formatters can be withheld individually only when every
            // callback will run through the typed isolated-output proxy.
            if(!known&&isolated&&callbacks.Length>0&&callbacks.All(d=>callbackType.IsInstanceOfType(d)))
            {
                var reviewed=callbacks.Where(d=>Role(d)>=0).ToArray();
                known=reviewed.Length==0||DailyRoutineText.Known(reviewed,out reason,action,Role);
            }
            if(known)
            {
                if(callbacks.Any(d=>d.Method.DeclaringType?.Assembly!=assembly))reason="元の機能アセンブリが変わりました";
                else
                {
                    var observers=new List<DailyRoutineObservation>();DailyRoutineObservation? salvage=null;
                    // Skill rows precede the append-only NPC/market paragraphs.
                    foreach(var callback in callbacks)
                        if(create(callback) is {} observer)
                        {
                            if(Role(callback)==2){salvage=observer;observers.Insert(0,observer);}
                            else observers.Add(observer);
                        }
                    cached=new(current!,DailyRoutineText.Fields(callbacks,action,Role),!action&&callbacks.Any(d=>Role(d)==0),observers.ToArray(),salvage);
                }
            }
        }
        snapshot=cached;failure=reason;return snapshot!=null;
    }
}
