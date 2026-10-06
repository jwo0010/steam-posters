using System.Text;
using SteamPosters.Core.Steam;
using SteamPosters.Core.Vdf;

namespace SteamPosters.Core.Tests;

public class SteamLocatorTests
{
    private const string LoginUsers = """
        "users"
        {
        	"76561198000000002"
        	{
        		"AccountName"		"someone"
        		"PersonaName"		"player \"One\""
        		"RememberPassword"		"1"
        		"MostRecent"		"0"
        		"Timestamp"		"1759700000"
        	}
        	// a comment
        	"76561198000000001"
        	{
        		"AccountName"		"tester"
        		"PersonaName"		"player_two"
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
        Assert.Equal("76561198000000002", children[0].Key);
        Assert.Equal("player \"One\"", children[0].Value.GetString("personaname"));
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

        Assert.Equal(new[] { "39734273", "39734274" }, accounts.Select(a => a.AccountId));
        var main = accounts.Single(a => a.AccountId == "39734274");
        Assert.Equal(76561198000000002UL, main.SteamId64);
        Assert.Equal("player \"One\"", main.PersonaName);
        Assert.Equal(2, main.ShortcutCount);
        Assert.EndsWith(Path.Combine("39734274", "config", "grid"), main.GridPath);
        Assert.Equal(0, accounts.Single(a => a.AccountId == "39734273").ShortcutCount);
    }

    [Fact]
    public void DefaultAccount_PrefersMostRecent()
    {
        using var steam = FakeSteam(mostRecentAccount: "76561198000000001");
        var accounts = new SteamLocator().GetAccounts(steam.Path);

        Assert.Equal("39734273", SteamLocator.GetDefaultAccount(accounts)!.AccountId);
    }

    [Fact]
    public void DefaultAccount_WithoutMostRecent_PicksOneWithShortcuts()
    {
        using var steam = FakeSteam(mostRecentAccount: null);
        var accounts = new SteamLocator().GetAccounts(steam.Path);

        Assert.Equal("39734274", SteamLocator.GetDefaultAccount(accounts)!.AccountId);
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
                "76561198000000002" { "PersonaName" "player \"One\"" "MostRecent" "{{Flag("76561198000000002")}}" }
                "76561198000000001" { "PersonaName" "player_two" "MostRecent" "{{Flag("76561198000000001")}}" }
            }
            """));
        Directory.CreateDirectory(steam.Combine("userdata", "39734273", "config"));
        steam.WriteFile(Path.Combine("userdata", "39734274", "config", "shortcuts.vdf"),
            VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "A", "a.exe", 1).Shortcut("1", "B", "b.exe", 2)));
        return steam;
    }
}
