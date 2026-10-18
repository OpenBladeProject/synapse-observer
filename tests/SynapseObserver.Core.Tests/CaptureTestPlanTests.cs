using System.Text;

namespace SynapseObserver.Core.Tests;

public sealed class CaptureTestPlanTests
{
    private static readonly IReadOnlySet<string> KnownActivities =
        new HashSet<string>(["inventory", "performance", "fans"], StringComparer.Ordinal);

    [Fact]
    public void Parse_ActivitiesOnly_PreservesOrder()
    {
        var plan = Parse("""
            {"schemaVersion":1,"activityIds":["fans","inventory","performance"]}
            """);

        Assert.Equal(["fans", "inventory", "performance"], plan.ActivityIds);
        Assert.Empty(plan.SpecialKeys);
        Assert.Equal(1, plan.FormatVersion);
        Assert.Null(plan.TargetModel);
        Assert.Empty(plan.Checks);
        Assert.Empty(plan.Covered);
    }

    [Fact]
    public void Parse_SpecialKeysOnly_UsesExactEnumNamesAndPreservesOrder()
    {
        var plan = Parse("""
            {"schemaVersion":1,"specialKeys":["FnP","FnF1","M1"]}
            """);

        Assert.Empty(plan.ActivityIds);
        Assert.Equal(
            [SpecialKeyExpectedKey.FnP, SpecialKeyExpectedKey.FnF1, SpecialKeyExpectedKey.M1],
            plan.SpecialKeys);
    }

    [Fact]
    public void Parse_MixedPlan_PreservesBothOrders()
    {
        var plan = Parse("""
            {"schemaVersion":1,"specialKeys":["M5","FnR"],"activityIds":["performance","fans"]}
            """);

        Assert.Equal(["performance", "fans"], plan.ActivityIds);
        Assert.Equal([SpecialKeyExpectedKey.M5, SpecialKeyExpectedKey.FnR], plan.SpecialKeys);
    }

    [Fact]
    public void Parse_VersionTwo_PreservesAllBoundedFields()
    {
        var plan = Parse("""
            {
              "schemaVersion": 2,
              "targetModel": "RZ09-0581",
              "activityIds": ["fans"],
              "specialKeys": ["FnP"],
              "checks": [
                {
                  "id": "manual-fan-round-trip",
                  "kind": "openblade",
                  "title": "Confirm manual fan control",
                  "steps": ["Save the current setting", "Apply one fan value"],
                  "expected": "OpenBlade reports the selected value",
                  "requiresRestoration": true,
                  "restore": "Restore the saved fan setting"
                },
                {
                  "id": "research-vendor-definition",
                  "kind": "research",
                  "title": "Review the vendor definition",
                  "steps": ["Compare the exact model definition"],
                  "expected": "The command shape is documented",
                  "requiresRestoration": false,
                  "restore": null
                }
              ],
              "covered": [
                {"id": "covered-inventory", "summary": "Inventory evidence already reviewed"}
              ]
            }
            """);

        Assert.Equal(2, plan.FormatVersion);
        Assert.Equal("RZ09-0581", plan.TargetModel);
        Assert.Equal(["fans"], plan.ActivityIds);
        Assert.Equal([SpecialKeyExpectedKey.FnP], plan.SpecialKeys);
        Assert.Collection(
            plan.Checks,
            check =>
            {
                Assert.Equal("manual-fan-round-trip", check.Id);
                Assert.Equal(PlanCheckKind.OpenBlade, check.Kind);
                Assert.Equal("Confirm manual fan control", check.Title);
                Assert.Equal(["Save the current setting", "Apply one fan value"], check.Steps);
                Assert.Equal("OpenBlade reports the selected value", check.Expected);
                Assert.True(check.RequiresRestoration);
                Assert.Equal("Restore the saved fan setting", check.Restore);
            },
            check =>
            {
                Assert.Equal("research-vendor-definition", check.Id);
                Assert.Equal(PlanCheckKind.Research, check.Kind);
                Assert.False(check.RequiresRestoration);
                Assert.Null(check.Restore);
            });
        var covered = Assert.Single(plan.Covered);
        Assert.Equal("covered-inventory", covered.Id);
        Assert.Equal("Inventory evidence already reviewed", covered.Summary);
    }

    [Fact]
    public void Parse_VersionTwo_AcceptsResearchOnlyPlanAndNullTargetModel()
    {
        var plan = Parse("""
            {
              "schemaVersion": 2,
              "targetModel": null,
              "checks": [{
                "id": "research-source",
                "kind": "research",
                "title": "Review source",
                "steps": ["Read the exact model definition"],
                "expected": "The finding is documented",
                "requiresRestoration": false
              }]
            }
            """);

        Assert.Null(plan.TargetModel);
        Assert.Equal(PlanCheckKind.Research, Assert.Single(plan.Checks).Kind);
    }

    [Fact]
    public void MatchesTarget_RequiresEveryProductNameAndAllowsUnscopedPlans()
    {
        var scoped = Parse("{\"schemaVersion\":2,\"targetModel\":\"RZ09-0581\",\"activityIds\":[\"fans\"]}");
        var unscoped = Parse("{\"schemaVersion\":2,\"activityIds\":[\"fans\"]}");

        Assert.True(scoped.MatchesTarget(Inventory("Razer Blade 16 RZ09-0581")));
        Assert.True(scoped.MatchesTarget(Inventory("razer blade 16 rz09-0581")));
        Assert.True(scoped.MatchesTarget(Inventory("Razer Blade 16 RZ09-0581 SKU variant")));
        Assert.False(scoped.MatchesTarget(Inventory("Razer Blade 16 RZ09-0528")));
        Assert.True(unscoped.MatchesTarget(Inventory("Razer Blade 16 RZ09-0528")));
        Assert.True(unscoped.MatchesTarget(Inventory(CollectionState.Partial)));
    }

    [Fact]
    public void MatchesTarget_RejectsMixedOrBlankProductNames()
    {
        var scoped = Parse("{\"schemaVersion\":2,\"targetModel\":\"RZ09-0581\",\"activityIds\":[\"fans\"]}");

        Assert.False(scoped.MatchesTarget(Inventory(
            CollectionState.Collected,
            "Razer Blade 16 RZ09-0581",
            "Razer Blade 16 RZ09-0528")));
        Assert.False(scoped.MatchesTarget(Inventory(
            CollectionState.Collected,
            "Razer Blade 16 RZ09-0581",
            " ")));
        Assert.False(scoped.MatchesTarget(Inventory(
            CollectionState.Collected,
            "Razer Blade 16 RZ09-0581",
            null)));
    }

    [Fact]
    public void MatchesTarget_RejectsPartialOrMissingIdentityRecords()
    {
        var scoped = Parse("{\"schemaVersion\":2,\"targetModel\":\"RZ09-0581\",\"activityIds\":[\"fans\"]}");

        Assert.False(scoped.MatchesTarget(Inventory(CollectionState.Partial, "Razer Blade 16 RZ09-0581")));
        Assert.False(scoped.MatchesTarget(Inventory(CollectionState.Collected)));
        Assert.False(scoped.MatchesTarget(Inventory(CollectionState.Unavailable)));
    }

    [Fact]
    public void Type_CanOnlyBeCreatedByParserAndHasGetOnlyProperties()
    {
        Assert.Empty(typeof(CaptureTestPlan).GetConstructors());
        Assert.Empty(typeof(TestPlanCheck).GetConstructors());
        Assert.Empty(typeof(TestPlanCoveredItem).GetConstructors());
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.FormatVersion))!.SetMethod);
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.TargetModel))!.SetMethod);
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.ActivityIds))!.SetMethod);
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.SpecialKeys))!.SetMethod);
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.Checks))!.SetMethod);
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.Covered))!.SetMethod);
        Assert.Null(typeof(TestPlanCheck).GetProperty(nameof(TestPlanCheck.Steps))!.SetMethod);
    }

    [Fact]
    public void Parse_ReturnsImmutableCollections()
    {
        var plan = Parse("""
            {"schemaVersion":1,"activityIds":["fans"],"specialKeys":["FnP"]}
            """);

        var activities = Assert.IsAssignableFrom<IList<string>>(plan.ActivityIds);
        var specialKeys = Assert.IsAssignableFrom<IList<SpecialKeyExpectedKey>>(plan.SpecialKeys);
        Assert.True(activities.IsReadOnly);
        Assert.True(specialKeys.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => activities[0] = "inventory");
        Assert.Throws<NotSupportedException>(() => specialKeys[0] = SpecialKeyExpectedKey.M1);
    }

    [Fact]
    public void Parse_VersionTwo_ReturnsImmutableCollections()
    {
        var plan = Parse("""
            {
              "schemaVersion": 2,
              "checks": [{
                "id": "manual-check",
                "kind": "openblade",
                "title": "Run one check",
                "steps": ["Apply the selected value"],
                "expected": "The selected value is reported",
                "requiresRestoration": false
              }],
              "covered": [{"id": "covered-check", "summary": "Previous evidence is reviewed"}]
            }
            """);

        var checks = Assert.IsAssignableFrom<IList<TestPlanCheck>>(plan.Checks);
        var covered = Assert.IsAssignableFrom<IList<TestPlanCoveredItem>>(plan.Covered);
        var steps = Assert.IsAssignableFrom<IList<string>>(checks[0].Steps);
        Assert.True(checks.IsReadOnly);
        Assert.True(covered.IsReadOnly);
        Assert.True(steps.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => checks[0] = checks[0]);
        Assert.Throws<NotSupportedException>(() => covered[0] = covered[0]);
        Assert.Throws<NotSupportedException>(() => steps[0] = "Changed");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":3,\"activityIds\":[\"fans\"]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"activityIds\":[\"fans\"]}")]
    [InlineData("{\"schemaVersion\":1.0,\"activityIds\":[\"fans\"]}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1,\"activityIds\":[\"fans\"]}")]
    [InlineData("{\"schemaVersion\":1,\"command\":\"capture\",\"activityIds\":[\"fans\"]}")]
    [InlineData("{\"schemaVersion\":1,\"activityIds\":\"fans\"}")]
    [InlineData("{\"schemaVersion\":1,\"specialKeys\":\"FnP\"}")]
    [InlineData("{\"schemaVersion\":1,\"activityIds\":[1]}")]
    [InlineData("{\"schemaVersion\":1,\"specialKeys\":[1]}")]
    [InlineData("{\"schemaVersion\":1,\"activityIds\":[\"fans\",\"fans\"]}")]
    [InlineData("{\"schemaVersion\":1,\"specialKeys\":[\"FnP\",\"FnP\"]}")]
    [InlineData("{\"schemaVersion\":1,\"specialKeys\":[\"fnp\"]}")]
    [InlineData("{\"schemaVersion\":1,\"specialKeys\":[\"0\"]}")]
    [InlineData("{\"schemaVersion\":1,\"specialKeys\":[\"Unknown\"]}")]
    [InlineData("{\"schemaVersion\":1,\"activityIds\":[\"\\uD800\"]}")]
    [InlineData("{\"schemaVersion\":1,\"activityIds\":[\"unknown\"]}")]
    [InlineData("{\"schemaVersion\":1,\"activityIds\":[\"fans\"],}")]
    [InlineData("{\"schemaVersion\":1,/* comment */\"activityIds\":[\"fans\"]}")]
    public void Parse_RejectsInvalidShapeOrValue(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    public static TheoryData<string> InvalidVersionTwoPlans => new()
    {
        "{\"schemaVersion\":2}",
        "{\"schemaVersion\":2,\"covered\":[{\"id\":\"old-check\",\"summary\":\"Already reviewed\"}]}",
        "{\"schemaVersion\":2,\"targetModel\":\"RZ09-581\",\"activityIds\":[\"fans\"]}",
        "{\"schemaVersion\":2,\"targetModel\":581,\"activityIds\":[\"fans\"]}",
        "{\"schemaVersion\":2,\"activityIds\":[\"fans\"],\"command\":\"capture\"}",
        "{\"schemaVersion\":2,\"checks\":{}}",
        "{\"schemaVersion\":2,\"checks\":[1]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"OpenBlade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual/path\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"openblade\",\"title\":\"Bad/path\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[],\"expected\":\"Expected\",\"requiresRestoration\":false}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\"],\"expected\":\"Expected\",\"requiresRestoration\":false}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":\"false\"}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":true}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false,\"restore\":\"Restore\"}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"research\",\"kind\":\"research\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":true,\"restore\":\"Restore\"}]}",
        "{\"schemaVersion\":2,\"activityIds\":[\"fans\"],\"checks\":[{\"id\":\"fans\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"same\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false}],\"covered\":[{\"id\":\"same\",\"summary\":\"Reviewed\"}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false,\"extra\":1}]}",
        "{\"schemaVersion\":2,\"checks\":[{\"id\":\"manual\",\"id\":\"other\",\"kind\":\"openblade\",\"title\":\"Title\",\"steps\":[\"Step\"],\"expected\":\"Expected\",\"requiresRestoration\":false}]}",
        "{\"schemaVersion\":2,\"activityIds\":[\"fans\"],\"covered\":[{\"id\":\"covered\",\"summary\":\"Bad/path\"}]}",
        "{\"schemaVersion\":2,\"activityIds\":[\"fans\"],\"covered\":[{\"id\":\"covered\"}]}",
    };

    [Theory]
    [MemberData(nameof(InvalidVersionTwoPlans))]
    public void Parse_VersionTwo_RejectsInvalidOrInconsistentShape(string json)
    {
        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void Parse_VersionTwo_RejectsBoundedScalarOverflow()
    {
        var identifier = new string('a', 81);
        var title = new string('t', 129);
        var text = new string('x', 257);

        Assert.Throws<InvalidDataException>(() => Parse(OpenBladeCheck(identifier, "Title", "Step", "Expected")));
        Assert.Throws<InvalidDataException>(() => Parse(OpenBladeCheck("manual", title, "Step", "Expected")));
        Assert.Throws<InvalidDataException>(() => Parse(OpenBladeCheck("manual", "Title", text, "Expected")));
        Assert.Throws<InvalidDataException>(() => Parse(OpenBladeCheck("manual", "Title", "Step", text)));
        Assert.Throws<InvalidDataException>(() => Parse($$"""
            {"schemaVersion":2,"activityIds":["fans"],"covered":[{"id":"covered","summary":"{{text}}"}]}
            """));
    }

    [Fact]
    public void Parse_VersionTwo_RejectsMoreThanTwentyFourChecksOrCoveredEntries()
    {
        var checks = string.Join(',', Enumerable.Range(0, 25).Select(index => $$"""
            {"id":"manual-{{index}}","kind":"openblade","title":"Title","steps":["Step"],"expected":"Expected","requiresRestoration":false}
            """));
        var covered = string.Join(',', Enumerable.Range(0, 25).Select(index =>
            $$"""{"id":"covered-{{index}}","summary":"Reviewed"}"""));

        Assert.Throws<InvalidDataException>(() => Parse($$"""{"schemaVersion":2,"checks":[{{checks}}]}"""));
        Assert.Throws<InvalidDataException>(() => Parse(
            $$"""{"schemaVersion":2,"activityIds":["fans"],"covered":[{{covered}}]}"""));
    }

    [Fact]
    public void Parse_RequiresOrdinalActivityIdentifierMatch()
    {
        IReadOnlySet<string> caseInsensitiveKnown =
            new HashSet<string>(["fans"], StringComparer.OrdinalIgnoreCase);

        Assert.Throws<InvalidDataException>(() => Parse(
            "{\"schemaVersion\":1,\"activityIds\":[\"Fans\"]}",
            caseInsensitiveKnown));
    }

    [Fact]
    public void Parse_RejectsIdentifierOverLengthWithoutEchoingIt()
    {
        var privateValue = new string('x', 129);
        var error = Assert.Throws<InvalidDataException>(() => Parse(
            $"{{\"schemaVersion\":1,\"activityIds\":[\"{privateValue}\"]}}"));

        Assert.DoesNotContain(privateValue, error.Message, StringComparison.Ordinal);
        Assert.True(error.Message.Length < 160);
    }

    [Fact]
    public void Parse_RejectsOversizeInputBeforeJsonParsing()
    {
        var bytes = new byte[CaptureTestPlan.MaxBytes + 1];

        Assert.Throws<InvalidDataException>(() => CaptureTestPlan.Parse(bytes, KnownActivities));
    }

    [Fact]
    public void Parse_RejectsMalformedUtf8PropertyNameWithBoundedError()
    {
        byte[] bytes = [(byte)'{', (byte)'\"', 0xFF, (byte)'\"', (byte)':', (byte)'1', (byte)'}'];

        var error = Assert.Throws<InvalidDataException>(() => CaptureTestPlan.Parse(bytes, KnownActivities));

        Assert.True(error.Message.Length < 160);
        Assert.DoesNotContain("BytePositionInLine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsMalformedUtf8StringValueWithBoundedError()
    {
        var prefix = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"activityIds\":[\"");
        var suffix = Encoding.UTF8.GetBytes("\"]}");
        byte[] bytes = [.. prefix, 0xFF, .. suffix];

        var error = Assert.Throws<InvalidDataException>(() => CaptureTestPlan.Parse(bytes, KnownActivities));

        Assert.True(error.Message.Length < 160);
        Assert.DoesNotContain("BytePositionInLine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsExcessiveJsonDepth()
    {
        const string json = "{\"schemaVersion\":1,\"activityIds\":[[[[[\"fans\"]]]]]}";

        Assert.Throws<InvalidDataException>(() => Parse(json));
    }

    [Fact]
    public void Parse_RejectsMoreThanBoundedActivityCount()
    {
        var known = Enumerable.Range(0, 65).Select(index => $"activity-{index}").ToHashSet(StringComparer.Ordinal);
        var entries = string.Join(',', known.Select(id => $"\"{id}\""));

        Assert.Throws<InvalidDataException>(() => Parse(
            $"{{\"schemaVersion\":1,\"activityIds\":[{entries}]}}",
            known));
    }

    [Fact]
    public void Parse_AcceptsAllTwentyDistinctSpecialKeys()
    {
        var names = Enum.GetNames<SpecialKeyExpectedKey>();
        var entries = string.Join(',', names.Select(name => $"\"{name}\""));

        var plan = Parse($"{{\"schemaVersion\":1,\"specialKeys\":[{entries}]}}");

        Assert.Equal(20, plan.SpecialKeys.Count);
    }

    [Fact]
    public void Parse_RejectsMoreThanBoundedSpecialKeyCount()
    {
        var names = Enum.GetNames<SpecialKeyExpectedKey>().Append("M1");
        var entries = string.Join(',', names.Select(name => $"\"{name}\""));

        Assert.Throws<InvalidDataException>(() => Parse(
            $"{{\"schemaVersion\":1,\"specialKeys\":[{entries}]}}"));
    }

    private static string OpenBladeCheck(string id, string title, string step, string expected) => $$"""
        {"schemaVersion":2,"checks":[{"id":"{{id}}","kind":"openblade","title":"{{title}}","steps":["{{step}}"],"expected":"{{expected}}","requiresRestoration":false}]}
        """;

    private static InventorySnapshot Inventory(string productName) =>
        Inventory(CollectionState.Collected, productName);

    private static InventorySnapshot Inventory(CollectionState state, params string?[] productNames) => new(
        InventorySnapshot.CurrentSchemaVersion,
        DateTimeOffset.UnixEpoch,
        new SectionResult<IReadOnlyList<MachineIdentity>>(
            state,
            productNames.Select((productName, index) =>
                new MachineIdentity($"Registry{index}", "Razer", productName, null, null, null)).ToArray(),
            state == CollectionState.Collected ? null : "Identity collection did not complete."),
        SectionResult<PowerSourceEvidence?>.Absent(null),
        SectionResult<IReadOnlyList<HidInterfaceEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<SynapseProcessEvidence>>.Absent([]));

    private static CaptureTestPlan Parse(string json, IReadOnlySet<string>? known = null) =>
        CaptureTestPlan.Parse(Encoding.UTF8.GetBytes(json), known ?? KnownActivities);
}
