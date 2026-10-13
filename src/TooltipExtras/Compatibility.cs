using System.Reflection;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed class Compatibility
{
    public bool NativeAllowed { get; private set; }
    public bool GlamourLogAvailable { get; private set; }
    public bool SimpleLayoutAllowed { get; private set; }
    public bool PriceAllowed { get; private set; }
    public bool PriceProviderDetected { get; private set; }
    public bool VendorProviderDetected { get; private set; }
    public string[] Rows { get; private set; } = [];
    public void Refresh(IDalamudPluginInterface pi)
    {
        NativeAllowed = typeof(AtkUnitBase).Assembly.ManifestModule.ModuleVersionId == new Guid("f2ce2d31-ef6f-45f8-a331-8b2785f00c81")
            && typeof(IDalamudPluginInterface).Assembly.ManifestModule.ModuleVersionId == new Guid("0982f2f8-b2dd-42d5-ad75-acd63588fc42");
        var plugins = pi.InstalledPlugins.ToArray();
        SimpleLayoutAllowed = plugins.Any(p => p.InternalName == "SimpleTweaksPlugin" && p.IsLoaded && p.Version == new Version(1, 15, 0, 7))
            && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "SimpleTweaksPlugin"
                && a.ManifestModule.ModuleVersionId == new Guid("f4d37b94-0e44-44cd-83fa-751e1fded533"));
        GlamourLogAvailable = plugins.Any(p => p.InternalName == "GlamourLog" && p.IsLoaded);
        // Price Insight uses assembly identity only. VendorAdapter separately accesses a pinned managed event.
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "PriceInsight"
            && a.ManifestModule.ModuleVersionId == new Guid("6ba90fe2-e666-4a94-b47d-abf7a855391e"));
        PriceProviderDetected = assembly != null && plugins.Any(p => p.InternalName == "PriceInsight" && p.IsLoaded && p.Version == new Version(2, 11, 5, 0));
        PriceAllowed = NativeAllowed && PriceProviderDetected;
        VendorProviderDetected = plugins.Any(p => p.InternalName == "ItemVendorLocation" && p.IsLoaded && p.Version == new Version(2, 14, 0, 1))
            && AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "ItemVendorLocation"
                && a.ManifestModule.ModuleVersionId == new Guid("ac0d5828-354a-4313-93fe-76680dcbb3fb"));
        var rows = new List<string> { "追加パッチ: 内蔵データ参照 / WhichPatchWasThatのロード不要" };
        foreach (var (id, reason) in new (string, string)[] {
            ("PriceInsight", PriceAllowed ? "対応DLL検出 / 専用ノード32612・形状確認後に分離" : "無効 / 未ロード・DLLまたはホスト識別不一致"),
            ("GlamourLog", "公開IPC / バージョン固定なし。IPC登録・呼び出し結果で判定"),
            ("SimpleTweaksPlugin", "未対応 / 文字列置換・専用ノード・アクション直接編集が混在"),
            ("WhichPatchWasThat", plugins.Any(p => p.InternalName == "WhichPatchWasThat" && p.IsLoaded && p.Version == new Version(7, 5, 0, 1)) ? "協調版検出 / ON時は内蔵表示。OFF時はIPC拒否・元表示へ戻す" : "内蔵パッチ表示を利用可能 / 通常版のネイティブ追記は元プラグイン無効化で停止"),
            ("Automaton", "未対応 / CBT: Collectable Reward Tooltip。元文字列の取得境界が必要"),
            ("ItemVendorLocation", "2.14.0.1専用 / 価格変更の前後取得・復元。実機未検証"),
            ("InventoryTools", "未対応 / Allagan Tools: 所持数・場所・用途の追記候補"),
            ("HaselTweaks", "対象機能は調査設定では無効 / InventoryHighlight等は分離対象外"),
            ("Marketbuddy", "未確認 / 価格操作補助。ネイティブ追記元とは未確定"),
            ("MarketBoardPlugin", "別ImGui / 二重描画しない"),
            ("MaskedDalamud", "配信除外は実録画で確認。ロード状態は成功判定に使わない") })
        {
            var p = plugins.Where(p => p.InternalName == id).OrderByDescending(p => p.IsLoaded).ThenByDescending(p => p.Version).FirstOrDefault();
            rows.Add($"{p?.Name ?? id} {p?.Version?.ToString() ?? "未導入"} [{(p?.IsLoaded == true ? "ロード中" : "未ロード")}]\n  {reason}");
        }
        Rows = rows.ToArray();
    }
}
