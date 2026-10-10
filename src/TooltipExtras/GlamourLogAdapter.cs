using Lumina.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace TooltipExtras;

// Read-only consumer: no Glamour Log implementation or inventory scanner is embedded.
internal sealed class GlamourLogAdapter
{
    private readonly IDataManager data;
    private readonly ICallGateSubscriber<uint, bool> armoire, dresser, complete;
    private Dictionary<uint, List<Outfit>>? outfits;
    private sealed record Outfit(uint Id, string Name, uint[] Pieces);
    private readonly RefreshCache cache = new();
    public string Status { get; private set; } = "待機中";
    public byte[]? Content => cache.Content;
    private readonly Queue<QueryDiagnostic> history = new();
    private sealed record ItemResult(uint ItemId, bool Armoire, bool Dresser);
    private sealed record SetResult(uint ItemId, bool Complete);
    private sealed record QueryDiagnostic(ulong RawId, uint ItemId, int MatchedSets, ItemResult[] Items, SetResult[] Sets, string Error);
    public object Snapshot() => history.ToArray();
    private double indexBuildMs, lastQueryMs, maxQueryMs;
    private int lastIpcCalls, maxIpcCalls;
    private long refreshes;
    public object Performance() => new { IndexBuildMs = indexBuildMs, LastQueryMs = lastQueryMs, MaxQueryMs = maxQueryMs,
        LastIpcCalls = lastIpcCalls, MaxIpcCalls = maxIpcCalls, Refreshes = refreshes };
    private void Record(QueryDiagnostic entry)
    {
        if (history.Count == 16) history.Dequeue();
        history.Enqueue(entry);
    }
    public GlamourLogAdapter(IDalamudPluginInterface pi, IDataManager data)
    {
        this.data = data;
        armoire = pi.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemInArmoire");
        dresser = pi.GetIpcSubscriber<uint, bool>("GlamourLog.IsItemInDresser");
        complete = pi.GetIpcSubscriber<uint, bool>("GlamourLog.IsSetComplete");
    }
    private void BuildIndex()
    {
        var result = new Dictionary<uint, List<Outfit>>();
        var items = data.GetExcelSheet<Item>();
        foreach (var row in data.GetExcelSheet<MirageStoreSetItem>())
        {
            var pieces = new[] { row.MainHand, row.OffHand, row.Head, row.Body, row.Hands, row.Legs, row.Feet, row.Earrings, row.Necklace, row.Bracelets, row.Ring }.Select(i => i.RowId).Where(id => id != 0).Distinct().ToArray();
            if (row.RowId == 0 || pieces.Length == 0) continue;
            var outfit = new Outfit(row.RowId, items.GetRow(row.RowId).Name.ExtractText(), pieces);
            foreach (var id in pieces.Append(row.RowId).Distinct())
            {
                if (!result.TryGetValue(id, out var list)) result[id] = list = [];
                list.Add(outfit);
            }
        }
        outfits = result;
    }
    public void Clear() => cache.Clear();
    public void Update(ulong rawId, nint addon, bool active, bool providerAvailable, long now)
    {
        if (!active || !providerAvailable)
        {
            Clear();
            Status = active ? "Glamour Logが未ロード" : "待機中";
            return;
        }
        if (!armoire.HasFunction || !dresser.HasFunction || !complete.HasFunction)
        {
            Clear();
            Status = "必要な公開IPCが未登録。登録後に自動復帰します。";
            return;
        }
        if (!cache.ShouldRefresh(rawId, addon, now)) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        lastIpcCalls = 0; refreshes++;
        try
        {
            if (rawId is 0 or >= 2_000_000) { cache.Set(null); Status = "対象外"; return; }
            var id = (uint)(rawId % 500_000);
            var item = data.GetExcelSheet<Item>().GetRow(id);
            BuildIndexIfNeeded();
            outfits!.TryGetValue(id, out var matches);
            if (item.EquipSlotCategory.RowId == 0 && matches == null)
            { cache.Set(null); Status = "装備・衣装セット以外"; return; }
            var states = new Dictionary<uint, string>();
            var unregistered = new HashSet<uint>();
            var itemResults = new List<ItemResult>(); var setResults = new List<SetResult>();
            string State(uint part)
            {
                if (!states.TryGetValue(part, out var state))
                {
                    lastIpcCalls++; var a = armoire.InvokeFunc(part);
                    lastIpcCalls++; var d = dresser.InvokeFunc(part);
                    itemResults.Add(new(part, a, d));
                    if (!a && !d) unregistered.Add(part);
                    states[part] = state = a && d ? "キャビネット／ドレッサー"
                        : a ? "キャビネット" : d ? "ドレッサー" : "収納未登録／情報未読込";
                }
                return state;
            }
            var lines = new List<(string Text, bool Ochre)> { ("このアイテム: " + State(id), false) };
            if (matches != null)
            {
                foreach (var outfit in matches)
                {
                    lastIpcCalls++; var finished = complete.InvokeFunc(outfit.Id);
                    setResults.Add(new(outfit.Id, finished));
                    lines.Add(($"セット: {outfit.Name}（{(finished ? "完成" : "未完成／情報未読込")}）", false));
                    foreach (var part in outfit.Pieces)
                        lines.Add(($"  {data.GetExcelSheet<Item>().GetRow(part).Name.ExtractText()} — {State(part)}", unregistered.Contains(part)));
                }
            }
            var builder = new SeStringBuilder();
            for (var index = 0; index < lines.Count; index++)
            {
                if (index != 0) builder.AppendNewLine();
                var line = lines[index];
                if (line.Ochre) builder.PushColorRgba(184, 155, 70, 255); // #B89B46
                builder.Append(line.Text);
                if (line.Ochre) builder.PopColor();
            }
            cache.Set(builder.GetViewAsSpan().ToArray());
            Record(new(rawId, id, matches?.Count ?? 0, itemResults.ToArray(), setResults.ToArray(), ""));
            Status = itemResults.All(r => !r.Armoire && !r.Dresser)
                ? "IPC正常応答・収納判定は全てfalse（未登録と未読込の区別不可）"
                : "IPC正常応答・収納済み判定あり";
        }
        catch (Exception ex)
        {
            cache.Set(null);
            Status = "IPCまたはゲームデータ取得失敗（再取得待ち）: " + ex.GetType().Name;
            Record(new(rawId, (uint)(rawId % 500_000), 0, [], [], ex.GetType().Name));
        }
        finally
        {
            lastQueryMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            maxQueryMs = Math.Max(maxQueryMs, lastQueryMs);
            maxIpcCalls = Math.Max(maxIpcCalls, lastIpcCalls);
        }
    }
    private void BuildIndexIfNeeded()
    {
        if (outfits != null) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { BuildIndex(); }
        finally { indexBuildMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }
}
