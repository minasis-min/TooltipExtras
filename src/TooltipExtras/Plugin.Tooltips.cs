using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TooltipExtras;

public sealed unsafe partial class Plugin
{
    private bool? lastShowSourceNames;
    private readonly List<PanelImage> panelImages=new(2);
    private byte[]? lastPrice, lastBridge, lastGlamour, lastVendor, lastPatch, lastAllagan, lastSimple, lastDaily, merged;

    private void Draw()
    {
        if (disposed) return;
        if (open) DrawSettings();
        if (!Available || !compatibility.NativeAllowed || faulted) { dailyActions.Clear(); glamour.Clear(); vendor.Clear(); daily.Clear(); allagan.Clear(); simple.Clear(); simpleId.Clear(); simpleText.Clear(); glamourNames.Clear(); unlockPreview.Clear(); paintingPreview.Clear(); return; }
        if (!settings.Items || !settings.GlamourLog) glamour.Clear();
        try
        {
            foreach (var kind in Kinds)
            {
                if (!(kind == TooltipKind.Item ? settings.Items : settings.Actions)) continue;
                var addon = Addon(kind); var key = Key(kind);
                if (addon == null || !addon->IsVisible || key.Id == 0) { if(kind==TooltipKind.Action)dailyActions.Clear(); bridge.Clear(kind); if (kind == TooltipKind.Item) { glamour.Clear(); vendor.Clear(); daily.Clear(); allagan.Clear(); simple.Clear(); simpleId.Clear(); simpleText.Clear(); glamourNames.Clear(); unlockPreview.Clear(); paintingPreview.Clear(); } continue; }
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
                var allaganBytes = kind == TooltipKind.Item ? allagan.Get(key.Id, (nint)addon) : null;
                var simpleBytes = kind == TooltipKind.Item ? SimpleContent(key.Id, (nint)addon) : null;
                var dailyBytes = kind == TooltipKind.Item ? daily.Get(key.Id,(nint)addon) : dailyActions.Get(key.Id,(nint)addon);
                var images=panelImages;images.Clear();
                if(kind==TooltipKind.Item)
                {
                    if(unlockPreview.Get(key.Id,(nint)addon) is {} unlock)images.Add(unlock);
                    if(paintingPreview.Get(key.Id,(nint)addon) is {} painting)images.Add(painting);
                }
                if(images.Count>0&&simpleBytes==null)simpleBytes=PreviewCaption;
                if (images.Count==0 && dailyBytes==null && simpleBytes == null && allaganBytes == null && priceBytes == null && bridgeBytes == null && glamourBytes == null && vendorBytes == null && patchBytes == null) continue;
                if (!ReferenceEquals(dailyBytes,lastDaily) || !ReferenceEquals(simpleBytes, lastSimple) || !ReferenceEquals(allaganBytes, lastAllagan) || lastShowSourceNames != settings.ShowSourceNames || !ReferenceEquals(priceBytes, lastPrice) || !ReferenceEquals(bridgeBytes, lastBridge) || !ReferenceEquals(glamourBytes, lastGlamour) || !ReferenceEquals(vendorBytes, lastVendor) || !ReferenceEquals(patchBytes, lastPatch))
                {
                    lastSimple = simpleBytes; lastAllagan = allaganBytes; lastShowSourceNames = settings.ShowSourceNames; lastPrice = priceBytes; lastBridge = bridgeBytes; lastGlamour = glamourBytes; lastVendor = vendorBytes; lastPatch = patchBytes;
                    lastDaily=dailyBytes;
                    merged = CooperativeBridge.Join(new[] {
                        SourceSection("Which Patch Was That?", patchBytes),
                        bridgeBytes,
                        SourceSection("Price Insight", priceBytes),
                        SourceSection("Glamour Log", glamourBytes),
                        SourceSection("Item Vendor Location", vendorBytes),
                        SourceSection("Allagan Tools", allaganBytes),
                        SourceSection("Simple Tweaks", simpleBytes),
                        SourceSection("Daily Routines",dailyBytes),
                    }.OfType<byte[]>());
                }
                if (merged is { Length: > 0 }) PanelRenderer.Draw(merged, bounds, settings,images);
                break; // Exactly one extras panel, even during overlapping native tooltip transitions.
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private static readonly byte[] PreviewCaption=System.Text.Encoding.UTF8.GetBytes("プレビュー");
    private byte[]? simpleNameCache,simpleIdCache, simpleExtraCache, simpleTextCache, simpleCombined;
    private byte[]? SimpleContent(ulong id, nint addon)
    {
        var names=glamourNames.Get(id,addon);
        var idBytes = simpleId.Get(id,addon); var extras = simple.Get(id,addon);var text=simpleText.Get(id,addon);
        if (!ReferenceEquals(names,simpleNameCache)||!ReferenceEquals(idBytes,simpleIdCache) || !ReferenceEquals(extras,simpleExtraCache)||!ReferenceEquals(text,simpleTextCache))
        {
            simpleNameCache=names;simpleIdCache = idBytes; simpleExtraCache = extras;simpleTextCache=text;
            var parts=new[]{names,idBytes,text,extras}.OfType<byte[]>().ToArray();simpleCombined=parts.Length==0?null:CapturedText.Rows(parts);
        }
        return simpleCombined;
    }
    private bool TryPricePanel(AtkUnitBase* addon, TooltipKey key, long currentFrame, out byte[] content, out Bounds bounds)
    {
        try { return price.TryPanel(addon, key, currentFrame, out content, out bounds); }
        catch (Exception ex) { StopPrice(ex); content = []; bounds = default; return false; }
    }
    private byte[]? SourceSection(string source, byte[]? content) =>
        content == null ? null : PanelContent.Section(source, content, settings.ShowSourceNames);
}
