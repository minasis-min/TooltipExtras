using System.Collections;
using System.Reflection;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class SimpleIdAdapter : IDisposable
{
    private readonly Func<bool> allowed;
    private readonly Func<(ulong Id, nint Addon)> context;
    private readonly Func<bool> frameworkThread;
    private bool sourceEnabled, failed;
    private long nextAttempt;
    private Saved? saved;
    private sealed record Saved(ulong Item, nint Addon, nint Node, byte[] Full, byte[] Category, byte[] Id);
    public string Status { get; private set; } = "待機中";
    public long Captures { get; private set; }
    public long Rejections { get; private set; }
    public SimpleIdAdapter(Func<bool> allowed, Func<(ulong, nint)> context, Func<bool> frameworkThread)
    { this.allowed = allowed; this.context = context; this.frameworkThread = frameworkThread; }
    public void Refresh(IDalamudPluginInterface pi, bool enabled)
    {
        if (!enabled) { Dispose(); failed = false; Status = "無効"; return; }
        if (Environment.TickCount64 < nextAttempt) return;
        nextAttempt = Environment.TickCount64 + 1000;
        try
        {
            if (!pi.InstalledPlugins.Any(p => p.InternalName == "SimpleTweaksPlugin" && p.IsLoaded)) { Dispose(); Status = "未ロード"; return; }
            var a = ForeignPlugin.SimpleAssembly(pi);
            var events = a.GetType("SimpleTweaksPlugin.Events.EventController", true)!;
            var flags = BindingFlags.Static | BindingFlags.NonPublic;
            var groups = (IDictionary)events.GetProperty("AddonEventSubscribers", flags)!.GetValue(null)!;
            var found = new List<object>();
            foreach (IDictionary addons in groups.Values)
                if (addons["ItemDetail"] is IEnumerable entries)
                    foreach (var entry in entries)
                    {
                        var tweak = entry.GetType().GetProperty("Tweak")!.GetValue(entry)!;
                        if (tweak.GetType().FullName == "SimpleTweaksPlugin.Tweaks.Tooltips.ShowItemID") found.Add(tweak);
                    }
            var target = found.Distinct(ReferenceEqualityComparer.Instance).SingleOrDefault();
            sourceEnabled = target != null && target.GetType().GetProperty("Enabled")!.GetValue(target) is true;
            if (!sourceEnabled) { Dispose(); Status = "元のShow IDが無効"; }
        }
        catch (Exception ex) { Dispose(); Status = "ID分離停止: " + ex.GetBaseException().Message; }
    }
    private static AtkTextNode* Node(AtkUnitBase* addon)
    {
        if (addon == null) return null;
        var node = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, 35, out _);
        return node != null && node->Type == NodeType.Text ? (AtkTextNode*)node : null;
    }
    private static void Set(AtkTextNode* node, byte[] bytes)
    {
        var terminated = new byte[bytes.Length + 1]; bytes.CopyTo(terminated, 0);
        node->SetText(terminated);
    }
    public void PreDraw(AtkUnitBase* addon, ulong item)
    {
        if (failed) return;
        try { Capture(addon,item); }
        catch (Exception ex) { failed = true; Clear(); Status = "ID分離停止: " + ex.GetBaseException().Message; }
    }
    private void Capture(AtkUnitBase* addon, ulong item)
    {
        if (!allowed() || !sourceEnabled || !frameworkThread() || addon == null || !addon->IsVisible || item == 0) return;
        var node = Node(addon);
        if (node == null || !node->AtkResNode.IsVisible()) { Clear(); return; }
        var bytes = node->NodeText.AsSpan();
        if (bytes.Length is 0 or > 32768) { Clear(); return; }
        if (saved is { } previous && previous.Item == item && previous.Addon == (nint)addon && previous.Node == (nint)node)
        {
            if (bytes.SequenceEqual(previous.Category)) return;
            if (bytes.SequenceEqual(previous.Full)) { Set(node, previous.Category); return; }
        }
        Clear();
        if (!CapturedText.SplitItemId(bytes.ToArray(), out var category, out var id)) { Rejections++; Status = "元表示を維持: Show IDのpayload境界が一致しません"; return; }
        var current = new Saved(item, (nint)addon, (nint)node, bytes.ToArray(), category, id);
        // Only text changes: retain the native category row's geometry/layout.
        saved = current;
        try
        {
            Set(node, category);
            if (!node->NodeText.AsSpan().SequenceEqual(category)) { Set(node, current.Full); Clear(); Status = "元表示を維持: ID本文の書換えを確認できません"; return; }
            Captures++; Status = "生成済みアイテムIDを分離済み";
        }
        catch { try { Set(node,current.Full); } finally { Clear(); } throw; }
    }
    public byte[]? Get(ulong item, nint addon)
    {
        if (!allowed() || !sourceEnabled || saved is not { } value || value.Item != item || value.Addon != addon) return null;
        return value.Id;
    }
    public void Clear() => saved = null;
    public void Dispose()
    {
        sourceEnabled = false;
        if (saved is { } value)
        {
            var key = context(); var node = key.Id == value.Item && key.Addon == value.Addon ? Node((AtkUnitBase*)key.Addon) : null;
            if ((nint)node == value.Node && node != null && node->NodeText.AsSpan().SequenceEqual(value.Category)) Set(node,value.Full);
        }
        Clear();
    }
    public void Reconnect() { Dispose(); failed = false; nextAttempt = 0; }
}
