using System.Runtime.InteropServices;
using SynapseObserver.App;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class SpecialKeyRegistrationTests
{
    private static LocalHidCandidate Candidate(ushort page, ushort usage) =>
        new($"owned-{page}-{usage}", "hid-owned", "Razer Blade fixture", 0x1532, 0xFFFF, null,
            page, usage, new(16, 0, 0), CollectionState.Collected, null,
            null, null, null, "owned-root", "owned-hardware", "Razer Blade fixture");

    [Fact]
    public void UndefinedDesktopUsesOneForegroundPageRegistrationAndExactRemoval()
    {
        var active = SpecialKeyProbe.CreateRegistrations(
            [Candidate(1, 0), Candidate(1, 6), Candidate(1, 2), Candidate(12, 1), Candidate(0xFF00, 1)], 123);
        Assert.Equal(3, active.Length);
        var desktop = Assert.Single(active, r => r.UsagePage == 1);
        Assert.Equal((ushort)0, desktop.Usage);
        Assert.Equal(0x20u, desktop.Flags);
        Assert.All(active, r => Assert.Equal((nint)123, r.Target));
        Assert.All(active.Where(r => r.UsagePage != 1), r => Assert.Equal(0u, r.Flags));
        var removals = SpecialKeyProbe.CreateRemovalRegistrations(active);
        Assert.Equal(active.Length, removals.Length);
        for (int i = 0; i < active.Length; i++)
        {
            Assert.Equal(active[i].UsagePage, removals[i].UsagePage);
            Assert.Equal(active[i].Usage, removals[i].Usage);
            Assert.Equal(active[i].Flags | 1u, removals[i].Flags);
            Assert.Equal((nint)0, removals[i].Target);
        }
        // A failed removal can retry against the untouched registration plan.
        Assert.Equal(0x20u, active[0].Flags);
        Assert.Equal((nint)123, active[0].Target);
    }

    [Fact]
    public void ExistingKeyboardAndConsumerRegistrationsStaySpecificWithoutUndefinedCollection()
    {
        var active = SpecialKeyProbe.CreateRegistrations([Candidate(1, 6), Candidate(12, 1), Candidate(1, 2)], 123);
        Assert.Equal(2, active.Length);
        Assert.Contains(active, r => r.UsagePage == 1 && r.Usage == 6 && r.Flags == 0);
        Assert.Contains(active, r => r.UsagePage == 12 && r.Usage == 1 && r.Flags == 0);
        Assert.Empty(SpecialKeyProbe.CreateRegistrations([Candidate(1, 2)], 123));
    }

    [Fact]
    public void WindowsAcceptsPageRegistrationAndRemovesItFromOwnedMessageWindow()
    {
        // Exercise the actual Windows registration contract without foreground,
        // a message pump, input reads, synthetic keys, or hardware handles.
        Assert.DoesNotContain(RegisteredDevices(), r => r.UsagePage == 1);
        nint window = CreateWindowEx(0, "STATIC", "Owned registration fixture", 0,
            0, 0, 0, 0, new nint(-3), 0, 0, 0);
        Assert.NotEqual((nint)0, window);
        var active = SpecialKeyProbe.CreateRegistrations([Candidate(1, 0), Candidate(1, 6)], window);
        var removals = SpecialKeyProbe.CreateRemovalRegistrations(active);
        bool registered = false;
        try
        {
            registered = RegisterRawInputDevices(active, (uint)active.Length, DeviceSize);
            Assert.True(registered, $"Registration error {Marshal.GetLastWin32Error()}");
            Assert.Contains(RegisteredDevices(), r => r.UsagePage == 1 && r.Usage == 0 && r.Target == window);
            Assert.True(RegisterRawInputDevices(removals, (uint)removals.Length, DeviceSize),
                $"Removal error {Marshal.GetLastWin32Error()}");
            registered = false;
            Assert.DoesNotContain(RegisteredDevices(), r => r.UsagePage == 1);
        }
        finally
        {
            if (registered) RegisterRawInputDevices(removals, (uint)removals.Length, DeviceSize);
            DestroyWindow(window);
        }
    }

    private static uint DeviceSize => (uint)Marshal.SizeOf<SpecialKeyProbe.RawInputDevice>();
    private static SpecialKeyProbe.RawInputDevice[] RegisteredDevices()
    {
        uint count = 0;
        Assert.NotEqual(uint.MaxValue, GetRegisteredRawInputDevices(null, ref count, DeviceSize));
        Assert.InRange(count, 0u, 256u);
        if (count == 0) return [];
        var devices = new SpecialKeyProbe.RawInputDevice[count];
        uint read = GetRegisteredRawInputDevices(devices, ref count, DeviceSize);
        Assert.NotEqual(uint.MaxValue, read);
        return devices.Take((int)read).ToArray();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateWindowExW")]
    private static extern nint CreateWindowEx(uint styleEx, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(SpecialKeyProbe.RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices([Out] SpecialKeyProbe.RawInputDevice[]? devices, ref uint count, uint size);
}
