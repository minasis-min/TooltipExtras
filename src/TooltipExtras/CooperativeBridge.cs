using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
namespace TooltipExtras;

internal sealed class CooperativeBridge : IDisposable
{
    private readonly ICallGateProvider<int, ulong, long> begin;
    private readonly ICallGateProvider<string, int, ulong, long, byte[], bool> publish;
    private readonly Func<bool> allowed, patchEnabled;
    private readonly CooperativeState state = new();
    private bool? lastShowNames, lastPatchEnabled, lastBuiltInPatch;
    private bool disposed;
    public CooperativeBridge(IDalamudPluginInterface pi, Func<bool> allowed, Func<bool> patchEnabled)
    {
        this.allowed = allowed; this.patchEnabled = patchEnabled;
        begin = pi.GetIpcProvider<int, ulong, long>("TooltipExtras.Begin.v1");
        publish = pi.GetIpcProvider<string, int, ulong, long, byte[], bool>("TooltipExtras.Publish.v1");
        try { begin.RegisterFunc(Begin); publish.RegisterFunc(Publish); }
        catch (Exception original)
        {
            disposed = true;
            var cleanup = new Cleanup();
            cleanup.Try("Begin IPC rollback", begin.UnregisterFunc);
            cleanup.Try("Publish IPC rollback", publish.UnregisterFunc);
            try { cleanup.ThrowIfFailed(); }
            catch (Exception ex) { throw new AggregateException(original, ex); }
            throw;
        }
    }
    public void Requested(TooltipKey key, nint addon) => state.Requested(key, addon);
    public void Observe(TooltipKey key, nint addon, bool visible) => state.Observe(key, addon, visible);
    public void PreDraw(TooltipKey key, nint addon, long frame) => state.PreDraw(key, addon);
    private long Begin(int kind, ulong id) => !disposed && allowed() && Enum.IsDefined((TooltipKind)kind) ? state.Begin((TooltipKind)kind, id) : 0;
    private bool Publish(string source, int kind, ulong id, long token, byte[] bytes)
    {
        if (disposed || !allowed() || !Enum.IsDefined((TooltipKind)kind) || bytes == null || bytes.Length > 32768) return false;
        try { if (!SeString.Parse(bytes).Encode().AsSpan().SequenceEqual(bytes)) return false; } catch { return false; }
        if (string.IsNullOrWhiteSpace(source) || source.Length > 128 || source.Any(char.IsControl)) return false;
        if (!CooperativePolicy.Accept(source, patchEnabled())) return false;
        return state.Publish(source, (TooltipKind)kind, id, token, bytes);
    }
    public byte[]? Get(TooltipKey key, nint addon, bool visible, bool showNames, bool patchEnabled, bool builtInPatch = true)
    {
        if (lastShowNames != showNames || lastPatchEnabled != patchEnabled || lastBuiltInPatch != builtInPatch)
        { lastShowNames = showNames; lastPatchEnabled = patchEnabled; lastBuiltInPatch = builtInPatch; state.InvalidatePresentation(); }
        return state.Get(key, addon, visible, (source, body) => !CooperativePolicy.Display(source, patchEnabled, builtInPatch)
            ? [] : PanelContent.Section(PanelContent.SourceName(source), body, showNames));
    }
    public static byte[] Join(IEnumerable<byte[]> parts) => CooperativeState.Join(parts);
    public string[] Snapshot() => [$"requests={state.Requests} accepted={state.Accepted}", state.Describe(TooltipKind.Item), state.Describe(TooltipKind.Action)];
    public void EndRequested(TooltipKind kind) => state.EndRequested(kind);
    public void Clear(TooltipKind kind) => state.Clear(kind);
    public void Clear() => state.Clear();
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var cleanup = new Cleanup();
        cleanup.Try("Begin IPC", begin.UnregisterFunc);
        cleanup.Try("Publish IPC", publish.UnregisterFunc);
        cleanup.Try("IPC state", Clear);
        cleanup.ThrowIfFailed();
    }
}
