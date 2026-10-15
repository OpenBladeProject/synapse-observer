namespace SynapseObserver.Core;

public enum ReviewedFeatureResponseKind
{
    ThermalSetterAcknowledgement,
}

public enum ReviewedFeatureResponseStatus
{
    Successful,
}

public enum OpaquePrefixDisposition
{
    Excluded,
}

public enum ReviewedThermalChannel
{
    First,
    Second,
}

public enum ReviewedThermalMode
{
    BalancedAutomatic,
    SilentAutomatic,
    PerformanceAutomatic,
    FixedMaximumModeZero,
    FixedMaximumModeFour,
    BatterySaverAutomatic,
    BatteryBalancedAutomatic,
    CustomAutomatic,
    HyperBoostObserved,
}

/// <summary>
/// Closed semantic fields from a reviewed thermal setter acknowledgement whose
/// opaque leading byte and unavailable final padding byte are never exported.
/// </summary>
public sealed record ReviewedFeatureResponseProjection(
    ReviewedFeatureResponseKind Kind,
    ReviewedFeatureResponseStatus Status,
    byte TransactionId,
    ReviewedThermalChannel ThermalChannel,
    ReviewedThermalMode ThermalMode,
    FeatureResponseCompleteness Completeness,
    OpaquePrefixDisposition OpaquePrefixDisposition);

internal static class ReviewedThermalEncoding
{
    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        out ReviewedThermalChannel channel,
        out ReviewedThermalMode mode)
    {
        channel = default;
        mode = default;
        if (payload.Length != 4 || payload[0] != 0x01)
        {
            return false;
        }

        channel = payload[1] switch
        {
            0x01 => ReviewedThermalChannel.First,
            0x02 => ReviewedThermalChannel.Second,
            _ => default,
        };
        if (payload[1] is not (0x01 or 0x02))
        {
            return false;
        }

        return (payload[2], payload[3]) switch
        {
            (0x00, 0x00) => Assign(ReviewedThermalMode.BalancedAutomatic, out mode),
            (0x05, 0x00) => Assign(ReviewedThermalMode.SilentAutomatic, out mode),
            (0x02, 0x00) => Assign(ReviewedThermalMode.PerformanceAutomatic, out mode),
            (0x00, 0x01) => Assign(ReviewedThermalMode.FixedMaximumModeZero, out mode),
            (0x04, 0x01) => Assign(ReviewedThermalMode.FixedMaximumModeFour, out mode),
            (0x03, 0x00) => Assign(ReviewedThermalMode.BatterySaverAutomatic, out mode),
            (0x06, 0x00) => Assign(ReviewedThermalMode.BatteryBalancedAutomatic, out mode),
            (0x04, 0x00) => Assign(ReviewedThermalMode.CustomAutomatic, out mode),
            (0x07, 0x00) => Assign(ReviewedThermalMode.HyperBoostObserved, out mode),
            _ => false,
        };
    }

    private static bool Assign(ReviewedThermalMode value, out ReviewedThermalMode mode)
    {
        mode = value;
        return true;
    }
}
