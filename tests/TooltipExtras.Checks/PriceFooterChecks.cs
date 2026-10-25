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
        var detached=new PriceGeometry(348,89,340,334);
        check(PriceCapturePlan.TryCreate(new(355,107,236,322),355,355,355,355,241,false,out var normalPlan,out _)
            &&normalPlan==new PriceCapturePlan(244,211,false),"価格計画: 拡張済みの表示は価格分だけ縮小する");
        check(!PriceCapturePlan.TryCreate(new(355,107,236,322),355,355,355,355,245,false,out _,out var reason)
            &&reason.Contains("本文"),"価格計画: 本文を切る逆変換を拒否し理由を区別");
        check(PriceCapturePlan.TryCreate(detached,348,348,348,362,345,false,out var detachedPlan,out _)
            &&detachedPlan==new PriceCapturePlan(348,334,true),"価格計画: 高さが復帰済みなら可視性だけを変更する");
        check(!PriceCapturePlan.TryCreate(detached,347,348,348,362,345,false,out _,out _),"価格計画: 表面の高さ不一致を許可しない");
        check(PriceFooterState.CanHideWithoutResize(detached,348,348,348,362,345),"価格はみ出し: 本文345pxが収まる348pxの本体を縮めずに分離可能");
        check(!PriceFooterState.CanHideWithoutResize(detached,348,348,348,362,349),"価格はみ出し: 現在の本文が既に本体を超える場合は変更しない");
        check(!PriceFooterState.CanHideWithoutResize(detached with{NodeY=-1},348,348,348,362,345),"価格はみ出し: 負の価格座標は表示専用の分離でも拒否");
        check(!PriceFooterState.CanHideWithoutResize(detached,347,348,348,362,345),"価格はみ出し: コンポーネントの高さが未確定なら拒否");
        check(!PriceFooterState.CanHideWithoutResize(detached,348,348,348,347,345),"価格はみ出し: ルートが本体より小さい状態を拒否");
        check(!PriceFooterState.CanHideWithoutResize(detached with{FooterY=float.NaN},348,348,348,362,345),"価格はみ出し: 不正な末尾座標を拒否");
        check(!PriceFooterState.CanHideWithoutResize(detached with{NodeHeight=8},348,348,348,362,345),"価格はみ出し: 未確定の小さな価格高さは分離しない");
        var dormantDetached=new PriceGeometry(244,107,236,301);
        var previewOverflow=new PriceGeometry(180,107,416,515);
        check(PriceCapturePlan.TryCreate(previewOverflow,180,180,180,180,178,true,out var overflowPlan,out _)
            &&overflowPlan==new PriceCapturePlan(180,515,true),"画像併用: 本文180pxの下416pxに残った価格を高さ変更なしで分離");
        check(PriceCapturePlan.TryCreate(previewOverflow with{NodeY=470,FooterY=569},180,180,180,180,178,true,out overflowPlan,out _)
            &&overflowPlan==new PriceCapturePlan(180,569,true),"画像併用: 再表示で価格の残存位置が変わっても現在の本体を維持");
        check(!PriceCapturePlan.TryCreate(previewOverflow,180,180,180,180,181,true,out _,out _),"画像併用: 本文が収まらない場合に高さ復帰を推測しない");
        check(!PriceCapturePlan.TryCreate(previewOverflow,180,180,180,180,178,false,out _,out _),"画像併用: 可視案内が枠外の状態は変更しない");
        check(!PriceCapturePlan.TryCreate(previewOverflow with{NodeY=15900},180,180,180,180,178,true,out _,out _),"画像併用: 価格ノード下端が上限を超える位置は拒否");
        check(!PriceCapturePlan.TryCreate(previewOverflow with{NodeY=float.PositiveInfinity},180,180,180,180,178,true,out _,out _),"画像併用: 無限大の位置は拒否");
        check(PriceCapturePlan.TryCreate(dormantDetached,244,244,244,244,241,true,out detachedPlan,out _)
            &&detachedPlan==new PriceCapturePlan(244,301,true),"価格計画: 休止中の旧末尾座標を移動しない");
        check(PriceFooterState.CanHideWithoutResize(dormantDetached,244,244,244,244,241,true),"別アイテム再発: 非表示案内の古い座標301を保持し244pxの本文を縮めずに分離");
        check(!PriceFooterState.CanHideWithoutResize(dormantDetached,244,244,244,244,241,false),"別アイテム再発: 案内が可視なら本体外の座標を許可しない");
        check(!PriceFooterState.CanHideWithoutResize(dormantDetached with{FooterY=float.NaN},244,244,244,244,241,true),"非表示案内でもNaNの座標を拒否");
        check(!PriceFooterState.CanHideWithoutResize(dormantDetached with{FooterY=-1},244,244,244,244,241,true),"非表示案内でも負の座標を拒否");
        check(!PriceFooterState.CanHideWithoutResize(dormantDetached,244,244,244,244,245,true),"非表示案内でも本文が本体を超える形状を拒否");
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
