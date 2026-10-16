using SynapseObserver.Core;

namespace SynapseObserver.App;

internal sealed partial class CollectorViewModel
{
    private SpecialKeyProbe? specialKeyProbe;
    private Task specialKeyReviewTask = Task.CompletedTask;
    private bool inputMismatchReported;
    private string inputSelectionIssue = "The laptop's input interfaces could not be selected unambiguously. Save the inventory so the maintainer can investigate.";
    private Func<bool>? probeForeground;
    private bool probingSpecialKey;
    private int probeSequence;
    private string activeProbeLabel = string.Empty;
    private double probeOrigin;
    private int rejectedProbeObservations;
    private SpecialKeyExpectedKey expectedSpecialKey = SpecialKeyExpectedKey.Fn;
    private string specialKeyStatus = SpecialKeySelectionGuidance(SpecialKeyExpectedKey.Fn);
    public sealed record SpecialKeyChoice(SpecialKeyExpectedKey Key, string Label);
    public IReadOnlyList<SpecialKeyChoice> SpecialKeyChoices { get; } =
    [
        new(SpecialKeyExpectedKey.Fn, SpecialKeyLabel(SpecialKeyExpectedKey.Fn)),
        .. Enumerable.Range(1, 12).Select(number =>
            new SpecialKeyChoice((SpecialKeyExpectedKey)((int)SpecialKeyExpectedKey.FnF1 + number - 1), $"Fn + F{number}")),
        new(SpecialKeyExpectedKey.FnR, SpecialKeyLabel(SpecialKeyExpectedKey.FnR)),
        new(SpecialKeyExpectedKey.FnP, SpecialKeyLabel(SpecialKeyExpectedKey.FnP)),
        new(SpecialKeyExpectedKey.M1, SpecialKeyLabel(SpecialKeyExpectedKey.M1)),
        new(SpecialKeyExpectedKey.M2, SpecialKeyLabel(SpecialKeyExpectedKey.M2)),
        new(SpecialKeyExpectedKey.M3, SpecialKeyLabel(SpecialKeyExpectedKey.M3)),
        new(SpecialKeyExpectedKey.M4, SpecialKeyLabel(SpecialKeyExpectedKey.M4)),
        new(SpecialKeyExpectedKey.M5, SpecialKeyLabel(SpecialKeyExpectedKey.M5)),
    ];
    public bool IsProbingSpecialKey => probingSpecialKey;
    public bool ShowSpecialKeyProbe => HasInventory;
    public bool IsSpecialKeyActivity => HasCurrentStep
        && CurrentActivity.Id.StartsWith("special-key-", StringComparison.Ordinal);
    public bool CanProbeSpecialKeys => HasInventory && !capacityReached && !closing && !busy && !probingSpecialKey && !preview
        && specialKeyProbe is not null && stepPhase == 0 && SpecialKeyProbeParser.IsSingleBladeRoot(inputCandidates)
        && inputCandidates.Any(SpecialKeyProbeParser.IsInputCandidate)
        && session!.SpecialKeyProbeCount < EvidenceLimits.MaxSpecialKeyProbes
        && session.SpecialKeyObservationCount < EvidenceLimits.MaxSpecialKeyObservations;
    public bool CanChooseSpecialKey => !probingSpecialKey;
    public string SpecialKeyStatus => specialKeyStatus + (HasInventory && !preview && inputCandidates.Count == 0
        ? " " + inputSelectionIssue : string.Empty);
    public SpecialKeyExpectedKey ExpectedSpecialKey
    {
        get => expectedSpecialKey;
        set
        {
            if (probingSpecialKey || !Enum.IsDefined(value) || expectedSpecialKey == value) return;
            expectedSpecialKey = value;
            specialKeyStatus = SpecialKeySelectionGuidance(value);
            Changed();
            Changed(nameof(SpecialKeyStatus));
        }
    }
    public AsyncCommand? ProbeSpecialKeyCommand { get; private set; }
    public AsyncCommand? CancelSpecialKeyProbeCommand { get; private set; }

    internal void SelectSpecialKeyInputCandidates(IReadOnlyList<LocalHidCandidate> candidates)
    {
        inputCandidates = [];
        inputSelectionIssue = "The laptop's input interfaces could not be selected unambiguously. Save the inventory so the maintainer can investigate.";
        var eligible = candidates.Where(candidate => SpecialKeyProbeParser.IsInputCandidate(candidate)
            && candidate.UsbParentProductName?.Contains("Blade", StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (!SpecialKeyProbeParser.IsSingleBladeRoot(eligible) || session is null) return;
        if (eligible.All(candidate => CanRetainSpecialKeyInput(candidate, session.Inventory.RazerHidInterfaces.Value)))
        {
            inputCandidates = eligible;
            return;
        }
        inputSelectionIssue = "The laptop's input descriptors changed after device information was collected. Save this ZIP, then reopen the collector and collect device information again before checking a key.";
        if (!inputMismatchReported)
        {
            inputMismatchReported = true;
            Marker(EvidenceMarkerKind.Failure, "observer", "Key check unavailable: input inventory mismatch", EvidenceProvenance.Collector);
        }
    }

    internal static bool CanRetainSpecialKeyInput(LocalHidCandidate candidate, IReadOnlyList<HidInterfaceEvidence> inventory) =>
        SpecialKeyProbeParser.IsInputCandidate(candidate) && inventory.Any(item =>
            item.InterfaceLabel == candidate.InterfaceLabel && item.VendorId == candidate.VendorId
            && item.ProductId == candidate.ProductId && item.UsagePage == candidate.UsagePage && item.Usage == candidate.Usage
            && item.ReportGeometry == candidate.ReportGeometry
            && SpecialKeyInputCollection.IsEligible(item.VendorId, item.DescriptorState, item.UsagePage, item.Usage, item.ReportGeometry));

    internal void ConfigureSpecialKeyProbe(SpecialKeyProbe probe, Func<bool> isForeground)
    {
        specialKeyProbe = probe;
        probeForeground = isForeground;
        probe.Completed += summary => specialKeyReviewTask = CompleteSpecialKeyProbeAsync(summary);
        ProbeSpecialKeyCommand = new AsyncCommand(() => GuardAsync(ProbeSpecialKeyAsync), () => CanProbeSpecialKeys);
        CancelSpecialKeyProbeCommand = new AsyncCommand(() => { probe.Disarm(); return Task.CompletedTask; }, () => probingSpecialKey);
        RefreshSpecialKeyProbe();
    }

    private Task ProbeSpecialKeyAsync()
    {
        if (!CanProbeSpecialKeys || session is null || specialKeyProbe is null || probeForeground is null) return Task.CompletedTask;
        if (!PrepareSpecialKeyProbe()) return Task.CompletedTask;
        activeProbeLabel = $"probe-{++probeSequence:D3}";
        probeOrigin = RelativeMilliseconds;
        rejectedProbeObservations = 0;
        probingSpecialKey = true;
        specialKeyStatus = $"Press only {SpecialKeyLabel(expectedSpecialKey)} now. Keep this window active for five seconds. Ordinary typing is omitted.";
        if (!Marker(EvidenceMarkerKind.SpecialKeyProbeStarted, SpecialKeyActivity(expectedSpecialKey), "Special-key probe requested", EvidenceProvenance.UserReported))
        {
            probingSpecialKey = false;
            RefreshSpecialKeyProbe();
            return Task.CompletedTask;
        }
        RefreshSpecialKeyProbe();
        try
        {
            specialKeyProbe.Arm(expectedSpecialKey, inputCandidates, item =>
            {
                var result = RecordSpecialKeyObservation(item);
                if (result == EvidenceAddResult.RejectedLimit || session.SpecialKeyObservationCount >= EvidenceLimits.MaxSpecialKeyObservations)
                {
                    // Completion synchronously publishes counts, gaps and the
                    // key-limit guidance. Preserve that complete result.
                    specialKeyProbe.EndAtCapacity();
                }
            }, probeForeground, SpecialKeyModeContext.Unknown);
        }
        catch
        {
            probingSpecialKey = false;
            specialKeyStatus = "The special-key probe could not start. Save the device information and collection gaps.";
            RefreshSpecialKeyProbe();
            throw;
        }
        return Task.CompletedTask;
    }

    // Admission precedes the requested-probe marker: do not create an intent
    // that cannot have a bounded result, or arm a probe in a saturated part.
    internal bool PrepareSpecialKeyProbe()
    {
        if (session is null || capacityReached) return false;
        if (session.MarkerCount >= EvidenceLimits.MaxMarkers - CleanupMarkerReserve - 1)
        {
            ReachCapacity("The checklist has reached its action-marker limit.");
            specialKeyStatus = "There is no room for another key-check marker. Save the partial evidence after cleanup.";
            RefreshSpecialKeyProbe();
            return false;
        }
        if (session.SpecialKeyProbeCount >= EvidenceLimits.MaxSpecialKeyProbes
            || session.SpecialKeyObservationCount >= EvidenceLimits.MaxSpecialKeyObservations)
        {
            specialKeyStatus = "The optional key-check limit has been reached. Setting recordings can continue; all collected key evidence will be included in the ZIP.";
            RefreshSpecialKeyProbe();
            return false;
        }
        return true;
    }

    internal EvidenceAddResult RecordSpecialKeyObservation(SpecialKeyObservation item)
    {
        if (session is null) return EvidenceAddResult.RejectedInvalid;
        var result = session.AddSpecialKeyObservation(item with
        { RelativeMilliseconds = probeOrigin + item.RelativeMilliseconds, ProbeLabel = activeProbeLabel });
        if (result is EvidenceAddResult.RejectedInvalid or EvidenceAddResult.RejectedLimit)
        {
            rejectedProbeObservations = Math.Min(rejectedProbeObservations + 1, SpecialKeyProbeParser.MaxObservations);
            specialKeyStatus = "Some input evidence could not be retained. Save the collection gaps.";
        }
        return result;
    }

    internal async Task CompleteSpecialKeyProbeAsync(SpecialKeyProbeSummary summary)
    {
        CompleteSpecialKeyProbe(summary);
        if (!recording)
        {
            await UpdateCollectionReviewAsync();
            Changed(nameof(ReviewSummary));
            Changed(nameof(ReviewNotes));
        }
    }

    internal void CompleteSpecialKeyProbe(SpecialKeyProbeSummary summary)
    {
        probingSpecialKey = false;
        summary = summary with
        {
            ObservationCount = Math.Max(0, summary.ObservationCount - rejectedProbeObservations),
            RejectedPacketCount = Math.Min(1000000, summary.RejectedPacketCount + rejectedProbeObservations),
        };
        rejectedProbeObservations = 0;
        if (session is not null)
        {
            var result = session.AddSpecialKeyProbe(summary with { ProbeLabel = activeProbeLabel, RelativeMilliseconds = probeOrigin });
            if (result != EvidenceAddResult.Added)
            {
                Marker(EvidenceMarkerKind.Failure, "observer", "Special-key probe result could not be retained", EvidenceProvenance.Collector);
                specialKeyStatus = "The key-check result could not be retained. Save partial evidence; no complete result is claimed.";
                RefreshSpecialKeyProbe();
                return;
            }
            // A probe never replaces the user's declaration of visible behavior.
        }
        specialKeyStatus = SpecialKeyCompletionStatus(summary);
        if (session is not null && (session.SpecialKeyProbeCount >= EvidenceLimits.MaxSpecialKeyProbes
            || session.SpecialKeyObservationCount >= EvidenceLimits.MaxSpecialKeyObservations))
        {
            specialKeyStatus += " The optional key-check limit has been reached. Setting recordings can continue; collected key evidence will be included in the ZIP.";
        }
        RefreshSpecialKeyProbe();
    }

    internal void CancelSpecialKeyProbe() => specialKeyProbe?.Disarm();
    private static string SpecialKeyActivity(SpecialKeyExpectedKey key) => key switch
    {
        SpecialKeyExpectedKey.FnP => "special-key-fn-p",
        SpecialKeyExpectedKey.M1 or SpecialKeyExpectedKey.M2 or SpecialKeyExpectedKey.M3
            or SpecialKeyExpectedKey.M4 or SpecialKeyExpectedKey.M5 => "special-key-" + key.ToString().ToLowerInvariant(),
        _ => "special-key-fn",
    };
    private static string SpecialKeyLabel(SpecialKeyExpectedKey key) => key switch
    {
        SpecialKeyExpectedKey.Fn => "Fn alone",
        SpecialKeyExpectedKey.FnP => "Fn + P",
        SpecialKeyExpectedKey.FnR => "Fn + R",
        _ when IsFnFunctionKey(key) => $"Fn + F{(int)key - (int)SpecialKeyExpectedKey.FnF1 + 1}",
        _ => key.ToString(),
    };
    private static string SpecialKeySelectionGuidance(SpecialKeyExpectedKey key) => key switch
    {
        SpecialKeyExpectedKey.Fn => "Fn alone may not produce a Windows input event. Click Check key for 5 seconds, keep this collector window in front, and press only Fn during the check. Then report any visible Fn behavior in the Fn key evidence item. You can also select a specific Fn chord that is printed on or offered by your laptop.",
        SpecialKeyExpectedKey.FnP => "Optional: check only Fn + P while this collector window is in front. It may change a setting. A P signal does not prove that Fn was detected or what the shortcut did.",
        SpecialKeyExpectedKey.FnR => "Optional: check only Fn + R while this collector window is in front. An R signal does not prove that Fn was detected or what the shortcut did.",
        _ when IsFnFunctionKey(key) => $"Optional: check only {SpecialKeyLabel(key)} while this collector window is in front. A function-key or media signal does not prove that Fn was detected or what the shortcut did; report visible behavior separately in the Fn key evidence item.",
        _ => "Optional: identify one M key. Click Check key for 5 seconds, keep this collector window in front, and press only the selected key during the check.",
    };
    private static bool IsFnFunctionKey(SpecialKeyExpectedKey key) =>
        (int)key is >= (int)SpecialKeyExpectedKey.FnF1 and <= (int)SpecialKeyExpectedKey.FnF12;
    private static string SpecialKeyCompletionStatus(SpecialKeyProbeSummary summary)
    {
        if (summary.EndReason == SpecialKeyProbeEndReason.Timeout)
        {
            if (summary.RejectedPacketCount > 0)
                return $"{SpecialKeyLabel(summary.ExpectedKey)}: {summary.ObservationCount} retained input observations. " +
                    $"{summary.RejectedPacketCount} input packets or observations were rejected. " +
                    "The check contains collection gaps; this does not show whether the physical key worked.";
            if (summary.ExpectedKey == SpecialKeyExpectedKey.Fn && summary.ObservationCount == 0)
                return "Fn: no Windows input observations were captured in five seconds. The check finished normally. " +
                    "Fn alone may be handled without a Windows event, so report any visible behavior in the Fn key evidence item. " +
                    "This result does not show whether the physical key worked.";

            return $"{SpecialKeyLabel(summary.ExpectedKey)}: {summary.ObservationCount} input observations in five seconds. " +
                "The check finished normally. No signal does not prove that the key is unavailable.";
        }

        return $"{SpecialKeyLabel(summary.ExpectedKey)}: {summary.ObservationCount} input observations. Probe ended: {summary.EndReason}. " +
            (summary.RejectedPacketCount > 0 ? $"{summary.RejectedPacketCount} input packets or observations were rejected. " : "") +
            "No signal does not prove that the key is unavailable.";
    }
    private void RefreshSpecialKeyProbe()
    {
        Changed(nameof(ProbeSpecialKeyCommand)); Changed(nameof(CancelSpecialKeyProbeCommand));
        Changed(nameof(IsProbingSpecialKey)); Changed(nameof(ShowSpecialKeyProbe)); Changed(nameof(IsSpecialKeyActivity)); Changed(nameof(CanProbeSpecialKeys));
        Changed(nameof(CapacitySummary));
        Changed(nameof(CanChooseSpecialKey)); Changed(nameof(SpecialKeyStatus));
        ProbeSpecialKeyCommand?.Refresh(); CancelSpecialKeyProbeCommand?.Refresh();
        RefreshCommands();
    }
}
