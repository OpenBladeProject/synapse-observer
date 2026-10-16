namespace SynapseObserver.Core.Tests;

public sealed class SpecialKeyEvidenceTests
{
    [Fact]
    public void OrdinaryTypingIsRejectedEvenWhenCallerLabelsItSpecial()
    {
        using var session = CreateSession();
        var observation = Key(0x41);
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddSpecialKeyObservation(observation));
        Assert.Empty(session.Snapshot().SpecialKeyObservations);
    }

    [Fact]
    public void FnPExceptionRequiresExplicitPromptAndDoesNotProveFn()
    {
        using var session = CreateSession();
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddSpecialKeyObservation(Key(0x50)));
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyObservation(Key(0x50) with { ExpectedKey = SpecialKeyExpectedKey.FnP }));
        var item = Assert.Single(session.Snapshot().SpecialKeyObservations);
        Assert.False(item.IsFirmwareReadback);
        Assert.Equal(EvidenceProvenance.UserReported, item.ContextProvenance);
    }

    [Fact]
    public void UnknownMetadataHasBoundedGeometryAndCopiesRanges()
    {
        using var session = CreateSession();
        var ranges = new List<ChangedByteRange> { new(1, 2) };
        var item = Key(0x7C) with { Origin = SpecialKeyObservationOrigin.UnknownHidMetadata,
            Phase = SpecialKeyPhase.Unknown, VirtualKey = null, ReportLength = 8,
            ProbeEqualityLabel = "input-01", ChangedByteCount = 2, ChangedRanges = ranges };
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyObservation(item));
        ranges.Clear();
        Assert.Single(Assert.Single(session.Snapshot().SpecialKeyObservations).ChangedRanges!);
        Assert.Equal(EvidenceAddResult.RejectedInvalid, session.AddSpecialKeyObservation(item with { ChangedRanges = [new(7, 2)] }));
    }

    [Fact]
    public void ProbeSummariesCannotExportArbitraryDiagnosticTextAndSurviveDispose()
    {
        var session = CreateSession();
        var summary = new SpecialKeyProbeSummary(SpecialKeyExpectedKey.M1, SpecialKeyModeContext.Unknown,
            SpecialKeyProbeEndReason.Timeout, 0, 0, 0, @"C:\Users\alice\secret");
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyProbe(summary));
        using var snapshot = session.Snapshot();
        session.Dispose();
        Assert.DoesNotContain("alice", Assert.Single(snapshot.SpecialKeyProbes).Gap);
        Assert.Throws<ObjectDisposedException>(() => session.AddSpecialKeyObservation(Key(0x7C)));
    }

    [Fact]
    public void PerProbeLimitRemainsBounded()
    {
        using var session = CreateSession();
        for (int i = 0; i < 32; i++) Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyObservation(Key(0x7C)));
        Assert.Equal(EvidenceAddResult.RejectedLimit, session.AddSpecialKeyObservation(Key(0x7C)));
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyObservation(Key(0x7C) with { ProbeLabel = "probe-002" }));
    }

    [Theory]
    [InlineData(CollectionState.Collected)]
    [InlineData(CollectionState.Partial)]
    public void UndefinedDesktopMetadataSurvivesExportWithKnownGeometry(CollectionState state)
    {
        var inventory = SyntheticPreview.CreateInventory();
        var hid = inventory.RazerHidInterfaces.Value.Single() with
        { UsagePage = 1, Usage = 0, ReportGeometry = new(16, 0, 0), DescriptorState = state };
        using var session = new EvidenceSession(inventory with
        { RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([hid]) });
        var item = new SpecialKeyObservation(1, SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Unknown,
            hid.InterfaceLabel, SpecialKeyObservationOrigin.UnknownHidMetadata, SpecialKeyPhase.Unknown,
            ReportLength: 16, ProbeEqualityLabel: "input-01", ChangedRanges: []);
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyObservation(item));
        using var snapshot = session.Snapshot();
        var exported = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(snapshot).SpecialKeyObservations);
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata, exported.Observation.Origin);
        Assert.Null(exported.Observation.ReviewedSignal);
        Assert.False(exported.Observation.IsFirmwareReadback);
        Assert.False(SpecialKeyInputCollection.IsEligible(0x1532, state, 1, 0, null));
        Assert.False(SpecialKeyInputCollection.IsEligible(0x1532, CollectionState.Unavailable, 1, 0, new(16, 0, 0)));
        Assert.False(SpecialKeyInputCollection.IsEligible(0x1532, state, 1, 2, new(16, 0, 0)));
    }

    [Theory]
    [InlineData(SpecialKeyProbeEndReason.Timeout, 0, "NoMatchingInput")]
    [InlineData(SpecialKeyProbeEndReason.Timeout, 1, "RejectedInput")]
    [InlineData(SpecialKeyProbeEndReason.Unavailable, 0, "InputUnavailable")]
    [InlineData(SpecialKeyProbeEndReason.FocusLost, 0, "InputProbeInterrupted")]
    [InlineData(SpecialKeyProbeEndReason.Cancelled, 0, "InputProbeInterrupted")]
    public void ProbeQualityDistinguishesNoSignalFromCollectionFailure(SpecialKeyProbeEndReason reason, int rejected, string expectedGap)
    {
        using var session = CreateSession();
        Assert.Equal(EvidenceAddResult.Added, session.AddSpecialKeyProbe(new(
            SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Unknown, reason, 0, 0, rejected, "")));
        using var snapshot = session.Snapshot();
        var gaps = ContributionDraftBuilder.CreateQualityReceipt(snapshot).Gaps.Where(gap => gap.Area == "specialKeys.Fn");
        Assert.Equal(expectedGap, Assert.Single(gaps).Code);
    }

    private static EvidenceSession CreateSession()
    {
        var inventory = SyntheticPreview.CreateInventory();
        var hid = inventory.RazerHidInterfaces.Value.Single() with { UsagePage = 1, Usage = 6 };
        return new EvidenceSession(inventory with { RazerHidInterfaces = SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Collected([hid]) });
    }

    private static SpecialKeyObservation Key(ushort key) => new(1, SpecialKeyExpectedKey.M1,
        SpecialKeyModeContext.Unknown, "hid-preview-01", SpecialKeyObservationOrigin.WindowsRawInput, SpecialKeyPhase.KeyDown, key);
}
