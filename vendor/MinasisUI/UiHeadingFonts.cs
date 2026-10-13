using Dalamud.Interface.ManagedFontAtlas;

namespace MinasisUI;

public static class UiTypography
{
    public const float LargeHeadingSize = 24;
    public const float SmallHeadingSize = 20;
}

/// <summary>Owns only the font handles it creates in the host atlas. Dispose after drawing has stopped.</summary>
public sealed class UiHeadingFonts : IDisposable
{
    private readonly IFontHandle large, small;
    private readonly Func<bool, IDisposable> push;
    private bool disposed;
    public UiHeadingFonts(IFontAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(atlas);
        large = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiTypography.LargeHeadingSize)));
        try { small = atlas.NewDelegateFontHandle(e => e.OnPreBuild(tk => tk.AddDalamudDefaultFont(UiTypography.SmallHeadingSize))); }
        catch { large.Dispose(); throw; }
        push = Push;
    }
    public void Apply(Ui ui)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(ui);
        ui.HeadingFontScope = push;
    }
    private IDisposable Push(bool isLarge)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return (isLarge ? large : small).Push();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        large.Dispose(); small.Dispose();
    }
}
