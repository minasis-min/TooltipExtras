using System.Collections;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Lumina.Text.ReadOnly;
namespace TooltipExtras;

// Synchronous scoped forwarding observes the actual formatter once. An optional
// checked selector can divert its one description operation before native use.
internal sealed class DailyRoutineObservation
{
    private readonly ConcurrentDictionary<Type,ImmutableList<Delegate>> methods;
    private readonly Type callbackType;
    private readonly Delegate original,proxy;
    private readonly Type modificationType;
    private readonly PropertyInfo targetProperty,typeProperty,textProperty;
    private readonly Func<int,byte[],byte[]?>? separate;
    private bool active;
    private ulong expected;
    public byte[]? Description {get;private set;}
    public byte[]? SeparatedDescription {get;private set;}
    public DailyRoutineObservation(IDictionary methods,Type callbackType,Delegate original,Func<int,byte[],byte[]?>? separate=null)
    {
        this.methods=methods as ConcurrentDictionary<Type,ImmutableList<Delegate>>??throw new NotSupportedException("DR registration dictionary");
        this.callbackType=callbackType;this.original=original;
        this.separate=separate;
        var invoke=callbackType.GetMethod("Invoke")!;
        var p=invoke.GetParameters().Select(x=>Expression.Parameter(x.ParameterType)).ToArray();
        if(p.Length!=3||!p[2].IsByRef||invoke.ReturnType!=typeof(void))throw new NotSupportedException("DR observation signature");
        var collection=invoke.GetParameters()[2].ParameterType.GetElementType()!;
        if(!collection.IsGenericType||collection.GetGenericTypeDefinition()!=typeof(List<>))throw new NotSupportedException("DR modification list signature");
        modificationType=collection.GetGenericArguments()[0];
        targetProperty=modificationType.GetProperty("Target")!;typeProperty=modificationType.GetProperty("Type")!;textProperty=modificationType.GetProperty("Text")!;
        if(targetProperty==null||typeProperty==null||textProperty?.PropertyType!=typeof(ReadOnlySeString))throw new NotSupportedException("DR modification property signatures");
        proxy=Expression.Lambda(callbackType,Expression.IfThen(Expression.Field(Expression.Constant(this),nameof(active)),Expression.Block(Expression.Invoke(Expression.Constant(original,callbackType),p),
            Expression.Call(Expression.Constant(this),typeof(DailyRoutineObservation).GetMethod(nameof(Observe),BindingFlags.Instance|BindingFlags.NonPublic)!,
                Expression.Convert(p[0],typeof(ulong)),p[1],Expression.Convert(p[2],typeof(object))))),p).Compile();
    }
    public IDisposable? Begin(ulong target)
    {
        Description=null;SeparatedDescription=null;
        if(active||!methods.TryGetValue(callbackType,out var list))return null;
        var index=list.IndexOf(original);if(index<0)return null;
        expected=target;active=true;
        var replacement=list.SetItem(index,proxy);
        if(!methods.TryUpdate(callbackType,replacement,list)){active=false;return null;}
        return new Scope(this,list,replacement);
    }
    private void Observe(ulong kind,uint item,object modifications)
    {
        try
        {
        if(!active||item+kind!=expected||modifications is not IList list||list.Count>256)return;
        byte[]? found=null;var foundIndex=-1;var operation=-1;
        for(var index=0;index<list.Count;index++)
        {
            var modification=list[index];
            if(modification==null)continue;
            if(!modificationType.IsInstanceOfType(modification))return;
            if(Convert.ToInt32(targetProperty.GetValue(modification))!=13)continue;
            if(found!=null){Description=null;return;}
            if(textProperty.GetValue(modification) is not ReadOnlySeString text||text.Data.Length>32768)return;
            found=text.Data.Span.ToArray();foundIndex=index;operation=Convert.ToInt32(typeProperty.GetValue(modification));
        }
        Description=operation==0?found:null;
        // Copy and validate the complete output before removing its one reviewed
        // instruction. No cached native prefix is written back on this path.
        if(found!=null&&separate!=null&&!list.IsReadOnly&&!list.IsFixedSize&&separate(operation,found) is {Length:>0 and <=32768} extra)
        {
            list.RemoveAt(foundIndex);
            SeparatedDescription=extra;Description=null;
        }
        }
        catch{Description=null;} // Observation failure must not become a source formatter error.
    }
    private sealed class Scope(DailyRoutineObservation owner,ImmutableList<Delegate> original,ImmutableList<Delegate> replacement):IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            if(disposed)return;disposed=true;owner.active=false;
            for(var attempt=0;attempt<16;attempt++)
            {
                if(!owner.methods.TryGetValue(owner.callbackType,out var current))return;
                var index=current.IndexOf(owner.proxy);
                if(index<0)return; // Our entry was explicitly removed; never resurrect it.
                var restored=ReferenceEquals(current,replacement)?original:
                    current.Contains(owner.original)?current.RemoveAt(index):current.SetItem(index,owner.original);
                if(owner.methods.TryUpdate(owner.callbackType,restored,current))return;
            }
            // A stale snapshot may still hold this delegate, but active=false
            // makes it inert even if contention prevents dictionary cleanup.
            throw new InvalidOperationException("DR registration cleanup contention");
        }
    }
}
