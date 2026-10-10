using Dalamud.Game.Gui;
namespace TooltipExtras;

internal sealed class PatchAdapter
{
    private TooltipKey previous;
    private byte[]? content;
    public byte[]? Get(TooltipKey key)
    {
        if (previous == key) return content;
        previous = key;
        var patch = key.Kind == TooltipKind.Item ? PatchLookup.Item(key.Id)
            : PatchLookup.Action(key.Id, (DetailKind)key.DetailKind switch
            {
                DetailKind.Companion => PatchAction.Minion,
                DetailKind.Mount => PatchAction.Mount,
                DetailKind.Ornament => PatchAction.Ornament,
                DetailKind.Glasses => PatchAction.Glasses,
                _ => PatchAction.Other
            });
        return content = patch == null ? null : System.Text.Encoding.UTF8.GetBytes($"[Patch {patch}]");
    }
}
