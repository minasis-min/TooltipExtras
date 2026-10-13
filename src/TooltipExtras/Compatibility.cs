using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
namespace TooltipExtras;
internal sealed class Compatibility
{
    private readonly CompatibilityEntry[] contracts = CompatibilityContract.Load();
    private readonly Dictionary<string, CompatibilityResult> states = new();
    private bool hostChecked, glamourLoaded;
    public bool NativeAllowed => State("Panel").Allowed;
    public bool PriceNativeAllowed => NativeAllowed && State("PriceNative").Allowed;
    public bool DiagnosticsAllowed => NativeAllowed && State("DiagnosticsNative").Allowed;
    public bool PriceAllowed => PriceNativeAllowed && State("PriceProvider").Allowed;
    public bool VendorAllowed => NativeAllowed && State("DiagnosticsNative").Allowed && State("VendorProvider").Allowed;
    public bool GlamourLogAvailable => State("GlamourSchema").Allowed && glamourLoaded;
    public bool SimpleLayoutAllowed => State("SimpleLayout").Allowed;
    public bool PriceProviderDetected { get; private set; }
    public bool VendorProviderDetected { get; private set; }
    public string[] Rows { get; private set; } = [];
    public CompatibilityResult State(string group) => states.GetValueOrDefault(group) ?? new(false, "未確認");
    private static Type? Resolve(string assembly, string type)
    {
        var matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == assembly).ToArray();
                if (matches.Length == 0 && assembly is "Dalamud" or "FFXIVClientStructs" or "Lumina.Excel")
        {
            try { _ = AssemblyLoadContext.GetLoadContext(typeof(Compatibility).Assembly)!.LoadFromAssemblyName(new AssemblyName(assembly)); }
            catch { return null; }
            matches = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == assembly).ToArray();
        }
        return matches.Length == 1 ? matches[0].GetType(type, false) : null;
    }
    public void Refresh(IDalamudPluginInterface pi)
    {
        if (!hostChecked)
        {
            foreach (var group in new[] { "Panel", "PriceNative", "DiagnosticsNative", "GlamourSchema" }) states[group] = CompatibilityContract.Check(contracts, group, Resolve);
            hostChecked = true;
        }
        var plugins = pi.InstalledPlugins.ToArray();
        bool Loaded(string id) => plugins.Any(p => p.InternalName == id && p.IsLoaded);
        PriceProviderDetected = Loaded("PriceInsight"); VendorProviderDetected = Loaded("ItemVendorLocation"); glamourLoaded = Loaded("GlamourLog");
        foreach (var pair in new[] { ("PriceProvider", "PriceInsight"), ("VendorProvider", "ItemVendorLocation"), ("SimpleLayout", "SimpleTweaksPlugin") }) states[pair.Item1] = Loaded(pair.Item2) ? CompatibilityContract.Check(contracts, pair.Item1, Resolve) : new(false, "未ロード");
        Rows = new[] { "共通パネルAPI・位置取得: " + State("Panel").Reason, "価格ネイティブ操作: " + State("PriceNative").Reason, "Price Insight: " + State("PriceProvider").Reason, "Item Vendor Location: " + State("VendorProvider").Reason, "Simple Tweaksの価格配置例外: " + State("SimpleLayout").Reason, "Glamour Logの参照シート: " + State("GlamourSchema").Reason, "販売情報・診断用ネイティブ配列: " + State("DiagnosticsNative").Reason, "追加パッチ: 内蔵固定データ / 共通パネル基盤が互換なら表示", "未対応: Simple Tweaks / Allagan Toolsの追加情報分離" };
    }
    public string PriceReason => !NativeAllowed ? State("Panel").Reason : !PriceNativeAllowed ? State("PriceNative").Reason : State("PriceProvider").Reason;
    public string VendorReason => !NativeAllowed ? State("Panel").Reason : !State("DiagnosticsNative").Allowed ? State("DiagnosticsNative").Reason : State("VendorProvider").Reason;
}
