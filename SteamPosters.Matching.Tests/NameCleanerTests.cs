namespace SteamPosters.Matching.Tests;

public class NameCleanerTests
{
    [Theory]
    [InlineData("ACBlackFlag", "AC Black Flag")]
    [InlineData("Cyberpunk2077.exe", "Cyberpunk 2077")]
    [InlineData("Left4Dead2", "Left 4 Dead 2")]
    [InlineData("Ryujinx.exe - Shortcut", "Ryujinx")]
    [InlineData("Launcher", "")]
    [InlineData("Game-Win64-Shipping.exe", "")]
    [InlineData("MyGame-Win64-Shipping", "My")]
    [InlineData("The_Witcher_3.x64.dx12", "The Witcher 3")]
    [InlineData("Hades II v1.2.3 (x64) [Repack]", "Hades II")]
    [InlineData("hollow knight 1.5.78", "hollow knight")]
    [InlineData("  \"Avowed\"  ", "Avowed")]
    [InlineData("The Legend of Zelda - TotK", "The Legend of Zelda TotK")]
    [InlineData("Some Title [0100ABCD00000000][v0]", "Some Title")]
    [InlineData(null, "")]
    public void Clean(string? raw, string expected)
    {
        Assert.Equal(expected, NameCleaner.Clean(raw));
    }

    [Theory]
    [InlineData("Starfield-AnkerGames", "Starfield")]
    [InlineData("Half-Life", "Half-Life")]
    [InlineData("Starfield-SomeStore", "Starfield-SomeStore")]
    [InlineData("Assassins-Creed-Black-Flag-Resynced", "Assassins-Creed-Black-Flag-Resynced")]
    public void StripDistributorSuffix_IsConservative(string raw, string expected)
    {
        Assert.Equal(expected, NameCleaner.StripDistributorSuffix(raw));
    }
}
