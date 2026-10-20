namespace TooltipExtras;

internal readonly record struct RequestedTransferKey(ulong Item,nint Addon,nint Strings,nint Numbers)
{
    public bool Valid=>Item!=0&&Addon!=0&&Strings!=0&&Numbers!=0;
}

// One synchronous RequestedUpdate only. Never keep work across targets/arrays,
// hide, disposal, or a newer request; dequeue before invoking to prevent reuse.
internal sealed class RequestedTransfer
{
    private RequestedTransferKey key;
    private Action? complete;
    public bool Pending=>complete!=null;
    public void Stage(RequestedTransferKey key,Action complete)
    {
        if(!key.Valid)throw new ArgumentException("Invalid requested-update identity");
        this.key=key;this.complete=complete;
    }
    public bool Complete(RequestedTransferKey current)
    {
        var work=complete;var expected=key;Clear();
        if(work==null||!current.Valid||current!=expected)return false;
        work();return true;
    }
    public void Clear(){complete=null;key=default;}
}
