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
            compatibility.PriceProviderDetected,
            compatibility.VendorProviderDetected,
            PriceDisplay = PriceDisplayStatus(),
            VendorDisplay = VendorDisplayStatus(),
            compatibility.Rows,
            price.Status,
            price.Captures,
            price.Rejections,
            PriceStructure = price.Structure,
            Bridge = bridge.Snapshot(),
            GlamourLog = glamour.Status,
            GlamourQueries = glamour.Snapshot(),
            GlamourPerformance = glamour.Performance(),
            DiagnosticPerformance = new { diagnostics.LastRecordMs, diagnostics.MaxRecordMs },
            VendorLocation = new { vendor.Status, vendor.Captures, vendor.Restores },
            Events = diagnostics.Snapshot(),
            Error = failure,
        }, JsonOptions);
        saveTask = Task.Run(() => store.Save(json, true));
    }
}
