using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using Windows.Devices.Enumeration;
using Windows.Devices.Lights;
using Windows.System;

namespace SynapseObserver.Core;

public sealed partial class WindowsInventorySource
{
    private const int MaxSupportDisplayModes = EvidenceLimits.MaxDisplayModesPerDisplay;
    private const int MaxSupportDevices = 24;
    private IReadOnlyList<HidDescriptorSupportEvidence> _lastHidDescriptorSupport = [];

    public async ValueTask<SupportInventorySnapshot> CollectSupportInventoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var localHid = CollectIndependentSection(
            () => DiscoverLocalControlCandidates(cancellationToken),
            Array.Empty<LocalHidCandidate>(),
            "Local HID support metadata was unavailable.",
            cancellationToken);
        var hidState = _lastHidDescriptorSupport.Count == 0
            ? localHid.State == CollectionState.Collected ? CollectionState.Absent : localHid.State
            : localHid.State switch
            {
                CollectionState.Collected => CollectionState.Collected,
                CollectionState.Cancelled => CollectionState.Cancelled,
                _ => CollectionState.Partial,
            };
        var hid = new SectionResult<IReadOnlyList<HidDescriptorSupportEvidence>>(
            hidState,
            _lastHidDescriptorSupport,
            hidState is CollectionState.Absent or CollectionState.Collected ? null : localHid.Issue);
        var drivers = localHid.State is CollectionState.Unavailable or CollectionState.Cancelled
            ? new SectionResult<IReadOnlyList<DeviceDriverEvidence>>(
                localHid.State,
                [],
                "Blade HID ancestry was unavailable, so its driver provenance could not be collected.")
            : CollectIndependentSection(
                () => CollectDeviceDrivers(localHid.Value, cancellationToken),
                Array.Empty<DeviceDriverEvidence>(),
                "Blade driver provenance was unavailable.",
                cancellationToken);
        var executableModules = CollectIndependentSection(
            () => CollectExecutableModules(cancellationToken),
            Array.Empty<ExecutableModuleProvenanceEvidence>(),
            "Synapse executable provenance was unavailable.",
            cancellationToken);
        var systemPlatform = CollectIndependentSection(
            () => CollectSystemPlatform(cancellationToken),
            default(SystemPlatformEvidence?),
            "Windows build and processor metadata were unavailable.",
            cancellationToken);
        var graphics = CollectIndependentSection(
            () => CollectGraphicsAdapters(cancellationToken),
            Array.Empty<GraphicsAdapterEvidence>(),
            "Windows graphics adapter metadata was unavailable.",
            cancellationToken);
        var brightness = CollectIndependentSection(
            () => CollectBrightness(cancellationToken),
            Array.Empty<BrightnessEvidence>(),
            "Windows brightness metadata was unavailable.",
            cancellationToken);
        var internalDisplayLabels = brightness.Value
            .Select(static item => item.DisplayLabel)
            .ToHashSet(StringComparer.Ordinal);
        var displayCollection = CollectIndependentDisplaySection(internalDisplayLabels, cancellationToken);
        var displays = displayCollection.Section;
        var thermal = CollectIndependentSection(
            () => CollectThermalZones(cancellationToken),
            Array.Empty<ThermalZoneEvidence>(),
            "Windows thermal-zone metadata was unavailable.",
            cancellationToken);
        var pdhTemperatures = PdhTemperatureCollector.Collect(new WindowsPdhTemperatureNative(), cancellationToken);
        var bladeCandidates = localHid.Value
            .Where(static candidate =>
                IsBladeName(candidate.ProductName)
                || IsBladeName(candidate.ParentFriendlyName)
                || IsBladeName(candidate.UsbParentProductName))
            .ToArray();
        var lamps = await CollectIndependentSectionAsync(
            () => CollectLampArraysAsync(bladeCandidates, cancellationToken),
            Array.Empty<LampArrayEvidence>(),
            "Windows LampArray inventory was unavailable.",
            cancellationToken).ConfigureAwait(false);
        var embeddedController = new SectionResult<NotCollectedEvidence>(
            CollectionState.Skipped,
            new NotCollectedEvidence("EmbeddedControllerOrMcu", "NotCollectedUncapturedCommand"),
            "Embedded-controller and MCU queries were not collected because no reviewed read command is available.");

        var gaps = new List<SupportCoverageGap>
        {
            new("EmbeddedControllerOrMcu", CollectionState.Skipped, "NotCollectedUncapturedCommand"),
            new("EdidRawData", CollectionState.Skipped, "NotCollectedToAvoidSerialDisclosure"),
            new("LampArrayCurrentFrame", CollectionState.Unavailable, "WindowsApiHasNoCurrentFrameReadback"),
            new("LampArrayControlAvailability", CollectionState.Unavailable, "WindowsSdk22621DoesNotExposeAvailability"),
            new("NativeBrightnessDriverTopology", CollectionState.Skipped, "ControlAndVirtualInterfacesNotCollected"),
            new("DriverCatalogSignature", CollectionState.Skipped, "CatalogVerificationNotCollected"),
        };
        AddSectionGap(gaps, "HidDescriptors", hid.State);
        AddSectionGap(gaps, "DeviceDrivers", drivers.State);
        AddSectionGap(gaps, "ExecutableModules", executableModules.State);
        AddSectionGap(gaps, "SystemPlatform", systemPlatform.State);
        AddSectionGap(gaps, "GraphicsAdapters", graphics.State);
        AddSectionGap(gaps, "Displays", displays.State);
        AddSectionGap(gaps, "Brightness", brightness.State);
        AddSectionGap(gaps, "ThermalZones", thermal.State);
        AddSectionGap(gaps, "PdhTemperatures", pdhTemperatures.State);
        AddSectionGap(gaps, "LampArrayLayout", lamps.State);
        if (lamps.Value.Any(static lamp => lamp.PhysicalDeviceLabel is null))
        {
            gaps.Add(new SupportCoverageGap(
                "LampArrayPhysicalRoot",
                CollectionState.Partial,
                "MultipleMatchingBladeRoots"));
        }

        gaps.AddRange(displayCollection.Gaps);
        if (displays.Value.Any(static display => display.IsInternal is null))
        {
            gaps.Add(new SupportCoverageGap(
                "DisplayConnectionClassification",
                CollectionState.Partial,
                "ConnectorClassificationUnavailable"));
        }

        return new SupportInventorySnapshot(
            hid,
            drivers,
            executableModules,
            graphics,
            displays,
            brightness,
            thermal,
            lamps,
            embeddedController,
            gaps)
        {
            SystemPlatform = systemPlatform,
            PdhTemperatures = pdhTemperatures,
        };
    }

    internal static SectionResult<T> CollectIndependentSection<T>(
        Func<SectionResult<T>> collector,
        T emptyValue,
        string unavailableIssue,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return SectionResult<T>.Cancelled(emptyValue);
        }

        try
        {
            return collector();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SectionResult<T>.Cancelled(emptyValue);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return SectionResult<T>.Unavailable(emptyValue, unavailableIssue);
        }
    }

    private static async ValueTask<SectionResult<T>> CollectIndependentSectionAsync<T>(
        Func<Task<SectionResult<T>>> collector,
        T emptyValue,
        string unavailableIssue,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return SectionResult<T>.Cancelled(emptyValue);
        }

        try
        {
            return await collector().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SectionResult<T>.Cancelled(emptyValue);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return SectionResult<T>.Unavailable(emptyValue, unavailableIssue);
        }
    }

    private DisplayCollectionResult CollectIndependentDisplaySection(
        IReadOnlySet<string> internalDisplayLabels,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new DisplayCollectionResult(
                SectionResult<IReadOnlyList<DisplayEvidence>>.Cancelled([]),
                []);
        }

        try
        {
            return CollectDisplays(internalDisplayLabels, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DisplayCollectionResult(
                SectionResult<IReadOnlyList<DisplayEvidence>>.Cancelled([]),
                []);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return new DisplayCollectionResult(
                SectionResult<IReadOnlyList<DisplayEvidence>>.Unavailable(
                    [],
                    "Windows display metadata was unavailable."),
                [new SupportCoverageGap(
                    "DisplayConfiguration",
                    CollectionState.Unavailable,
                    "SectionCollectorFailed")]);
        }
    }

    private async Task<SectionResult<IReadOnlyList<LampArrayEvidence>>> CollectLampArraysAsync(
        IReadOnlyList<LocalHidCandidate> bladeCandidates,
        CancellationToken cancellationToken)
    {
        if (bladeCandidates.Count == 0)
        {
            return SectionResult<IReadOnlyList<LampArrayEvidence>>.Absent([]);
        }

        var bladeProductIds = bladeCandidates.Select(static candidate => candidate.ProductId).ToHashSet();

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            var enumeration = await EnumerateLampArraysAsync(bounded.Token).ConfigureAwait(false);
            var arrays = new List<LampArrayEvidence>();
            var failures = enumeration.Truncated ? 1 : 0;
            foreach (var device in enumeration.Devices)
            {
                bounded.Token.ThrowIfCancellationRequested();
                try
                {
                    var lampArray = await LampArray.FromIdAsync(device.Id)
                        .AsTask(bounded.Token)
                        .ConfigureAwait(false);
                    if (lampArray is null
                        || lampArray.HardwareVendorId != RazerVendorId
                        || !bladeProductIds.Contains(lampArray.HardwareProductId))
                    {
                        continue;
                    }

                    var physicalLabels = bladeCandidates
                        .Where(candidate => candidate.ProductId == lampArray.HardwareProductId)
                        .Select(candidate => candidate.UsbParentDeviceInstanceId
                            ?? candidate.ParentDeviceInstanceId
                            ?? candidate.InterfacePath)
                        .Select(identity => MakeOpaqueLabel("device", identity))
                        .Distinct(StringComparer.Ordinal)
                        .Take(2)
                        .ToArray();
                    if (physicalLabels.Length != 1)
                    {
                        failures++;
                    }

                    var snapshot = SnapshotLampArray(
                        device.Id,
                        physicalLabels.Length == 1 ? physicalLabels[0] : null,
                        lampArray);
                    arrays.Add(snapshot.Evidence);
                    if (snapshot.Truncated)
                    {
                        failures++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    failures++;
                }

                if (arrays.Count >= 16)
                {
                    failures++;
                    break;
                }
            }

            IReadOnlyList<LampArrayEvidence> value = arrays;
            if (arrays.Count == 0 && failures == 0)
            {
                return SectionResult<IReadOnlyList<LampArrayEvidence>>.Absent(value);
            }

            if (arrays.Count == 0)
            {
                return SectionResult<IReadOnlyList<LampArrayEvidence>>.Unavailable(
                    value,
                    "Windows LampArray devices for the selected Blade could not be opened for read-only inventory.");
            }

            return failures > 0
                ? SectionResult<IReadOnlyList<LampArrayEvidence>>.Partial(
                    value,
                    "Some LampArray layout details were unavailable or reached a documented bound.")
                : SectionResult<IReadOnlyList<LampArrayEvidence>>.Collected(value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return SectionResult<IReadOnlyList<LampArrayEvidence>>.Unavailable(
                [],
                "Windows LampArray discovery exceeded its bounded observation time.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            return SectionResult<IReadOnlyList<LampArrayEvidence>>.Unavailable(
                [],
                "Windows LampArray discovery was unavailable.");
        }
    }

    private static async Task<LampArrayEnumerationResult> EnumerateLampArraysAsync(
        CancellationToken cancellationToken)
    {
        const int maxDevices = 64;
        var devices = new ConcurrentQueue<DeviceInformation>();
        var observedCount = 0;
        var truncated = 0;
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = DeviceInformation.CreateWatcher(LampArray.GetDeviceSelector());

        void OnAdded(DeviceWatcher _, DeviceInformation device)
        {
            if (Interlocked.Increment(ref observedCount) <= maxDevices)
            {
                devices.Enqueue(device);
            }
            else
            {
                Interlocked.Exchange(ref truncated, 1);
            }
        }

        void OnEnumerationCompleted(DeviceWatcher _, object __) => completed.TrySetResult(true);

        void OnStopped(DeviceWatcher _, object __) => completed.TrySetResult(false);

        watcher.Added += OnAdded;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        watcher.Stopped += OnStopped;
        try
        {
            watcher.Start();
            var enumerated = await completed.Task
                .WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
            if (!enumerated)
            {
                throw new InvalidOperationException("The Windows LampArray watcher stopped before enumeration completed.");
            }
        }
        finally
        {
            watcher.Added -= OnAdded;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            watcher.Stopped -= OnStopped;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }

        return new LampArrayEnumerationResult(devices.ToArray(), Volatile.Read(ref truncated) != 0);
    }

    private LampArrayReadResult SnapshotLampArray(
        string localDeviceId,
        string? physicalDeviceLabel,
        LampArray lampArray)
    {
        const int maxLamps = 4096;
        const int maxKeys = 256;
        const int maxIndicesPerKey = 512;
        var lampLimit = Math.Min(lampArray.LampCount, maxLamps);
        var lamps = new List<LampPositionEvidence>(lampLimit);
        for (var index = 0; index < lampLimit; index++)
        {
            var info = lampArray.GetLampInfo(index);
            var position = info.Position;
            lamps.Add(new LampPositionEvidence(
                info.Index,
                position.X,
                position.Y,
                position.Z,
                info.Purposes.ToString().Replace(", ", "_", StringComparison.Ordinal),
                info.RedLevelCount,
                info.GreenLevelCount,
                info.BlueLevelCount,
                info.GainLevelCount,
                checked((long)(info.UpdateLatency.TotalMilliseconds * 1000))));
        }

        var mappings = new List<LampVirtualKeyMappingEvidence>();
        var mappingFailure = false;
        if (lampArray.SupportsVirtualKeys)
        {
            var keys = Enum.GetValues<VirtualKey>().Distinct().ToArray();
            if (keys.Length > maxKeys)
            {
                mappingFailure = true;
            }

            foreach (var key in keys.Take(maxKeys))
            {
                try
                {
                    var allIndices = lampArray.GetIndicesForKey(key)
                        .Where(static index => index >= 0)
                        .Distinct()
                        .ToArray();
                    if (allIndices.Length > maxIndicesPerKey)
                    {
                        mappingFailure = true;
                    }

                    var indices = allIndices.Take(maxIndicesPerKey).ToArray();
                    if (indices.Length > 0)
                    {
                        mappings.Add(new LampVirtualKeyMappingEvidence(key.ToString(), indices));
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                {
                    mappingFailure = true;
                }
            }
        }

        var bounds = lampArray.BoundingBox;
        var evidence = new LampArrayEvidence(
            MakeOpaqueLabel("lamp", localDeviceId),
            lampArray.LampCount,
            CountDistinctLampCoordinates(lamps.Select(static lamp => lamp.Y)),
            CountDistinctLampCoordinates(lamps.Select(static lamp => lamp.X)),
            mappings.Select(static mapping => mapping.Key).ToArray())
        {
            VendorId = lampArray.HardwareVendorId,
            ProductId = lampArray.HardwareProductId,
            PhysicalDeviceLabel = physicalDeviceLabel,
            Kind = lampArray.LampArrayKind.ToString(),
            IsConnected = lampArray.IsConnected,
            IsEnabled = lampArray.IsEnabled,
            SupportsVirtualKeys = lampArray.SupportsVirtualKeys,
            BrightnessLevel = lampArray.BrightnessLevel,
            MinimumUpdateIntervalMicroseconds = checked((long)(lampArray.MinUpdateInterval.TotalMilliseconds * 1000)),
            BoundingBoxWidth = bounds.X,
            BoundingBoxHeight = bounds.Y,
            BoundingBoxDepth = bounds.Z,
            Lamps = lamps,
            VirtualKeys = mappings,
        };
        return new LampArrayReadResult(
            evidence,
            mappingFailure || lampArray.LampCount > maxLamps || mappings.Count >= maxKeys);
    }

    internal static int? CountDistinctLampCoordinates(IEnumerable<float> coordinates)
    {
        var distinct = coordinates
            .Where(float.IsFinite)
            .Select(static coordinate => MathF.Round(coordinate, 4))
            .Distinct()
            .Take(257)
            .Count();
        return distinct is > 0 and <= 256 ? distinct : null;
    }

    private static void AddSectionGap(List<SupportCoverageGap> gaps, string area, CollectionState state)
    {
        if (state == CollectionState.Collected)
        {
            return;
        }

        gaps.Add(new SupportCoverageGap(area, state, state switch
        {
            CollectionState.Partial => "PartiallyCollected",
            CollectionState.Absent => "NoItemsReported",
            CollectionState.Cancelled => "Cancelled",
            CollectionState.Skipped => "NotCollected",
            _ => "WindowsApiUnavailable",
        }));
    }

    private SectionResult<IReadOnlyList<DeviceDriverEvidence>> CollectDeviceDrivers(
        IReadOnlyList<LocalHidCandidate> candidates,
        CancellationToken cancellationToken)
    {
        var results = new List<DeviceDriverEvidence>();
        var failures = 0;
        var instances = candidates
            .Where(static candidate =>
                IsBladeName(candidate.ProductName)
                || IsBladeName(candidate.ParentFriendlyName)
                || IsBladeName(candidate.UsbParentProductName))
            .SelectMany(static candidate => new[]
            {
                (candidate.ParentDeviceInstanceId, candidate.ParentFriendlyName, candidate.UsbParentDeviceInstanceId),
                (candidate.UsbParentDeviceInstanceId, candidate.UsbParentProductName, candidate.UsbParentDeviceInstanceId),
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Item1))
            .DistinctBy(static item => item.Item1, StringComparer.OrdinalIgnoreCase)
            .Take(MaxSupportDevices)
            .ToArray();

        foreach (var (instanceId, knownName, physicalId) in instances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var device = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Enum\{instanceId}",
                    writable: false);
                if (device is null)
                {
                    failures++;
                    continue;
                }

                var driverKeyName = RegistryText(device, "Driver");
                string? provider = null;
                string? driverVersion = null;
                string? infName = null;
                if (driverKeyName is not null)
                {
                    using var driver = Registry.LocalMachine.OpenSubKey(
                        $@"SYSTEM\CurrentControlSet\Control\Class\{driverKeyName}",
                        writable: false);
                    provider = RegistryText(driver, "ProviderName");
                    driverVersion = RegistryText(driver, "DriverVersion");
                    infName = RegistryText(driver, "InfPath");
                }

                var service = RegistryText(device, "Service");
                var upperFilters = RegistryStrings(device, "UpperFilters");
                var lowerFilters = RegistryStrings(device, "LowerFilters");
                var serviceNames = new[] { service }
                    .Concat(upperFilters)
                    .Concat(lowerFilters)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(8);
                var files = new List<DriverFileEvidence>();
                foreach (var serviceName in serviceNames)
                {
                    var file = CollectDriverFile(serviceName!);
                    if (file is null)
                    {
                        failures++;
                    }
                    else
                    {
                        files.Add(file);
                    }
                }
                var groupIdentity = physicalId ?? instanceId!;
                results.Add(new DeviceDriverEvidence(
                    MakeOpaqueLabel("device", groupIdentity),
                    CleanText(knownName) ?? RegistryText(device, "FriendlyName") ?? RegistryText(device, "DeviceDesc"),
                    provider,
                    driverVersion,
                    infName,
                    service,
                    upperFilters,
                    lowerFilters,
                    files));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                failures++;
            }
        }

        IReadOnlyList<DeviceDriverEvidence> value = results;
        if (results.Count == 0 && failures == 0)
        {
            return SectionResult<IReadOnlyList<DeviceDriverEvidence>>.Absent(value);
        }

        if (results.Count == 0)
        {
            return SectionResult<IReadOnlyList<DeviceDriverEvidence>>.Unavailable(
                value,
                "Driver metadata for the selected Razer HID ancestry was unavailable.");
        }

        return failures > 0
            ? SectionResult<IReadOnlyList<DeviceDriverEvidence>>.Partial(
                value,
                "Some driver metadata for the selected Razer HID ancestry was unavailable.")
            : SectionResult<IReadOnlyList<DeviceDriverEvidence>>.Collected(value);
    }

    private static DriverFileEvidence? CollectDriverFile(string serviceName)
    {
        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}",
                writable: false);
            var imagePath = RegistryText(service, "ImagePath");
            var path = ResolveServiceImagePath(imagePath);
            if (path is null)
            {
                return null;
            }

            var fileName = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                return new DriverFileEvidence(serviceName, fileName, null, null, FileSignatureState.Unavailable);
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var version = CleanText(FileVersionInfo.GetVersionInfo(path).FileVersion);
            return new DriverFileEvidence(serviceName, fileName, version, hash, GetSignatureState(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return null;
        }
    }

    private static bool IsPhysicalBladeHid(ParentIdentity parent, string? productName) =>
        IsBladeName(productName) || IsBladeName(parent.FriendlyName) || IsBladeName(parent.UsbProductName);

    internal static bool IsBladeName(string? value) =>
        value?.Contains("Blade", StringComparison.OrdinalIgnoreCase) == true;

    private SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>> CollectExecutableModules(
        CancellationToken cancellationToken)
    {
        var results = new List<ExecutableModuleProvenanceEvidence>();
        var failures = 0;
        var selectedProcesses = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string processName;
                try
                {
                    processName = process.ProcessName;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (!SynapseInventoryScope.IsProcessName(processName))
                {
                    continue;
                }

                if (selectedProcesses++ >= EvidenceLimits.MaxSynapseProcesses || results.Count >= 64)
                {
                    failures++;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var mainPath = process.MainModule?.FileName;
                    var processLabel = MakeOpaqueLabel("process", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (!string.IsNullOrWhiteSpace(mainPath))
                    {
                        var main = CollectExecutableFile(processLabel, mainPath, isMainExecutable: true);
                        if (main is not null)
                        {
                            results.Add(main);
                        }
                    }

                    foreach (ProcessModule module in process.Modules)
                    {
                        using (module)
                        {
                            if (results.Count >= 64)
                            {
                                failures++;
                                break;
                            }

                            cancellationToken.ThrowIfCancellationRequested();
                            if (string.Equals(module.FileName, mainPath, StringComparison.OrdinalIgnoreCase)
                                || !SynapseInventoryScope.IsModule(module.FileName, mainPath))
                            {
                                continue;
                            }

                            var evidence = CollectExecutableFile(processLabel, module.FileName, isMainExecutable: false);
                            if (evidence is not null)
                            {
                                results.Add(evidence);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    failures++;
                }
            }
        }

        IReadOnlyList<ExecutableModuleProvenanceEvidence> value = results;
        if (results.Count == 0 && failures == 0)
        {
            return SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>>.Absent(value);
        }

        if (results.Count == 0)
        {
            return SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>>.Unavailable(
                value,
                "Signed Synapse executable and module provenance was unavailable.");
        }

        return failures > 0
            ? SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>>.Partial(
                value,
                "Some signed Synapse executable or module provenance was unavailable or reached a documented bound.")
            : SectionResult<IReadOnlyList<ExecutableModuleProvenanceEvidence>>.Collected(value);
    }

    private static ExecutableModuleProvenanceEvidence? CollectExecutableFile(
        string processLabel,
        string path,
        bool isMainExecutable)
    {
        try
        {
            var fileName = CleanText(Path.GetFileName(path)) ?? "module";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            var version = CleanText(FileVersionInfo.GetVersionInfo(path).FileVersion);
            return new ExecutableModuleProvenanceEvidence(
                processLabel,
                fileName,
                version,
                hash,
                GetSignatureState(path),
                isMainExecutable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    internal static string? ResolveServiceImagePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (expanded.StartsWith("\\SystemRoot\\", StringComparison.OrdinalIgnoreCase))
        {
            expanded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                expanded[12..]);
        }

        if (expanded.StartsWith('"'))
        {
            var quote = expanded.IndexOf('"', 1);
            expanded = quote > 1 ? expanded[1..quote] : expanded.Trim('"');
        }
        else
        {
            var extension = expanded.IndexOf(".sys", StringComparison.OrdinalIgnoreCase);
            if (extension >= 0)
            {
                expanded = expanded[..(extension + 4)];
            }
        }

        if (!Path.IsPathRooted(expanded))
        {
            expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), expanded);
        }

        return Path.GetFullPath(expanded);
    }

    private SectionResult<IReadOnlyList<GraphicsAdapterEvidence>> CollectGraphicsAdapters(
        CancellationToken cancellationToken)
    {
        var query = QueryWmi(
            @"root\cimv2",
            "SELECT Name, AdapterCompatibility, DriverVersion, PNPDeviceID FROM Win32_VideoController",
            ["Name", "AdapterCompatibility", "DriverVersion", "PNPDeviceID"],
            cancellationToken);
        var adapters = query.Rows.Take(8).Select((row, index) =>
        {
            var pnpIdentity = WmiString(row, "PNPDeviceID");
            var pci = ParsePciIdentity(pnpIdentity);
            return new GraphicsAdapterEvidence(
                MakeOpaqueLabel("gpu", pnpIdentity ?? index.ToString()),
                CleanText(WmiString(row, "Name")),
                CleanText(WmiString(row, "AdapterCompatibility")),
                CleanText(WmiString(row, "DriverVersion")))
            {
                PciVendorId = pci.VendorId,
                PciDeviceId = pci.DeviceId,
                PciSubsystemVendorId = pci.SubsystemVendorId,
                PciSubsystemId = pci.SubsystemId,
            };
        }).ToArray();
        return WmiSection(adapters, query, "Windows graphics adapter metadata was unavailable.");
    }

    private SectionResult<SystemPlatformEvidence?> CollectSystemPlatform(CancellationToken cancellationToken)
    {
        string? productName = null;
        string? displayVersion = null;
        string? build = null;
        int? updateBuildRevision = null;
        var registryAvailable = false;
        try
        {
            using var currentVersion = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
                writable: false);
            if (currentVersion is not null)
            {
                registryAvailable = true;
                productName = RegistryText(currentVersion, "ProductName");
                displayVersion = RegistryText(currentVersion, "DisplayVersion");
                build = RegistryText(currentVersion, "CurrentBuildNumber");
                if (currentVersion.GetValue("UBR") is int ubr && ubr >= 0)
                {
                    updateBuildRevision = ubr;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            registryAvailable = false;
        }

        var processorsQuery = QueryWmi(
            @"root\cimv2",
            "SELECT Name, Manufacturer FROM Win32_Processor",
            ["Name", "Manufacturer"],
            cancellationToken);
        var processors = processorsQuery.Rows.Take(8).Select((row, index) => new ProcessorEvidence(
            MakeOpaqueLabel("cpu", index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            CleanText(WmiString(row, "Name")),
            CleanText(WmiString(row, "Manufacturer")))).ToArray();
        var value = new SystemPlatformEvidence(
            productName,
            displayVersion,
            build,
            updateBuildRevision,
            RuntimeInformation.OSArchitecture.ToString(),
            processors);
        if (!registryAvailable && !processorsQuery.Available)
        {
            return SectionResult<SystemPlatformEvidence?>.Unavailable(
                value,
                "Windows build and processor metadata were unavailable.");
        }

        return !registryAvailable || !processorsQuery.Available
            ? SectionResult<SystemPlatformEvidence?>.Partial(
                value,
                "Some Windows build or processor metadata was unavailable.")
            : SectionResult<SystemPlatformEvidence?>.Collected(value);
    }

    internal static PciIdentity ParsePciIdentity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !value.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        ushort? vendor = ParsePciWord(value, "VEN_");
        ushort? device = ParsePciWord(value, "DEV_");
        ushort? subsystem = null;
        ushort? subsystemVendor = null;
        var subsystemIndex = value.IndexOf("SUBSYS_", StringComparison.OrdinalIgnoreCase);
        if (subsystemIndex >= 0 && subsystemIndex + 15 <= value.Length
            && uint.TryParse(
                value.AsSpan(subsystemIndex + 7, 8),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out var subsystemValue))
        {
            subsystem = checked((ushort)(subsystemValue >> 16));
            subsystemVendor = checked((ushort)(subsystemValue & ushort.MaxValue));
        }

        return new PciIdentity(vendor, device, subsystemVendor, subsystem);
    }

    private static ushort? ParsePciWord(string value, string marker)
    {
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index >= 0 && index + marker.Length + 4 <= value.Length
            && ushort.TryParse(
                value.AsSpan(index + marker.Length, 4),
                System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;
    }

    private DisplayCollectionResult CollectDisplays(
        IReadOnlySet<string> internalDisplayLabels,
        CancellationToken cancellationToken)
    {
        var results = new List<DisplayEvidence>();
        var failures = 0;
        var configuration = CollectDisplayConfiguration(cancellationToken);
        var gaps = new List<SupportCoverageGap>(configuration.Gaps);
        if (!configuration.Available)
        {
            failures++;
        }
        for (uint adapterIndex = 0; adapterIndex < 16; adapterIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adapter = DisplayDevice.Create();
            if (!EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
            {
                break;
            }

            if ((adapter.StateFlags & DisplayDeviceActive) == 0 || (adapter.StateFlags & DisplayDeviceMirroringDriver) != 0)
            {
                continue;
            }

            var monitorName = CleanText(adapter.DeviceString);
            var monitor = DisplayDevice.Create();
            if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0))
            {
                monitorName = CleanText(monitor.DeviceString) ?? monitorName;
            }

            var current = TryGetDisplayMode(adapter.DeviceName, EnumCurrentSettings);
            if (current is null)
            {
                failures++;
            }

            var modeEnumeration = CollectBoundedDisplayModes(
                modeIndex => TryGetDisplayMode(adapter.DeviceName, modeIndex));
            if (modeEnumeration.Truncated)
            {
                failures++;
                gaps.Add(new SupportCoverageGap(
                    "DisplayModes",
                    CollectionState.Partial,
                    "DocumentedBoundReached"));
            }

            var localIdentity = string.Concat(adapter.DeviceName, "|", adapter.DeviceId, "|", monitor.DeviceId);
            var displayLabel = MakeDisplayLabel(monitor.DeviceId, localIdentity);
            configuration.Targets.TryGetValue(adapter.DeviceName, out var target);
            results.Add(new DisplayEvidence(
                displayLabel,
                target?.FriendlyName ?? monitorName,
                target?.IsInternal ?? (internalDisplayLabels.Contains(displayLabel) ? true : null),
                current,
                modeEnumeration.Modes)
            {
                ConnectionKind = target?.ConnectionKind,
                HdrSupported = target?.HdrSupported,
                HdrEnabled = target?.HdrEnabled,
                BitsPerColorChannel = target?.BitsPerColorChannel,
                ColorEncoding = target?.ColorEncoding,
                ActiveColorMode = target?.ActiveColorMode,
                VariableRefreshRateSupported = null,
                DynamicRefreshRateActive = target?.DynamicRefreshRateActive,
                ActiveRefreshRateHertz = target?.ActiveRefreshRateHertz,
                EdidManufacturerCode = target?.EdidManufacturerCode,
                EdidProductCode = target?.EdidProductCode,
                ColorProfiles = target?.ColorProfiles,
            });
        }

        IReadOnlyList<DisplayEvidence> value = results;
        if (results.Count == 0 && failures == 0)
        {
            return new DisplayCollectionResult(
                SectionResult<IReadOnlyList<DisplayEvidence>>.Absent(value),
                gaps);
        }

        if (results.Count == 0)
        {
            return new DisplayCollectionResult(
                SectionResult<IReadOnlyList<DisplayEvidence>>.Unavailable(value, "Windows display modes were unavailable."),
                gaps);
        }

        var section = failures > 0
            ? SectionResult<IReadOnlyList<DisplayEvidence>>.Partial(value, "One or more current display modes were unavailable.")
            : SectionResult<IReadOnlyList<DisplayEvidence>>.Collected(value);
        return new DisplayCollectionResult(section, gaps);
    }

    internal static DisplayModeEnumerationResult CollectBoundedDisplayModes(
        Func<int, DisplayModeEvidence?> readMode)
    {
        const int maxRawModes = 4096;
        var modes = new List<DisplayModeEvidence>(MaxSupportDisplayModes);
        var seenModes = new HashSet<DisplayModeEvidence>();
        var reachedEnd = false;
        var truncated = false;
        for (var modeIndex = 0; modeIndex < maxRawModes; modeIndex++)
        {
            var mode = readMode(modeIndex);
            if (mode is null)
            {
                reachedEnd = true;
                break;
            }

            if (!seenModes.Add(mode))
            {
                continue;
            }

            if (modes.Count < MaxSupportDisplayModes)
            {
                modes.Add(mode);
            }
            else
            {
                truncated = true;
            }
        }

        if (!reachedEnd && readMode(maxRawModes) is not null)
        {
            truncated = true;
        }

        return new DisplayModeEnumerationResult(modes, truncated);
    }

    private DisplayConfigurationResult CollectDisplayConfiguration(CancellationToken cancellationToken)
    {
        const uint flags = QdcOnlyActivePaths | QdcVirtualModeAware | QdcVirtualRefreshRateAware;
        var targets = new Dictionary<string, DisplayTargetMetadata>(StringComparer.OrdinalIgnoreCase);
        var gaps = new List<SupportCoverageGap>
        {
            new("VariableRefreshRateCapability", CollectionState.Unavailable, "NotExposedByDisplayConfig"),
        };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sizeResult = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (sizeResult != ErrorSuccess || pathCount > 64 || modeCount > 256)
            {
                gaps.Add(new SupportCoverageGap(
                    "DisplayConfiguration",
                    CollectionState.Unavailable,
                    sizeResult == ErrorSuccess ? "DocumentedBoundReached" : "WindowsApiUnavailable"));
                return new DisplayConfigurationResult(false, targets, gaps);
            }

            var paths = new DisplayConfigPathInfo[pathCount];
            var modes = new DisplayConfigModeInfo[modeCount];
            var queryResult = QueryDisplayConfig(
                flags,
                ref pathCount,
                paths,
                ref modeCount,
                modes,
                IntPtr.Zero);
            if (queryResult == ErrorInsufficientBuffer)
            {
                continue;
            }

            if (queryResult != ErrorSuccess)
            {
                gaps.Add(new SupportCoverageGap(
                    "DisplayConfiguration",
                    CollectionState.Unavailable,
                    "WindowsApiUnavailable"));
                return new DisplayConfigurationResult(false, targets, gaps);
            }

            foreach (var path in paths.Take(checked((int)pathCount)))
            {
                if ((path.Flags & DisplayConfigPathActive) == 0)
                {
                    continue;
                }

                var source = new DisplayConfigSourceDeviceName
                {
                    Header = CreateDisplayConfigHeader(
                        DisplayConfigDeviceInfoGetSourceName,
                        path.SourceInfo.AdapterId,
                        path.SourceInfo.Id,
                        Marshal.SizeOf<DisplayConfigSourceDeviceName>()),
                    ViewGdiDeviceName = string.Empty,
                };
                if (DisplayConfigGetSourceName(ref source) != ErrorSuccess)
                {
                    gaps.Add(new SupportCoverageGap(
                        "DisplaySourceIdentity",
                        CollectionState.Partial,
                        "WindowsApiUnavailable"));
                    continue;
                }

                var target = new DisplayConfigTargetDeviceName
                {
                    Header = CreateDisplayConfigHeader(
                        DisplayConfigDeviceInfoGetTargetName,
                        path.TargetInfo.AdapterId,
                        path.TargetInfo.Id,
                        Marshal.SizeOf<DisplayConfigTargetDeviceName>()),
                    MonitorFriendlyDeviceName = string.Empty,
                    MonitorDevicePath = string.Empty,
                };
                var targetAvailable = DisplayConfigGetTargetName(ref target) == ErrorSuccess;
                if (!targetAvailable)
                {
                    gaps.Add(new SupportCoverageGap(
                        "DisplayTargetIdentity",
                        CollectionState.Partial,
                        "WindowsApiUnavailable"));
                }
                var advanced = new DisplayConfigGetAdvancedColorInfo2
                {
                    Header = CreateDisplayConfigHeader(
                        DisplayConfigDeviceInfoGetAdvancedColorInfo2,
                        path.TargetInfo.AdapterId,
                        path.TargetInfo.Id,
                        Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo2>()),
                };
                var advancedAvailable = DisplayConfigGetAdvancedColorInfo2Native(ref advanced) == ErrorSuccess;
                var colorProfiles = CollectDisplayColorProfiles(
                    path.TargetInfo.AdapterId,
                    path.SourceInfo.Id,
                    cancellationToken);
                var sourceName = source.ViewGdiDeviceName.TrimEnd('\0');
                targets[sourceName] = new DisplayTargetMetadata(
                    targetAvailable ? CleanText(target.MonitorFriendlyDeviceName.TrimEnd('\0')) : null,
                    IsInternalOutputTechnology(path.TargetInfo.OutputTechnology),
                    ConnectionKind(path.TargetInfo.OutputTechnology),
                    advancedAvailable ? (advanced.Value & HdrSupportedMask) != 0 : null,
                    advancedAvailable
                        ? (advanced.Value & HdrSupportedMask) != 0
                            && (advanced.Value & HdrUserEnabledMask) != 0
                        : null,
                    advancedAvailable && advanced.BitsPerColorChannel > 0
                        ? checked((int)advanced.BitsPerColorChannel)
                        : null,
                    advancedAvailable ? advanced.ColorEncoding : null,
                    advancedAvailable ? advanced.ActiveColorMode : null,
                    (path.Flags & DisplayConfigPathBoostRefreshRate) != 0,
                    path.TargetInfo.RefreshRate.Denominator == 0
                        ? null
                        : (double)path.TargetInfo.RefreshRate.Numerator / path.TargetInfo.RefreshRate.Denominator,
                    targetAvailable ? DecodeEdidManufacturer(target.EdidManufactureId) : null,
                    targetAvailable && target.EdidProductCodeId != 0
                        ? target.EdidProductCodeId.ToString("X4", System.Globalization.CultureInfo.InvariantCulture)
                        : null,
                    colorProfiles);

                if (!advancedAvailable)
                {
                    gaps.Add(new SupportCoverageGap(
                        "DisplayAdvancedColor",
                        CollectionState.Unavailable,
                        "WindowsApiUnavailable"));
                }

                if (colorProfiles.State is CollectionState.Partial or CollectionState.Unavailable)
                {
                    gaps.Add(new SupportCoverageGap(
                        "DisplayColorProfiles",
                        colorProfiles.State,
                        colorProfiles.State == CollectionState.Partial
                            ? "SomeProfileStateUnavailable"
                            : "WindowsApiUnavailable"));
                }
            }

            return new DisplayConfigurationResult(true, targets, gaps.Distinct().ToArray());
        }

        gaps.Add(new SupportCoverageGap(
            "DisplayConfiguration",
            CollectionState.Unavailable,
            "TopologyChangedDuringRead"));
        return new DisplayConfigurationResult(false, targets, gaps);
    }

    internal static string? DecodeEdidManufacturer(ushort encoded)
    {
        var first = (char)(((encoded >> 10) & 0x1F) + 'A' - 1);
        var second = (char)(((encoded >> 5) & 0x1F) + 'A' - 1);
        var third = (char)((encoded & 0x1F) + 'A' - 1);
        return first is >= 'A' and <= 'Z'
            && second is >= 'A' and <= 'Z'
            && third is >= 'A' and <= 'Z'
                ? string.Concat(first, second, third)
                : null;
    }

    internal static bool IsInternalOutputTechnology(uint technology) => technology is
        DisplayConfigOutputTechnologyLvds or
        DisplayConfigOutputTechnologyDisplayPortEmbedded or
        DisplayConfigOutputTechnologyUdiEmbedded or
        DisplayConfigOutputTechnologyInternal;

    internal static string ConnectionKind(uint technology) => technology switch
    {
        0xFFFFFFFF => "Other",
        0 => "Hd15",
        1 => "SVideo",
        2 => "CompositeVideo",
        3 => "ComponentVideo",
        4 => "Dvi",
        5 => "Hdmi",
        DisplayConfigOutputTechnologyLvds => "Lvds",
        8 => "DviJapan",
        9 => "Sdi",
        10 => "DisplayPortExternal",
        DisplayConfigOutputTechnologyDisplayPortEmbedded => "DisplayPortEmbedded",
        12 => "UdiExternal",
        DisplayConfigOutputTechnologyUdiEmbedded => "UdiEmbedded",
        15 => "Miracast",
        16 => "IndirectWired",
        17 => "IndirectVirtual",
        18 => "DisplayPortUsbTunnel",
        DisplayConfigOutputTechnologyInternal => "Internal",
        _ => "Unknown",
    };

    private static DisplayConfigDeviceInfoHeader CreateDisplayConfigHeader(
        uint type,
        DisplayConfigLuid adapterId,
        uint id,
        int size) => new()
    {
        Type = type,
        Size = checked((uint)size),
        AdapterId = adapterId,
        Id = id,
    };

    private static DisplayModeEvidence? TryGetDisplayMode(string deviceName, int modeIndex)
    {
        var mode = DevMode.Create();
        return EnumDisplaySettings(deviceName, modeIndex, ref mode)
            ? new DisplayModeEvidence(
                checked((int)mode.PelsWidth),
                checked((int)mode.PelsHeight),
                checked((int)mode.BitsPerPel),
                checked((int)mode.DisplayFrequency))
            : null;
    }

    private SectionResult<IReadOnlyList<BrightnessEvidence>> CollectBrightness(
        CancellationToken cancellationToken)
    {
        var query = QueryWmi(
            @"root\wmi",
            "SELECT InstanceName, CurrentBrightness, Level FROM WmiMonitorBrightness WHERE Active=TRUE",
            ["InstanceName", "CurrentBrightness", "Level"],
            cancellationToken);
        var brightness = query.Rows.Take(16).Select((row, index) => new BrightnessEvidence(
            MakeDisplayLabel(WmiString(row, "InstanceName"), index.ToString()),
            WmiByte(row, "CurrentBrightness"),
            WmiBytes(row, "Level").Distinct().Order().Take(101).ToArray())).ToArray();
        return WmiSection(brightness, query, "Windows brightness metadata was unavailable.");
    }

    private SectionResult<IReadOnlyList<ThermalZoneEvidence>> CollectThermalZones(
        CancellationToken cancellationToken)
    {
        var query = QueryWmi(
            @"root\wmi",
            "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature",
            ["InstanceName", "CurrentTemperature"],
            cancellationToken);
        var zones = query.Rows.Take(16).Select((row, index) => new ThermalZoneEvidence(
            MakeOpaqueLabel("thermal", WmiString(row, "InstanceName") ?? index.ToString()),
            WmiUInt32(row, "CurrentTemperature"))).ToArray();
        return WmiSection(zones, query, "Windows thermal-zone metadata was unavailable.");
    }

    private static SectionResult<IReadOnlyList<T>> WmiSection<T>(
        IReadOnlyList<T> values,
        WmiQueryResult query,
        string unavailableIssue)
    {
        if (!query.Available)
        {
            return SectionResult<IReadOnlyList<T>>.Unavailable(values, unavailableIssue);
        }

        return values.Count == 0
            ? SectionResult<IReadOnlyList<T>>.Absent(values)
            : SectionResult<IReadOnlyList<T>>.Collected(values);
    }

    private static WmiQueryResult QueryWmi(
        string scope,
        string query,
        IReadOnlyList<string> properties,
        CancellationToken cancellationToken)
    {
        object? locator = null;
        object? services = null;
        object? rows = null;
        var result = new List<IReadOnlyDictionary<string, object?>>();
        try
        {
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null)
            {
                return new WmiQueryResult(false, result);
            }

            locator = Activator.CreateInstance(locatorType);
            services = locatorType.InvokeMember(
                "ConnectServer",
                BindingFlags.InvokeMethod,
                null,
                locator,
                [".", scope]);
            rows = services?.GetType().InvokeMember(
                "ExecQuery",
                BindingFlags.InvokeMethod,
                null,
                services,
                [query]);
            if (rows is not IEnumerable enumerable)
            {
                return new WmiQueryResult(false, result);
            }

            foreach (var row in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (row is null)
                {
                    continue;
                }

                try
                {
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in properties)
                    {
                        try
                        {
                            values[property] = row.GetType().InvokeMember(
                                property,
                                BindingFlags.GetProperty,
                                null,
                                row,
                                null);
                        }
                        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
                        {
                            values[property] = null;
                        }
                    }

                    result.Add(values);
                }
                finally
                {
                    ReleaseComObject(row);
                }
            }

            return new WmiQueryResult(true, result);
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return new WmiQueryResult(false, result);
        }
        finally
        {
            ReleaseComObject(rows);
            ReleaseComObject(services);
            ReleaseComObject(locator);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }

    private static string? WmiString(IReadOnlyDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var value) ? value?.ToString() : null;

    private static byte? WmiByte(IReadOnlyDictionary<string, object?> row, string name)
    {
        if (!row.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static uint? WmiUInt32(IReadOnlyDictionary<string, object?> row, string name)
    {
        if (!row.TryGetValue(name, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    private static IEnumerable<byte> WmiBytes(IReadOnlyDictionary<string, object?> row, string name)
    {
        if (!row.TryGetValue(name, out var value) || value is not IEnumerable values)
        {
            yield break;
        }

        foreach (var item in values)
        {
            if (item is not null)
            {
                byte converted;
                try
                {
                    converted = Convert.ToByte(item, System.Globalization.CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                    continue;
                }

                yield return converted;
            }
        }
    }

    private string MakeDisplayLabel(string? deviceIdentity, string fallbackIdentity) =>
        MakeOpaqueLabel("display", GetDisplayModelKey(deviceIdentity) ?? fallbackIdentity);

    internal static string? GetDisplayModelKey(string? deviceIdentity)
    {
        if (string.IsNullOrWhiteSpace(deviceIdentity))
        {
            return null;
        }

        var parts = deviceIdentity.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[1].Length is > 0 and <= 32
            && parts[1].All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
                ? parts[1].ToUpperInvariant()
                : null;
    }

    private static string? RegistryText(RegistryKey? key, string name) =>
        CleanText(key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString());

    private static IReadOnlyList<string> RegistryStrings(RegistryKey key, string name)
    {
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value switch
        {
            string[] values => values.Select(CleanText).OfType<string>().Take(16).ToArray(),
            string text => text.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanText)
                .OfType<string>()
                .Take(16)
                .ToArray(),
            _ => [],
        };
    }

    private static FileSignatureState GetSignatureState(string path)
    {
        var fileInfo = new WinTrustFileInfo(path);
        var data = new WinTrustData(fileInfo);
        var policy = WinTrustActionGenericVerifyV2;
        var trustCallCompleted = false;
        try
        {
            var result = WinVerifyTrust(new IntPtr(-1), ref policy, data);
            trustCallCompleted = true;
            return result switch
            {
                0 => FileSignatureState.Trusted,
                TrustENoSignature or TrustESubjectFormUnknown => FileSignatureState.Unsigned,
                _ => FileSignatureState.Invalid,
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return FileSignatureState.Unavailable;
        }
        finally
        {
            if (trustCallCompleted)
            {
                data.StateAction = WinTrustStateActionClose;
                _ = WinVerifyTrust(new IntPtr(-1), ref policy, data);
            }

            data.Dispose();
            fileInfo.Dispose();
        }
    }

    private const int EnumCurrentSettings = -1;
    private const int DisplayDeviceActive = 0x1;
    private const int DisplayDeviceMirroringDriver = 0x8;
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const uint QdcOnlyActivePaths = 0x00000002;
    private const uint QdcVirtualModeAware = 0x00000010;
    private const uint QdcVirtualRefreshRateAware = 0x00000040;
    private const uint DisplayConfigPathActive = 0x00000001;
    private const uint DisplayConfigPathBoostRefreshRate = 0x00000010;
    private const uint DisplayConfigOutputTechnologyLvds = 6;
    private const uint DisplayConfigOutputTechnologyDisplayPortEmbedded = 11;
    private const uint DisplayConfigOutputTechnologyUdiEmbedded = 13;
    private const uint DisplayConfigOutputTechnologyInternal = 0x80000000;
    private const uint DisplayConfigDeviceInfoGetSourceName = 1;
    private const uint DisplayConfigDeviceInfoGetTargetName = 2;
    private const uint DisplayConfigDeviceInfoGetAdvancedColorInfo2 = 15;
    private const uint HdrSupportedMask = 1u << 4;
    private const uint HdrUserEnabledMask = 1u << 5;
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const uint WinTrustStateActionClose = 2;
    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    private sealed record WmiQueryResult(
        bool Available,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);

    private sealed record DisplayCollectionResult(
        SectionResult<IReadOnlyList<DisplayEvidence>> Section,
        IReadOnlyList<SupportCoverageGap> Gaps);

    internal sealed record DisplayModeEnumerationResult(
        IReadOnlyList<DisplayModeEvidence> Modes,
        bool Truncated);

    private sealed record DisplayConfigurationResult(
        bool Available,
        IReadOnlyDictionary<string, DisplayTargetMetadata> Targets,
        IReadOnlyList<SupportCoverageGap> Gaps);

    private sealed record DisplayTargetMetadata(
        string? FriendlyName,
        bool IsInternal,
        string ConnectionKind,
        bool? HdrSupported,
        bool? HdrEnabled,
        int? BitsPerColorChannel,
        uint? ColorEncoding,
        uint? ActiveColorMode,
        bool DynamicRefreshRateActive,
        double? ActiveRefreshRateHertz,
        string? EdidManufacturerCode,
        string? EdidProductCode,
        DisplayColorProfileInventoryEvidence ColorProfiles);

    private sealed record LampArrayReadResult(LampArrayEvidence Evidence, bool Truncated);

    private sealed record LampArrayEnumerationResult(
        IReadOnlyList<DeviceInformation> Devices,
        bool Truncated);

    internal readonly record struct PciIdentity(
        ushort? VendorId,
        ushort? DeviceId,
        ushort? SubsystemVendorId,
        ushort? SubsystemId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;

        public static DisplayDevice Create() => new() { Size = Marshal.SizeOf<DisplayDevice>() };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint ICMMethod;
        public uint ICMIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;

        public static DevMode Create() => new() { Size = checked((ushort)Marshal.SizeOf<DevMode>()) };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigLuid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigPathSourceInfo
    {
        public DisplayConfigLuid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigPathTargetInfo
    {
        public DisplayConfigLuid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public DisplayConfigRational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfig2DRegion
    {
        public uint Width;
        public uint Height;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigVideoSignalInfo
    {
        public ulong PixelRate;
        public DisplayConfigRational HSyncFreq;
        public DisplayConfigRational VSyncFreq;
        public DisplayConfig2DRegion ActiveSize;
        public DisplayConfig2DRegion TotalSize;
        public uint VideoStandard;
        public uint ScanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigTargetMode
    {
        public DisplayConfigVideoSignalInfo TargetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigSourceMode
    {
        public uint Width;
        public uint Height;
        public uint PixelFormat;
        public DisplayConfigPoint Position;
    }

    [StructLayout(LayoutKind.Explicit, Size = 48, Pack = 4)]
    private struct DisplayConfigModeUnion
    {
        [FieldOffset(0)] public DisplayConfigTargetMode TargetMode;
        [FieldOffset(0)] public DisplayConfigSourceMode SourceMode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
        public uint Id;
        public DisplayConfigLuid AdapterId;
        public DisplayConfigModeUnion Mode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public DisplayConfigLuid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 4)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct DisplayConfigGetAdvancedColorInfo2
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Value;
        public uint ColorEncoding;
        public uint BitsPerColorChannel;
        public uint ActiveColorMode;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        private readonly uint structSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
        private IntPtr filePath = Marshal.StringToCoTaskMemUni(string.Empty);
        private readonly IntPtr fileHandle = IntPtr.Zero;
        private readonly IntPtr knownSubject = IntPtr.Zero;

        public WinTrustFileInfo(string path)
        {
            Marshal.FreeCoTaskMem(filePath);
            filePath = Marshal.StringToCoTaskMemUni(path);
        }

        public void Dispose()
        {
            if (filePath != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(filePath);
                filePath = IntPtr.Zero;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData : IDisposable
    {
        private readonly uint structSize = (uint)Marshal.SizeOf<WinTrustData>();
        private readonly IntPtr policyCallbackData = IntPtr.Zero;
        private readonly IntPtr sipClientData = IntPtr.Zero;
        private readonly uint uiChoice = 2;
        private readonly uint revocationChecks = 0;
        private readonly uint unionChoice = 1;
        private IntPtr fileInfo;
        public uint StateAction = 1;
        private IntPtr StateData = IntPtr.Zero;
        private readonly string? urlReference = null;
        private readonly uint providerFlags = 0x1000;
        private readonly uint uiContext = 0;

        public WinTrustData(WinTrustFileInfo info)
        {
            fileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(info, fileInfo, false);
        }

        public void Dispose()
        {
            if (fileInfo != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfo);
                fileInfo = IntPtr.Zero;
            }
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(
        string? device,
        uint deviceNumber,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DevMode devMode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [In, Out] DisplayConfigPathInfo[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [In, Out] DisplayConfigModeInfo[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetSourceName(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetTargetName(ref DisplayConfigTargetDeviceName requestPacket);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetAdvancedColorInfo2Native(ref DisplayConfigGetAdvancedColorInfo2 requestPacket);

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid policy, [In, Out] WinTrustData data);
}
