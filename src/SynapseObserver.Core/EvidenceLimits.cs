namespace SynapseObserver.Core;

public static class EvidenceLimits
{
    public const int MaxTextLength = 256;
    public const int MaxTokenLength = 80;
    public const int MaxMarkers = 512;
    public const int MaxObservations = 4096;
    public const int MaxCoverageItems = 128;
    public const int MaxPayloadBytesPerObservation = 4096;
    public const int MaxPayloadComparisonStreams = 64;
    public const int MaxPayloadChangedRanges = 16;
    public const int MaxTotalPayloadBytes = 4 * 1024 * 1024;
    public const int MaxObservationEventBytes = 16 * 1024;
    public const int MaxBundleJsonBytes = 16 * 1024 * 1024;
    public const int MaxHidInterfaces = 256;
    public const int MaxHidCapabilityShapesPerInterface = 128;
    public const int MaxSynapseProcesses = 32;
    public const int MaxModulesPerProcess = 256;
}
