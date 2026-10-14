namespace SynapseObserver.Core;

public static class ContributionDraftBuilder
{
    private static readonly string[] CapabilityPaths =
    [
        "identityAndFirmware.usbDescriptors",
        "identityAndFirmware.modelNumber",
        "identityAndFirmware.biosVersion",
        "identityAndFirmware.ecVersion",
        "identityAndFirmware.mcuVersion",
        "identityAndFirmware.relevantInterfaces",
        "identityAndFirmware.hidTopology",
        "identityAndFirmware.driverProvenance",
        "identityAndFirmware.softwareProvenance",
        "identityAndFirmware.windowsPlatform",
        "performance.ac.presets",
        "performance.ac.customCpuLevels",
        "performance.ac.customGpuLevels",
        "performance.ac.readback",
        "performance.usbC.powerClasses",
        "performance.usbC.presetsByPowerClass",
        "performance.usbC.customCpuLevels",
        "performance.usbC.customGpuLevels",
        "performance.usbC.readback",
        "performance.battery.presets",
        "performance.battery.customCpuLevels",
        "performance.battery.customGpuLevels",
        "performance.battery.readback",
        "performance.ac.cpuPowerLimit",
        "performance.ac.cpuVoltageOptimizer",
        "performance.ac.gpuCoreClockOffset",
        "performance.ac.gpuMemoryClockOffset",
        "performance.ac.hyperBoostWithCoolingPad",
        "performance.ac.hyperBoostWithoutCoolingPad",
        "performance.ac.hyperBoostBaseModes",
        "performance.externalPower.presets",
        "performance.externalPower.customCpuLevels",
        "performance.externalPower.customGpuLevels",
        "performance.externalPower.readback",
        "fans.automatic",
        "fans.fixedMinimum",
        "fans.fixedMaximum",
        "fans.fixedStep",
        "fans.maximum",
        "fans.manualCurveWrites",
        "fans.manualCurveReadback",
        "fans.rpmQuery",
        "fans.fixedSpeedReports",
        "sensors.cpuTemperature",
        "sensors.gpuTemperature",
        "sensors.fanRpm",
        "sensors.health",
        "battery.protectionOff",
        "battery.advertisedLimits",
        "battery.limitQuery",
        "battery.temporaryFullCharge",
        "battery.temporaryFullChargeQuery",
        "keyboardLighting.off",
        "keyboardLighting.brightnessRange",
        "keyboardLighting.brightnessQuery",
        "keyboardLighting.effects.ambientAwareness.base",
        "keyboardLighting.effects.ambientAwareness.screenRegions",
        "keyboardLighting.effects.ambientAwareness.matrixFormat",
        "keyboardLighting.effects.ambientAwareness.frameCadence",
        "keyboardLighting.effects.breathing.base",
        "keyboardLighting.effects.breathing.singleColor",
        "keyboardLighting.effects.breathing.dualColor",
        "keyboardLighting.effects.breathing.randomColor",
        "keyboardLighting.effects.breathing.durations",
        "keyboardLighting.effects.customMatrix.setupCommand",
        "keyboardLighting.effects.customMatrix.matrixFormat",
        "keyboardLighting.effects.customMatrix.frameCadence",
        "keyboardLighting.effects.reactive.base",
        "keyboardLighting.effects.reactive.colors",
        "keyboardLighting.effects.reactive.durations",
        "keyboardLighting.effects.reactive.keyMap",
        "keyboardLighting.effects.ripple.base",
        "keyboardLighting.effects.ripple.colors",
        "keyboardLighting.effects.ripple.durations",
        "keyboardLighting.effects.ripple.keyMap",
        "keyboardLighting.effects.spectrum.base",
        "keyboardLighting.effects.spectrum.hostColorSequence",
        "keyboardLighting.effects.spectrum.frameCadence",
        "keyboardLighting.effects.starlight.base",
        "keyboardLighting.effects.starlight.singleColor",
        "keyboardLighting.effects.starlight.dualColor",
        "keyboardLighting.effects.starlight.randomColor",
        "keyboardLighting.effects.starlight.durations",
        "keyboardLighting.effects.static.base",
        "keyboardLighting.effects.static.colors",
        "keyboardLighting.effects.tidal.base",
        "keyboardLighting.effects.tidal.primaryColor",
        "keyboardLighting.effects.tidal.secondaryColor",
        "keyboardLighting.effects.tidal.randomColor",
        "keyboardLighting.effects.tidal.inwardDirection",
        "keyboardLighting.effects.tidal.outwardDirection",
        "keyboardLighting.effects.wave.base",
        "keyboardLighting.effects.wave.leftDirection",
        "keyboardLighting.effects.wave.rightDirection",
        "keyboardLighting.effects.wheel.base",
        "keyboardLighting.effects.wheel.leftDirection",
        "keyboardLighting.effects.wheel.rightDirection",
        "keyboardLighting.effectGetter",
        "keyboardLighting.matrixInterfaceDiscovery",
        "keyboardLighting.lampArrayInterfaceDiscovery",
        "keyboardLighting.windowsHostRendering",
        "keyboardLighting.layoutIdentifier",
        "keyboardLighting.physicalSlotMap",
        "logoLighting.off",
        "logoLighting.static",
        "logoLighting.breathing",
        "logoLighting.getter",
        "keyboardBehavior.functionKeyPrimary",
        "keyboardBehavior.functionKeyPrimaryGetter",
        "keyboardBehavior.gamingMode",
        "keyboardBehavior.gamingModeGetter",
        "keyboardBehavior.startupAnimation",
        "keyboardBehavior.startupAnimationGetter",
        "keyboardBehavior.normalDeviceMode",
        "keyboardBehavior.driverDeviceMode",
        "keyboardBehavior.deviceModeGetter",
        "keyboardBehavior.playbackMuteIndicator",
        "specialKeys.functionLayerReports",
        "specialKeys.performanceKey",
        "specialKeys.m1PageUp",
        "specialKeys.m2PageDown",
        "specialKeys.m3GamingMode",
        "specialKeys.m4PerformanceMode",
        "specialKeys.m5MicrophoneMute",
        "specialKeys.physicalSlotMap",
        "specialKeys.m1.inputEvidence",
        "specialKeys.m2.inputEvidence",
        "specialKeys.m3.inputEvidence",
        "specialKeys.m4.inputEvidence",
        "specialKeys.m5.inputEvidence",
        "specialKeys.fn.inputEvidence",
        "specialKeys.fnP.inputEvidence",
        "display.windowsVisibleModes",
        "display.dynamicRefreshRate",
        "display.variableRefreshRange",
        "display.hdr",
        "display.colorProfilesSdr",
        "display.colorProfilesHdr",
        "display.panelBrightnessCurrent",
        "display.panelBrightnessLevels",
        "display.nativeBrightnessDriver",
        "lifecycle.coldBoot",
        "lifecycle.signIn",
        "lifecycle.serviceRestart",
        "lifecycle.sleepResume",
        "lifecycle.deviceReconnect",
        "power.fullAc",
        "power.usbCPowerClasses",
        "power.battery",
        "power.acToUsbC",
        "power.usbCToAc",
        "power.acToBattery",
        "power.batteryToAc",
        "power.usbCToBattery",
        "power.batteryToUsbC",
        "power.adapterPowerQuery",
        "conflicts.vendorControllerPresent",
        "conflicts.externalChangeDetection",
        "conflicts.defaultZeroWritePolicy",
        "conflicts.explicitReclaim",
        "failures.timeout",
        "failures.malformedResponse",
        "failures.checksumMismatch",
        "failures.transactionMismatch",
        "failures.commandClassMismatch",
        "failures.commandIdMismatch",
        "failures.statusFailure",
        "failures.unsupportedPid",
        "failures.unsupportedFirmware",
        "failures.disconnectMidTransaction",
    ];

    private static readonly string[] MaintainerStages =
    [
        "Commit a sanitized oracle capture for the exact model, PID, firmware, and interface.",
        "Decode requests and responses deterministically, including bounds, checksum, transaction, status, and timeout handling.",
        "Implement a typed read-only query that works without the vendor process.",
        "Validate one typed setter with independent readback and exact-state restoration.",
        "Prove unsupported-device and unsupported-firmware admission produces zero writes.",
        "Validate conflict policy, required power contexts, and lifecycle transitions.",
        "Add production tests backed by the committed sanitized fixture and complete an explicit admission review.",
    ];

    // Bundle export shares one set of windows across the three draft documents.
    internal static (ContributionCoverageDraft Coverage, ContributionAnnotationDraft Annotation,
        ContributionQualityReceipt Quality) CreateDrafts(
        EvidenceSessionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var windows = CreateActionWindows(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        var coverage = CreateCoverageDraft(snapshot, windows);
        cancellationToken.ThrowIfCancellationRequested();
        var annotation = CreateAnnotationDraft(snapshot, windows);
        cancellationToken.ThrowIfCancellationRequested();
        var quality = CreateQualityReceipt(snapshot, windows, coverage);
        cancellationToken.ThrowIfCancellationRequested();
        return (coverage, annotation, quality);
    }

    public static ContributionCoverageDraft CreateCoverageDraft(EvidenceSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CreateCoverageDraft(snapshot, CreateActionWindows(snapshot));
    }

    private static ContributionCoverageDraft CreateCoverageDraft(
        EvidenceSessionSnapshot snapshot, IReadOnlyList<ContributionActionWindowDraft> windows)
    {
        var evidence = CapabilityPaths.ToDictionary(
            static path => path,
            static _ => new List<ContributionEvidenceReference>(),
            StringComparer.Ordinal);

        AddInventoryEvidence(snapshot.Inventory, evidence);
        AddFeatureEvidence(snapshot, windows, evidence);
        AddSpecialKeyEvidence(snapshot, evidence);

        var identities = snapshot.Inventory.MachineIdentity.Value;
        var interfaces = snapshot.Inventory.Support.HidDescriptors.Value;
        var device = new ContributionDeviceIdentity(
            DistinctValues(identities.Select(static item => item.ProductName)),
            DistinctValues(identities.Select(static item => item.SystemSku)),
            DistinctValues(identities.Select(static item => item.BiosVersion)),
            interfaces.Select(static item => item.VendorId.ToString("X4")).Distinct(StringComparer.Ordinal).Order().ToArray(),
            interfaces.Select(static item => item.ProductId.ToString("X4")).Distinct(StringComparer.Ordinal).Order().ToArray());

        var entries = CapabilityPaths
            .Select(path => new ContributionCoverageEntry(
                path,
                evidence[path].Count == 0 ? ContributionCoverageStatus.NotInvestigated : ContributionCoverageStatus.Captured,
                evidence[path].AsReadOnly()))
            .ToArray();

        return new ContributionCoverageDraft(
            ContributionCoverageDraft.CurrentSchemaVersion,
            snapshot.SchemaVersion,
            device,
            [ContributionCoverageStatus.NotInvestigated, ContributionCoverageStatus.Captured],
            entries,
            "Captured means that this bundle contains passive evidence for the leaf. It does not mean query validation, setter validation, firmware readback, restoration confirmation, or production admission.");
    }

    public static ContributionAnnotationDraft CreateAnnotationDraft(EvidenceSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return CreateAnnotationDraft(snapshot, CreateActionWindows(snapshot));
    }

    private static ContributionAnnotationDraft CreateAnnotationDraft(
        EvidenceSessionSnapshot snapshot, IReadOnlyList<ContributionActionWindowDraft> windows)
    {
        var actionMarkers = windows.SelectMany(window => window.Markers.Select(marker =>
            (window.ActivityId, Marker: marker))).ToHashSet();
        return new ContributionAnnotationDraft(
            ContributionAnnotationDraft.CurrentSchemaVersion,
            snapshot.SchemaVersion,
            Enum.GetValues<ContributionObservationLayer>(),
            windows,
            snapshot.SpecialKeyObservations
                .Select(static item => new ContributionSpecialKeyDraft(ContributionObservationLayer.WindowsRawInput, item))
                .ToArray(),
            snapshot.SpecialKeyProbes,
            "This is a bounded annotation draft from user-mode and Windows evidence. It is not a PCAP, a capture-toolkit annotation, a firmware trace, or proof that a command was applied.")
        {
            ContextMarkers = snapshot.Markers
                .Select(marker => new ContributionContextMarkerDraft(marker.ActivityId, CreateMarkerDraft(marker)))
                .Where(item => !actionMarkers.Contains((item.ActivityId, item.Marker)))
                .ToArray(),
        };
    }

    public static ContributionQualityReceipt CreateQualityReceipt(EvidenceSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var windows = CreateActionWindows(snapshot);
        return CreateQualityReceipt(snapshot, windows, CreateCoverageDraft(snapshot, windows));
    }

    private static ContributionQualityReceipt CreateQualityReceipt(
        EvidenceSessionSnapshot snapshot, IReadOnlyList<ContributionActionWindowDraft> windows,
        ContributionCoverageDraft coverageDraft)
    {
        var associated = windows.SelectMany(static window => window.Observations)
            .Select(static item => item.ReferenceId)
            .ToHashSet(StringComparer.Ordinal);
        var reviewed = snapshot.Observations.Count(static item =>
            item.PayloadProjectionKind != FeaturePayloadProjectionKind.MetadataOnly);
        var redacted = snapshot.Observations.Count(static item => !item.CompletePayloadRetained);
        var gaps = CreateQualityGaps(snapshot, windows, associated);
        var inventoryGapCount = gaps.Count(static gap => gap.Code.StartsWith("Inventory", StringComparison.Ordinal));
        var coverage = coverageDraft.Coverage;
        var capturedCoverage = coverage.Count(static item => item.Status == ContributionCoverageStatus.Captured);

        return new ContributionQualityReceipt(
            ContributionQualityReceipt.CurrentSchemaVersion,
            snapshot.SchemaVersion,
            new ContributionQualityCounts(
                inventoryGapCount,
                coverage.Count,
                capturedCoverage,
                coverage.Count - capturedCoverage,
                windows.Count,
                windows.Count(static item => item.CompleteUserReportedSequence),
                snapshot.Observations.Count,
                reviewed,
                redacted,
                snapshot.Observations.Select(ObservationReference).Count(reference => !associated.Contains(reference)),
                snapshot.SpecialKeyObservations.Count,
                snapshot.RejectedMarkerCount + snapshot.RejectedObservationCount + snapshot.RejectedCoverageCount),
            gaps,
            MaintainerStages,
            false,
            false,
            false);
    }

    private static IReadOnlyList<ContributionActionWindowDraft> CreateActionWindows(EvidenceSessionSnapshot snapshot)
    {
        var labels = snapshot.Coverage.ToDictionary(static item => item.ActivityId, static item => item.Label, StringComparer.Ordinal);
        var windows = new List<ContributionActionWindowDraft>();
        foreach (var group in snapshot.Markers
                     .Where(static marker => IsActionMarker(marker.Kind))
                     .GroupBy(static marker => marker.ActivityId, StringComparer.Ordinal))
        {
            var pending = new List<EvidenceMarker>();
            foreach (var marker in group.OrderBy(static marker => marker.RelativeMilliseconds))
            {
                if (marker.Kind == EvidenceMarkerKind.UserReportedStartingValue && pending.Count > 0)
                {
                    AddSettingWindow(snapshot, group.Key, labels, pending, windows);
                    pending.Clear();
                }

                if (pending.Count == 0 && marker.Kind != EvidenceMarkerKind.UserReportedStartingValue)
                {
                    AddSettingWindow(snapshot, group.Key, labels, [marker], windows);
                    continue;
                }

                pending.Add(marker);
                if (marker.Kind == EvidenceMarkerKind.UserReportedRestoration)
                {
                    AddSettingWindow(snapshot, group.Key, labels, pending, windows);
                    pending.Clear();
                }
            }

            if (pending.Count > 0)
            {
                AddSettingWindow(snapshot, group.Key, labels, pending, windows);
            }
        }

        return windows.OrderBy(static item => item.StartRelativeMilliseconds).ToArray();
    }

    private static void AddSettingWindow(
        EvidenceSessionSnapshot snapshot, string activityId, IReadOnlyDictionary<string, string> labels,
        IReadOnlyList<EvidenceMarker> markers, List<ContributionActionWindowDraft> windows)
    {
        // Legacy report IDs reused StartingValue/Action for declarations and probes.
        // Preserve ambiguous starts elsewhere as incomplete setting evidence.
        var legacyReport = activityId is "cooling-pad" or "keyboard-layout" or "mute-indicator"
            or "startup-lighting" or "special-key-m1" or "special-key-m2" or "special-key-m3"
            or "special-key-m4" or "special-key-m5" or "special-key-fn" or "special-key-fn-p"
            or "power-source" or "adapter-rating" or "sensor-telemetry" or "ec-mcu" or "other-feature";
        if (!legacyReport || markers.Any(static marker => marker.Kind is EvidenceMarkerKind.UserReportedTargetValue
                or EvidenceMarkerKind.UserReportedRestoration))
        {
            windows.Add(CreateActionWindow(snapshot, activityId, labels, markers));
        }
    }

    private static ContributionMarkerDraft CreateMarkerDraft(EvidenceMarker marker) => new(
        marker.RelativeMilliseconds, marker.Kind, marker.Provenance, marker.Label, marker.Value);

    private static ContributionActionWindowDraft CreateActionWindow(
        EvidenceSessionSnapshot snapshot,
        string activityId,
        IReadOnlyDictionary<string, string> labels,
        IReadOnlyList<EvidenceMarker> markerList)
    {
        var markers = markerList.OrderBy(static marker => marker.RelativeMilliseconds).ToArray();
        var start = markers[0].RelativeMilliseconds;
        var end = markers[^1].RelativeMilliseconds;
        var complete = HasOrderedUserSequence(markers);
        var observations = snapshot.Observations
            .Select((item, index) => (Item: item, Reference: ObservationReference(item, index)))
            .Where(item => item.Item.RelativeMilliseconds >= start && item.Item.RelativeMilliseconds <= end)
            .Select(static item => new ContributionObservationDraft(
                ContributionObservationLayer.UserModeFeatureApi,
                item.Reference,
                item.Item.OperationId,
                GetObservationRole(item.Item),
                item.Item.RelativeMilliseconds,
                item.Item.Api,
                item.Item.Direction,
                item.Item.Status,
                item.Item.PayloadProjectionKind,
                item.Item.PayloadDisposition,
                item.Item.CompletePayloadRetained,
                false))
            .ToArray();
        var markerDrafts = markers.Select(CreateMarkerDraft).ToArray();
        labels.TryGetValue(activityId, out var label);
        var gap = complete
            ? observations.Length == 0 ? "No user-mode feature observation fell inside this action window." : null
            : "An ordered starting, target, action, and restoration sequence is not present.";
        return new ContributionActionWindowDraft(
            activityId,
            label ?? markers[0].Label,
            start,
            end,
            complete,
            markerDrafts,
            observations,
            gap);
    }

    private static bool HasOrderedUserSequence(IReadOnlyList<EvidenceMarker> markers)
    {
        var expected = new[]
        {
            EvidenceMarkerKind.UserReportedStartingValue,
            EvidenceMarkerKind.UserReportedTargetValue,
            EvidenceMarkerKind.UserActionConfirmed,
            EvidenceMarkerKind.UserReportedRestoration,
        };
        var next = 0;
        foreach (var marker in markers)
        {
            if (marker.Kind == expected[next] && ++next == expected.Length)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsActionMarker(EvidenceMarkerKind kind) => kind is
        EvidenceMarkerKind.UserReportedStartingValue
        or EvidenceMarkerKind.UserReportedTargetValue
        or EvidenceMarkerKind.UserActionConfirmed
        or EvidenceMarkerKind.UserReportedRestoration;

    private static void AddInventoryEvidence(
        InventorySnapshot inventory,
        Dictionary<string, List<ContributionEvidenceReference>> evidence)
    {
        if (IsUsable(inventory.MachineIdentity.State)
            && inventory.MachineIdentity.Value.Any(static item => !string.IsNullOrWhiteSpace(item.ProductName)))
        {
            Add(evidence, "identityAndFirmware.modelNumber", ContributionObservationLayer.WindowsInventory,
                "inventory.machineIdentity", "Windows reported a product name.");
        }

        if (IsUsable(inventory.MachineIdentity.State)
            && inventory.MachineIdentity.Value.Any(static item => !string.IsNullOrWhiteSpace(item.BiosVersion)))
        {
            Add(evidence, "identityAndFirmware.biosVersion", ContributionObservationLayer.WindowsInventory,
                "inventory.machineIdentity", "Windows reported a BIOS version.");
        }

        if (IsUsable(inventory.Support.HidDescriptors.State) && inventory.Support.HidDescriptors.Value.Count > 0)
        {
            Add(evidence, "identityAndFirmware.usbDescriptors", ContributionObservationLayer.WindowsInventory,
                "inventory.support.hidDescriptors", "Windows returned bounded HID descriptor capabilities.");
        }

        if (IsUsable(inventory.RazerHidInterfaces.State) && inventory.RazerHidInterfaces.Value.Count > 0)
        {
            Add(evidence, "identityAndFirmware.relevantInterfaces", ContributionObservationLayer.WindowsInventory,
                "inventory.razerHidInterfaces", "Windows enumerated one or more Razer HID interfaces.");
        }

        if (IsUsable(inventory.Support.Displays.State)
            && inventory.Support.Displays.Value.Any(static display => display.CurrentMode is not null || display.AvailableModes.Count > 0))
        {
            Add(evidence, "display.windowsVisibleModes", ContributionObservationLayer.WindowsInventory,
                "inventory.support.displays", "Windows returned current or available display modes.");
        }

        if (IsUsable(inventory.Support.Displays.State)
            && inventory.Support.Displays.Value.Any(static display => display.DynamicRefreshRateActive is not null))
        {
            Add(evidence, "display.dynamicRefreshRate", ContributionObservationLayer.WindowsInventory,
                "inventory.support.displays", "Windows returned dynamic-refresh state.");
        }

        if (IsUsable(inventory.Support.Displays.State)
            && inventory.Support.Displays.Value.Any(static display => display.HdrSupported is not null || display.HdrEnabled is not null))
        {
            Add(evidence, "display.hdr", ContributionObservationLayer.WindowsInventory,
                "inventory.support.displays", "Windows returned HDR capability or active-state metadata.");
        }

        if (IsUsable(inventory.Support.LampArrays.State) && inventory.Support.LampArrays.Value.Count > 0)
        {
            Add(evidence, "keyboardLighting.lampArrayInterfaceDiscovery", ContributionObservationLayer.WindowsInventory,
                "inventory.support.lampArrays", "Windows returned one or more LampArray interfaces.");
        }

        if (IsUsable(inventory.Support.DeviceDrivers.State) && inventory.Support.DeviceDrivers.Value.Count > 0)
        {
            Add(evidence, "identityAndFirmware.driverProvenance", ContributionObservationLayer.WindowsInventory,
                "inventory.support.deviceDrivers", "Windows returned bounded device-driver provenance.");
            Add(evidence, "identityAndFirmware.hidTopology", ContributionObservationLayer.WindowsInventory,
                "inventory.support.deviceDrivers", "Windows linked bounded driver and physical-device labels.");
        }

        if (IsUsable(inventory.Support.ExecutableModules.State) && inventory.Support.ExecutableModules.Value.Count > 0)
        {
            Add(evidence, "identityAndFirmware.softwareProvenance", ContributionObservationLayer.WindowsInventory,
                "inventory.support.executableModules", "Windows returned signed executable-module provenance.");
        }

        if (IsUsable(inventory.Support.SystemPlatform.State) && inventory.Support.SystemPlatform.Value is not null)
        {
            Add(evidence, "identityAndFirmware.windowsPlatform", ContributionObservationLayer.WindowsInventory,
                "inventory.support.systemPlatform", "Windows returned bounded OS, architecture, and processor inventory.");
        }

        if (IsUsable(inventory.Support.Brightness.State))
        {
            if (inventory.Support.Brightness.Value.Any(static item => item.CurrentPercent is not null))
            {
                Add(evidence, "display.panelBrightnessCurrent", ContributionObservationLayer.WindowsInventory,
                    "inventory.support.brightness", "Windows returned current panel brightness.");
            }

            if (inventory.Support.Brightness.Value.Any(static item => item.AvailableLevels.Count > 0))
            {
                Add(evidence, "display.panelBrightnessLevels", ContributionObservationLayer.WindowsInventory,
                    "inventory.support.brightness", "Windows returned advertised panel brightness levels.");
            }
        }
    }

    private static void AddFeatureEvidence(
        EvidenceSessionSnapshot snapshot,
        IReadOnlyList<ContributionActionWindowDraft> windows,
        Dictionary<string, List<ContributionEvidenceReference>> evidence)
    {
        var sourceByReference = snapshot.Observations
            .Select((item, index) => (Item: item, Reference: ObservationReference(item, index)))
            .ToDictionary(static item => item.Reference, static item => item.Item, StringComparer.Ordinal);
        var powerSource = snapshot.Inventory.PowerSource.Value?.Source ?? PowerSourceKind.Unknown;
        foreach (var window in windows.Where(static item => item.CompleteUserReportedSequence))
        {
            foreach (var observation in window.Observations.Where(static item =>
                         item.ProjectionKind != FeaturePayloadProjectionKind.MetadataOnly
                         && item.CompletePayloadRetained))
            {
                if (!sourceByReference.TryGetValue(observation.ReferenceId, out var source))
                {
                    continue;
                }

                var summary = $"Reviewed {observation.ProjectionKind} report inside action {window.ActivityId}.";
                foreach (var path in MapFeatureCapability(source, powerSource))
                {
                    Add(evidence, path, ContributionObservationLayer.UserModeFeatureApi, observation.ReferenceId, summary);
                }
            }
        }
    }

    private static IEnumerable<string> MapFeatureCapability(
        SanitizedControlObservation observation,
        PowerSourceKind powerSource)
    {
        // GET_FEATURE transports both setter ACKs and query responses.
        var query = observation.HeaderHints is { CommandId: var commandId } && (commandId & 0x80) != 0;
        if (observation.HeaderHints is null)
        {
            yield break;
        }
        var queryResponse = query
            && observation.Source == ObservationSource.ReturnedBuffer
            && observation.Direction == ObservationDirection.Output;
        if (query && !queryResponse)
        {
            // A reviewed input buffer proves only that a query was requested.
            // Getter/readback coverage needs the separately observed response.
            yield break;
        }

        var powerPrefix = powerSource switch
        {
            // Windows' AC flag means external power and does not distinguish a
            // full barrel adapter from USB-C. Keep exact AC and USB-C leaves open.
            PowerSourceKind.Ac => "performance.externalPower",
            PowerSourceKind.Battery => "performance.battery",
            _ => null,
        };
        var payload = TryGetPayload(observation);
        switch (observation.PayloadProjectionKind)
        {
            case FeaturePayloadProjectionKind.ReviewedThermalMode when powerPrefix is not null && (!query || queryResponse):
                yield return queryResponse ? $"{powerPrefix}.readback" : $"{powerPrefix}.presets";
                break;
            case FeaturePayloadProjectionKind.ReviewedFanSpeed when !query || queryResponse:
                yield return "fans.fixedSpeedReports";
                break;
            case FeaturePayloadProjectionKind.ReviewedFanRpm when queryResponse:
                yield return "fans.rpmQuery";
                yield return "sensors.fanRpm";
                break;
            case FeaturePayloadProjectionKind.ReviewedMatrixFrame:
            case FeaturePayloadProjectionKind.ReviewedMatrixRow:
                yield return "keyboardLighting.effects.customMatrix.matrixFormat";
                break;
            case FeaturePayloadProjectionKind.ReviewedMatrixLatch:
                yield return "keyboardLighting.effects.customMatrix.setupCommand";
                break;
            case FeaturePayloadProjectionKind.ReviewedHardwareLightingEffect when payload is { Length: 80 }:
                foreach (var path in MapHardwareLightingEffect(payload))
                {
                    yield return path;
                }

                break;
            case FeaturePayloadProjectionKind.ReviewedKeyboardBrightness when !query || queryResponse:
                yield return queryResponse ? "keyboardLighting.brightnessQuery" : "keyboardLighting.brightnessRange";
                break;
            case FeaturePayloadProjectionKind.ReviewedLogoState when queryResponse:
                yield return "logoLighting.getter";
                break;
            case FeaturePayloadProjectionKind.ReviewedLogoState when payload is { Length: 3 } && payload[2] == 0:
                yield return "logoLighting.off";
                break;
            case FeaturePayloadProjectionKind.ReviewedLogoEffect when queryResponse:
                yield return "logoLighting.getter";
                break;
            case FeaturePayloadProjectionKind.ReviewedLogoEffect when payload is { Length: 3 }:
                if (payload[2] == 0)
                {
                    yield return "logoLighting.static";
                }
                else if (payload[2] == 2)
                {
                    yield return "logoLighting.breathing";
                }

                break;
            case FeaturePayloadProjectionKind.ReviewedDeviceMode when queryResponse:
                yield return "keyboardBehavior.deviceModeGetter";
                break;
            case FeaturePayloadProjectionKind.ReviewedDeviceMode when payload is { Length: 2 }:
                yield return payload[0] == 0
                    ? "keyboardBehavior.normalDeviceMode"
                    : "keyboardBehavior.driverDeviceMode";
                break;
            case FeaturePayloadProjectionKind.ReviewedKeyboardBehavior:
                foreach (var path in MapKeyboardBehavior(observation.HeaderHints, queryResponse))
                {
                    yield return path;
                }

                break;
            case FeaturePayloadProjectionKind.ReviewedCustomPerformance
                when payload is { Length: 3 } && powerPrefix is not null && (!query || queryResponse):
                yield return payload[1] == 1
                    ? $"{powerPrefix}.customCpuLevels"
                    : $"{powerPrefix}.customGpuLevels";
                break;
            case FeaturePayloadProjectionKind.ReviewedBatterySetting:
                if (observation.HeaderHints?.CommandId is 0x0F or 0x8F)
                {
                    if (!query || queryResponse)
                    {
                        yield return queryResponse ? "battery.temporaryFullChargeQuery" : "battery.temporaryFullCharge";
                    }
                }
                else if (observation.HeaderHints?.CommandId is 0x12 or 0x92)
                {
                    if (!query || queryResponse)
                    {
                        yield return queryResponse ? "battery.limitQuery" : "battery.advertisedLimits";
                    }
                }

                break;
            case FeaturePayloadProjectionKind.ReviewedAdapterPower when queryResponse:
                yield return "power.adapterPowerQuery";
                break;
        }
    }

    private static IEnumerable<string> MapHardwareLightingEffect(byte[] payload)
    {
        switch (payload[0])
        {
            case 0:
                yield return "keyboardLighting.off";
                break;
            case 1:
                yield return "keyboardLighting.effects.wave.base";
                yield return payload[1] == 1
                    ? "keyboardLighting.effects.wave.leftDirection"
                    : "keyboardLighting.effects.wave.rightDirection";
                break;
            case 4:
                yield return "keyboardLighting.effects.spectrum.base";
                break;
            case 6:
                yield return "keyboardLighting.effects.static.base";
                yield return "keyboardLighting.effects.static.colors";
                break;
        }
    }

    private static IEnumerable<string> MapKeyboardBehavior(
        FeatureReportHeaderHints? header,
        bool queryResponse)
    {
        if (header is null)
        {
            yield break;
        }

        if (header.CommandClass == 0 && header.CommandId is 0x08 or 0x88)
        {
            if (header.CommandId == 0x08)
            {
                yield return "keyboardBehavior.functionKeyPrimary";
            }
            else if (queryResponse)
            {
                yield return "keyboardBehavior.functionKeyPrimaryGetter";
            }
        }
        else if (header.CommandClass == 2 && header.CommandId is 0x06 or 0x86)
        {
            if (header.CommandId == 0x06)
            {
                yield return "keyboardBehavior.gamingMode";
            }
            else if (queryResponse)
            {
                yield return "keyboardBehavior.gamingModeGetter";
            }
        }
        else if (header.CommandClass == 0x0F && header.CommandId is 0x18 or 0x98)
        {
            if (header.CommandId == 0x18)
            {
                yield return "keyboardBehavior.startupAnimation";
            }
            else if (queryResponse)
            {
                yield return "keyboardBehavior.startupAnimationGetter";
            }
        }
    }

    private static byte[]? TryGetPayload(SanitizedControlObservation observation)
    {
        if (observation.PayloadHex is null)
        {
            return null;
        }

        var bytes = Convert.FromHexString(observation.PayloadHex);
        if (bytes.Length is not (90 or 91))
        {
            return null;
        }

        var reportOffset = bytes.Length == 91 ? 1 : 0;
        var dataSize = bytes[reportOffset + 5];
        var payloadOffset = reportOffset + 8;
        return dataSize <= bytes.Length - payloadOffset
            ? bytes.AsSpan(payloadOffset, dataSize).ToArray()
            : null;
    }

    private static void AddSpecialKeyEvidence(
        EvidenceSessionSnapshot snapshot,
        Dictionary<string, List<ContributionEvidenceReference>> evidence)
    {
        var completedProbes = snapshot.SpecialKeyProbes
            .Where(static probe => probe.ObservationCount > 0)
            .Select(static probe => (probe.ExpectedKey, probe.ModeContext, probe.ProbeLabel))
            .ToHashSet();
        foreach (var indexed in snapshot.SpecialKeyObservations
                     .Select((item, index) => (Item: item, Index: index))
                     .Where(item => completedProbes.Contains(
                         (item.Item.ExpectedKey, item.Item.ModeContext, item.Item.ProbeLabel))))
        {
            var observation = indexed.Item;
            var reference = $"special-key-{indexed.Index:D4}:{observation.ProbeLabel}:{observation.ModeContext}:{observation.ExpectedKey}";
            var genericPath = observation.ExpectedKey switch
            {
                SpecialKeyExpectedKey.M1 => "specialKeys.m1.inputEvidence",
                SpecialKeyExpectedKey.M2 => "specialKeys.m2.inputEvidence",
                SpecialKeyExpectedKey.M3 => "specialKeys.m3.inputEvidence",
                SpecialKeyExpectedKey.M4 => "specialKeys.m4.inputEvidence",
                SpecialKeyExpectedKey.M5 => "specialKeys.m5.inputEvidence",
                SpecialKeyExpectedKey.Fn => "specialKeys.fn.inputEvidence",
                SpecialKeyExpectedKey.FnP => "specialKeys.fnP.inputEvidence",
                _ => throw new ArgumentOutOfRangeException(),
            };
            Add(evidence, genericPath, ContributionObservationLayer.WindowsRawInput, reference,
                $"Windows recorded input during the user-reported {observation.ExpectedKey} prompt in {observation.ModeContext} mode.");

            if (observation.Origin == SpecialKeyObservationOrigin.WindowsRawInput
                && observation.ExpectedKey == SpecialKeyExpectedKey.M1
                && observation.VirtualKey == 0x21)
            {
                Add(evidence, "specialKeys.m1PageUp", ContributionObservationLayer.WindowsRawInput, reference,
                    "Windows Raw Input reported Page Up during the user-reported M1 prompt.");
            }
            else if (observation.Origin == SpecialKeyObservationOrigin.WindowsRawInput
                && observation.ExpectedKey == SpecialKeyExpectedKey.M2
                && observation.VirtualKey == 0x22)
            {
                Add(evidence, "specialKeys.m2PageDown", ContributionObservationLayer.WindowsRawInput, reference,
                    "Windows Raw Input reported Page Down during the user-reported M2 prompt.");
            }

            if (observation.Origin == SpecialKeyObservationOrigin.ReviewedHidSpecialKey
                && observation.ReviewedSignal?.StartsWith("Fn", StringComparison.Ordinal) == true)
            {
                Add(evidence, "specialKeys.functionLayerReports", ContributionObservationLayer.WindowsRawInput,
                    reference, "A reviewed HID signal explicitly reported the Fn layer.");
            }
        }
    }

    private static IReadOnlyList<ContributionQualityGap> CreateQualityGaps(
        EvidenceSessionSnapshot snapshot,
        IReadOnlyList<ContributionActionWindowDraft> windows,
        HashSet<string> associated)
    {
        var gaps = new List<ContributionQualityGap>();
        AddSectionGap(gaps, "machineIdentity", snapshot.Inventory.MachineIdentity.State);
        AddSectionGap(gaps, "powerSource", snapshot.Inventory.PowerSource.State);
        AddSectionGap(gaps, "razerHidInterfaces", snapshot.Inventory.RazerHidInterfaces.State);
        AddSectionGap(gaps, "synapseProcesses", snapshot.Inventory.SynapseProcesses.State);
        AddSectionGap(gaps, "support.hidDescriptors", snapshot.Inventory.Support.HidDescriptors.State);
        AddSectionGap(gaps, "support.deviceDrivers", snapshot.Inventory.Support.DeviceDrivers.State);
        AddSectionGap(gaps, "support.executableModules", snapshot.Inventory.Support.ExecutableModules.State);
        AddSectionGap(gaps, "support.graphicsAdapters", snapshot.Inventory.Support.GraphicsAdapters.State);
        AddSectionGap(gaps, "support.displays", snapshot.Inventory.Support.Displays.State);
        AddSectionGap(gaps, "support.brightness", snapshot.Inventory.Support.Brightness.State);
        AddSectionGap(gaps, "support.thermalZones", snapshot.Inventory.Support.ThermalZones.State);
        AddSectionGap(gaps, "support.lampArrays", snapshot.Inventory.Support.LampArrays.State);
        AddSectionGap(gaps, "support.embeddedController", snapshot.Inventory.Support.EmbeddedController.State);
        AddSectionGap(gaps, "support.systemPlatform", snapshot.Inventory.Support.SystemPlatform.State);
        foreach (var gap in snapshot.Inventory.Support.CoverageGaps)
        {
            gaps.Add(new ContributionQualityGap(
                $"inventory.support.{gap.Area}",
                $"Inventory{gap.State}",
                gap.Reason));
        }

        if (snapshot.Inventory.Support.Displays.Value.Any(static item => item.VariableRefreshRateSupported is not null))
        {
            gaps.Add(new ContributionQualityGap(
                "inventory.support.displays.variableRefreshRange",
                "RangeNotCollected",
                "Windows reported variable-refresh support but did not provide the panel's validated refresh range."));
        }

        foreach (var window in windows.Where(static item => item.Gap is not null))
        {
            gaps.Add(new ContributionQualityGap(
                $"action.{window.ActivityId}",
                window.CompleteUserReportedSequence ? "NoMatchingObservation" : "IncompleteActionMarkers",
                window.Gap!));
        }

        foreach (var item in snapshot.Coverage.Where(static item => item.State != CoverageState.Completed))
        {
            gaps.Add(new ContributionQualityGap(
                $"coverage.{item.ActivityId}",
                item.Reason.ToString(),
                $"The guided item ended as {item.State}."));
        }

        if (snapshot.Observations.Select(ObservationReference).Any(reference => !associated.Contains(reference)))
        {
            gaps.Add(new ContributionQualityGap(
                "observations",
                "OutsideActionWindow",
                "One or more control observations could not be associated with a prompted action window."));
        }

        if (snapshot.Observations.Any(static item => !item.CompletePayloadRetained))
        {
            gaps.Add(new ContributionQualityGap(
                "observations",
                "PayloadRedacted",
                "One or more unsupported, malformed, failed, sensitive, or over-limit report bodies were withheld."));
        }

        if (snapshot.Observations.Any(static item => item.ReturnedLengthUnknown))
        {
            gaps.Add(new ContributionQualityGap(
                "observations",
                "ReturnedLengthNotProvided",
                "HidD_GetFeature succeeded, but the API does not provide an actual returned-byte count."));
        }

        if (snapshot.Observations.Any(static item => item.Status == ObservationStatus.Failed))
        {
            gaps.Add(new ContributionQualityGap(
                "observations",
                "ObservedApiFailure",
                "One or more observed calls completed with a failure status."));
        }

        if (snapshot.Observations.Any(static item => item.Status is ObservationStatus.Pending or ObservationStatus.Cancelled))
        {
            gaps.Add(new ContributionQualityGap(
                "observations",
                "CompletionNotObserved",
                "One or more observed calls remained pending or were cancelled before completion was observed."));
        }

        if (snapshot.RejectedMarkerCount + snapshot.RejectedObservationCount + snapshot.RejectedCoverageCount > 0)
        {
            gaps.Add(new ContributionQualityGap(
                "session",
                "RejectedRecords",
                "One or more records were rejected by a documented privacy or size bound."));
        }

        foreach (var probe in snapshot.SpecialKeyProbes.Where(static probe => probe.ObservationCount == 0 || probe.RejectedPacketCount > 0))
        {
            gaps.Add(new ContributionQualityGap(
                $"specialKeys.{probe.ExpectedKey}",
                probe.ObservationCount == 0 ? "NoMatchingInput" : "RejectedInput",
                probe.Gap));
        }

        gaps.Add(new ContributionQualityGap(
            "admission",
            "MaintainerValidationRequired",
            "Passive collection cannot establish query validation, setter validation, firmware readback, restoration confirmation, lifecycle safety, or production admission."));
        return gaps;
    }

    private static void AddSectionGap(List<ContributionQualityGap> gaps, string area, CollectionState state)
    {
        if (state != CollectionState.Collected)
        {
            gaps.Add(new ContributionQualityGap($"inventory.{area}", $"Inventory{state}",
                $"The Windows inventory section ended as {state}."));
        }
    }

    private static bool IsUsable(CollectionState state) =>
        state is CollectionState.Collected or CollectionState.Partial;

    private static void Add(
        Dictionary<string, List<ContributionEvidenceReference>> evidence,
        string path,
        ContributionObservationLayer layer,
        string referenceId,
        string summary)
    {
        if (evidence.TryGetValue(path, out var entries)
            && !entries.Any(item => item.Layer == layer && item.ReferenceId == referenceId))
        {
            entries.Add(new ContributionEvidenceReference(layer, referenceId, summary));
        }
    }

    private static string ObservationReference(SanitizedControlObservation observation, int index) =>
        $"observation-{index:D4}:{observation.OperationId}:{observation.Source}:{observation.Direction}";

    private static ContributionObservationRole GetObservationRole(SanitizedControlObservation observation)
    {
        // GET_FEATURE transports both setter ACKs and query responses.
        var query = observation.HeaderHints is { CommandId: var commandId } && (commandId & 0x80) != 0;
        if (observation.HeaderHints is null)
        {
            return ContributionObservationRole.UnclassifiedFeatureCall;
        }

        if (observation.Direction == ObservationDirection.Input)
        {
            return query ? ContributionObservationRole.FeatureQueryRequest : ContributionObservationRole.FeatureSetRequest;
        }

        return observation.Source == ObservationSource.ReturnedBuffer
            ? query ? ContributionObservationRole.FeatureQueryResponse : ContributionObservationRole.FeatureSetResponse
            : ContributionObservationRole.UnclassifiedFeatureCall;
    }

    private static IReadOnlyList<string> DistinctValues(IEnumerable<string?> values) =>
        values.OfType<string>()
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
