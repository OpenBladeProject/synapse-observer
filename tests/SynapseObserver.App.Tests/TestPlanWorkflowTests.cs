using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SynapseObserver.App;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class TestPlanWorkflowTests
{
    private static byte[] Plan(string activities, string keys = "[]") => Encoding.UTF8.GetBytes(
        "{\"schemaVersion\":1,\"activityIds\":" + activities + ",\"specialKeys\":" + keys + "}");

    [Fact]
    public async Task ImportFiltersInRequestedOrderAndUsesActualActivityChoices()
    {
        var model = new CollectorViewModel(true);
        Assert.True(model.TryImportTestPlan(Plan("[\"full-charge\",\"fans\"]")));
        Assert.Equal(new[] { "full-charge", "fans" }, model.Activities.Select(item => item.Id));
        Assert.Equal(new[] { "Off", "On", "Other state…" }, model.StartingEntry.Choices.Select(item => item.Label));
        Assert.False(model.ShowSpecialKeyProbe);
        Assert.False(model.AttachConsent);
        Assert.False(model.IsRecording);
        model.CollectCommand.Execute(null);
        await model.CollectCommand.CurrentExecution;
        Assert.True(model.ShowRecordingGuide);
        Assert.True(model.CanChangeTestPlan);
        Assert.Equal(2, model.Evidence!.RequestedTestPlan!.ActivityIds.Count);
        model.SelectedActivityIndex = 1;
        Assert.Equal("fans", model.Activities[model.SelectedActivityIndex].Id);
        Assert.Contains(model.StartingEntry.Choices, choice => choice.Label == "Fixed");
        model.ClearTestPlan();
        Assert.False(model.HasTestPlan);
        Assert.Equal("performance", model.Activities[0].Id);
        Assert.Null(model.Evidence!.RequestedTestPlan);
        await model.CloseAsync();
    }

    [Fact]
    public async Task KeyOnlyPlanShowsOnlyRequestedKeysAndNoRecordingGuide()
    {
        var model = new CollectorViewModel(true);
        Assert.True(model.TryImportTestPlan(Plan("[]", "[\"FnP\",\"FnF2\"]")));
        model.CollectCommand.Execute(null);
        await model.CollectCommand.CurrentExecution;
        Assert.Empty(model.Activities);
        Assert.False(model.ShowRecordingGuide);
        Assert.True(model.ShowSpecialKeyProbe);
        Assert.True(model.IsSpecialKeyExpanded);
        Assert.Equal(new[] { SpecialKeyExpectedKey.FnP, SpecialKeyExpectedKey.FnF2 }, model.SpecialKeyChoices.Select(item => item.Key));
        Assert.Equal(SpecialKeyExpectedKey.FnP, model.ExpectedSpecialKey);
        model.ExpectedSpecialKey = SpecialKeyExpectedKey.M1;
        Assert.Equal(SpecialKeyExpectedKey.FnP, model.ExpectedSpecialKey);
        Assert.False(model.StartCommand.CanExecute(null));
        await model.CloseAsync();
    }

    [Fact]
    public void InvalidImportLeavesPreviousSelectionAndConsentUnchanged()
    {
        var model = new CollectorViewModel(true);
        Assert.True(model.TryImportTestPlan(Plan("[\"fans\"]")));
        Assert.False(model.TryImportTestPlan(Plan("[\"C:\\private\"]")));
        Assert.Equal("fans", Assert.Single(model.Activities).Id);
        Assert.Contains("unchanged", model.TestPlanStatus);
        Assert.DoesNotContain("private", model.TestPlanStatus);
        Assert.False(model.AttachConsent);
    }

    [Fact]
    public async Task PlanCannotChangeDuringRecordingOrAfterAReportedStep()
    {
        var model = new CollectorViewModel(true);
        model.TryImportTestPlan(Plan("[\"keyboard-layout\"]"));
        model.CollectCommand.Execute(null);
        await model.CollectCommand.CurrentExecution;
        model.StartingSetting = "ANSI US";
        model.MarkCommand.Execute(null);
        await model.MarkCommand.CurrentExecution;
        Assert.False(model.CanChangeTestPlan);
        Assert.False(model.TryImportTestPlan(Plan("[\"fans\"]")));
        model.ClearTestPlan();
        Assert.Equal("keyboard-layout", Assert.Single(model.Activities).Id);
        await model.CloseAsync();
        var recording = new CollectorViewModel(true);
        recording.SetPreviewStage(2);
        Assert.False(recording.CanChangeTestPlan);
        Assert.False(recording.TryImportTestPlan(Plan("[\"fans\"]")));
        await recording.CloseAsync();
    }

    [Fact]
    public async Task ExportIncludesNormalizedReimportablePlanWithoutImportPath()
    {
        var model = new CollectorViewModel(true);
        model.TryImportTestPlan(Plan("[\"fans\"]", "[\"FnP\"]"));
        model.CollectCommand.Execute(null);
        await model.CollectCommand.CurrentExecution;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".zip");
        try
        {
            using var snapshot = model.Evidence!;
            using var session = new EvidenceSession(snapshot.Inventory);
            session.SetRequestedTestPlan(snapshot.RequestedTestPlan);
            await EvidenceBundleWriter.WriteAsync(session, path);
            using var archive = ZipFile.OpenRead(path);
            using var stream = archive.GetEntry("test-plan.json")!.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var plan = CaptureTestPlan.Parse(buffer.ToArray(), new HashSet<string> { "fans" });
            Assert.Equal("fans", Assert.Single(plan.ActivityIds));
            Assert.Equal(SpecialKeyExpectedKey.FnP, Assert.Single(plan.SpecialKeys));
            using var manifest = JsonDocument.Parse(archive.GetEntry("manifest.json")!.Open());
            Assert.Contains(manifest.RootElement.GetProperty("entries").EnumerateArray(), value => value.GetString() == "test-plan.json");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            await model.CloseAsync();
        }
    }
}
