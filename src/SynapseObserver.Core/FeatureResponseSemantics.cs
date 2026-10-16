namespace SynapseObserver.Core;

public enum ReviewedFeatureResponseKind
{
    ThermalSetterAcknowledgement,
    ThermalModeQueryResponse,
    FanRpmQueryResponse,
    StoredFanTargetQueryResponse,
    CustomPerformanceQueryResponse,
    LogoStateQueryResponse,
    LogoEffectQueryResponse,
    KeyboardBrightnessQueryResponse,
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

public enum ReviewedFanChannel
{
    First,
    Second,
}

public enum ReviewedCustomPerformanceDomain
{
    Cpu,
    Gpu,
}

public enum ReviewedCustomPerformanceLevel
{
    Low,
    Medium,
    High,
    Boost,
}

public enum ReviewedLogoState
{
    Off,
    On,
}

public enum ReviewedLogoEffect
{
    Static,
    Breathing,
}

public enum ReviewedLightingClass
{
    Zero,
    One,
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
/// Closed semantic fields from a reviewed incomplete feature response whose
/// leading byte and unavailable final padding byte are never exported. A
/// successful API return and status byte do not independently establish that
/// the response belongs to a request or confirms a firmware transition.
/// </summary>
public sealed record ReviewedFeatureResponseProjection(
    ReviewedFeatureResponseKind Kind,
    ReviewedFeatureResponseStatus Status,
    byte TransactionId,
    FeatureResponseCompleteness Completeness,
    OpaquePrefixDisposition OpaquePrefixDisposition)
{
    public ReviewedThermalChannel? ThermalChannel { get; init; }

    public ReviewedThermalMode? ThermalMode { get; init; }

    public ReviewedFanChannel? FanChannel { get; init; }

    public int? FanRpm { get; init; }

    public int? StoredFanTargetRpm { get; init; }

    public ReviewedCustomPerformanceDomain? CustomPerformanceDomain { get; init; }

    public ReviewedCustomPerformanceLevel? CustomPerformanceLevel { get; init; }

    public byte? CustomPerformanceRawLevel { get; init; }

    public ReviewedLogoState? LogoState { get; init; }

    public ReviewedLogoEffect? LogoEffect { get; init; }

    public ReviewedLightingClass? LightingClass { get; init; }

    public byte? KeyboardBrightness { get; init; }
}

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
