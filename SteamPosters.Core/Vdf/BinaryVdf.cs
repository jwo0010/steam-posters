using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace SteamPosters.Core.Vdf;

/// <summary>Type byte that precedes every entry in a binary VDF file.</summary>
public enum VdfType : byte
{
    Map = 0x00,
    String = 0x01,
    Int32 = 0x02,
    Float32 = 0x03,
    Pointer = 0x04,
    WideString = 0x05,
    Color = 0x06,
    UInt64 = 0x07,
    End = 0x08,
    Int64 = 0x0A,
    AlternateEnd = 0x0B,
}

/// <summary>
/// One key/value entry. Keys and string values are kept as raw bytes so a file
/// round-trips byte for byte even if it contains invalid UTF-8.
/// </summary>
public sealed class VdfEntry
{
    private byte[] _keyBytes;
    private byte[]? _raw;
    private VdfMap? _map;

    private VdfEntry(VdfType type, byte[] keyBytes, byte[]? raw, VdfMap? map)
    {
        Type = type;
        _keyBytes = keyBytes;
        _raw = raw;
        _map = map;
    }

    public VdfType Type { get; private set; }

    public string Key => Encoding.UTF8.GetString(_keyBytes);

    internal ReadOnlySpan<byte> KeyBytes => _keyBytes;

    /// <summary>Raw payload: string bytes without the terminator, or the fixed-size number bytes.</summary>
    public ReadOnlySpan<byte> RawValue => _raw;

    public VdfMap? Map => _map;

    public string? StringValue => Type == VdfType.String ? Encoding.UTF8.GetString(_raw!) : null;

    public uint? UInt32Value => Type == VdfType.Int32 ? BinaryPrimitives.ReadUInt32LittleEndian(_raw) : null;

    public static VdfEntry CreateMap(string key, VdfMap? map = null) =>
        new(VdfType.Map, Encoding.UTF8.GetBytes(key), null, map ?? new VdfMap());

    public static VdfEntry CreateString(string key, string value) =>
        new(VdfType.String, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value), null);

    public static VdfEntry CreateUInt32(string key, uint value)
    {
        var raw = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(raw, value);
        return new(VdfType.Int32, Encoding.UTF8.GetBytes(key), raw, null);
    }

    internal static VdfEntry CreateRaw(VdfType type, byte[] keyBytes, byte[]? raw, VdfMap? map) =>
        new(type, keyBytes, raw, map);

    public void SetString(string value)
    {
        Type = VdfType.String;
        _raw = Encoding.UTF8.GetBytes(value);
        _map = null;
    }

    public void SetUInt32(uint value)
    {
        Type = VdfType.Int32;
        _raw = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(_raw, value);
        _map = null;
    }

    public override string ToString() => Type switch
    {
        VdfType.Map => $"{Key} {{{_map!.Count}}}",
        VdfType.String => $"{Key} = \"{StringValue}\"",
        VdfType.Int32 => $"{Key} = {UInt32Value}",
        _ => $"{Key} ({Type}, {_raw?.Length ?? 0} bytes)",
    };
}

/// <summary>Ordered list of entries. Key lookups are case-insensitive; original casing is kept.</summary>
public sealed class VdfMap : IEnumerable<VdfEntry>
{
    private readonly List<VdfEntry> _entries = new();

    public int Count => _entries.Count;

    public VdfEntry this[int index] => _entries[index];

    public VdfEntry? Find(string key) =>
        _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    public string? GetString(string key) => Find(key)?.StringValue;

    public uint? GetUInt32(string key) => Find(key)?.UInt32Value;

    public VdfMap? GetMap(string key) => Find(key)?.Map;

    /// <summary>Sets a string value, keeping the existing key's casing and position; appends if missing.</summary>
    public void SetString(string key, string value)
    {
        var entry = Find(key);
        if (entry is null) _entries.Add(VdfEntry.CreateString(key, value));
        else entry.SetString(value);
    }

    /// <summary>Sets an int32 value, keeping the existing key's casing and position; appends if missing.</summary>
    public void SetUInt32(string key, uint value)
    {
        var entry = Find(key);
        if (entry is null) _entries.Add(VdfEntry.CreateUInt32(key, value));
        else entry.SetUInt32(value);
    }

    public void Add(VdfEntry entry) => _entries.Add(entry);

    public bool Remove(string key)
    {
        var entry = Find(key);
        return entry is not null && _entries.Remove(entry);
    }

    public IEnumerator<VdfEntry> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A binary VDF file (e.g. shortcuts.vdf). The root is an implicit map that is closed by
/// a final 0x08; anything after it is kept as trailing bytes so the file round-trips exactly.
/// </summary>
public sealed class BinaryVdfDocument
{
    private BinaryVdfDocument(VdfMap root, bool rootTerminated, byte[] trailing)
    {
        Root = root;
        RootTerminated = rootTerminated;
        TrailingBytes = trailing;
    }

    public VdfMap Root { get; }

    /// <summary>Whether the file ended its root map with 0x08 (rather than just ending).</summary>
    public bool RootTerminated { get; }

    public byte[] TrailingBytes { get; }

    public static BinaryVdfDocument Parse(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        var root = reader.ReadMap(out var terminated, isRoot: true);
        var trailing = data[reader.Position..].ToArray();
        return new BinaryVdfDocument(root, terminated, trailing);
    }

    public byte[] ToBytes()
    {
        using var stream = new MemoryStream();
        WriteMap(stream, Root);
        if (RootTerminated) stream.WriteByte((byte)VdfType.End);
        stream.Write(TrailingBytes);
        return stream.ToArray();
    }

    private static void WriteMap(Stream stream, VdfMap map)
    {
        foreach (var entry in map)
        {
            stream.WriteByte((byte)entry.Type);
            stream.Write(entry.KeyBytes);
            stream.WriteByte(0);
            switch (entry.Type)
            {
                case VdfType.Map:
                    WriteMap(stream, entry.Map!);
                    stream.WriteByte((byte)VdfType.End);
                    break;
                case VdfType.String:
                    stream.Write(entry.RawValue);
                    stream.WriteByte(0);
                    break;
                default:
                    // Fixed-size numbers, and wide strings whose raw bytes include their terminator.
                    stream.Write(entry.RawValue);
                    break;
            }
        }
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;

        public Reader(ReadOnlySpan<byte> data)
        {
            _data = data;
            Position = 0;
        }

        public int Position { get; private set; }

        public VdfMap ReadMap(out bool terminated, bool isRoot)
        {
            var map = new VdfMap();
            while (Position < _data.Length)
            {
                var typeOffset = Position;
                var type = (VdfType)_data[Position++];
                if (type is VdfType.End or VdfType.AlternateEnd)
                {
                    if (type == VdfType.AlternateEnd)
                        throw new InvalidDataException($"Unsupported map terminator 0x0B at offset {typeOffset}.");
                    terminated = true;
                    return map;
                }

                var key = ReadCString();
                switch (type)
                {
                    case VdfType.Map:
                        var child = ReadMap(out var childTerminated, isRoot: false);
                        if (!childTerminated)
                            throw new InvalidDataException($"Map '{Encoding.UTF8.GetString(key)}' is not terminated.");
                        map.Add(VdfEntry.CreateRaw(type, key, null, child));
                        break;
                    case VdfType.String:
                        map.Add(VdfEntry.CreateRaw(type, key, ReadCString(), null));
                        break;
                    case VdfType.Int32 or VdfType.Float32 or VdfType.Pointer or VdfType.Color:
                        map.Add(VdfEntry.CreateRaw(type, key, ReadFixed(4, typeOffset), null));
                        break;
                    case VdfType.UInt64 or VdfType.Int64:
                        map.Add(VdfEntry.CreateRaw(type, key, ReadFixed(8, typeOffset), null));
                        break;
                    case VdfType.WideString:
                        map.Add(VdfEntry.CreateRaw(type, key, ReadWideString(typeOffset), null));
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unknown VDF type 0x{(byte)type:X2} at offset {typeOffset}; refusing to guess its size.");
                }
            }

            if (!isRoot) throw new InvalidDataException("Unexpected end of data inside a map.");
            terminated = false;
            return map;
        }

        private byte[] ReadCString()
        {
            var rest = _data[Position..];
            var end = rest.IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException($"Unterminated string at offset {Position}.");
            var bytes = rest[..end].ToArray();
            Position += end + 1;
            return bytes;
        }

        private byte[] ReadFixed(int size, int typeOffset)
        {
            if (Position + size > _data.Length)
                throw new InvalidDataException($"Truncated value at offset {typeOffset}.");
            var bytes = _data.Slice(Position, size).ToArray();
            Position += size;
            return bytes;
        }

        private byte[] ReadWideString(int typeOffset)
        {
            // UTF-16LE, terminated by a 0x0000 code unit; raw bytes keep the terminator.
            var start = Position;
            while (Position + 1 < _data.Length)
            {
                var unit = _data[Position] | (_data[Position + 1] << 8);
                Position += 2;
                if (unit == 0) return _data[start..Position].ToArray();
            }
            throw new InvalidDataException($"Unterminated wide string at offset {typeOffset}.");
        }
    }
}
