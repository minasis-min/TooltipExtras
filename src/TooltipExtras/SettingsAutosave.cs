namespace TooltipExtras;

internal sealed class SettingsAutosave(Func<string, string> write)
{
    private string? pending;
    private long due;
    private Task<string>? active;
    private long pendingRevision,activeRevision,revision;
    public long CompletedRevision { get; private set; }
    public string Status { get; private set; } = "";
    public long Queue(string json, long now) { pending = json; due = now + 300; return pendingRevision=++revision; }
    public void Tick(long now)
    {
        if (active is { IsCompleted: true }) Complete();
        if (active != null || pending == null || now < due) return;
        var json = pending; pending = null;
        activeRevision=pendingRevision;
        active = Task.Run(() => write(json));
    }
    public void Flush()
    {
        if (active != null) Complete();
        if (pending != null) { var json = pending; pending = null; Status = Task.Run(() => write(json)).GetAwaiter().GetResult(); CompletedRevision=pendingRevision; }
    }
    private void Complete(){Status=active!.GetAwaiter().GetResult();CompletedRevision=activeRevision;active=null;}
}
