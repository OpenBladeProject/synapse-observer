namespace SynapseObserver.Core;

internal static class SpecialKeyEvidencePolicy
{
    public static string Gap(SpecialKeyProbeEndReason reason) =>
        (reason switch
        {
            SpecialKeyProbeEndReason.Unavailable => "Input collection or registration removal was unavailable. ",
            SpecialKeyProbeEndReason.FocusLost => "The input check stopped when the collector lost focus. ",
            SpecialKeyProbeEndReason.Cancelled => "The input check was cancelled. ",
            SpecialKeyProbeEndReason.LimitReached => "The input check reached its observation limit. ",
            SpecialKeyProbeEndReason.Disposed => "The input check stopped when the collector closed. ",
            _ => "",
        }) +
        "Expected key and mode are user-reported. Ordinary typing and macros are omitted. P is admitted only for Fn+P and does not prove Fn. Unknown input bodies are redacted. No event does not prove absence. This is not firmware readback or confirmed setting restoration.";

    public static bool IsValid(SpecialKeyObservation item, InventorySnapshot inventory)
    {
        if (!double.IsFinite(item.RelativeMilliseconds) || item.RelativeMilliseconds < 0
            || !Enum.IsDefined(item.ExpectedKey) || !Enum.IsDefined(item.ModeContext)
            || !Enum.IsDefined(item.Origin) || !Enum.IsDefined(item.Phase)
            || !EvidenceText.IsToken(item.ProbeLabel) || !EvidenceText.IsToken(item.InterfaceLabel)
            || !inventory.RazerHidInterfaces.Value.Any(h => h.InterfaceLabel == item.InterfaceLabel
                && SpecialKeyInputCollection.IsEligible(h.VendorId, h.DescriptorState,
                    h.UsagePage, h.Usage, h.ReportGeometry))) return false;
        if (item.Origin == SpecialKeyObservationOrigin.WindowsRawInput)
            return item.VirtualKey is ushort key && (key is >= 0x70 and <= 0x87 or 0x21 or 0x22 or >= 0xAD and <= 0xB3 or 0x2C or 0x5F
                || item.ExpectedKey == SpecialKeyExpectedKey.FnP && key == 0x50)
                && item.ScanCode is null or <= 0xFF && item.ReviewedSignal is null && item.ProbeEqualityLabel is null
                && item.ChangedRanges is null && item.ReportLength is null;
        if (item.VirtualKey is not null || item.ScanCode is not null || item.IsExtendedKey is not null
            || item.ReportLength is not (> 0 and <= 1024)) return false;
        if (item.Origin == SpecialKeyObservationOrigin.ReviewedHidSpecialKey)
            return item.ReviewedSignal is "Released" or "Fn" or "Fn+M3" or "Fn+M4" or "Fn+M5" or "M3" or "M5"
                && item.ProbeEqualityLabel is null && item.ChangedRanges is null;
        return item.ReviewedSignal is null && EvidenceText.IsToken(item.ProbeEqualityLabel)
            && item.ChangedByteCount is null or >= 0 and <= 1024
            && item.ChangedRanges is { Count: <= 16 } && item.ChangedRanges.All(range =>
                range.Offset >= 0 && range.Length > 0 && range.Offset <= item.ReportLength - range.Length);
    }
}
