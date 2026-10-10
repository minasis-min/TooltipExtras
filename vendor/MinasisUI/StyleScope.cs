using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MinasisUI;

/// <summary>Adapter allows push/pop exception tests without a game or ImGui context.</summary>
public interface IStyleStack
{
    void Color(ImGuiCol slot, Vector4 color);
    void Variable(ImGuiStyleVar slot, float value);
    void Variable(ImGuiStyleVar slot, Vector2 value);
    void PopColors(int count);
    void PopVariables(int count);
}
internal sealed class ImGuiStyleStack : IStyleStack
{
    internal static readonly ImGuiStyleStack Instance = new();
    public void Color(ImGuiCol s, Vector4 c) => ImGui.PushStyleColor(s, c);
    public void Variable(ImGuiStyleVar s, float v) => ImGui.PushStyleVar(s, v);
    public void Variable(ImGuiStyleVar s, Vector2 v) => ImGui.PushStyleVar(s, v);
    public void PopColors(int n) => ImGui.PopStyleColor(n);
    public void PopVariables(int n) => ImGui.PopStyleVar(n);
}

public sealed class StyleScope : IDisposable
{
    private readonly IStyleStack stack;
    private int colors, variables;
    public StyleScope(UiTheme theme, UiMetrics metrics, IStyleStack? stack = null)
    {
        this.stack = stack ?? ImGuiStyleStack.Instance;
        try
        {
            C(ImGuiCol.Text, theme.Text); C(ImGuiCol.TextDisabled, theme.Muted);
            C(ImGuiCol.WindowBg, theme.Background); C(ImGuiCol.ChildBg, Vector4.Zero); C(ImGuiCol.PopupBg, theme.Panel);
            C(ImGuiCol.Border, theme.Border); C(ImGuiCol.BorderShadow, Vector4.Zero);
            C(ImGuiCol.FrameBg, theme.Inset); C(ImGuiCol.FrameBgHovered, theme.Hover); C(ImGuiCol.FrameBgActive, theme.Pressed);
            C(ImGuiCol.Button, theme.Panel); C(ImGuiCol.ButtonHovered, theme.Hover); C(ImGuiCol.ButtonActive, theme.Pressed);
            C(ImGuiCol.Header, theme.Selected); C(ImGuiCol.HeaderHovered, theme.Hover); C(ImGuiCol.HeaderActive, theme.Pressed);
            C(ImGuiCol.CheckMark, theme.Accent); C(ImGuiCol.SliderGrab, theme.Accent); C(ImGuiCol.SliderGrabActive, theme.AccentHover);
            C(ImGuiCol.TitleBg, theme.Background); C(ImGuiCol.TitleBgActive, theme.Panel); C(ImGuiCol.TitleBgCollapsed, theme.Background);
            C(ImGuiCol.Separator, theme.Border); C(ImGuiCol.SeparatorHovered, theme.AccentHover); C(ImGuiCol.SeparatorActive, theme.Accent);
            C(ImGuiCol.ScrollbarBg, theme.Inset); C(ImGuiCol.ScrollbarGrab, theme.Border);
            C(ImGuiCol.ScrollbarGrabHovered, theme.Muted); C(ImGuiCol.ScrollbarGrabActive, theme.Accent);
            C(ImGuiCol.ResizeGrip, theme.Border); C(ImGuiCol.ResizeGripHovered, theme.AccentHover); C(ImGuiCol.ResizeGripActive, theme.Accent);
            C(ImGuiCol.TextSelectedBg, theme.Selected); C(ImGuiCol.NavHighlight, theme.Focus);
            C(ImGuiCol.ModalWindowDimBg, new Vector4(0, 0, 0, .48f));
            V(ImGuiStyleVar.WindowPadding, metrics.Padding); V(ImGuiStyleVar.FramePadding, metrics.FramePadding);
            V(ImGuiStyleVar.ItemSpacing, metrics.Spacing); V(ImGuiStyleVar.ItemInnerSpacing, metrics.Spacing);
            V(ImGuiStyleVar.CellPadding, new Vector2(metrics.Px(4), metrics.Px(5)));
            V(ImGuiStyleVar.WindowRounding, metrics.PanelRounding); V(ImGuiStyleVar.ChildRounding, metrics.PanelRounding);
            V(ImGuiStyleVar.PopupRounding, metrics.PanelRounding); V(ImGuiStyleVar.FrameRounding, metrics.FrameRounding);
            V(ImGuiStyleVar.GrabRounding, metrics.FrameRounding); V(ImGuiStyleVar.ScrollbarRounding, metrics.FrameRounding);
            V(ImGuiStyleVar.WindowBorderSize, metrics.Border); V(ImGuiStyleVar.ChildBorderSize, metrics.Border);
            V(ImGuiStyleVar.PopupBorderSize, metrics.Border); V(ImGuiStyleVar.FrameBorderSize, metrics.Border);
            V(ImGuiStyleVar.ScrollbarSize, metrics.Px(12)); V(ImGuiStyleVar.GrabMinSize, metrics.Px(10));
            V(ImGuiStyleVar.DisabledAlpha, theme.DisabledAlpha);
        }
        catch { Dispose(); throw; }
    }
    private void C(ImGuiCol s, Vector4 c) { stack.Color(s, c); colors++; }
    private void V(ImGuiStyleVar s, float v) { stack.Variable(s, v); variables++; }
    private void V(ImGuiStyleVar s, Vector2 v) { stack.Variable(s, v); variables++; }
    public void Dispose()
    {
        if (variables != 0) { stack.PopVariables(variables); variables = 0; }
        if (colors != 0) { stack.PopColors(colors); colors = 0; }
    }
}
