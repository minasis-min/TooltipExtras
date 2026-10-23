using System.Text.Json;
namespace TooltipExtras;

internal sealed class SettingsStore
{
    private readonly string directory;
    private readonly object diagnosticGate=new();
    internal const int DiagnosticFileLimit=10;
    public string Path { get; }
    public string Message { get; private set; } = "未保存";
    public bool CanSave { get; private set; } = true;
    private readonly string legacyPath;
    public bool NeedsMigration { get; private set; }
    public SettingsStore(string configDirectory, string assemblyDirectory)
    {
        if (!System.IO.Path.IsPathFullyQualified(configDirectory)) throw new IOException("設定フォルダの絶対パスが必要です。");
        directory = System.IO.Path.GetFullPath(configDirectory);
        Path = System.IO.Path.Combine(directory, "settings.json");
        legacyPath = System.IO.Path.Combine(System.IO.Path.GetFullPath(assemblyDirectory), "data", "settings.json");
    }
    public Settings Load()
    {
        NeedsMigration = false;
        var input = File.Exists(Path) ? Path : File.Exists(legacyPath) ? legacyPath : null;
        if (input == null) return new();
        try
        {
            var value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(input)) ?? throw new IOException("空の設定");
            if (value.Version != 1) throw new IOException("未対応の設定バージョン");
            value.Normalize(); NeedsMigration = input != Path; Message = NeedsMigration ? "旧設定を読み込みました。標準設定フォルダへ保存します。" : "設定読み込み済み"; return value;
        }
        catch (Exception ex) { CanSave = false; Message = "設定読み込み失敗。上書きを停止: " + ex.Message; return new(); }
    }
    public string Save(string json, bool diagnostic = false)
    {
        // Called only on a worker thread. The game draw/lifecycle callbacks do no I/O.
        if (!diagnostic && !CanSave) return Message;
        string? temp=null;
        try
        {
            Directory.CreateDirectory(directory);
            if (diagnostic)
            {
                lock(diagnosticGate)
                {
                var destination = System.IO.Path.Combine(directory, $"diagnostic-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
                File.WriteAllText(destination, json);
                var removed=PruneDiagnostics(destination);
                return "診断保存: " + destination+(removed?"":" / 古い診断ファイルの整理を完了できませんでした");
                }
            }
            temp = System.IO.Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, json);
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak", true);
            else File.Move(temp, Path);
            return "保存しました（既存設定は .bak）";
        }
        catch (Exception ex) { return "保存失敗: " + ex.Message; }
        finally
        {
            // This save owns only this file. Preserve the settings, backup and
            // any other in-progress save; deletion failure must not mask I/O errors.
            if(temp!=null)try{File.Delete(temp);}catch{}
        }
    }
    private bool PruneDiagnostics(string current)
    {
        try
        {
            var files=Directory.EnumerateFiles(directory,"diagnostic-*.json",SearchOption.TopDirectoryOnly)
                .Where(path=>System.Text.RegularExpressions.Regex.IsMatch(System.IO.Path.GetFileName(path),@"^diagnostic-[0-9]{8}T[0-9]{9}Z-[0-9a-f]{32}\.json$",System.Text.RegularExpressions.RegexOptions.CultureInvariant)
                    &&(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0)
                .Where(path=>!string.Equals(path,current,StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path=>System.IO.Path.GetFileName(path),StringComparer.Ordinal).Skip(DiagnosticFileLimit-1).ToArray();
            var complete=true;
            foreach(var path in files)
            {
                // Only direct files with TE's generated names. No recursive delete,
                // settings/backups, subdirectories, links or arbitrary JSON files.
                if(!string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),System.IO.Path.TrimEndingDirectorySeparator(directory),StringComparison.OrdinalIgnoreCase)){complete=false;continue;}
                try{File.Delete(path);}catch{complete=false;}
            }
            return complete;
        }
        catch{return false;} // A pruning failure must not hide a successful save.
    }
}
