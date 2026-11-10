using Dalamud.Interface;
using MinasisUI.Workspace;
namespace TooltipExtras;

internal static class SettingsNavigation
{
    internal static readonly WorkspacePage[] Pages = [
        new("general","基本",FontAwesomeIcon.Cog,[]),
        new("display","表示",FontAwesomeIcon.Eye,[]),
        new("providers","有効機能",FontAwesomeIcon.Check,[new("plugins","プラグイン"),new("details","個別情報")]),
        new("diagnostics","Debug / 診断",FontAwesomeIcon.InfoCircle,[new("status","状態"),new("rejected","未分離記録"),new("files","収集・保存")])
    ];
    internal static readonly string[] PageLabels=Pages.Select(p=>p.Label).ToArray();
    private static readonly string[][] Labels=Pages.Select(p=>p.Tabs.Select(t=>t.Label).ToArray()).ToArray();
    internal static string[] TabLabels(int page)=>Labels[page];
    internal static WorkspaceDefinition Definition(string version)=>new("TooltipExtras.Settings","Tooltip Extras","Tooltip Extras",version,Pages);
    internal static string Title(WorkspaceState state)
    {
        var page=Pages.First(p=>p.Id==state.PageId);
        return page.Tabs.FirstOrDefault(t=>t.Id==state.TabId)?.Label??page.Label;
    }
}
