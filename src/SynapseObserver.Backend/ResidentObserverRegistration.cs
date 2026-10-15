namespace SynapseObserver.Backend;

internal enum ResidentObserverState
{
    Idle,
    Recording,
    Faulted,
}

internal enum ResidentStartDecision
{
    Started,
    Busy,
    Faulted,
    IdentityMismatch,
}

internal readonly record struct ResidentObserverAvailability(bool Eligible, string Diagnostic);

internal readonly record struct ResidentObserverIdentity(
    int ProcessId,
    long ProcessStartTimeUtcTicks,
    string FullPath,
    string Sha256,
    nint ModuleBase)
{
    public bool Matches(ResidentObserverIdentity other) =>
        ProcessId == other.ProcessId
        && ProcessStartTimeUtcTicks == other.ProcessStartTimeUtcTicks
        && ModuleBase == other.ModuleBase
        && string.Equals(FullPath, other.FullPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ResidentObserverRegistration(ResidentObserverIdentity identity)
{
    private int state = (int)ResidentObserverState.Idle;

    public ResidentObserverIdentity Identity { get; } = identity;

    public ResidentObserverState State => (ResidentObserverState)Volatile.Read(ref state);

    public ResidentStartDecision TryBegin(ResidentObserverIdentity current)
    {
        if (!Identity.Matches(current))
        {
            Interlocked.Exchange(ref state, (int)ResidentObserverState.Faulted);
            return ResidentStartDecision.IdentityMismatch;
        }

        return State switch
        {
            ResidentObserverState.Faulted => ResidentStartDecision.Faulted,
            ResidentObserverState.Recording => ResidentStartDecision.Busy,
            _ => Interlocked.CompareExchange(
                    ref state,
                    (int)ResidentObserverState.Recording,
                    (int)ResidentObserverState.Idle) == (int)ResidentObserverState.Idle
                ? ResidentStartDecision.Started
                : State == ResidentObserverState.Recording
                    ? ResidentStartDecision.Busy
                    : ResidentStartDecision.Faulted,
        };
    }

    public void Complete(bool cleanupConfirmed)
    {
        if (!cleanupConfirmed
            || Interlocked.CompareExchange(
                ref state,
                (int)ResidentObserverState.Idle,
                (int)ResidentObserverState.Recording) != (int)ResidentObserverState.Recording)
        {
            Interlocked.Exchange(ref state, (int)ResidentObserverState.Faulted);
        }
    }

    public void Fault() => Interlocked.Exchange(ref state, (int)ResidentObserverState.Faulted);
}
