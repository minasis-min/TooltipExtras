using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static class DailyRoutineChecks
{
    public sealed class Listener {public bool IsRequestedToClear{get;set;}}
    public enum ObservationKind:uint {Normal=0,HighQuality=1000000}
    public sealed class ObservedModification
    {
        public int Target{get;set;}
        public int Type{get;set;}
        public Lumina.Text.ReadOnly.ReadOnlySeString Text{get;set;}
    }
    public delegate void ObservationCallback(ObservationKind kind,uint item,ref List<ObservedModification> modifications);
    public static void Run(Action<bool,string> check)
    {
        var original=new Listener();var proxy=new Listener();var other=new Listener();var set=new HashSet<Listener>{original,other};
        using(var relay=new ListenerRelay(set,original,proxy))
        {
            check(set.SetEquals([proxy,other])&&relay.Active,"DR: 中継は対象登録だけ置き換え他者を保持");
            check(set.SequenceEqual(new[]{proxy,other}),"DR: 実際のイベント列挙順を維持");
            check(!original.IsRequestedToClear,"DR: 元の登録オブジェクトと解除状態を変更しない");
        }
        check(set.SetEquals([original,other]),"DR: TE停止時に元の登録を復元");
        using(var relay=new ListenerRelay(set,original,proxy))
        {original.IsRequestedToClear=true;check(!relay.Active,"DR: 元delegateによる解除要求を即時検出");}
        check(set.SetEquals([other]),"DR: DR側で解除済みの登録を復活させない");
        original=new();set.Add(original);
        using(var relay=new ListenerRelay(set,original,proxy)){set.Remove(proxy);}
        check(set.SetEquals([other]),"DR: 所有していない中継スロットを復元しない");
        var rejected=false;try{using var relay=new ListenerRelay(set,original,proxy);}catch(InvalidOperationException){rejected=true;}
        check(rejected&&set.SetEquals([other]),"DR: 元登録がなければ接続を拒否");
        byte[] B(string s)=>Encoding.UTF8.GetBytes(s);
        var baseline=B("元の説明");var generated=B("元の説明\n\n[NPC販売]\n金額\n\n[MARKET]\n市場");
        var extra=DailyRoutineText.Extract(13,baseline,generated);
        check(extra!=null&&Encoding.UTF8.GetString(extra)=="[NPC販売]\n金額\n\n[MARKET]\n市場","DR: 完成本文の2項目を順序のまま捕捉");
        check(DailyRoutineText.Extract(13,[],generated)!.AsSpan().SequenceEqual(generated),"DR: 空の標準本文でもDRの完成文字列を捕捉");
        check(DailyRoutineText.Extract(13,baseline,B("別の本文"))==null,"DR: prefix不一致は元表示を維持");
        check(DailyRoutineText.Extract(13,baseline,baseline)==null&&DailyRoutineText.Extract(13,baseline,B("元の説明\n\n"))==null,"DR: 空結果で古い追加情報を再利用しない");
        var colored=baseline.Concat(new byte[]{10,2,19,2,33,3,65,2,19,2,1,3}).ToArray();
        check(DailyRoutineText.Extract(13,baseline,colored)!.AsSpan().SequenceEqual(colored.AsSpan(baseline.Length+1)),"DR: 色payloadを再整形せず保持");
        var current=generated;var writes=0;
        var plan=TextTransferPlan.Create([13],[baseline],[generated],true,out _)!;
        check(plan.Apply((_,value)=>current=value!,_=>current,null,0,0,out _)&&current.AsSpan().SequenceEqual(baseline),"DR: 共有転送計画で元本文へ復元");
        current=generated;
        check(!plan.Apply((_,value)=>{writes++;current=value!;},_=>B("不一致"),null,0,0,out _)&&writes==2&&ReferenceEquals(current,generated),"DR: 読戻し失敗でDR生成済み本文へロールバック");
        var module=AssemblyBuilder.DefineDynamicAssembly(new("DailyCallbackTest"),AssemblyBuilderAccess.Run).DefineDynamicModule("main");
        Delegate Producer(string type,string name="OnItemTooltipUpdate")
        {
            var builder=module.DefineType(type,TypeAttributes.Public);
            var method=builder.DefineMethod(name,MethodAttributes.Public|MethodAttributes.Static,typeof(void),Type.EmptyTypes);method.GetILGenerator().Emit(OpCodes.Ret);
            return builder.CreateType()!.GetMethod(name)!.CreateDelegate<Action>();
        }
        var npc=Producer(DailyRoutineText.Producers[0]);var market=Producer(DailyRoutineText.Producers[1]);
        check(DailyRoutineText.Known([npc,market],out _),"DR: 対象2機能の登録だけを許可");
        check(DailyRoutineText.Known([npc],out _)&&DailyRoutineText.Known([market],out _),"DR: 元の片方だけ有効でも捕捉可能");
        check(!DailyRoutineText.Known([],out _)&&!DailyRoutineText.Known([npc,npc],out _),"DR: 空登録・重複登録を拒否");
        check(!DailyRoutineText.Known([npc,Producer("UnreviewedTooltip")],out var reason)&&reason.Contains("UnreviewedTooltip"),"DR: 未対応登録の本文を誤って捕捉しない");
        var salvage=Producer(DailyRoutineText.Producers[2],"OnItemTooltip");
        var ids=Producer(DailyRoutineText.Producers[3],"OnItemTooltip");
        var dye=Producer(DailyRoutineText.Producers[4],"OnItemTooltip");
        check(DailyRoutineText.Known([npc,market,salvage,ids,dye],out _),"DR: 確認済み全item callbackの併用を許可");
        check(DailyRoutineText.Fields([npc,market,salvage,ids,dye]).SequenceEqual(new[]{2,13,35}),"DR: 確認済みの追記先だけを選択");
        check(DailyRoutineText.Fields([dye]).Length==0,"DR: 別ウィンドウの染色プレビューは文字追記と扱わない");
        // Create a separate assembly to reuse the actual provider type name.
        var actionModule=AssemblyBuilder.DefineDynamicAssembly(new("DailyActionCallbackTest"),AssemblyBuilderAccess.Run).DefineDynamicModule("main");
        var actionType=actionModule.DefineType(DailyRoutineText.Producers[3],TypeAttributes.Public);
        actionType.DefineMethod("OnActionTooltip",MethodAttributes.Public|MethodAttributes.Static,typeof(void),Type.EmptyTypes).GetILGenerator().Emit(OpCodes.Ret);
        var actionId=actionType.CreateType()!.GetMethod("OnActionTooltip")!.CreateDelegate<Action>();
        check(DailyRoutineText.Known([actionId],out _,true)&&DailyRoutineText.Fields([actionId],true).SequenceEqual(new[]{1}),"DR: アクションIDの確認済みcallbackと欄を識別");
        check(!DailyRoutineText.Known([market],out _,true)&&!DailyRoutineText.Known([ids],out _,true),"DR: item callbackをactionへ混同しない");
        var nativeAbility=B("マテリア精製:○ 武具投影:○ 分解適正:1.00");
        var drAbility=B("マテリア精製:○\n武具投影:○\n分解適正:1.00\n現在の分解スキル:489.47");
        check(Encoding.UTF8.GetString(DailyRoutineText.Extract(35,nativeAbility,drAbility)!)=="現在の分解スキル:489.47","DR: 改行が変更された標準能力欄を分離本文へ重複させない");
        check(DailyRoutineText.Extract(35,nativeAbility,B("マテリア精製:○\n武具投影:○\n分解適正:2.00\n現在:489.47"))==null,"DR: 標準値まで変わった境界は推測しない");
        check(DailyRoutineText.Extract(13,nativeAbility,drAbility)==null,"DR: 改行差の許容を監査対象の能力欄以外へ広げない");
        var emojiBefore=B("標準😀 A");var emojiAfter=B("標準😀\nA\n追加");
        check(Encoding.UTF8.GetString(DailyRoutineText.Extract(35,emojiBefore,emojiAfter)!)=="追加","DR: UTF8グリフの途中で追記を切らない");
        check(DailyRoutineText.RestoreFlags(0x88006,[13],[Array.Empty<byte>()])==0x88004,"DR: 空説明文のMARKET分離でDescriptionビットだけ戻す");
        check(DailyRoutineText.RestoreFlags(0x88006,[13],[B("本文")])==0x88006,"DR: 標準説明文があれば表示フラグを保つ");
        check(DailyRoutineText.RestoreFlags(0x88046,[35],[Array.Empty<byte>()])==0x88046,"DR: 分解追加分を消しても修理グループを消さない");
        var fields=new[]{2,13,35};byte[]?[] bases=[B("カテゴリ"),[],nativeAbility];byte[]?[] outputs=[B("カテゴリ [ID]"),B("[MARKET]"),drAbility];
        var transfer=TextTransferPlan.Create(fields,bases,outputs,false,out _)!;
        var storage=fields.Select((field,index)=>(field,index)).ToDictionary(x=>x.field,x=>outputs[x.index]);
        var flags=0x88046;var restored=DailyRoutineText.RestoreFlags(flags,fields,bases);
        check(transfer.Apply((field,value)=>storage[field]=value,field=>storage[field],value=>flags=value,restored,flags,out _)&&flags==0x88044&&storage[13]!.Length==0&&storage[35]!.AsSpan().SequenceEqual(nativeAbility),"DR: 空本文・ID・分解を一括で元へ戻す");
        storage=fields.Select((field,index)=>(field,index)).ToDictionary(x=>x.field,x=>outputs[x.index]);flags=0x88046;
        check(!transfer.Apply((field,value)=>storage[field]=value,field=>B("読戻し失敗"),value=>flags=value,restored,flags,out _)&&flags==0x88046&&fields.All(field=>ReferenceEquals(storage[field],outputs[Array.IndexOf(fields,field)])),"DR: 一括復元失敗で全DR本文とフラグを戻す");
        check(!DailyRoutineText.ChangedVisible(35,[],B("前アイテムの分解情報"),2),"DR: 非装備の休眠能力欄に残った古い文字列を捕捉しない");
        check(!DailyRoutineText.ChangedVisible(13,[],B("非表示の説明"),64),"DR: 非表示の説明欄を捕捉しない");
        check(!DailyRoutineText.ChangedVisible(13,B("既存表示"),B("既存表示"),2),"DR: 元処理が変更していない文字列をDR由来と推測しない");
        check(DailyRoutineText.ChangedVisible(13,[],B("[MARKET]"),2),"DR: 空本文から生成した可視MARKETを識別");
        check(DailyRoutineText.ChangedVisible(1,B("分類"),B("分類 [ID]"),0,true),"DR: アクションIDにitem表示フラグを誤適用しない");
        var skillLabel="現在の分解スキル：";
        byte[] Skill(uint color,string number="580.53",string note=" ［レアアイテム獲得確率上昇　分解スキル経験値を獲得できません］")=>
            new Lumina.Text.SeStringBuilder().Append(skillLabel).PushColorType(color).Append(number).PopColorType().Append(note).ToArray();
        var reformattedBase=B("マテリア精製:○\n武具投影:○\n分解適正スキル：445.00\n");
        var realBase=B("マテリア精製:○ 武具投影:○ 分解適正スキル：445");
        var skill=Skill(32);var salvageGenerated=reformattedBase.Concat(skill).ToArray();
        check(DailyRoutineText.Extract(35,realBase,salvageGenerated)==null&&DailyRoutineText.ExtractSalvage(realBase,salvageGenerated,skillLabel)!.AsSpan().SequenceEqual(skill),"DR分解: 再構築された標準書式が違っても元のラベル・色付き数値から追加行を識別");
        check(DailyRoutineText.Extract(35,B("適正:445"),B("適正:445.00\n追加"))==null,"DR分解: 完全一致prefixでも数値の途中を追加行の境界にしない");
        check(new uint[]{0,32,45}.All(color=>DailyRoutineText.ExtractSalvage(realBase,reformattedBase.Concat(Skill(color,note:"")).ToArray(),skillLabel)!=null),"DR分解: スキル差に応じた3色と注記なしを扱う");
        check(DailyRoutineText.ExtractSalvage(realBase,salvageGenerated,"別の言語：")==null&&DailyRoutineText.ExtractSalvage(realBase,salvageGenerated,"")==null,"DR分解: 元の実際の翻訳ラベル以外で推測しない");
        check(DailyRoutineText.ExtractSalvage(realBase,reformattedBase.Concat(Skill(99)).ToArray(),skillLabel)==null,"DR分解: 未監査の色書式を拒否");
        check(DailyRoutineText.ExtractSalvage(realBase,reformattedBase.Concat(Skill(32,"580.5")).ToArray(),skillLabel)==null,"DR分解: 元処理と異なる数値書式を拒否");
        check(DailyRoutineText.ExtractSalvage(realBase,reformattedBase.Concat(Skill(32,note:" 任意の追記")).ToArray(),skillLabel)==null,"DR分解: 元処理と異なる注記構造を拒否");
        check(DailyRoutineText.ExtractSalvage(realBase,salvageGenerated.Concat(skill).ToArray(),skillLabel)==null&&DailyRoutineText.ExtractSalvage(salvageGenerated,salvageGenerated,skillLabel)==null,"DR分解: 重複ラベルと既に追加情報を含む基準を拒否");
        var salvagePlan=TextTransferPlan.Create([35],[realBase],[salvageGenerated],false,out _)!;var ability=salvageGenerated;
        check(salvagePlan.Apply((_,value)=>ability=value!,_=>ability,null,0,0,out _)&&ability.AsSpan().SequenceEqual(realBase),"DR分解: DRの標準欄を再利用せず保存した元の値と書式を正確に復元");
        check(DailyRoutineLayout.PresentationFields([2,13,35]).SequenceEqual(new[]{2,13,35}),"DR配置: 分離したカテゴリ・説明・能力欄すべての表示を更新");
        check(DailyRoutineLayout.PresentationFields([13]).SequenceEqual(new[]{13})&&DailyRoutineLayout.PresentationFields([35]).SequenceEqual(new[]{35}),"DR配置: 説明の元本文が空でなくても表示復元を省略しない");
        check(DailyRoutineLayout.PresentationFields([63]).Length==0,"DR配置: 分離していない標準欄を変更しない");
        check(DailyRoutineLayout.ResizeGroup(120,100,20)==40&&DailyRoutineLayout.ResizeGroup(180,60,14)==134,"DR配置: NPC・分解の追加行だけ高さを縮め既存の余白と修理行を保持");
        check(DailyRoutineLayout.ResizeGroup(137,129,59)==67,"DR実機余白: node高さ59が更新されなくても描画高さ129からMARKETの70pxだけ除去");
        check(DailyRoutineLayout.ResizeGroup(67,59,59)==67,"DR実機余白: 本文が同じ再配置で8pxの余白を累積して削らない");
        var badHeight=false;try{DailyRoutineLayout.ResizeGroup(10,100,20);}catch(NotSupportedException){badHeight=true;}
        check(badHeight,"DR配置: 高さの逆転を拒否して元表示へ戻す");
        check(DailyRoutineText.SameFlowContent(B("標準の長い説明です 次の単語"),B("標準の長い\n説明です\n次の単語")),"DR表示検査: native折り返し改行で標準本文を誤って失敗にしない");
        check(DailyRoutineText.SameFlowContent(B("マテリア精製:○ 武具投影:○ 分解適正:370.00"),B("マテリア精製:○\n武具投影:○\n分解適正:370.00")),"DR表示検査: 修理能力欄の折り返しを許容");
        check(!DailyRoutineText.SameFlowContent(B("標準説明"),B("標準説明\n[NPC販売]\n交換情報")),"DR表示検査: NPC情報の残留は失敗として検出");
        check(!DailyRoutineText.SameFlowContent(B("分解適正:370.00"),B("分解適正:370.00\n現在の分解スキル:489.47")),"DR表示検査: 分解情報の残留は失敗として検出");
        check(!DailyRoutineText.SameFlowContent(B("分解適正:370.00"),B("分解適正:370.01")),"DR表示検査: 値の変化を折り返し扱いしない");
        check(!DailyRoutineText.SameFlowContent(Skill(32),Skill(45)),"DR表示検査: 色payloadの変化も検出");
        check(DailyRoutineText.SameFlowContent([],B("\n\n"))&&!DailyRoutineText.SameFlowContent([],B("[NPC販売]")),"DR表示検査: 空本文は改行だけ許容し残った文字を拒否");
        check(DailyRoutineText.SameFlowContent(B("a　b 🌿\nc"),B("ab🌿c")),"DR表示比較: Unicode空白と補助文字を配列化せず同じ不変条件で比較");
        check(!DailyRoutineText.SameFlowContent(B("é"),B("e\u0301")),"DR表示比較: Unicode正規化で異なる本文を同一扱いしない");
        var oldFlags=FFXIVClientStructs.FFXIV.Component.GUI.TextFlags.Ellipsis|FFXIVClientStructs.FFXIV.Component.GUI.TextFlags.Edge;
        var safeFlags=DailyRoutineLayout.FlowFlags(oldFlags);
        check((safeFlags&FFXIVClientStructs.FFXIV.Component.GUI.TextFlags.Ellipsis)==0
            &&(safeFlags&FFXIVClientStructs.FFXIV.Component.GUI.TextFlags.WordWrap)!=0
            &&(safeFlags&FFXIVClientStructs.FFXIV.Component.GUI.TextFlags.MultiLine)!=0
            &&(safeFlags&FFXIVClientStructs.FFXIV.Component.GUI.TextFlags.Edge)!=0,"DR整形: 省略を無効にし折り返しを有効にして本文と装飾を保持");
        var pinnedBytes=B("整形元の固定入力\0");var pinned=new DailyRoutineLayout.PinnedInput(pinnedBytes);var pinnedAddress=pinned.Address;
        GC.Collect(2,GCCollectionMode.Forced,true,true);
        unsafe{check(pinned.Address==pinnedAddress&&new ReadOnlySpan<byte>((void*)pinned.Address,pinnedBytes.Length).SequenceEqual(pinnedBytes),"DR整形: GC後も入力バッファの位置と内容を保持（ゲーム未使用）");}
        pinned.Dispose();pinned.Dispose();var disposedInput=false;try{_=pinned.Address;}catch(ObjectDisposedException){disposedInput=true;}
        check(disposedInput,"DR整形: ピン解放は冪等で解放後ポインターを利用しない");
        var rejectedInput=false;try{using var invalid=new DailyRoutineLayout.PinnedInput(B("非終端"));}catch(ArgumentException){rejectedInput=true;}
        check(rejectedInput,"DR整形: 非終端の入力をnativeへ渡す前に拒否");
        var marketNative=B("[MARKET]\n現在のワールド:160\n最安値:60");var marketArray=marketNative;var marketView=marketNative;
        var marketFlags=66;
        var marketTransfer=TextTransferPlan.Create([13],[Array.Empty<byte>()],[marketNative],false,out _)!;
        check(marketTransfer.Apply((_,value)=>marketArray=value!,_=>marketArray,value=>marketFlags=value,64,66,out _,out _,restore=>marketView=restore?Array.Empty<byte>():marketNative),"DR MARKET: 共通転送計画で空の元説明と表示フラグ・ノードを復元");
        var abilitySource=B("標準能力\n現在の分解スキル:753.08");var abilityArray=abilitySource;var abilityView=abilitySource;
        var abilityTransfer=TextTransferPlan.Create([35],[B("標準能力")],[abilitySource],false,out _)!;
        var flagsAtAbility=marketFlags;
        check(!abilityTransfer.Apply((_,value)=>abilityArray=value!,_=>abilityArray,value=>marketFlags=value,flagsAtAbility,flagsAtAbility,out _,out var abilityReverted,restore=>{
            abilityView=restore?B("整形で変わった値"):abilitySource;if(restore)throw new InvalidOperationException("分解欄失敗");})&&abilityReverted,"DR MARKET: 分解欄は元本文・表示の読戻し確認付きで復元して失敗を局所化");
        check(marketArray.Length==0&&marketView.Length==0&&marketFlags==64&&ReferenceEquals(abilityArray,abilitySource)&&ReferenceEquals(abilityView,abilitySource),"DR MARKET: 分解欄の失敗後もMARKETの分離と説明欄非表示を維持");
        var restoredDescription=B("ミニオン修得\n標準の説明\n［修得/登録済み］");var minionMarket=restoredDescription.Concat(B("\n\n[MARKET]\n市場データ")).ToArray();
        var minionBody=minionMarket;
        var minionTransfer=TextTransferPlan.Create([13],[restoredDescription],[minionMarket],true,out _)!;
        check(minionTransfer.Apply((_,value)=>minionBody=value!,_=>minionBody,null,0,0,out _,out _)
            &&minionBody.AsSpan().SequenceEqual(restoredDescription)
            &&Encoding.UTF8.GetString(DailyRoutineText.Extract(13,restoredDescription,minionMarket)!).StartsWith("[MARKET]"),"DR MARKET: 画像のミニオンに相当する標準説明・登録案内を残して市場の追加分だけを取り出す");
        abilityArray=abilitySource;
        check(!abilityTransfer.Apply((_,value)=>abilityArray=value!,_=>B("誤った読戻し"),null,0,0,out _,out var unsafeRollback)&&!unsafeRollback,"DR MARKET: 元表示の復元読戻しを確認できない失敗を区別して後続書込みを停止");
        var request=new RequestedTransfer();var requestA=new RequestedTransferKey(1,100,200,300);var requestB=new RequestedTransferKey(2,100,200,300);
        var requestWrites=0;var displayed=B("前のアイテムの説明");
        var nextBase=B("次のアイテムの説明");var nextGenerated=B("次のアイテムの説明\n\n[MARKET]\n価格");
        request.Stage(requestB,()=>{if(DailyRoutineText.PresentationMatches(nextBase,nextGenerated,displayed)){displayed=nextBase;requestWrites++;}});
        check(requestWrites==0&&request.Pending,"DR切替: 元の表示更新前には前アイテムのノードへ一切書き込まない");
        check(request.Complete(requestB)&&requestWrites==0&&!request.Pending,"DR切替: 更新後も前の説明文なら所有確認で書戻しを拒否");
        request.Stage(requestB,()=>{if(DailyRoutineText.PresentationMatches(nextBase,nextGenerated,displayed)){displayed=nextBase;requestWrites++;}});
        displayed=nextGenerated;
        check(request.Complete(requestB)&&requestWrites==1&&ReferenceEquals(displayed,nextBase),"DR切替: nativeの今回の生成完了後だけ本文を復元する");
        check(!request.Complete(requestB)&&requestWrites==1,"DR切替: PostRequestedUpdateの重複で同じ転送を再実行しない");
        request.Stage(requestA,()=>requestWrites++);
        check(!request.Complete(requestB)&&requestWrites==1&&!request.Pending,"DR切替: 別アイテムで古い転送を破棄");
        request.Stage(requestA,()=>requestWrites++);
        check(!request.Complete(requestA with {Addon=101})&&requestWrites==1,"DR切替: Addon再生成で古い転送を破棄");
        request.Stage(requestA,()=>requestWrites++);
        check(!request.Complete(requestA with {Strings=201})&&requestWrites==1,"DR切替: 文字列配列の差替えを拒否");
        request.Stage(requestA,()=>requestWrites++);
        check(!request.Complete(requestA with {Numbers=301})&&requestWrites==1,"DR切替: 数値配列の差替えを拒否");
        request.Stage(requestA,()=>requestWrites+=10);request.Stage(requestB,()=>requestWrites++);
        check(!request.Complete(requestA)&&requestWrites==1&&!request.Pending,"DR切替: 新しい更新を古い完了通知で確定しない");
        request.Stage(requestA,()=>requestWrites++);request.Clear();
        check(!request.Complete(requestA)&&requestWrites==1,"DR切替: hideやDisposeのClearで保留転送を破棄");
        request.Stage(requestA,()=>throw new InvalidOperationException("native failure"));
        try{request.Complete(requestA);}catch(InvalidOperationException){}
        check(!request.Pending&&!request.Complete(requestA),"DR切替: 実行中の失敗でも保留処理を再実行しない");
        check(DailyRoutineText.PresentationMatches(B("新しい 説明"),B("新しい 説明\n[MARKET]"),B("新しい\n説明"))
            &&!DailyRoutineText.PresentationMatches(B("新しい説明"),B("新しい説明\n[MARKET]"),B("前の説明")),"DR切替: 今回の折り返しは許容し前アイテムの内容は拒否");
        check(DailyRoutineText.EmptyShopCandidate(true,true,true,true,true,0x82086),"DR空行: 実測の空ShopInfoと表示フラグを検出");
        check(!DailyRoutineText.EmptyShopCandidate(false,true,true,true,true,0x82086),"DR空行: 確認済みNPCコールバック以外を変更しない");
        check(!DailyRoutineText.EmptyShopCandidate(true,false,true,true,true,0x82086)
            &&!DailyRoutineText.EmptyShopCandidate(true,true,false,true,true,0x82086),"DR空行: 配列または可視ノードに価格がある欄を隠さない");
        check(!DailyRoutineText.EmptyShopCandidate(true,true,true,true,false,0x2086),"DR空行: 既に非表示の空欄へ繰り返し変更しない");
        var shopFlags=0x82086;var shopVisible=true;var priceY=183;var shopTextWrites=0;
        var emptyShopPlan=TextTransferPlan.FlagsOnly();
        check(emptyShopPlan.Apply((_,_)=>shopTextWrites++,_=>throw new InvalidOperationException("no text read"),v=>shopFlags=v,0x2086,0x82086,out _,out var shopRollback,
            restore=>{shopVisible=!restore;priceY=restore?167:183;})
            &&shopFlags==0x2086&&!shopVisible&&priceY==167&&shopTextWrites==0&&!shopRollback,"DR空行: 本文・説明フラグを変えず16pxの空Shop欄だけを除去");
        shopFlags=0x82086;shopVisible=true;priceY=183;
        check(!emptyShopPlan.Apply((_,_)=>shopTextWrites++,_=>throw new InvalidOperationException("no text read"),v=>shopFlags=v,0x2086,0x82086,out _,out shopRollback,
            restore=>{shopVisible=!restore;priceY=restore?167:183;if(restore)throw new InvalidOperationException("layout failure");})
            &&shopRollback&&shopFlags==0x82086&&shopVisible&&priceY==183&&shopTextWrites==0,"DR空行: 配置失敗時は表示フラグ・可視状態・位置を元へ戻す");
        const string nonGearMacro="<string(lstr1)><br>Suggested: <num(lnum2)>.00 [<sheet(ClassJob,lnum3,0)>]<br>Current: <string(lstr4)>";
        var pattern=DailyRoutineText.NonGearPattern.Parse(nonGearMacro)!;
        var cleanFurniture=B("This table has a green glass top.");
        var skillValue=new Lumina.Text.SeStringBuilder().PushColorType(32).Append("640.69").PopColorType().ToArray();
        var nonGearSource=cleanFurniture.Concat(B("\nSuggested: 560.00 [Carpenter]\nCurrent: ")).Concat(skillValue).ToArray();
        var nonGearMarket=nonGearSource.Concat(B("\n\n[MARKET]\ncompleted provider prices")).ToArray();
        check(DailyRoutineText.SplitNonGear(nonGearMarket,pattern,out var clean,out var allExtras)&&clean.AsSpan().SequenceEqual(cleanFurniture)
            &&Encoding.UTF8.GetString(allExtras).StartsWith("Suggested:")&&Encoding.UTF8.GetString(allExtras).Contains("[MARKET]"),"DR家具: 元キャッシュに追加情報が混ざっても元formatterの標準説明と全追記を分離");
        check(DailyRoutineText.SplitNonGear(nonGearSource,pattern,out clean,out allExtras)&&clean.AsSpan().SequenceEqual(cleanFurniture),"DR家具: MARKETが無い場合も分解適正と現在スキルを分離");
        check(DailyRoutineText.NonGearPattern.Parse(nonGearMacro.Replace("<string(lstr1)>","changed"))==null,"DR家具: 元マクロの標準説明プレースホルダー変更を拒否");
        check(!DailyRoutineText.SplitNonGear(nonGearMarket.Concat(B("\nSuggested: duplicate")).ToArray(),pattern,out _,out _),"DR家具: 境界が重複した本文を推測で切らない");
        check(!DailyRoutineText.SplitNonGear(nonGearMarket.Select(b=>b).ToArray(),pattern with {Current="Other label: "},out _,out _),"DR家具: 元テンプレート以外のラベルを拒否");
        var semanticBreak=Dalamud.Game.Text.SeStringHandling.Payloads.NewLinePayload.Payload.Encode();
        var nonGearEncoded=cleanFurniture.Concat(semanticBreak).Concat(B("Suggested: 560.00 [Carpenter]")).Concat(semanticBreak).Concat(B("Current: ")).Concat(skillValue).ToArray();
        check(DailyRoutineText.SplitNonGear(nonGearEncoded,pattern,out clean,out _)&&clean.AsSpan().SequenceEqual(cleanFurniture),"DR家具: native改行payloadでも境界の1個だけを除き標準本文を保存");
        var observationCalls=0;var observationThrows=false;
        ObservationCallback formatter=(ObservationKind _,uint _,ref List<ObservedModification> modifications)=>{
            observationCalls++;if(observationThrows)throw new InvalidOperationException("source formatter failure");
            modifications.Add(new(){Target=13,Type=0,Text=new Lumina.Text.ReadOnly.ReadOnlySeString(nonGearSource)});
        };
        var immutable=System.Collections.Immutable.ImmutableList.Create<Delegate>(formatter);
        var registrationMap=new System.Collections.Concurrent.ConcurrentDictionary<Type,System.Collections.Immutable.ImmutableList<Delegate>>();
        registrationMap[typeof(ObservationCallback)]=immutable;
        var observation=new DailyRoutineObservation(registrationMap,typeof(ObservationCallback),formatter);
        var mods=new List<ObservedModification>();
        using(observation.Begin(1000007))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.HighQuality,7,ref mods);
        check(observationCalls==1&&mods.Count==1&&observation.Description!.AsSpan().SequenceEqual(nonGearSource)
            &&ReferenceEquals(registrationMap[typeof(ObservationCallback)],immutable),"DR観測: 実際のby-ref元formatterを1回だけ呼び完成リストを変更せず登録を復元");
        mods=[];
        using(observation.Begin(8))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(observationCalls==2&&observation.Description==null&&mods.Count==1,"DR観測: 別アイテムのformatter結果を採用しない");
        observationThrows=true;var originalError=false;
        try{using(observation.Begin(7))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);}
        catch(InvalidOperationException ex){originalError=ex.Message=="source formatter failure";}
        check(originalError&&observationCalls==3&&ReferenceEquals(registrationMap[typeof(ObservationCallback)],immutable),"DR観測: 元の例外をそのまま通しfinallyで登録を戻し再実行しない");
        observationThrows=false;
        var foreign=immutable.Add(formatter);
        using(observation.Begin(7))registrationMap[typeof(ObservationCallback)]=foreign;
        check(ReferenceEquals(registrationMap[typeof(ObservationCallback)],foreign),"DR観測: 他者が差し替えた登録を古いリストで上書きしない");
        registrationMap[typeof(ObservationCallback)]=immutable;
        Delegate? retainedProxy=null;
        ObservationCallback foreignCallback=(ObservationKind _,uint _,ref List<ObservedModification> _)=>{};
        using(observation.Begin(7))
        {
            var live=registrationMap[typeof(ObservationCallback)];retainedProxy=live[0];
            registrationMap[typeof(ObservationCallback)]=live.Add(foreignCallback);
        }
        var cleaned=registrationMap[typeof(ObservationCallback)];
        check(cleaned.SequenceEqual(new Delegate[]{formatter,foreignCallback}),"DR登録修正: 他者の追加を保持して自分の中継だけ元delegateへ戻す");
        var beforeRetainedCall=observationCalls;mods=[];
        ((ObservationCallback)retainedProxy!)(ObservationKind.Normal,7,ref mods);
        check(observationCalls==beforeRetainedCall&&mods.Count==0,"DR登録修正: 取得済みの古い中継snapshotは解除後に元処理を呼ばない");
        registrationMap[typeof(ObservationCallback)]=cleaned.Remove(formatter);
        foreach(ObservationCallback callback in registrationMap[typeof(ObservationCallback)])callback(ObservationKind.Normal,7,ref mods);
        check(observationCalls==beforeRetainedCall,"DR登録修正: スコープ後の元機能解除が元delegateで正常に行える");
        registrationMap[typeof(ObservationCallback)]=immutable;
        using(observation.Begin(7))registrationMap[typeof(ObservationCallback)]=System.Collections.Immutable.ImmutableList.Create<Delegate>(foreignCallback);
        check(registrationMap[typeof(ObservationCallback)].SequenceEqual(new Delegate[]{foreignCallback}),"DR登録修正: 他者が除去した中継を復活させない");
        registrationMap[typeof(ObservationCallback)]=immutable;
        using(observation.Begin(7))registrationMap[typeof(ObservationCallback)]=registrationMap[typeof(ObservationCallback)].Add(formatter);
        check(registrationMap[typeof(ObservationCallback)].SequenceEqual(new Delegate[]{formatter}),"DR登録修正: 元delegateが再登録済みなら二重登録を作らない");

        var callbackModule=AssemblyBuilder.DefineDynamicAssembly(new("DailyRegistrationRefreshTest"),AssemblyBuilderAccess.Run).DefineDynamicModule("main");
        Func<ObservationCallback> MakeRegisteredSource(string name)
        {
            var type=callbackModule.DefineType(name,TypeAttributes.Public);type.DefineDefaultConstructor(MethodAttributes.Public);
            var method=type.DefineMethod("OnItemTooltipUpdate",MethodAttributes.Public,typeof(void),new[]{typeof(ObservationKind),typeof(uint),typeof(List<ObservedModification>).MakeByRefType()});
            method.GetILGenerator().Emit(OpCodes.Ret);var built=type.CreateType()!;
            return ()=>built.GetMethod("OnItemTooltipUpdate")!.CreateDelegate<ObservationCallback>(Activator.CreateInstance(built));
        }
        var newNpc=MakeRegisteredSource(DailyRoutineText.Producers[0]);var newMarket=MakeRegisteredSource(DailyRoutineText.Producers[1]);
        var registeredNpc=newNpc();var registeredMarket=newMarket();
        var changingMap=new System.Collections.Concurrent.ConcurrentDictionary<Type,System.Collections.Immutable.ImmutableList<Delegate>>();
        changingMap[typeof(ObservationCallback)]=System.Collections.Immutable.ImmutableList.Create<Delegate>(registeredNpc);
        var observerBuilds=0;
        var metadata=new DailyRoutineCallbacks(changingMap,typeof(ObservationCallback),registeredNpc.Method.DeclaringType!.Assembly,false,
            d=>{observerBuilds++;return new DailyRoutineObservation(changingMap,typeof(ObservationCallback),d);});
        check(metadata.TrySnapshot(out var initialSnapshot,out _)&&initialSnapshot!.Observers.Length==1&&initialSnapshot.Shop,"DR接続更新: 初期登録から分離対象とobserverを作る");
        metadata.TrySnapshot(out var sameSnapshot,out _);
        check(ReferenceEquals(initialSnapshot,sameSnapshot)&&observerBuilds==1,"DR接続更新: 登録が同じ通常更新では再走査やobserver生成をしない");
        changingMap[typeof(ObservationCallback)]=changingMap[typeof(ObservationCallback)].Add(registeredMarket);
        check(metadata.TrySnapshot(out var enabledSnapshot,out _)&&enabledSnapshot!.Observers.Length==2&&enabledSnapshot.Fields.Contains(13),"DR接続更新: 後から有効にした市場機能も既存listenerのまま捕捉対象へ追加");
        changingMap[typeof(ObservationCallback)]=changingMap[typeof(ObservationCallback)].Remove(registeredMarket);
        metadata.TrySnapshot(out var offSnapshot,out _);var replacementMarket=newMarket();
        changingMap[typeof(ObservationCallback)]=changingMap[typeof(ObservationCallback)].Add(replacementMarket);
        metadata.TrySnapshot(out var reenabledSnapshot,out _);
        var boundNewSource=false;
        foreach(var observer in reenabledSnapshot!.Observers)using(var scope=observer.Begin(7))
            boundNewSource|=scope!=null&&changingMap[typeof(ObservationCallback)].Contains(registeredNpc)&&!changingMap[typeof(ObservationCallback)].Contains(replacementMarket);
        check(offSnapshot!.Observers.Length==1&&reenabledSnapshot.Observers.Length==2&&boundNewSource,"DR接続更新: OFF/ONで作られた別instanceのdelegateへobserverを張り替える");
        changingMap[typeof(ObservationCallback)]=changingMap[typeof(ObservationCallback)].Add(replacementMarket);
        check(!metadata.TrySnapshot(out _,out _),"DR接続更新: 登録変更でも重複・未対応登録の検査を省略しない");
        var sourceCalls=0;var appendEnabled=true;
        ObservationCallback appendFormatter=(ObservationKind kind,uint id,ref List<ObservedModification> modifications)=>
        {
            sourceCalls++;
            modifications.Add(new(){Target=63,Type=0,Text=new Lumina.Text.ReadOnly.ReadOnlySeString(Array.Empty<byte>())});
            if(appendEnabled)modifications.Add(new(){Target=13,Type=2,Text=new Lumina.Text.ReadOnly.ReadOnlySeString(B("[MARKET]\n完成情報"))});
        };
        var appendOriginal=System.Collections.Immutable.ImmutableList.Create<Delegate>(appendFormatter);
        registrationMap[typeof(ObservationCallback)]=appendOriginal;
        var appendObserver=new DailyRoutineObservation(registrationMap,typeof(ObservationCallback),appendFormatter,
            (operation,text)=>operation==2?CapturedText.TrimPresentationEdges(text):null);
        // Reproduce the audited manager's cache behavior: an append causes its
        // cached prefix to replace the request input; no description operation
        // leaves that input intact. Exercise rapid A/B/A, not just one prefix.
        var preserved=true;var keptShop=true;
        foreach(var fresh in new[]{B("本文A"),B("本文B"),B("本文A"),B("本文C")})
        {
            mods=[];
            using(appendObserver.Begin(7))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
            var descriptionMods=mods.Where(m=>m.Target==13).ToArray();
            var nativeResult=descriptionMods.Length==0?fresh:B("古い本文").Concat(descriptionMods.SelectMany(m=>m.Text.Data.ToArray())).ToArray();
            preserved&=nativeResult.AsSpan().SequenceEqual(fresh)&&appendObserver.SeparatedDescription!.AsSpan().SequenceEqual(B("[MARKET]\n完成情報"));
            keptShop&=mods.Count==1&&mods[0].Target==63;
        }
        check(preserved&&sourceCalls==4,"DR直接分離: A/B/A高速切替で古いキャッシュ本文を再適用せず今回の標準本文を保持");
        check(keptShop&&ReferenceEquals(registrationMap[typeof(ObservationCallback)],appendOriginal),"DR直接分離: NPCの空Shop操作と元登録を保持");
        appendEnabled=false;mods=[];
        using(appendObserver.Begin(7))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(appendObserver.SeparatedDescription==null&&sourceCalls==5,"DR直接分離: 元機能が追記しない更新では前回の情報を破棄");
        appendEnabled=true;mods=[];
        using(appendObserver.Begin(8))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(mods.Count==2&&appendObserver.SeparatedDescription==null,"DR直接分離: 表示対象と元生成対象が違えばリストを変更しない");
        var throwingObserver=new DailyRoutineObservation(registrationMap,typeof(ObservationCallback),appendFormatter,(_,_)=>throw new InvalidOperationException());mods=[];
        using(throwingObserver.Begin(7))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(mods.Count==2&&throwingObserver.SeparatedDescription==null,"DR直接分離: コピー検証失敗は元リストへ一切変更しない");
        ObservationCallback duplicateFormatter=(ObservationKind kind,uint id,ref List<ObservedModification> modifications)=>
        {
            modifications.Add(new(){Target=13,Type=2,Text=new Lumina.Text.ReadOnly.ReadOnlySeString(B("一つ目"))});
            modifications.Add(new(){Target=13,Type=2,Text=new Lumina.Text.ReadOnly.ReadOnlySeString(B("二つ目"))});
        };
        registrationMap[typeof(ObservationCallback)]=System.Collections.Immutable.ImmutableList.Create<Delegate>(duplicateFormatter);
        var duplicateObserver=new DailyRoutineObservation(registrationMap,typeof(ObservationCallback),duplicateFormatter,(_,text)=>text);mods=[];
        using(duplicateObserver.Begin(7))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(mods.Count==2&&duplicateObserver.SeparatedDescription==null,"DR直接分離: 未対応の複数説明操作を部分的に除去しない");
        registrationMap[typeof(ObservationCallback)]=immutable;
        var sourceNative=B("今回の標準本文");
        var salvageObserver=new DailyRoutineObservation(registrationMap,typeof(ObservationCallback),formatter,
            (operation,text)=>operation==0&&DailyRoutineText.SplitNonGear(text,pattern,out var native,out var addition)&&DailyRoutineText.SameFlowContent(native,sourceNative)?addition:null);
        mods=[];
        using(salvageObserver.Begin(7))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(mods.Count==1&&salvageObserver.SeparatedDescription==null&&salvageObserver.Description!=null,"DR直接分離: 分解の元本文が今回の入力と違う場合は既存の検証経路を保持");
        DailyRoutineText.SplitNonGear(nonGearSource,pattern,out sourceNative,out _);mods=[];
        using(salvageObserver.Begin(7))((ObservationCallback)((System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)[0])(ObservationKind.Normal,7,ref mods);
        check(mods.Count==0&&salvageObserver.SeparatedDescription is {Length:>0}&&salvageObserver.Description==null,"DR直接分離: 今回の元本文が一致する家具は分解の追加情報だけを受け取りネイティブを書き戻さない");
        registrationMap[typeof(ObservationCallback)]=System.Collections.Immutable.ImmutableList.Create<Delegate>(appendFormatter,formatter);
        var bothOriginal=registrationMap[typeof(ObservationCallback)];mods=[];
        using(appendObserver.Begin(7))using(salvageObserver.Begin(7))
            foreach(ObservationCallback callback in (System.Collections.Immutable.ImmutableList<Delegate>)registrationMap[typeof(ObservationCallback)]!)callback(ObservationKind.Normal,7,ref mods);
        check(mods.Count==1&&mods[0].Target==63&&ReferenceEquals(registrationMap[typeof(ObservationCallback)],bothOriginal)
            &&appendObserver.SeparatedDescription!=null&&salvageObserver.SeparatedDescription!=null,"DR直接分離: 市場と分解の入れ子スコープを逆順で解除し元登録を完全復元");
        var description=B("標準説明\n");var npcText=description.Concat(B("[NPC販売]\n交換情報")).ToArray();
        var npcPlan=TextTransferPlan.Create([13],[description],[npcText],true,out _)!;
        var npcArray=npcText;var npcNode=npcText;ushort groupSize=114;var groupShown=true;
        void NpcLayout(bool restore)
        {
            foreach(var field in DailyRoutineLayout.PresentationFields([13]))
                if(field==13){npcNode=restore?description:npcText;groupSize=restore?DailyRoutineLayout.ResizeGroup(114,100,20):(ushort)114;groupShown=true;}
        }
        check(npcPlan.Apply((_,value)=>npcArray=value!,_=>npcArray,null,0,0,out _,NpcLayout)
            &&npcNode.AsSpan().SequenceEqual(description)&&groupSize==34&&groupShown,"DR配置: 元説明が非空でもNPC販売の生成済み文字と追加高さを取り除く");
        npcArray=npcText;npcNode=npcText;groupSize=114;
        check(!npcPlan.Apply((_,value)=>npcArray=value!,_=>B("不一致"),null,0,0,out _,NpcLayout)
            &&ReferenceEquals(npcNode,npcText)&&groupSize==114&&groupShown,"DR配置: 非空説明の復元失敗でNPC本文と高さも元へ戻す");
        var marketBody=B("[MARKET]\n価格");var empty=Array.Empty<byte>();
        var layoutPlan=TextTransferPlan.Create([13],[empty],[marketBody],false,out _)!;
        var node=marketBody;var visible=true;var positionedVisible=true;current=marketBody;flags=66;var layoutCalls=0;
        void Layout(bool restore)
        {
            check((flags&2)==(restore?0:2)&&ReferenceEquals(current,restore?empty:marketBody),"DR配置: 文字列とフラグを確定してから表示ノードを更新");
            node=restore?empty:marketBody;visible=!restore;positionedVisible=visible;layoutCalls++;
        }
        check(layoutPlan.Apply((_,value)=>current=value!,_=>current,value=>flags=value,64,66,out _,Layout)
            &&node.Length==0&&!visible&&!positionedVisible&&flags==64&&layoutCalls==1,"DR配置: 配列だけでなく生成済みMARKET表示と占有領域を除去");
        current=marketBody;node=marketBody;flags=66;visible=true;layoutCalls=0;
        check(!layoutPlan.Apply((_,value)=>current=value!,_=>B("失敗"),value=>flags=value,64,66,out _,Layout)
            &&ReferenceEquals(node,marketBody)&&visible&&positionedVisible&&flags==66&&layoutCalls==2,"DR配置: 読戻し失敗時は本文・表示フラグ・配置をまとめてDR表示へ戻す");
        current=marketBody;flags=66;layoutCalls=0;
        check(!layoutPlan.Apply((_,value)=>current=value!,_=>current,value=>flags=value,64,66,out _,restore=>{Layout(restore);if(restore)throw new InvalidOperationException("配置失敗");})
            &&ReferenceEquals(node,marketBody)&&visible&&flags==66&&layoutCalls==2,"DR配置: 配置更新が失敗してもDRの取得処理を再実行せず元表示へ戻す");
    }
}
