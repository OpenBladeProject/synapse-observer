using SynapseObserver.App;

namespace SynapseObserver.App.Tests;

public sealed class SettingEntryTests
{
    [Theory]
    [InlineData("Windows/Copilot disabled")]
    [InlineData("Windows\\Copilot disabled")]
    public void SlashCharactersCannotEnableMarkerAction(string details)
    {
        var entry = Entry("On", details);

        Assert.False(entry.HasValue);
        Assert.Contains("slash", entry.ValidationHint, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"On: {details}", entry.Value);
    }

    [Theory]
    [InlineData("Windows key disabled\nAlt+Tab allowed")]
    [InlineData("Windows key disabled\tAlt+Tab allowed")]
    public void ControlCharactersCannotEnableMarkerAction(string details)
    {
        var entry = Entry("On", details);

        Assert.False(entry.HasValue);
        Assert.Contains("control characters", entry.ValidationHint, StringComparison.OrdinalIgnoreCase);
        Assert.Equal($"On: {details}", entry.Value);
    }

    [Fact]
    public void PunctuationWithoutPrivacySeparatorsRemainsValid()
    {
        const string details = "Windows and Copilot disabled; Alt+Tab and Alt+F4 allowed; in-game-only Off";
        var entry = Entry("On", details);

        Assert.True(entry.HasValue);
        Assert.Equal(string.Empty, entry.ValidationHint);
        Assert.Equal($"On: {details}", entry.Value);
    }

    private static SettingEntry Entry(string choice, string details)
    {
        var entry = new SettingEntry();
        entry.Reset(17);
        entry.SelectedChoice = entry.Choices.Single(item => item.Label == choice);
        entry.Details = details;
        return entry;
    }
}
