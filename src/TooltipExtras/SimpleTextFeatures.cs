namespace TooltipExtras;

// One ordering shared by capture, compatibility checks, settings and diagnostics.
internal sealed record SimpleTextFeature(string Name,string Group,int[] Fields,string Label,string OptionId,string OptionLabel,Func<Settings,bool> Selected,Action<Settings,bool>? Change)
{
    public string Source {get;}="Simple Tweaks / "+Name;
}
internal static class SimpleTextFeatures
{
    public static readonly IReadOnlyList<SimpleTextFeature> All=Array.AsReadOnly<SimpleTextFeature>([
        new("FoodStats","SimpleFood",[16],"食事・薬品","simple-food","食事・薬品の詳細ステータス",s=>s.SimpleFood,(s,v)=>s.SimpleFood=v),
        new("MateriaStats","SimpleMateriaStats",[37,38,39,40,41,42,43,44],"マテリア能力値","simple-materia-stats","マテリアの合計・増分",s=>s.SimpleMateriaStats,(s,v)=>s.SimpleMateriaStats=v),
        new("DesynthesisSkill","SimpleDesynth",[13,35],"分解スキル","simple-desynth","分解スキル",s=>s.SimpleDesynth,(s,v)=>s.SimpleDesynth=v),
        new("CollectableRewards","SimpleCollectables",[13],"収集品報酬","simple-collectables","収集品の納品報酬",s=>s.SimpleCollectables,(s,v)=>s.SimpleCollectables=v),
        new("LogosTooltip","SimpleLogos",[13],"ロゴスアクション","simple-logos","ロゴスアクションの入手情報",s=>s.SimpleLogos,(s,v)=>s.SimpleLogos=v),
        new("TrackFadedRolls","SimpleFadedRolls",[13],"譜面の製作先・取得状況","simple-faded-rolls","譜面の製作先・取得状況",s=>s.SimpleFadedRolls,(s,v)=>s.SimpleFadedRolls=v),
        new("TrackGachaItems","SimpleGacha",[13],"ランダム報酬アイテム","simple-gacha","ランダム報酬アイテムの取得状況",s=>s.SimpleGacha,(s,v)=>s.SimpleGacha=v),
        new("TrackOutfits","SimpleOutfits",[13],"衣装セットの取得状況","simple-outfits","衣装セットの取得状況",s=>s.SimpleOutfits,(s,v)=>s.SimpleOutfits=v),
        new("GlamItemNames","SimpleGlamourName",[],"アイテム名・ミラプリ名","simple-glamour-name","アイテム名・ミラプリ名",s=>s.SimpleGlamourName,(s,v)=>s.SimpleGlamourName=v),
        new("ShowItemID","SimpleItemIdText",[2],"アイテムID","simple-item-id","アイテムID",s=>true,null),
        new("ItemHotkeys","SimpleHotkeys",[64],"アイテム操作のヒント","simple-hotkeys","アイテム操作のヒント",s=>s.SimpleHotkeys,(s,v)=>s.SimpleHotkeys=v)
    ]);
    public static int IndexOf(string name)
    {
        for(var i=0;i<All.Count;i++)if(All[i].Name==name)return i;
        return -1;
    }
}
