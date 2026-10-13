using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace MinasisUI;

public sealed record UiChoice(string Id, string Label);
public sealed record UiTreeNode(string Id, string Label, IReadOnlyList<UiTreeNode>? Children = null);

/// <summary>One instance per consuming plugin. Call only on the ImGui drawing thread.</summary>
public sealed class Ui : IDisposable
{
    private ImGuiListClipperPtr clipper;
    private bool hasClipper, disposed;
    public UiTheme Theme { get; set; } = UiTheme.SoftCharcoal;
    /// <summary>Host-provided native font scope; Ui never owns or destroys its fonts.</summary>
    public Func<bool, IDisposable>? HeadingFontScope { get; set; }
    public UiDimensions Dimensions { get; set; } = new();
    /// <summary>Total dimension scale: Dalamud global scale × optional local preview scale.</summary>
    public float Scale { get; set; } = 1;
    /// <summary>Legacy compatibility option; rendering is always flat.</summary>
    public bool Shadows { get; set; } = false;
    public int LastListVertices { get; private set; }
    public int LastListIndices { get; private set; }
    public UiMetrics Metrics => Dimensions.At(Scale);
    public StyleScope PushTheme()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return new(Theme, Metrics);
    }

    private readonly struct ControlScope : IDisposable
    {
        public ControlScope(string id, bool enabled)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ImGui.PushID(id); ImGui.BeginDisabled(!enabled);
        }
        public void Dispose() { ImGui.EndDisabled(); ImGui.PopID(); }
    }
    private static string Label(string text) => text + "###control";
    public void Heading(string text, bool large = true)
    {
        // Preserve the first heading and intentional spacing; separate subsequent sections.
        var window = ImGui.GetCurrentContext().CurrentWindow;
        var cursor = ImGui.GetCursorScreenPos();
        var lastSize = ImGui.GetItemRectSize();
        var explicitSpacer = lastSize.X <= 0 && lastSize.Y >= Metrics.Px(Dimensions.SectionGap / 2);
        if (!explicitSpacer && window.DC.CursorMaxPos.Y > window.DC.CursorStartPos.Y + .5f)
        {
            var precedingBottom = ImGui.GetItemRectMax().Y;
            if (precedingBottom < window.DC.CursorStartPos.Y || precedingBottom > cursor.Y)
                precedingBottom = window.DC.CursorMaxPos.Y;
            var targetY = precedingBottom + Metrics.Px(Dimensions.SectionGap + Dimensions.Gap);
            if (cursor.Y < targetY) ImGui.SetCursorScreenPos(new Vector2(cursor.X, targetY));
        }
        // Push a separately baked font instead of stretching atlas glyphs.
        using (HeadingFontScope?.Invoke(large))
        {
            var position = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(new Vector2(MathF.Round(position.X), MathF.Round(position.Y)));
            ImGui.TextWrapped(text);
        }
        if (large) ImGui.Separator();
    }
    public void Text(string text) => ImGui.TextWrapped(text);
    public void Muted(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.Muted);
        try { ImGui.TextWrapped(text); } finally { ImGui.PopStyleColor(); }
    }
    public void Separator()
    {
        ImGui.Dummy(new Vector2(0, Metrics.Px(Dimensions.SectionGap / 2)));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, Metrics.Px(Dimensions.SectionGap / 2)));
    }
    public bool Button(string id, string label, ButtonKind kind = ButtonKind.Secondary, bool enabled = true, float width = 0)
    {
        using var control = new ControlScope(id, enabled);
        var primary = kind == ButtonKind.Primary;
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, primary ? 0 : Metrics.Border);
        if (primary)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.AccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.AccentPressed);
            ImGui.PushStyleColor(ImGuiCol.Text, Theme.OnAccent);
        }
        try
        {
            var available = Math.Max(1, ImGui.GetContentRegionAvail().X);
            var buttonWidth = width == 0 ? Math.Min(available, ImGui.CalcTextSize(label).X + Metrics.FramePadding.X * 2) : Math.Min(available, Metrics.Px(width));
            var wrapWidth = Math.Max(1, buttonWidth - Metrics.FramePadding.X * 2);
            var wrapped = ImGui.CalcTextSize(label).X > wrapWidth;
            bool changed;
            if (wrapped)
            {
                var size = ImGui.CalcTextSize(label, false, wrapWidth);
                changed = ImGui.Button("###control", new Vector2(buttonWidth, size.Y + Metrics.FramePadding.Y * 2));
                ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetItemRectMin() + Metrics.FramePadding,
                    ImGui.GetColorU32(primary ? Theme.OnAccent : Theme.Text), label, wrapWidth);
            }
            else changed = ImGui.Button(Label(label), new Vector2(buttonWidth, 0));
            FocusOutline(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), Metrics.FrameRounding, ImGui.IsItemFocused());
            return changed;
        }
        finally { if (primary) ImGui.PopStyleColor(4); ImGui.PopStyleVar(); ImGui.PopStyleColor(); }
    }
    /// <summary>Pass an icon font owned by the host; the kit never loads or disposes it.</summary>
    public bool IconButton(string id, string glyph, string tooltip, ImFontPtr font, bool enabled = true)
    {
        ImGui.PushFont(font);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(.5f));
        bool clicked;
        try { clicked = Button(id, glyph, enabled: enabled); }
        finally { ImGui.PopStyleVar(); ImGui.PopFont(); }
        Tooltip(tooltip);
        return clicked;
    }
    public bool Checkbox(string id, string label, ref bool value, bool enabled = true)
    {
        using var c = new ControlScope(id, enabled);
        using var smooth = new SmoothDrawing();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        foreach (var color in new[] { ImGuiCol.FrameBg, ImGuiCol.FrameBgHovered, ImGuiCol.FrameBgActive, ImGuiCol.CheckMark, ImGuiCol.NavHighlight })
            ImGui.PushStyleColor(color, Vector4.Zero);
        bool changed;
        try { changed = ImGui.Checkbox(Label(label), ref value); }
        finally { ImGui.PopStyleColor(5); ImGui.PopStyleVar(); }
        var a = ImGui.GetItemRectMin(); var size = ImGui.GetFrameHeight(); var b = a + new Vector2(size);
        var draw = ImGui.GetWindowDrawList();
        var fill = value ? (ImGui.IsItemActive() ? Theme.AccentPressed : ImGui.IsItemHovered() ? Theme.AccentHover : Theme.Accent) : Theme.Inset;
        draw.AddRectFilled(a, b, ImGui.GetColorU32(fill), Metrics.FrameRounding);
        if (!value) draw.AddRect(a + new Vector2(.5f), b - new Vector2(.5f), ImGui.GetColorU32(Theme.Border), Metrics.FrameRounding);
        if (value)
        {
            draw.PathLineTo(a + new Vector2(size * .24f, size * .51f));
            draw.PathLineTo(a + new Vector2(size * .43f, size * .70f));
            draw.PathLineTo(a + new Vector2(size * .77f, size * .30f));
            draw.PathStroke(ImGui.GetColorU32(Theme.OnAccent), ImDrawFlags.None, Math.Max(2, size * .095f));
        }
        FocusOutline(a, b, Metrics.FrameRounding, ImGui.IsItemFocused() && ImGui.GetIO().NavVisible);
        return changed;
    }
    private readonly struct SmoothDrawing : IDisposable
    {
        private readonly ImDrawListPtr draw;
        private readonly ImDrawListFlags flags;
        public SmoothDrawing()
        {
            draw = ImGui.GetWindowDrawList(); flags = draw.Flags;
            draw.Flags |= ImDrawListFlags.AntiAliasedFill | ImDrawListFlags.AntiAliasedLines;
        }
        public void Dispose() => draw.Flags = flags;
    }
    public bool Toggle(string id, string label, ref bool value, bool enabled = true) => Toggle(id, label, ref value, enabled, false);
    public bool Toggle(string id, string label, ref bool value, bool enabled, bool alignRight)
    {
        using var c = new ControlScope(id, enabled);
        using var smooth = new SmoothDrawing();
        var height = ImGui.GetFrameHeight();
        var width = height * 1.75f;
        if (alignRight) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - width));
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Vector4.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        bool clicked;
        try { clicked = ImGui.Button("###control", new Vector2(width, height)); }
        finally { ImGui.PopStyleVar(); ImGui.PopStyleColor(4); }
        if (clicked) value = !value;
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        var track = ImGui.IsItemActive() ? (value ? Theme.AccentPressed : Theme.Pressed)
            : value ? (ImGui.IsItemHovered() ? Theme.AccentHover : Theme.Accent) : ImGui.IsItemHovered() ? Theme.Hover : Theme.Inset;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(track), height / 2);
        if (!value) draw.AddRect(min + new Vector2(.5f), max - new Vector2(.5f), ImGui.GetColorU32(Theme.Border), height / 2);
        FocusOutline(min, max, height / 2, ImGui.IsItemFocused() && ImGui.GetIO().NavVisible);
        var radius = height * .33f;
        var center = new Vector2(value ? max.X - height / 2 : min.X + height / 2, min.Y + height / 2);
        draw.AddCircleFilled(center, radius, ImGui.GetColorU32(Theme.SwitchThumb), 48);
        if (!string.IsNullOrEmpty(label)) { ImGui.SameLine(); ImGui.TextUnformatted(label + (value ? "：ON" : "：OFF")); }
        return clicked;
    }
    public bool Radio(string id, string label, ref int value, int option, bool enabled = true)
    {
        using var c = new ControlScope(id, enabled);
        if (!ImGui.RadioButton(Label(label), value == option)) return false;
        value = option; return true;
    }
    public bool InputText(string id, string label, ref string value, int maxLength = 512, bool enabled = true)
    {
        using var c = new ControlScope(id, enabled);
        LabelAbove(label);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Vector4.Zero);
        bool changed;
        try { changed = ImGui.InputText("###control", ref value, maxLength); }
        finally { ImGui.PopStyleColor(); }
        // LastItemData is the outer input rectangle, without an appended label.
        // Never recalculate CalcItemWidth after InputText consumes SetNextItemWidth.
        ShadeItem(SurfaceKind.Inset); return changed;
    }
    public bool InputMultiline(string id, string label, ref string value, float height = 120, int maxLength = 8192, bool enabled = true)
    {
        using var c = new ControlScope(id, enabled);
        ImGui.TextUnformatted(label);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Vector4.Zero);
        bool changed;
        try { changed = ImGui.InputTextMultiline("###control", ref value, maxLength,
            new Vector2(ImGui.GetContentRegionAvail().X, Metrics.Px(height))); }
        finally { ImGui.PopStyleColor(); }
        ShadeItem(SurfaceKind.Inset); return changed;
    }
    // Preserve the original signatures for already compiled consumers.
    public bool InputInt(string id, string label, ref int value, bool enabled = true) => InputInt(id, label, ref value, 1, enabled, int.MinValue, int.MaxValue);
    public bool InputFloat(string id, string label, ref float value, bool enabled = true) => InputFloat(id, label, ref value, .1f, enabled, -float.MaxValue, float.MaxValue, "%.2f");
    /// <summary>Numeric entry followed by separate minus/plus buttons. Limits apply to entry and steps.</summary>
    public bool InputInt(string id, string label, ref int value, int step, bool enabled = true, int min = int.MinValue, int max = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        if (min > max) throw new ArgumentException("min must not exceed max.");
        using var c = new ControlScope(id, enabled);
        LabelAbove(label);
        var sizes = StepSizes();
        ImGui.BeginGroup();
        try
        {
            ImGui.SetNextItemWidth(sizes.Input);
            var entered = value;
            var changed = ImGui.InputInt("###control", ref entered, 0, 0, "%d", ImGuiInputTextFlags.AutoSelectAll);
            if (changed) value = Math.Clamp(entered, min, max);
            ShadeItem(SurfaceKind.Inset);
            ImGui.SameLine(0, sizes.Gap);
            if (Button("decrease", "-", enabled: value > min, width: sizes.Button / Scale))
            { value = (int)Math.Clamp((long)value - step, min, max); changed = true; }
            ImGui.SameLine(0, sizes.Gap);
            if (Button("increase", "+", enabled: value < max, width: sizes.Button / Scale))
            { value = (int)Math.Clamp((long)value + step, min, max); changed = true; }
            return changed;
        }
        finally { ImGui.EndGroup(); }
    }
    public bool InputFloat(string id, string label, ref float value, float step, bool enabled = true,
        float min = -float.MaxValue, float max = float.MaxValue, string format = "%.2f")
    {
        if (!float.IsFinite(step) || step <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        if (!float.IsFinite(min) || !float.IsFinite(max) || min > max) throw new ArgumentException("Invalid numeric range.");
        using var c = new ControlScope(id, enabled);
        LabelAbove(label);
        var sizes = StepSizes();
        ImGui.BeginGroup();
        try
        {
            ImGui.SetNextItemWidth(sizes.Input);
            var entered = value;
            var changed = ImGui.InputFloat("###control", ref entered, 0, 0, format, ImGuiInputTextFlags.AutoSelectAll) && float.IsFinite(entered);
            if (changed) value = Math.Clamp(entered, min, max);
            ShadeItem(SurfaceKind.Inset);
            ImGui.SameLine(0, sizes.Gap);
            if (Button("decrease", "-", enabled: value > min, width: sizes.Button / Scale))
            { value = (float)Math.Clamp((double)value - step, min, max); changed = true; }
            ImGui.SameLine(0, sizes.Gap);
            if (Button("increase", "+", enabled: value < max, width: sizes.Button / Scale))
            { value = (float)Math.Clamp((double)value + step, min, max); changed = true; }
            return changed;
        }
        finally { ImGui.EndGroup(); }
    }
    private (float Input, float Button, float Gap) StepSizes()
    {
        var width = Math.Max(1, ImGui.CalcItemWidth());
        var gap = Math.Min(Metrics.Spacing.X, width / 12);
        var button = Math.Min(ImGui.GetFrameHeight(), (width - gap * 2) / 3);
        return (Math.Max(1, width - button * 2 - gap * 2), button, gap);
    }
    /// <summary>Shared sidebar hierarchy: plugin name, divider, optional version, divider.</summary>
    public void SidebarHeader(string pluginName, string? version = null)
    {
        Heading(pluginName);
        if (!string.IsNullOrEmpty(version)) { Muted(version); Separator(); }
    }
    private static readonly UiChoice[] StyleChoices = [new("minasis", "MinasisUI"), new("dalamud", "Dalamud標準UI")];
    /// <summary>Call after navigation rows. Returns a selection; the host applies and persists its UI mode.</summary>
    public bool UiStyleSelector(string id, ref bool useMinasisUI)
    {
        Separator();
        ImGui.SetNextItemWidth(-1);
        var selected = useMinasisUI ? 0 : 1;
        if (!Dropdown(id, "UI", ref selected, StyleChoices)) return false;
        useMinasisUI = selected == 0;
        return true;
    }
    public bool Slider(string id, string label, ref float value, float min = 0, float max = 100, string format = "%.2f%%", bool enabled = true)
    {
        using var c = new ControlScope(id, enabled);
        LabelAbove("");
        using var smooth = new SmoothDrawing();
        var totalWidth = ImGui.CalcItemWidth();
        var header = ImGui.GetCursorScreenPos();
        var entryWidth = Math.Min(totalWidth, Math.Max(Metrics.Px(90), totalWidth * .24f));
        var labelWidth = Math.Max(0, totalWidth - entryWidth);
        ImGui.Dummy(new Vector2(labelWidth, ImGui.GetFrameHeight()));
        if (!string.IsNullOrEmpty(label)) ImGui.GetWindowDrawList().AddText(header + new Vector2(0, Metrics.FramePadding.Y), ImGui.GetColorU32(Theme.Text), label);
        ImGui.SameLine(0, 0); ImGui.SetNextItemWidth(entryWidth);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Theme.Inset);
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Theme.Inset);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Vector4.Zero);
        bool entryChanged;
        var entered = value;
        try { entryChanged = ImGui.InputFloat("###value", ref entered, 0, 0, format, ImGuiInputTextFlags.AutoSelectAll); }
        finally { ImGui.PopStyleColor(4); ImGui.PopStyleVar(); }
        var changed = entryChanged && float.IsFinite(entered);
        if (changed) value = Math.Clamp(entered, min, max);
        Tooltip("クリックして小数を入力。値はスライダーの範囲内に収まります。");
        ImGui.SetNextItemWidth(totalWidth);
        var fontHeight = ImGui.GetFontSize();
        var padding = ImGui.GetStyle().FramePadding;
        var background = ImGui.GetStyle().Colors[(int)ImGuiCol.ChildBg];
        if (background.W == 0) background = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Vector4.Zero);
        try { changed |= ImGui.SliderFloat("###control", ref value, min, max, format); }
        finally { ImGui.PopStyleColor(); }
        var itemId = ImGui.GetCurrentContext().LastItemData.ID;
        // Ctrl+click / keyboard scalar editing stays native, including its caret, IME and selection.
        if (ImGui.GetCurrentContext().TempInputId == itemId) return changed;
        var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        background.W = 1;
        draw.AddRectFilled(a, b, ImGui.ColorConvertFloat4ToU32(background));
        var radius = Math.Max(Metrics.Px(8), fontHeight * .48f);
        var y = b.Y - padding.Y - radius;
        // Align the visual thumb with the native SliderFloat's usable range.
        var inset = 2 + ImGui.GetStyle().GrabMinSize / 2;
        var left = a.X + inset; var right = Math.Max(left, b.X - inset);
        var fraction = max > min ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;
        var center = new Vector2(left + (right - left) * fraction, y);
        var halfTrack = Metrics.Px(2.5f);
        draw.AddRectFilled(new(left, y - halfTrack), new(right, y + halfTrack), ImGui.GetColorU32(Theme.Inset), halfTrack);
        draw.AddRect(new(left, y - halfTrack), new(right, y + halfTrack), ImGui.GetColorU32(Theme.Border), halfTrack);
        if (center.X > left) draw.AddRectFilled(new(left, y - halfTrack), new(center.X, y + halfTrack), ImGui.GetColorU32(Theme.Accent), halfTrack);
        var thumbColor = ImGui.IsItemActive() ? Theme.AccentPressed : ImGui.IsItemHovered() ? Theme.AccentHover : Theme.Accent;
        draw.AddCircleFilled(center, radius, ImGui.GetColorU32(thumbColor), 48);
        // Slider interaction uses its thumb and fill; no outer focus outline.
        return changed;
    }
    public bool Dropdown(string id, string label, ref int selected, IReadOnlyList<UiChoice> choices, bool enabled = true)
    {
        using var c = new ControlScope(id, enabled);
        LabelAbove(label);
        var changed = false;
        if (!BeginCombo(selected >= 0 && selected < choices.Count ? choices[selected].Label : "未選択")) return false;
        try
        {
            for (var i = 0; i < choices.Count; i++)
            {
                if (Selectable(choices[i].Id, choices[i].Label, selected == i)) { selected = i; changed = true; }
                if (selected == i) ImGui.SetItemDefaultFocus();
            }
        }
        finally { ImGui.EndCombo(); }
        return changed;
    }
    public bool SearchDropdown(string id, string label, ref int selected, IReadOnlyList<UiChoice> choices, ref string search, bool enabled = true)
    {
        using var c = new ControlScope(id, enabled);
        LabelAbove(label);
        var changed = false;
        if (!BeginCombo(selected >= 0 && selected < choices.Count ? choices[selected].Label : "未選択")) return false;
        try
        {
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            InputText("search", "検索", ref search);
            var found = false;
            for (var i = 0; i < choices.Count; i++)
            {
                if (!Matches(choices[i].Label, search)) continue;
                found = true;
                if (Selectable(choices[i].Id, choices[i].Label, selected == i)) { selected = i; changed = true; }
            }
            if (!found) Empty("一致する項目がありません", "検索語を短くして試してください。");
        }
        finally { ImGui.EndCombo(); }
        return changed;
    }
    public static bool Matches(string label, string search) => label.Contains(search, StringComparison.OrdinalIgnoreCase);
    public bool Selectable(string id, string label, bool selected, bool enabled = true, float height = 0)
    {
        using var c = new ControlScope(id, enabled);
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X - Metrics.Px(4));
        if (height == 0 && ImGui.CalcTextSize(label).X > width)
        {
            var size = ImGui.CalcTextSize(label, false, width);
            var clicked = ImGui.Selectable("###control", selected, ImGuiSelectableFlags.None, new Vector2(0, size.Y));
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFontSize(), ImGui.GetItemRectMin() + new Vector2(Metrics.Px(2), 0),
                ImGui.GetColorU32(Theme.Text), label, width);
            return clicked;
        }
        return ImGui.Selectable(Label(label), selected, ImGuiSelectableFlags.None,
            new Vector2(0, height == 0 ? 0 : Metrics.Px(height)));
    }
    /// <summary>Rounded sidebar row. Optional glyph font remains owned by the host.</summary>
    public bool NavigationItem(string id, string label, bool selected, string? glyph = null, ImFontPtr font = default)
    {
        using var c = new ControlScope(id, true);
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var padding = Metrics.FramePadding;
        var iconWidth = string.IsNullOrEmpty(glyph) ? 0 : ImGui.GetFontSize() + Metrics.Px(10);
        var labelWidth = Math.Max(1, width - padding.X * 2 - iconWidth);
        var textSize = ImGui.CalcTextSize(label, false, labelWidth);
        ImGui.PushStyleColor(ImGuiCol.NavHighlight, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Button, selected ? Theme.Selected : Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected ? Theme.Selected : Theme.Hover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Pressed);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        bool clicked;
        try { clicked = ImGui.Button("###control", new Vector2(width, Math.Max(ImGui.GetFontSize(), textSize.Y) + padding.Y * 2)); }
        finally { ImGui.PopStyleVar(); ImGui.PopStyleColor(4); }
        var a = ImGui.GetItemRectMin(); var b = ImGui.GetItemRectMax();
        var draw = ImGui.GetWindowDrawList();
        if (!string.IsNullOrEmpty(glyph) && !font.IsNull)
        {
            var size = ImGui.GetFontSize();
            Vector2 glyphSize;
            ImGui.PushFont(font);
            try { glyphSize = ImGui.CalcTextSize(glyph) * (size / ImGui.GetFontSize()); }
            finally { ImGui.PopFont(); }
            var iconPosition = new Vector2(a.X + padding.X + (size - glyphSize.X) / 2, a.Y + (b.Y - a.Y - glyphSize.Y) / 2);
            draw.AddText(font, size, iconPosition, ImGui.GetColorU32(selected ? Theme.AccentInk ?? Theme.Accent : Theme.Muted), glyph);
        }
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), a + padding + new Vector2(iconWidth, 0), ImGui.GetColorU32(Theme.Text), label, labelWidth);
        FocusOutline(a, b, Metrics.FrameRounding, ImGui.IsItemFocused());
        return clicked;
    }
    /// <summary>Uniform single-line rows. Filter the collection outside Draw when a query changes.</summary>
    public bool List(string id, IReadOnlyList<UiChoice> items, ref int selected, float height = 240)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var changed = false;
        LastListVertices = LastListIndices = 0;
        var visible = ImGui.BeginChild(id, new Vector2(0, Metrics.Px(height)), true);
        try
        {
            if (!visible) return false;
            var listDraw = ImGui.GetWindowDrawList();
            var beforeVertices = listDraw.VtxBuffer.Size; var beforeIndices = listDraw.IdxBuffer.Size;
            if (items.Count == 0) { Empty("項目がありません", "追加または検索条件の変更で表示されます。"); return false; }
            if (!hasClipper) { clipper = ImGui.ImGuiListClipper(); hasClipper = true; }
            var rowHeight = ImGui.GetTextLineHeight() + Metrics.Px(8);
            clipper.Begin(items.Count, rowHeight + ImGui.GetStyle().ItemSpacing.Y);
            try
            {
                while (clipper.Step())
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        if (Selectable(items[i].Id, items[i].Label, i == selected, height: rowHeight / Scale))
                        { selected = i; changed = true; }
            }
            finally { clipper.End(); }
            LastListVertices = listDraw.VtxBuffer.Size - beforeVertices;
            LastListIndices = listDraw.IdxBuffer.Size - beforeIndices;
        }
        finally
        {
            ImGui.EndChild();
            var parentDraw = ImGui.GetWindowDrawList();
            var beforeVertices = parentDraw.VtxBuffer.Size; var beforeIndices = parentDraw.IdxBuffer.Size;
            ShadeItem(SurfaceKind.Inset, panel: true);
            LastListVertices += parentDraw.VtxBuffer.Size - beforeVertices;
            LastListIndices += parentDraw.IdxBuffer.Size - beforeIndices;
        }
        return changed;
    }
    public string? Tree(string id, IReadOnlyList<UiTreeNode> nodes, string? selected)
    {
        ImGui.PushID(id);
        try { foreach (var node in nodes) selected = DrawNode(node, selected); }
        finally { ImGui.PopID(); }
        return selected;
    }
    private string? DrawNode(UiTreeNode node, string? selected)
    {
        ImGui.PushID(node.Id);
        try
        {
            var leaf = node.Children is not { Count: > 0 };
            var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (leaf) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
            if (selected == node.Id) flags |= ImGuiTreeNodeFlags.Selected;
            var open = ImGui.TreeNodeEx(Label(node.Label), flags);
            if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) selected = node.Id;
            if (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter)) selected = node.Id;
            if (open && !leaf)
            {
                try { foreach (var child in node.Children!) selected = DrawNode(child, selected); }
                finally { ImGui.TreePop(); }
            }
        }
        finally { ImGui.PopID(); }
        return selected;
    }
    public void Panel(string id, Action draw, float height = 180, SurfaceKind kind = SurfaceKind.Raised)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, SurfaceColor(kind));
        try
        {
            var visible = ImGui.BeginChild(id, new Vector2(0, Metrics.Px(height)), true, ImGuiWindowFlags.AlwaysUseWindowPadding);
            try { if (visible) draw(); } finally { ImGui.EndChild(); }
            ShadeItem(kind, panel: true);
        }
        finally { ImGui.PopStyleColor(); }
    }
    /// <summary>Call before ImGui.Begin for a window that must retain its left sidebar.</summary>
    public void ConstrainNextTwoColumnWindow(float sidebarWidth = 175, float minimumDetailWidth = 280, float minimumHeight = 300)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sidebarWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumDetailWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumHeight);
        var style = ImGui.GetStyle();
        var contentWidth = Metrics.Px(Math.Max(520, sidebarWidth + minimumDetailWidth + Dimensions.Gap));
        // Include parent padding and a possible vertical scrollbar so content never reaches the stacking threshold.
        var minimumWidth = contentWidth + style.WindowPadding.X * 2 + style.ScrollbarSize + Metrics.Px(1);
        ImGui.SetNextWindowSizeConstraints(new Vector2(minimumWidth, Metrics.Px(minimumHeight)), new Vector2(float.MaxValue));
    }
    /// <summary>At narrow widths the sidebar stacks above the detail pane. Both panes scroll.</summary>
    public void TwoColumn(string id, Action sidebar, Action detail, float sidebarWidth = 175)
    {
        ImGui.PushID(id);
        try
        {
            var available = ImGui.GetContentRegionAvail();
            var stacked = available.X < Metrics.Px(520);
            var leftWidth = stacked ? 0 : Metrics.Px(sidebarWidth);
            var leftHeight = stacked ? Math.Min(Metrics.Px(140), available.Y * .35f) : 0;
            Child("sidebar", new Vector2(leftWidth, leftHeight), sidebar);
            var sidebarMin = ImGui.GetItemRectMin(); var sidebarMax = ImGui.GetItemRectMax();
            var parentDraw = ImGui.GetWindowDrawList();
            if (stacked) parentDraw.AddLine(new(sidebarMin.X, sidebarMax.Y), sidebarMax, ImGui.GetColorU32(Theme.Border));
            else parentDraw.AddLine(new(sidebarMax.X, sidebarMin.Y), sidebarMax, ImGui.GetColorU32(Theme.Border));
            if (!stacked) ImGui.SameLine();
            Child("detail", Vector2.Zero, detail);
        }
        finally { ImGui.PopID(); }
    }
    public void WithoutSidebar(string id, Action detail) => Child(id, Vector2.Zero, detail);
    private static void Child(string id, Vector2 size, Action draw)
    {
        var visible = ImGui.BeginChild(id, size, false, ImGuiWindowFlags.AlwaysUseWindowPadding);
        try { if (visible) draw(); } finally { ImGui.EndChild(); }
    }
    public void Form(string id, Action rows)
    {
        var stacked = ImGui.GetContentRegionAvail().X < Metrics.Px(330);
        if (!ImGui.BeginTable(id, stacked ? 1 : 2, ImGuiTableFlags.SizingStretchProp)) return;
        try
        {
            ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthStretch, 1);
            if (!stacked) ImGui.TableSetupColumn("input", ImGuiTableColumnFlags.WidthStretch, 2);
            rows();
        }
        finally { ImGui.EndTable(); }
    }
    public void Field(string label, Action input)
    {
        ImGui.TableNextRow(); ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding(); ImGui.TextWrapped(label); ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1); input();
    }
    public void Collapsing(string id, string label, Action draw)
    {
        ImGui.PushID(id);
        try { if (ImGui.CollapsingHeader(Label(label))) draw(); } finally { ImGui.PopID(); }
    }
    public void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) return;
        ImGui.BeginTooltip();
        try { ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24); try { ImGui.TextUnformatted(text); } finally { ImGui.PopTextWrapPos(); } }
        finally { ImGui.EndTooltip(); }
    }
    public void Notice(string text, NoticeKind kind = NoticeKind.Information)
    {
        var prefix = kind switch { NoticeKind.Error => "エラー", NoticeKind.Warning => "注意", NoticeKind.Success => "完了", _ => "情報" };
        ImGui.PushStyleColor(ImGuiCol.Text, Theme.NoticeColor(kind));
        try { ImGui.TextWrapped(prefix + "：" + text); } finally { ImGui.PopStyleColor(); }
    }
    public void InfoCard(string id, string message)
    {
        var height = Math.Max(62, (ImGui.CalcTextSize(message, false, Math.Max(1, ImGui.GetContentRegionAvail().X - Metrics.Px(40))).Y + Metrics.Padding.Y * 2) / Scale);
        Panel(id, () => Muted(message), height);
    }
    public void Empty(string title, string description) { Heading(title, false); Muted(description); }
    public void OpenConfirmation(string id) => ImGui.OpenPopup(id);
    /// <returns>null while closed/waiting; true on confirmation, false on cancel.</returns>
    public bool? Confirmation(string id, string text)
    {
        if (!ImGui.BeginPopupModal(id, ImGuiWindowFlags.AlwaysAutoResize)) return null;
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26);
            try { Text(text); } finally { ImGui.PopTextWrapPos(); }
            if (Button("confirm", "実行する", ButtonKind.Primary)) { ImGui.CloseCurrentPopup(); return true; }
            ImGui.SameLine();
            if (Button("cancel", "キャンセル")) { ImGui.CloseCurrentPopup(); return false; }
            return null;
        }
        finally { ImGui.EndPopup(); }
    }
    public void SurfaceSample(string id, string label, SurfaceKind kind)
    {
        using var c = new ControlScope(id, true);
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X), ImGui.GetFrameHeight() * 1.4f);
        ImGui.GetWindowDrawList().AddRectFilled(min, min + size, ImGui.GetColorU32(SurfaceColor(kind)), Metrics.FrameRounding);
        ImGui.Dummy(size); ShadeItem(kind);
        ImGui.GetWindowDrawList().AddText(min + Metrics.FramePadding, ImGui.GetColorU32(Theme.Text), label);
    }
    private void ShadeItem(SurfaceKind kind, bool panel = false)
    {
        var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
        var radius = panel ? Metrics.PanelRounding : Metrics.FrameRounding;
        FocusOutline(min, max, radius, ImGui.IsItemFocused());
    }
    private void FocusOutline(Vector2 min, Vector2 max, float radius, bool focused)
    {
        if (!focused) return;
        var inset = new Vector2(Metrics.Border / 2);
        ImGui.GetWindowDrawList().AddRect(min + inset, max - inset, ImGui.GetColorU32(Theme.Focus), radius, ImDrawFlags.None, Metrics.Border);
    }
    private void LabelAbove(string label)
    {
        var requestedWidth = ImGui.CalcItemWidth();
        if (!string.IsNullOrEmpty(label)) ImGui.TextWrapped(label);
        ImGui.SetNextItemWidth(Math.Clamp(requestedWidth, 1, Math.Max(1, ImGui.GetContentRegionAvail().X)));
    }
    private bool BeginCombo(string preview)
    {
        // Match the native arrow region to the preview surface, instead of a separate button face.
        ImGui.PushStyleColor(ImGuiCol.Button, Theme.Inset);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Theme.Inset);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Theme.Inset);
        try { return ImGui.BeginCombo("###control", preview); }
        finally { ImGui.PopStyleColor(3); }
    }
    private Vector4 SurfaceColor(SurfaceKind kind) => kind switch
    { SurfaceKind.Raised => Theme.Panel, SurfaceKind.Pressed => Theme.Pressed, _ => Theme.Inset };
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        HeadingFontScope = null;
        if (hasClipper) { clipper.Destroy(); hasClipper = false; }
    }
}
