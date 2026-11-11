namespace TooltipExtras;

internal sealed class HookConnectionRetry
{
    private static readonly int[] Delays = [5_000, 15_000, 45_000];
    private long nextAttempt;
    public int Failures { get; private set; }
    public bool CanTry(long now) => now >= nextAttempt;

    public bool Failed(Exception error, long now)
    {
        var retry=IsAllocationFailure(error.GetBaseException()) && Failures<Delays.Length;
        nextAttempt=retry ? now+Delays[Failures] : long.MaxValue;
        Failures++;
        return retry;
    }

    public void Reset()
    {
        Failures=0;
        nextAttempt=0;
    }

    private static bool IsAllocationFailure(Exception error)
        => error is OutOfMemoryException || error.Message.Contains(
            "Unable to find memory location to fit MemoryBuffer",StringComparison.Ordinal);

    public static string Reason(Exception error)
    {
        var cause=error.GetBaseException();
        return IsAllocationFailure(cause)
            ? "Dalamudがフック用のメモリを確保できませんでした"
            : cause.GetType().Name+": "+cause.Message;
    }
}
