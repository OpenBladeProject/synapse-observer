using SynapseObserver.Backend;

namespace SynapseObserver.App;

internal static class RecorderDiscoveryDiagnostics
{
    public static string AvailabilityStatus(bool ready, bool consent, string diagnostic) =>
        !ready ? diagnostic + " Your collected evidence is preserved."
        : !consent ? "Recording is available. Allow observation, then select Start recording. Your collected evidence is preserved."
        : "Recording is available. Select Start recording when ready. Your collected evidence is preserved.";

    public static string UnavailableSummary(IReadOnlyList<BackendCandidate> candidates)
    {
        // Only the verified HID owner can report resident observer state. Its
        // recovery step takes priority over unrelated Electron helper failures.
        var blocked = candidates.Where(candidate => candidate.VerifiedTargetBlocked)
            .Select(candidate => candidate.Diagnostic).Distinct(StringComparer.Ordinal).ToArray();
        if (blocked.Length > 0) return string.Join("\n", blocked);
        return candidates.Select(candidate => candidate.Diagnostic).Distinct(StringComparer.Ordinal).ToArray() is [var onlyReason]
            ? onlyReason
            : "No recording target was verified. Open your laptop's controls in Synapse, start both apps normally, then refresh. See collection details.";
    }

    public static string Format(IReadOnlyList<BackendCandidate> candidates)
    {
        var verified = candidates.Where(candidate => candidate.Eligible).ToArray();
        if (verified.Length > 0)
        {
            var details = string.Join("\n", verified.Select(candidate => candidate.Diagnostic).Distinct(StringComparer.Ordinal));
            var excluded = candidates.Count - verified.Length;
            return excluded == 0 ? details : $"{details}\n\n{excluded} other Razer engine processes were excluded from recording.";
        }

        if (candidates.Count == 0) return "No Synapse engine was found. Open Synapse normally, then refresh.";
        return "No recording target was verified.\n\n" + string.Join("\n\n", candidates.GroupBy(candidate => candidate.Diagnostic, StringComparer.Ordinal)
            .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} ({group.Count()} processes)"));
    }
}
