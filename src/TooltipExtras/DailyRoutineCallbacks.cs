using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
namespace TooltipExtras;

// Immutable registration identity is also the metadata cache key. Read it at
// each ownership boundary, but classify/build observers only when it changes.
internal sealed class DailyRoutineCallbacks(IDictionary methods,Type callbackType,Assembly assembly,bool action,Func<Delegate,DailyRoutineObservation?> create)
{
    internal sealed record Snapshot(ImmutableList<Delegate> Registrations,int[] Fields,bool Shop,DailyRoutineObservation[] Observers,DailyRoutineObservation? Salvage);
    private readonly ConcurrentDictionary<Type,ImmutableList<Delegate>> methods=methods as ConcurrentDictionary<Type,ImmutableList<Delegate>>??throw new NotSupportedException("DR registration dictionary");
    private ImmutableList<Delegate>? previous;
    private Snapshot? cached;
    private string reason="対象機能の登録待ち";
    public object? Current=>methods.TryGetValue(callbackType,out var value)?value:null;
    public bool TrySnapshot(out Snapshot? snapshot,out string failure)
    {
        var current=Current as ImmutableList<Delegate>;
        if(!ReferenceEquals(current,previous))
        {
            previous=current;cached=null;
            var callbacks=current?.ToArray()??[];
            if(DailyRoutineText.Known(callbacks,out reason,action))
            {
                if(callbacks.Any(d=>d.Method.DeclaringType?.Assembly!=assembly))reason="元の機能アセンブリが変わりました";
                else
                {
                    var observers=new List<DailyRoutineObservation>();DailyRoutineObservation? salvage=null;
                    // Skill rows precede the append-only NPC/market paragraphs.
                    foreach(var callback in callbacks)
                        if(create(callback) is {} observer)
                        {
                            if(callback.Method.DeclaringType?.FullName==DailyRoutineText.Producers[2]){salvage=observer;observers.Insert(0,observer);}
                            else observers.Add(observer);
                        }
                    cached=new(current!,DailyRoutineText.Fields(callbacks,action),!action&&callbacks.Any(d=>d.Method.DeclaringType?.FullName==DailyRoutineText.Producers[0]),observers.ToArray(),salvage);
                }
            }
        }
        snapshot=cached;failure=reason;return snapshot!=null;
    }
}
