using System.Text.Json;

namespace SynapseObserver.Core;

public sealed class CaptureTestPlan
{
    public const int SchemaVersion = 1;
    public const int MaxBytes = 16 * 1024;

    private const int MaxActivityIds = 64;
    private const int MaxSpecialKeys = 20;
    private const int MaxIdentifierCharacters = 128;

    private CaptureTestPlan(
        IEnumerable<string> activityIds,
        IEnumerable<SpecialKeyExpectedKey> specialKeys)
    {
        ActivityIds = Array.AsReadOnly(activityIds.ToArray());
        SpecialKeys = Array.AsReadOnly(specialKeys.ToArray());
    }

    public IReadOnlyList<string> ActivityIds { get; }
    public IReadOnlyList<SpecialKeyExpectedKey> SpecialKeys { get; }

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

                var exactKnownActivityIds = new HashSet<string>(knownActivityIds, StringComparer.Ordinal);
                var seenProperties = new HashSet<string>(StringComparer.Ordinal);
                IReadOnlyList<string> activities = Array.Empty<string>();
                IReadOnlyList<SpecialKeyExpectedKey> specialKeys = Array.Empty<SpecialKeyExpectedKey>();
                var hasSchemaVersion = false;

                foreach (var property in root.EnumerateObject())
                {
                    if (!seenProperties.Add(property.Name))
                    {
                        throw Invalid("The test plan contains a duplicate field.");
                    }

                    switch (property.Name)
                    {
                        case "schemaVersion":
                            if (property.Value.ValueKind != JsonValueKind.Number
                                || !property.Value.TryGetInt32(out var schemaVersion)
                                || schemaVersion != SchemaVersion)
                            {
                                throw Invalid("The test plan schemaVersion must be the integer 1.");
                            }

                            hasSchemaVersion = true;
                            break;
                        case "activityIds":
                            activities = ParseActivityIds(property.Value, exactKnownActivityIds);
                            break;
                        case "specialKeys":
                            specialKeys = ParseSpecialKeys(property.Value);
                            break;
                        default:
                            throw Invalid("The test plan contains an unknown field.");
                    }
                }

                if (!hasSchemaVersion)
                {
                    throw Invalid("The test plan requires schemaVersion 1.");
                }

                if (activities.Count == 0 && specialKeys.Count == 0)
                {
                    throw Invalid("The test plan must request at least one activity or special key.");
                }

                return new CaptureTestPlan(activities, specialKeys);
            }
        }
        catch (InvalidOperationException)
        {
            throw Invalid("The test plan is not valid bounded UTF-8 JSON.");
        }
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

    private static InvalidDataException Invalid(string message) => new(message);
}
