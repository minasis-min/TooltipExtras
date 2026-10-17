using FFXIVClientStructs.FFXIV.Component.GUI;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static unsafe class BodyBoundsChecks
{
    public static void Run(Action<bool,string> check)
    {
        // Default structs and raw fields only: never call native visibility/layout functions here.
        AtkResNode root=default,group=default,footer=default;
        root.NodeId=1;root.NodeFlags=NodeFlags.Visible;root.ScaleY=1;
        group.NodeId=10;group.ParentNode=&root;group.Y=100;group.ScaleY=1;group.NodeFlags=NodeFlags.Visible;
        footer.NodeId=2;footer.ParentNode=&root;footer.Y=500;footer.ScaleY=1;footer.NodeFlags=NodeFlags.Visible;
        AtkTextNode body=default,food=default,hint=default,price=default,foreign=default;
        void Init(AtkTextNode* p,uint id,float y,ushort h)
        {p->AtkResNode.NodeId=id;p->AtkResNode.Type=NodeType.Text;p->AtkResNode.Y=y;p->AtkResNode.Height=h;p->AtkResNode.ScaleY=1;p->AtkResNode.NodeFlags=NodeFlags.Visible;p->NodeText.BufUsed=5;}
        Init(&body,13,50,20);body.AtkResNode.ParentNode=&group;
        Init(&food,16,300,60);food.AtkResNode.ParentNode=&root;
        Init(&hint,3,10,20);hint.AtkResNode.ParentNode=&footer;
        Init(&price,32612,700,100);price.AtkResNode.ParentNode=&root;
        Init(&foreign,0x5354000B,800,100);foreign.AtkResNode.ParentNode=&root;
        AtkResNode** nodes=stackalloc AtkResNode*[5]{&body.AtkResNode,&food.AtkResNode,&hint.AtkResNode,&price.AtkResNode,&foreign.AtkResNode};
        check(NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out var minimum)&&minimum==368,"本文範囲: 現在の食事・薬品の本文末尾を親座標込みで保護");
        food.AtkResNode.ParentNode=null;
        check(!NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out _),"不正親: 正常本文と可視の親切れ本文が混在しても縮小を許可しない");
        food.AtkResNode.ParentNode=&root;
        check(NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out minimum)&&minimum==368,"不正親: 同じ本文の親が正常なら本来の368pxを測定");
        food.AtkResNode.NodeFlags=0;
        check(NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out minimum)&&minimum==178,"本文範囲: 非表示の旧食品欄とヒント・外部追記を高さに含めない");
        group.NodeFlags=0;food.AtkResNode.NodeFlags=NodeFlags.Visible;
        check(NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out minimum)&&minimum==368,"本文範囲: 非表示の親に残った旧装備欄を除外");
        group.NodeFlags=NodeFlags.Visible;food.AtkResNode.NodeFlags=0;body.NodeText.BufUsed=1;
        check(!NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out _),"本文範囲: 空文字しか測定できない状態で縮小を許可しない");
        body.NodeText.BufUsed=5;group.Rotation=0.1f;
        check(!NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out _),"本文範囲: 未対応の回転座標を推測しない");
        group.Rotation=0;group.ParentNode=&group;
        check(!NativeBodyBounds.TryMinimum(nodes,5,&root,&footer,out _),"本文範囲: 親の循環を上限付きで拒否");
        group.ParentNode=&root;
        check(!NativeBodyBounds.TryMinimum(null,5,&root,&footer,out _)&&!NativeBodyBounds.TryMinimum(nodes,1025,&root,&footer,out _),"本文範囲: 不正リスト・走査数を拒否");
        check(NativeBodyBounds.Fit(new(152,152,152,132),194)==new PreviewGeometry(194,194,194,174),"染料切替再現: 152pxへ縮めず本文の194pxを確保");
        check(NativeBodyBounds.Fit(new(194,194,194,174),178,16)==new PreviewGeometry(178,178,178,158),"下端余白の診断再現: 本文178pxに対するヘアプレビュー専用16pxを除去");
        check(NativeBodyBounds.Fit(new(194,194,194,174),188,8)==new PreviewGeometry(188,188,188,168),"絵画下端: 専用8pxを除去しても本文の必要高さを確保");
        check(NativeBodyBounds.Fit(new(194,194,194,174),210,16)==new PreviewGeometry(210,210,210,190),"下端余白: 本文が長い場合は余白除去より本文保護を優先");
        var generated=new PreviewGeometry(478,478,478,458);
        check(new PreviewGeometry(400,400,400,458).TryCollapseImage(generated,new(152,152,152,132),178,284,16,false,out var final)&&final==new PreviewGeometry(194,194,194,174),
            "種類切替: 同期された窓にも残る直前の400pxを採用しない");
        check(NativeBodyBounds.Fit(new(194,194,194,174),220)==new PreviewGeometry(220,220,220,200),"本文範囲: 今回の本文が画像由来の高さを超える場合は本文を優先");
        foreach(var previous in new ushort[]{152,204,400,600,900})
        {
            var stale=new PreviewGeometry(previous,previous,previous,previous-20);
            check(generated.TryCollapseImage(generated,stale,178,284,16,false,out var compact)&&compact==new PreviewGeometry(194,194,194,174),
                "種類切替: 直前の高さ"+previous+"pxを現在のプレビューへ持ち込まない");
        }
    }
}
