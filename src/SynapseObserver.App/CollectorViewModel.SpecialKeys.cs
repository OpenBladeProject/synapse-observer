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
    private SpecialKeyModeContext reportedKeyboardMode = SpecialKeyModeContext.Unknown;
    private string specialKeyStatus = "Optional: identify one M or Fn key. Keep this window active and press only the selected key for five seconds.";
    public sealed record SpecialKeyChoice(SpecialKeyExpectedKey Key, string Label);
    public IReadOnlyList<SpecialKeyChoice> SpecialKeyChoices { get; } = Enum.GetValues<SpecialKeyExpectedKey>()
        .Select(key => new SpecialKeyChoice(key, key == SpecialKeyExpectedKey.FnP ? "Fn + P" : key.ToString())).ToArray();
    public IReadOnlyList<SpecialKeyModeContext> KeyboardModeChoices { get; } = Enum.GetValues<SpecialKeyModeContext>();
    public bool IsProbingSpecialKey => probingSpecialKey;
    public bool ShowSpecialKeyProbe => HasInventory;
    public bool CanProbeSpecialKeys => HasInventory && !busy && !probingSpecialKey && !preview
        && specialKeyProbe is not null && stepPhase == 0 && SpecialKeyProbeParser.IsSingleBladeRoot(inputCandidates)
        && inputCandidates.Any(SpecialKeyProbeParser.IsInputCandidate);
    public bool CanChooseSpecialKey => !probingSpecialKey;
    public string SpecialKeyStatus => specialKeyStatus + (HasInventory && !preview && inputCandidates.Count == 0
        ? " The laptop's input interfaces could not be selected unambiguously. Save the inventory so the maintainer can investigate." : string.Empty);
    public SpecialKeyExpectedKey ExpectedSpecialKey
    {
        get => expectedSpecialKey;
        set { if (probingSpecialKey || !Enum.IsDefined(value)) return; expectedSpecialKey = value; Changed(); }
    }
    public SpecialKeyModeContext ReportedKeyboardMode
    {
        get => reportedKeyboardMode;
        set { if (probingSpecialKey || !Enum.IsDefined(value)) return; reportedKeyboardMode = value; Changed(); }
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
        Marker(EvidenceMarkerKind.UserActionConfirmed, SpecialKeyActivity(expectedSpecialKey), "Special-key probe requested", EvidenceProvenance.UserReported, reportedKeyboardMode.ToString());
        RefreshSpecialKeyProbe();
        try
        {
            specialKeyProbe.Arm(expectedSpecialKey, inputCandidates, item =>
            {
                var result = session.AddSpecialKeyObservation(item with
                { RelativeMilliseconds = probeOrigin + item.RelativeMilliseconds, ProbeLabel = activeProbeLabel });
                if (result is EvidenceAddResult.RejectedInvalid or EvidenceAddResult.RejectedLimit)
                    specialKeyStatus = "Some input evidence was rejected by the privacy or size limits.";
            }, probeForeground, reportedKeyboardMode);
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

    private void CompleteSpecialKeyProbe(SpecialKeyProbeSummary summary)
    {
        probingSpecialKey = false;
        if (session is not null)
        {
            session.AddSpecialKeyProbe(summary with { ProbeLabel = activeProbeLabel, RelativeMilliseconds = probeOrigin });
            var state = summary.ObservationCount > 0 ? CoverageState.CompletedWithGaps : CoverageState.Unavailable;
            session.SetCoverage(new CoverageItem(SpecialKeyActivity(summary.ExpectedKey), summary.ExpectedKey.ToString(),
                state, summary.ObservationCount > 0 ? CoverageReason.None : CoverageReason.NoMatchingEvents));
            Activities.FirstOrDefault(item => item.Id == SpecialKeyActivity(summary.ExpectedKey))?.SetState(state);
        }
        specialKeyStatus = $"{summary.ExpectedKey}: {summary.ObservationCount} input observations · {summary.EndReason}. " +
            "Expected key and keyboard mode are your reports; input is not firmware readback. No event does not prove absence.";
        RefreshSpecialKeyProbe();
    }

    internal void CancelSpecialKeyProbe() => specialKeyProbe?.Disarm();
    private static string SpecialKeyActivity(SpecialKeyExpectedKey key) => key switch
    { SpecialKeyExpectedKey.Fn => "special-key-fn", SpecialKeyExpectedKey.FnP => "special-key-fn-p", _ => "special-key-" + key.ToString().ToLowerInvariant() };
    private void RefreshSpecialKeyProbe()
    {
        Changed(nameof(ProbeSpecialKeyCommand)); Changed(nameof(CancelSpecialKeyProbeCommand));
        Changed(nameof(IsProbingSpecialKey)); Changed(nameof(ShowSpecialKeyProbe)); Changed(nameof(CanProbeSpecialKeys));
        Changed(nameof(CanChooseSpecialKey)); Changed(nameof(SpecialKeyStatus));
        ProbeSpecialKeyCommand?.Refresh(); CancelSpecialKeyProbeCommand?.Refresh();
        RefreshCommands();
    }
}
