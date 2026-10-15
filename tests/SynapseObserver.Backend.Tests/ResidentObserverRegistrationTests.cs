using SynapseObserver.Backend;
using Xunit;

namespace SynapseObserver.Backend.Tests;

public sealed class ResidentObserverRegistrationTests
{
    private static readonly ResidentObserverIdentity Identity = new(
        42,
        123456,
        @"C:\Program Files\Observer\SynapseObserver.Native.dll",
        new string('A', 64),
        (nint)0x100000);

    [Fact]
    public void CleanCompletionAllowsSameVerifiedResidentToStartAgain()
    {
        var registration = new ResidentObserverRegistration(Identity);

        Assert.Equal(ResidentStartDecision.Started, registration.TryBegin(Identity));
        Assert.Equal(ResidentStartDecision.Busy, registration.TryBegin(Identity));
        registration.Complete(cleanupConfirmed: true);
        Assert.Equal(ResidentObserverState.Idle, registration.State);
        Assert.Equal(ResidentStartDecision.Started, registration.TryBegin(
            Identity with
            {
                FullPath = Identity.FullPath.ToUpperInvariant(),
                Sha256 = Identity.Sha256.ToLowerInvariant(),
            }));
    }

    [Fact]
    public void UncertainCleanupPermanentlyRefusesReuse()
    {
        var registration = new ResidentObserverRegistration(Identity);

        Assert.Equal(ResidentStartDecision.Started, registration.TryBegin(Identity));
        registration.Complete(cleanupConfirmed: false);

        Assert.Equal(ResidentObserverState.Faulted, registration.State);
        Assert.Equal(ResidentStartDecision.Faulted, registration.TryBegin(Identity));
    }

    [Theory]
    [InlineData("process")]
    [InlineData("path")]
    [InlineData("hash")]
    [InlineData("base")]
    public void ChangedResidentIdentityFaultsRegistration(string change)
    {
        var registration = new ResidentObserverRegistration(Identity);
        ResidentObserverIdentity changed = change switch
        {
            "process" => Identity with { ProcessStartTimeUtcTicks = Identity.ProcessStartTimeUtcTicks + 1 },
            "path" => Identity with { FullPath = @"C:\Elsewhere\SynapseObserver.Native.dll" },
            "hash" => Identity with { Sha256 = new string('B', 64) },
            "base" => Identity with { ModuleBase = Identity.ModuleBase + 4096 },
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };

        Assert.Equal(ResidentStartDecision.IdentityMismatch, registration.TryBegin(changed));
        Assert.Equal(ResidentObserverState.Faulted, registration.State);
        Assert.Equal(ResidentStartDecision.Faulted, registration.TryBegin(Identity));
    }

    [Fact]
    public void ConcurrentStartsHaveOneWinner()
    {
        var registration = new ResidentObserverRegistration(Identity);
        ResidentStartDecision[] decisions = Enumerable.Range(0, 16)
            .AsParallel()
            .Select(_ => registration.TryBegin(Identity))
            .ToArray();

        Assert.Equal(1, decisions.Count(static result => result == ResidentStartDecision.Started));
        Assert.Equal(15, decisions.Count(static result => result == ResidentStartDecision.Busy));
        Assert.Equal(ResidentObserverState.Recording, registration.State);
    }

    [Fact]
    public void StalePidRegistrationIsDiscardedOnlyForAConfirmedNewProcessWithoutObserverModules()
    {
        var registration = new ResidentObserverRegistration(Identity);

        Assert.True(NativeObservationSession.ShouldDiscardStaleResident(
            registration,
            Identity.ProcessStartTimeUtcTicks + 1,
            observerModuleCount: 0));
        Assert.False(NativeObservationSession.ShouldDiscardStaleResident(
            registration,
            Identity.ProcessStartTimeUtcTicks,
            observerModuleCount: 0));
        Assert.False(NativeObservationSession.ShouldDiscardStaleResident(
            registration,
            Identity.ProcessStartTimeUtcTicks + 1,
            observerModuleCount: 1));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void UnregisteredProcessIsEligibleOnlyWithoutAnObserverModule(
        int observerModuleCount,
        bool expectedEligible)
    {
        ResidentObserverAvailability availability = NativeObservationSession.EvaluateResidentAvailability(
            registration: null,
            Identity.ProcessId,
            Identity.ProcessStartTimeUtcTicks,
            observerModuleCount);

        Assert.Equal(expectedEligible, availability.Eligible);
        if (!expectedEligible)
            Assert.Contains("did not verify", availability.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void SameVerifiedIdleResidentIsEligible()
    {
        var registration = new ResidentObserverRegistration(Identity);

        ResidentObserverAvailability availability = NativeObservationSession.EvaluateResidentAvailability(
            registration,
            Identity.ProcessId,
            Identity.ProcessStartTimeUtcTicks,
            observerModuleCount: 1);

        Assert.True(availability.Eligible);
        Assert.Empty(availability.Diagnostic);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void IdleResidentRequiresExactlyOneLoadedObserverModule(int observerModuleCount)
    {
        var registration = new ResidentObserverRegistration(Identity);

        ResidentObserverAvailability availability = NativeObservationSession.EvaluateResidentAvailability(
            registration,
            Identity.ProcessId,
            Identity.ProcessStartTimeUtcTicks,
            observerModuleCount);

        Assert.False(availability.Eligible);
        Assert.Contains("exact resident observer module", availability.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingResidentIsNotEligible()
    {
        var registration = new ResidentObserverRegistration(Identity);
        Assert.Equal(ResidentStartDecision.Started, registration.TryBegin(Identity));

        ResidentObserverAvailability availability = NativeObservationSession.EvaluateResidentAvailability(
            registration,
            Identity.ProcessId,
            Identity.ProcessStartTimeUtcTicks,
            observerModuleCount: 1);

        Assert.False(availability.Eligible);
        Assert.Contains("already active", availability.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void FaultedResidentIsNotEligible()
    {
        var registration = new ResidentObserverRegistration(Identity);
        registration.Fault();

        ResidentObserverAvailability availability = NativeObservationSession.EvaluateResidentAvailability(
            registration,
            Identity.ProcessId,
            Identity.ProcessStartTimeUtcTicks,
            observerModuleCount: 1);

        Assert.False(availability.Eligible);
        Assert.Contains("cannot be reused", availability.Diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void ReusedPidIsEligibleOnlyWhenNewProcessHasNoObserverModule(
        int observerModuleCount,
        bool expectedEligible)
    {
        var registration = new ResidentObserverRegistration(Identity);

        ResidentObserverAvailability availability = NativeObservationSession.EvaluateResidentAvailability(
            registration,
            Identity.ProcessId,
            Identity.ProcessStartTimeUtcTicks + 1,
            observerModuleCount);

        Assert.Equal(expectedEligible, availability.Eligible);
        if (!expectedEligible)
            Assert.Contains("new Synapse process", availability.Diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationForAnotherPidIsNotEligible()
    {
        var registration = new ResidentObserverRegistration(Identity);

        ResidentObserverAvailability availability = NativeObservationSession.EvaluateResidentAvailability(
            registration,
            Identity.ProcessId + 1,
            Identity.ProcessStartTimeUtcTicks,
            observerModuleCount: 1);

        Assert.False(availability.Eligible);
        Assert.Contains("identity is unavailable", availability.Diagnostic, StringComparison.Ordinal);
    }
}
