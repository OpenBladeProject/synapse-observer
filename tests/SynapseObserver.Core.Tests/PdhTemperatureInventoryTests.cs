using System.IO.Compression;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class PdhTemperatureInventoryTests
{
    [Fact]
    public void Collect_UsesOnlyTwoKnownPathsAndClosesEachQuery()
    {
        var native = new FakeNative();
        var result = PdhTemperatureCollector.Collect(native, CancellationToken.None);
        Assert.Equal(CollectionState.Collected, result.State);
        Assert.Equal(new[]
        {
            @"\Thermal Zone Information(\_SB.PCI0.SBRG.EC0.TZRZ)\High Precision Temperature",
            @"\Thermal Zone Information(\_SB.PC00.LPCB.EC0.TZRZ)\High Precision Temperature",
        }, native.Paths);
        Assert.Equal(2, native.OpenCount);
        Assert.Equal(2, native.CloseCount);
        Assert.All(result.Value, item =>
        {
            Assert.Equal(3231.5, item.RawTenthsKelvin);
            Assert.Equal(50, item.ValueCelsius!.Value, 6);
            Assert.Equal("TenthsKelvin", item.RawUnits);
            Assert.Equal("Celsius", item.ValueUnits);
            Assert.Equal("WindowsPdhThermalZoneInformationHighPrecisionTemperature", item.Provenance);
        });
    }

    [Theory]
    [InlineData(PdhTemperatureFailure.OpenQuery)]
    [InlineData(PdhTemperatureFailure.AddCounter)]
    [InlineData(PdhTemperatureFailure.CollectQuery)]
    [InlineData(PdhTemperatureFailure.ReadCounter)]
    public void Collect_PreservesNativeFailureAndContinuesOtherAlias(PdhTemperatureFailure stage)
    {
        var native = new FakeNative { FailStage = stage };
        var result = PdhTemperatureCollector.Collect(native, CancellationToken.None);
        Assert.Equal(CollectionState.Partial, result.State);
        Assert.Equal(stage, result.Value[0].Failure);
        Assert.Equal(0xC0000BB8u, result.Value[0].NativeStatus);
        Assert.Null(result.Value[0].ValueCelsius);
        Assert.Equal(CollectionState.Collected, result.Value[1].State);
        Assert.Equal(stage == PdhTemperatureFailure.OpenQuery ? 1 : 2, native.CloseCount);
    }

    [Theory]
    [InlineData(0u, CollectionState.Collected)]
    [InlineData(1u, CollectionState.Collected)]
    [InlineData(0xC0000BC6u, CollectionState.Unavailable)]
    public void Collect_ChecksReturnedCounterStatus(uint status, CollectionState expected)
    {
        var result = PdhTemperatureCollector.Collect(new FakeNative { CounterStatus = status }, CancellationToken.None);
        Assert.Equal(expected, result.State);
        Assert.All(result.Value, item => Assert.Equal(status, item.CounterStatus));
        if (expected == CollectionState.Unavailable)
            Assert.All(result.Value, item => Assert.Null(item.RawTenthsKelvin));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(5000)]
    public void Collect_RejectsInvalidOrImplausibleValues(double raw)
    {
        var result = PdhTemperatureCollector.Collect(new FakeNative { Raw = raw }, CancellationToken.None);
        Assert.All(result.Value, item =>
        {
            Assert.Equal(PdhTemperatureFailure.InvalidValue, item.Failure);
            Assert.Null(item.ValueCelsius);
            Assert.Equal(double.IsFinite(raw) ? raw : (double?)null, item.RawTenthsKelvin);
        });
        JsonSerializer.Serialize(result); // Non-finite values cannot poison partial export.
    }

    [Theory]
    [InlineData(PdhTemperatureFailure.OpenQuery)]
    [InlineData(PdhTemperatureFailure.AddCounter)]
    [InlineData(PdhTemperatureFailure.CollectQuery)]
    [InlineData(PdhTemperatureFailure.ReadCounter)]
    public void Collect_CancellationStopsFurtherNativeReadsAndReleasesQuery(PdhTemperatureFailure stage)
    {
        using var cancellation = new CancellationTokenSource();
        var native = new FakeNative { Cancellation = cancellation, CancelStage = stage };
        var result = PdhTemperatureCollector.Collect(native, cancellation.Token);
        Assert.Equal(CollectionState.Cancelled, result.State);
        Assert.All(result.Value, item => Assert.Equal(PdhTemperatureFailure.Cancelled, item.Failure));
        Assert.Equal(1, native.OpenCount);
        Assert.Equal(1, native.CloseCount);
        Assert.Equal(stage, native.LastStage);
    }

    [Fact]
    public void Collect_AlreadyCancelledDoesNotOpenQuery()
    {
        var native = new FakeNative();
        var result = PdhTemperatureCollector.Collect(native, new CancellationToken(true));
        Assert.Equal(CollectionState.Cancelled, result.State);
        Assert.Equal(0, native.OpenCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collect_MissingNativeApiIsExplicitAndDoesNotExportExceptionText(bool missingEntry)
    {
        var native = new FakeNative { MissingApi = true, MissingEntry = missingEntry };
        var result = PdhTemperatureCollector.Collect(native, CancellationToken.None);
        Assert.All(result.Value, item => Assert.Equal(PdhTemperatureFailure.NativeApiUnavailable, item.Failure));
        Assert.DoesNotContain("private", JsonSerializer.Serialize(result));
        Assert.Equal(2, native.CloseCount);
    }

    [Fact]
    public void Collect_CleanupFailureKeepsReadingButReportsPartialEvidence()
    {
        var native = new FakeNative { CloseStatus = 5 };
        var result = PdhTemperatureCollector.Collect(native, CancellationToken.None);
        Assert.Equal(CollectionState.Partial, result.State);
        Assert.All(result.Value, item =>
        {
            Assert.Equal(5u, item.CleanupStatus);
            Assert.NotNull(item.ValueCelsius);
        });
        Assert.Equal(2, native.CloseCount);
    }

    [Fact]
    public void Sanitize_BoundsAliasesAndRecomputesConversion()
    {
        var valid = new PdhTemperatureEvidence(PdhTemperatureAlias.RazerEcPc00LpcbTzrz,
            CollectionState.Collected, PdhTemperatureFailure.None, 0, 0, 3231.5, 99);
        var sanitized = SupportInventorySanitizer.Sanitize(SupportInventorySnapshot.Empty with
        {
            PdhTemperatures = SectionResult<IReadOnlyList<PdhTemperatureEvidence>>.Collected(
                [valid, valid with { Alias = (PdhTemperatureAlias)99 }, valid]),
        });
        Assert.Equal(CollectionState.Partial, sanitized.PdhTemperatures.State);
        Assert.Equal(50, Assert.Single(sanitized.PdhTemperatures.Value).ValueCelsius!.Value, 6);
    }

    [Fact]
    public async Task Bundle_SeparatesWmiFromPdhAndPreservesCoverageGap()
    {
        var inventory = SyntheticPreview.CreateInventory() with
        {
            Support = SupportInventorySnapshot.Empty with
            {
                ThermalZones = SectionResult<IReadOnlyList<ThermalZoneEvidence>>.Unavailable([], "WMI unavailable"),
                PdhTemperatures = PdhTemperatureCollector.Collect(new FakeNative { FailStage = PdhTemperatureFailure.AddCounter }, CancellationToken.None),
            },
        };
        using var session = new EvidenceSession(inventory);
        var path = Path.Combine(Path.GetTempPath(), $"pdh-evidence-{Guid.NewGuid():N}.zip");
        try
        {
            await EvidenceBundleWriter.WriteAsync(session, path);
            using var archive = ZipFile.OpenRead(path);
            using var manifestReader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
            var json = await manifestReader.ReadToEndAsync();
            Assert.Contains("pdhTemperatures", json);
            Assert.Contains("razerEcPc00LpcbTzrz", json);
            Assert.Contains("WindowsPdhThermalZoneInformationHighPrecisionTemperature", json);
            Assert.Contains("rawTenthsKelvin", json);
            Assert.DoesNotContain(@"\_SB.", json);
            using var qualityReader = new StreamReader(archive.GetEntry("quality-receipt.json")!.Open());
            var quality = await qualityReader.ReadToEndAsync();
            Assert.Contains("support.pdhTemperatures", quality);
            Assert.Contains("support.thermalZones", quality);
        }
        finally { File.Delete(path); }
    }

    private sealed class FakeNative : IPdhTemperatureNative
    {
        public List<string> Paths { get; } = [];
        public int OpenCount { get; private set; }
        public int CloseCount { get; private set; }
        public PdhTemperatureFailure FailStage { get; init; }
        public PdhTemperatureFailure CancelStage { get; init; }
        public PdhTemperatureFailure LastStage { get; private set; }
        public CancellationTokenSource? Cancellation { get; init; }
        public uint CounterStatus { get; init; }
        public uint CloseStatus { get; init; }
        public double Raw { get; init; } = 3231.5;
        public bool MissingApi { get; init; }
        public bool MissingEntry { get; init; }
        private uint Step(PdhTemperatureFailure stage)
        {
            LastStage = stage;
            if (stage == CancelStage) Cancellation!.Cancel();
            return OpenCount == 1 && stage == FailStage ? 0xC0000BB8u : 0;
        }
        public uint OpenQuery(out nint query)
        {
            OpenCount++;
            var result = Step(PdhTemperatureFailure.OpenQuery);
            query = result == 0 ? OpenCount : 0;
            return result;
        }
        public uint AddCounter(nint query, string path, out nint counter)
        {
            counter = query;
            Paths.Add(path);
            if (MissingApi)
            {
                if (MissingEntry) throw new EntryPointNotFoundException(@"C:\private\native.dll");
                throw new DllNotFoundException(@"C:\private\native.dll");
            }
            return Step(PdhTemperatureFailure.AddCounter);
        }
        public uint CollectQuery(nint query) => Step(PdhTemperatureFailure.CollectQuery);
        public uint ReadCounter(nint counter, out uint counterStatus, out double value)
        {
            counterStatus = CounterStatus;
            value = Raw;
            return Step(PdhTemperatureFailure.ReadCounter);
        }
        public uint CloseQuery(nint query) { CloseCount++; return CloseStatus; }
    }
}
