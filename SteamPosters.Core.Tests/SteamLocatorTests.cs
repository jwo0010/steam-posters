using System.Text;
using SteamPosters.Core.Steam;
using SteamPosters.Core.Vdf;

namespace SteamPosters.Core.Tests;

public class SteamLocatorTests
{
    private const string LoginUsers = """
        "users"
        {
        	"76561198036191964"
        	{
        		"AccountName"		"someone"
        		"PersonaName"		"skee \"Wilin\""
        		"RememberPassword"		"1"
        		"MostRecent"		"0"
        		"Timestamp"		"1759700000"
        	}
        	// a comment
        	"76561198065774433"
        	{
        		"AccountName"		"tester"
        		"PersonaName"		"test_toob_baby"
        		"MostRecent"		"0"
        	}
        }
        """;

    [Fact]
    public void TextVdf_ParsesLoginUsers()
    {
        var users = TextVdf.Parse(LoginUsers).GetObject("USERS")!;

        var children = users.Children.ToList();
        Assert.Equal(2, children.Count);
        Assert.Equal("76561198036191964", children[0].Key);
        Assert.Equal("skee \"Wilin\"", children[0].Value.GetString("personaname"));
        Assert.Equal("0", children[1].Value.GetString("MostRecent"));
    }

    [Fact]
    public void TextVdf_RejectsUnbalancedBraces()
    {
        Assert.Throws<FormatException>(() => TextVdf.Parse("\"users\" { \"a\" { }"));
    }

    [Fact]
    public void FindSteamPath_UsesRegistryThenFallback()
    {
        using var temp = new TempFolder();
        var registryPath = Directory.CreateDirectory(temp.Combine("FromRegistry")).FullName;
        var fallback = Directory.CreateDirectory(temp.Combine("Fallback")).FullName;

        Assert.Equal(registryPath, new SteamLocator(() => registryPath.Replace('\\', '/'), fallback).FindSteamPath());
        Assert.Equal(fallback, new SteamLocator(() => null, fallback).FindSteamPath());
        Assert.Equal(fallback, new SteamLocator(() => temp.Combine("Missing"), fallback).FindSteamPath());
        Assert.Null(new SteamLocator(() => null, temp.Combine("Nope")).FindSteamPath());
    }

    [Fact]
    public void GetAccounts_ReadsFoldersPersonasAndShortcutCounts()
    {
        using var steam = FakeSteam(mostRecentAccount: null);

        var accounts = new SteamLocator(() => steam.Path).GetAccounts(steam.Path);

        Assert.Equal(new[] { "105508705", "75926236" }, accounts.Select(a => a.AccountId));
        var main = accounts.Single(a => a.AccountId == "75926236");
        Assert.Equal(76561198036191964UL, main.SteamId64);
        Assert.Equal("skee \"Wilin\"", main.PersonaName);
        Assert.Equal(2, main.ShortcutCount);
        Assert.EndsWith(Path.Combine("75926236", "config", "grid"), main.GridPath);
        Assert.Equal(0, accounts.Single(a => a.AccountId == "105508705").ShortcutCount);
    }

    [Fact]
    public void DefaultAccount_PrefersMostRecent()
    {
        using var steam = FakeSteam(mostRecentAccount: "76561198065774433");
        var accounts = new SteamLocator().GetAccounts(steam.Path);

        Assert.Equal("105508705", SteamLocator.GetDefaultAccount(accounts)!.AccountId);
    }

    [Fact]
    public void DefaultAccount_WithoutMostRecent_PicksOneWithShortcuts()
    {
        using var steam = FakeSteam(mostRecentAccount: null);
        var accounts = new SteamLocator().GetAccounts(steam.Path);

        Assert.Equal("75926236", SteamLocator.GetDefaultAccount(accounts)!.AccountId);
    }

    [Fact]
    public void DefaultAccount_FallsBackToFirst()
    {
        using var steam = new TempFolder();
        Directory.CreateDirectory(steam.Combine("userdata", "222"));
        Directory.CreateDirectory(steam.Combine("userdata", "111"));
        Directory.CreateDirectory(steam.Combine("userdata", "0"));
        Directory.CreateDirectory(steam.Combine("userdata", "anonymous"));

        var accounts = new SteamLocator().GetAccounts(steam.Path);

        Assert.Equal(new[] { "111", "222" }, accounts.Select(a => a.AccountId));
        Assert.Equal("111", SteamLocator.GetDefaultAccount(accounts)!.AccountId);
        Assert.Null(SteamLocator.GetDefaultAccount([]));
    }

    private static TempFolder FakeSteam(string? mostRecentAccount)
    {
        var steam = new TempFolder();
        string Flag(string id) => id == mostRecentAccount ? "1" : "0";
        steam.WriteFile(Path.Combine("config", "loginusers.vdf"), Encoding.UTF8.GetBytes($$"""
            "users"
            {
                "76561198036191964" { "PersonaName" "skee \"Wilin\"" "MostRecent" "{{Flag("76561198036191964")}}" }
                "76561198065774433" { "PersonaName" "test_toob_baby" "MostRecent" "{{Flag("76561198065774433")}}" }
            }
            """));
        Directory.CreateDirectory(steam.Combine("userdata", "105508705", "config"));
        steam.WriteFile(Path.Combine("userdata", "75926236", "config", "shortcuts.vdf"),
            VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "A", "a.exe", 1).Shortcut("1", "B", "b.exe", 2)));
        return steam;
    }
}
