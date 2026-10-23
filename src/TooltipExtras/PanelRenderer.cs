using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiSeStringRenderer;
using Dalamud.Interface.Utility;
namespace TooltipExtras;

internal static unsafe class PanelRenderer
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    private static byte[]? cached;
    private static float cachedWidth, cachedFont;
    private static nint cachedFontHandle;
    private static Vector2 measured;
    public static bool Draw(byte[] bytes, Bounds native, Settings settings, IReadOnlyList<PanelImage>? images = null)
    {
        var vp = ImGui.GetMainViewport();
        var hwnd = (nint)(vp.PlatformHandleRaw != null ? vp.PlatformHandleRaw : vp.PlatformHandle);
        GetWindowThreadProcessId(hwnd, out var process);
        if (hwnd == 0 || process != Environment.ProcessId || IsIconic(hwnd) || !GetClientRect(hwnd, out var rect)) return false;
        var client = new Vector2(rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (client.X <= 0 || client.Y <= 0) return false;
        native = Placement.ToViewport(native, client, vp.Pos, vp.Size);
        var fontSize = settings.FontSize * vp.DpiScale;
        var padding = 8 * vp.DpiScale;
        var wrap = Math.Max(40, native.Size.X - padding * 2);
        var font = ImGui.GetFont();
        if (!ReferenceEquals(cached, bytes) || cachedWidth != wrap || cachedFont != fontSize || cachedFontHandle != (nint)font.Handle)
        {
            measured = ImGuiHelpers.SeStringWrapped(bytes, new SeStringDrawParams
            {
                TargetDrawList = default(ImDrawListPtr),
                Font = font,
                FontSize = fontSize,
                WrapWidth = wrap,
                ScreenOffset = Vector2.Zero
            }).Size;
            cached = bytes; cachedWidth = wrap; cachedFont = fontSize; cachedFontHandle = (nint)font.Handle;
        }
        var imageRows=new List<(Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap Texture,Vector2 Size)>();
        var imageHeight=0f;
        if(images!=null)foreach(var preview in images)
        {
            if(!preview.Texture.TryGetWrap(out var texture,out _)||preview.Width<=0||preview.Height<=0)continue;
            var width=Math.Min(wrap,preview.Width*vp.DpiScale);var height=width*preview.Height/preview.Width;
            if(!float.IsFinite(height)||height<=0||height>4096)continue;
            imageRows.Add((texture,new(width,height)));imageHeight+=height+padding;
        }
        var size = new Vector2(wrap + padding * 2, measured.Y + padding * 2+imageHeight);
        var pos = Placement.Position(native, size, settings.Side, settings.Gap * vp.DpiScale,
            new Vector2(settings.OffsetX, settings.OffsetY) * vp.DpiScale);
        // Explicit draw-list positions prevent ImGui window clamping / tooltip auto-flipping.
        var draw = ImGui.GetForegroundDrawList(vp);
        draw.PushClipRect(vp.Pos, vp.Pos + vp.Size, true);
        try
        {
            draw.AddRectFilled(pos, pos + size, 0xE6222222, 4);
            draw.AddRect(pos, pos + size, 0xCC787878, 4);
            ImGuiHelpers.SeStringWrapped(bytes, new SeStringDrawParams
            {
                TargetDrawList = draw,
                Font = font,
                FontSize = fontSize,
                WrapWidth = wrap,
                ScreenOffset = pos + new Vector2(padding),
                LinkUnderlineThickness = 0
            }); // No ImGui ID, item, window, or input receiver: links are display-only.
            var imageY=pos.Y+padding+measured.Y;
            foreach(var row in imageRows)
            {
                imageY+=padding;var left=pos.X+(size.X-row.Size.X)/2;
                draw.AddImage(row.Texture.Handle,new Vector2(left,imageY),new Vector2(left+row.Size.X,imageY+row.Size.Y));imageY+=row.Size.Y;
            }
        }
        finally { draw.PopClipRect(); }
        return true;
    }
}
