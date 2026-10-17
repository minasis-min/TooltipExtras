namespace TooltipExtras;

// Complete independent reconnect steps once; retry only failed steps at a bounded rate.
internal sealed class RetryBatch(params (string Name,Action Run)[] steps)
{
    private readonly bool[] pending=new bool[steps.Length];
    private long nextAttempt;
    public bool Pending=>pending.Any(value=>value);
    public void Request(){Array.Fill(pending,true);nextAttempt=0;}
    public void Tick(long now,Action<string,Exception> failed)
    {
        if(!Pending||now<nextAttempt)return;
        nextAttempt=now+1000;
        for(var i=0;i<steps.Length;i++)
        {
            if(!pending[i])continue;
            try{steps[i].Run();pending[i]=false;}
            catch(Exception ex){try{failed(steps[i].Name,ex);}catch{/* Reporting must not interrupt independent recovery steps. */}}
        }
    }
}