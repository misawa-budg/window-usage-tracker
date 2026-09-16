using WinTracker.Shared.Analytics;
using Xunit;

namespace WinTracker.Viewer.Tests;

public sealed class AppChoiceTests
{
    [Theory]
    [InlineData("Code.exe", "Code")]
    [InlineData("msedge.EXE", "msedge")]
    [InlineData("my.exe.app.exe", "my.exe.app")]
    [InlineData("Other", "Other")]
    [InlineData(".exe", ".exe")]
    [InlineData("", "")]
    public void ShortenedDisplayNameDoesNotChangeTheDatabaseKey(string original, string expected)
    {
        var item = new AppChoice(original);
        Assert.Equal(expected, item.DisplayName);
        Assert.Equal(original, item.ExeName);
    }

    [Fact]
    public void IdenticalDisplayNamesDoNotCollapseDistinctKeys()
    {
        var executable = new AppChoice("editor.exe");
        var other = new AppChoice("editor");
        Assert.Equal(executable.DisplayName, other.DisplayName);
        Assert.NotEqual(executable, other);
    }
}
