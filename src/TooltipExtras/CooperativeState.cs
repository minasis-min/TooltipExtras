namespace TooltipExtras;

// Content lifetime is the visible target, not an OnRequestedUpdate call or a Framework frame.
// A refresh may update an unrelated field without rerunning the producer.
public sealed class CooperativeState
{
    private readonly Dictionary<TooltipKind, Entry> entries = new();
    private long serial;
    public long Accepted { get; private set; }
    public long Requests { get; private set; }
    private sealed class Entry(TooltipKey key, nint addon)
    {
        public readonly TooltipKey Key = key;
        public readonly nint Addon = addon;
        public long Token;
        public bool Accepting, Ready;
        public readonly SortedDictionary<string, byte[]> Parts = new(StringComparer.Ordinal);
        public byte[]? Combined;
    }
    public void Requested(TooltipKey key, nint addon)
    {
        if (key.Id == 0 || addon == 0) { Clear(key.Kind); return; }
        if (!entries.TryGetValue(key.Kind, out var e) || e.Key != key || e.Addon != addon)
            entries[key.Kind] = e = new Entry(key, addon);
        e.Token = ++serial; e.Accepting = true; Requests++;
    }
    public void Observe(TooltipKey key, nint addon, bool visible)
    {
        if (!visible || key.Id == 0 || addon == 0 || entries.TryGetValue(key.Kind, out var e) && (e.Key != key || e.Addon != addon)) Clear(key.Kind);
    }
    public void PreDraw(TooltipKey key, nint addon)
    {
        Observe(key, addon, true);
        if (entries.TryGetValue(key.Kind, out var e)) e.Ready = true;
    }
    public long Begin(TooltipKind kind, ulong id) => entries.TryGetValue(kind, out var e) && e.Accepting && e.Key.Id == id ? e.Token : 0;
    public bool Publish(string source, TooltipKind kind, ulong id, long token, byte[] bytes)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Length > 128 || bytes == null || bytes.Length > 32768
            || token == 0 || !entries.TryGetValue(kind, out var e) || !e.Accepting || e.Token != token || e.Key.Id != id
            || e.Parts.Count >= 16 && !e.Parts.ContainsKey(source)) return false;
        if (bytes.Length == 0) { if (e.Parts.Remove(source)) e.Combined = null; }
        else if (!e.Parts.TryGetValue(source, out var old) || !old.AsSpan().SequenceEqual(bytes))
        { e.Parts[source] = bytes.ToArray(); e.Combined = null; }
        Accepted++; return true;
    }
    public byte[]? Get(TooltipKey key, nint addon, bool visible, Func<string, byte[], byte[]>? format = null)
    {
        Observe(key, addon, visible);
        if (!entries.TryGetValue(key.Kind, out var e) || !e.Ready || e.Parts.Count == 0) return null;
        return e.Combined ??= Join(e.Parts.Select(p => format == null ? p.Value : format(p.Key, p.Value)));
    }
    public void EndRequested(TooltipKind kind) { if (entries.TryGetValue(kind, out var e)) e.Accepting = false; }
    public string Describe(TooltipKind kind) => entries.TryGetValue(kind, out var e)
        ? $"{kind}: id={e.Key.Id} detail={e.Key.DetailKind} token={e.Token} accepting={e.Accepting} ready={e.Ready} sources={e.Parts.Count} bytes={e.Parts.Values.Sum(p => p.Length)}"
        : $"{kind}: empty";
    public void Clear(TooltipKind kind) => entries.Remove(kind);
    public void InvalidatePresentation() { foreach (var entry in entries.Values) entry.Combined = null; }
    public void Clear() => entries.Clear();
    public static byte[] Join(IEnumerable<byte[]> parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts) { if (part.Length == 0) continue; if (stream.Length > 0) { stream.WriteByte(10); stream.WriteByte(10); } stream.Write(part); }
        return stream.ToArray();
    }
}
