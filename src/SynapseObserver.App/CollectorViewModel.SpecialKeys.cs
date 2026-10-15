using SynapseObserver.Core;

namespace SynapseObserver.App;

internal sealed partial class CollectorViewModel
{
    private SpecialKeyProbe? specialKeyProbe;
    private Func<bool>? probeForeground;
    private bool probingSpecialKey;
    private int probeSequence;
    private string activeProbeLabel = string.Empty;
    private double probeOrigin;
    private SpecialKeyExpectedKey expectedSpecialKey = SpecialKeyExpectedKey.M1;
    private string specialKeyStatus = SpecialKeySelectionGuidance(SpecialKeyExpectedKey.M1);
    public sealed record SpecialKeyChoice(SpecialKeyExpectedKey Key, string Label);
    public IReadOnlyList<SpecialKeyChoice> SpecialKeyChoices { get; } = Enum.GetValues<SpecialKeyExpectedKey>()
        .Select(key => new SpecialKeyChoice(key, key == SpecialKeyExpectedKey.FnP ? "Fn + P" : key.ToString())).ToArray();
    public bool IsProbingSpecialKey => probingSpecialKey;
    public bool ShowSpecialKeyProbe => HasInventory;
    public bool CanProbeSpecialKeys => HasInventory && !capacityReached && !closing && !busy && !probingSpecialKey && !preview
        && specialKeyProbe is not null && stepPhase == 0 && SpecialKeyProbeParser.IsSingleBladeRoot(inputCandidates)
        && inputCandidates.Any(SpecialKeyProbeParser.IsInputCandidate);
    public bool CanChooseSpecialKey => !probingSpecialKey;
    public string SpecialKeyStatus => specialKeyStatus + (HasInventory && !preview && inputCandidates.Count == 0
        ? " The laptop's input interfaces could not be selected unambiguously. Save the inventory so the maintainer can investigate." : string.Empty);
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

    internal void ConfigureSpecialKeyProbe(SpecialKeyProbe probe, Func<bool> isForeground)
    {
        specialKeyProbe = probe;
        probeForeground = isForeground;
        probe.Completed += CompleteSpecialKeyProbe;
        ProbeSpecialKeyCommand = new AsyncCommand(() => GuardAsync(ProbeSpecialKeyAsync), () => CanProbeSpecialKeys);
        CancelSpecialKeyProbeCommand = new AsyncCommand(() => { probe.Disarm(); return Task.CompletedTask; }, () => probingSpecialKey);
        RefreshSpecialKeyProbe();
    }

    private Task ProbeSpecialKeyAsync()
    {
        if (!CanProbeSpecialKeys || session is null || specialKeyProbe is null || probeForeground is null) return Task.CompletedTask;
        activeProbeLabel = $"probe-{++probeSequence:D3}";
        probeOrigin = RelativeMilliseconds;
        probingSpecialKey = true;
        specialKeyStatus = $"Press only {expectedSpecialKey} now. Keep this window active for five seconds. Ordinary typing is omitted.";
        if (!Marker(EvidenceMarkerKind.SpecialKeyProbeStarted, SpecialKeyActivity(expectedSpecialKey), "Special-key probe requested", EvidenceProvenance.UserReported))
        {
            probingSpecialKey = false;
            RefreshSpecialKeyProbe();
            return Task.CompletedTask;
        }
        CheckMarkerCapacity();
        if (capacityReached)
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
                var result = session.AddSpecialKeyObservation(item with
                { RelativeMilliseconds = probeOrigin + item.RelativeMilliseconds, ProbeLabel = activeProbeLabel });
                if (result is EvidenceAddResult.RejectedInvalid or EvidenceAddResult.RejectedLimit)
                    specialKeyStatus = "Some input evidence was rejected by the privacy or size limits.";
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

    internal void CompleteSpecialKeyProbe(SpecialKeyProbeSummary summary)
    {
        probingSpecialKey = false;
        if (session is not null)
        {
            session.AddSpecialKeyProbe(summary with { ProbeLabel = activeProbeLabel, RelativeMilliseconds = probeOrigin });
            // A probe is separate evidence. It never replaces a declaration
            // or treats missing input as proof that the key is unavailable.
        }
        specialKeyStatus = SpecialKeyCompletionStatus(summary);
        RefreshSpecialKeyProbe();
    }

    internal void CancelSpecialKeyProbe() => specialKeyProbe?.Disarm();
    private static string SpecialKeyActivity(SpecialKeyExpectedKey key) => key switch
    { SpecialKeyExpectedKey.Fn => "special-key-fn", SpecialKeyExpectedKey.FnP => "special-key-fn-p", _ => "special-key-" + key.ToString().ToLowerInvariant() };
    private static string SpecialKeySelectionGuidance(SpecialKeyExpectedKey key) => key == SpecialKeyExpectedKey.Fn
        ? "Fn alone may not produce a Windows input event. Click Check key for 5 seconds, keep this collector window in front, and press only Fn during the check. Then report any visible Fn behavior in the Fn key evidence item. If your laptop has an Fn + P shortcut, you can probe it separately; it may change a setting."
        : "Optional: identify one M or Fn key. Click Check key for 5 seconds, keep this collector window in front, and press only the selected key during the check.";
    private static string SpecialKeyCompletionStatus(SpecialKeyProbeSummary summary)
    {
        if (summary.EndReason == SpecialKeyProbeEndReason.Timeout)
        {
            if (summary.ExpectedKey == SpecialKeyExpectedKey.Fn && summary.ObservationCount == 0)
                return "Fn: no Windows input observations were captured in five seconds. The check finished normally. " +
                    "Fn alone may be handled without a Windows event, so report any visible behavior in the Fn key evidence item. " +
                    "This result does not show whether the physical key worked.";

            return $"{summary.ExpectedKey}: {summary.ObservationCount} input observations in five seconds. " +
                "The check finished normally. No signal does not prove that the key is unavailable.";
        }

        return $"{summary.ExpectedKey}: {summary.ObservationCount} input observations. Probe ended: {summary.EndReason}. " +
            "No signal does not prove that the key is unavailable.";
    }
    private void RefreshSpecialKeyProbe()
    {
        Changed(nameof(ProbeSpecialKeyCommand)); Changed(nameof(CancelSpecialKeyProbeCommand));
        Changed(nameof(IsProbingSpecialKey)); Changed(nameof(ShowSpecialKeyProbe)); Changed(nameof(CanProbeSpecialKeys));
        Changed(nameof(CanChooseSpecialKey)); Changed(nameof(SpecialKeyStatus));
        ProbeSpecialKeyCommand?.Refresh(); CancelSpecialKeyProbeCommand?.Refresh();
        RefreshCommands();
    }
}
