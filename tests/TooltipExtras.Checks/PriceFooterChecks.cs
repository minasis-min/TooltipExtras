using FFXIVClientStructs.FFXIV.Component.GUI;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static unsafe class PriceFooterChecks
{
    public static void Run(Action<bool,string> check)
    {
        var footer=new AtkResNode{NodeId=2};
        var hint=new AtkResNode{NodeId=3,ParentNode=&footer};
        var nodes=stackalloc AtkResNode*[2];nodes[0]=&footer;nodes[1]=&hint;
        check(PriceFooterState.HintsDormant(nodes,2,&footer),"価格末尾: 直属の非表示ヒントだけ休止中と判定");
        hint.NodeFlags=NodeFlags.Visible;
        check(!PriceFooterState.HintsDormant(nodes,2,&footer),"価格末尾: 可視ヒントは対象外");
        hint.NodeFlags=0;hint.ParentNode=null;
        check(!PriceFooterState.HintsDormant(nodes,2,&footer),"価格末尾: 不明な親では例外を許可しない");
        hint.ParentNode=&footer;nodes[0]=&hint;
        check(!PriceFooterState.HintsDormant(nodes,2,&footer),"価格末尾: 重複ヒントIDは対象外");
        var shape=new PriceGeometry(347,107,228,353);
        check(!PriceFooterState.TryRemove(shape,false,out _,out _),"ページ切替: 可視案内が本体高さ236外なら拒否");
        check(PriceFooterState.TryRemove(shape,true,out var height,out var y)&&height==236&&y==242,"ページ切替: 非表示の旧案内Y242を再配置せず価格111pxだけ分離");
        check(PriceRestorePlan.TryCreate(347,236,353,242,new(236,242),out var plan,true)&&plan==new PriceRestorePlan(347,353,228),"ページ切替: 価格OFFで同じ座標基準へ復元");
        check(!PriceRestorePlan.TryCreate(347,236,353,242,new(236,242),out _,false),"価格復元: 案内が可視になったら範囲外の復元を保留");
        check(!PriceFooterState.TryRemove(shape with{NodeY=229},true,out _,out _),"休止中末尾でも価格の生成位置不一致は拒否");
        check(!PriceFooterState.Safe(236,float.NaN,true)&&!PriceFooterState.Safe(236,-1,true)&&!PriceFooterState.Safe(236,16001,true),"休止中末尾でも不正値・負数・上限超過は拒否");
        check(PriceFooterState.TryRemove(new(245,89,144,335),true,out height,out y)&&height==152&&y==242,"小型アイテム: 診断の245→152pxと335→242pxを再現");
        check(PriceRestorePlan.TryCreate(245,152,335,242,new(152,242),out plan,true)&&plan==new PriceRestorePlan(245,335,144),"小型アイテム: 分離・復元で高さが累積しない");
    }
}