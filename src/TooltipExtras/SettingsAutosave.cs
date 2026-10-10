namespace TooltipExtras;

internal sealed class SettingsAutosave(Func<string, string> write)
{
    private string? pending;
    private long due;
    private Task<string>? active;
    public string Status { get; private set; } = "";
    public void Queue(string json, long now) { pending = json; due = now + 300; }
    public void Tick(long now)
    {
        if (active is { IsCompleted: true }) { Status = active.GetAwaiter().GetResult(); active = null; }
        if (active != null || pending == null || now < due) return;
        var json = pending; pending = null;
        active = Task.Run(() => write(json));
    }
    public void Flush()
    {
        if (active != null) { Status = active.GetAwaiter().GetResult(); active = null; }
        if (pending != null) { var json = pending; pending = null; Status = Task.Run(() => write(json)).GetAwaiter().GetResult(); }
    }
}
