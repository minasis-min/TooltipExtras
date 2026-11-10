using FFXIVClientStructs.FFXIV.Component.GUI;

namespace TooltipExtras;

// Local to one ItemDetail event. Adapters see the projected geometry; only this
// transaction writes the final frame and its provider node changes.
internal unsafe ref struct ItemLayoutUpdate
{
    private AtkResNode* footer, surface, priceToHide, priceToMove, firstImage, secondImage;
    private PreviewGeometry target;
    private float priceY;
    private bool resize;

    public readonly bool Hides(AtkResNode* node) => priceToHide != null && node == priceToHide;

    public readonly PreviewGeometry Read(AtkUnitBase* addon, AtkResNode* liveSurface, AtkResNode* liveFooter)
    {
        if (!resize)
            return new(addon->WindowNode->AtkResNode.Height, liveSurface->Height, addon->RootNode->Height, liveFooter->Y);
        RequireFooter(liveFooter);
        return surface==liveSurface ? target : target with { Surface=liveSurface->Height };
    }

    public readonly bool HasResize => resize;
    public readonly bool HasPreviewResize => firstImage != null;
    public readonly LayoutStamp Stamp => new(target.Window, target.Footer);

    public void PlanPrice(AtkUnitBase* addon, AtkResNode* node, AtkResNode* liveFooter, PriceCapturePlan plan)
    {
        priceToHide = node;
        if (plan.PresentationOnly) return;
        Resize(liveFooter, SynchronizedSurface(addon), new(plan.Height, plan.Height, plan.Height, plan.Footer));
    }

    public void PlanPreview(AtkResNode* image, AtkResNode* background, AtkResNode* liveFooter,
        PreviewGeometry geometry, AtkResNode* price, float newPriceY)
    {
        if (price != null && priceToMove != null && price != priceToMove)
            throw new InvalidOperationException("価格ノードが計画中に変更されました");
        Resize(liveFooter, background, geometry);
        if (firstImage == null) firstImage = image;
        else secondImage = image;
        if (price != null) { priceToMove = price; priceY = newPriceY; }
    }

    private void Resize(AtkResNode* liveFooter, AtkResNode* background, PreviewGeometry geometry)
    {
        RequireFooter(liveFooter);
        footer = liveFooter;
        surface = background;
        target = geometry;
        resize = true;
    }

    private readonly void RequireFooter(AtkResNode* node)
    {
        if (footer != null && footer != node)
            throw new InvalidOperationException("フッターが計画中に変更されました");
    }

    public readonly void Commit(AtkUnitBase* addon)
    {
        if (!resize)
        {
            if (priceToHide != null) priceToHide->ToggleVisibility(false);
            return;
        }

        var window = addon->WindowNode;
        var root = window->Component->UldManager.RootNode;
        var border = root->PrevSiblingNode;
        var old = new PreviewGeometry(window->AtkResNode.Height, surface == null ? (ushort)0 : surface->Height,
            addon->RootNode->Height, footer->Y);
        var oldRoot = root->Height;
        var oldBorder = border->Height;
        var oldPriceY = priceToMove == null ? 0 : priceToMove->Y;
        var priceWasVisible = priceToHide != null && priceToHide->IsVisible();
        try
        {
            Apply(addon, surface, footer, target);
            if (priceToMove != null && !Hides(priceToMove)) priceToMove->SetYFloat(priceY);
            if (priceWasVisible) priceToHide->ToggleVisibility(false);
        }
        catch
        {
            try
            {
                Apply(addon, surface, footer, old, oldRoot, oldBorder);
                if (priceToMove != null) priceToMove->SetYFloat(oldPriceY);
                if (priceWasVisible) priceToHide->ToggleVisibility(true);
            }
            catch { /* The caller discards all projected state on failure. */ }
            throw;
        }
    }

    public readonly void ReleaseImages()
    {
        if (firstImage != null) firstImage->ToggleVisibility(true);
        if (secondImage != null) secondImage->ToggleVisibility(true);
    }

    public static AtkResNode* SynchronizedSurface(AtkUnitBase* addon)
    {
        var manager = &addon->WindowNode->Component->UldManager;
        var background = NativeNodeLookup.FindUnique(manager->NodeList, manager->NodeListCount, 2, out _);
        return background != null && background->Height == addon->WindowNode->AtkResNode.Height ? background : null;
    }

    public static void Apply(AtkUnitBase* addon, AtkResNode* surface, AtkResNode* footer, PreviewGeometry geometry,
        ushort? componentRootHeight = null, ushort? borderHeight = null)
    {
        var window = addon->WindowNode;
        var root = window->Component->UldManager.RootNode;
        window->SetHeight(geometry.Window);
        var border = root->PrevSiblingNode;
        root->SetHeight(componentRootHeight ?? geometry.Window);
        border->SetHeight(borderHeight ?? geometry.Window);
        if (surface != null && surface != root && surface != border)
            surface->SetHeight(geometry.Surface);
        addon->RootNode->SetHeight(geometry.Root);
        footer->SetYFloat(geometry.Footer);
    }
}
