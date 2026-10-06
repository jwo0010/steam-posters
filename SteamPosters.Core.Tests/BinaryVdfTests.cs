using SteamPosters.Core.Vdf;

namespace SteamPosters.Core.Tests;

public class BinaryVdfTests
{
    [Fact]
    public void RoundTrip_IsByteIdentical()
    {
        var bytes = VdfBuilder.ShortcutsFile(b => b
            .Shortcut("0", "Avowed", "\"F:\\games\\Avowed\\Avowed.exe\"", 3249773087)
            .Shortcut("1", "ACBlackFlag", "F:\\games\\AC\\ACBlackFlag.exe", 2485127489, appNameKey: "appname"));

        var doc = BinaryVdfDocument.Parse(bytes);

        Assert.Equal(bytes, doc.ToBytes());
        Assert.True(doc.RootTerminated);
    }

    [Fact]
    public void RoundTrip_KeepsTrailingBytesAndMissingRootTerminator()
    {
        var withTrailing = new VdfBuilder().Map("shortcuts").End().End().ToArray().Concat(new byte[] { 0x00, 0x42 }).ToArray();
        Assert.Equal(withTrailing, BinaryVdfDocument.Parse(withTrailing).ToBytes());

        var unterminatedRoot = new VdfBuilder().Map("shortcuts").End().ToArray();
        var doc = BinaryVdfDocument.Parse(unterminatedRoot);
        Assert.False(doc.RootTerminated);
        Assert.Equal(unterminatedRoot, doc.ToBytes());
    }

    [Fact]
    public void RoundTrip_PreservesInvalidUtf8Bytes()
    {
        var bytes = VdfBuilder.ShortcutsFile(b => b
            .Map("0").Str("AppName", new byte[] { 0x47, 0xFF, 0xFE, 0x61 }).End());

        Assert.Equal(bytes, BinaryVdfDocument.Parse(bytes).ToBytes());
    }

    [Fact]
    public void OtherKnownTypes_ArePreservedRaw()
    {
        var float32 = BitConverter.GetBytes(1.5f);
        var uint64 = BitConverter.GetBytes(0x0123456789ABCDEFUL);
        var wide = System.Text.Encoding.Unicode.GetBytes("Hi\0");
        var bytes = VdfBuilder.ShortcutsFile(b => b
            .Map("0")
            .Typed(0x03, "f", float32)
            .Typed(0x07, "u64", uint64)
            .Typed(0x05, "wide", wide)
            .Typed(0x06, "color", new byte[] { 1, 2, 3, 4 })
            .Typed(0x0A, "i64", BitConverter.GetBytes(-5L))
            .End());

        var doc = BinaryVdfDocument.Parse(bytes);
        var entry = doc.Root.GetMap("shortcuts")!.GetMap("0")!;

        Assert.Equal(bytes, doc.ToBytes());
        Assert.Equal(VdfType.UInt64, entry.Find("u64")!.Type);
        Assert.Equal(uint64, entry.Find("u64")!.RawValue.ToArray());
        Assert.Equal(wide, entry.Find("wide")!.RawValue.ToArray());
    }

    [Fact]
    public void UnknownType_IsRejectedRatherThanGuessed()
    {
        var bytes = VdfBuilder.ShortcutsFile(b => b.Map("0").Typed(0x42, "mystery", new byte[] { 1, 2 }).End());

        var ex = Assert.Throws<InvalidDataException>(() => BinaryVdfDocument.Parse(bytes));
        Assert.Contains("0x42", ex.Message);
    }

    [Fact]
    public void TruncatedFile_IsRejected()
    {
        var bytes = VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "Avowed", "a.exe", 1));

        Assert.Throws<InvalidDataException>(() => BinaryVdfDocument.Parse(bytes.AsSpan(0, bytes.Length - 10)));
    }

    [Fact]
    public void Lookup_IsCaseInsensitive_AndSetterKeepsOriginalKeyCasing()
    {
        var bytes = VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "Old", "a.exe", 7, appNameKey: "appname"));
        var doc = BinaryVdfDocument.Parse(bytes);
        var map = doc.Root.GetMap("SHORTCUTS")!.GetMap("0")!;

        Assert.Equal("Old", map.GetString("AppName"));
        map.SetString("AppName", "New");

        Assert.Equal("appname", map.Find("APPNAME")!.Key);
        var expected = VdfBuilder.ShortcutsFile(b => b.Shortcut("0", "New", "a.exe", 7, appNameKey: "appname"));
        Assert.Equal(expected, doc.ToBytes());
    }
}
