using System.Runtime.InteropServices;

namespace SynapseObserver.Core;

public enum PdhTemperatureAlias
{
    RazerEcPci0SbrgTzrz,
    RazerEcPc00LpcbTzrz,
}

public enum PdhTemperatureFailure
{
    None,
    OpenQuery,
    AddCounter,
    CollectQuery,
    ReadCounter,
    CounterStatus,
    InvalidValue,
    NativeApiUnavailable,
    Cancelled,
    NativeFailure,
}

// Fixed aliases identify reviewed counter paths, never arbitrary instance names.
public sealed record PdhTemperatureEvidence(
    PdhTemperatureAlias Alias,
    CollectionState State,
    PdhTemperatureFailure Failure,
    uint? NativeStatus = null,
    uint? CounterStatus = null,
    double? RawTenthsKelvin = null,
    double? ValueCelsius = null)
{
    public uint? CleanupStatus { get; init; }
    public bool CleanupApiUnavailable { get; init; }
    public string Provenance => "WindowsPdhThermalZoneInformationHighPrecisionTemperature";
    public string RawUnits => "TenthsKelvin";
    public string ValueUnits => "Celsius";
}

internal interface IPdhTemperatureNative
{
    uint OpenQuery(out nint query);
    uint AddCounter(nint query, string path, out nint counter);
    uint CollectQuery(nint query);
    uint ReadCounter(nint counter, out uint counterStatus, out double value);
    uint CloseQuery(nint query);
}

internal static class PdhTemperatureCollector
{
    // Same two fixed EC TZRZ paths used by OpenBlade's Windows temperature source.
    // Query each independently: one working alias must not hide the other's result.
    private static readonly (PdhTemperatureAlias Alias, string Path)[] Counters =
    [
        (PdhTemperatureAlias.RazerEcPci0SbrgTzrz,
            @"\Thermal Zone Information(\_SB.PCI0.SBRG.EC0.TZRZ)\High Precision Temperature"),
        (PdhTemperatureAlias.RazerEcPc00LpcbTzrz,
            @"\Thermal Zone Information(\_SB.PC00.LPCB.EC0.TZRZ)\High Precision Temperature"),
    ];

    internal static SectionResult<IReadOnlyList<PdhTemperatureEvidence>> Collect(
        IPdhTemperatureNative native, CancellationToken cancellationToken)
    {
        var results = Counters.Select(counter => Read(native, counter.Alias, counter.Path, cancellationToken)).ToArray();
        var state = results.Any(static result => result.State == CollectionState.Cancelled)
            ? CollectionState.Cancelled
            : results.All(static result => result.State == CollectionState.Collected)
                ? CollectionState.Collected
                : results.Any(static result => result.State is CollectionState.Collected or CollectionState.Partial)
                    ? CollectionState.Partial : CollectionState.Unavailable;
        return new(state, results, state == CollectionState.Collected ? null
            : "One or more fixed Windows PDH temperature counters could not be read.");
    }

    private static PdhTemperatureEvidence Read(
        IPdhTemperatureNative native, PdhTemperatureAlias alias, string path, CancellationToken cancellationToken)
    {
        nint query = 0;
        var result = new PdhTemperatureEvidence(alias, CollectionState.Unavailable, PdhTemperatureFailure.NativeFailure);
        try
        {
            result = ReadQuery(native, alias, path, cancellationToken, ref query);
        }
        finally
        {
            // Query closure releases the counter too, including cancellation and failed reads.
            if (query != 0)
            {
                try
                {
                    var status = native.CloseQuery(query);
                    if (status != 0) result = result with
                    {
                        State = result.State == CollectionState.Cancelled ? CollectionState.Cancelled : CollectionState.Partial,
                        CleanupStatus = status,
                    };
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    result = result with
                    {
                        State = result.State == CollectionState.Cancelled ? CollectionState.Cancelled : CollectionState.Partial,
                        CleanupApiUnavailable = true,
                    };
                }
            }
        }
        return result;
    }

    private static PdhTemperatureEvidence ReadQuery(
        IPdhTemperatureNative native, PdhTemperatureAlias alias, string path, CancellationToken cancellationToken, ref nint query)
    {
        var stage = PdhTemperatureFailure.OpenQuery;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = native.OpenQuery(out query);
            if (status != 0) return Failed(alias, stage, status);
            cancellationToken.ThrowIfCancellationRequested();
            stage = PdhTemperatureFailure.AddCounter;
            status = native.AddCounter(query, path, out var counter);
            if (status != 0) return Failed(alias, stage, status);
            cancellationToken.ThrowIfCancellationRequested();
            stage = PdhTemperatureFailure.CollectQuery;
            status = native.CollectQuery(query);
            if (status != 0) return Failed(alias, stage, status);
            cancellationToken.ThrowIfCancellationRequested();
            stage = PdhTemperatureFailure.ReadCounter;
            status = native.ReadCounter(counter, out var counterStatus, out var raw);
            if (status != 0) return Failed(alias, stage, status);
            cancellationToken.ThrowIfCancellationRequested();
            if (counterStatus is not 0 and not 1)
                return new(alias, CollectionState.Unavailable, PdhTemperatureFailure.CounterStatus, status, counterStatus);
            var celsius = Convert(raw);
            return new(alias, celsius.HasValue ? CollectionState.Collected : CollectionState.Unavailable,
                celsius.HasValue ? PdhTemperatureFailure.None : PdhTemperatureFailure.InvalidValue,
                status, counterStatus, double.IsFinite(raw) ? raw : null, celsius);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(alias, CollectionState.Cancelled, PdhTemperatureFailure.Cancelled);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return new(alias, CollectionState.Unavailable, PdhTemperatureFailure.NativeApiUnavailable);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new(alias, CollectionState.Unavailable, PdhTemperatureFailure.NativeFailure);
        }
    }

    private static PdhTemperatureEvidence Failed(PdhTemperatureAlias alias, PdhTemperatureFailure stage, uint status) =>
        new(alias, CollectionState.Unavailable, stage, status);

    internal static double? Convert(double raw)
    {
        var value = raw / 10 - 273.15;
        return double.IsFinite(raw) && value is >= 0 and <= 115 ? value : null;
    }

    internal static SectionResult<IReadOnlyList<PdhTemperatureEvidence>> Sanitize(
        SectionResult<IReadOnlyList<PdhTemperatureEvidence>> source)
    {
        var values = source.Value.Take(2)
            .Where(static item => Enum.IsDefined(item.Alias))
            .DistinctBy(static item => item.Alias)
            .Select(static item =>
            {
                var raw = item.RawTenthsKelvin is { } value && double.IsFinite(value) ? value : (double?)null;
                var celsius = item.State is CollectionState.Collected or CollectionState.Partial && item.Failure == PdhTemperatureFailure.None
                    && item.NativeStatus == 0 && item.CounterStatus is 0 or 1 && raw.HasValue
                        ? Convert(raw.Value) : null;
                return item with
                {
                    State = item.State == CollectionState.Cancelled ? CollectionState.Cancelled
                        : item.CleanupApiUnavailable || item.CleanupStatus is not null and not 0 ? CollectionState.Partial
                        : celsius.HasValue ? CollectionState.Collected
                        : item.State == CollectionState.Cancelled ? CollectionState.Cancelled : CollectionState.Unavailable,
                    Failure = Enum.IsDefined(item.Failure) && item.Failure != PdhTemperatureFailure.None
                        ? item.Failure : celsius.HasValue ? PdhTemperatureFailure.None : PdhTemperatureFailure.InvalidValue,
                    RawTenthsKelvin = raw,
                    ValueCelsius = celsius,
                };
            }).ToArray();
        var state = source.State == CollectionState.Skipped && values.Length == 0 ? CollectionState.Skipped
            : source.State == CollectionState.Cancelled || values.Any(static item => item.State == CollectionState.Cancelled)
                ? CollectionState.Cancelled
                : values.Length == 2 && values.All(static item => item.State == CollectionState.Collected)
                    ? CollectionState.Collected
                    : values.Any(static item => item.State is CollectionState.Collected or CollectionState.Partial) ? CollectionState.Partial : CollectionState.Unavailable;
        return new(state, values, state == CollectionState.Collected ? null : "Fixed Windows PDH temperature evidence is incomplete.");
    }
}

internal sealed class WindowsPdhTemperatureNative : IPdhTemperatureNative
{
    public uint OpenQuery(out nint query) => PdhOpenQueryW(null, 0, out query);
    public uint AddCounter(nint query, string path, out nint counter) => PdhAddEnglishCounterW(query, path, 0, out counter);
    public uint CollectQuery(nint query) => PdhCollectQueryData(query);
    public uint ReadCounter(nint counter, out uint counterStatus, out double value)
    {
        var status = PdhGetFormattedCounterValue(counter, 0x200, out _, out var formatted);
        counterStatus = formatted.Status;
        value = formatted.Value;
        return status;
    }
    public uint CloseQuery(nint query) => PdhCloseQuery(query);

    [StructLayout(LayoutKind.Explicit)]
    private struct FormattedValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? dataSource, nuint userData, out nint query);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(nint query, string fullCounterPath, nuint userData, out nint counter);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(nint query);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterValue(nint counter, uint format, out uint counterType, out FormattedValue value);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(nint query);
}
