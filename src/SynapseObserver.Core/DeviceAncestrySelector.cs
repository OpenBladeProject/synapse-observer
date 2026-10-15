namespace SynapseObserver.Core;

internal static class DeviceAncestrySelector
{
    internal static int FindNearestPhysicalUsbVid(
        IReadOnlyList<string?> ancestorInstanceIds,
        ushort vendorId,
        ushort productId)
    {
        var expectedVid = $"VID_{vendorId:X4}";
        var expectedPid = $"PID_{productId:X4}";
        for (var index = 0; index < ancestorInstanceIds.Count; index++)
        {
            var instanceId = ancestorInstanceIds[index];
            if (string.IsNullOrWhiteSpace(instanceId)
                || !instanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var identityEnd = instanceId.IndexOf('\\', 4);
            var hardwareIdentity = identityEnd < 0 ? instanceId[4..] : instanceId[4..identityEnd];
            var identityParts = hardwareIdentity.Split('&', StringSplitOptions.RemoveEmptyEntries);
            if (!hardwareIdentity.Contains("&MI_", StringComparison.OrdinalIgnoreCase)
                && identityParts.Contains(expectedVid, StringComparer.OrdinalIgnoreCase)
                && identityParts.Contains(expectedPid, StringComparer.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}
