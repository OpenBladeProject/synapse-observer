namespace SynapseObserver.Core.Tests;

internal static class FeatureReportTestData
{
    public static byte[] Create(
        byte commandClass,
        byte commandId,
        ReadOnlySpan<byte> payload,
        ObservationDirection direction = ObservationDirection.Input,
        byte reportId = 0,
        byte transactionId = 0x1F)
    {
        var feature = new byte[91];
        feature[0] = reportId;
        var report = feature.AsSpan(1);
        report[0] = direction == ObservationDirection.Output ? (byte)0x02 : (byte)0x00;
        report[1] = transactionId;
        report[5] = checked((byte)payload.Length);
        report[6] = commandClass;
        report[7] = commandId;
        payload.CopyTo(report[8..]);

        byte checksum = 0;
        for (var index = 2; index <= 87; index++)
        {
            checksum ^= report[index];
        }

        report[88] = checksum;
        return feature;
    }
}
