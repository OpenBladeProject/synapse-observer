using System.Buffers.Binary;
using System.Text.Json;
using SynapseObserver.App;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class SpecialKeyProbeTests
{
    private static LocalHidCandidate Candidate(ushort pid = 0xFFFF, string model = "Razer Blade Unknown", ushort page = 1,
        ushort usage = 6, ushort length = 8, string root = "private-root", string label = "hid-01") =>
        new("private-device-path", label, model, 0x1532, pid, null, page, usage, new(length, 0, 0),
            CollectionState.Collected, null, null, null, null, root, "private-hardware-id", model);

    [Fact]
    public void LaterDescriptorScanMustMatchEligibleExportedInventory()
    {
        var candidate = Candidate(page: 1, usage: 0, length: 16);
        var saved = new HidInterfaceEvidence(candidate.InterfaceLabel, candidate.VendorId, candidate.ProductId,
            candidate.VersionNumber, candidate.UsagePage, candidate.Usage, candidate.ReportGeometry,
            CollectionState.Collected, null);
        Assert.True(CollectorViewModel.CanRetainSpecialKeyInput(candidate, [saved]));
        Assert.True(CollectorViewModel.CanRetainSpecialKeyInput(candidate, [saved with { DescriptorState = CollectionState.Partial }]));
        Assert.False(CollectorViewModel.CanRetainSpecialKeyInput(candidate, [saved with
            { DescriptorState = CollectionState.Partial, UsagePage = null, Usage = null, ReportGeometry = null }]));
        Assert.False(CollectorViewModel.CanRetainSpecialKeyInput(candidate, [saved with { ReportGeometry = new(8, 0, 0) }]));
        Assert.False(CollectorViewModel.CanRetainSpecialKeyInput(candidate, [saved with { InterfaceLabel = "hid-other" }]));
        Assert.False(CollectorViewModel.CanRetainSpecialKeyInput(candidate, [saved with { ProductId = 0x0001 }]));
    }

    [Theory]
    [InlineData(0x41)] [InlineData(0x50)] [InlineData(0x30)] [InlineData(0x20)] [InlineData(0x0D)] [InlineData(0xBA)]
    public void OrdinaryTypingIsCountedWithoutValues(int key)
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.M1, SpecialKeyModeContext.Normal);
        Assert.Null(parser.Keyboard(Candidate(), (ushort)key, 0, 0));
        var summary = parser.Summary(SpecialKeyProbeEndReason.Timeout);
        Assert.Equal(1, summary.IgnoredTypingCount);
        Assert.Equal(0, summary.ObservationCount);
    }

    [Fact]
    public void PIsAllowedOnlyForExplicitFnPPromptAndIsNotProofOfFn()
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.FnP, SpecialKeyModeContext.Unknown);
        var observation = Assert.IsType<SpecialKeyObservation>(parser.Keyboard(Candidate(), 0x50, 0, 1));
        Assert.Equal((ushort)0x50, observation.VirtualKey);
        Assert.False(observation.IsFirmwareReadback);
        Assert.Equal(EvidenceProvenance.UserReported, observation.ContextProvenance);
        Assert.Contains("does not prove Fn", parser.Summary(SpecialKeyProbeEndReason.Timeout).Gap);
    }

    [Fact]
    public void KeyboardStateTracksDownRepeatAndUpWithoutScanCodesOrText()
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.M1, SpecialKeyModeContext.Driver);
        Assert.Equal(SpecialKeyPhase.KeyDown, parser.Keyboard(Candidate(), 0x7C, 0, 0)!.Phase);
        Assert.Equal(SpecialKeyPhase.Repeat, parser.Keyboard(Candidate(), 0x7C, 0, 1)!.Phase);
        Assert.Equal(SpecialKeyPhase.KeyUp, parser.Keyboard(Candidate(), 0x7C, 1, 2)!.Phase);
        Assert.Equal(SpecialKeyPhase.KeyDown, parser.Keyboard(Candidate(), 0x7C, 0, 3)!.Phase);
    }

    [Fact]
    public void OtherLaptopRootsAndNonBladeProductsAreRejected()
    {
        Assert.True(SpecialKeyProbeParser.IsSingleBladeRoot([Candidate(), Candidate(page: 12, usage: 1)]));
        Assert.False(SpecialKeyProbeParser.IsSingleBladeRoot([Candidate(), Candidate(root: "other-laptop")]));
        Assert.False(SpecialKeyProbeParser.IsSingleBladeRoot([Candidate(model: "Razer Mouse")]));
        Assert.False(SpecialKeyProbeParser.IsSingleBladeRoot([Candidate(root: "")]));
    }

    [Fact]
    public void InputRegistrationExcludesMouseAndUnprovenDescriptors()
    {
        Assert.True(SpecialKeyProbeParser.IsInputCandidate(Candidate()));
        Assert.True(SpecialKeyProbeParser.IsInputCandidate(Candidate(page: 12, usage: 1)));
        Assert.True(SpecialKeyProbeParser.IsInputCandidate(Candidate(page: 0xFF00, usage: 1)));
        Assert.False(SpecialKeyProbeParser.IsInputCandidate(Candidate(page: 1, usage: 2)));
        Assert.False(SpecialKeyProbeParser.IsInputCandidate(Candidate() with { DescriptorState = CollectionState.Unavailable }));
        Assert.False(SpecialKeyProbeParser.IsInputCandidate(Candidate(length: 0)));
    }

    [Fact]
    public void UndefinedDesktopCollectionIsAdmittedWithoutGuessingFnOrRetainingBytes()
    {
        var candidate = Candidate(page: 1, usage: 0, length: 16);
        Assert.True(SpecialKeyProbeParser.IsInputCandidate(candidate));
        Assert.True(SpecialKeyProbeParser.IsSingleBladeRoot([candidate, Candidate()]));
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Unknown);
        byte[] report = new byte[16];
        report[0] = 4; report[1] = 10;
        var pressed = Assert.IsType<SpecialKeyObservation>(parser.Hid(candidate, report, 0));
        report[1] = 0;
        var released = Assert.IsType<SpecialKeyObservation>(parser.Hid(candidate, report, 1));
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata, pressed.Origin);
        Assert.Null(pressed.ReviewedSignal);
        Assert.Null(pressed.VirtualKey);
        Assert.Equal(1, released.ChangedByteCount);
        Assert.Equal(new ChangedByteRange(1, 1), Assert.Single(released.ChangedRanges!));
        Assert.NotEqual(pressed.ProbeEqualityLabel, released.ProbeEqualityLabel);
        Assert.Equal(2, parser.Summary(SpecialKeyProbeEndReason.Timeout).ObservationCount);
        Assert.DoesNotContain(Convert.ToHexString(report), JsonSerializer.Serialize(released));
        Assert.False(SpecialKeyProbeParser.IsInputCandidate(candidate with { VendorId = 0xFFFF }));
        Assert.True(SpecialKeyProbeParser.IsInputCandidate(candidate with { DescriptorState = CollectionState.Partial }));
        Assert.False(SpecialKeyProbeParser.IsInputCandidate(candidate with { DescriptorState = CollectionState.Partial, ReportGeometry = null }));
        Assert.False(SpecialKeyProbeParser.IsSingleBladeRoot([candidate, Candidate(root: "other-root")]));
    }

    [Fact]
    public void UnknownHidDoesNotExportBytesPathsOrPersistentHashes()
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Unknown);
        var candidate = Candidate(page: 0xFF00, usage: 1, length: 4);
        var first = parser.Hid(candidate, [0x41, 0x42, 0x43, 0x44], 0)!;
        var changed = parser.Hid(candidate, [0x41, 0x63, 0x64, 0x44], 1)!;
        var same = parser.Hid(candidate, [0x41, 0x42, 0x43, 0x44], 2)!;
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata, first.Origin);
        Assert.Equal("input-01", first.ProbeEqualityLabel);
        Assert.Equal(first.ProbeEqualityLabel, same.ProbeEqualityLabel);
        Assert.NotEqual(first.ProbeEqualityLabel, changed.ProbeEqualityLabel);
        Assert.Equal(2, changed.ChangedByteCount);
        Assert.Equal(new ChangedByteRange(1, 2), Assert.Single(changed.ChangedRanges!));
        Assert.Null(first.VirtualKey);
        string json = JsonSerializer.Serialize(first);
        Assert.DoesNotContain("41424344", json);
        Assert.DoesNotContain("private-", json);
        Assert.Null(first.ReviewedSignal);
    }

    [Theory]
    [InlineData(0x21)]
    [InlineData(0x22)]
    [InlineData(0x70)]
    [InlineData(0x7B)]
    public void NonTextNavigationAndFunctionSignalsRemainAvailable(ushort virtualKey)
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.M1, SpecialKeyModeContext.Unknown);
        var keyboard = Candidate(0xFFFF, "Razer Blade", 1, 6, 8);
        var observed = parser.Keyboard(keyboard, virtualKey, 0, 1, 0x49);
        Assert.NotNull(observed);
        Assert.Equal(virtualKey, observed.VirtualKey);
        Assert.Equal((ushort)0x49, observed.ScanCode);
    }

    [Fact]
    public void ReviewedSignalsRequireModelAndStrictEnvelopeAndZeroPadding()
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Driver);
        var known = Candidate(0x02E0, "Razer Blade 16 RZ09-0581", 0xFF00, 1, 4);
        Assert.Equal("Fn", parser.Hid(known, [4, 10, 0, 0], 0)!.ReviewedSignal);
        Assert.Equal(SpecialKeyPhase.Unknown, parser.Hid(known, [4, 10, 0, 0], 1)!.Phase);
        Assert.Equal(SpecialKeyPhase.Unknown, parser.Hid(known, [4, 0, 0, 0], 2)!.Phase);
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata, parser.Hid(known, [4, 10, 0, 0x41], 3)!.Origin);
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata, parser.Hid(known with { ProductId = 0xFFFF }, [4, 10, 0, 0], 4)!.Origin);
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata, parser.Hid(known with { UsbParentProductName = "Razer Blade Unknown" }, [4, 10, 0, 0], 5)!.Origin);
    }

    [Theory]
    [InlineData(0x02E0, "Razer Blade 16 RZ09-0581")]
    [InlineData(0x02C6, "Razer Blade 16 RZ09-0528")]
    public void ExistingReviewedFnEnvelopeWorksOnUndefinedDesktopCollection(ushort pid, string model)
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Unknown);
        var candidate = Candidate(pid, model, 1, 0, 16);
        byte[] report = new byte[16];
        report[0] = 4; report[1] = 10;
        Assert.Equal("Fn", parser.Hid(candidate, report, 1)!.ReviewedSignal);
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata,
            parser.Hid(candidate with { ProductId = 0xFFFF }, report, 2)!.Origin);
        report[15] = 0x41;
        Assert.Equal(SpecialKeyObservationOrigin.UnknownHidMetadata, parser.Hid(candidate, report, 3)!.Origin);
    }

    [Theory]
    [InlineData(0, 1, 8, false)] [InlineData(4, 0, 8, false)]
    [InlineData(4, 2, 16, true)] [InlineData(4, 2, 15, false)]
    [InlineData(4, 33, 140, false)] [InlineData(-1, -1, 8, false)]
    public void HidPacketGeometryRejectsMalformedCountsAndTruncation(int size, int count, int bytes, bool accepted)
    {
        byte[] body = new byte[bytes];
        BinaryPrimitives.WriteUInt32LittleEndian(body, unchecked((uint)size));
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), unchecked((uint)count));
        Assert.Equal(accepted, SpecialKeyProbeParser.TryGetHidShape(body, 4, out _, out _));
    }

    [Fact]
    public void ExpiryBetweenParsingAndDeliveryDoesNotInventRetainedInput()
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.Fn, SpecialKeyModeContext.Unknown);
        Assert.NotNull(parser.Hid(Candidate(page: 1, usage: 0, length: 4), [4, 10, 0, 0], 4999));
        // Emit's final foreground/deadline gate declined the parsed packet.
        Assert.Equal(1, parser.ObservationCount);
        Assert.Equal(0, parser.Summary(SpecialKeyProbeEndReason.Timeout, deliveredObservations: 0).ObservationCount);
        Assert.Equal(1, parser.Summary(SpecialKeyProbeEndReason.LimitReached, deliveredObservations: 1).ObservationCount);
    }

    [Fact]
    public void ReportAndObservationLimitsAreEnforcedAndDisposedParserStops()
    {
        using var parser = new SpecialKeyProbeParser(SpecialKeyExpectedKey.M1, SpecialKeyModeContext.Unknown);
        Assert.Null(parser.Hid(Candidate(length: 4), [1, 2, 3], 0));
        for (int i = 0; i < 32; i++) Assert.NotNull(parser.Keyboard(Candidate(), 0x7C, 0, i));
        Assert.Null(parser.Keyboard(Candidate(), 0x7C, 0, 33));
        Assert.Null(parser.Hid(Candidate(length: 4), [1, 2, 3, 4], 33));
        Assert.Equal(32, parser.ObservationCount);
        Assert.Equal(1, parser.Summary(SpecialKeyProbeEndReason.LimitReached).RejectedPacketCount);
        parser.Dispose();
        Assert.Null(parser.Keyboard(Candidate(), 0x7C, 0, 34));
    }
}
