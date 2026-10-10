using Lumina.Text;
namespace TooltipExtras;

internal static class PanelContent
{
    // A balanced color scope keeps the provider's original body colors intact.
    public static byte[] Section(string pluginName, byte[] body, bool showName = true)
    {
        if (body.Length == 0) return [];
        if (!showName) return body;
        var header = new SeStringBuilder();
        header.PushColorRgba(255, 253, 208, 255); // #FFFDD0
        header.Append("[" + pluginName + "]");
        header.PopColor();
        header.AppendNewLine();
        return [.. header.GetViewAsSpan(), .. body];
    }
    public static string SourceName(string source) => source == "WhichPatchWasThat.Patch"
        ? "Which Patch Was That?" : source;
}
