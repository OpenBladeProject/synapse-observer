using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class InventorySupportTests
{
    [Fact]
    public void Sanitize_RemovesPathsIdentifiersAndBoundsCollections()
    {
        var support = new SupportInventorySnapshot(
            SectionResult<IReadOnlyList<HidDescriptorSupportEvidence>>.Collected(
            [
                new HidDescriptorSupportEvidence(
                    "hid-safe", "device-safe", 0x1532, 0x02e0, 0x01, 0x02,
                    64, 90, 91, 1, 2, 3, 4, 5, 6),
                new HidDescriptorSupportEvidence(
                    @"HID\VID_1532&PID_02E0\RAW-SERIAL", "device-safe", 0x1532, 0x02e0, null, null,
                    64, 90, 91, 0, 0, 0, 0, 0, 0),
            ]),
            SectionResult<IReadOnlyList<DeviceDriverEvidence>>.Partial(
            [
                new DeviceDriverEvidence(
                    "device-safe",
                    @"C:\Users\person\private.txt",
                    "Razer Inc.",
                    "1.2.3.4",
                    @"C:\Windows\INF\oem42.inf",
                    @"bad\service",
                    ["filter-safe", @"bad\filter"],
                    [],
                    [new DriverFileEvidence(
                        "service-safe",
                        @"C:\Windows\System32\drivers\rzdev.sys",
                        "1.2.3.4",
                        new string('a', 64),
                        FileSignatureState.Trusted)]),
            ],
            @"C:\Users\person\collector-error.txt"),
            SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>>.Collected(
            [
                new ExecutableModuleProvenanceEvidence(
                    "process-safe",
                    @"C:\Program Files\Razer\Synapse.exe",
                    "1.2.3.4",
                    new string('b', 64),
                    FileSignatureState.Trusted,
                    true),
            ]),
            SectionResult<IReadOnlyList<GraphicsAdapterEvidence>>.Collected(
                [new GraphicsAdapterEvidence("gpu-safe", "Graphics", "Vendor", "1.0")]),
            SectionResult<IReadOnlyList<DisplayEvidence>>.Collected(
            [
                new DisplayEvidence(
                    "display-safe",
                    "Internal Display",
                    true,
                    new DisplayModeEvidence(2560, 1600, 32, 240),
                    Enumerable.Range(0, 200)
                        .Select(static index => new DisplayModeEvidence(1920, 1080, 32, index + 1))
                        .ToArray()),
            ]),
            SectionResult<IReadOnlyList<BrightnessEvidence>>.Collected(
                [new BrightnessEvidence("display-safe", 50, Enumerable.Range(0, 200).Select(static i => (byte)i).ToArray())]),
            SectionResult<IReadOnlyList<ThermalZoneEvidence>>.Collected(
                [new ThermalZoneEvidence("thermal-safe", 3000)]),
            SectionResult<IReadOnlyList<LampArrayEvidence>>.Unavailable([], @"C:\private\lamp.txt"),
            new SectionResult<NotCollectedEvidence>(
                CollectionState.Skipped,
                new NotCollectedEvidence("EmbeddedControllerOrMcu", "NotCollectedUncapturedCommand"),
                @"C:\private\ec.txt"),
            [new SupportCoverageGap("LampArrayLayout", CollectionState.Unavailable, "NoSupportedZeroCommandWindowsApi")])
        {
            SystemPlatform = SectionResult<SystemPlatformEvidence?>.Collected(new SystemPlatformEvidence(
                @"C:\private\Windows 11",
                "24H2",
                "26100",
                1234,
                "X64",
                [new ProcessorEvidence("cpu-safe", @"C:\private\processor", "GenuineIntel")])),
        };

        var sanitized = SupportInventorySanitizer.Sanitize(support);
        var json = JsonSerializer.Serialize(sanitized);

        Assert.Single(sanitized.HidDescriptors.Value);
        Assert.Null(Assert.Single(sanitized.DeviceDrivers.Value).DeviceName);
        Assert.Equal("oem42.inf", sanitized.DeviceDrivers.Value[0].InfName);
        Assert.Null(sanitized.DeviceDrivers.Value[0].ServiceName);
        Assert.Equal(new[] { "filter-safe" }, sanitized.DeviceDrivers.Value[0].UpperFilters);
        Assert.Equal("rzdev.sys", Assert.Single(sanitized.DeviceDrivers.Value[0].Files).FileName);
        Assert.Equal("Synapse.exe", Assert.Single(sanitized.ExecutableModules.Value).FileName);
        Assert.Equal(128, Assert.Single(sanitized.Displays.Value).AvailableModes.Count);
        Assert.Equal(101, Assert.Single(sanitized.Brightness.Value).AvailableLevels.Count);
        Assert.DoesNotContain("RAW-SERIAL", json, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\\\", json, StringComparison.Ordinal);
        Assert.DoesNotContain("person", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(sanitized.SystemPlatform.Value?.WindowsProductName);
        Assert.Null(Assert.Single(sanitized.SystemPlatform.Value!.Processors).Name);
    }

    [Fact]
    public async Task CollectAsync_SupportFailureDoesNotDiscardBaseInventory()
    {
        var snapshot = await new InventoryCollector(new ThrowingSupportSource()).CollectAsync();

        Assert.Equal(CollectionState.Absent, snapshot.MachineIdentity.State);
        Assert.Equal(CollectionState.Unavailable, snapshot.Support.HidDescriptors.State);
        Assert.Equal(CollectionState.Unavailable, snapshot.Support.DeviceDrivers.State);
        Assert.Equal(CollectionState.Unavailable, snapshot.Support.ExecutableModules.State);
        Assert.Contains(
            snapshot.Support.CoverageGaps,
            static gap => gap.Area == "SupportInventory" && gap.State == CollectionState.Unavailable);
    }

    [Fact]
    public void CollectIndependentSection_ContainsFailureAndAllowsLaterSection()
    {
        var failed = WindowsInventorySource.CollectIndependentSection<IReadOnlyList<string>>(
            static () => throw new NullReferenceException("Synthetic WMI row failure."),
            [],
            "Thermal metadata unavailable.",
            CancellationToken.None);
        var later = WindowsInventorySource.CollectIndependentSection(
            static () => SectionResult<IReadOnlyList<string>>.Collected(["display"]),
            Array.Empty<string>(),
            "Display metadata unavailable.",
            CancellationToken.None);

        Assert.Equal(CollectionState.Unavailable, failed.State);
        Assert.Empty(failed.Value);
        Assert.Equal("Thermal metadata unavailable.", failed.Issue);
        Assert.Equal(CollectionState.Collected, later.State);
        Assert.Equal("display", Assert.Single(later.Value));
    }

    [Fact]
    public void Empty_ReportsCommandAndLampArrayGapsExplicitly()
    {
        var support = SupportInventorySnapshot.Empty;

        Assert.Equal(CollectionState.Absent, support.LampArrays.State);
        Assert.Equal(CollectionState.Skipped, support.EmbeddedController.State);
        Assert.Equal("NotCollectedUncapturedCommand", support.EmbeddedController.Value.Reason);
        Assert.Contains(support.CoverageGaps, static gap => gap.Area == "LampArrayLayout");
        Assert.Contains(support.CoverageGaps, static gap => gap.Area == "EmbeddedControllerOrMcu");
    }

    [Fact]
    public void ResolveServiceImagePath_StripsArgumentsWithoutExportingThem()
    {
        var resolved = WindowsInventorySource.ResolveServiceImagePath(
            @"\SystemRoot\System32\drivers\example.sys -ignored");

        Assert.NotNull(resolved);
        Assert.EndsWith(@"System32\drivers\example.sys", resolved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignored", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Razer Blade 16", true)]
    [InlineData("Razer Blade", true)]
    [InlineData("Razer Basilisk V3", false)]
    [InlineData("USB Composite Device", false)]
    [InlineData(null, false)]
    public void IsBladeName_ScopesSupportDriversToLaptopAncestry(string? name, bool expected)
    {
        Assert.Equal(expected, WindowsInventorySource.IsBladeName(name));
    }

    [Theory]
    [InlineData(@"DISPLAY\BOE0A55\4&private-serial_0", "BOE0A55")]
    [InlineData(@"MONITOR\BOE0A55\private-serial", "BOE0A55")]
    [InlineData(@"DISPLAY\BAD.VALUE\private", null)]
    public void GetDisplayModelKey_UsesNonSerialModelComponent(string identity, string? expected)
    {
        Assert.Equal(expected, WindowsInventorySource.GetDisplayModelKey(identity));
    }

    [Fact]
    public void Sanitize_BoundsLampGeometryAndVirtualKeyMappings()
    {
        var lamps = Enumerable.Range(0, 4_100)
            .Select(static index => new LampPositionEvidence(
                index,
                index % 10,
                (index / 10) % 10,
                0,
                "Control",
                256,
                256,
                256,
                256,
                100))
            .ToArray();
        var mappings = Enumerable.Range(0, 300)
            .Select(index => new LampVirtualKeyMappingEvidence(
                $"Key{index}",
                Enumerable.Range(0, 600).Append(-1).Append(5_000).ToArray()))
            .ToArray();
        var evidence = new LampArrayEvidence(
            "lamp-safe",
            5_000,
            500,
            500,
            mappings.Select(static mapping => mapping.Key).ToArray())
        {
            VendorId = 0x1532,
            ProductId = 0x02c6,
            Kind = "Keyboard",
            BrightnessLevel = 2,
            Lamps = lamps,
            VirtualKeys = mappings,
        };
        var support = SupportInventorySnapshot.Empty with
        {
            LampArrays = SectionResult<IReadOnlyList<LampArrayEvidence>>.Collected([evidence]),
        };

        var sanitized = SupportInventorySanitizer.Sanitize(support);
        var actual = Assert.Single(sanitized.LampArrays.Value);

        Assert.Null(actual.LampCount);
        Assert.Null(actual.RowCount);
        Assert.Null(actual.ColumnCount);
        Assert.Null(actual.BrightnessLevel);
        Assert.Equal(4_096, actual.Lamps.Count);
        Assert.Equal(256, actual.VirtualKeys.Count);
        Assert.All(actual.VirtualKeys, static mapping =>
        {
            Assert.Equal(512, mapping.Indices.Count);
            Assert.All(mapping.Indices, static index => Assert.InRange(index, 0, 4_095));
        });
    }

    [Fact]
    public void DisplayAndPciHelpers_KeepOnlyNonSerialIdentityFields()
    {
        const ushort del = (4 << 10) | (5 << 5) | 12;
        var pci = WindowsInventorySource.ParsePciIdentity(
            @"PCI\VEN_8086&DEV_A7A0&SUBSYS_72708086&REV_04\private-instance");

        Assert.Equal("DEL", WindowsInventorySource.DecodeEdidManufacturer(del));
        Assert.Equal((ushort)0x8086, pci.VendorId);
        Assert.Equal((ushort)0xA7A0, pci.DeviceId);
        Assert.Equal((ushort)0x8086, pci.SubsystemVendorId);
        Assert.Equal((ushort)0x7270, pci.SubsystemId);
        Assert.True(WindowsInventorySource.IsInternalOutputTechnology(11));
        Assert.Equal("Hdmi", WindowsInventorySource.ConnectionKind(5));
    }

    [Fact]
    public void CountDistinctLampCoordinates_RoundsNoiseAndHonorsLayoutBound()
    {
        Assert.Equal(2, WindowsInventorySource.CountDistinctLampCoordinates([0, 0.00001f, 1, 1.00001f]));
        Assert.Null(WindowsInventorySource.CountDistinctLampCoordinates(Enumerable.Range(0, 257).Select(static value => (float)value)));
    }

    [Fact]
    public void CollectBoundedDisplayModes_DuplicatesDoNotConsumeUniqueModeLimit()
    {
        var result = WindowsInventorySource.CollectBoundedDisplayModes(index => index < 300
            ? new DisplayModeEvidence(1920, 1080, 32, index % 2 == 0 ? 60 : 120)
            : null);

        Assert.False(result.Truncated);
        Assert.Equal(2, result.Modes.Count);
    }

    [Fact]
    public void CollectBoundedDisplayModes_ReportsUniqueAndRawClipping()
    {
        var unique = WindowsInventorySource.CollectBoundedDisplayModes(index => index < 130
            ? new DisplayModeEvidence(index + 1, 1080, 32, 60)
            : null);
        var raw = WindowsInventorySource.CollectBoundedDisplayModes(index => index <= 4096
            ? new DisplayModeEvidence(1920, 1080, 32, 60)
            : null);

        Assert.True(unique.Truncated);
        Assert.Equal(128, unique.Modes.Count);
        Assert.True(raw.Truncated);
        Assert.Single(raw.Modes);
    }

    private sealed class ThrowingSupportSource : IInventorySource
    {
        public ValueTask<SectionResult<IReadOnlyList<MachineIdentity>>> CollectMachineIdentityAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<IReadOnlyList<MachineIdentity>>.Absent([]));

        public ValueTask<SectionResult<PowerSourceEvidence?>> CollectPowerSourceAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<PowerSourceEvidence?>.Absent(null));

        public ValueTask<SectionResult<IReadOnlyList<HidInterfaceEvidence>>> CollectRazerHidInterfacesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Absent([]));

        public ValueTask<SectionResult<IReadOnlyList<SynapseProcessEvidence>>> CollectSynapseProcessesAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Absent([]));

        public ValueTask<SupportInventorySnapshot> CollectSupportInventoryAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Synthetic support inventory failure.");
    }
}
