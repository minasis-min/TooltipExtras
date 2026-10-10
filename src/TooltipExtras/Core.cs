using System.Numerics;
namespace TooltipExtras;

public enum TooltipKind { Item, Action }
public enum PanelSide { Below, Above }
public readonly record struct TooltipKey(TooltipKind Kind, ulong Id, int DetailKind = 0);
public readonly record struct Bounds(Vector2 Position, Vector2 Size);
public static class Placement
{
    // OffsetY points outwards: even a negative adjustment cannot overlap the native tooltip.
    public static Vector2 Position(Bounds native, Vector2 panel, PanelSide side, float gap, Vector2 offset)
    {
        var distance = Math.Max(0, gap + offset.Y);
        return new(native.Position.X + offset.X, side == PanelSide.Below
            ? native.Position.Y + native.Size.Y + distance : native.Position.Y - distance - panel.Y);
    }
    public static Bounds ToViewport(Bounds native, Vector2 client, Vector2 viewportPos, Vector2 viewportSize)
    {
        if (client.X <= 0 || client.Y <= 0) throw new ArgumentOutOfRangeException(nameof(client));
        var scale = viewportSize / client;
        return new(viewportPos + native.Position * scale, native.Size * scale);
    }
}
public readonly record struct PriceGeometry(ushort Height, ushort NodeHeight, float NodeY, float FooterY)
{
    public bool TryRemove(out ushort height, out float footerY)
    {
        var delta = NodeHeight + 4;
        height = 0; footerY = 0;
        if (NodeHeight == 0 || delta >= Height || Height > 16000 || !float.IsFinite(FooterY)
            || !float.IsFinite(NodeY) || Math.Abs(NodeY - (Height - delta - 8)) > 0.01f) return false;
        height = (ushort)(Height - delta);
        footerY = FooterY - delta;
        return height >= 24;
    }
}
public sealed class ContextEpoch
{
    public TooltipKey Key { get; private set; }
    public nint Addon { get; private set; }
    public long Generation { get; private set; }
    public bool Active { get; private set; }
    public long Observe(TooltipKey key, nint addon, bool visible)
    {
        if (!visible || key.Id == 0 || addon == 0) { Invalidate(); return 0; }
        if (!Active || Key != key || Addon != addon) { Generation++; Key = key; Addon = addon; }
        Active = true; return Generation;
    }
    public bool Matches(TooltipKey key, nint addon, long generation) => Active && Key == key && Addon == addon && Generation == generation;
    public void Invalidate() { if (Active) Generation++; Active = false; Addon = 0; }
}
