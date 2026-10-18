namespace SynapseObserver.Core;

public enum FileSignatureState
{
    Trusted,
    Unsigned,
    Invalid,
    Unavailable,
}

public enum HidReportDirection
{
    Input,
    Output,
    Feature,
}

public enum HidReportCapabilityKind
{
    Button,
    Value,
}

public sealed record HidReportCapabilityEvidence(
    HidReportDirection Direction,
    HidReportCapabilityKind Kind,
    byte ReportId,
    ushort UsagePage,
    bool IsRange,
    ushort UsageMinimum,
    ushort UsageMaximum,
    ushort LinkCollection,
    ushort LinkUsagePage,
    ushort LinkUsage,
    ushort? BitSize,
    ushort? ReportCount,
    int? LogicalMinimum,
    int? LogicalMaximum)
{
    public bool IsAbsolute { get; init; }

    public bool? HasNull { get; init; }
}

public sealed record HidDescriptorSupportEvidence(
    string InterfaceLabel,
    string PhysicalDeviceLabel,
    ushort VendorId,
    ushort ProductId,
    ushort? UsagePage,
    ushort? Usage,
    ushort InputBytes,
    ushort OutputBytes,
    ushort FeatureBytes,
    ushort InputButtonCaps,
    ushort InputValueCaps,
    ushort OutputButtonCaps,
    ushort OutputValueCaps,
    ushort FeatureButtonCaps,
    ushort FeatureValueCaps)
{
    public IReadOnlyList<HidReportCapabilityEvidence> CapabilityShapes { get; init; } = [];

    public CollectionState CapabilityShapeState { get; init; } = CollectionState.Unavailable;

    public bool CapabilityShapesTruncated { get; init; }
}

public sealed record DriverFileEvidence(
    string ServiceName,
    string FileName,
    string? FileVersion,
    string? Sha256,
    FileSignatureState SignatureState);

public sealed record DeviceDriverEvidence(
    string PhysicalDeviceLabel,
    string? DeviceName,
    string? Provider,
    string? DriverVersion,
    string? InfName,
    string? ServiceName,
    IReadOnlyList<string> UpperFilters,
    IReadOnlyList<string> LowerFilters,
    IReadOnlyList<DriverFileEvidence> Files);

public sealed record ExecutableModuleProvenanceEvidence(
    string ProcessLabel,
    string FileName,
    string? FileVersion,
    string? Sha256,
    FileSignatureState SignatureState,
    bool IsMainExecutable);

public sealed record GraphicsAdapterEvidence(
    string AdapterLabel,
    string? Name,
    string? Manufacturer,
    string? DriverVersion)
{
    public ushort? PciVendorId { get; init; }

    public ushort? PciDeviceId { get; init; }

    public ushort? PciSubsystemVendorId { get; init; }

    public ushort? PciSubsystemId { get; init; }
}

public sealed record ProcessorEvidence(
    string ProcessorLabel,
    string? Name,
    string? Manufacturer);

public sealed record SystemPlatformEvidence(
    string? WindowsProductName,
    string? WindowsDisplayVersion,
    string? WindowsBuild,
    int? WindowsUpdateBuildRevision,
    string Architecture,
    IReadOnlyList<ProcessorEvidence> Processors);

public sealed record DisplayModeEvidence(
    int Width,
    int Height,
    int BitsPerPixel,
    int RefreshRateHertz);

public enum DisplayColorProfileScope
{
    SystemWide,
    CurrentUser,
}

public enum DisplayColorProfileSubtype
{
    StandardSdr,
    ExtendedHdr,
}

public sealed record DisplayColorProfileEvidence(
    string ProfileLabel,
    string? FileName,
    string? Sha256,
    bool IsAssociated,
    IReadOnlyList<DisplayColorProfileSubtype> DefaultSubtypes);

public sealed record DisplayColorProfileScopeEvidence(
    DisplayColorProfileScope Scope,
    CollectionState State,
    IReadOnlyList<DisplayColorProfileEvidence> Profiles,
    bool Truncated,
    string? Issue);

public sealed record DisplayColorProfileInventoryEvidence(
    DisplayColorProfileScope? SelectedScope,
    CollectionState State,
    IReadOnlyList<DisplayColorProfileScopeEvidence> Scopes,
    string? Issue);

public sealed record DisplayEvidence(
    string DisplayLabel,
    string? DisplayName,
    bool? IsInternal,
    DisplayModeEvidence? CurrentMode,
    IReadOnlyList<DisplayModeEvidence> AvailableModes)
{
    public string? ConnectionKind { get; init; }

    public bool? HdrSupported { get; init; }

    public bool? HdrEnabled { get; init; }

    public int? BitsPerColorChannel { get; init; }

    public uint? ColorEncoding { get; init; }

    public uint? ActiveColorMode { get; init; }

    public bool? VariableRefreshRateSupported { get; init; }

    public bool? DynamicRefreshRateActive { get; init; }

    public double? ActiveRefreshRateHertz { get; init; }

    public string? EdidManufacturerCode { get; init; }

    public string? EdidProductCode { get; init; }

    public DisplayColorProfileInventoryEvidence? ColorProfiles { get; init; }
}

public sealed record BrightnessEvidence(
    string DisplayLabel,
    byte? CurrentPercent,
    IReadOnlyList<byte> AvailableLevels);

public sealed record ThermalZoneEvidence(
    string ZoneLabel,
    uint? CurrentTemperatureTenthsKelvin)
{
    public string Provenance => "WindowsWmiMSAcpiThermalZoneTemperature";
}

public sealed record LampPositionEvidence(
    int Index,
    float X,
    float Y,
    float Z,
    string Purposes,
    int RedLevelCount,
    int GreenLevelCount,
    int BlueLevelCount,
    int GainLevelCount,
    long UpdateLatencyMicroseconds);

public sealed record LampVirtualKeyMappingEvidence(
    string Key,
    IReadOnlyList<int> Indices);

public sealed record LampArrayEvidence(
    string LampArrayLabel,
    int? LampCount,
    int? RowCount,
    int? ColumnCount,
    IReadOnlyList<string> KeyNames)
{
    public ushort? VendorId { get; init; }

    public ushort? ProductId { get; init; }

    public string? PhysicalDeviceLabel { get; init; }

    public string? Kind { get; init; }

    public bool? IsAvailable { get; init; }

    public bool? IsConnected { get; init; }

    public bool? IsEnabled { get; init; }

    public bool? SupportsVirtualKeys { get; init; }

    public double? BrightnessLevel { get; init; }

    public long? MinimumUpdateIntervalMicroseconds { get; init; }

    public float? BoundingBoxWidth { get; init; }

    public float? BoundingBoxHeight { get; init; }

    public float? BoundingBoxDepth { get; init; }

    public IReadOnlyList<LampPositionEvidence> Lamps { get; init; } = [];

    public IReadOnlyList<LampVirtualKeyMappingEvidence> VirtualKeys { get; init; } = [];
}

public sealed record NotCollectedEvidence(string Area, string Reason);

public sealed record SupportCoverageGap(string Area, CollectionState State, string Reason);

public sealed record SupportInventorySnapshot(
    SectionResult<IReadOnlyList<HidDescriptorSupportEvidence>> HidDescriptors,
    SectionResult<IReadOnlyList<DeviceDriverEvidence>> DeviceDrivers,
    SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>> ExecutableModules,
    SectionResult<IReadOnlyList<GraphicsAdapterEvidence>> GraphicsAdapters,
    SectionResult<IReadOnlyList<DisplayEvidence>> Displays,
    SectionResult<IReadOnlyList<BrightnessEvidence>> Brightness,
    SectionResult<IReadOnlyList<ThermalZoneEvidence>> ThermalZones,
    SectionResult<IReadOnlyList<LampArrayEvidence>> LampArrays,
    SectionResult<NotCollectedEvidence> EmbeddedController,
    IReadOnlyList<SupportCoverageGap> CoverageGaps)
{
    public SectionResult<SystemPlatformEvidence?> SystemPlatform { get; init; } =
        SectionResult<SystemPlatformEvidence?>.Absent(null);

    public SectionResult<IReadOnlyList<PdhTemperatureEvidence>> PdhTemperatures { get; init; } =
        new(CollectionState.Skipped, [], "Fixed Windows PDH counters were not collected.");

    public static SupportInventorySnapshot Empty { get; } = new(
        SectionResult<IReadOnlyList<HidDescriptorSupportEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<DeviceDriverEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<GraphicsAdapterEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<DisplayEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<BrightnessEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<ThermalZoneEvidence>>.Absent([]),
        SectionResult<IReadOnlyList<LampArrayEvidence>>.Absent([]),
        new SectionResult<NotCollectedEvidence>(
            CollectionState.Skipped,
            new NotCollectedEvidence("EmbeddedControllerOrMcu", "NotCollectedUncapturedCommand"),
            "Embedded-controller and MCU queries were not collected because no reviewed read command is available."),
        [
            new SupportCoverageGap("LampArrayLayout", CollectionState.Absent, "NoItemsReported"),
            new SupportCoverageGap("LampArrayCurrentFrame", CollectionState.Unavailable, "WindowsApiHasNoCurrentFrameReadback"),
            new SupportCoverageGap("EmbeddedControllerOrMcu", CollectionState.Skipped, "NotCollectedUncapturedCommand"),
        ]);

    public static SupportInventorySnapshot Cancelled { get; } = CreateUniform(
        CollectionState.Cancelled,
        "Collection was cancelled before this section completed.");

    public static SupportInventorySnapshot Unavailable(string issue) =>
        CreateUniform(CollectionState.Unavailable, issue);

    private static SupportInventorySnapshot CreateUniform(CollectionState state, string issue)
    {
        static SectionResult<IReadOnlyList<T>> List<T>(CollectionState state, string issue) => new(state, [], issue);

        return new SupportInventorySnapshot(
            List<HidDescriptorSupportEvidence>(state, issue),
            List<DeviceDriverEvidence>(state, issue),
            List<ExecutableModuleProvenanceEvidence>(state, issue),
            List<GraphicsAdapterEvidence>(state, issue),
            List<DisplayEvidence>(state, issue),
            List<BrightnessEvidence>(state, issue),
            List<ThermalZoneEvidence>(state, issue),
            List<LampArrayEvidence>(state, issue),
            new SectionResult<NotCollectedEvidence>(
                state,
                new NotCollectedEvidence("EmbeddedControllerOrMcu", "NotCollected"),
                issue),
            [new SupportCoverageGap("SupportInventory", state, state.ToString())])
        {
            SystemPlatform = new SectionResult<SystemPlatformEvidence?>(state, null, issue),
            PdhTemperatures = List<PdhTemperatureEvidence>(state, issue),
        };
    }
}

internal static class SupportInventorySanitizer
{
    private const int MaxHidDescriptors = 32;
    private const int MaxDeviceDrivers = 24;
    private const int MaxDriverFiles = 8;
    private const int MaxFilters = 16;
    private const int MaxGraphicsAdapters = 8;
    private const int MaxDisplays = 16;
    private const int MaxDisplayModes = EvidenceLimits.MaxDisplayModesPerDisplay;
    private const int MaxBrightnessEntries = 16;
    private const int MaxBrightnessLevels = 101;
    private const int MaxThermalZones = 16;
    private const int MaxLampArrays = 16;
    private const int MaxLampKeys = 256;
    private const int MaxLampsPerArray = 4096;
    private const int MaxIndicesPerKey = 512;

    public static SupportInventorySnapshot Sanitize(SupportInventorySnapshot snapshot) =>
        new SupportInventorySnapshot(
            SanitizeSection(snapshot.HidDescriptors, snapshot.HidDescriptors.Value
                .Take(MaxHidDescriptors)
                .Where(static item => IsToken(item.InterfaceLabel) && IsToken(item.PhysicalDeviceLabel))
                .Select(SanitizeHidDescriptor)
                .ToArray()),
            SanitizeSection(snapshot.DeviceDrivers, snapshot.DeviceDrivers.Value
                .Take(MaxDeviceDrivers)
                .Where(static item => IsToken(item.PhysicalDeviceLabel))
                .Select(static item => item with
                {
                    DeviceName = Text(item.DeviceName),
                    Provider = Text(item.Provider),
                    DriverVersion = Text(item.DriverVersion),
                    InfName = FileName(item.InfName),
                    ServiceName = Token(item.ServiceName),
                    UpperFilters = item.UpperFilters.Take(MaxFilters).Select(Token).OfType<string>().ToArray(),
                    LowerFilters = item.LowerFilters.Take(MaxFilters).Select(Token).OfType<string>().ToArray(),
                    Files = item.Files.Take(MaxDriverFiles).Select(static file => file with
                    {
                        ServiceName = Token(file.ServiceName) ?? "driver",
                        FileName = FileName(file.FileName) ?? "driver",
                        FileVersion = Text(file.FileVersion),
                        Sha256 = Sha256(file.Sha256),
                    }).ToArray(),
                })
                .ToArray()),
            SanitizeSection(snapshot.ExecutableModules, snapshot.ExecutableModules.Value
                .Take(64)
                .Where(static item => IsToken(item.ProcessLabel))
                .Select(static item => item with
                {
                    FileName = FileName(item.FileName) ?? "module",
                    FileVersion = Text(item.FileVersion),
                    Sha256 = Sha256(item.Sha256),
                })
                .ToArray()),
            SanitizeSection(snapshot.GraphicsAdapters, snapshot.GraphicsAdapters.Value
                .Take(MaxGraphicsAdapters)
                .Where(static item => IsToken(item.AdapterLabel))
                .Select(static item => item with
                {
                    Name = Text(item.Name),
                    Manufacturer = Text(item.Manufacturer),
                    DriverVersion = Text(item.DriverVersion),
                })
                .ToArray()),
            SanitizeSection(snapshot.Displays, snapshot.Displays.Value
                .Take(MaxDisplays)
                .Where(static item => IsToken(item.DisplayLabel))
                .Select(static item => item with
                {
                    ConnectionKind = Token(item.ConnectionKind),
                    BitsPerColorChannel = BoundedCount(item.BitsPerColorChannel, 64),
                    ActiveRefreshRateHertz = item.ActiveRefreshRateHertz is > 0 and <= 1000
                        && double.IsFinite(item.ActiveRefreshRateHertz.Value)
                            ? item.ActiveRefreshRateHertz
                            : null,
                    EdidManufacturerCode = EdidManufacturer(item.EdidManufacturerCode),
                    EdidProductCode = EdidProduct(item.EdidProductCode),
                    DisplayName = Text(item.DisplayName),
                    CurrentMode = ValidMode(item.CurrentMode),
                    ColorProfiles = SanitizeColorProfiles(item.ColorProfiles),
                    AvailableModes = item.AvailableModes
                        .Where(static mode => ValidMode(mode) is not null)
                        .Take(MaxDisplayModes)
                        .ToArray(),
                })
                .ToArray()),
            SanitizeSection(snapshot.Brightness, snapshot.Brightness.Value
                .Take(MaxBrightnessEntries)
                .Where(static item => IsToken(item.DisplayLabel))
                .Select(static item => item with
                {
                    CurrentPercent = item.CurrentPercent is <= 100 ? item.CurrentPercent : null,
                    AvailableLevels = item.AvailableLevels
                        .Where(static level => level <= 100)
                        .Distinct()
                        .Take(MaxBrightnessLevels)
                        .ToArray(),
                })
                .ToArray()),
            SanitizeSection(snapshot.ThermalZones, snapshot.ThermalZones.Value
                .Take(MaxThermalZones)
                .Where(static item => IsToken(item.ZoneLabel))
                .Select(static item => item with
                {
                    CurrentTemperatureTenthsKelvin = item.CurrentTemperatureTenthsKelvin is <= 10000
                        ? item.CurrentTemperatureTenthsKelvin
                        : null,
                })
                .ToArray()),
            SanitizeSection(snapshot.LampArrays, snapshot.LampArrays.Value
                .Take(MaxLampArrays)
                .Where(static item => IsToken(item.LampArrayLabel))
                .Select(static item => item with
                {
                    LampCount = BoundedCount(item.LampCount, 4096),
                    RowCount = BoundedCount(item.RowCount, 256),
                    ColumnCount = BoundedCount(item.ColumnCount, 256),
                    Kind = Token(item.Kind),
                    PhysicalDeviceLabel = Token(item.PhysicalDeviceLabel),
                    BrightnessLevel = item.BrightnessLevel is >= 0 and <= 1 && double.IsFinite(item.BrightnessLevel.Value)
                        ? item.BrightnessLevel
                        : null,
                    MinimumUpdateIntervalMicroseconds = item.MinimumUpdateIntervalMicroseconds is >= 0 and <= 60_000_000
                        ? item.MinimumUpdateIntervalMicroseconds
                        : null,
                    BoundingBoxWidth = BoundedCoordinate(item.BoundingBoxWidth),
                    BoundingBoxHeight = BoundedCoordinate(item.BoundingBoxHeight),
                    BoundingBoxDepth = BoundedCoordinate(item.BoundingBoxDepth),
                    KeyNames = item.KeyNames.Take(MaxLampKeys).Select(Token).OfType<string>().ToArray(),
                    Lamps = item.Lamps
                        .Where(static lamp => lamp.Index is >= 0 and < MaxLampsPerArray
                            && BoundedCoordinate(lamp.X) is not null
                            && BoundedCoordinate(lamp.Y) is not null
                            && BoundedCoordinate(lamp.Z) is not null)
                        .Take(MaxLampsPerArray)
                        .Select(static lamp => lamp with
                        {
                            Purposes = Token(lamp.Purposes) ?? "Unknown",
                            RedLevelCount = BoundedLevelCount(lamp.RedLevelCount),
                            GreenLevelCount = BoundedLevelCount(lamp.GreenLevelCount),
                            BlueLevelCount = BoundedLevelCount(lamp.BlueLevelCount),
                            GainLevelCount = BoundedLevelCount(lamp.GainLevelCount),
                            UpdateLatencyMicroseconds = lamp.UpdateLatencyMicroseconds is >= 0 and <= 60_000_000
                                ? lamp.UpdateLatencyMicroseconds
                                : 0,
                        })
                        .ToArray(),
                    VirtualKeys = item.VirtualKeys
                        .Take(MaxLampKeys)
                        .Where(static mapping => IsToken(mapping.Key))
                        .Select(static mapping => mapping with
                        {
                            Indices = mapping.Indices
                                .Where(static index => index is >= 0 and < MaxLampsPerArray)
                                .Distinct()
                                .Take(MaxIndicesPerKey)
                                .ToArray(),
                        })
                        .ToArray(),
                })
                .ToArray()),
            SanitizeSection(snapshot.EmbeddedController, new NotCollectedEvidence(
                Token(snapshot.EmbeddedController.Value.Area) ?? "EmbeddedControllerOrMcu",
                Token(snapshot.EmbeddedController.Value.Reason) ?? "NotCollected")),
            snapshot.CoverageGaps
                .Take(32)
                .Select(static gap => new SupportCoverageGap(
                    Token(gap.Area) ?? "SupportInventory",
                    gap.State,
                    Token(gap.Reason) ?? "Unavailable"))
                .ToArray())
        {
            PdhTemperatures = PdhTemperatureCollector.Sanitize(snapshot.PdhTemperatures),
            SystemPlatform = SanitizeSection(
                snapshot.SystemPlatform,
                snapshot.SystemPlatform.Value is { } platform
                    ? platform with
                    {
                        WindowsProductName = Text(platform.WindowsProductName),
                        WindowsDisplayVersion = Text(platform.WindowsDisplayVersion),
                        WindowsBuild = Token(platform.WindowsBuild),
                        WindowsUpdateBuildRevision = platform.WindowsUpdateBuildRevision is >= 0 and <= 1_000_000
                            ? platform.WindowsUpdateBuildRevision
                            : null,
                        Architecture = Token(platform.Architecture) ?? "Unknown",
                        Processors = platform.Processors
                            .Take(8)
                            .Where(static processor => IsToken(processor.ProcessorLabel))
                            .Select(static processor => processor with
                            {
                                Name = Text(processor.Name),
                                Manufacturer = Text(processor.Manufacturer),
                            })
                            .ToArray(),
                    }
                    : null),
        };

    private static HidDescriptorSupportEvidence SanitizeHidDescriptor(HidDescriptorSupportEvidence descriptor)
    {
        var sanitizedShapes = descriptor.CapabilityShapes
            .Where(IsValidHidCapabilityShape)
            .Take(EvidenceLimits.MaxHidCapabilityShapesPerInterface)
            .Select(static shape => shape.Kind == HidReportCapabilityKind.Button
                ? shape with
                {
                    BitSize = null,
                    LogicalMinimum = null,
                    LogicalMaximum = null,
                    HasNull = null,
                }
                : shape)
            .ToArray();
        var invalidShapeRemoved = sanitizedShapes.Length != descriptor.CapabilityShapes.Count
            && descriptor.CapabilityShapes.Count <= EvidenceLimits.MaxHidCapabilityShapesPerInterface;
        var bounded = descriptor.CapabilityShapes.Count > EvidenceLimits.MaxHidCapabilityShapesPerInterface;
        var totalDeclaredShapes = descriptor.InputButtonCaps
            + descriptor.InputValueCaps
            + descriptor.OutputButtonCaps
            + descriptor.OutputValueCaps
            + descriptor.FeatureButtonCaps
            + descriptor.FeatureValueCaps;
        var validState = descriptor.CapabilityShapeState is CollectionState.Collected
            or CollectionState.Partial
            or CollectionState.Absent
            or CollectionState.Unavailable;
        var inconsistentState = descriptor.CapabilityShapeState == CollectionState.Absent
                && (totalDeclaredShapes > 0 || sanitizedShapes.Length > 0)
            || descriptor.CapabilityShapeState == CollectionState.Unavailable && sanitizedShapes.Length > 0
            || descriptor.CapabilityShapeState == CollectionState.Collected
                && sanitizedShapes.Length != totalDeclaredShapes;
        var state = !validState || invalidShapeRemoved || bounded || inconsistentState
            ? CollectionState.Partial
            : descriptor.CapabilityShapeState;

        return descriptor with
        {
            CapabilityShapes = sanitizedShapes,
            CapabilityShapeState = state,
            CapabilityShapesTruncated = descriptor.CapabilityShapesTruncated
                || bounded
                || descriptor.CapabilityShapeState == CollectionState.Collected
                    && totalDeclaredShapes > EvidenceLimits.MaxHidCapabilityShapesPerInterface,
        };
    }

    private static DisplayColorProfileInventoryEvidence? SanitizeColorProfiles(
        DisplayColorProfileInventoryEvidence? inventory)
    {
        if (inventory is null)
        {
            return null;
        }

        var partial = inventory.SelectedScope is not null && !Enum.IsDefined(inventory.SelectedScope.Value);
        DisplayColorProfileScope? selectedScope = partial ? null : inventory.SelectedScope;
        var scopes = new List<DisplayColorProfileScopeEvidence>(2);
        var seenScopes = new HashSet<DisplayColorProfileScope>();
        foreach (var scope in inventory.Scopes.Take(2))
        {
            if (!Enum.IsDefined(scope.Scope) || !seenScopes.Add(scope.Scope))
            {
                partial = true;
                continue;
            }

            var profiles = scope.Profiles
                .Take(WindowsInventorySource.MaxColorProfileRecordsPerScope)
                .Where(static profile => IsToken(profile.ProfileLabel))
                .Select(static profile => profile with
                {
                    FileName = WindowsInventorySource.GetSafeColorProfileFileName(profile.FileName),
                    Sha256 = WindowsInventorySource.GetSafeColorProfileFileName(profile.FileName) is not null
                        ? Sha256(profile.Sha256) : null,
                    DefaultSubtypes = profile.DefaultSubtypes
                        .Where(Enum.IsDefined)
                        .Distinct()
                        .Take(2)
                        .ToArray(),
                })
                .ToArray();
            bool scopePartial = scope.Profiles.Count > WindowsInventorySource.MaxColorProfileRecordsPerScope
                || profiles.Length != Math.Min(scope.Profiles.Count, WindowsInventorySource.MaxColorProfileRecordsPerScope)
                || scope.Profiles.Any(static profile => profile.FileName is not null
                    && WindowsInventorySource.GetSafeColorProfileFileName(profile.FileName) is null
                    || profile.Sha256 is not null
                        && (Sha256(profile.Sha256) is null
                            || WindowsInventorySource.GetSafeColorProfileFileName(profile.FileName) is null)
                    || profile.DefaultSubtypes.Any(static subtype => !Enum.IsDefined(subtype)));
            CollectionState state = !Enum.IsDefined(scope.State) || scopePartial
                ? CollectionState.Partial
                : scope.State;
            partial |= scopePartial || state is CollectionState.Partial or CollectionState.Unavailable;
            scopes.Add(scope with
            {
                State = state,
                Profiles = profiles,
                Truncated = scope.Truncated
                    || scope.Profiles.Count > WindowsInventorySource.MaxColorProfileRecordsPerScope,
                Issue = Issue(state, scope.Issue),
            });
        }

        partial |= inventory.Scopes.Count > 2
            || !scopes.Any(static scope => scope.Scope == DisplayColorProfileScope.CurrentUser)
            || selectedScope == DisplayColorProfileScope.SystemWide
                && !scopes.Any(static scope => scope.Scope == DisplayColorProfileScope.SystemWide);
        var whollyUnavailable = inventory.State == CollectionState.Unavailable
            && !scopes.Any(static scope => scope.Profiles.Count > 0
                || scope.State is CollectionState.Collected or CollectionState.Absent);
        CollectionState inventoryState = whollyUnavailable
            ? CollectionState.Unavailable
            : !Enum.IsDefined(inventory.State) || partial ? CollectionState.Partial : inventory.State;
        return inventory with
        {
            SelectedScope = selectedScope,
            State = inventoryState,
            Scopes = scopes,
            Issue = Issue(inventoryState, inventory.Issue),
        };
    }

    private static bool IsValidHidCapabilityShape(HidReportCapabilityEvidence shape) =>
        Enum.IsDefined(shape.Direction)
        && Enum.IsDefined(shape.Kind)
        && (shape.Kind == HidReportCapabilityKind.Button
            ? shape.ReportCount is null or > 0
            : shape.ReportCount is > 0)
        && shape.UsageMinimum <= shape.UsageMaximum
        && (shape.Kind != HidReportCapabilityKind.Value
            || shape.BitSize is > 0
                && shape.LogicalMinimum is int logicalMinimum
                && shape.LogicalMaximum is int logicalMaximum
                && logicalMinimum <= logicalMaximum);

    private static SectionResult<T> SanitizeSection<T>(SectionResult<T> source, T value) =>
        new(source.State, value, Issue(source.State, source.Issue));

    private static string? Issue(CollectionState state, string? issue) => state switch
    {
        CollectionState.Partial when !string.IsNullOrWhiteSpace(issue) =>
            "Some support inventory details were unavailable or reached a documented bound.",
        CollectionState.Unavailable when !string.IsNullOrWhiteSpace(issue) =>
            "This support inventory section was unavailable.",
        CollectionState.Skipped when !string.IsNullOrWhiteSpace(issue) =>
            "This support inventory section was not collected.",
        CollectionState.Cancelled => "Collection was cancelled before this section completed.",
        _ => null,
    };

    private static bool IsToken(string? value) => EvidenceText.IsToken(value);

    private static string? Token(string? value) => IsToken(value) ? value : null;

    private static string? FileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var segments = value.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return Text(segments.LastOrDefault());
    }

    private static string? Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > EvidenceLimits.MaxTextLength
            || trimmed.Contains('\\')
            || trimmed.Contains(":/", StringComparison.Ordinal)
            || trimmed.StartsWith('/'))
        {
            return null;
        }

        return trimmed.All(static character => !char.IsControl(character)) ? trimmed : null;
    }

    private static string? Sha256(string? value) =>
        value is { Length: 64 } && value.All(static character => char.IsAsciiHexDigit(character))
            ? value.ToUpperInvariant()
            : null;

    private static DisplayModeEvidence? ValidMode(DisplayModeEvidence? mode) =>
        mode is not null
            && mode.Width is > 0 and <= 16384
            && mode.Height is > 0 and <= 16384
            && mode.BitsPerPixel is > 0 and <= 128
            && mode.RefreshRateHertz is > 0 and <= 1000
                ? mode
                : null;

    private static int? BoundedCount(int? value, int maximum) =>
        value is int count && count > 0 && count <= maximum ? count : null;

    private static int BoundedLevelCount(int value) => value is >= 0 and <= 65536 ? value : 0;

    private static float? BoundedCoordinate(float? value) =>
        value is float coordinate && float.IsFinite(coordinate) && Math.Abs(coordinate) <= 100
            ? coordinate
            : null;

    private static string? EdidManufacturer(string? value) =>
        value is { Length: 3 } && value.All(static character => char.IsAsciiLetterUpper(character))
            ? value
            : null;

    private static string? EdidProduct(string? value) =>
        value is { Length: 4 } && value.All(static character => char.IsAsciiHexDigit(character))
            ? value.ToUpperInvariant()
            : null;
}
