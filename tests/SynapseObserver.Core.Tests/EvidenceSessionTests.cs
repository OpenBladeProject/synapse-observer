using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class EvidenceSessionTests
{
    [Fact]
    public void AddObservation_PendingCall_DoesNotFabricateCompletion()
    {
        var session = SyntheticPreview.CreateSession();
        var result = session.AddObservation(CreateObservation(
            ObservationStatus.Pending,
            PayloadDisposition.RedactedUnproven,
            [0x00, 0xFF, 0x10]));

        var snapshot = session.Snapshot();

        Assert.Equal(EvidenceAddResult.Added, result);
        var observation = Assert.Single(snapshot.Observations);
        Assert.Equal(ObservationStatus.Pending, observation.Status);
        Assert.Equal(3, observation.BufferLength);
        Assert.Null(observation.PayloadHex);
        Assert.DoesNotContain(snapshot.Observations, item => item.Status == ObservationStatus.Succeeded);
    }

    [Fact]
    public void AddObservation_UnprovenPayload_DropsBytesWithoutDroppingShape()
    {
        var secret = Encoding.UTF8.GetBytes(@"C:\Users\alice\private-device-path");
        var session = SyntheticPreview.CreateSession();

        var result = session.AddObservation(CreateObservation(
            ObservationStatus.Failed,
            PayloadDisposition.ExcludedSensitive,
            secret));

        var observation = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(EvidenceAddResult.Added, result);
        Assert.Equal(secret.Length, observation.BufferLength);
        Assert.Equal(PayloadDisposition.ExcludedSensitive, observation.PayloadDisposition);
        Assert.Null(observation.PayloadHex);
    }

    [Fact]
    public void AddObservation_ProvenPayloadRequiresRazerVendor()
    {
        var session = SyntheticPreview.CreateSession();
        var observation = CreateObservation(
            ObservationStatus.Succeeded,
            PayloadDisposition.ProvenFeatureControl,
            [1, 2, 3]) with
        {
            VendorId = 0x1234,
        };

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(observation));
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(PayloadDisposition.RedactedUnproven, stored.PayloadDisposition);
        Assert.Null(stored.PayloadHex);
    }

    [Fact]
    public void AddObservation_OverLimitPayloadRetainsOnlyShape()
    {
        var payload = new byte[EvidenceLimits.MaxPayloadBytesPerObservation + 1];
        var inventory = SyntheticPreview.CreateInventory();
        var candidate = Assert.Single(inventory.RazerHidInterfaces.Value) with
        {
            ReportGeometry = new HidReportGeometry(91, 91, (ushort)payload.Length),
        };
        var session = new EvidenceSession(inventory with
        {
            RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([candidate]),
        });

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(CreateObservation(
            ObservationStatus.Succeeded,
            PayloadDisposition.ProvenFeatureControl,
            payload)));

        var observation = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(payload.Length, observation.BufferLength);
        Assert.Equal(PayloadDisposition.RedactedOverLimit, observation.PayloadDisposition);
        Assert.Null(observation.PayloadHex);
    }

    [Fact]
    public void AddObservation_ProvenMatchingFeaturePayloadIncludesHex()
    {
        var session = SyntheticPreview.CreateSession();
        var payload = FeatureReportTestData.Create(0x0D, 0x82, [0x01, 0x01, 0x00, 0x00]);

        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(CreateObservation(
            ObservationStatus.Succeeded,
            PayloadDisposition.ProvenFeatureControl,
            payload)));

        var observation = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(Convert.ToHexString(payload), observation.PayloadHex);
        Assert.Equal(PayloadDisposition.ProvenFeatureControl, observation.PayloadDisposition);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedThermalMode, observation.PayloadProjectionKind);
    }

    [Fact]
    public void AddObservation_MatchingFeatureScopeDoesNotClearUnknownPayload()
    {
        var session = SyntheticPreview.CreateSession();
        var payload = FeatureReportTestData.Create(
            0x0E,
            0x99,
            Encoding.ASCII.GetBytes("SERIAL-PRIVATE-123"));

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(CreateObservation(
            ObservationStatus.Succeeded,
            PayloadDisposition.ProvenFeatureControl,
            payload)));

        var observation = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(PayloadDisposition.ExcludedSensitive, observation.PayloadDisposition);
        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, observation.PayloadProjectionKind);
        Assert.Equal((byte)0x0E, Assert.IsType<FeatureReportHeaderHints>(observation.HeaderHints).CommandClass);
        Assert.Null(observation.PayloadHex);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(499.99)]
    public void AddObservation_InvalidCompletionTimestampIsRejected(double completedMilliseconds)
    {
        var session = SyntheticPreview.CreateSession();
        var observation = CreateObservation(
            ObservationStatus.Succeeded,
            PayloadDisposition.RedactedUnproven,
            []) with
        {
            CompletedRelativeMilliseconds = completedMilliseconds,
        };

        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddObservation(observation));
        Assert.Empty(session.Snapshot().Observations);
    }

    [Fact]
    public void AddObservation_CompletionTimestampIsCarriedToSnapshot()
    {
        var session = SyntheticPreview.CreateSession();
        var observation = CreateObservation(
            ObservationStatus.Succeeded,
            PayloadDisposition.RedactedUnproven,
            []) with
        {
            CompletedRelativeMilliseconds = 501.25,
        };

        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(observation));
        Assert.Equal(501.25, Assert.Single(session.Snapshot().Observations).CompletedRelativeMilliseconds);
    }

    [Fact]
    public void AddObservation_PendingReturnedBufferIsRetainedButRedacted()
    {
        var session = SyntheticPreview.CreateSession();
        var observation = CreateObservation(
            ObservationStatus.Pending,
            PayloadDisposition.ProvenFeatureControl,
            new byte[91]) with
        {
            Source = ObservationSource.ReturnedBuffer,
            Direction = ObservationDirection.Output,
        };

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(observation));
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(ObservationStatus.Pending, stored.Status);
        Assert.Null(stored.PayloadHex);
        Assert.Equal(PayloadDisposition.RedactedUnproven, stored.PayloadDisposition);
    }

    [Fact]
    public void AddMarker_RestorationCannotClaimObserverVerification()
    {
        var session = SyntheticPreview.CreateSession();

        var result = session.AddMarker(new EvidenceMarker(
            1000,
            EvidenceMarkerKind.UserReportedRestoration,
            "performance",
            "Restored setting",
            EvidenceProvenance.Observer,
            "Balanced"));

        Assert.Equal(EvidenceAddResult.RejectedInvalid, result);
        Assert.Equal(2, session.Snapshot().Markers.Count);
    }

    [Fact]
    public void AddMarker_RejectsPathShapedFreeText()
    {
        var session = SyntheticPreview.CreateSession();

        var result = session.AddMarker(new EvidenceMarker(
            1000,
            EvidenceMarkerKind.Failure,
            "performance",
            @"C:\Users\alice\failure.log",
            EvidenceProvenance.Observer));

        Assert.Equal(EvidenceAddResult.RejectedInvalid, result);
    }

    [Fact]
    public void AddMarker_EnforcesDocumentedCountBound()
    {
        var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        for (var index = 0; index < EvidenceLimits.MaxMarkers; index++)
        {
            Assert.Equal(EvidenceAddResult.Added, session.AddMarker(new EvidenceMarker(
                index,
                EvidenceMarkerKind.UserActionConfirmed,
                $"action-{index}",
                "User action",
                EvidenceProvenance.UserReported)));
        }

        Assert.Equal(EvidenceAddResult.RejectedLimit, session.AddMarker(new EvidenceMarker(
            EvidenceLimits.MaxMarkers,
            EvidenceMarkerKind.UserActionConfirmed,
            "action-over-limit",
            "User action",
            EvidenceProvenance.UserReported)));
        Assert.Equal(EvidenceLimits.MaxMarkers, session.Snapshot().Markers.Count);
        Assert.Equal(1, session.Snapshot().RejectedMarkerCount);
    }

    [Fact]
    public void Constructor_SanitizesCallerProvidedInventoryStrings()
    {
        var inventory = SyntheticPreview.CreateInventory();
        var process = Assert.Single(inventory.SynapseProcesses.Value) with
        {
            ProcessName = @"C:\Users\alice\Synapse.exe",
            Issue = @"failed at C:\Users\alice\module.dll",
            Modules =
            [
                new SynapseModuleEvidence(
                    @"C:\Users\alice\module.dll",
                    @"C:\private\version",
                    "not-a-hash",
                    CollectionState.Unavailable,
                    @"C:\private\failure"),
            ],
        };
        var session = new EvidenceSession(inventory with
        {
            SynapseProcesses = SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Partial(
                [process],
                @"C:\Users\alice\diagnostic.log"),
        });

        var sanitized = Assert.Single(session.Inventory.SynapseProcesses.Value);
        Assert.Equal("Synapse process", sanitized.ProcessName);
        Assert.Equal(
            "Some details were unavailable or reached a documented bound.",
            session.Inventory.SynapseProcesses.Issue);
        Assert.Null(sanitized.Issue);
        var module = Assert.Single(sanitized.Modules);
        Assert.Equal("module.dll", module.FileName);
        Assert.Null(module.FileVersion);
        Assert.Null(module.Sha256);
        Assert.Equal("This item was unavailable.", module.Issue);
    }

    private static ControlObservation CreateObservation(
        ObservationStatus status,
        PayloadDisposition payloadDisposition,
        byte[] payload) =>
        new(
            "op-1",
            500,
            ObservationSource.UserModeCall,
            "HidD_SetFeature",
            ObservationDirection.Input,
            status,
            null,
            status == ObservationStatus.Failed ? "access-denied" : null,
            0x1532,
            0xFFFF,
            "hid-preview-01",
            1,
            2,
            0,
            payload,
            payloadDisposition);
}
