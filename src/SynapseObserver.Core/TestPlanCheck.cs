namespace SynapseObserver.Core;

public enum PlanCheckKind
{
    OpenBlade,
    Research,
}

public sealed class TestPlanCheck
{
    private TestPlanCheck(
        string id,
        PlanCheckKind kind,
        string title,
        IEnumerable<string> steps,
        string expected,
        bool requiresRestoration,
        string? restore)
    {
        Id = id;
        Kind = kind;
        Title = title;
        Steps = Array.AsReadOnly(steps.ToArray());
        Expected = expected;
        RequiresRestoration = requiresRestoration;
        Restore = restore;
    }

    public string Id { get; }
    public PlanCheckKind Kind { get; }
    public string Title { get; }
    public IReadOnlyList<string> Steps { get; }
    public string Expected { get; }
    public bool RequiresRestoration { get; }
    public string? Restore { get; }

    internal static TestPlanCheck Create(
        string id,
        PlanCheckKind kind,
        string title,
        IEnumerable<string> steps,
        string expected,
        bool requiresRestoration,
        string? restore) =>
        new(id, kind, title, steps, expected, requiresRestoration, restore);
}

public sealed class TestPlanCoveredItem
{
    private TestPlanCoveredItem(string id, string summary)
    {
        Id = id;
        Summary = summary;
    }

    public string Id { get; }
    public string Summary { get; }

    internal static TestPlanCoveredItem Create(string id, string summary) => new(id, summary);
}
