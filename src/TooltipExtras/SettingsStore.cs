using System.Text.Json;
namespace TooltipExtras;

internal sealed class SettingsStore
{
    private readonly string directory;
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
        try
        {
            Directory.CreateDirectory(directory);
            if (diagnostic)
            {
                var destination = System.IO.Path.Combine(directory, $"diagnostic-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
                File.WriteAllText(destination, json); return "診断保存: " + destination;
            }
            var temp = System.IO.Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, json);
            if (File.Exists(Path)) File.Replace(temp, Path, Path + ".bak", true);
            else File.Move(temp, Path);
            return "保存しました（既存設定は .bak）";
        }
        catch (Exception ex) { return "保存失敗: " + ex.Message; }
    }
}
