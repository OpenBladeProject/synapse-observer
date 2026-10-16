using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SynapseObserver.Core;

public static class EvidenceBundleWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // Contribution schema labels intentionally retain their documented names
    // (for example UserModeFeatureApi and NotInvestigated). Other manifest
    // enums remain camel-case for bundle compatibility.
    private static readonly JsonSerializerOptions ContributionJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task WriteAsync(
        EvidenceSession session,
        string destinationPath,
        CancellationToken cancellationToken = default,
        bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        // Freeze before returning to the caller; no worker reads the mutable session.
        using var snapshot = session.Snapshot();
        await WriteSnapshotAsync(snapshot, destinationPath, cancellationToken, overwrite).ConfigureAwait(false);
    }

    internal static async Task WriteSnapshotAsync(
        EvidenceSessionSnapshot snapshot,
        string destinationPath,
        CancellationToken cancellationToken = default,
        bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullDestination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException("The selected export directory does not exist.");
        }

        using var projections = new ExportProjectionScope(cancellationToken, destinationDirectory);
        var drafts = await Task.Run(() => ContributionDraftBuilder.CreateDrafts(snapshot, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var manifest = CreateManifest(snapshot);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(fullDestination)}.{Guid.NewGuid():N}.tmp");
        var temporaryCreated = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous))
            {
                temporaryCreated = true;
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
                {
                    await WriteJsonEntryAsync(archive, "manifest.json", manifest, JsonOptions, cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "coverage-draft.json", drafts.Coverage, ContributionJsonOptions, cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "annotation-draft.json", drafts.Annotation, ContributionJsonOptions, cancellationToken).ConfigureAwait(false);
                    await WriteJsonEntryAsync(archive, "quality-receipt.json", drafts.Quality, ContributionJsonOptions, cancellationToken).ConfigureAwait(false);
                    await WriteEntryAsync(archive, "README.txt", Encoding.UTF8.GetBytes(CreateReadme(snapshot, drafts.Quality, drafts.Statistics)), cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            // Scratch cleanup can fail; complete it before publishing success.
            projections.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullDestination, overwrite: overwrite);
            temporaryCreated = false;
        }
        finally
        {
            if (temporaryCreated)
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static EvidenceBundleManifest CreateManifest(EvidenceSessionSnapshot snapshot) =>
        new EvidenceBundleManifest(
            "synapse-observer.bundle.v2",
            DateTimeOffset.UtcNow,
            GetCollectorBuild(),
            FeaturePayloadPrivacy.ReviewedSourceUrls,
            "No upload was performed. Restoration markers are user-reported, and observations are user-mode call evidence rather than firmware readback.",
            [
                "manifest.json",
                "coverage-draft.json",
                "annotation-draft.json",
                "quality-receipt.json",
                "README.txt",
            ],
            snapshot);

    private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string name,
        T content, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, content, options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await entryStream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    private static string CreateReadme(EvidenceSessionSnapshot snapshot, ContributionQualityReceipt quality, ObservationQualityStatistics statistics)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Synapse Observer evidence bundle");
        builder.AppendLine("================================");
        builder.AppendLine();
        builder.AppendLine("This bundle was saved locally. The collector did not upload it.");
        var review = EvidenceCollectionReview.Create(snapshot, quality, statistics);
        builder.AppendLine();
        builder.AppendLine(review.Summary);
        foreach (var note in review.Notes) builder.AppendLine("- " + note);
        builder.AppendLine();
        builder.AppendLine("It contains Windows identity fields, Razer HID descriptor metadata, bounded Synapse module metadata, user action markers, prompted special-key observations, coverage status, and privacy-reviewed control observations.");
        builder.AppendLine("It does not contain HID interface paths, PnP instance paths, process IDs, command lines, account data, or module paths.");
        builder.AppendLine("Payload hex is included only for a matching selected Razer HID feature call whose complete envelope, checksum, padding, command, and typed values match a reviewed numeric control or lighting grammar. Unknown, sensitive, malformed, failed, and over-limit payloads keep bounded header and shape metadata while their bytes are omitted.");
        builder.AppendLine("This privacy classification does not admit a device or command, prove physical transport, or prove firmware application. See payloadPrivacySources in manifest.json for the reviewed grammar sources.");
        builder.AppendLine("A user-reported restoration marker records what the user selected; it is not independent hardware readback.");
        builder.AppendLine("coverage-draft.json lists every OpenBlade support-evidence leaf. Captured means only that this bundle contains passive evidence for that leaf; every other leaf remains NotInvestigated.");
        builder.AppendLine("annotation-draft.json groups bounded evidence into prompted action windows. quality-receipt.json keeps collection gaps and the remaining maintainer validation stages visible.");
        builder.AppendLine("These drafts are not PCAP files or capture-toolkit annotations, and they do not claim capture-validator compliance, query validation, setter validation, or production admission.");
        builder.AppendLine();
        builder.AppendLine($"Session started (UTC): {snapshot.StartedAtUtc:O}");
        builder.AppendLine($"Collector build: {GetCollectorBuild().InformationalVersion}");
        builder.AppendLine($"Razer HID inventory: {snapshot.Inventory.RazerHidInterfaces.State}");
        builder.AppendLine($"Synapse process inventory: {snapshot.Inventory.SynapseProcesses.State}");
        builder.AppendLine($"Markers: {snapshot.Markers.Count}");
        builder.AppendLine($"Control observations: {snapshot.Observations.Count}");
        builder.AppendLine($"Special-key observations: {snapshot.SpecialKeyObservations.Count}");
        builder.AppendLine($"Coverage items: {snapshot.Coverage.Count}");
        builder.AppendLine($"Rejected input events: {snapshot.RejectedMarkerCount + snapshot.RejectedObservationCount + snapshot.RejectedCoverageCount}");
        builder.AppendLine();
        builder.AppendLine("Open manifest.json for the schema-defined evidence. A pending, skipped, unavailable, failed, or cancelled item is not evidence that the laptop lacks that capability.");
        return builder.ToString();
    }

    private static CollectorBuildEvidence GetCollectorBuild()
    {
        var assembly = typeof(EvidenceBundleWriter).Assembly;
        var assemblyName = assembly.GetName();
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return new CollectorBuildEvidence(
            assemblyName.Name ?? "SynapseObserver.Core",
            string.IsNullOrWhiteSpace(informationalVersion) ? "unavailable" : informationalVersion,
            assemblyName.Version?.ToString() ?? "unavailable");
    }

    private sealed record EvidenceBundleManifest(
        string SchemaVersion,
        DateTimeOffset ExportedAtUtc,
        CollectorBuildEvidence CollectorBuild,
        IReadOnlyList<string> PayloadPrivacySources,
        string EvidenceNotice,
        IReadOnlyList<string> Entries,
        EvidenceSessionSnapshot Session);
}
