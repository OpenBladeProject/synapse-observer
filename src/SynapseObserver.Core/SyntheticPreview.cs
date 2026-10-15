namespace SynapseObserver.Core;

public static class SyntheticPreview
{
    public static InventorySnapshot CreateInventory(DateTimeOffset? collectedAtUtc = null)
    {
        IReadOnlyList<MachineIdentity> identity =
        [
            new MachineIdentity(
                "SyntheticPreview",
                "Razer",
                "Blade preview laptop",
                "PREVIEW-SKU",
                "PREVIEW-BIOS",
                "01/01/2026"),
        ];
        IReadOnlyList<HidInterfaceEvidence> hid =
        [
            new HidInterfaceEvidence(
                "hid-preview-01",
                0x1532,
                0xFFFF,
                1,
                0x0001,
                0x0002,
                new HidReportGeometry(91, 91, 91),
                CollectionState.Collected,
                null),
        ];
        IReadOnlyList<SynapseProcessEvidence> processes =
        [
            new SynapseProcessEvidence(
                "SynapsePreview",
                "process-preview-01",
                "0.0-preview",
                [new SynapseModuleEvidence("preview.dll", "0.0-preview", new string('0', 64), CollectionState.Collected, null)],
                CollectionState.Collected,
                null),
        ];

        return new InventorySnapshot(
            InventorySnapshot.CurrentSchemaVersion,
            collectedAtUtc ?? DateTimeOffset.UnixEpoch,
            SectionResult<IReadOnlyList<MachineIdentity>>.Collected(identity),
            SectionResult<PowerSourceEvidence?>.Collected(new PowerSourceEvidence(PowerSourceKind.Ac, 80, true)),
            SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected(hid),
            SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Collected(processes));
    }

    public static EvidenceSession CreateSession(DateTimeOffset? startedAtUtc = null)
    {
        var started = startedAtUtc ?? DateTimeOffset.UnixEpoch;
        var session = new EvidenceSession(CreateInventory(started), started);
        session.AddMarker(new EvidenceMarker(
            0,
            EvidenceMarkerKind.SessionStarted,
            "preview-performance",
            "Performance mode",
            EvidenceProvenance.Collector));
        session.AddMarker(new EvidenceMarker(
            100,
            EvidenceMarkerKind.UserReportedStartingValue,
            "preview-performance",
            "Starting value",
            EvidenceProvenance.UserReported,
            "Balanced"));
        session.SetCoverage(new CoverageItem(
            "preview-performance",
            "Performance mode",
            CoverageState.Pending,
            CoverageReason.None));
        return session;
    }
}
