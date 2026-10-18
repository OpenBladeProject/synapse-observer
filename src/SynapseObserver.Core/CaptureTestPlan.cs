using System.Text.Json;

namespace SynapseObserver.Core;

public sealed class CaptureTestPlan
{
    public const int SchemaVersion = 1;
    public const int LatestSchemaVersion = 2;
    public const int MaxBytes = 16 * 1024;

    private const int MaxActivityIds = 64;
    private const int MaxSpecialKeys = 20;
    private const int MaxChecks = 24;
    private const int MaxCoveredItems = 24;
    private const int MaxIdentifierCharacters = 128;
    private const int MaxCheckIdentifierCharacters = 80;
    private const int MaxTitleCharacters = 128;
    private const int MaxDisplayTextCharacters = 256;
    private const int MaxSteps = 6;

    private CaptureTestPlan(
        int formatVersion,
        string? targetModel,
        IEnumerable<string> activityIds,
        IEnumerable<SpecialKeyExpectedKey> specialKeys,
        IEnumerable<TestPlanCheck> checks,
        IEnumerable<TestPlanCoveredItem> covered)
    {
        FormatVersion = formatVersion;
        TargetModel = targetModel;
        ActivityIds = Array.AsReadOnly(activityIds.ToArray());
        SpecialKeys = Array.AsReadOnly(specialKeys.ToArray());
        Checks = Array.AsReadOnly(checks.ToArray());
        Covered = Array.AsReadOnly(covered.ToArray());
    }

    private byte[] validatedJson = [];
    internal ReadOnlyMemory<byte> ValidatedJson => validatedJson;

    public int FormatVersion { get; }
    public string? TargetModel { get; }
    public IReadOnlyList<string> ActivityIds { get; }
    public IReadOnlyList<SpecialKeyExpectedKey> SpecialKeys { get; }
    public IReadOnlyList<TestPlanCheck> Checks { get; }
    public IReadOnlyList<TestPlanCoveredItem> Covered { get; }

    public bool MatchesTarget(InventorySnapshot inventory) =>
        TargetModel is null
        || inventory.MachineIdentity.Value.Any(identity =>
            identity.ProductName?.Contains(TargetModel, StringComparison.OrdinalIgnoreCase) == true);

    public static CaptureTestPlan Parse(
        ReadOnlyMemory<byte> utf8,
        IReadOnlySet<string> knownActivityIds)
    {
        ArgumentNullException.ThrowIfNull(knownActivityIds);

        if (utf8.IsEmpty)
        {
            throw Invalid("The test plan is empty.");
        }

        if (utf8.Length > MaxBytes)
        {
            throw Invalid("The test plan exceeds the 16 KiB size limit.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 4,
            });
        }
        catch (JsonException)
        {
            throw Invalid("The test plan is not valid bounded UTF-8 JSON.");
        }

        try
        {
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("The test plan root must be an object.");
                }

                var properties = ParseObjectProperties(root, "The test plan contains a duplicate field.");
                if (!properties.TryGetValue("schemaVersion", out var schemaElement))
                {
                    throw Invalid("The test plan requires schemaVersion 1 or 2.");
                }

                if (schemaElement.ValueKind != JsonValueKind.Number
                    || !schemaElement.TryGetInt32(out var schemaVersion)
                    || schemaVersion is not SchemaVersion and not LatestSchemaVersion)
                {
                    throw Invalid("The test plan schemaVersion must be the integer 1 or 2.");
                }

                var exactKnownActivityIds = new HashSet<string>(knownActivityIds, StringComparer.Ordinal);
                var plan = schemaVersion == SchemaVersion
                    ? ParseVersionOne(properties, exactKnownActivityIds)
                    : ParseVersionTwo(properties, exactKnownActivityIds);
                // Preserve the validated object without adding projection fields or escaping
                // that could push an accepted near-limit v2 plan over the import bound.
                plan.validatedJson = System.Text.Encoding.UTF8.GetBytes(root.GetRawText());
                return plan;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException)
        {
            throw Invalid("The test plan is not valid bounded UTF-8 JSON.");
        }
    }

    private static CaptureTestPlan ParseVersionOne(
        IReadOnlyDictionary<string, JsonElement> properties,
        IReadOnlySet<string> knownActivityIds)
    {
        RejectUnknownProperties(properties, "schemaVersion", "activityIds", "specialKeys");

        var activities = properties.TryGetValue("activityIds", out var activityElement)
            ? ParseActivityIds(activityElement, knownActivityIds)
            : Array.Empty<string>();
        var specialKeys = properties.TryGetValue("specialKeys", out var specialKeyElement)
            ? ParseSpecialKeys(specialKeyElement)
            : Array.Empty<SpecialKeyExpectedKey>();

        if (activities.Count == 0 && specialKeys.Count == 0)
        {
            throw Invalid("The test plan must request at least one activity or special key.");
        }

        return new CaptureTestPlan(
            SchemaVersion,
            targetModel: null,
            activities,
            specialKeys,
            Array.Empty<TestPlanCheck>(),
            Array.Empty<TestPlanCoveredItem>());
    }

    private static CaptureTestPlan ParseVersionTwo(
        IReadOnlyDictionary<string, JsonElement> properties,
        IReadOnlySet<string> knownActivityIds)
    {
        RejectUnknownProperties(
            properties,
            "schemaVersion",
            "targetModel",
            "activityIds",
            "specialKeys",
            "checks",
            "covered");

        var targetModel = properties.TryGetValue("targetModel", out var targetModelElement)
            && targetModelElement.ValueKind != JsonValueKind.Null
            ? ParseTargetModel(targetModelElement)
            : null;
        var activities = properties.TryGetValue("activityIds", out var activityElement)
            ? ParseActivityIds(activityElement, knownActivityIds)
            : Array.Empty<string>();
        var specialKeys = properties.TryGetValue("specialKeys", out var specialKeyElement)
            ? ParseSpecialKeys(specialKeyElement)
            : Array.Empty<SpecialKeyExpectedKey>();

        var identifiers = new HashSet<string>(activities, StringComparer.Ordinal);
        var checks = properties.TryGetValue("checks", out var checksElement)
            ? ParseChecks(checksElement, identifiers)
            : Array.Empty<TestPlanCheck>();
        var covered = properties.TryGetValue("covered", out var coveredElement)
            ? ParseCovered(coveredElement, identifiers)
            : Array.Empty<TestPlanCoveredItem>();

        if (activities.Count == 0 && specialKeys.Count == 0 && checks.Count == 0)
        {
            throw Invalid("The test plan must request at least one activity, special key, or check.");
        }

        return new CaptureTestPlan(LatestSchemaVersion, targetModel, activities, specialKeys, checks, covered);
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseObjectProperties(
        JsonElement value,
        string duplicateMessage)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw Invalid(duplicateMessage);
            }
        }

        return properties;
    }

    private static void RejectUnknownProperties(
        IReadOnlyDictionary<string, JsonElement> properties,
        params string[] allowedNames)
    {
        var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
        if (properties.Keys.Any(name => !allowed.Contains(name)))
        {
            throw Invalid("The test plan contains an unknown field.");
        }
    }

    private static string ParseTargetModel(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid("targetModel must be an exact RZ09 model name.");
        }

        var targetModel = value.GetString()!;
        if (targetModel.Length != 9
            || !targetModel.StartsWith("RZ09-", StringComparison.Ordinal)
            || !targetModel.Skip(5).All(char.IsAsciiDigit))
        {
            throw Invalid("targetModel must match RZ09- followed by four digits.");
        }

        return targetModel;
    }

    private static IReadOnlyList<string> ParseActivityIds(
        JsonElement value,
        IReadOnlySet<string> knownActivityIds)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("activityIds must be an array of known identifiers.");
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= MaxActivityIds)
            {
                throw Invalid("The test plan requests too many activities.");
            }

            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid("Each activityIds entry must be a string.");
            }

            var activityId = item.GetString()!;
            if (activityId.Length is 0 or > MaxIdentifierCharacters)
            {
                throw Invalid("An activity identifier is outside the allowed length.");
            }

            if (!knownActivityIds.Contains(activityId))
            {
                throw Invalid("The test plan contains an unknown activity identifier.");
            }

            if (!seen.Add(activityId))
            {
                throw Invalid("The test plan contains a duplicate activity identifier.");
            }

            result.Add(activityId);
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyList<SpecialKeyExpectedKey> ParseSpecialKeys(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("specialKeys must be an array of exact key names.");
        }

        var result = new List<SpecialKeyExpectedKey>();
        var seen = new HashSet<SpecialKeyExpectedKey>();
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= MaxSpecialKeys)
            {
                throw Invalid("The test plan requests too many special keys.");
            }

            if (item.ValueKind != JsonValueKind.String)
            {
                throw Invalid("Each specialKeys entry must be a string.");
            }

            var name = item.GetString()!;
            if (name.Length is 0 or > MaxIdentifierCharacters
                || !Enum.TryParse(name, ignoreCase: false, out SpecialKeyExpectedKey key)
                || !Enum.IsDefined(key)
                || !string.Equals(Enum.GetName(key), name, StringComparison.Ordinal))
            {
                throw Invalid("The test plan contains an unknown special-key name.");
            }

            if (!seen.Add(key))
            {
                throw Invalid("The test plan contains a duplicate special key.");
            }

            result.Add(key);
        }

        return result.AsReadOnly();
    }

    private static IReadOnlyList<TestPlanCheck> ParseChecks(JsonElement value, ISet<string> identifiers)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("checks must be an array.");
        }

        var result = new List<TestPlanCheck>();
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= MaxChecks)
            {
                throw Invalid("The test plan contains too many checks.");
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Each checks entry must be an object.");
            }

            var properties = ParseObjectProperties(item, "A check contains a duplicate field.");
            RejectUnknownCheckProperties(properties);

            var id = ParseRequiredToken(properties, "id", MaxCheckIdentifierCharacters, "check");
            if (!identifiers.Add(id))
            {
                throw Invalid("The test plan contains a duplicate identifier.");
            }

            var kind = ParseCheckKind(GetRequired(properties, "kind", "check"));
            var title = ParseRequiredDisplayText(properties, "title", MaxTitleCharacters, "check");
            var steps = ParseSteps(GetRequired(properties, "steps", "check"));
            var expected = ParseRequiredDisplayText(
                properties,
                "expected",
                MaxDisplayTextCharacters,
                "check");
            var requiresRestoration = ParseRequiredBoolean(properties, "requiresRestoration", "check");
            var restore = ParseRestore(properties, requiresRestoration);

            if (kind == PlanCheckKind.Research && requiresRestoration)
            {
                throw Invalid("Research checks cannot require user restoration.");
            }

            result.Add(TestPlanCheck.Create(
                id,
                kind,
                title,
                steps,
                expected,
                requiresRestoration,
                restore));
        }

        return result.AsReadOnly();
    }

    private static void RejectUnknownCheckProperties(IReadOnlyDictionary<string, JsonElement> properties)
    {
        var allowed = new HashSet<string>(
            ["id", "kind", "title", "steps", "expected", "requiresRestoration", "restore"],
            StringComparer.Ordinal);
        if (properties.Keys.Any(name => !allowed.Contains(name)))
        {
            throw Invalid("A check contains an unknown field.");
        }
    }

    private static PlanCheckKind ParseCheckKind(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid("A check kind must be a string.");
        }

        return value.GetString() switch
        {
            "openblade" => PlanCheckKind.OpenBlade,
            "research" => PlanCheckKind.Research,
            _ => throw Invalid("A check kind must be exactly openblade or research."),
        };
    }

    private static IReadOnlyList<string> ParseSteps(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("A check steps value must be an array.");
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= MaxSteps)
            {
                throw Invalid("A check may contain at most six steps.");
            }

            result.Add(ParseDisplayText(item, MaxDisplayTextCharacters, "A check step"));
        }

        if (result.Count == 0)
        {
            throw Invalid("A check requires at least one step.");
        }

        return result.AsReadOnly();
    }

    private static string? ParseRestore(
        IReadOnlyDictionary<string, JsonElement> properties,
        bool requiresRestoration)
    {
        if (!properties.TryGetValue("restore", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (requiresRestoration)
            {
                throw Invalid("A check that requires restoration must include restore text.");
            }

            return null;
        }

        if (!requiresRestoration)
        {
            throw Invalid("A check without restoration must omit restore text or set it to null.");
        }

        return ParseDisplayText(value, MaxDisplayTextCharacters, "A check restore value");
    }

    private static IReadOnlyList<TestPlanCoveredItem> ParseCovered(JsonElement value, ISet<string> identifiers)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("covered must be an array.");
        }

        var result = new List<TestPlanCoveredItem>();
        foreach (var item in value.EnumerateArray())
        {
            if (result.Count >= MaxCoveredItems)
            {
                throw Invalid("The test plan contains too many covered entries.");
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("Each covered entry must be an object.");
            }

            var properties = ParseObjectProperties(item, "A covered entry contains a duplicate field.");
            if (properties.Count != 2
                || properties.Keys.Any(name => name is not "id" and not "summary"))
            {
                throw Invalid("A covered entry must contain exactly id and summary.");
            }

            var id = ParseRequiredToken(properties, "id", MaxCheckIdentifierCharacters, "covered entry");
            if (!identifiers.Add(id))
            {
                throw Invalid("The test plan contains a duplicate identifier.");
            }

            var summary = ParseRequiredDisplayText(
                properties,
                "summary",
                MaxDisplayTextCharacters,
                "covered entry");
            result.Add(TestPlanCoveredItem.Create(id, summary));
        }

        return result.AsReadOnly();
    }

    private static JsonElement GetRequired(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string owner)
    {
        if (!properties.TryGetValue(name, out var value))
        {
            throw Invalid($"A {owner} requires {name}.");
        }

        return value;
    }

    private static string ParseRequiredToken(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        int maxCharacters,
        string owner)
    {
        var value = GetRequired(properties, name, owner);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"A {owner} {name} must be a string.");
        }

        var text = value.GetString()!;
        if (text.Length > maxCharacters || !EvidenceText.IsToken(text))
        {
            throw Invalid($"A {owner} {name} is not a valid bounded identifier.");
        }

        return text;
    }

    private static string ParseRequiredDisplayText(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        int maxCharacters,
        string owner) =>
        ParseDisplayText(GetRequired(properties, name, owner), maxCharacters, $"A {owner} {name}");

    private static string ParseDisplayText(JsonElement value, int maxCharacters, string description)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"{description} must be a string.");
        }

        var text = value.GetString()!;
        if (text.Length > maxCharacters || !EvidenceText.IsDisplayText(text))
        {
            throw Invalid($"{description} is not valid bounded display text.");
        }

        return text;
    }

    private static bool ParseRequiredBoolean(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        string owner)
    {
        var value = GetRequired(properties, name, owner);
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw Invalid($"A {owner} {name} must be a Boolean.");
        }

        return value.GetBoolean();
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
