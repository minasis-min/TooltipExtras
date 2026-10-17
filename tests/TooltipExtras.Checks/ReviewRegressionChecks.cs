using Dalamud.Game.Addon.Lifecycle;
using FFXIVClientStructs.FFXIV.Component.GUI;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static unsafe class ReviewRegressionChecks
{
    public static void Run(Action<bool,string> check)
    {
        var first=0;var second=0;var fail=true;var errors=0;
        var retry=new RetryBatch(("first",()=>{first++;if(fail)throw new IOException("simulated");}),
            ("second",()=>second++));
        retry.Request();retry.Tick(0,(_,_)=>errors++);
        check(first==1&&second==1&&errors==1&&retry.Pending,"再接続: 先頭の復元例外でも後続を実行し失敗項目を保持");
        retry.Tick(999,(_,_)=>errors++);
        check(first==1&&second==1,"再接続: 毎フレーム再試行せず1秒間隔");
        fail=false;retry.Tick(1000,(_,_)=>errors++);
        check(first==2&&second==1&&!retry.Pending,"再接続: 失敗項目のみ再試行し成功済みフックを二重登録しない");
        retry.Request();retry.Tick(1001,(_,_)=>errors++);
        check(first==3&&second==2&&!retry.Pending,"再接続: 新しいプラグイン変更は全項目を再検証");
        var later=0;
        var reportFailure=new RetryBatch(("failing",()=>throw new IOException("simulated")),("later",()=>later++));
        reportFailure.Request();reportFailure.Tick(0,(_,_)=>throw new IOException("logger simulated"));
        check(later==1&&reportFailure.Pending,"再接続: エラー報告の例外も後続処理を妨げない");
        SimpleGeometry before=new(300,300,280),after=new(317,317,297);
        check(SimpleRestorePlan.TryCreate(before,after,before,17,290,null,out var plan)&&plan.Layout==after&&plan.PriceY==null,
            "Simple復元: 単独の隠した17pxを正確に戻す");
        check(SimpleRestorePlan.TryCreate(before,after,new(411,411,391),17,290,new(411,107,292,391),out plan)&&
            plan.Layout==new SimpleGeometry(428,428,408)&&plan.PriceY==309,
            "Simple復元: 可視価格111pxを保ち価格ノードを17px下へ移す");
        check(!SimpleRestorePlan.TryCreate(before,after,new(412,412,391),17,290,new(411,107,292,391),out _),
            "Simple復元: 価格と窓の高さ不一致は変更しない");
        check(!SimpleRestorePlan.TryCreate(before,after,new(411,411,391),17,290,new(411,107,293,391),out _),
            "Simple復元: 価格の生成位置が違えば推測しない");
        check(!SimpleRestorePlan.TryCreate(before,after,new(411,410,391),17,290,new(411,107,292,391),out _),
            "Simple復元: 背景高さが違う場合は保留");
        check(!SimpleRestorePlan.TryCreate(before,after,new(400,400,380),17,290,null,out _),
            "Simple復元: 未知の追記を価格として扱わない");
        check(!SimpleRestorePlan.TryCreate(new(300,300,-1),new(317,317,16),new(300,300,-1),17,290,null,out _),
            "Simple復元: 元記録でも負の案内座標は適用しない");
        var diagnostics=new Diagnostics();
        foreach(var type in new[]{AddonEvent.PreUpdate,AddonEvent.PostUpdate,AddonEvent.PreDraw,AddonEvent.PostDraw})
            for(var frame=0;frame<10000;frame++)diagnostics.Record(type,null!,frame,0);
        check(diagnostics.Snapshot().Length==0,"診断: 40000件のアイドル更新・描画で履歴を消費しない");
        var root=new AtkResNode();var window=new AtkComponentNode();
        var footer=new AtkResNode{NodeId=2,Y=280};var hint=new AtkResNode{NodeId=3,ParentNode=&footer};
        var nodes=stackalloc AtkResNode*[2];nodes[0]=&footer;nodes[1]=&hint;
        var addon=new AtkUnitBase{RootNode=&root,WindowNode=&window};addon.UldManager.NodeList=nodes;addon.UldManager.NodeListCount=2;
        for(var frame=0;frame<10000;frame++)diagnostics.RecordLayout("Ready",frame,100,&addon,false);
        check(((Array)diagnostics.LayoutSnapshot()).Length==1,"診断: 同じ配置は10000フレームでも1件");
        footer.Y=297;diagnostics.RecordLayout("Ready",10001,100,&addon,false);
        check(((Array)diagnostics.LayoutSnapshot()).Length==2,"診断: 実際の位置変化は残す");
        diagnostics.Clear();
        check(diagnostics.Snapshot().Length==0&&((Array)diagnostics.LayoutSnapshot()).Length==0,"診断: リセットでイベント・配置履歴を両方消去");
    }
}