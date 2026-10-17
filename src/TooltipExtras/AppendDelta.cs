using System.Collections;
namespace TooltipExtras;

internal static class AppendDelta
{
    // A single audited field may grow. A rewrite or a second changed field is not an append.
    public static bool TrySelect(byte[]?[] before, byte[]?[] after, out int field, out byte[] suffix)
    {
        field = -1; suffix = [];
        if (before.Length != after.Length) return false;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] == null && after[i] == null) continue;
            if (before[i] == null || after[i] == null) return false;
            if (before[i]!.AsSpan().SequenceEqual(after[i])) continue;
            if (field >= 0 || !after[i]!.AsSpan().StartsWith(before[i])) return false;
            field = i;
        }
        if (field < 0) return false;
        suffix = after[field]!.AsSpan(before[field]!.Length).ToArray();
        return suffix.Length > 0;
    }
    public static void RestoreSlot(IList list, object proxy, object original)
    {
        for (var i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], proxy)) list[i] = original;
    }
}
