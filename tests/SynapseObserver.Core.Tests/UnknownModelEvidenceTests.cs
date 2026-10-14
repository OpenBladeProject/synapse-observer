namespace SynapseObserver.Core.Tests;
public sealed class UnknownModelEvidenceTests
{
    [Theory]
    [InlineData("Razer Blade Stealth 13", 0xFFFF, 37)]
    [InlineData("Razer Blade 18", 0xFFFE, 375)]
    [InlineData("Razer Blade Future Model", 0xFFFD, 512)]
    public void UnfamiliarModelsAndNonstandardFeatureReportsKeepEvidence(string product, ushort pid, ushort length)
    {
        var preview = SyntheticPreview.CreateInventory();
        var hid = preview.RazerHidInterfaces.Value.Single() with { ProductId = pid, ReportGeometry = new(0, 0, length) };
        var inventory = preview with
        {
            MachineIdentity = SectionResult<IReadOnlyList<MachineIdentity>>.Collected([new("Registry64", "Razer", product, null, null, null)]),
            RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([hid]),
            SynapseProcesses = SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Absent([]),
        };
        var session = new EvidenceSession(inventory);
        var result = session.AddObservation(new("op-unknown", 1, ObservationSource.UserModeCall, "HidD_SetFeature", ObservationDirection.Input,
            ObservationStatus.Succeeded, 0, null, 0x1532, pid, hid.InterfaceLabel, hid.UsagePage, hid.Usage, 0,
            new byte[length], PayloadDisposition.ProvenFeatureControl, 2));
        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, result);
        var snapshot = session.Snapshot();
        Assert.Equal(product, snapshot.Inventory.MachineIdentity.Value.Single().ProductName);
        Assert.Equal(pid, snapshot.Inventory.RazerHidInterfaces.Value.Single().ProductId);
        var observation = Assert.Single(snapshot.Observations);
        Assert.Equal(length, observation.BufferLength);
        Assert.Equal(pid, observation.ProductId);
        Assert.Null(observation.PayloadHex);
        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, observation.PayloadProjectionKind);
        Assert.Equal(0, snapshot.RejectedObservationCount);
    }
}
