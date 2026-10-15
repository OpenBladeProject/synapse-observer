using Microsoft.Win32;

namespace SynapseObserver.Core.Tests;

public sealed class MachineIdentityInventoryTests
{
    [Fact]
    public void CollectMachineIdentity_PreservesIndependentSourcesAndMismatches()
    {
        var result = WindowsInventorySource.CollectMachineIdentity(
            static (view, source) =>
            {
                string product = source == MachineIdentitySource.BiosRegistry
                    ? "Blade BIOS value"
                    : "Blade SystemInformation value";
                return (Identity(view, source, "Razer", product), true);
            },
            CancellationToken.None);

        Assert.Equal(CollectionState.Collected, result.State);
        Assert.Equal(4, result.Value.Count);
        Assert.Equal(2, result.Value.Count(item => item.Source == MachineIdentitySource.BiosRegistry));
        Assert.Equal(2, result.Value.Count(item => item.Source == MachineIdentitySource.SystemInformationRegistry));
        Assert.Contains(result.Value, static item => item.ProductName == "Blade BIOS value");
        Assert.Contains(result.Value, static item => item.ProductName == "Blade SystemInformation value");
    }

    [Fact]
    public void CollectMachineIdentity_MissingSourceIsPartialAndKeepsSuccessfulRows()
    {
        var result = WindowsInventorySource.CollectMachineIdentity(
            static (view, source) => source == MachineIdentitySource.BiosRegistry
                ? (Identity(view, source, "Razer", "Blade test"), true)
                : (null, false),
            CancellationToken.None);

        Assert.Equal(CollectionState.Partial, result.State);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, static item =>
            Assert.Equal(MachineIdentitySource.BiosRegistry, item.Source));
    }

    [Fact]
    public void CollectMachineIdentity_IncompleteSourceIsPartialAndPreservesRow()
    {
        var result = WindowsInventorySource.CollectMachineIdentity(
            static (view, source) => source == MachineIdentitySource.SystemInformationRegistry
                ? (Identity(view, source, null, "Blade test"), false)
                : (Identity(view, source, "Razer", "Blade test"), true),
            CancellationToken.None);

        Assert.Equal(CollectionState.Partial, result.State);
        Assert.Equal(4, result.Value.Count);
        Assert.Equal(2, result.Value.Count(item =>
            item.Source == MachineIdentitySource.SystemInformationRegistry && item.Manufacturer is null));
    }

    [Fact]
    public void Sanitize_PreservesFourIdentityRowsAndSourceProvenance()
    {
        MachineIdentity[] identities =
        [
            Identity(RegistryView.Registry64, MachineIdentitySource.BiosRegistry, "Razer", "Blade test"),
            Identity(RegistryView.Registry64, MachineIdentitySource.SystemInformationRegistry, "Razer", "Blade test"),
            Identity(RegistryView.Registry32, MachineIdentitySource.BiosRegistry, "Razer", "Blade test"),
            Identity(RegistryView.Registry32, MachineIdentitySource.SystemInformationRegistry, "Razer", "Blade test"),
        ];
        var inventory = new InventorySnapshot(
            InventorySnapshot.CurrentSchemaVersion,
            DateTimeOffset.UnixEpoch,
            SectionResult<IReadOnlyList<MachineIdentity>>.Collected(identities),
            SectionResult<PowerSourceEvidence?>.Absent(null),
            SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Absent([]),
            SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Absent([]));

        InventorySnapshot sanitized = InventorySanitizer.Sanitize(inventory);

        Assert.Equal(4, sanitized.MachineIdentity.Value.Count);
        Assert.Equal(
            identities.Select(static item => (item.RegistryView, item.Source)),
            sanitized.MachineIdentity.Value.Select(static item => (item.RegistryView, item.Source)));
    }

    private static MachineIdentity Identity(
        RegistryView view,
        MachineIdentitySource source,
        string? manufacturer,
        string? productName) =>
        new(
            view == RegistryView.Registry64 ? "Registry64" : "Registry32",
            manufacturer,
            productName,
            source == MachineIdentitySource.BiosRegistry ? "RZ09-test" : null,
            source == MachineIdentitySource.BiosRegistry ? "1.00" : null,
            source == MachineIdentitySource.BiosRegistry ? "01/01/2026" : null)
        {
            Source = source,
        };
}
