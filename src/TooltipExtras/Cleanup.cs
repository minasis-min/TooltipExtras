namespace TooltipExtras;

// Run every cleanup step before reporting any failures to the host.
internal sealed class Cleanup
{
    private readonly List<Exception> errors = [];
    public void Try(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { errors.Add(new InvalidOperationException(name, ex)); }
    }
    public void ThrowIfFailed()
    {
        if (errors.Count != 0) throw new AggregateException("Tooltip Extras cleanup failed", errors);
    }
}
