using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;
using TooltipExtras;

namespace TooltipExtrasChecks;

internal static unsafe class ItemLayoutChecks
{
    public static void Run(Action<bool,string> check)
    {
        AtkUnitBase addon=default;
        AtkComponentNode window=default;
        AtkComponentBase component=default;
        AtkResNode root=default, border=default, background=default, addonRoot=default;
        AtkResNode footer=default, price=default, image=default, secondFooter=default;
        addon.WindowNode=&window;
        addon.RootNode=&addonRoot;
        window.Component=&component;
        component.UldManager.RootNode=&root;
        root.PrevSiblingNode=&border;
        background.NodeId=2;
        var nodes=stackalloc AtkResNode*[1];
        nodes[0]=&background;
        component.UldManager.NodeList=nodes;
        component.UldManager.NodeListCount=1;
        window.AtkResNode.Height=root.Height=border.Height=background.Height=addonRoot.Height=504;
        footer.Y=484;
        price.Y=416;
        price.NodeFlags=NodeFlags.Visible;
        var windowWrites=0;
        var windowAddress=(nint)(&window.AtkResNode);
        AtkResNode.Delegates.SetHeight height=(node,value)=>{
            node->Height=value;
            if((nint)node==windowAddress)windowWrites++;
        };
        AtkResNode.Delegates.SetYFloat y=(node,value)=>node->Y=value;
        AtkResNode.Delegates.IsVisible visible=node=>(node->NodeFlags&NodeFlags.Visible)!=0;
        AtkResNode.Delegates.ToggleVisibility toggle=(node,value)=>{
            if(value)node->NodeFlags|=NodeFlags.Visible;
            else node->NodeFlags&=~NodeFlags.Visible;
        };
        var addresses=new[]{AtkResNode.Addresses.SetHeight,AtkResNode.Addresses.SetYFloat,
            AtkResNode.Addresses.IsVisible,AtkResNode.Addresses.ToggleVisibility};
        var original=addresses.Select(a=>a.Value).ToArray();
        Delegate[] stubs=[height,y,visible,toggle];
        for(var i=0;i<addresses.Length;i++)addresses[i].Value=Marshal.GetFunctionPointerForDelegate(stubs[i]);
        try
        {
            var update=new ItemLayoutUpdate();
            update.PlanPrice(&addon,&price,&footer,new(424,404,false));
            var projected=update.Read(&addon,&background,&footer);
            check(projected==new PreviewGeometry(424,424,424,404)&&update.Hides(&price)
                &&window.AtkResNode.Height==504&&price.IsVisible(),
                "共通配置: 価格の計画は仮の寸法だけを更新し元ノードへ書き込まない");

            check(projected.TryCollapseImage(projected,new(180,180,180,404),178,230,16,false,out var compact),
                "共通配置: 価格込みの実寸から画像を除去する計画へ引き継ぐ");
            compact=NativeBodyBounds.Fit(compact,178,16);
            update.PlanPreview(&image,&background,&footer,compact,null,0);
            update.Commit(&addon);
            check(windowWrites==1&&window.AtkResNode.Height==178&&root.Height==178&&border.Height==178
                &&background.Height==178&&addonRoot.Height==178&&footer.Y==158&&!price.IsVisible(),
                "共通配置: 価格と画像の分離を一度の確定で反映");

            // A later provider result can keep its price visible while the image is removed.
            price.NodeFlags=NodeFlags.Visible;
            var next=new ItemLayoutUpdate();
            next.PlanPreview(&image,&background,&footer,new(258,258,258,238),&price,170);
            next.Commit(&addon);
            check(price.IsVisible()&&price.Y==170&&windowWrites==2,
                "共通配置: 残す価格は表示を維持し、最終配置で一度だけ移動");
            var inconsistent=new ItemLayoutUpdate();
            inconsistent.PlanPrice(&addon,&price,&footer,new(178,158,false));
            var rejected=false;
            try { inconsistent.PlanPreview(&image,&background,&secondFooter,compact,null,0); }
            catch(InvalidOperationException) { rejected=true; }
            check(rejected&&windowWrites==2&&price.IsVisible(),
                "共通配置: 別フッターの計画を混ぜず、確定前は元配置を保持");

            // A new item event starts with an empty plan, even if the addon reuses its nodes.
            window.AtkResNode.Height=root.Height=border.Height=background.Height=addonRoot.Height=220;
            footer.Y=200;
            var newItem=new ItemLayoutUpdate();
            newItem.Commit(&addon);
            check(window.AtkResNode.Height==220&&footer.Y==200&&price.IsVisible()&&windowWrites==2,
                "共通配置: 別アイテムのイベントには前回の計画を引き継がない");

            window.AtkResNode.Height=border.Height=background.Height=addonRoot.Height=258;
            footer.Y=238;
            root.Height=180;
            // Simulate a failed final visibility call after geometry was written.
            AtkResNode.Addresses.ToggleVisibility.Value=0;
            var failed=new ItemLayoutUpdate();
            failed.PlanPrice(&addon,&price,&footer,new(178,158,false));
            var rolledBack=false;
            try { failed.Commit(&addon); }
            catch(InvalidOperationException) { rolledBack=true; }
            catch(NullReferenceException) { rolledBack=true; }
            check(rolledBack&&window.AtkResNode.Height==258&&background.Height==258
                &&root.Height==180&&footer.Y==238&&price.IsVisible(),
                "共通配置: 確定失敗時は同期前の個別高さを含めて元に戻す");
        }
        finally
        {
            for(var i=0;i<addresses.Length;i++)addresses[i].Value=original[i];
            GC.KeepAlive(stubs);
        }
    }
}
