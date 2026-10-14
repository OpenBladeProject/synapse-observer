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

    public static async Task WriteAsync(
        EvidenceSession session,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var fullDestination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestination);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException("The selected export directory does not exist.");
        }

        var snapshot = session.Snapshot();
        var manifest = new EvidenceBundleManifest(
            "synapse-observer.bundle.v1",
            DateTimeOffset.UtcNow,
            GetCollectorBuild(),
            FeaturePayloadPrivacy.ReviewedSourceUrls,
            "No upload was performed. Restoration markers are user-reported, and observations are user-mode call evidence rather than firmware readback.",
            snapshot);
        var json = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (json.Length > EvidenceLimits.MaxBundleJsonBytes)
        {
            throw new InvalidOperationException("The evidence manifest exceeds the documented export bound.");
        }

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
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                temporaryCreated = true;
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
                {
                    await WriteEntryAsync(archive, "manifest.json", json, cancellationToken).ConfigureAwait(false);
                    await WriteEntryAsync(
                        archive,
                        "README.txt",
                        Encoding.UTF8.GetBytes(CreateReadme(snapshot)),
                        cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullDestination, overwrite: true);
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

    private static string CreateReadme(EvidenceSessionSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Synapse Observer evidence bundle");
        builder.AppendLine("================================");
        builder.AppendLine();
        builder.AppendLine("This bundle was saved locally. The collector did not upload it.");
        builder.AppendLine("It contains Windows identity fields, Razer HID descriptor metadata, bounded Synapse module metadata, user action markers, coverage status, and explicitly admitted control observations.");
        builder.AppendLine("It does not contain HID interface paths, PnP instance paths, process IDs, command lines, account data, or module paths.");
        builder.AppendLine("Payload hex is included only for a matching selected Razer HID feature call whose complete 91-byte envelope, checksum, padding, command, and typed values match a reviewed thermal, fan, or battery grammar. Unknown, sensitive, malformed, failed, and over-limit payloads keep bounded header and shape metadata while their bytes are omitted.");
        builder.AppendLine("This privacy classification does not admit a device or command, prove physical transport, or prove firmware application. See payloadPrivacySources in manifest.json for the reviewed grammar sources.");
        builder.AppendLine("A user-reported restoration marker records what the user selected; it is not independent hardware readback.");
        builder.AppendLine();
        builder.AppendLine($"Session started (UTC): {snapshot.StartedAtUtc:O}");
        builder.AppendLine($"Collector build: {GetCollectorBuild().InformationalVersion}");
        builder.AppendLine($"Razer HID inventory: {snapshot.Inventory.RazerHidInterfaces.State}");
        builder.AppendLine($"Synapse process inventory: {snapshot.Inventory.SynapseProcesses.State}");
        builder.AppendLine($"Markers: {snapshot.Markers.Count}");
        builder.AppendLine($"Control observations: {snapshot.Observations.Count}");
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
        EvidenceSessionSnapshot Session);
}
