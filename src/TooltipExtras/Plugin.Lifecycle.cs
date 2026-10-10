using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TooltipExtras;

public sealed unsafe partial class Plugin
{
    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (disposed || args.AddonName is not ("ItemDetail" or "ActionDetail") || !compatibility.NativeAllowed || faulted) return;
        var kind = args.AddonName == "ItemDetail" ? TooltipKind.Item : TooltipKind.Action;
        var addon = (AtkUnitBase*)args.Addon.Address;
        var key = Key(kind);
        try
        {
            if (settings.Diagnostics) diagnostics.Record(type, args, frame, key.Id);
            if (type == AddonEvent.PreFinalize) { if (kind == TooltipKind.Item) { price.Clear(); glamour.Clear(); vendor.Clear(); } bridge.Clear(kind); return; }
            if (type == AddonEvent.PreHide) { if (kind == TooltipKind.Item) { price.Restore(addon, key); glamour.Clear(); vendor.Clear(); } bridge.Clear(kind); return; }
            if (type == AddonEvent.PreRequestedUpdate)
            {
                if (kind == TooltipKind.Item) price.Restore(addon, key);
                if (Available && (kind == TooltipKind.Item ? settings.Items : settings.Actions) && key.Id != 0)
                    bridge.Requested(key, (nint)addon);
                else bridge.Clear(kind);
                return;
            }
            if (type == AddonEvent.PostRequestedUpdate) { bridge.EndRequested(kind); return; }
            if (type != AddonEvent.PreDraw) return;
            if (!Available || !(kind == TooltipKind.Item ? settings.Items : settings.Actions) || addon == null || !addon->IsVisible || key.Id == 0)
            { if (kind == TooltipKind.Item) { price.Restore(addon, key); glamour.Clear(); vendor.Clear(); } bridge.Clear(kind); return; }
            bridge.PreDraw(key, (nint)addon, frame);
            if (kind == TooltipKind.Item && settings.PriceInsight && compatibility.PriceAllowed) price.PreDraw(addon, key, frame, settings.Diagnostics, compatibility.SimpleLayoutAllowed);
        }
        catch (Exception ex) { Fail(ex); }
    }
}
