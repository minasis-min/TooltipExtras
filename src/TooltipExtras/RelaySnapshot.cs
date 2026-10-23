namespace TooltipExtras;

// A requested update need not run the producer again. Retain its last result for
// the same visible target; an actual callback can replace/remove one section.
internal sealed class RelaySnapshot(bool compact = false, int sectionCount = 3)
{
    private readonly byte[]?[] sections = new byte[sectionCount][];
    private ulong item;
    private nint addon;
    private byte[]? combined;
    private bool dirty;
    public int Count => sections.Count(s => s != null);
    public void Begin(ulong id, nint address)
    {
        if (id == 0 || address == 0 || id != item || address != addon) Clear();
        item = id; addon = address;
    }
    public void Set(int section, byte[]? value)
    {
        if (value is { Length: 0 }) value = null;
        var old = sections[section];
        if (old == null && value == null || old != null && value != null && old.AsSpan().SequenceEqual(value)) return;
        sections[section] = value; dirty = true;
    }
    public byte[]? Get(ulong id, nint address)
    {
        if (id == 0 || address == 0 || id != item || address != addon) { Clear(); return null; }
        if (dirty)
        {
            var parts = sections.OfType<byte[]>().ToArray();
            combined = parts.Length == 0 ? null : compact ? JoinRows(parts) : CooperativeState.Join(parts);
            dirty = false;
        }
        return combined;
    }
    private static byte[] JoinRows(byte[][] parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts) { if (stream.Length > 0) stream.WriteByte(10); stream.Write(part); }
        return stream.ToArray();
    }
    public void Clear()
    {
        item = 0; addon = 0; Array.Clear(sections); combined = null; dirty = false;
    }
}
