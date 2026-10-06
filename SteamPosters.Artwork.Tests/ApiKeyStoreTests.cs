using System.Text;

namespace SteamPosters.Artwork.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ApiKeyStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips_AndFileIsNotPlainText()
    {
        if (!OperatingSystem.IsWindows()) return; // DPAPI is Windows-only
        using var temp = new TempFolder();
        var store = new ApiKeyStore(temp.Path);

        Assert.Null(store.Load());
        store.Save("  0123456789abcdef-secret  ");

        Assert.Equal("0123456789abcdef-secret", new ApiKeyStore(temp.Path).Load());
        var onDisk = File.ReadAllBytes(Path.Combine(temp.Path, ApiKeyStore.FileName));
        Assert.DoesNotContain("0123456789abcdef", Encoding.UTF8.GetString(onDisk));
        Assert.DoesNotContain("0123456789abcdef", Encoding.Unicode.GetString(onDisk));
    }

    [Fact]
    public void Load_ReturnsNull_ForUnreadableFile_AndDeleteRemovesKey()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TempFolder();
        var store = new ApiKeyStore(temp.Path);
        File.WriteAllBytes(Path.Combine(temp.Path, ApiKeyStore.FileName), [1, 2, 3, 4]);

        Assert.Null(store.Load());

        store.Save("abc");
        store.Delete();
        Assert.Null(store.Load());
        Assert.Throws<ArgumentException>(() => store.Save(" "));
    }
}
