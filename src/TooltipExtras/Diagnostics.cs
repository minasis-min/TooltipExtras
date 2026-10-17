using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Component.GUI;
namespace TooltipExtras;

internal sealed unsafe class Diagnostics
{
    private readonly Queue<string> events = new();
    private readonly Dictionary<string, uint[]> before = new();
    private readonly Queue<LayoutSample> layouts=new(128);
    private readonly Dictionary<string,LayoutSample> lastLayouts=new();
    private readonly record struct LayoutSample(string Stage,long Frame,ulong Item,short X,short Y,ushort Height,ushort ComponentHeight,ushort RootHeight,float FooterY,float HintY,uint HintParent,float HintParentY,float FooterScreenY,float HintScreenY,uint FooterDrawFlags);
    public void RecordLayout(string stage,long frame,ulong item,AtkUnitBase* a,bool screenCoordinates)
    {
        if(item==0&&stage!="PreHide"&&stage!="PreFinalize")return;
        if(a==null||a->RootNode==null||a->WindowNode==null||a->UldManager.NodeListCount>1024)return;
        var footer=NativeNodeLookup.FindUnique(a->UldManager.NodeList,a->UldManager.NodeListCount,2,out _);
        var hint=NativeNodeLookup.FindUnique(a->UldManager.NodeList,a->UldManager.NodeListCount,3,out _);
        if(footer==null||hint==null)return;
        var parent=hint->ParentNode;
        var sample=new LayoutSample(stage,frame,item,a->X,a->Y,a->WindowNode->AtkResNode.Height,a->WindowNode->Height,a->RootNode->Height,
            footer->Y,hint->Y,parent==null?0:parent->NodeId,parent==null?0:parent->Y,screenCoordinates?footer->ScreenY:0,screenCoordinates?hint->ScreenY:0,screenCoordinates?footer->DrawFlags:0);
        // Keep transitions rather than filling the ring with identical idle frames.
        var comparable=sample with {Frame=0};
        if(lastLayouts.TryGetValue(stage,out var old)&&old==comparable)return;
        lastLayouts[stage]=comparable;layouts.Enqueue(sample);
        while(layouts.Count>128)layouts.Dequeue();
    }
    public object LayoutSnapshot()=>layouts.ToArray();
    public long Sequence { get; private set; }
    public double LastRecordMs { get; private set; }
    public double MaxRecordMs { get; private set; }
    public void Record(AddonEvent type, AddonArgs args, long frame, ulong id)
    {

        // Per-frame geometry is captured separately as deduplicated transitions.
        // Keep requested/hide/finalize events in this ring instead of idle ticks.
        if(type is AddonEvent.PreUpdate or AddonEvent.PostUpdate or AddonEvent.PreDraw or AddonEvent.PostDraw)return;
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
    public void Clear() { events.Clear(); before.Clear(); layouts.Clear(); lastLayouts.Clear(); LastRecordMs = MaxRecordMs = 0; }
}
