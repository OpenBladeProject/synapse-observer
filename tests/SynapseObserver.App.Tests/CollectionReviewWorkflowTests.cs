using SynapseObserver.App;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class CollectionReviewWorkflowTests
{
    [Fact]
    public async Task InventoryReportsAndSkipsRefreshTheVisibleReview()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        Assert.Contains("No Synapse recording", model.ReviewSummary);
        model.SelectedActivityIndex = model.Activities.Select((activity, index) => (activity, index))
            .Single(item => item.activity.Id == "keyboard-layout").index;
        model.StartingSetting = "ANSI US";
        await Run(model.MarkCommand);
        Assert.Contains("40 evidence items", model.ReviewNotes);
        await Run(model.SkipCommand);
        Assert.Contains("39 evidence items", model.ReviewNotes);
        Assert.False(model.IsBusy);
        await model.CloseAsync();
    }

    [Fact]
    public async Task EveryActionPhaseUsesFreshPowerAndStopRefreshesReview()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        var sources = new Queue<PowerSourceKind>([PowerSourceKind.Battery, PowerSourceKind.Ac, PowerSourceKind.Ac]);
        model.ActionPowerSourceForTest = () => ValueTask.FromResult(sources.Dequeue());
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        await Run(model.StopCommand);
        var window = Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(model.Evidence!).ActionWindows);
        Assert.Equal(PowerSourceKind.Unknown, window.PowerSourceAtAction);
        Assert.Equal(new[] { PowerSourceKind.Battery, PowerSourceKind.Ac, PowerSourceKind.Ac, PowerSourceKind.Ac },
            window.Markers.Select(marker => marker.PowerSourceAtMarker!.Value));
        Assert.Contains("1 documented setting change", model.ReviewSummary);
        Assert.Contains("40 evidence items", model.ReviewNotes);
        Assert.False(model.IsBusy);
        await model.CloseAsync();
    }

    [Fact]
    public async Task CleanStopAllowsExplicitSecondRecordingAndPreservesPriorEvidence()
    {
        var model = new CollectorViewModel(preview: true);
        await Run(model.CollectCommand);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        model.StartingSetting = "Balanced";
        await Run(model.MarkCommand);
        model.ChangedSetting = "Silent";
        await Run(model.MarkCommand);
        await Run(model.MarkCommand);
        await Run(model.StopCommand);
        var first = model.Evidence!;
        Assert.True(model.AttachConsent);
        Assert.True(model.StartCommand.CanExecute(null));
        Assert.False(model.IsRecording);
        await Run(model.StartCommand);
        Assert.True(model.IsRecording);
        await Run(model.StopCommand);
        var second = model.Evidence!;
        Assert.Equal(first.Observations.Count, second.Observations.Count);
        Assert.Equal(2, second.Markers.Count(item => item.Kind == EvidenceMarkerKind.RecordingStarted));
        Assert.Equal(2, second.Markers.Count(item => item.Kind == EvidenceMarkerKind.RecordingStopped));
        Assert.Single(ContributionDraftBuilder.CreateAnnotationDraft(second).ActionWindows);
        await model.CloseAsync();
    }

    private static async Task Run(AsyncCommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        await command.CurrentExecution;
    }
}
