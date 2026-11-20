using System.Buffers.Binary;
using System.Text;

namespace ClipboardSync.Messaging;

/// <summary>
///     One list, as the local server sends it: a header, the column names, then the rows and a single
///     UTF-8 string pool. The web view reads the numbers as they are and decodes only the strings it
///     draws, so a page costs one copy of its bytes instead of a parsed object per row.
/// </summary>
/// <remarks>
///     Layout, little-endian:
///     <code>
///     header  "YABS" u16 version u16 kind u32 rows u32 textCols u32 numberCols
///             u32 metaCount u32 poolBytes u32 reserved                              (32 bytes)
///     names   u16 length + UTF-8, for every text column, then every number column
///     meta    u16 key length + key, u16 value length + value, metaCount times
///     rows    textCols × (u32 offset, u16 length), numberCols × f64
///     pool    the strings the row offsets point at
///     </code>
/// </remarks>
internal sealed class DataBlock(ushort kind)
{
    public const uint Magic = 0x53424159; // "YABS"
    public const ushort Version = 1;
    public const string ContentType = "application/x-clipboardsync-block";

    private readonly List<(string Key, string Value)> _meta = [];
    private readonly List<string> _numberNames = [];
    private readonly List<double[]> _numberRows = [];
    private readonly List<string> _textNames = [];
    private readonly List<string[]> _textRows = [];

    public DataBlock Text(string name)
    {
        _textNames.Add(name);
        return this;
    }

    public DataBlock Number(string name)
    {
        _numberNames.Add(name);
        return this;
    }

    public DataBlock Meta(string key, string? value)
    {
        _meta.Add((key, value ?? ""));
        return this;
    }

    public DataBlock Row(string[] text, params double[] numbers)
    {
        _textRows.Add(text);
        _numberRows.Add(numbers);
        return this;
    }

    public byte[] Build()
    {
        var count = _textRows.Count;

        if (_numberRows.Count != count)
            throw new InvalidOperationException("Every row needs both halves.");

        if (_textRows.Any(row => row.Length != _textNames.Count))
            throw new InvalidOperationException($"A row has the wrong number of strings for {_textNames.Count} columns.");

        var pool = new List<byte>(count * 32);
        var offsets = new int[count][];
        var lengths = new int[count][];

        for (var i = 0; i < count; i++)
        {
            offsets[i] = new int[_textNames.Count];
            lengths[i] = new int[_textNames.Count];

            for (var column = 0; column < _textNames.Count; column++)
            {
                var bytes = Encoding.UTF8.GetBytes(_textRows[i][column]);
                offsets[i][column] = pool.Count;
                lengths[i][column] = bytes.Length;
                pool.AddRange(bytes);
            }
        }

        var names = _textNames.Concat(_numberNames).ToArray();
        var nameBytes = names.Sum(name => 2 + Encoding.UTF8.GetByteCount(name));
        var metaBytes = _meta.Sum(entry => 4 + Encoding.UTF8.GetByteCount(entry.Key) + Encoding.UTF8.GetByteCount(entry.Value));
        var rowBytes = count * (_textNames.Count * 6 + _numberNames.Count * 8);

        var buffer = new byte[32 + nameBytes + metaBytes + rowBytes + pool.Count];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], kind);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], (uint)_textNames.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)_numberNames.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)_meta.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)pool.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], 0);

        var at = 32;

        foreach (var name in names)
            at = WriteString(span, at, name);

        foreach (var (key, value) in _meta)
        {
            at = WriteString(span, at, key);
            at = WriteString(span, at, value);
        }

        for (var i = 0; i < count; i++)
        {
            for (var column = 0; column < _textNames.Count; column++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[at..], (uint)offsets[i][column]);
                BinaryPrimitives.WriteUInt16LittleEndian(span[(at + 4)..], (ushort)lengths[i][column]);
                at += 6;
            }

            foreach (var number in _numberRows[i])
            {
                BinaryPrimitives.WriteDoubleLittleEndian(span[at..], number);
                at += 8;
            }
        }

        pool.CopyTo(span[at..]);
        return buffer;
    }

    private static int WriteString(Span<byte> span, int at, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        BinaryPrimitives.WriteUInt16LittleEndian(span[at..], (ushort)bytes.Length);
        bytes.CopyTo(span[(at + 2)..]);
        return at + 2 + bytes.Length;
    }
}
