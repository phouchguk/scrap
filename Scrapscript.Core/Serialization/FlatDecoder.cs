using System.Collections.Immutable;
using System.Text;
using Scrapscript.Core.Eval;

namespace Scrapscript.Core.Serialization;

public static class FlatDecoder
{
    public static ScrapValue Decode(byte[] data)
    {
        int pos = 0;
        return Read(data, ref pos);
    }

    private static ScrapValue Read(byte[] data, ref int pos)
    {
        byte b = data[pos++];

        // positive fixint: 0x00–0x7F
        if (b <= 0x7F)
            return new ScrapInt(b);

        // fixmap: 0x80–0x8F
        if (b >= 0x80 && b <= 0x8F)
            return ReadRecord(b & 0x0F, data, ref pos);

        // fixarray: 0x90–0x9F
        if (b >= 0x90 && b <= 0x9F)
            return ReadList(b & 0x0F, data, ref pos);

        // fixstr: 0xA0–0xBF
        if (b >= 0xA0 && b <= 0xBF)
            return ReadText(b & 0x1F, data, ref pos);

        // negative fixint: 0xE0–0xFF
        if (b >= 0xE0)
            return new ScrapInt((sbyte)b);

        return b switch
        {
            0xC0 => new ScrapHole(),
            0xC4 => ReadBytes(data[pos++], data, ref pos),
            0xC5 => ReadBytes(ReadUInt16(data, ref pos), data, ref pos),
            0xC7 => ReadVariant(data, ref pos),
            0xCB => ReadFloat(data, ref pos),
            0xD3 => ReadInt64(data, ref pos),
            0xD9 => ReadText(data[pos++], data, ref pos),
            0xDA => ReadText(ReadUInt16(data, ref pos), data, ref pos),
            0xDC => ReadList(ReadUInt16(data, ref pos), data, ref pos),
            0xDE => ReadRecord(ReadUInt16(data, ref pos), data, ref pos),
            _ => throw new InvalidOperationException($"Unknown msgpack byte: 0x{b:X2}")
        };
    }

    private static int ReadUInt16(byte[] data, ref int pos)
    {
        int hi = data[pos++];
        int lo = data[pos++];
        return (hi << 8) | lo;
    }

    private static ScrapFloat ReadFloat(byte[] data, ref int pos)
    {
        var bytes = new byte[8];
        Array.Copy(data, pos, bytes, 0, 8);
        pos += 8;
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new ScrapFloat(BitConverter.ToDouble(bytes));
    }

    private static ScrapInt ReadInt64(byte[] data, ref int pos)
    {
        var bytes = new byte[8];
        Array.Copy(data, pos, bytes, 0, 8);
        pos += 8;
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return new ScrapInt(BitConverter.ToInt64(bytes));
    }

    private static ScrapText ReadText(int len, byte[] data, ref int pos)
    {
        var s = Encoding.UTF8.GetString(data, pos, len);
        pos += len;
        return new ScrapText(s);
    }

    private static ScrapBytes ReadBytes(int len, byte[] data, ref int pos)
    {
        var bytes = new byte[len];
        Array.Copy(data, pos, bytes, 0, len);
        pos += len;
        return new ScrapBytes(bytes);
    }

    private static ScrapList ReadList(int count, byte[] data, ref int pos)
    {
        var items = ImmutableList<ScrapValue>.Empty;
        for (int i = 0; i < count; i++)
            items = items.Add(Read(data, ref pos));
        return new ScrapList(items);
    }

    private static ScrapRecord ReadRecord(int count, byte[] data, ref int pos)
    {
        var fields = ImmutableDictionary<string, ScrapValue>.Empty;
        for (int i = 0; i < count; i++)
        {
            var key = (ScrapText)Read(data, ref pos);
            var val = Read(data, ref pos);
            fields = fields.SetItem(key.Value, val);
        }
        return new ScrapRecord(fields);
    }

    private static ScrapVariant ReadVariant(byte[] data, ref int pos)
    {
        int totalLen = data[pos++];
        int extType = data[pos++]; // 0x00
        _ = extType;

        int tagLen = data[pos++];
        var tag = Encoding.UTF8.GetString(data, pos, tagLen);
        pos += tagLen;

        int payloadLen = totalLen - 1 - tagLen; // subtract tag-len byte + tag bytes
        ScrapValue? payload = null;
        if (payloadLen > 0)
        {
            payload = Read(data, ref pos);
        }

        return new ScrapVariant(tag, payload);
    }
}
