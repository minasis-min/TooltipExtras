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
    private readonly Func<bool>? exclusive;
    private readonly Func<int,int,byte[],byte[]?>? route;
    private readonly bool action;
    private bool active;
    private ulong expected;
    public byte[]? Description {get;private set;}
    public byte[]? SeparatedDescription {get;private set;}
    public List<byte[]> Outputs {get;}=[];
    public int Withheld {get;private set;}
    public bool Failed;
    public Action<int?,string,byte[]?>? Rejected {get;set;}
    public DailyRoutineObservation(IDictionary methods,Type callbackType,Delegate original,Func<int,byte[],byte[]?>? separate=null,Func<bool>? exclusive=null,Func<int,int,byte[],byte[]?>? route=null,bool action=false,bool executeExclusive=true)
    {
        this.methods=methods as ConcurrentDictionary<Type,ImmutableList<Delegate>>??throw new NotSupportedException("DR registration dictionary");
        this.callbackType=callbackType;this.original=original;
        this.separate=separate;
        this.exclusive=exclusive;this.route=route;this.action=action;
        var invoke=callbackType.GetMethod("Invoke")!;
        var p=invoke.GetParameters().Select(x=>Expression.Parameter(x.ParameterType)).ToArray();
        if(p.Length!=3||!p[2].IsByRef||invoke.ReturnType!=typeof(void))throw new NotSupportedException("DR observation signature");
        var collection=invoke.GetParameters()[2].ParameterType.GetElementType()!;
        if(!collection.IsGenericType||collection.GetGenericTypeDefinition()!=typeof(List<>))throw new NotSupportedException("DR modification list signature");
        modificationType=collection.GetGenericArguments()[0];
        targetProperty=modificationType.GetProperty("Target")!;typeProperty=modificationType.GetProperty("Type")!;textProperty=modificationType.GetProperty("Text")!;
        if(targetProperty==null||typeProperty==null||textProperty?.PropertyType!=typeof(ReadOnlySeString))throw new NotSupportedException("DR modification property signatures");
        var self=Expression.Constant(this);
        var invokeOriginal=Expression.Invoke(Expression.Constant(original,callbackType),p);
        var legacy=Expression.Block(invokeOriginal,
            Expression.Call(Expression.Constant(this),typeof(DailyRoutineObservation).GetMethod(nameof(Observe),BindingFlags.Instance|BindingFlags.NonPublic)!,
                Expression.Convert(p[0],typeof(ulong)),p[1],Expression.Convert(p[2],typeof(object))));
        var incoming=Expression.Variable(collection,"incoming");
        var isolated=Expression.Block([incoming],Expression.Assign(incoming,p[2]),Expression.Assign(p[2],Expression.New(collection)),
            Expression.TryCatch(Expression.TryFinally(Expression.Block(invokeOriginal,
                Expression.Call(self,typeof(DailyRoutineObservation).GetMethod(nameof(ObserveExclusive),BindingFlags.Instance|BindingFlags.NonPublic)!,
                    Expression.Convert(p[0],typeof(ulong)),p[1],Expression.Convert(p[2],typeof(object)))),
                Expression.Block(Expression.Assign(p[2],incoming),Expression.Empty())),
                Expression.Catch(typeof(Exception),Expression.Block(Expression.Assign(Expression.Field(self,nameof(Failed)),Expression.Constant(true)),Expression.Rethrow()))));
        proxy=Expression.Lambda(callbackType,Expression.IfThen(Expression.Field(self,nameof(active)),
            Expression.IfThenElse(Expression.Call(self,typeof(DailyRoutineObservation).GetMethod(nameof(IsExclusive),BindingFlags.Instance|BindingFlags.NonPublic)!),
                executeExclusive?isolated:Expression.Call(self,typeof(DailyRoutineObservation).GetMethod(nameof(Withhold),BindingFlags.Instance|BindingFlags.NonPublic)!),legacy)),p).Compile();
    }
    public IDisposable? Begin(ulong target)
    {
        Description=null;SeparatedDescription=null;Outputs.Clear();Withheld=0;Failed=false;
        if(active||!methods.TryGetValue(callbackType,out var list))return null;
        var index=list.IndexOf(original);if(index<0)return null;
        expected=target;active=true;
        var replacement=list.SetItem(index,proxy);
        if(!methods.TryUpdate(callbackType,replacement,list)){active=false;return null;}
        return new Scope(this,list,replacement);
    }
    private bool IsExclusive()=>exclusive?.Invoke()==true;
    private void Withhold(){Failed=true;Rejected?.Invoke(null,"未対応の生成処理は実行せず追記を保留しました",null);}
    private void ObserveExclusive(ulong kind,uint item,object modifications)
    {
        if(modifications is not IList list)return;
        Withheld=list.Count;
        // The source ran against an isolated list. Failure to copy or classify
        // any instruction cannot cause it to enter the real native operation list.
        if(!active||(action?item:item+kind)!=expected||list.Count>256){Failed=true;Rejected?.Invoke(null,"対象の不一致または命令数の上限",null);return;}
        try
        {
            var total=0;
            foreach(var entry in list)
            {
                if(entry==null||!modificationType.IsInstanceOfType(entry)){Failed=true;continue;}
                var field=Convert.ToInt32(targetProperty.GetValue(entry));var operation=Convert.ToInt32(typeProperty.GetValue(entry));
                if(textProperty.GetValue(entry) is not ReadOnlySeString text||text.Data.Length>32768){Failed=true;continue;}
                if(route?.Invoke(field,operation,text.Data.Span.ToArray()) is {Length:>0 and <=32768} output)
                {total+=output.Length;if(total>32768){Outputs.Clear();Failed=true;return;}Outputs.Add(output);}
                else if(text.Data.Length>0){Failed=true;Rejected?.Invoke(field,"追加情報の生成形式が未対応です",text.Data.Span.ToArray());}
            }
        }
        catch { Outputs.Clear();Failed=true;Rejected?.Invoke(null,"追加情報の取得・判定に失敗しました",null); }
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
