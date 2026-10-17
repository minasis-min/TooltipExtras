using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TooltipExtras;

public sealed unsafe partial class Plugin
{
    private void PreDrawSimpleId(AtkUnitBase* addon, ulong item)
    {
        try { simpleId.PreDraw(addon,item); }
        catch (Exception ex) { log.Debug("Tooltip Extras ID分離の処理失敗: {Reason}", ex.GetBaseException().Message); }
    }
    private void OnAddon(AddonEvent type, AddonArgs args)
    {
        if (disposed || args.AddonName is not ("ItemDetail" or "ActionDetail") || !compatibility.NativeAllowed || faulted) return;
        var kind = args.AddonName == "ItemDetail" ? TooltipKind.Item : TooltipKind.Action;
        var addon = (AtkUnitBase*)args.Addon.Address;
        var key = Key(kind);
        try
        {
            if(settings.Diagnostics&&compatibility.DiagnosticsAllowed)
            {
                diagnostics.Record(type,args,frame,key.Id);
                if(kind==TooltipKind.Item&&compatibility.PriceNativeAllowed)diagnostics.RecordLayout(type.ToString(),frame,key.Id,addon,compatibility.State("LayoutTiming").Allowed);
            }
            if (type == AddonEvent.PreFinalize) { if (kind == TooltipKind.Item) { price.Clear(); glamour.Clear(); vendor.Clear(); allagan.Clear(); simple.Invalidate(); simpleId.Clear(); simpleText.Clear(); glamourNames.Clear(); unlockPreview.Clear(); paintingPreview.Clear(); } bridge.Clear(kind); return; }
            if (type == AddonEvent.PreHide) { if (kind == TooltipKind.Item) { Restore(); glamour.Clear(); vendor.Clear(); allagan.Clear(); simple.Clear(); simpleId.Clear(); simpleText.Clear(); glamourNames.Clear(); unlockPreview.Clear(); paintingPreview.Clear(); } bridge.Clear(kind); return; }
            if (type == AddonEvent.PreRequestedUpdate)
            {
                if (kind == TooltipKind.Item)
                {
                    if(Available&&settings.Items)Restore(false);else Restore();
                    glamourNames.Requested(key.Id,(nint)addon);
                    var strings = compatibility.AllaganNativeAllowed && args is AddonRequestedUpdateArgs requested ? (StringArrayData**)requested.StringArrayData : null;
                    allagan.Begin(key.Id, (nint)addon, strings == null ? 0 : (nint)strings[(int)StringArrayType.ItemDetail]);
                }
                if (Available && (kind == TooltipKind.Item ? settings.Items : settings.Actions) && key.Id != 0)
                    bridge.Requested(key, (nint)addon);
                else bridge.Clear(kind);
                return;
            }
            if (type == AddonEvent.PostRequestedUpdate)
            {
                bridge.EndRequested(kind);
                if(kind==TooltipKind.Item)
                {
                    allagan.End();
                    // Finalized source output can often be removed before later
                    // placement/draw work sees its temporary expanded size.
                    if(Available&&settings.Items&&addon!=null&&addon->IsVisible&&key.Id!=0)PrepareItemLayout(addon,key);
                }
                return;
            }
            if(type==AddonEvent.PreUpdate)
            {
                // Native Update resolves ScreenY for the footer and its children.
                // PostUpdate/PreDraw can change local Y but leave that resolved
                // position stale until the next update. Trim before resolution.
                if(kind==TooltipKind.Item&&compatibility.State("LayoutTiming").Allowed&&Available&&settings.Items&&addon!=null&&addon->IsVisible&&key.Id!=0)
                    PrepareItemLayout(addon,key);
                return;
            }
            if (type != AddonEvent.PreDraw) return;
            if (!Available || !(kind == TooltipKind.Item ? settings.Items : settings.Actions) || addon == null || !addon->IsVisible || key.Id == 0)
            { if (kind == TooltipKind.Item) { Restore(); glamour.Clear(); vendor.Clear(); allagan.Clear(); simple.Clear(); simpleId.Clear(); simpleText.Clear(); glamourNames.Clear(); unlockPreview.Clear(); paintingPreview.Clear(); } bridge.Clear(kind); return; }
            bridge.PreDraw(key, (nint)addon, frame);
            if (kind == TooltipKind.Item && compatibility.SimpleIdAllowed && settings.SimpleExtras)
                PreDrawSimpleId(addon, key.Id);
            if(kind==TooltipKind.Item&&compatibility.SimpleGlamourNameAllowed)TrySimple(glamourNamesPreDraw);
            if(kind==TooltipKind.Item)
            {
                PrepareItemLayout(addon,key);
                if(settings.Diagnostics&&compatibility.DiagnosticsAllowed&&compatibility.PriceNativeAllowed)
                    diagnostics.RecordLayout("Ready",frame,key.Id,addon,compatibility.State("LayoutTiming").Allowed);
                if(compatibility.PreviewDrawAllowed)
                {
                    var now=Environment.TickCount64;
                    var waitUnlock=unlockPreview.SuppressDraw(key.Id,(nint)addon,now);
                    var waitPainting=paintingPreview.SuppressDraw(key.Id,(nint)addon,now);
                    if(waitUnlock||waitPainting)args.PreventOriginal();
                }
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    private void PrepareItemLayout(AtkUnitBase* addon,TooltipKey key)
    {
        if(!compatibility.PriceNativeAllowed||addon==null)return;
        if(settings.PriceInsight&&compatibility.PriceAllowed&&priceFailure.Length==0)
        {
            price.SetKnownPreviewNodes(unlockPreview.KnownNodeId,paintingPreview.KnownNodeId);
            try {price.PreDraw(addon,key,frame,settings.Diagnostics&&compatibility.DiagnosticsAllowed,compatibility.SimpleLayoutAllowed);}
            catch(Exception ex){StopPrice(ex);}
        }
        var unlock=settings.SimpleExtras&&compatibility.UnlockPreviewAllowed&&settings.SimpleUnlockPreview;
        var painting=settings.SimpleExtras&&compatibility.PaintingPreviewAllowed&&settings.SimplePaintingPreview;
        if(!unlock&&!painting)return;
        var footer=addon->GetNodeById(2);
        if(footer==null||addon->WindowNode==null)return;
        var previous=new LayoutStamp(addon->WindowNode->AtkResNode.Height,footer->Y);
        var reflows=unlockPreview.Reflows+paintingPreview.Reflows;
        if(unlock)TrySimple(unlockPreviewPreDraw);
        if(painting)TrySimple(paintingPreviewPreDraw);
        price.FollowLayout(previous,new(addon->WindowNode->AtkResNode.Height,footer->Y));
        if(reflows!=unlockPreview.Reflows+paintingPreview.Reflows)price.FollowVerifiedPreviewReflow(addon,key,frame);
    }
}
