namespace SynapseObserver.Core;

public interface IInventorySource
{
    ValueTask<SectionResult<IReadOnlyList<MachineIdentity>>> CollectMachineIdentityAsync(CancellationToken cancellationToken);

    ValueTask<SectionResult<PowerSourceEvidence?>> CollectPowerSourceAsync(CancellationToken cancellationToken);

    ValueTask<SectionResult<IReadOnlyList<HidInterfaceEvidence>>> CollectRazerHidInterfacesAsync(CancellationToken cancellationToken);

    ValueTask<SectionResult<IReadOnlyList<SynapseProcessEvidence>>> CollectSynapseProcessesAsync(CancellationToken cancellationToken);
}

public sealed class InventoryCollector
{
    private readonly IInventorySource _source;

    public InventoryCollector(IInventorySource? source = null)
    {
        _source = source ?? new WindowsInventorySource();
    }

    public async Task<InventorySnapshot> CollectAsync(CancellationToken cancellationToken = default)
    {
        var identity = await CollectSectionAsync(
            _source.CollectMachineIdentityAsync,
            Array.Empty<MachineIdentity>(),
            cancellationToken).ConfigureAwait(false);

        var power = await CollectSectionAsync(
            _source.CollectPowerSourceAsync,
            default(PowerSourceEvidence),
            cancellationToken).ConfigureAwait(false);

        var hid = await CollectSectionAsync(
            _source.CollectRazerHidInterfacesAsync,
            Array.Empty<HidInterfaceEvidence>(),
            cancellationToken).ConfigureAwait(false);

        var processes = await CollectSectionAsync(
            _source.CollectSynapseProcessesAsync,
            Array.Empty<SynapseProcessEvidence>(),
            cancellationToken).ConfigureAwait(false);

        return new InventorySnapshot(
            InventorySnapshot.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            identity,
            power,
            hid,
            processes);
    }

    private static async ValueTask<SectionResult<T>> CollectSectionAsync<T>(
        Func<CancellationToken, ValueTask<SectionResult<T>>> collector,
        T emptyValue,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return SectionResult<T>.Cancelled(emptyValue);
        }

        try
        {
            return await collector(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SectionResult<T>.Cancelled(emptyValue);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return SectionResult<T>.Unavailable(emptyValue, $"Collector failed with {ex.GetType().Name}.");
        }
    }
}
