namespace TooltipExtras;

internal sealed class OutputNotices
{
    private readonly HashSet<string> seen=[];
    public bool Report(string key,Action notify)
    {
        // One local notice per failure category per load. Repeated hovers and
        // persistent unsupported formatters must not fill the game chat log.
        if(!seen.Add(key))return false;
        notify();return true;
    }
}
