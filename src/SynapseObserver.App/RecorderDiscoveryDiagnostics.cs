using SynapseObserver.Backend;

namespace SynapseObserver.App;

internal static class RecorderDiscoveryDiagnostics
{
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
