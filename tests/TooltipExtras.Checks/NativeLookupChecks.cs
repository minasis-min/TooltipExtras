using FFXIVClientStructs.FFXIV.Component.GUI;
using TooltipExtras;

internal static unsafe class NativeLookupChecks
{
    public static void Run(Action<bool, string> check)
    {
        AtkResNode vanilla = default, price = default, other = default;
        vanilla.NodeId = 2; price.NodeId = 32612; other.NodeId = 0x5354000B;
        AtkResNode** nodes = stackalloc AtkResNode*[4];
        nodes[0] = &vanilla; nodes[1] = null; nodes[2] = &other; nodes[3] = &price;
        check(NativeNodeLookup.FindUnique(nodes, 4, 32612, out var matches) == &price && matches == 1,
            "動的追加された価格ノードをnullエントリと他追記から区別");
        check(NativeNodeLookup.FindUnique(nodes, 3, 32612, out matches) == null && matches == 0,
            "有効なNodeListCount外を読まない");
        other.NodeId = 32612;
        check(NativeNodeLookup.FindUnique(nodes, 4, 32612, out matches) == null && matches == 2,
            "同一IDが競合した場合はどちらも採用しない");
        check(NativeNodeLookup.FindUnique(null, 4, 32612, out _) == null, "nullリストを拒否");
        check(NativeNodeLookup.FindUnique(nodes, 1025, 32612, out _) == null, "不正なノード数を走査前に拒否");
        check(NativeNodeLookup.FindUnique(nodes, 0, 32612, out _) == null, "空リストを拒否");
AtkResNode parent = default;
        price.Type = other.Type = NodeType.Text;
        other.NodeId = 0x5354000B;
        price.ParentNode = vanilla.ParentNode = other.ParentNode = &parent;
        price.NextSiblingNode = &other; other.PrevSiblingNode = &price;
        other.NextSiblingNode = &vanilla; vanilla.PrevSiblingNode = &other;
        check(NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, false),
            "実機診断再現: 非表示Simple Tweaksノードを挟む価格行");
        check(!NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, true),
            "表示中の他追記を挟む未検証配置は拒否");
        price.Y = 647; price.Height = 107; other.Y = 610; other.Height = 35;
        check(NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, true, true),
            "装備中の実機診断: 表示中の追加情報が価格欄より上にある配置を許可");
        check(!NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, true, false),
            "Simple TweaksのDLL識別不一致では表示中ノードの例外を許可しない");
        check(new PriceGeometry(766, 107, 647, 752).TryRemove(out var equippedHeight, out var equippedFooter)
            && equippedHeight == 655 && equippedFooter == 641 && other.Y == 610 && other.Height == 35,
            "装備中の価格111pxのみ除去し追加情報の座標と高さを維持");
        other.Y = 700;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, true, true),
            "価格欄より下にある表示中の追加情報は拒否");
        other.Y = float.NaN;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, true, true), "不正な追加情報座標を拒否");
        other.Y = 610;
        other.NodeId = 12345;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, false), "未知の介在ノードは拒否");
        other.NodeId = 0x5354000B; other.PrevSiblingNode = null;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, false), "片方向だけ一致するリンクは拒否");
        other.PrevSiblingNode = &price; other.ParentNode = null;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, &other, false), "別の親を持つ介在ノードは拒否");
        price.NextSiblingNode = &vanilla; vanilla.PrevSiblingNode = &price;
        check(NativeNodeLookup.HasPriceFooterTopology(&price, &vanilla, null, false), "価格と末尾が直接隣接する従来配置");
        AtkResNode image=default;image.NodeId=0x53541050;image.Type=NodeType.Image;image.ParentNode=&parent;
        price.NextSiblingNode=&other;other.ParentNode=&parent;other.PrevSiblingNode=&price;
        other.NextSiblingNode=&image;image.PrevSiblingNode=&other;image.NextSiblingNode=&vanilla;vanilla.PrevSiblingNode=&image;
        check(NativeNodeLookup.HasPriceFooterTopology(&price,&vanilla,&other,false,false,&image,false),"1.0.8診断再現: 既知の非表示プレビューが価格と末尾の間に残る配置");
        check(!NativeNodeLookup.HasPriceFooterTopology(&price,&vanilla,&other,false,false,&image,true),"表示中のプレビューを価格から勝手に除去しない");
        check(!NativeNodeLookup.HasPriceFooterTopology(&price,&vanilla,&other,false),"未同定の画像は非表示でも拒否");
        image.Y=178;image.Height=284;price.Y=470;
        check(NativeNodeLookup.HasPriceFooterTopology(&price,&vanilla,&other,false,false,&image,true),"髪型との併用: 上側の既知画像を変更せず価格末尾だけ先に分離可能");
        image.Y=200;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price,&vanilla,&other,false,false,&image,true),"画像が価格行へ重なる配置は拒否");
        image.Y=float.NaN;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price,&vanilla,&other,false,false,&image,true),"既知画像でも不正な座標は拒否");        image.PrevSiblingNode=&price;
        check(!NativeNodeLookup.HasPriceFooterTopology(&price,&vanilla,&other,false,false,&image,false),"画像の逆リンク不一致を拒否");        check(new PriceGeometry(425, 89, 324, 638).TryRemove(out var height, out var y) && height == 332 && y == 545,
            "実機診断の価格高さ89と余白4だけを差し引く");
    }
}
