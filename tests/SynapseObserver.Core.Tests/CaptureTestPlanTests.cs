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
    public void Type_CanOnlyBeCreatedByParserAndHasGetOnlyProperties()
    {
        Assert.Empty(typeof(CaptureTestPlan).GetConstructors());
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.ActivityIds))!.SetMethod);
        Assert.Null(typeof(CaptureTestPlan).GetProperty(nameof(CaptureTestPlan.SpecialKeys))!.SetMethod);
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

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":2,\"activityIds\":[\"fans\"]}")]
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

    private static CaptureTestPlan Parse(string json, IReadOnlySet<string>? known = null) =>
        CaptureTestPlan.Parse(Encoding.UTF8.GetBytes(json), known ?? KnownActivities);
}
