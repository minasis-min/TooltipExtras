namespace TooltipExtras;
// Failed requests are also throttled. Target changes immediately discard the previous target's data.
internal sealed class RefreshCache
{
    private ulong id;
    private nint addon;
    private long next;
    public byte[]? Content { get; private set; }
    public bool ShouldRefresh(ulong target, nint address, long now)
    {
        if (id != target || addon != address) { Clear(); id = target; addon = address; }
        if (target == 0 || address == 0 || now < next) return false;
        next = now + 1000;
        return true;
    }
    public void Set(byte[]? value)
    {
        if (value == null) Content = null;
        else if (Content == null || !Content.AsSpan().SequenceEqual(value)) Content = value;
    }
    public void Clear() { id = 0; addon = 0; next = 0; Content = null; }
}
