using System.Collections;
namespace TooltipExtras;

// Only used inside the synchronous native generation call. Settings/Draw never
// see the proxy registrations; every exit restores owned slots in finally.
internal sealed class ScopedRegistration : IDisposable
{
    private readonly List<(IList List,object Original,object Proxy)> changed = [];
    public void Replace(IList list, object original, object proxy)
    {
        var index = list.IndexOf(original);
        if (index < 0) throw new InvalidOperationException("元の追記登録がありません");
        if(list.Cast<object>().Any(entry=>ReferenceEquals(entry,proxy)))throw new InvalidOperationException("代理登録が重複しています");
        changed.Add((list,original,proxy)); list[index] = proxy;
    }
    public void Dispose()
    {
        var cleanup = new Cleanup();
        for (var i = changed.Count-1; i >= 0; i--)
        {
            var slot = changed[i];
            cleanup.Try("generation slot", () => AppendDelta.RestoreSlot(slot.List,slot.Proxy,slot.Original));
        }
        changed.Clear(); cleanup.ThrowIfFailed();
    }
}
