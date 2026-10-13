using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
namespace TooltipExtras;

internal sealed class VendorAdapter : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Func<bool> allowed;
    private readonly Func<(ulong Id, nint Addon)> context;
    private object? owner, priceField;
    private FieldInfo? slot;
    private Delegate? original, wrapper;
    private PropertyInfo? indexer;
    private ulong item;
    private nint addon;
    private bool busy, failed;
    private long nextAttempt;
    private byte[]? content;
    public string Status { get; private set; } = "待機中";
    public long Captures { get; private set; }
    public long Restores { get; private set; }
    public VendorAdapter(Func<bool> allowed, Func<(ulong, nint)> context)
    { this.allowed = allowed; this.context = context; }
    public void Clear() { content = null; item = 0; addon = 0; }
    public byte[]? Get(ulong id, nint address) { if (!allowed() || id != item || address != addon) Clear(); return content; }
    public void Refresh(IDalamudPluginInterface pi, bool enabled)
    {
        if (!enabled) { Dispose(); Status = "無効"; return; }
        if (failed || wrapper != null || Environment.TickCount64 < nextAttempt) return;
        nextAttempt = Environment.TickCount64 + 1000;
        try
        {
            if (!pi.InstalledPlugins.Any(p => p.InternalName == "ItemVendorLocation" && p.IsLoaded))
            { Status = "未ロード"; return; }
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "ItemVendorLocation").ToArray();
            if (assemblies.Length != 1) { Status = "ロード済みアセンブリが一意ではありません"; return; }
            var plugin = assemblies[0].GetType("ItemVendorLocation.Service", true)!.GetProperty("Plugin", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            var common = plugin.GetType().GetField("_xivCommon", Flags)!.GetValue(plugin)!;
            var functions = common.GetType().GetProperty("Functions")!.GetValue(common)!;
            var target = functions.GetType().GetProperty("Tooltips")!.GetValue(functions)!;
            var field = target.GetType().GetField("OnItemTooltip", Flags)!;
            var current = (Delegate)field.GetValue(target)!;
            var matches = current.GetInvocationList().Where(d => ReferenceEquals(d.Target, plugin) && d.Method.Name == "Tooltips_OnOnItemTooltip").ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("価格コールバックが一意ではありません");
            var signature = field.FieldType.GetMethod("Invoke")!;
            var parameters = signature.GetParameters();
            if (parameters.Length != 2 || parameters[1].ParameterType != typeof(ulong) || signature.ReturnType != typeof(void)) throw new InvalidOperationException("署名不一致");
            indexer = parameters[0].ParameterType.GetProperty("Item", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!;
            if (indexer.PropertyType != typeof(SeString)) throw new InvalidOperationException("文字列型不一致");
            priceField = Enum.Parse(indexer.GetIndexParameters()[0].ParameterType, "ShopSellingPrice");
            if (Convert.ToInt32(priceField) != 63) throw new InvalidOperationException("価格欄不一致");
            var tooltip = Expression.Parameter(parameters[0].ParameterType);
            var id = Expression.Parameter(typeof(ulong));
            var replacement = Expression.Lambda(field.FieldType, Expression.Call(Expression.Constant(this),
                typeof(VendorAdapter).GetMethod(nameof(Intercept), Flags)!, Expression.Convert(tooltip, typeof(object)), id), tooltip, id).Compile();
            original = matches[0]; owner = target; slot = field; wrapper = replacement;
            field.SetValue(target, DelegateSlot.Replace(current, original, replacement));
            Status = "接続済み / 実機未検証";
        }
        catch (Exception ex) { Status = "接続失敗: " + ex.GetBaseException().Message; failed = true; Dispose(); }
    }
    private void InvokeOriginal(object tooltip, ulong id)
    {
        try { original!.DynamicInvoke(tooltip, id); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
    }
    private void Intercept(object tooltip, ulong id)
    {
        if (busy || failed || !allowed()) { InvokeOriginal(tooltip, id); return; }
        var key = context();
        if (id == 0 || key.Id != id || key.Addon == 0) { InvokeOriginal(tooltip, id); return; }
        byte[] before;
        try { before = ((SeString)indexer!.GetValue(tooltip, [priceField])!).Encode(); }
        catch (Exception ex) { Clear(); Status = "取得失敗: " + ex.GetBaseException().Message; InvokeOriginal(tooltip, id); return; }
        busy = true;
        byte[]? after = null;
        bool completed = false, restored = false;
        try
        {
            InvokeOriginal(tooltip, id);
            completed = true;
            try { after = ((SeString)indexer!.GetValue(tooltip, [priceField])!).Encode(); }
            catch (Exception ex) { Status = "変更後取得失敗: " + ex.GetBaseException().Message; }
        }
        finally
        {
            try
            {
                // Never retain the provider's native tooltip object beyond this synchronous call.
                indexer!.SetValue(tooltip, SeString.Parse(before), [priceField]);
                restored = true; Restores++;
            }
            catch (Exception ex) { failed = true; Status = "復元失敗・分離停止: " + ex.GetBaseException().Message; }
            busy = false;
            Clear();
            if (completed && restored && after != null && !before.AsSpan().SequenceEqual(after))
            {
                item = id; addon = key.Addon;
                content = after;
                Captures++; Status = "価格欄を分離済み";
            }
        }
    }
    public bool HasContent(ulong id, nint address) => !failed && allowed() && content != null && id == item && address == addon;
    public void Dispose()
    {
        var wasFailed = failed;
        failed = true; // A retained wrapper must only forward to its original callback.
        try
        {
            if (slot != null && owner != null && original != null && wrapper != null && slot.GetValue(owner) is Delegate current)
                slot.SetValue(owner, DelegateSlot.Replace(current, wrapper, original));
            wrapper = null; original = null; owner = null; slot = null;
            failed = wasFailed;
        }
        catch (Exception ex)
        {
            Status = "購読復元失敗・分離停止: " + ex.GetBaseException().Message;
            // Keep the original delegate alive if the foreign slot still contains our wrapper.
            throw;
        }
        finally { Clear(); }
    }
}
