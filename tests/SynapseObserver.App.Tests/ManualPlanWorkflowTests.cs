using System.IO;
using System.Text;
using SynapseObserver.App;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class ManualPlanWorkflowTests
{
    private static byte[] Plan(bool restore = true, string? target = null) => Encoding.UTF8.GetBytes(
        $$"""
        {"schemaVersion":2,"targetModel":{{(target is null ? "null" : "\"" + target + "\"")}},"activityIds":["fans"],"checks":[
        {"id":"ob-first","kind":"openblade","title":"First check","steps":["Use the available control once."],"expected":"The requested state is shown.","requiresRestoration":{{restore.ToString().ToLowerInvariant()}},"restore":{{(restore ? "\"Restore your original settings.\"" : "null")}}},
        {"id":"ob-second","kind":"openblade","title":"Second check","steps":["Check another available control."],"expected":"The expected behavior is shown.","requiresRestoration":false},
        {"id":"abi-research","kind":"research","title":"Maintainer ABI research","steps":["Review the exact installed stack."],"expected":"A reviewed interface contract.","requiresRestoration":false}],
        "covered":[{"id":"old-lighting","summary":"Existing lighting capture is already retained."}]}
        """);

    private static async Task<CollectorViewModel> Create(bool restore = true, string? target = null)
    {
        var model = new CollectorViewModel(true);
        Assert.True(model.TryImportTestPlan(Plan(restore, target)));
        await Run(model.CollectCommand);
        return model;
    }
    private static async Task Run(AsyncCommand command) { command.Execute(null); await command.CurrentExecution; }

    [Fact]
    public async Task ShippedBlade18PlanImportsWithOnlyRemainingCategories()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SynapseObserver.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var bytes = File.ReadAllBytes(Path.Combine(root.FullName, "docs", "examples", "blade18-remaining-checks.json"));
        Assert.True(bytes.Length <= 14 * 1024);
        var model = new CollectorViewModel(true);
        Assert.True(model.TryImportTestPlan(bytes));
        Assert.Equal("sensor-telemetry", Assert.Single(model.Activities).Id);
        Assert.Empty(model.SpecialKeyChoices);
        Assert.Equal(13, model.ManualChecks.Count);
        Assert.Equal(8, model.ResearchChecks.Count);
        Assert.Equal(8, model.CoveredChecks.Count);
        await Run(model.CollectCommand);
        var destination = Path.Combine(Path.GetTempPath(), $"observer-blade18-{Guid.NewGuid():N}.zip");
        try
        {
            using var snapshot = model.Evidence!;
            await EvidenceBundleWriter.WriteSnapshotAsync(snapshot, destination);
            using var zip = System.IO.Compression.ZipFile.OpenRead(destination);
            using var stream = zip.GetEntry("test-plan.json")!.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var imported = CaptureTestPlan.Parse(buffer.ToArray(), new HashSet<string> { "sensor-telemetry" });
            Assert.Equal(21, imported.Checks.Count);
        }
        finally { File.Delete(destination); }
        await model.CloseAsync();
    }

    [Fact]
    public async Task ManualChecksResearchAndCoveredItemsStaySeparate()
    {
        var model = await Create();
        Assert.Equal(2, model.ManualChecks.Count);
        Assert.Single(model.ResearchChecks);
        Assert.Single(model.CoveredChecks);
        Assert.Equal("fans", Assert.Single(model.Activities).Id);
        Assert.False(model.IsRecording);
        Assert.False(model.AttachConsent);
        Assert.True(model.ShowManualChecks);
        Assert.True(model.ShowResearchChecks);
        await model.CloseAsync();
    }

    [Fact]
    public async Task StartedCheckFreezesBaselineAndRequiresReportedRestoration()
    {
        var model = await Create();
        model.ManualBaseline = "Balanced with fixed fans at 3000 RPM";
        await Run(model.BeginManualCheckCommand);
        Assert.True(model.ManualCheckActive);
        Assert.False(model.CanChangeTestPlan);
        model.ManualBaseline = "Changed after begin";
        model.ManualOutcome = model.ManualOutcomes.Single(item => item.Value == PlanCheckOutcome.Worked);
        Assert.False(model.SaveManualCheckCommand.CanExecute(null));
        model.ManualRestoration = model.ManualRestorations.Single(item => item.Value == PlanRestoration.Restored);
        await Run(model.SaveManualCheckCommand);
        using var evidence = model.Evidence!;
        Assert.Equal(2, evidence.PlanCheckReports.Count);
        Assert.All(evidence.PlanCheckReports, report => Assert.Equal("Balanced with fixed fans at 3000 RPM", report.Baseline));
        Assert.All(evidence.PlanCheckReports, report => Assert.False(report.IsFirmwareReadback));
        Assert.Equal(PlanCheckOutcome.Worked, model.ManualChecks[0].Outcome);
        Assert.False(model.ManualCheckActive);
        await model.CloseAsync();
    }

    [Fact]
    public async Task UnconfirmedRestorationStopsFurtherChecksAndCannotClaimWorked()
    {
        var model = await Create();
        model.ManualBaseline = "Original settings";
        await Run(model.BeginManualCheckCommand);
        model.ManualOutcome = model.ManualOutcomes.Single(item => item.Value == PlanCheckOutcome.Worked);
        model.ManualRestoration = model.ManualRestorations.Single(item => item.Value == PlanRestoration.Unconfirmed);
        Assert.False(model.SaveManualCheckCommand.CanExecute(null));
        model.ManualOutcome = model.ManualOutcomes.Single(item => item.Value == PlanCheckOutcome.Problem);
        await Run(model.SaveManualCheckCommand);
        model.SelectedManualCheckIndex = 1;
        Assert.Equal(0, model.SelectedManualCheckIndex);
        Assert.False(model.BeginManualCheckCommand.CanExecute(null));
        Assert.Contains("Stop testing", model.ManualCheckStatus);
        Assert.False(model.StartCommand.CanExecute(null));
        await model.CloseAsync();
    }

    [Fact]
    public async Task SynapseRecordingAndManualChecksCannotOverlap()
    {
        var model = await Create(false);
        model.AttachConsent = true;
        await Run(model.StartCommand);
        model.ManualBaseline = "Current state";
        Assert.False(model.BeginManualCheckCommand.CanExecute(null));
        await Run(model.StopCommand);
        await Run(model.BeginManualCheckCommand);
        Assert.True(model.ManualCheckActive);
        Assert.False(model.StartCommand.CanExecute(null));
        Assert.False(model.CanSelectActivity);
        model.ManualOutcome = model.ManualOutcomes.Single(item => item.Value == PlanCheckOutcome.Skipped);
        await Run(model.SaveManualCheckCommand);
        using var evidence = model.Evidence!;
        Assert.Equal(PlanRestoration.NotRequired, evidence.PlanCheckReports.Last().Restoration);
        await model.CloseAsync();
    }

    [Fact]
    public async Task WrongLaptopPlanDoesNotEnableOwnerChecks()
    {
        var model = await Create(target: "RZ09-9999");
        model.ManualBaseline = "Current state";
        Assert.False(model.CanEnterManualBaseline);
        Assert.False(model.BeginManualCheckCommand.CanExecute(null));
        Assert.Contains("RZ09-9999", model.ManualCheckStatus);
        await model.CloseAsync();
    }

    [Fact]
    public async Task PrivatePathsAreNotAcceptedAsOwnerReports()
    {
        var model = await Create(false);
        model.ManualBaseline = @"C:\Users\person";
        Assert.False(model.BeginManualCheckCommand.CanExecute(null));
        model.ManualBaseline = "Current state";
        await Run(model.BeginManualCheckCommand);
        model.ManualOutcome = model.ManualOutcomes.Single(item => item.Value == PlanCheckOutcome.Worked);
        model.ManualDetails = @"C:\private";
        Assert.False(model.SaveManualCheckCommand.CanExecute(null));
        using var evidence = model.Evidence!;
        Assert.Single(evidence.PlanCheckReports);
        await model.CloseAsync();
    }
}
