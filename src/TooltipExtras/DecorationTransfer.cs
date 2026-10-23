namespace TooltipExtras;

// Tracks only the observed decoration callbacks within one producer dispatch.
// Unknown output in the monitored fields keeps the producer's native decoration.
internal sealed class DecorationTransfer
{
    private byte[]?[]? baseline, expected;
    public void Clear() { baseline = expected = null; }
    public void Begin(byte[]?[] values) { baseline = expected = values; }
    public static bool Same(byte[]?[] left, byte[]?[] right) => left.Length == right.Length
        && left.Zip(right).All(p => p.First == null ? p.Second == null : p.Second != null && p.First.AsSpan().SequenceEqual(p.Second));
    public void Observe(byte[]?[] before, byte[]?[] after)
    {
        if (expected == null || !Same(expected, before)
            || !Same(before, after) && !AppendDelta.TrySelect(before, after, out _, out _)) { Clear(); return; }
        expected = after;
    }
    public TextTransferPlan? Finish(int[] fields, byte[]?[] actual)
    {
        var start = baseline; var end = expected; Clear();
        return start != null && end != null && Same(end, actual)
            ? TextTransferPlan.Create(fields, start, actual, true, out _) : null;
    }
}
