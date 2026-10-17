using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class AllaganAdapter : IDisposable
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly int[] Fields = [13, 16, 23];
    private static readonly string[] Names = ["AmountOwnedTooltip", "SourceInformationTooltip", "UseInformationTooltip"];
    private readonly Func<bool> allowed;
    private readonly Func<(ulong Id, nint Addon)> context;
    private readonly Func<bool> frameworkThread;
    private readonly List<(IList List, object Original, object Proxy)> slots = [];
    private readonly RelaySnapshot snapshot = new(compact: true);
    private bool stopped, busy, requested, generationFailed;
    private ulong item;
    private nint addon, strings;
    private long nextAttempt;
    public string Status { get; private set; } = "待機中";
    public long Captures { get; private set; }
    public long Rejections { get; private set; }
    public long Requests { get; private set; }
    public long Callbacks { get; private set; }
    public long OutsideRequests { get; private set; }
    public int Sections => snapshot.Count;
    public int Connections => slots.Count;
    public AllaganAdapter(Func<bool> allowed, Func<(ulong, nint)> context, Func<bool> frameworkThread)
    { this.allowed = allowed; this.context = context; this.frameworkThread = frameworkThread; }

    private static object? Field(object owner, string name)
    {
        for (var type = owner.GetType(); type != null; type = type.BaseType)
            if (type.GetField(name, Flags | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(owner);
        throw new MissingFieldException(owner.GetType().FullName, name);
    }
    public void Refresh(IDalamudPluginInterface pi, bool enabled)
    {
        if (!enabled) { if (slots.Count > 0) Dispose(); stopped = false; Clear(); Status = "無効"; return; }
        if (stopped || Environment.TickCount64 < nextAttempt) return;
        nextAttempt = Environment.TickCount64 + 1000;
        try
        {
            var installed = pi.InstalledPlugins.Where(p => p.InternalName == "InventoryTools" && p.IsLoaded).ToArray();
            if (installed.Length != 1) { if (slots.Count > 0) Dispose(); Status = "未ロード、または複数ロード"; return; }
            // Current Dalamud ExposedPlugin holds LocalPlugin; inspect only this selected plugin.
            var localField = installed[0].GetType().GetFields(Flags).Single(f => f.FieldType.FullName == "Dalamud.Plugin.Internal.Types.LocalPlugin");
            var local = localField.GetValue(installed[0])!;
            var plugin = Field(local, "instance");
            if (plugin?.GetType().FullName != "InventoryTools.InventoryToolsPlugin") { Status = "インスタンス準備待ち"; return; }
            if (plugin.GetType().GetProperty("IsStarted", Flags)?.GetValue(plugin) is not true) { Status = "初期化待ち"; return; }
            var hostProperty = plugin.GetType().GetProperty("Host", Flags) ?? throw new MissingMemberException("HostedPlugin.Host");
            var host = hostProperty.GetValue(plugin)!;
            var services = (IServiceProvider)hostProperty.PropertyType.GetProperty("Services")!.GetValue(host)!;
            var loadContext = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
            var common = loadContext.Assemblies.Single(a => a.GetName().Name == "CriticalCommonLib");
            var service = services.GetService(common.GetType("CriticalCommonLib.Services.ITooltipService", true)!) ?? throw new InvalidOperationException("TooltipService未登録");
            var list = (IList)Field(service, "_tooltipTweaks")!;
            if (slots.Count > 0)
            {
                if (slots.All(s => ReferenceEquals(s.List, list) && list.Cast<object>().Any(p => ReferenceEquals(p, s.Proxy)))) return;
                Dispose();
            }
            var baseType = common.GetType("CriticalCommonLib.Services.TooltipService+TooltipTweak", true)!;
            var check = CompatibilityContract.Check(CompatibilityContract.Load(), "AllaganProvider", (assembly, type) =>
                assembly == "CriticalCommonLib" ? common.GetType(type) : assembly == "InventoryTools" ? plugin.GetType().Assembly.GetType(type) : null);
            if (!check.Allowed) throw new NotSupportedException(check.Reason);
            var selected = Names.Select(name => list.Cast<object>().Single(t => t.GetType().FullName == "InventoryTools.Tooltips." + name)).ToArray();
            var callback = baseType.GetMethod("OnGenerateItemTooltip")!;
            var pointerTypes = callback.GetParameters().Select(p => p.ParameterType.GetElementType()!.FullName).ToArray();
            var providerNative = callback.GetParameters()[1].ParameterType.GetElementType()!.Assembly;
            var abi = CompatibilityContract.Check(CompatibilityContract.Load(), "AllaganNative", (assembly, type) => assembly == "FFXIVClientStructs" ? providerNative.GetType(type) : null);
            if (!abi.Allowed) throw new NotSupportedException("AT側配列ABI: " + abi.Reason);
            if (!pointerTypes.SequenceEqual(new[] { typeof(NumberArrayData).FullName, typeof(StringArrayData).FullName })) throw new NotSupportedException("配列型不一致");
            var proxyType = TooltipProxy.Create(baseType);
            for (var i = 0; i < selected.Length; i++)
            {
                var index = i; var original = selected[i];
                var readEnabled = Expression.Lambda<Func<bool>>(Expression.Property(Expression.Constant(original), "IsEnabled")).Compile();
                Func<bool> enabledGetter = () =>
                {
                    var enabled = readEnabled();
                    if (!enabled) snapshot.Set(index, null);
                    return enabled;
                };
                var invoke = TooltipProxy.Forward(original, callback);
                Action<nint, nint> capture = (numbers, array) => Intercept(index, invoke, numbers, array);
                var proxy = Activator.CreateInstance(proxyType, [Field(original, "_logger"), enabledGetter, capture])!;
                var position = list.IndexOf(original);
                if (position < 0) throw new InvalidOperationException("追記登録が変更されました");
                slots.Add((list, original, proxy)); // Rollback can restore even if a later insertion fails.
                list[position] = proxy;
            }
            stopped = false; Status = "接続済み / 捕捉待ち";
        }
        catch (Exception ex)
        {
            stopped = true;
            Status = "中継停止: " + ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message;
            Detach();
        }
    }
    public void Begin(ulong id, nint address, nint array)
    {
        // Only the borrowing window resets here. The display lifetime is the target.
        snapshot.Begin(id, address); item = id; addon = address; strings = array;
        generationFailed = false; Requests++;
        requested = allowed() && frameworkThread() && id != 0 && address != 0 && array != 0;
    }
    public void End() { requested = false; strings = 0; }
    public void Clear()
    {
        snapshot.Clear(); requested = false; generationFailed = false;
        item = 0; addon = strings = 0;
    }
    public byte[]? Get(ulong id, nint address)
    {
        if (!allowed() || id != item || address != addon) { Clear(); return null; }
        return snapshot.Get(id, address);
    }
    private static byte[]? Read(StringArrayData* array, int field)
    {
        byte* text = array->StringArray[field];
        if (text == null) return null;
        var length = 0;
        while (length < 32768 && text[length] != 0) length++;
        if (length == 32768) throw new NotSupportedException("文字列上限超過");
        return new ReadOnlySpan<byte>(text, length).ToArray();
    }
    private static byte[]?[] Snapshot(StringArrayData* array) => Fields.Select(field => Read(array, field)).ToArray();
    private void Reject(string reason)
    {
        Rejections++; Status = "元表示を維持: " + reason;
        // Earlier successful sections were already removed from native fields. Keep them visible.
        generationFailed = true;
    }
    private void Intercept(int section, Action<nint, nint> original, nint numbers, nint address)
    {
        Callbacks++;
        if (stopped || busy || generationFailed || !requested || !frameworkThread() || !allowed() || address != strings)
        {
            OutsideRequests++;
            snapshot.Set(section, null); // Native forwarding must not also retain the same section on TE.
            original(numbers, address); return;
        }
        var key = context();
        if (key.Id != item || key.Addon != addon) { snapshot.Clear(); original(numbers, address); return; }
        var array = (StringArrayData*)address;
        byte[]?[] before;
        try
        {
            if (array == null || array->StringArray == null || array->AtkArrayData.Size <= 23 || array->AtkArrayData.Size > 256) throw new NotSupportedException("配列形状不一致");
            before = Snapshot(array);
        }
        catch (Exception ex) { snapshot.Set(section, null); Reject(ex.GetBaseException().Message); original(numbers, address); return; }
        busy = true;
        try
        {
            try { original(numbers, address); } // Exactly once; retain the provider's own failure behavior.
            catch { snapshot.Set(section, null); throw; }
            try
            {
                var after = Snapshot(array);
                if (before.Zip(after).All(p => p.First == null ? p.Second == null : p.Second != null && p.First.AsSpan().SequenceEqual(p.Second)))
                { snapshot.Set(section, null); return; }
                if (!AppendDelta.TrySelect(before, after, out var changed, out var suffix)) { snapshot.Set(section, null); Reject("追記境界が一致しません"); return; }
                key = context();
                if (!allowed() || key.Id != item || key.Addon != addon) { Reject("対象が変わりました"); return; }
                var restore = new byte[before[changed]!.Length + 1]; before[changed]!.CopyTo(restore, 0);
                try
                {
                    array->SetValue(Fields[changed], restore, false, true, false);
                    var restored = Read(array, Fields[changed]);
                    if (restored == null || !restored.AsSpan().SequenceEqual(before[changed])) throw new InvalidOperationException("復元値不一致");
                }
                catch (Exception restoreError)
                {
                    // If a write fails, try to retain the exact provider result in native output.
                    try
                    {
                        var fallback = new byte[after[changed]!.Length + 1]; after[changed]!.CopyTo(fallback, 0);
                        array->SetValue(Fields[changed], fallback, false, true, false);
                    }
                    catch { /* Stop all later writes; do not retry the provider callback. */ }
                    snapshot.Set(section, null); Reject("元本文の復元失敗: " + restoreError.GetBaseException().Message); stopped = true; return;
                }
                snapshot.Set(section, CapturedText.TrimAllaganEdges(suffix)); Captures++; Status = "追記を取得・元欄を復元済み";
            }
            catch (Exception ex) { Reject(ex.GetBaseException().GetType().Name + ": " + ex.GetBaseException().Message); }
        }
        finally { busy = false; }
    }
    private void Detach()
    {
        var cleanup = new Cleanup();
        foreach (var slot in slots.ToArray()) cleanup.Try("AT slot", () =>
        {
            AppendDelta.RestoreSlot(slot.List, slot.Proxy, slot.Original);
            slots.Remove(slot);
        });
        cleanup.ThrowIfFailed();
    }
    public void Dispose()
    {
        stopped = true; Clear(); Detach();
    }
    public void Reconnect()
    {
        Dispose(); stopped = false; nextAttempt = 0; Status = "再接続待ち";
    }
}
