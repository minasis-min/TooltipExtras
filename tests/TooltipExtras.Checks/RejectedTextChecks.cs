using System.Text;
using System.Text.Json;
using TooltipExtras;
namespace TooltipExtrasChecks;

internal static class RejectedTextChecks
{
    public static void Run(Action<bool,string> check,string testRoot)
    {
        byte[] B(string value)=>Encoding.UTF8.GetBytes(value);
        var log=new RejectedTextLog();
        log.Record("source",13,"reason",B("not collected"));
        check(log.Snapshot().Length==0,"未分離履歴: 初期OFFでは文字列を保持しない");
        log.Enabled=true;
        log.Record("source",13,"reason",B("日本語\n100%"));
        for(var i=0;i<100;i++)log.Record("source",13,"reason",B("日本語\n100%"));
        var copy=log.Snapshot();
        check(copy.Length==1&&copy[0].Count==101&&copy[0].Text=="日本語\n100%","未分離履歴: 日本語・改行・%を保持し、同じ失敗101回を1件へ集約");
        for(var i=0;i<20;i++)log.Record("source",13,"reason",B("row"+i));
        check(log.Snapshot().Length==20&&!log.Snapshot().Any(e=>e.Text=="日本語\n100%"),"未分離履歴: 上限20件で最も古い内容を破棄");
        log.Record("source",13,"reason",B("row0"));
        log.Record("source",13,"reason",B("new"));
        check(log.Snapshot().Any(e=>e.Text=="row0"&&e.Count==2)&&!log.Snapshot().Any(e=>e.Text=="row1"),"未分離履歴: 再発した内容を最新として保持");
        check(copy[0].Count==101&&copy[0].Text=="日本語\n100%","未分離履歴: 保存用スナップショットは後続更新で変化しない");
        log.Clear();
        log.Record("source",16,"long",B(string.Concat(Enumerable.Repeat("🧪",1500))));
        var text=log.Snapshot()[0].Text!;
        check(text.Length<=1024&&text.EndsWith('…')&&!text.Contains('\uFFFD'),"未分離履歴: 長文とサロゲートペアを上限内で省略");
        log.Record("source",null,"not generated");
        check(log.Snapshot()[0].Text==null,"未分離履歴: 未実行の生成処理は文字列を捏造しない");
        var json=JsonSerializer.Serialize(new{UnseparatedText=log.Snapshot()});
        using(var doc=JsonDocument.Parse(json))
            check(doc.RootElement.GetProperty("UnseparatedText")[0].GetProperty("Reason").GetString()=="not generated","未分離履歴: 明示保存JSONに理由と文字列を含める");
        log.Enabled=false;log.Enabled=true;
        check(log.Snapshot().Length==0,"未分離履歴: OFFで消去し、再度ONでも復元しない");
        log.Record("source",2,"control",B("a\u0001b\nc"));
        check(log.Snapshot()[0].Text=="ab\nc","未分離履歴: 制御文字を除き、行区切りを保持");

        var directory=Path.Combine(testRoot,"diagnostic-retention");Directory.CreateDirectory(directory);
        var store=new SettingsStore(directory+Path.DirectorySeparatorChar,testRoot);
        store.Save("{\"Version\":1}");var settings=File.ReadAllText(store.Path);
        var unrelated=Path.Combine(directory,"diagnostic-manual.json");File.WriteAllText(unrelated,"manual");
        var other=Path.Combine(directory,"other.json");File.WriteAllText(other,"other");
        var old=Path.Combine(directory,"diagnostic-20000101T000000000Z-00000000000000000000000000000000.json");File.WriteAllText(old,"{}");
        string result="";
        for(var i=0;i<12;i++)result=store.Save(JsonSerializer.Serialize(new{Index=i}),true);
        var files=Directory.GetFiles(directory,"diagnostic-*.json").Where(path=>path!=unrelated).ToArray();
        check(files.Length==10&&!File.Exists(old)&&result.StartsWith("診断保存: "),"診断保持: 保存成功後にTE診断を直近10ファイルへ整理");
        check(files.Any(path=>File.ReadAllText(path).Contains("\"Index\":11")),"診断保持: 新しく保存したJSONを必ず保持");
        check(File.ReadAllText(store.Path)==settings&&File.ReadAllText(unrelated)=="manual"&&File.ReadAllText(other)=="other","診断保持: 設定・手動命名の診断・別JSONを保持");
    }
}
