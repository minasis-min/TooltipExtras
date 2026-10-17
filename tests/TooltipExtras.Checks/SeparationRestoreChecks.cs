using TooltipExtras;
namespace TooltipExtrasChecks;
internal static class SeparationRestoreChecks
{
    public static void Run(Action<bool,string> check)
    {
        PreviewGeometry G(ushort height)=>new(height,height,height,height-20);
        // These are the same immutable layout records / restore plans used by
        // PriceAdapter and SimplePreviewAdapter, including intermediate states.
        var initial=new PreviewLayout(G(589),G(478),G(478),G(178));
        var compact=new LayoutStamp(178,158);
        check(PriceRestorePlan.TryCreate(589,478,569,458,compact,out var off)&&off==new PriceRestorePlan(289,269,170),
            "価格OFF: 589→478→178の分離を178→289で逆変換し価格をY170へ戻す");
        check(initial.TryFollowPriceRestore(new(compact,new(off.Height,off.Footer)),out var priceVisible)&&
            priceVisible.Collapsed==G(289)&&priceVisible.Expanded==G(589)&&priceVisible.VisiblePriceDelta==111,
            "価格OFF: プレビュー非表示を保ち縮小側289・復元側589へ同時追従");
        check(priceVisible.SourceBefore==initial.SourceBefore&&priceVisible.Generated==initial.Generated,
            "価格OFF: 生成直前Rootと元画像配置は更新しない");
        check(!initial.TryFollowPriceRestore(new(new(179,159),new(290,270)),out _),
            "価格OFF: 一致しないレイアウト世代の復元通知を拒否");
        check(!initial.TryFollowPriceRestore(new(compact,new(289,270)),out _),
            "価格OFF: 窓と末尾の変位が違う通知を拒否");
        check(PriceRestorePlan.TryCreate(289,178,269,158,new(478,458),out var unload)&&unload==new PriceRestorePlan(589,569,470),
            "非同期価格: 289→178で再捕捉後に画像178→478を復元しても価格478→589を復元");
        check(priceVisible.TryFollowVisiblePrice(G(178),177,out var updated)&&updated.Collapsed==G(355)&&updated.Expanded==G(655)&&updated.VisiblePriceDelta==177,
            "元側価格の非同期更新: 新しい価格177pxへ両方の復元座標を追従");
        check(updated.TryFollowVisiblePrice(G(178),0,out var separated)&&separated.Collapsed==G(178)&&separated.Expanded==G(478)&&separated.VisiblePriceDelta==0,
            "価格再ON: 可視価格の除去後に画像の復元座標も価格なしへ戻す");
        check(PriceRestorePlan.TryCreate(355,178,335,158,new(478,458),out var updatedUnload)&&updatedUnload.Height==655&&updatedUnload.NodeY==470,
            "価格再ON後の終了: 最新の価格サイズ177pxで655pxへ復元");
        check(!updated.TryFollowVisiblePrice(G(179),0,out _),"可視価格追従: 本文の基準が違う場合は状態を変更しない");
        check(!PriceRestorePlan.TryCreate(289,178,float.NaN,158,compact,out _)&&
            !PriceRestorePlan.TryCreate(289,178,268,158,compact,out _),
            "価格復元: 不正な記録・変位は拒否");
        check(!PriceRestorePlan.TryCreate(289,178,269,158,new(15950,15930),out _),
            "価格復元: 高さの上限を超えて書き込まない");
        // The provider's painting Root starts at 400, later synchronizes to 600.
        var painting=new PreviewLayout(G(400),new(600,600,400,580),G(600),G(450));
        check(G(600).TryCollapseImage(painting.Generated,painting.SourceBefore,450,142,8,true,out var first),
            "絵画: 生成前Root400の同期例外で初回の縮小に成功");
        painting=painting with {Expanded=G(600),Collapsed=NativeBodyBounds.Fit(first,450,8)};
        check(G(600).TryCollapseImage(painting.Generated,painting.SourceBefore,450,142,8,true,out var repeat)&&repeat==first,
            "絵画: 初回縮小後に生成配置へ戻っても不変Root400で再調整");
        check(!G(600).TryCollapseImage(painting.Generated,painting.Collapsed,450,142,8,true,out _),
            "絵画回帰: 縮小先450を生成前Rootに使う旧処理では失敗する");
        var current=initial;
        foreach(var delta in new[]{111,177,21,200})
        {
            check(current.TryFollowVisiblePrice(G(178),delta,out current)&&current.Expanded.Window==478+delta&&current.Collapsed.Window==178+delta,
                "連続価格更新: 元側の価格量"+delta+"pxへ重複加算せず追従");
        }
    }
}
