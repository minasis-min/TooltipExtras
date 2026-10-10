namespace TooltipExtras;

internal static class DelegateSlot
{
    public static Delegate? Replace(Delegate current, Delegate from, Delegate to)
        => Delegate.Combine(current.GetInvocationList().Select(d => d.Equals(from) ? to : d).ToArray());
}
