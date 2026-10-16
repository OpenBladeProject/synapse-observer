namespace SynapseObserver.Core;

public static class SpecialKeyInputCollection
{
    // HidP_GetCaps supplies the top-level usage and report size before optional
    // per-report capability parsing. Partial shape details do not invalidate
    // that geometry; absent geometry still fails closed in both App and export.
    public static bool IsEligible(ushort vendorId, CollectionState descriptorState,
        ushort? usagePage, ushort? usage, HidReportGeometry? geometry) =>
        vendorId == 0x1532 && descriptorState is CollectionState.Collected or CollectionState.Partial &&
        geometry?.InputBytes is > 0 and <= 1024 &&
        ((usagePage == 1 && usage is 0 or 6) || (usagePage == 0x0C && usage == 1) ||
         (usagePage is >= 0xFF00 && usage is > 0));
}
