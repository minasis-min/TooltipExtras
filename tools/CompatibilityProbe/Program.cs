using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using TooltipExtras;
if (args.Length < 3) throw new ArgumentException("Usage: probe generate|check output-or-contracts sdk-directory [provider-directories...]");
var folders = args.Skip(2).ToArray();
AssemblyLoadContext.Default.Resolving += (_, name) => {
    foreach (var folder in folders) { var file = Path.Combine(folder, name.Name + ".dll"); if (File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(file)); }
    return null;
};
Type? Resolve(string assembly, string name) => Assembly.Load(new AssemblyName(assembly)).GetType(name, false);
if (args[0] == "check") {
    var definitions = JsonSerializer.Deserialize<CompatibilityEntry[]>(File.ReadAllText(args[1]))!;
    var failed = false;
    foreach (var group in definitions.Select(d => d.Group).Distinct()) { var result = CompatibilityContract.Check(definitions, group, Resolve); Console.WriteLine(group + ": " + result.Reason); failed |= !result.Allowed; }
    return failed ? 1 : 0;
}
var rows = new List<CompatibilityEntry>();
void Add(string group, string assembly, string type, string kind, params string[] names) {
    var t = Resolve(assembly, type) ?? throw new TypeLoadException(type);
    foreach (var name in names) rows.Add(new(group, assembly, type, kind, name, CompatibilityContract.Value(t, kind, name)));
}
void NativeDependencies(string group, Type t, string name) {
    var pending = new Stack<MethodBase>();
    foreach(var method in t.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).Where(m=>m.Name==name)) pending.Push(method);
    var visited = new HashSet<MethodBase>();
    while(pending.TryPop(out var method)) {
        if(!visited.Add(method)) continue;
        if(visited.Count>256) throw new NotSupportedException("Native call graph too large");
        foreach(var field in MethodCompatibility.Fields(method)) {
            if(field.DeclaringType?.Assembly==t.Assembly && field.GetCustomAttribute<System.Runtime.InteropServices.FieldOffsetAttribute>()!=null) {
                var owner=field.DeclaringType!;
                Add(group,"FFXIVClientStructs",owner.FullName!,"layout", "");
                Add(group,"FFXIVClientStructs",owner.FullName!,"field",field.Name);
            }
        }
        foreach(var called in MethodCompatibility.Calls(method)) if(called.DeclaringType?.Assembly==t.Assembly && called.GetMethodBody()!=null) pending.Push(called);
    }
}
void Native(string group, string type, string[] fields, string[] methods) {
    var name="FFXIVClientStructs.FFXIV.Component.GUI."+type;
    Add(group, "FFXIVClientStructs", name, "layout", "");
    if(fields.Length > 0) Add(group,"FFXIVClientStructs",name,"field",fields);
    if(methods.Length > 0) Add(group,"FFXIVClientStructs",name,"operation",methods);
    foreach(var method in methods) NativeDependencies(group,Resolve("FFXIVClientStructs",name)!,method);
}
Add("Panel", "Dalamud", "Dalamud.Plugin.Services.IGameGui", "methods", "GetAddonByName","get_HoveredItem","get_HoveredAction","get_GameUiHidden");
Add("Panel", "Dalamud", "Dalamud.Game.Gui.DetailKind", "enum", "Companion", "Mount", "Ornament", "Glasses");
Add("Panel", "Dalamud", "Dalamud.Plugin.Services.ICondition", "methods", "get_Item");
Add("Panel", "Dalamud", "Dalamud.Plugin.Services.IFramework", "methods", "get_IsInFrameworkUpdateThread");
Add("Panel", "Dalamud", "Dalamud.Plugin.IDalamudPluginInterface", "methods", "GetIpcSubscriber");
Add("Panel", "Dalamud", "Dalamud.Plugin.Services.IClientState", "methods", "get_IsLoggedIn","get_TerritoryType");
Add("Panel", "Dalamud", "Dalamud.Plugin.Services.IAddonLifecycle", "methods", "RegisterListener","UnregisterListener");
Add("Panel","Dalamud","Dalamud.Game.Addon.Lifecycle.AddonEvent","enum","PreRequestedUpdate","PostRequestedUpdate","PreDraw","PreHide","PreFinalize");
Add("Panel","Dalamud","Dalamud.Game.ClientState.Conditions.ConditionFlag","enum","BetweenAreas","BetweenAreas51");
Native("Panel","AtkUnitBase",["X","Y","RootNode","Scale","Flags198"],["get_IsVisible","GetScaledWidth","GetScaledHeight"]);
Native("Panel","AtkResNode",["Width","Height","ScaleX","ScaleY"],[]);
Native("PriceNative","AtkUnitBase",["UldManager","WindowNode","RootNode"],["GetNodeById"]);
Native("PriceNative","AtkResNode",["NodeId","Type","ParentNode","PrevSiblingNode","NextSiblingNode","Y","Width","Height","NodeFlags"],["IsVisible","ToggleVisibility","SetHeight","SetYFloat"]);
Native("PriceNative","AtkTextNode",["AtkResNode","NodeText"],[]);
Native("PriceNative","AtkComponentNode",["AtkResNode","Component","Height"],["SetHeight"]);
Native("PriceNative","AtkComponentBase",["UldManager"],[]);
Native("PriceNative","AtkUldManager",["NodeListCount","NodeList","RootNode"],[]);
var utf8=Resolve("FFXIVClientStructs","FFXIVClientStructs.FFXIV.Client.System.String.Utf8String")!;
Add("PriceNative","FFXIVClientStructs",utf8.FullName!,"layout", "");
foreach(var f in utf8.GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>f.GetCustomAttribute<System.Runtime.InteropServices.FieldOffsetAttribute>()!=null)) Add("PriceNative","FFXIVClientStructs",utf8.FullName!,"field",f.Name);
Add("PriceNative","FFXIVClientStructs",utf8.FullName!,"body","AsSpan");
Add("DiagnosticsNative","FFXIVClientStructs","FFXIVClientStructs.FFXIV.Component.GUI.StringArrayType","enum","ItemDetail","ActionDetail");
Add("PriceNative","FFXIVClientStructs","FFXIVClientStructs.FFXIV.Component.GUI.NodeType","enum","Text");
Native("DiagnosticsNative","StringArrayData",["AtkArrayData","StringArray"],[]);
Native("DiagnosticsNative","AtkArrayData",["Size"],[]);
Add("PriceProvider","PriceInsight","PriceInsight.ItemPriceTooltip","operation","UpdateItemTooltip","RestoreToNormal","OnItemTooltip");
Add("VendorProvider","ItemVendorLocation","ItemVendorLocation.EntryPoint","operation","Tooltips_OnOnItemTooltip");
// The visible foreign row exception is allowed only when its relevant tooltip operation is unchanged.
var simple = Resolve("SimpleTweaksPlugin", "SimpleTweaksPlugin.Tweaks.Tooltips.AdditionalItemInfo")!;
Add("SimpleLayout","SimpleTweaksPlugin",simple.FullName!,"operation","BeforeItemDetailUpdate","AfterItemDetailUpdate");
Add("GlamourSchema","Lumina.Excel","Lumina.Excel.Sheets.Item","property","Name","EquipSlotCategory");
Add("GlamourSchema","Lumina.Excel","Lumina.Excel.Sheets.MirageStoreSetItem","property","RowId","MainHand","OffHand","Head","Body","Hands","Legs","Feet","Earrings","Necklace","Bracelets","Ring");
File.WriteAllText(args[1], JsonSerializer.Serialize(rows.Distinct().ToArray(), new JsonSerializerOptions { WriteIndented=true })+Environment.NewLine);
Console.WriteLine("Generated "+rows.Distinct().Count()+" contracts.");
return 0;
