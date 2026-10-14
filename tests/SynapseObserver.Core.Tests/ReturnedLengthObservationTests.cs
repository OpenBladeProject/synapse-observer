using System.IO.Compression;
using System.Text.Json;

namespace SynapseObserver.Core.Tests;

public sealed class ReturnedLengthObservationTests
{
    [Fact]
    public void SuccessfulHidDResponse_RetainsReviewedBytesAndReportsUnknownLength()
    {
        using var session = SyntheticPreview.CreateSession();
        var bytes = ReviewedResponse();

        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(Observation(bytes)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.True(stored.ReturnedLengthUnknown);
        Assert.Null(stored.ReturnedBytes);
        Assert.Null(stored.ErrorCategory);
        Assert.Equal(ObservationStatus.Succeeded, stored.Status);
        Assert.Equal(FeaturePayloadProjectionKind.ReviewedThermalMode, stored.PayloadProjectionKind);
        Assert.Equal(Convert.ToHexString(bytes), stored.PayloadHex);

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        var caveat = Assert.Single(gaps, item => item.Code == "ReturnedLengthNotProvided");
        Assert.Contains("succeeded", caveat.Detail, StringComparison.Ordinal);
        Assert.Contains("does not provide an actual returned-byte count", caveat.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(gaps, item => item.Code is "ObservedApiFailure" or "CompletionNotObserved" or "IncompleteCall");
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("unknown")]
    public void UnknownLength_DoesNotWeakenReviewedPayloadGrammar(string payloadKind)
    {
        var bytes = payloadKind switch
        {
            "malformed" => ReviewedResponse(),
            "unknown" => FeatureReportTestData.Create(
                0x0E, 0x99, [0x01, 0x02, 0x03], ObservationDirection.Output),
            _ => throw new ArgumentOutOfRangeException(nameof(payloadKind)),
        };
        if (payloadKind == "malformed")
        {
            bytes[89] ^= 0x01;
        }

        using var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(Observation(bytes)));

        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.True(stored.ReturnedLengthUnknown);
        Assert.Equal(FeaturePayloadProjectionKind.MetadataOnly, stored.PayloadProjectionKind);
        Assert.Null(stored.PayloadHex);
    }

    [Fact]
    public async Task Export_PreservesUnknownLengthFlagWithoutInventingReturnedBytes()
    {
        using var session = SyntheticPreview.CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(Observation(ReviewedResponse())));
        var directory = Path.Combine(Path.GetTempPath(), $"synapse-observer-returned-length-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "evidence.zip");
            await EvidenceBundleWriter.WriteAsync(session, destination);

            using var archive = ZipFile.OpenRead(destination);
            var entry = Assert.Single(archive.Entries, item => item.FullName == "manifest.json");
            await using var stream = entry.Open();
            using var manifest = await JsonDocument.ParseAsync(stream);
            var observation = Assert.Single(manifest.RootElement
                .GetProperty("session")
                .GetProperty("observations")
                .EnumerateArray());
            Assert.True(observation.GetProperty("returnedLengthUnknown").GetBoolean());
            Assert.Equal(JsonValueKind.Null, observation.GetProperty("returnedBytes").ValueKind);
            Assert.Equal(JsonValueKind.Null, observation.GetProperty("errorCategory").ValueKind);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("api")]
    [InlineData("direction")]
    [InlineData("status")]
    [InlineData("error")]
    [InlineData("returned-bytes")]
    public void UnknownLengthFlag_RejectsContradictoryCallMetadata(string defect)
    {
        var observation = Observation(ReviewedResponse());
        observation = defect switch
        {
            "source" => observation with { Source = ObservationSource.UserModeCall },
            "api" => observation with { Api = "DeviceIoControl.HID_GET_FEATURE" },
            "direction" => observation with { Direction = ObservationDirection.Input },
            "status" => observation with { Status = ObservationStatus.Failed },
            "error" => observation with { ErrorCategory = "capture-failed" },
            "returned-bytes" => observation with { ReturnedBytes = 91 },
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        using var session = SyntheticPreview.CreateSession();

        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddObservation(observation));
        Assert.Empty(session.Snapshot().Observations);
    }

    [Fact]
    public void FailedCall_PreservesFailureAndPrivacyMetadataWithSpecificQualityGap()
    {
        using var session = SyntheticPreview.CreateSession();
        var failed = Observation(FeatureReportTestData.Create(
            0x0D, 0x82, [0x01, 0x01, 0x00, 0x00])) with
        {
            Source = ObservationSource.UserModeCall,
            Direction = ObservationDirection.Input,
            Status = ObservationStatus.Failed,
            NativeStatus = 5,
            ErrorCategory = "access-denied",
            CompletedRelativeMilliseconds = 11,
            ReturnedLengthUnknown = false,
        };

        Assert.Equal(EvidenceAddResult.AddedPayloadRedacted, session.AddObservation(failed));
        var stored = Assert.Single(session.Snapshot().Observations);
        Assert.Equal(ObservationStatus.Failed, stored.Status);
        Assert.Equal(5, stored.NativeStatus);
        Assert.Equal("access-denied", stored.ErrorCategory);
        Assert.False(stored.ReturnedLengthUnknown);
        Assert.Null(stored.PayloadHex);

        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "ObservedApiFailure");
        Assert.DoesNotContain(gaps, item => item.Code is "CompletionNotObserved" or "ReturnedLengthNotProvided" or "IncompleteCall");
    }

    [Theory]
    [InlineData(ObservationStatus.Pending)]
    [InlineData(ObservationStatus.Cancelled)]
    public void CallsWithoutObservedCompletion_ReportSpecificQualityGap(ObservationStatus status)
    {
        using var session = SyntheticPreview.CreateSession();
        var observation = Observation([]) with
        {
            Source = ObservationSource.UserModeCall,
            Direction = ObservationDirection.Input,
            Status = status,
            NativeStatus = null,
            CompletedRelativeMilliseconds = null,
            ReturnedLengthUnknown = false,
            PayloadDisposition = PayloadDisposition.RedactedUnproven,
        };

        Assert.Equal(EvidenceAddResult.Added, session.AddObservation(observation));
        var gaps = ContributionDraftBuilder.CreateQualityReceipt(session.Snapshot()).Gaps;
        Assert.Contains(gaps, item => item.Code == "CompletionNotObserved");
        Assert.DoesNotContain(gaps, item => item.Code is "ObservedApiFailure" or "IncompleteCall");
    }

    private static ControlObservation Observation(byte[] bytes) => new(
        "op-response",
        10,
        ObservationSource.ReturnedBuffer,
        "HidD_GetFeature",
        ObservationDirection.Output,
        ObservationStatus.Succeeded,
        0,
        null,
        0x1532,
        0xFFFF,
        "hid-preview-01",
        1,
        2,
        0,
        bytes,
        PayloadDisposition.ProvenFeatureControl,
        11,
        91,
        91,
        null)
    {
        ReturnedLengthUnknown = true,
    };

    private static byte[] ReviewedResponse() => FeatureReportTestData.Create(
        0x0D,
        0x82,
        [0x01, 0x01, 0x05, 0x00],
        ObservationDirection.Output);
}
