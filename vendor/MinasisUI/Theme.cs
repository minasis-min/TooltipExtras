using System.Globalization;
using System.Numerics;

namespace MinasisUI;

public enum ThemeKind { SoftCharcoal, Cream }
public enum SurfaceKind { Raised, Inset, Pressed }
public enum NoticeKind { Information, Success, Warning, Error }
public enum ButtonKind { Secondary, Primary }

/// <summary>Immutable palette. No global registry, texture, or font ownership.</summary>
public sealed record UiTheme(
    string Name, Vector4 Background, Vector4 Panel, Vector4 Inset, Vector4 Text,
    Vector4 Muted, Vector4 Accent, Vector4 AccentHover, Vector4 AccentPressed,
    Vector4 OnAccent, Vector4 Selected, Vector4 Border, Vector4 Hover, Vector4 Pressed,
    Vector4 Focus, Vector4 Warning, Vector4 Error, Vector4 Success,
    Vector4 Highlight, Vector4 Shadow, float DisabledAlpha = .58f)
{
    /// <summary>Optional text/icon ink when the accent fill is too light for the theme background.</summary>
    public Vector4? AccentInk { get; init; }
    public Vector4 SwitchThumb { get; init; } = Hex("#D0D3D5");
    public static Vector4 Hex(string hex)
    {
        if (hex.Length != 7 || hex[0] != '#') throw new ArgumentException("#RRGGBB形式を指定してください。", nameof(hex));
        var n = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new((n >> 16 & 255) / 255f, (n >> 8 & 255) / 255f, (n & 255) / 255f, 1);
    }
    public static string ToHex(Vector4 c) => $"#{(int)MathF.Round(c.X * 255):X2}{(int)MathF.Round(c.Y * 255):X2}{(int)MathF.Round(c.Z * 255):X2}";
    private static Vector4 H(string h) => Hex(h);
    public static UiTheme SoftCharcoal { get; } = new(
        "Soft Charcoal", H("#242527"), H("#303235"), H("#1D1F21"), H("#D0D3D5"),
        H("#9DA4A8"), H("#39DDCD"), H("#57E5D7"), H("#2CC5B6"), H("#101F1E"),
        H("#334D4B"), H("#51575B"), H("#3C4144"), H("#252C2D"), H("#88CFC5"),
        H("#DBB96D"), H("#EF9393"), H("#85C3A3"), new(1, 1, 1, .10f), new(0, 0, 0, .24f));
    public static UiTheme Cream { get; } = new(
        "Cream", H("#EDE5D5"), H("#F5EEDF"), H("#E3DAC8"), H("#302F29"),
        H("#716B60"), H("#39DDCD"), H("#57E5D7"), H("#2CC5B6"), H("#101F1E"),
        H("#CBDCD2"), H("#C8BDA8"), H("#EAE2D2"), H("#D8CEBB"), H("#127575"),
        H("#86530B"), H("#A83238"), H("#266445"), new(1, 1, 1, .65f), new(.2f, .17f, .12f, .15f)) { SwitchThumb = H("#F5EEDF"), AccentInk = H("#0C5555") };
    public static UiTheme For(ThemeKind kind) => kind == ThemeKind.Cream ? Cream : SoftCharcoal;
    public Vector4 NoticeColor(NoticeKind kind) => kind switch
    { NoticeKind.Warning => Warning, NoticeKind.Error => Error, NoticeKind.Success => Success, _ => AccentInk ?? Accent };
}

public sealed record UiDimensions(float FrameRounding = 6, float PanelRounding = 8,
    float Border = 1, float Gap = 10, float SectionGap = 14, float Padding = 16,
    float FramePaddingX = 12, float FramePaddingY = 8, float Shadow = 2)
{
    public UiMetrics At(float scale)
    {
        if (!float.IsFinite(scale) || scale <= 0 || scale > 8) throw new ArgumentOutOfRangeException(nameof(scale));
        return new(this, scale);
    }
}
public readonly record struct UiMetrics(UiDimensions Base, float Scale)
{
    public float Px(float logical) => logical * Scale;
    public Vector2 Padding => new(Px(Base.Padding));
    public Vector2 FramePadding => new(Px(Base.FramePaddingX), Px(Base.FramePaddingY));
    public Vector2 Spacing => new(Px(Base.Gap));
    public float FrameRounding => Px(Base.FrameRounding);
    public float PanelRounding => Px(Base.PanelRounding);
    public float Border => Px(Base.Border);
    public float Shadow => Px(Base.Shadow);
}
