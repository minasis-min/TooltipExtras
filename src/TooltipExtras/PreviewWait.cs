namespace TooltipExtras;

// One monotonic deadline per hover target. Repeated updates cannot extend it.
// This tracks layout only; texture loading never holds back the native tooltip.
internal sealed class PreviewWait
{
    public const int LimitMs=100;
    private ulong item;
    private nint addon;
    private long started,cycleStarted;
    private bool active,timedOut;
    public long SuppressedFrames {get;private set;}
    public long Timeouts {get;private set;}
    public long LastWaitMs {get;private set;}
    public long MaxWaitMs {get;private set;}
    public void Begin(ulong id,nint address,long now)
    {
        if(item!=id||addon!=address){item=id;addon=address;started=cycleStarted=now;timedOut=false;}
        if(!active)cycleStarted=now;active=true;
    }
    private long Elapsed(long now)=>Math.Max(0,now-started);
    public bool ShouldSuppress(long now)
    {
        if(!active)return false;
        LastWaitMs=Math.Max(0,now-cycleStarted);MaxWaitMs=Math.Max(MaxWaitMs,LastWaitMs);
        if(Elapsed(now)>=LimitMs)
        {
            if(!timedOut){Timeouts++;timedOut=true;}
            return false;
        }
        SuppressedFrames++;return true;
    }
    public void Complete(long now)
    {
        if(active){LastWaitMs=Math.Max(0,now-cycleStarted);MaxWaitMs=Math.Max(MaxWaitMs,LastWaitMs);}
        active=false;
    }
    public void Clear(){item=0;addon=0;active=false;timedOut=false;}
}
