using SynapseObserver.App;
using SynapseObserver.Backend;

namespace SynapseObserver.App.Tests;

public sealed class RecorderDiscoveryDiagnosticsTests
{
    [Fact]
    public void VerifiedTargetDoesNotPresentExcludedProcessFailuresAsRecordingFailures()
    {
        BackendCandidate[] candidates = [new(1, "engine", true, "Target verified"), .. Enumerable.Range(2, 8).Select(id => new BackendCandidate(id, "engine", false, "Same account required"))];
        var details = RecorderDiscoveryDiagnostics.Format(candidates);
        Assert.Contains("Target verified", details);
        Assert.Contains("8 other", details);
        Assert.DoesNotContain("Same account required", details);
    }

    [Fact]
    public void NoVerifiedTargetKeepsDistinctFailuresAndCountsRepeatedReasons()
    {
        BackendCandidate[] candidates = [new(1, "engine", false, "Open laptop controls"), new(2, "engine", false, "Same account required"), new(3, "engine", false, "Same account required")];
        var details = RecorderDiscoveryDiagnostics.Format(candidates);
        Assert.Contains("No recording target was verified", details);
        Assert.Contains("Open laptop controls", details);
        Assert.Contains("Same account required (2 processes)", details);
        Assert.Equal(1, details.Split("Same account required").Length - 1);
    }

    [Fact]
    public void MissingEngineHasOneActionableMessage() =>
        Assert.Equal("No Synapse engine was found. Open Synapse normally, then refresh.", RecorderDiscoveryDiagnostics.Format([]));
}
