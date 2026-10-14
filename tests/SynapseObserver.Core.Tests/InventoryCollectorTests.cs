namespace SynapseObserver.Core.Tests;

public sealed class InventoryCollectorTests
{
    [Fact]
    public async Task CollectAsync_CancellationReturnsPartialUsefulSnapshot()
    {
        using var source = new CancellingInventorySource();
        var snapshot = await new InventoryCollector(source).CollectAsync(source.Token);

        Assert.Equal(CollectionState.Collected, snapshot.MachineIdentity.State);
        Assert.Equal("Blade test", Assert.Single(snapshot.MachineIdentity.Value).ProductName);
        Assert.Equal(CollectionState.Cancelled, snapshot.PowerSource.State);
        Assert.Equal(CollectionState.Cancelled, snapshot.RazerHidInterfaces.State);
        Assert.Equal(CollectionState.Cancelled, snapshot.SynapseProcesses.State);
    }

    [Fact]
    public async Task CollectAsync_KeepsUnavailableDistinctFromAbsent()
    {
        var snapshot = await new InventoryCollector(new StatusInventorySource()).CollectAsync();

        Assert.Equal(CollectionState.Unavailable, snapshot.RazerHidInterfaces.State);
        Assert.Equal(CollectionState.Absent, snapshot.SynapseProcesses.State);
    }

    private sealed class CancellingInventorySource : IInventorySource, IDisposable
    {
        private readonly CancellationTokenSource _source = new();

        public CancellationToken Token => _source.Token;

        public ValueTask<SectionResult<IReadOnlyList<MachineIdentity>>> CollectMachineIdentityAsync(
            CancellationToken cancellationToken)
        {
            IReadOnlyList<MachineIdentity> identity =
            [new MachineIdentity("test", "Razer", "Blade test", null, null, null)];
            _source.Cancel();
            return ValueTask.FromResult(SectionResult<IReadOnlyList<MachineIdentity>>.Collected(identity));
        }

        public ValueTask<SectionResult<PowerSourceEvidence?>> CollectPowerSourceAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Cancelled sections must not call the source.");

        public ValueTask<SectionResult<IReadOnlyList<HidInterfaceEvidence>>> CollectRazerHidInterfacesAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Cancelled sections must not call the source.");

        public ValueTask<SectionResult<IReadOnlyList<SynapseProcessEvidence>>> CollectSynapseProcessesAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Cancelled sections must not call the source.");

        public void Dispose() => _source.Dispose();
    }

    private sealed class StatusInventorySource : IInventorySource
    {
        public ValueTask<SectionResult<IReadOnlyList<MachineIdentity>>> CollectMachineIdentityAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<IReadOnlyList<MachineIdentity>>.Absent([]));

        public ValueTask<SectionResult<PowerSourceEvidence?>> CollectPowerSourceAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<PowerSourceEvidence?>.Unavailable(null, "test unavailable"));

        public ValueTask<SectionResult<IReadOnlyList<HidInterfaceEvidence>>> CollectRazerHidInterfacesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Unavailable([], "test unavailable"));

        public ValueTask<SectionResult<IReadOnlyList<SynapseProcessEvidence>>> CollectSynapseProcessesAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Absent([]));
    }
}
