using TooltipExtras.PatchData;
namespace TooltipExtras;

// Independent lookup: historical patch data is attributed to WhichPatchWasThat.
internal static class PatchLookup
{
    public static string? Item(ulong rawId) => rawId == 0 ? null
        : ItemPatchMapper.GetPatch(rawId < 2_000_000 ? rawId % 500_000 : rawId);
    public static string? Action(ulong id, PatchAction kind)
    {
        var item = kind switch
        {
            PatchAction.Minion => ActionToItemMapper.GetItemOfMinion(id),
            PatchAction.Mount => ActionToItemMapper.GetItemOfMount(id),
            PatchAction.Ornament => ActionToItemMapper.GetItemOfFashionAccessory(id),
            PatchAction.Glasses => ActionToItemMapper.GetItemOfGlasses(id),
            _ => null
        };
        return item is { } value ? Item(value) : null;
    }
}
internal enum PatchAction { Other, Minion, Mount, Ornament, Glasses }
