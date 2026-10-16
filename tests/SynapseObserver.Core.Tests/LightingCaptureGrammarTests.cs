using System.IO.Compression;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class LightingCaptureGrammarTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(17)]
    public async Task RgbRowsSurviveSpoolingAndZipExportWithTheirActualGeometry(int columns)
    {
        var payload = Row(columns);
        var bytes = FeatureReportTestData.Create(3, 0x0B, payload);
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(Observation(bytes)));
        using var snapshot = session.Snapshot();
        var stored = Assert.Single(snapshot.Observations);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedMatrixRow, stored.PayloadProjectionKind);
        Assert.Equal(Convert.ToHexString(bytes), stored.PayloadHex);
        Assert.True(stored.CompletePayloadRetained);
        var destination = Path.Combine(Path.GetTempPath(), $"observer-lighting-{Guid.NewGuid():N}.zip");
        try
        {
            await EvidenceBundleWriter.WriteAsync(session, destination);
            using var zip = ZipFile.OpenRead(destination);
            using var manifest = await JsonDocument.ParseAsync(zip.GetEntry("manifest.json")!.Open());
            var exported = manifest.RootElement.GetProperty("session").GetProperty("observations")[0];
            Assert.Equal(Convert.ToHexString(bytes), exported.GetProperty("payloadHex").GetString());
        }
        finally { File.Delete(destination); }
    }

    [Theory]
    [InlineData("row")]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("selector")]
    [InlineData("length")]
    [InlineData("checksum")]
    [InlineData("padding")]
    [InlineData("failed")]
    [InlineData("input-api")]
    public void RgbRowRejectsUnreviewedShapesAndContexts(string defect)
    {
        var payload = Row(16);
        switch (defect)
        {
            case "row": payload[1] = 6; break;
            case "start": payload[2] = 1; break;
            case "end": payload[3] = 16; break;
            case "selector": payload[0] = 0x41; break;
            case "length": payload = payload[..^1]; break;
        }
        var bytes = FeatureReportTestData.Create(3, 0x0B, payload);
        if (defect == "checksum") bytes[89] ^= 1;
        if (defect == "padding")
        {
            bytes[70] = 42;
            bytes[89] ^= 42;
        }
        var observation = Observation(bytes);
        if (defect == "failed") observation = observation with { Status = ObservationStatus.Failed };
        if (defect == "input-api") observation = observation with { Api = "ReadFile" };
        Assert.Null(FeaturePayloadPrivacy.Classify(observation).PayloadHex);
    }

    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 1, 2 })]
    [InlineData(new byte[] { 2, 3, 255, 0, 42 })]
    [InlineData(new byte[] { 3, 1, 255, 0, 42 })]
    [InlineData(new byte[] { 3, 2, 255, 0, 42, 1, 2, 3 })]
    [InlineData(new byte[] { 3, 3 })]
    [InlineData(new byte[] { 4 })]
    [InlineData(new byte[] { 6, 255, 0, 42 })]
    public void UsedLengthAndPaddedVendorEffectsRetainOnlyReviewedFields(byte[] payload)
    {
        foreach (var length in new[] { payload.Length, 80 })
        {
            var padded = new byte[length];
            payload.CopyTo(padded, 0);
            var projection = FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(3, 0x0A, padded)));
            Assert.Equal(FeaturePayloadProjectionKind.ReviewedHardwareLightingEffect, projection.Kind);
            Assert.NotNull(projection.PayloadHex);
        }
    }

    [Theory]
    [InlineData(new byte[] { 1, 3 })]
    [InlineData(new byte[] { 3, 1 })]
    [InlineData(new byte[] { 3, 2, 1, 2, 3 })]
    [InlineData(new byte[] { 6, 1, 2, 3, 4 })]
    [InlineData(new byte[] { 42, 1, 2, 3 })]
    public void UnknownAndTruncatedEffectBodiesRemainPrivate(byte[] payload) =>
        Assert.Null(FeaturePayloadPrivacy.Classify(Observation(FeatureReportTestData.Create(3, 0x0A, payload))).PayloadHex);

    [Theory]
    [InlineData(new byte[] { 3, 3 }, "keyboardLighting.effects.breathing.base")]
    [InlineData(new byte[] { 2, 3, 255, 0, 42 }, "keyboardLighting.effects.reactive.base")]
    [InlineData(new byte[] { 1, 2 }, "keyboardLighting.effects.wave.rightDirection")]
    public void ShortEffectsContributeCoverageOnlyInsideCompletedUserSequence(byte[] payload, string capability)
    {
        using var session = new EvidenceSession(SyntheticPreview.CreateInventory());
        session.AddMarker(new(1, EvidenceMarkerKind.UserReportedStartingValue, "lighting", "Start", EvidenceProvenance.UserReported, "Off"));
        session.AddObservation(Observation(FeatureReportTestData.Create(3, 0x0A, payload)));
        using (var incomplete = session.Snapshot())
            Assert.Equal(ContributionCoverageStatus.NotInvestigated, ContributionDraftBuilder.CreateCoverageDraft(incomplete)
                .Coverage.Single(item => item.Capability == capability).Status);
        session.AddMarker(new(12, EvidenceMarkerKind.UserReportedTargetValue, "lighting", "Changed", EvidenceProvenance.UserReported, "Effect"));
        session.AddMarker(new(13, EvidenceMarkerKind.UserActionConfirmed, "lighting", "Marked", EvidenceProvenance.UserReported));
        session.AddMarker(new(14, EvidenceMarkerKind.UserReportedRestoration, "lighting", "Restored", EvidenceProvenance.UserReported, "Off"));
        using var complete = session.Snapshot();
        Assert.Equal(ContributionCoverageStatus.Captured, ContributionDraftBuilder.CreateCoverageDraft(complete)
            .Coverage.Single(item => item.Capability == capability).Status);
    }

    private static byte[] Row(int columns)
    {
        var payload = new byte[4 + columns * 3];
        payload[0] = 0xFF;
        payload[1] = 5;
        payload[3] = (byte)(columns - 1);
        for (var i = 4; i < payload.Length; i++) payload[i] = (byte)(i * 3);
        return payload;
    }

    private static ControlObservation Observation(byte[] bytes) => new("lighting-op", 10,
        ObservationSource.UserModeCall, "HidD_SetFeature", ObservationDirection.Input,
        ObservationStatus.Succeeded, 0, null, 0x1532, 0xFFFF, "hid-preview-01", 1, 2,
        0, bytes, PayloadDisposition.ProvenFeatureControl, 11);
}
