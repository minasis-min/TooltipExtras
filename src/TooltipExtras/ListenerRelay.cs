using System.Reflection;
using System.Linq.Expressions;
namespace TooltipExtras;

// Preserve the source's registration object and delegate in its plugin-scoped list.
// Dalamud still marks that object on Unregister; the replacement observes the mark.
internal sealed class ListenerRelay : IDisposable
{
    private readonly object set, source, proxy;
    private readonly Func<bool> sourceActive;
    private readonly Func<object,bool> contains, add, remove;
    private bool SourceActive => sourceActive();
    public bool Active => SourceActive&&Has(proxy);
    public ListenerRelay(object set, object source, object proxy)
    {
        this.set=set;this.source=source;this.proxy=proxy;
        var clear=source.GetType().GetProperty("IsRequestedToClear",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;
        sourceActive=Expression.Lambda<Func<bool>>(Expression.Not(Expression.Property(Expression.Constant(source),clear))).Compile();
        Func<object,bool> Operation(string name)
        {
            var method=set.GetType().GetMethod(name,[source.GetType()])!;
            var value=Expression.Parameter(typeof(object));
            return Expression.Lambda<Func<object,bool>>(Expression.Call(Expression.Constant(set),method,Expression.Convert(value,source.GetType())),value).Compile();
        }
        contains=Operation("Contains");add=Operation("Add");remove=Operation("Remove");
        if(!SourceActive||!Has(source)||Has(proxy))throw new InvalidOperationException("元のイベント登録がありません、または中継が重複しています");
        var order=((System.Collections.IEnumerable)set).Cast<object>().ToArray();
        if(!remove(source))throw new InvalidOperationException("元のイベント登録が変わりました");
        try
        {
            if(!add(proxy))throw new InvalidOperationException("中継イベントが重複しています");
            var expected=order.Select(value=>ReferenceEquals(value,source)?proxy:value);
            if(!((System.Collections.IEnumerable)set).Cast<object>().SequenceEqual(expected,ReferenceEqualityComparer.Instance))
                throw new NotSupportedException("イベントの呼出し順を保持できません");
        }
        catch {remove(proxy);add(source);throw;}
    }
    private bool Has(object value)=>contains(value);
    public void Dispose()
    {
        // A removed/replaced relay no longer owns the slot. Never resurrect a source
        // that its scoped owner has already unregistered, including deferred removal.
        if(!Has(proxy))return;
        remove(proxy);
        if(SourceActive&&!Has(source))add(source);
    }
}
