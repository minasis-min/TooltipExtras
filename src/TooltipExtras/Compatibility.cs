using System.Reflection;
using Dalamud.Plugin;
namespace TooltipExtras;
internal sealed class Compatibility
{
    private readonly CompatibilityEntry[] contracts = CompatibilityContract.Load();
    private readonly Dictionary<string, CompatibilityResult> states = new();
    private bool hostChecked, glamourLoaded;
    private Assembly? simpleAssembly;
    public bool NativeAllowed => State("Panel").Allowed;
    public bool PriceNativeAllowed => NativeAllowed && State("PriceNative").Allowed;
    public bool DiagnosticsAllowed => NativeAllowed && State("DiagnosticsNative").Allowed;
    public bool PriceAllowed => PriceNativeAllowed && State("BodyBounds").Allowed && State("PriceProvider").Allowed;
    public bool VendorAllowed => NativeAllowed && State("DiagnosticsNative").Allowed && State("VendorProvider").Allowed;
    public bool GlamourLogAvailable => State("GlamourSchema").Allowed && glamourLoaded;
    public bool SimpleTextAllowed=>PriceNativeAllowed&&State("AllaganNative").Allowed&&State("SimpleTextNative").Allowed&&State("SimpleTextProvider").Allowed;
    public bool SimpleTextFeatureAllowed(int index)=>State(index switch {0=>"SimpleFood",1=>"SimpleMateriaStats",2=>"SimpleDesynth",3=>"SimpleCollectables",4=>"SimpleLogos",5=>"SimpleFadedRolls",6=>"SimpleGacha",7=>"SimpleOutfits",8=>"SimpleGlamourName",_=>"invalid"}).Allowed;
    public bool PreviewDrawAllowed=>State("PreviewDraw").Allowed; public bool UnlockPreviewAllowed=>PriceNativeAllowed&&State("BodyBounds").Allowed&&State("SimpleEvents").Allowed&&State("PreviewNative").Allowed&&State("UnlockPreview").Allowed;
    public bool PaintingPreviewAllowed=>PriceNativeAllowed&&State("BodyBounds").Allowed&&State("SimpleEvents").Allowed&&State("PreviewNative").Allowed&&State("PaintingPreview").Allowed;
    public bool SimpleGlamourNameAllowed=>SimpleTextAllowed&&State("SimpleGlamourName").Allowed&&State("SimpleIdNative").Allowed&&State("NameColorNative").Allowed&&State("NameColorApi").Allowed;
    public bool SimpleIdAllowed => PriceNativeAllowed && State("SimpleIdNative").Allowed && State("SimpleIdProvider").Allowed; public bool AllaganNativeAllowed => NativeAllowed && State("AllaganNative").Allowed; public bool SimpleRelayAllowed => PriceNativeAllowed && SimpleLayoutAllowed && State("SimpleEvents").Allowed; public bool SimpleLayoutAllowed => State("SimpleLayout").Allowed;
    public bool PriceProviderDetected { get; private set; }
    public bool VendorProviderDetected { get; private set; }
    public string[] Rows { get; private set; } = [];
    public CompatibilityResult State(string group) => states.GetValueOrDefault(group) ?? new(false, "未確認");
    private Type? Resolve(string assembly, string type)
    {
        // Inspect the exact host types TE dereferences. Multiple plugin load contexts
        // can expose assemblies with the same simple name; a global name scan is not
        // a reliable way to identify the host ABI.
        var host = assembly switch
        {
            "Dalamud" => typeof(IDalamudPluginInterface).Assembly,
            "FFXIVClientStructs" => typeof(FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase).Assembly,
            "Lumina.Excel" => typeof(Lumina.Excel.Sheets.Item).Assembly,
            "Lumina" => typeof(Lumina.Text.SeStringBuilder).Assembly,
            "SimpleTweaksPlugin" => simpleAssembly,
            _ => null
        };
        return CompatibilityContract.ResolveType(assembly, type, host, AppDomain.CurrentDomain.GetAssemblies());
    }
    public void Refresh(IDalamudPluginInterface pi)
    {
        if (!hostChecked)
        {
            foreach (var group in new[] { "Panel", "PriceNative", "DiagnosticsNative", "GlamourSchema", "AllaganNative", "SimpleIdNative", "SimpleTextNative", "PreviewNative", "PreviewDraw", "BodyBounds", "NameColorNative", "NameColorApi", "LayoutTiming" }) states[group] = CompatibilityContract.Check(contracts, group, Resolve);
            hostChecked = true;
        }
        var plugins = pi.InstalledPlugins.ToArray();
        bool Loaded(string id) => plugins.Any(p => p.InternalName == id && p.IsLoaded);
        simpleAssembly=null;
        if(Loaded("SimpleTweaksPlugin"))try{simpleAssembly=ForeignPlugin.SimpleAssembly(pi);}catch{/* Feature contracts remain unavailable until the actual instance is available. */}
        PriceProviderDetected = Loaded("PriceInsight"); VendorProviderDetected = Loaded("ItemVendorLocation"); glamourLoaded = Loaded("GlamourLog");
        foreach (var pair in new[] { ("PriceProvider", "PriceInsight"), ("VendorProvider", "ItemVendorLocation"), ("SimpleLayout", "SimpleTweaksPlugin"), ("SimpleEvents", "SimpleTweaksPlugin"), ("SimpleIdProvider", "SimpleTweaksPlugin"),("SimpleTextProvider","SimpleTweaksPlugin"),("UnlockPreview","SimpleTweaksPlugin"),("PaintingPreview","SimpleTweaksPlugin"),("SimpleFood","SimpleTweaksPlugin"),("SimpleMateriaStats","SimpleTweaksPlugin"),("SimpleDesynth","SimpleTweaksPlugin"),("SimpleCollectables","SimpleTweaksPlugin"),("SimpleLogos","SimpleTweaksPlugin"),("SimpleFadedRolls","SimpleTweaksPlugin"),("SimpleGacha","SimpleTweaksPlugin"),("SimpleOutfits","SimpleTweaksPlugin"),("SimpleGlamourName","SimpleTweaksPlugin") }) states[pair.Item1] = Loaded(pair.Item2) ? CompatibilityContract.Check(contracts, pair.Item1, Resolve) : new(false, "未ロード");
        Rows = new[] { "共通パネルAPI・位置取得: " + State("Panel").Reason, "価格ネイティブ操作: " + State("PriceNative").Reason, "Price Insight: " + State("PriceProvider").Reason, "Item Vendor Location: " + State("VendorProvider").Reason, "Simple Tweaksの価格配置例外: " + State("SimpleLayout").Reason, "Glamour Logの参照シート: " + State("GlamourSchema").Reason, "販売情報・診断用ネイティブ配列: " + State("DiagnosticsNative").Reason, "追加パッチ: 内蔵固定データ / 共通パネル基盤が互換なら表示", "AT中継ネイティブ配列: " + State("AllaganNative").Reason, "Simple専用ノードのイベント: " + State("SimpleEvents").Reason, "SimpleアイテムID: " + State("SimpleIdProvider").Reason, "Simple文字中継: "+State("SimpleTextProvider").Reason,"Simple文字API・配列: "+State("SimpleTextNative").Reason,"画像API: "+State("PreviewNative").Reason,"画像配置待ちの描画抑止: "+State("PreviewDraw").Reason,"描画位置計算前の分離: "+State("LayoutTiming").Reason,"本文の範囲確認: "+State("BodyBounds").Reason,"アンロック画像: "+State("UnlockPreview").Reason,"絵画画像: "+State("PaintingPreview").Reason,"未対応: SimpleのアクションID分離" };
    }
    public string PriceReason => !NativeAllowed ? State("Panel").Reason : !PriceNativeAllowed ? State("PriceNative").Reason : !State("BodyBounds").Allowed ? State("BodyBounds").Reason : State("PriceProvider").Reason;
    public string VendorReason => !NativeAllowed ? State("Panel").Reason : !State("DiagnosticsNative").Allowed ? State("DiagnosticsNative").Reason : State("VendorProvider").Reason;
}
