using System.Collections;
using System.Reflection;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using TooltipExtras;
namespace TooltipExtrasChecks;
internal static unsafe class ExpansionChecks
{
    public static void Run(Action<bool,string> check)
    {
        byte[] B(string s)=>Encoding.UTF8.GetBytes(s);
        var original=new object();var proxy=new object();var other=new object();IList list=new ArrayList{original,other};
        using(var scope=new ScopedRegistration()){scope.Replace(list,original,proxy);check(ReferenceEquals(list[0],proxy),"生成中継: 呼出し中だけ代理を登録");}
        check(ReferenceEquals(list[0],original)&&ReferenceEquals(list[1],other),"生成中継: 終了後は元インスタンス・他項目を保持");
        var thrown=false;try{using var scope=new ScopedRegistration();scope.Replace(list,original,proxy);throw new IOException("producer");}catch(IOException){thrown=true;}
        check(thrown&&ReferenceEquals(list[0],original),"生成中継: 例外でも元登録を必ず復元");
        using(var outer=new ScopedRegistration()){outer.Replace(list,original,proxy);using(var inner=new ScopedRegistration()){inner.Replace(list,proxy,new object());}check(ReferenceEquals(list[0],proxy),"生成中継: 内側の解除は外側を維持");}
        check(ReferenceEquals(list[0],original),"生成中継: 入れ子の解除後も元へ復元");
        using(var scope=new ScopedRegistration()){scope.Replace(list,original,proxy);list.Remove(proxy);}
        check(!list.Contains(original)&&list.Count==1,"生成中継: 外部で削除された登録を復活させない");
                var duplicate=false;list.Insert(0,original);
        try{using var scope=new ScopedRegistration();scope.Replace(list,original,other);}catch(InvalidOperationException){duplicate=true;}
        check(duplicate&&ReferenceEquals(list[0],original),"生成中継: 別登録と同じ代理参照は拒否");
        var type=SimpleDispatchProxy.Create(typeof(MockSimpleBase));var active=true;var calls=0;
        var instance=(MockSimpleBase)Activator.CreateInstance(type,[new Func<bool>(()=>active),new Action<nint,nint>((n,s)=>{if(n==11&&s==22)calls++;})])!;
        check(instance.Enabled,"生成中継: 元のEnabledを転送");active=false;check(!instance.Enabled,"生成中継: 元機能のOFFも転送");
        instance.OnGenerateItemTooltip((byte*)11,(int*)22);check(calls==1,"生成中継: ポインター署名を1回転送（参照なし）");
        byte[]?[] before=[B("HP +10"),B("MP +20")];byte[]?[] after=[B("HP +10 [15]"),B("MP +20 [25]")];int[] fields=[37,38];
        var plan=TextTransferPlan.Create(fields,before,after,false,out var reason)!;
        check(plan!=null&&reason.Length==0&&Encoding.UTF8.GetString(plan.Output(false).First())=="HP +10 [15]","文字移設: 計算せず元の完成済み行を捕捉");
        var data=new Dictionary<int,byte[]>{{37,after[0]!},{38,after[1]!}};var count=8;
        check(plan!.Apply((f,v)=>data[f]=v!,f=>data[f],v=>count=v,6,8,out _)&&count==6&&data[37].AsSpan().SequenceEqual(before[0])&&data[38].AsSpan().SequenceEqual(before[1]),"文字移設: 複数欄と行数を追記前へ復元");
        data[37]=after[0]!;data[38]=after[1]!;count=8;
        var ok=plan.Apply((f,v)=>{if(f==38&&ReferenceEquals(v,before[1]))throw new IOException("write");data[f]=v!;},f=>data[f],v=>count=v,6,8,out reason);
        check(!ok&&count==8&&data[37].AsSpan().SequenceEqual(after[0])&&data[38].AsSpan().SequenceEqual(after[1]),"文字移設: 途中の書込失敗で全欄・行数を元結果へ戻す");
        ok=plan.Apply((f,v)=>data[f]=v!,f=>after[f==37?0:1],v=>count=v,6,8,out reason);
        check(!ok&&count==8&&data[37].AsSpan().SequenceEqual(after[0]),"文字移設: 読戻し不一致も元表示へフォールバック");
        check(TextTransferPlan.Create([13],[B("Base")],[B("Base\nExtra")],true,out _)!.Output(true).Single().AsSpan().SequenceEqual(B("\nExtra")),"文字移設: 純追記は追加分だけを捕捉");
        check(TextTransferPlan.Create([13],[B("Base")],[B("Replacement")],true,out reason)==null&&reason.Length>0,"文字移設: 追記専用機能の置換を拒否");
        check(TextTransferPlan.Create([13],[null],[B("New")],false,out reason)==null&&reason.Length>0,"文字移設: null欄への新規生成は元表示を維持");
        check(TextTransferPlan.Create([13],[B("Same")],[B("Same")],false,out reason)==null&&reason.Length==0,"文字移設: 実際に変更がない場合は空結果");
        check(TextTransferPlan.Create([13],[],[],false,out reason)==null&&reason.Length>0,"文字移設: 形状の不一致を拒否");
        var newStat=TextTransferPlan.Create([37,41],[B("HP"),null],[B("HP [10]"),B("CP [54]")],false,out _,41)!;
        var nullableData=new Dictionary<int,byte[]?>{{37,B("HP [10]")},{41,B("CP [54]")}};var rowCount=5;
        check(newStat.Apply((f,v)=>nullableData[f]=v,f=>nullableData[f],v=>rowCount=v,4,5,out _)&&nullableData[41]==null&&rowCount==4,"能力値: 元処理が新規生成した未使用欄をnull・元行数へ復元");
        nullableData[37]=B("HP [10]");nullableData[41]=B("CP [54]");rowCount=5;
        check(!newStat.Apply((f,v)=>{if(v==null)throw new IOException("null write");nullableData[f]=v;},f=>nullableData[f],v=>rowCount=v,4,5,out _)&&nullableData[41]!=null&&rowCount==5,"能力値: 新規欄の復元失敗でも元結果と追加行数へ戻す");
        check(TextTransferPlan.Create([37],[null],[B("new")],false,out _,41)==null,"能力値: 有効行のnull変更は依然拒否");        var pre=new PreviewGeometry(400,400,400,380);
        check(new PreviewGeometry(600,600,600,580).IsGeneratedAfter(pre,450,134,16),"画像: アンロック元の正確な高さ・余白を確認");
        check(new PreviewGeometry(600,600,400,580).IsGeneratedAfter(pre,450,142,8),"画像: 絵画元の正確な高さ・余白を確認");
        check(!new PreviewGeometry(600,600,600,579).IsGeneratedAfter(pre,450,134,16),"画像: フッター変位不一致を拒否");
        check(!new PreviewGeometry(600,599,600,580).IsGeneratedAfter(pre,450,134,16),"画像: 背景の高さ不一致を拒否");
        check(!new PreviewGeometry(600,600,600,580).IsGeneratedAfter(pre,float.NaN,134,16),"画像: 非数座標を拒否");
        check(!new PreviewGeometry(600,600,600,580).IsGeneratedAfter(pre,450,0,16),"画像: 空画像を拒否");
                check(new PreviewGeometry(400,400,400,380).IsGeneratedAfter(pre,250,134,16),"画像: 既存画像の再生成で高さが同じでも元処理へ復元可能");
        check(new PreviewGeometry(350,350,350,330).IsGeneratedAfter(pre,200,134,16),"画像: 価格欄等より小さい画像へ上書きした元処理も逆変換可能");
        check(!new PreviewGeometry(350,350,350,330).IsGeneratedAfter(pre,210,134,16),"画像: 上書き時も元の画像・余白と一致しない高さは拒否");
        check(SourceStatCount.CanRestore(15,15),"能力値: 元処理が既存行を書き換える際は8超の不変値を保持");
        check(SourceStatCount.CanRestore(4,5),"能力値: 元処理が追加した行数を復元");
        check(!SourceStatCount.CanRestore(15,16)&&!SourceStatCount.CanRestore(4,9),"能力値: 未対応の行数変更は拒否");
        var captured=new PreviewGeometry(600,600,400,580);var synced=new PreviewGeometry(600,600,600,580);
        check(synced.MatchesCaptured(captured,pre,true),"画像: 絵画のRootだけがWindow高さへ同期された場合を許可");
        check(!synced.MatchesCaptured(captured,pre,false),"画像: Root変更を行う元機能には同期例外を適用しない");
        check(!new PreviewGeometry(600,600,600,579).MatchesCaptured(captured,pre,true),"画像: Root同期と同時の別変位は拒否");
        check(!new PreviewGeometry(600,600,590,580).MatchesCaptured(captured,pre,true),"画像: 中途半端なRoot変更を拒否");
        var baseCommitted=new PreviewGeometry(204,204,204,458);
        var imageGenerated=new PreviewGeometry(478,478,478,458);
        check(baseCommitted.TryFinalizedBase(imageGenerated,178,284,out var collapsed)&&collapsed==new PreviewGeometry(194,194,194,174),"髪型診断再現: 同期済みの窓も画像の現在位置から復元し旧サイズを流用しない");
        check(!new PreviewGeometry(204,478,204,458).TryFinalizedBase(imageGenerated,178,284,out _),"髪型: 背景の不一致を拒否");
        check(!new PreviewGeometry(204,204,204,400).TryFinalizedBase(imageGenerated,178,284,out _),"髪型: 別処理が変更した末尾は拒否");
        check(!baseCommitted.TryFinalizedBase(imageGenerated,205,250,out _),"髪型: 画像開始が基礎ウィンドウの外なら拒否");
        check(!baseCommitted.TryFinalizedBase(imageGenerated,178,10,out _),"髪型: ウィンドウ内に収まる別画像には適用しない");
        check(Encoding.UTF8.GetString(MateriaPresentation.Extract(B("ダイレクトヒット +447 [+54=54]"))!)=="ダイレクトヒット [+54=54]","能力値: 未使用欄の基礎値447を結果に含めない");
        check(Encoding.UTF8.GetString(MateriaPresentation.Extract(B("意思力 +188 [+54=242]"))!)=="意思力 [+54=242]","能力値: 元処理の増分・合計を計算せず保持");
        check(Encoding.UTF8.GetString(MateriaPresentation.Extract(B("Direct Hit Rate +999 [54]"))!)=="Direct Hit Rate [54]","能力値: 英語・合計のみでも元結果を保持");
        var coloured=new SeString(new TextPayload("Direct Hit Rate +447 ["),new UIForegroundPayload(500),new TextPayload("54"),new UIForegroundPayload(0),new TextPayload("]")).Encode();
        check(SeString.Parse(MateriaPresentation.Extract(coloured)!).TextValue=="Direct Hit Rate [54]"&&SeString.Parse(MateriaPresentation.Extract(coloured)!).Payloads.OfType<UIForegroundPayload>().Count()==2,"能力値: 生成された結果の色を保持");
        check(MateriaPresentation.Extract(B("Direct Hit Rate +447"))==null&&MateriaPresentation.Extract(B("Direct Hit Rate unknown [54]"))==null,"能力値: 未確認の整形は推測せず拒否");        var expandedHair=new PreviewGeometry(478,478,478,458);
        check(expandedHair.TryCollapseImage(expandedHair,expandedHair,178,284,16,false,out var compactHair)&&compactHair==new PreviewGeometry(194,194,194,174),"Ctrl再現: beforeが画像込みの478でも画像284pxだけ一度除去");
        check(expandedHair.TryCollapseImage(expandedHair,compactHair,178,284,16,false,out var secondCompact)&&secondCompact==compactHair,"Ctrl再現: 非表示画像の領域だけ再拡大されても同じ高さへ戻す");
        var ctrlBase=new PreviewGeometry(196,196,196,458);
        check(ctrlBase.TryCollapseImage(expandedHair,compactHair,178,284,16,false,out var compactCtrl)&&compactCtrl==new PreviewGeometry(194,194,194,174),"Ctrl再現: 基礎高さ候補より今回の画像の位置を優先する");
        check(expandedHair.TryCollapseImage(expandedHair,compactCtrl,178,284,16,false,out var afterNextCtrl)&&afterNextCtrl==compactHair,"Ctrl再現: 再拡大後は今回の画像の位置から縮小し過去の高さを持ち込まない");
        check(!expandedHair.TryCollapseImage(expandedHair,expandedHair,178,280,16,false,out _),"Ctrl: 元画像と一致しない寸法の縮小は拒否");
        check(!new PreviewGeometry(500,500,500,470).TryCollapseImage(expandedHair,compactHair,178,284,16,false,out _),"Ctrl: 未確認の追記を含む高さは縮小しない");        var afterPrice=new LayoutStamp(478,458);
        var afterPreview=new LayoutStamp(204,184);
        check(afterPrice.TryFollow(afterPrice,afterPreview,out var displayLayout)&&displayLayout==afterPreview,"価格・画像: 価格分離後に画像を縮めても表示用配置を追従");
        check(displayLayout.TryFollow(displayLayout,new(164,144),out var afterSlots)&&afterSlots==new LayoutStamp(164,144),"価格・画像: 複数の分離が順に縮めた寸法へ追従");
        check(afterPrice==new LayoutStamp(478,458),"価格: 表示用配置の変更で元の復元地点を変更しない");
        check(!afterPrice.TryFollow(new(499,479),afterPreview,out _),"価格: 一致しない前状態の変更を採用しない");
        check(!afterPrice.TryFollow(afterPrice,new(204,185),out _),"価格: 高さと末尾の変位が一致しない変更を採用しない");        var extra=new SeString(new UIForegroundPayload(3),new NewLinePayload(),new TextPayload("Extra\n  row\n"),new NewLinePayload(),new UIForegroundPayload(0)).Encode();
        check(SeString.Parse(CapturedText.TrimPresentationEdges(extra)).TextValue=="Extra\n  row","追加情報: 外側の改行だけ除去し本文・字下げを維持");
        var itemName=B("ヴォイドロード・クロー");var glamourName=B("魔戒拳");
        var twoNames=new SeString(new TextPayload("ヴォイドロード・クロー"),NewLinePayload.Payload,new TextPayload("魔戒拳")).Encode();
        check(GlamourNameText.Matches(itemName,glamourName,twoNames),"ミラプリ名: 元処理のアイテム名・改行・ミラプリ名の2行を確認");
        check(!GlamourNameText.Matches(B("別のアイテム"),glamourName,twoNames),"ミラプリ名: 別アイテムの古い専用ノードを拒否");
        check(!GlamourNameText.Matches(itemName,B("別のミラプリ"),twoNames),"ミラプリ名: 古いミラプリ名を拒否");
        check(!GlamourNameText.Matches(itemName,[],twoNames),"ミラプリ名: 未設定時は分離しない");
        check(!GlamourNameText.Matches(itemName,glamourName,B("魔戒拳\nヴォイドロード・クロー")),"ミラプリ名: 名前の順序が違う生成結果は拒否");
        var colouredName=new SeString(new UIForegroundPayload(500),new TextPayload("魔戒拳"),new UIForegroundPayload(0)).Encode();
        var withColour=new SeString(new TextPayload("ヴォイドロード・クロー"),NewLinePayload.Payload,new UIForegroundPayload(500),new TextPayload("魔戒拳"),new UIForegroundPayload(0)).Encode();
        check(GlamourNameText.Matches(itemName,colouredName,withColour),"ミラプリ名: 元の色ペイロードを保持して比較");
        check(System.Text.Json.JsonSerializer.Deserialize<Settings>("{}")!.SimpleGlamourName,"ミラプリ名: 既存設定にない新機能は既定ON");
        check(!System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"SimpleGlamourName\":false}")!.SimpleGlamourName,"ミラプリ名: 明示的なOFFは保持");        var nameRelay=new RelaySnapshot();nameRelay.Begin(10,100);nameRelay.Set(0,twoNames);nameRelay.Begin(10,100);
        check(nameRelay.Get(10,100)!.AsSpan().SequenceEqual(twoNames),"ミラプリ名更新: 生成後の要求更新で同じ対象の名前を保持");
        nameRelay.Begin(11,100);check(nameRelay.Get(11,100)==null,"ミラプリ名更新: 対象変更で古い名前を破棄");
        var coloredNames=GlamourNameText.WithColors(itemName,glamourName,0xFF332211,0xFF665544);
        check(SeString.Parse(coloredNames).TextValue==SeString.Parse(twoNames).TextValue,"名前の色: RGBA転送で名前・2行の順序を維持");
        check(!coloredNames.AsSpan().SequenceEqual(GlamourNameText.WithColors(itemName,glamourName,0xFF112233,0xFF665544)),"名前の色: 元ノードの色変化を反映");
        check(!coloredNames.AsSpan().SequenceEqual(GlamourNameText.WithColors(itemName,glamourName,0x80332211,0xFF665544)),"名前の色: 元ノードのアルファも保持");
        var coloredPayloadNames=GlamourNameText.WithColors(itemName,colouredName,0xFF332211,0xFF665544);
        check(SeString.Parse(coloredPayloadNames).Payloads.OfType<UIForegroundPayload>().Count()==2,"名前の色: 内部の既存色ペイロードを変更しない");
        var api=new CompatibilityEntry("exact","System.Private.CoreLib","System.String","signature","StartsWith","instance System.String.StartsWith(System.String):System.Boolean");
        check(CompatibilityContract.Check([api],"exact",(_,_)=>typeof(string)).Allowed,"互換API: 必要な1署名だけを確認し他のオーバーロードを許可");
        check(!CompatibilityContract.Check([api with {Expected="instance System.String.StartsWith(System.String):System.Int32"}],"exact",(_,_)=>typeof(string)).Allowed,"互換API: 必要な署名がない場合は拒否");        var settings=new Settings();check(settings.SimpleFood&&settings.SimpleMateriaStats&&settings.SimpleUnlockPreview&&settings.SimplePaintingPreview,"新項目: 対応するSimple表示は既定ON");
        var saved=System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"SimpleFood\":false,\"SimpleMateriaStats\":false,\"NativeMateriaSlots\":true}")!;
                check(!saved.SimpleFood&&!saved.SimpleMateriaStats,"旧設定: 削除済みの標準マテリア移設フラグを無視し他項目を保持");
        check(!System.Text.Json.JsonSerializer.Serialize(saved).Contains("NativeMateriaSlots",StringComparison.Ordinal),"旧設定: 削除済み移設フラグを次の保存に含めない");
    }
}
public abstract unsafe class MockSimpleBase
{
    protected MockSimpleBase(){}
    public virtual bool Enabled{get;protected set;}
    public virtual void OnGenerateItemTooltip(byte* n,int* s){}
}
