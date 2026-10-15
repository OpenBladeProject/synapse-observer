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
    FixedMaximum,
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
