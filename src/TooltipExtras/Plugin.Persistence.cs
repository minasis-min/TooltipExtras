using System.Text.Json;

namespace TooltipExtras;

public sealed unsafe partial class Plugin
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string PluginVersion = typeof(Plugin).Assembly.GetName().Version!.ToString(3);
    private string SerializeSettings() => JsonSerializer.Serialize(settings, JsonOptions);
    private void SaveDiagnostics()
    {
        var json = JsonSerializer.Serialize(new
        {
            Version = PluginVersion,
            HostMatched = compatibility.NativeAllowed,
            Compatibility = compatibility.Rows,
            PriceFailure = priceFailure,
            VendorFailure = vendorFailure,
            compatibility.PriceProviderDetected,
            compatibility.VendorProviderDetected,
            PriceDisplay = PriceDisplayStatus(),
            VendorDisplay = VendorDisplayStatus(),
            compatibility.Rows,
            price.Status,
            price.Captures,
            price.Rejections,
            PriceStructure = price.Structure,
            PriceRejectionHistory = price.RejectionSnapshot(),
            PriceRestorePending = price.PendingRestore,
            SimpleRestorePending = simple.PendingRestore,
            ReconnectPending = reconnect.Pending,
            PriceUpdateFlow = new {price.RequestsPreserved,price.Restores},
            Bridge = bridge.Snapshot(),
            GlamourLog = glamour.Status,
            GlamourQueries = glamour.Snapshot(),
            GlamourPerformance = glamour.Performance(),
            DiagnosticPerformance = new { diagnostics.LastRecordMs, diagnostics.MaxRecordMs },
            VendorLocation = new { vendor.Status, vendor.Captures, vendor.Restores },
            SimpleGlamourName = new {glamourNames.Status,glamourNames.Captures,glamourNames.Rejections,glamourNames.RequestsKept,glamourNames.Restores,glamourNames.Draws,glamourNames.LastStage,glamourNames.Colors},
            SimpleText = new {simpleText.Status,simpleText.Captures,simpleText.Rejections,simpleText.HookCalls,simpleText.ProducerCalls,Features=simpleText.FeatureStates()}, UnlockPreview = new {unlockPreview.Status,unlockPreview.Captures,unlockPreview.GeometryProbe,unlockPreview.BaseGeometry,unlockPreview.LayoutProbe,unlockPreview.Reflows,unlockPreview.RequestsPreserved,unlockPreview.Wait}, PaintingPreview = new {paintingPreview.Status,paintingPreview.Captures,paintingPreview.GeometryProbe,paintingPreview.BaseGeometry,paintingPreview.LayoutProbe,paintingPreview.Reflows,paintingPreview.RequestsPreserved,paintingPreview.Wait}, SimpleItemId = new { simpleId.Status, simpleId.Captures, simpleId.Rejections }, SimpleTweaks = new { simple.Status, simple.Captures, simple.Rejections }, AllaganTools = new { Failure = allaganFailure, allagan.Status, allagan.Connections, allagan.Captures, allagan.Rejections, allagan.Requests, allagan.Callbacks, allagan.OutsideRequests, allagan.Sections },
            Events = diagnostics.Snapshot(),
            LayoutTransitions = diagnostics.LayoutSnapshot(),
            Error = failure,
        }, JsonOptions);
        saveTask = Task.Run(() => store.Save(json, true));
    }
}
