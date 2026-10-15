using System.Globalization;
using System.Text.Json;

namespace SynapseObserver.Core;

public static class ControlObservationParser
{
    public static bool TryParseUtf8(
        ReadOnlySpan<byte> utf8Json,
        out ControlObservation? observation,
        out string? error)
    {
        observation = null;
        error = null;
        if (utf8Json.IsEmpty || utf8Json.Length > EvidenceLimits.MaxObservationEventBytes)
        {
            error = "Observation event size is outside the documented bounds.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "operationId", out var operationId)
                || !TryGetDouble(root, "relativeMilliseconds", out var relativeMilliseconds)
                || !TryGetEnum(root, "source", out ObservationSource source)
                || !TryGetString(root, "api", out var api)
                || !TryGetEnum(root, "direction", out ObservationDirection direction)
                || !TryGetEnum(root, "status", out ObservationStatus status)
                || !TryGetUInt16(root, "vendorId", out var vendorId)
                || !TryGetUInt16(root, "productId", out var productId)
                || !TryGetString(root, "interfaceLabel", out var interfaceLabel)
                || !TryGetEnum(root, "payloadDisposition", out PayloadDisposition payloadDisposition))
            {
                error = "Observation event is missing a required field or contains an invalid value.";
                return false;
            }

            if (!TryGetNullableInt32(root, "nativeStatus", out var nativeStatus)
                || !TryGetNullableString(root, "errorCategory", out var errorCategory)
                || !TryGetNullableUInt16(root, "usagePage", out var usagePage)
                || !TryGetNullableUInt16(root, "usage", out var usage)
                || !TryGetNullableByte(root, "reportId", out var reportId)
                || !TryGetNullableDouble(root, "completedRelativeMilliseconds", out var completedRelativeMilliseconds)
                || !TryGetHex(root, "bufferHex", out var buffer))
            {
                error = "Observation event contains malformed optional data.";
                return false;
            }

            if (completedRelativeMilliseconds is not null
                && completedRelativeMilliseconds.Value < relativeMilliseconds)
            {
                error = "Observation completion time precedes its entry time.";
                return false;
            }

            observation = new ControlObservation(
                operationId,
                relativeMilliseconds,
                source,
                api,
                direction,
                status,
                nativeStatus,
                errorCategory,
                vendorId,
                productId,
                interfaceLabel,
                usagePage,
                usage,
                reportId,
                buffer,
                payloadDisposition,
                completedRelativeMilliseconds);
            return true;
        }
        catch (JsonException)
        {
            error = "Observation event is not valid JSON.";
            return false;
        }
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && (value = property.GetString() ?? string.Empty).Length > 0;
    }

    private static bool TryGetDouble(JsonElement root, string name, out double value)
    {
        value = 0;
        return root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool TryGetUInt16(JsonElement root, string name, out ushort value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetUInt16(out value))
        {
            return true;
        }

        return property.ValueKind == JsonValueKind.String
            && ushort.TryParse(
                property.GetString(),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static bool TryGetEnum<T>(JsonElement root, string name, out T value)
        where T : struct, Enum
    {
        value = default;
        return root.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && Enum.TryParse(property.GetString(), ignoreCase: true, out value)
            && Enum.IsDefined(value);
    }

    private static bool TryGetNullableString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetNullableInt32(JsonElement root, string name, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetNullableUInt16(JsonElement root, string name, out ushort? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetUInt16(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetNullableByte(JsonElement root, string name, out byte? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetByte(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetNullableDouble(JsonElement root, string name, out double? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number
            || !property.TryGetDouble(out var parsed)
            || !double.IsFinite(parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetHex(JsonElement root, string name, out ReadOnlyMemory<byte> value)
    {
        value = ReadOnlyMemory<byte>.Empty;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var hex = property.GetString();
        if (hex is null
            || hex.Length % 2 != 0
            || hex.Length > EvidenceLimits.MaxPayloadBytesPerObservation * 2)
        {
            return false;
        }

        try
        {
            value = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
