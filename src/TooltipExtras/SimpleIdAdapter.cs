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
    private readonly bool action;
    private readonly Func<int> detailKind;
    private Func<(uint Id,int Kind)>? sourceAction;
    private readonly Func<uint>? baseActionId;
    private long nextAttempt;
    private Saved? saved;
    private sealed record Saved(ulong Item, int Kind, ulong SourceId, nint Addon, nint Node, byte[] Full, byte[] Category, byte[] Id);
    public string Status { get; private set; } = "待機中";
    public long Captures { get; private set; }
    public long Rejections { get; private set; }
    public SimpleIdAdapter(Func<bool> allowed, Func<(ulong, nint)> context, Func<bool> frameworkThread)
        :this(allowed,context,frameworkThread,TooltipKind.Item,()=>0){}
    public SimpleIdAdapter(Func<bool> allowed,Func<(ulong,nint)> context,Func<bool> frameworkThread,TooltipKind kind,Func<int> detailKind,Func<uint>? baseActionId=null)
    { this.allowed=allowed;this.context=context;this.frameworkThread=frameworkThread;action=kind==TooltipKind.Action;this.detailKind=detailKind;this.baseActionId=baseActionId; }
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
                if (addons[action?"ActionDetail":"ItemDetail"] is IEnumerable entries)
                    foreach (var entry in entries)
                    {
                        var tweak = entry.GetType().GetProperty("Tweak")!.GetValue(entry)!;
                        if (tweak.GetType().FullName == "SimpleTweaksPlugin.Tweaks.Tooltips.ShowItemID") found.Add(tweak);
                    }
            var target = found.Distinct(ReferenceEqualityComparer.Instance).SingleOrDefault();
            sourceEnabled = target != null && target.GetType().GetProperty("Enabled")!.GetValue(target) is true;
            if (!sourceEnabled) { Dispose(); Status = "元のShow IDが無効"; }
            else if(action)
            {
                var owner=a.GetType("SimpleTweaksPlugin.Tweaks.TooltipTweaks",true)!;
                var hovered=owner.GetField("HoveredAction",BindingFlags.Public|BindingFlags.Static)!;
                var id=hovered.FieldType.GetField("Id")!;var kind=hovered.FieldType.GetField("Category")!;
                if(id.FieldType!=typeof(uint)||!kind.FieldType.IsEnum)throw new NotSupportedException("元アクション識別の型が未対応です");
                sourceAction=()=>{var value=hovered.GetValue(null)??throw new InvalidOperationException("元アクション未取得");return ((uint)id.GetValue(value)!,Convert.ToInt32(kind.GetValue(value)));};
            }
        }
        catch (Exception ex) { Dispose(); Status = "ID分離停止: " + ex.GetBaseException().Message; }
    }
    private AtkTextNode* Node(AtkUnitBase* addon)
    {
        if (addon == null) return null;
        var node = NativeNodeLookup.FindUnique(addon->UldManager.NodeList, addon->UldManager.NodeListCount, action?6u:35u, out _);
        return node != null && node->Type == NodeType.Text ? (AtkTextNode*)node : null;
    }
    private static void Set(AtkTextNode* node, byte[] bytes)
    {
        fixed (byte* input = NativeTooltipText.Terminate(bytes))
        {
            try { NativeTooltipText.Set(node,input); }
            finally { NativeTooltipText.ReleaseInput(node,input); }
        }
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
        var key=context();var kind=detailKind();
        if(key.Id!=item||key.Addon!=(nint)addon){Clear();return;}
        var sourceId=MatchedSourceId(item,kind);
        if(sourceId==0)
        {
            Clear();
            Status="アクションIDの照合待ち";
            return;
        }
        var node = Node(addon);
        if (node == null || !node->AtkResNode.IsVisible()) { Clear(); return; }
        var bytes = node->NodeText.AsSpan();
        if (bytes.Length > 32768) { Clear(); return; }
        if (saved is { } previous && previous.Item == item && previous.Kind == kind && previous.SourceId == sourceId && previous.Addon == (nint)addon && previous.Node == (nint)node)
        {
            if (bytes.SequenceEqual(previous.Category)) return;
            if (bytes.SequenceEqual(previous.Full)) { Set(node, previous.Category); return; }
        }
        Clear();
        if(action&&!ActionIdText.IsCandidate(bytes)){Status="アクションIDの追記待ち";return;}
        byte[] category,id;
        if (!(action?ActionIdText.Split(bytes.ToArray(),out category,out id):CapturedText.SplitItemId(bytes.ToArray(),out category,out id))) { Rejections++; Status = "元表示を維持: Show IDのpayload境界が一致しません"; return; }
        var current = new Saved(item, kind, sourceId, (nint)addon, (nint)node, bytes.ToArray(), category, id);
        // Only text changes: retain the native category row's geometry/layout.
        saved = current;
        try
        {
            Set(node, category);
            if (!node->NodeText.AsSpan().SequenceEqual(category)) { Set(node, current.Full); Clear(); Status = "元表示を維持: ID本文の書換えを確認できません"; return; }
            Captures++; Status = action?"生成済みアクションIDを分離済み":"生成済みアイテムIDを分離済み";
        }
        catch { try { Set(node,current.Full); } finally { Clear(); } throw; }
    }
    public byte[]? Get(ulong item, nint addon)
    {
        if (!allowed() || !sourceEnabled || saved is not { } value || value.Item != item || value.Addon != addon) return null;
        if(value.Kind!=detailKind()||MatchedSourceId(item,value.Kind)!=value.SourceId){Clear();return null;}
        return value.Id;
    }
    private ulong MatchedSourceId(ulong id,int kind)
    {
        if (!action) return id;
        try
        {
            // Simple stores the argument to HandleActionHover. Dalamud's
            // ActionId is the resolved result; BaseActionId is that argument.
            var baseId = baseActionId?.Invoke() ?? 0;
            return sourceAction?.Invoke() is {} value && value.Kind==kind &&
                value.Id==(baseId!=0?baseId:id) ? value.Id : 0;
        }
        catch { return 0; }
    }
    public void Clear() => saved=null;
    public void Dispose()
    {
        sourceEnabled = false;
        if (saved is { } value)
        {
            var key = context(); var node = key.Id == value.Item && key.Addon == value.Addon && detailKind()==value.Kind && MatchedSourceId(key.Id,value.Kind)==value.SourceId ? Node((AtkUnitBase*)key.Addon) : null;
            if ((nint)node == value.Node && node != null && node->NodeText.AsSpan().SequenceEqual(value.Category)) Set(node,value.Full);
        }
        Clear();sourceAction=null;
    }
    public void Reconnect() { Dispose(); failed = false; nextAttempt = 0; }
}
