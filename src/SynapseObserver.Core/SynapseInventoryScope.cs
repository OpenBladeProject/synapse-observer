namespace SynapseObserver.Core;

internal static class SynapseInventoryScope
{
    private static readonly HashSet<string> KnownProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "RazerAppEngine",
        "Razer Synapse",
        "Razer Synapse 3",
        "Razer Synapse Service",
        "RzSynapse",
    };

    public static bool IsProcessName(string processName) => KnownProcessNames.Contains(processName);

    public static bool IsModule(string modulePath, string? mainModulePath)
    {
        if (!string.IsNullOrWhiteSpace(mainModulePath)
            && string.Equals(modulePath, mainModulePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var fileName = Path.GetFileName(modulePath);
        if (fileName.Equals("node-rz-hid.node", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (fileName.Equals("HID.node", StringComparison.OrdinalIgnoreCase)
            && modulePath
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment.Equals("node-rz-hid", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName);
        return (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".node", StringComparison.OrdinalIgnoreCase))
            && (fileName.StartsWith("Razer", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Rz", StringComparison.OrdinalIgnoreCase));
    }
}
