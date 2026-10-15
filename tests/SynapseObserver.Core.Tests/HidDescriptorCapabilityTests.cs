using System.Runtime.InteropServices;

namespace SynapseObserver.Core.Tests;

public sealed class HidDescriptorCapabilityTests
{
    [Fact]
    public void NativeCapabilityLayouts_MatchWindowsSdkHidpiHeader()
    {
        Assert.Equal(72, Marshal.SizeOf<WindowsInventorySource.HidpButtonCaps>());
        Assert.Equal(72, Marshal.SizeOf<WindowsInventorySource.HidpValueCaps>());
        Assert.Equal(56, Marshal.OffsetOf<WindowsInventorySource.HidpButtonCaps>(nameof(WindowsInventorySource.HidpButtonCaps.UsageData)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<WindowsInventorySource.HidpValueCaps>(nameof(WindowsInventorySource.HidpValueCaps.LogicalMinimum)).ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<WindowsInventorySource.HidpValueCaps>(nameof(WindowsInventorySource.HidpValueCaps.UsageData)).ToInt32());
    }

    [Fact]
    public void CollectHidCapabilityShapes_ParsesRangeAndNonRangeAcrossDirections()
    {
        var caps = new WindowsInventorySource.HidpCaps
        {
            NumberInputButtonCaps = 1,
            NumberOutputValueCaps = 1,
            NumberFeatureValueCaps = 1,
        };

        var result = WindowsInventorySource.CollectHidCapabilityShapes(
            IntPtr.Zero,
            caps,
            ReadButtons,
            ReadValues);

        Assert.Equal(CollectionState.Collected, result.State);
        Assert.False(result.Truncated);
        Assert.Equal(3, result.Shapes.Count);

        var input = result.Shapes[0];
        Assert.Equal(HidReportDirection.Input, input.Direction);
        Assert.Equal(HidReportCapabilityKind.Button, input.Kind);
        Assert.Equal((byte)2, input.ReportId);
        Assert.Equal((ushort)0x09, input.UsagePage);
        Assert.True(input.IsRange);
        Assert.Equal((ushort)1, input.UsageMinimum);
        Assert.Equal((ushort)8, input.UsageMaximum);
        Assert.Equal((ushort)3, input.LinkCollection);
        Assert.Equal((ushort)0x01, input.LinkUsagePage);
        Assert.Equal((ushort)0x06, input.LinkUsage);
        Assert.Null(input.BitSize);
        Assert.Equal((ushort)8, input.ReportCount);

        var output = result.Shapes[1];
        Assert.Equal(HidReportDirection.Output, output.Direction);
        Assert.Equal(HidReportCapabilityKind.Value, output.Kind);
        Assert.False(output.IsRange);
        Assert.Equal((ushort)0x44, output.UsageMinimum);
        Assert.Equal((ushort)0x44, output.UsageMaximum);
        Assert.Equal((ushort)8, output.BitSize);
        Assert.Equal(-10, output.LogicalMinimum);
        Assert.Equal(100, output.LogicalMaximum);
        Assert.True(output.HasNull);

        var feature = result.Shapes[2];
        Assert.Equal(HidReportDirection.Feature, feature.Direction);
        Assert.True(feature.IsRange);
        Assert.Equal((ushort)0x20, feature.UsageMinimum);
        Assert.Equal((ushort)0x22, feature.UsageMaximum);

        static int ReadButtons(
            WindowsInventorySource.HidpReportType reportType,
            WindowsInventorySource.HidpButtonCaps[] nativeCaps,
            ref ushort length,
            IntPtr preparsedData)
        {
            Assert.Equal(WindowsInventorySource.HidpReportType.Input, reportType);
            nativeCaps[0] = new WindowsInventorySource.HidpButtonCaps
            {
                UsagePage = 0x09,
                ReportId = 2,
                LinkCollection = 3,
                LinkUsagePage = 0x01,
                LinkUsage = 0x06,
                IsRange = 1,
                IsAbsolute = 1,
                ReportCount = 8,
                UsageData = new WindowsInventorySource.HidpCapsUsageData
                {
                    Range = new WindowsInventorySource.HidpCapsRange
                    {
                        UsageMin = 1,
                        UsageMax = 8,
                    },
                },
            };
            length = 1;
            return 0;
        }

        static int ReadValues(
            WindowsInventorySource.HidpReportType reportType,
            WindowsInventorySource.HidpValueCaps[] nativeCaps,
            ref ushort length,
            IntPtr preparsedData)
        {
            nativeCaps[0] = reportType == WindowsInventorySource.HidpReportType.Output
                ? new WindowsInventorySource.HidpValueCaps
                {
                    UsagePage = 0xff00,
                    ReportId = 4,
                    LinkCollection = 5,
                    LinkUsagePage = 0xff00,
                    LinkUsage = 0x10,
                    BitSize = 8,
                    ReportCount = 1,
                    LogicalMinimum = -10,
                    LogicalMaximum = 100,
                    HasNull = 1,
                    UsageData = new WindowsInventorySource.HidpCapsUsageData
                    {
                        NotRange = new WindowsInventorySource.HidpCapsNotRange { Usage = 0x44 },
                    },
                }
                : new WindowsInventorySource.HidpValueCaps
                {
                    UsagePage = 0xff00,
                    ReportId = 5,
                    IsRange = 1,
                    BitSize = 16,
                    ReportCount = 3,
                    LogicalMinimum = 0,
                    LogicalMaximum = 1023,
                    UsageData = new WindowsInventorySource.HidpCapsUsageData
                    {
                        Range = new WindowsInventorySource.HidpCapsRange
                        {
                            UsageMin = 0x20,
                            UsageMax = 0x22,
                        },
                    },
                };
            length = 1;
            return 0;
        }
    }

    [Fact]
    public void LegacyParser_PreservesButtonShapeWithoutReadingReservedCount()
    {
        var result = WindowsInventorySource.CollectHidCapabilityShapes(
            IntPtr.Zero, new() { NumberInputButtonCaps = 1 }, ReadButtons, NoValues,
            supportsButtonReportCount: false);
        Assert.Equal(CollectionState.Collected, result.State);
        Assert.Null(Assert.Single(result.Shapes).ReportCount);

        static int ReadButtons(WindowsInventorySource.HidpReportType reportType,
            WindowsInventorySource.HidpButtonCaps[] nativeCaps, ref ushort length, IntPtr preparsedData)
        {
            nativeCaps[0] = ValidButton(4);
            nativeCaps[0].ReportCount = 0;
            length = 1;
            return 0;
        }
    }

    [Fact]
    public void CollectHidCapabilityShapes_BoundsBufferTooSmallResults()
    {
        var caps = new WindowsInventorySource.HidpCaps
        {
            NumberInputButtonCaps = ushort.MaxValue,
        };

        var result = WindowsInventorySource.CollectHidCapabilityShapes(
            IntPtr.Zero,
            caps,
            ReadButtons,
            NoValues);

        Assert.Equal(CollectionState.Partial, result.State);
        Assert.True(result.Truncated);
        Assert.Empty(result.Shapes);

        static int ReadButtons(
            WindowsInventorySource.HidpReportType reportType,
            WindowsInventorySource.HidpButtonCaps[] nativeCaps,
            ref ushort length,
            IntPtr preparsedData)
        {
            for (var index = 0; index < nativeCaps.Length; index++)
            {
                nativeCaps[index] = ValidButton((ushort)index);
            }

            length = ushort.MaxValue;
            return unchecked((int)0xC0110007);
        }
    }

    [Fact]
    public void CollectHidCapabilityShapes_RejectsMalformedCapsAndNativeCounts()
    {
        var caps = new WindowsInventorySource.HidpCaps
        {
            NumberInputButtonCaps = 1,
            NumberFeatureValueCaps = 2,
        };

        var result = WindowsInventorySource.CollectHidCapabilityShapes(
            IntPtr.Zero,
            caps,
            ReadButtons,
            ReadValues);

        Assert.Equal(CollectionState.Partial, result.State);
        Assert.True(result.Truncated);
        Assert.Single(result.Shapes);

        static int ReadButtons(
            WindowsInventorySource.HidpReportType reportType,
            WindowsInventorySource.HidpButtonCaps[] nativeCaps,
            ref ushort length,
            IntPtr preparsedData)
        {
            nativeCaps[0] = ValidButton(1);
            length = ushort.MaxValue;
            return 0;
        }

        static int ReadValues(
            WindowsInventorySource.HidpReportType reportType,
            WindowsInventorySource.HidpValueCaps[] nativeCaps,
            ref ushort length,
            IntPtr preparsedData)
        {
            nativeCaps[0] = new WindowsInventorySource.HidpValueCaps
            {
                IsRange = 1,
                BitSize = 8,
                ReportCount = 1,
                LogicalMinimum = 0,
                LogicalMaximum = 1,
                UsageData = new WindowsInventorySource.HidpCapsUsageData
                {
                    Range = new WindowsInventorySource.HidpCapsRange { UsageMin = 9, UsageMax = 2 },
                },
            };
            nativeCaps[1] = new WindowsInventorySource.HidpValueCaps
            {
                BitSize = 0,
                ReportCount = 1,
                LogicalMinimum = 0,
                LogicalMaximum = 1,
            };
            length = 2;
            return 0;
        }
    }

    [Fact]
    public void Sanitize_BoundsAndValidatesHidCapabilityShapes()
    {
        var validShapes = Enumerable.Range(0, EvidenceLimits.MaxHidCapabilityShapesPerInterface + 2)
            .Select(static index => new HidReportCapabilityEvidence(
                HidReportDirection.Input,
                HidReportCapabilityKind.Button,
                (byte)(index % 256),
                0x09,
                false,
                (ushort)(index % 16),
                (ushort)(index % 16),
                0,
                0,
                0,
                64,
                1,
                -1,
                1)
            {
                HasNull = true,
            })
            .ToList();
        validShapes.Insert(0, validShapes[0] with
        {
            Direction = (HidReportDirection)99,
            ReportCount = 0,
        });
        var descriptor = new HidDescriptorSupportEvidence(
            "hid-safe",
            "device-safe",
            0x1532,
            0x02e0,
            0x01,
            0x02,
            64,
            90,
            91,
            1,
            0,
            0,
            0,
            0,
            0)
        {
            CapabilityShapes = validShapes,
            CapabilityShapeState = CollectionState.Collected,
        };

        var sanitized = SupportInventorySanitizer.Sanitize(SupportInventorySnapshot.Empty with
        {
            HidDescriptors = SectionResult<IReadOnlyList<HidDescriptorSupportEvidence>>.Collected([descriptor]),
        });

        var result = Assert.Single(sanitized.HidDescriptors.Value);
        Assert.Equal(EvidenceLimits.MaxHidCapabilityShapesPerInterface, result.CapabilityShapes.Count);
        Assert.Equal(CollectionState.Partial, result.CapabilityShapeState);
        Assert.True(result.CapabilityShapesTruncated);
        Assert.All(result.CapabilityShapes, static shape =>
        {
            Assert.True(Enum.IsDefined(shape.Direction));
            Assert.Null(shape.BitSize);
            Assert.Null(shape.LogicalMinimum);
            Assert.Null(shape.LogicalMaximum);
            Assert.Null(shape.HasNull);
        });
    }

    private static WindowsInventorySource.HidpButtonCaps ValidButton(ushort usage) => new()
    {
        UsagePage = 0x09,
        ReportCount = 1,
        UsageData = new WindowsInventorySource.HidpCapsUsageData
        {
            NotRange = new WindowsInventorySource.HidpCapsNotRange { Usage = usage },
        },
    };

    private static int NoValues(
        WindowsInventorySource.HidpReportType reportType,
        WindowsInventorySource.HidpValueCaps[] nativeCaps,
        ref ushort length,
        IntPtr preparsedData) => throw new InvalidOperationException("No value caps were declared.");
}
