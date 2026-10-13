using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TooltipExtras;

public sealed unsafe partial class Plugin
{
    private bool? lastShowSourceNames;
    private byte[]? lastPrice, lastBridge, lastGlamour, lastVendor, lastPatch, merged;

    private void Draw()
    {
        if (disposed) return;
        if (open) DrawSettings();
        if (!Available || !compatibility.NativeAllowed || faulted) { glamour.Clear(); vendor.Clear(); return; }
        if (!settings.Items || !settings.GlamourLog) glamour.Clear();
        try
        {
            foreach (var kind in Kinds)
            {
                if (!(kind == TooltipKind.Item ? settings.Items : settings.Actions)) continue;
                var addon = Addon(kind); var key = Key(kind);
                if (addon == null || !addon->IsVisible || key.Id == 0) { bridge.Clear(kind); if (kind == TooltipKind.Item) { glamour.Clear(); vendor.Clear(); } continue; }
                byte[]? priceBytes = null;
                var bounds = new Bounds(new Vector2(addon->X, addon->Y), new Vector2(addon->GetScaledWidth(true), addon->GetScaledHeight(true)));
                if (kind == TooltipKind.Item && settings.PriceInsight && compatibility.PriceAllowed && priceFailure.Length == 0 && TryPricePanel(addon, key, frame, out var p, out var b))
                { priceBytes = p; bounds = b; }
                var bridgeBytes = bridge.Get(key, (nint)addon, addon->IsVisible, settings.ShowSourceNames, settings.PatchInfo);
                byte[]? glamourBytes = null;
                if (kind == TooltipKind.Item)
                {
                    glamour.Update(key.Id, (nint)addon, settings.GlamourLog, compatibility.GlamourLogAvailable, Environment.TickCount64);
                    glamourBytes = glamour.Content;
                }
                var patchBytes = settings.PatchInfo ? patchInfo.Get(key) : null;
                var vendorBytes = kind == TooltipKind.Item ? vendor.Get(key.Id, (nint)addon) : null;
                if (priceBytes == null && bridgeBytes == null && glamourBytes == null && vendorBytes == null && patchBytes == null) continue;
                if (lastShowSourceNames != settings.ShowSourceNames || !ReferenceEquals(priceBytes, lastPrice) || !ReferenceEquals(bridgeBytes, lastBridge) || !ReferenceEquals(glamourBytes, lastGlamour) || !ReferenceEquals(vendorBytes, lastVendor) || !ReferenceEquals(patchBytes, lastPatch))
                {
                    lastShowSourceNames = settings.ShowSourceNames; lastPrice = priceBytes; lastBridge = bridgeBytes; lastGlamour = glamourBytes; lastVendor = vendorBytes; lastPatch = patchBytes;
                    merged = CooperativeBridge.Join(new[] {
                        SourceSection("Which Patch Was That?", patchBytes),
                        bridgeBytes,
                        SourceSection("Price Insight", priceBytes),
                        SourceSection("Glamour Log", glamourBytes),
                        SourceSection("Item Vendor Location", vendorBytes),
                    }.OfType<byte[]>());
                }
                if (merged is { Length: > 0 }) PanelRenderer.Draw(merged, bounds, settings);
                break; // Exactly one extras panel, even during overlapping native tooltip transitions.
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private bool TryPricePanel(AtkUnitBase* addon, TooltipKey key, long currentFrame, out byte[] content, out Bounds bounds)
    {
        try { return price.TryPanel(addon, key, currentFrame, out content, out bounds); }
        catch (Exception ex) { StopPrice(ex); content = []; bounds = default; return false; }
    }
    private byte[]? SourceSection(string source, byte[]? content) =>
        content == null ? null : PanelContent.Section(source, content, settings.ShowSourceNames);
}
