namespace SynapseObserver.Core;

internal static class InventorySanitizer
{
    public static InventorySnapshot Sanitize(InventorySnapshot snapshot) =>
        new(
            InventorySnapshot.CurrentSchemaVersion,
            snapshot.CollectedAtUtc,
            SanitizeSection(
                snapshot.MachineIdentity,
                snapshot.MachineIdentity.Value
                    .Take(2)
                    .Select(static item => new MachineIdentity(
                        SanitizeToken(item.RegistryView) ?? "unavailable",
                        SanitizeText(item.Manufacturer),
                        SanitizeText(item.ProductName),
                        SanitizeText(item.SystemSku),
                        SanitizeText(item.BiosVersion),
                        SanitizeText(item.BiosReleaseDate)))
                    .ToArray()),
            SanitizeSection(snapshot.PowerSource, snapshot.PowerSource.Value),
            SanitizeSection(
                snapshot.RazerHidInterfaces,
                snapshot.RazerHidInterfaces.Value
                    .Take(EvidenceLimits.MaxHidInterfaces)
                    .Where(static item => EvidenceText.IsToken(item.InterfaceLabel))
                    .Select(static item => item with { Issue = SanitizeIssue(item.DescriptorState, item.Issue) })
                    .ToArray()),
            SanitizeSection(
                snapshot.SynapseProcesses,
                snapshot.SynapseProcesses.Value
                    .Take(EvidenceLimits.MaxSynapseProcesses)
                    .Where(static item => EvidenceText.IsToken(item.ProcessLabel))
                    .Select(static item => new SynapseProcessEvidence(
                        SanitizeText(item.ProcessName) ?? "Synapse process",
                        item.ProcessLabel,
                        SanitizeText(item.ProductVersion),
                        item.Modules
                            .Take(EvidenceLimits.MaxModulesPerProcess)
                            .Select(static module => new SynapseModuleEvidence(
                                SanitizeFileName(module.FileName),
                                SanitizeText(module.FileVersion),
                                IsSha256(module.Sha256) ? module.Sha256!.ToUpperInvariant() : null,
                                module.State,
                                SanitizeIssue(module.State, module.Issue)))
                            .ToArray(),
                        item.ModuleState,
                        SanitizeIssue(item.ModuleState, item.Issue)))
                    .ToArray()));

    private static SectionResult<T> SanitizeSection<T>(SectionResult<T> section, T value) =>
        new(section.State, value, SanitizeIssue(section.State, section.Issue));

    private static string? SanitizeIssue(CollectionState state, string? issue)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            return null;
        }

        return state switch
        {
            CollectionState.Partial => "Some details were unavailable or reached a documented bound.",
            CollectionState.Unavailable => "This item was unavailable.",
            CollectionState.Skipped => "This item was skipped.",
            CollectionState.Cancelled => "Collection was cancelled before this item completed.",
            _ => null,
        };
    }

    private static string SanitizeFileName(string value)
    {
        var segments = value.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return SanitizeText(segments.LastOrDefault()) ?? "module";
    }

    private static string? SanitizeToken(string? value) =>
        EvidenceText.IsToken(value) ? value : null;

    private static string? SanitizeText(string? value)
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

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(static character => char.IsAsciiHexDigit(character));
}
