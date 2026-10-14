using SynapseObserver.App;
using SynapseObserver.Core;

namespace SynapseObserver.App.Tests;

public sealed class RawInputDeviceAliasResolverTests
{
    [Fact]
    public void KeyboardAliasMatchesExactPnpInstanceAcrossInterfaceClasses()
    {
        var candidate = Candidate("hid-interface", "hid-01");
        var aliases = RawInputDeviceAliasResolver.MatchKeyboardAliases(
            [candidate],
            [new("hid-interface", "HID\\VID_1532&PID_02E0&MI_00&COL01\\7&exact")],
            [
                new("keyboard-interface", "HID\\VID_1532&PID_02E0&MI_00&COL01\\7&exact"),
                new("unrelated-keyboard", "HID\\VID_1532&PID_02E0&MI_00&COL01\\7&other"),
            ]);

        var match = Assert.Single(aliases);
        Assert.Equal("keyboard-interface", match.Key);
        Assert.Same(candidate, match.Value);
    }

    [Fact]
    public void AmbiguousCandidateIdentityIsRejected()
    {
        var first = Candidate("hid-interface-1", "hid-01");
        var second = Candidate("hid-interface-2", "hid-02");
        var aliases = RawInputDeviceAliasResolver.MatchKeyboardAliases(
            [first, second],
            [
                new("hid-interface-1", "ambiguous-instance"),
                new("hid-interface-2", "ambiguous-instance"),
            ],
            [new("keyboard-interface", "ambiguous-instance")]);

        Assert.Empty(aliases);
    }

    [Fact]
    public void AliasWithConflictingInstanceAssociationsIsRejected()
    {
        var first = Candidate("hid-interface-1", "hid-01");
        var second = Candidate("hid-interface-2", "hid-02");
        var aliases = RawInputDeviceAliasResolver.MatchKeyboardAliases(
            [first, second],
            [
                new("hid-interface-1", "instance-1"),
                new("hid-interface-2", "instance-2"),
            ],
            [
                new("keyboard-interface", "instance-1"),
                new("keyboard-interface", "instance-2"),
            ]);

        Assert.Empty(aliases);
    }

    private static LocalHidCandidate Candidate(string path, string label) =>
        new(path, label, "Razer Blade 16", 0x1532, 0x02E0, null, 1, 6, new(8, 0, 0),
            CollectionState.Collected, null, "keyboard-node", "keyboard-hardware", "keyboard",
            "blade-root", "blade-hardware", "Razer Blade 16 RZ09-0581");
}
