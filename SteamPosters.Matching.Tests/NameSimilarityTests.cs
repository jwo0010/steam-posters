namespace SteamPosters.Matching.Tests;

public class NameSimilarityTests
{
    [Theory]
    [InlineData("Avowed", "Avowed")]
    [InlineData("avowed", "AVOWED")]
    [InlineData("Witcher 3", "The Witcher III")]
    [InlineData("Pokemon", "Pokémon")]
    [InlineData("Ratchet and Clank", "Ratchet & Clank")]
    public void EquivalentNames_ScoreOne(string a, string b)
    {
        Assert.Equal(1.0, NameSimilarity.Score(a, b));
    }

    [Fact]
    public void Acronyms_ExpandAgainstCandidate()
    {
        Assert.True(NameSimilarity.Score("The Legend of Zelda TotK", "The Legend of Zelda: Tears of the Kingdom") >= 0.95);
        Assert.True(NameSimilarity.Score("AC Black Flag", "Assassin's Creed IV Black Flag") >= 0.85);
    }

    [Fact]
    public void CloseNames_ScoreHigherThanUnrelatedOnes()
    {
        var close = NameSimilarity.Score("Assassins Creed Black Flag Resynced", "Assassin's Creed IV Black Flag");
        var other = NameSimilarity.Score("Assassins Creed Black Flag Resynced", "Assassin's Creed Unity");
        var unrelated = NameSimilarity.Score("Starfield", "Stardew Valley");

        Assert.InRange(close, 0.7, 0.95);
        Assert.True(close > other);
        Assert.True(unrelated < 0.5);
    }

    [Fact]
    public void EmptyInput_ScoresZero()
    {
        Assert.Equal(0, NameSimilarity.Score("", "Avowed"));
        Assert.Equal(0, NameSimilarity.Score("!!", "Avowed"));
    }
}
