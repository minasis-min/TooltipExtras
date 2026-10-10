using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class Diagnostics
{
    private readonly Queue<string> events = new();
    private readonly Dictionary<string, uint[]> before = new();
    public long Sequence { get; private set; }
    public double LastRecordMs { get; private set; }
    public double MaxRecordMs { get; private set; }
    public void Record(AddonEvent type, AddonArgs args, long frame, ulong id)
    {
        if (args.AddonName is not ("ItemDetail" or "ActionDetail")) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        var line = $"{++Sequence}: frame={frame} {args.AddonName} {type} target={id}";
        if (args is AddonRequestedUpdateArgs update)
        {
            var index = args.AddonName == "ItemDetail" ? (int)StringArrayType.ItemDetail : (int)StringArrayType.ActionDetail;
            var arrays = (StringArrayData**)update.StringArrayData;
            if (arrays != null && arrays[index] != null)
            {
                var array = arrays[index];
                var count = Math.Clamp(array->AtkArrayData.Size, 0, 256);
                var hashes = new uint[count];
                for (var i = 0; i < count; i++)
                {
                    byte* p = array->StringArray[i];
                    if (p == null) continue;
                    uint hash = 2166136261;
                    for (var b = 0; b < 32768 && p[b] != 0; b++) hash = unchecked((hash ^ p[b]) * 16777619);
                    hashes[i] = hash;
                }
                if (type == AddonEvent.PreRequestedUpdate) before[args.AddonName] = hashes;
                else if (type == AddonEvent.PostRequestedUpdate && before.TryGetValue(args.AddonName, out var old))
                    line += " changedFields=" + string.Join(',', Enumerable.Range(0, Math.Min(old.Length, hashes.Length)).Where(i => old[i] != hashes[i]));
            }
        }
        events.Enqueue(line);
        while (events.Count > 256) events.Dequeue();
        }
        finally
        {
            LastRecordMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            MaxRecordMs = Math.Max(MaxRecordMs, LastRecordMs);
        }
    }
    public string[] Snapshot() => events.ToArray();
    public void Clear() { events.Clear(); before.Clear(); LastRecordMs = MaxRecordMs = 0; }
}
