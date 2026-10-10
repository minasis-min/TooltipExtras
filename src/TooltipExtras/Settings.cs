namespace TooltipExtras;

public sealed class Settings
{
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public bool Items { get; set; } = true;
    public bool Actions { get; set; } = true;
    // Supported provider toggles default to enabled, including newly added settings.
    // Deserialization preserves explicitly saved true/false values.
    public bool PriceInsight { get; set; } = true;
    public PanelSide Side { get; set; } = PanelSide.Below;
    public float FontSize { get; set; } = 16;
    public float Gap { get; set; } = 4;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool VendorLocation { get; set; } = true;
    public bool GlamourLog { get; set; } = true;
    public bool PatchInfo { get; set; } = true;
    public bool UseMinasisUI { get; set; } = true;
    public bool ShowSourceNames { get; set; } = true;
    public bool Diagnostics { get; set; }
    public void Normalize()
    {
        FontSize = Clamp(FontSize, 10, 40, 16); Gap = Clamp(Gap, 0, 80, 4);
        OffsetX = Clamp(OffsetX, -1000, 1000, 0); OffsetY = Clamp(OffsetY, -80, 1000, 0);
        if (!Enum.IsDefined(Side)) Side = PanelSide.Below;
    }
    private static float Clamp(float value, float min, float max, float fallback) => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
