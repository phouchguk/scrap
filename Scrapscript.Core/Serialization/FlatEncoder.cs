using System.Text;
using Scrapscript.Core.Eval;

namespace Scrapscript.Core.Serialization;

public static class FlatEncoder
{
    public static byte[] Encode(ScrapValue value)
    {
        var buf = new List<byte>();
        Write(value, buf);
        return buf.ToArray();
    }

    private static void Write(ScrapValue value, List<byte> buf)
    {
        switch (value)
        {
            case ScrapHole:
                buf.Add(0xC0);
                break;

            case ScrapInt i:
                WriteInt(i.Value, buf);
                break;

            case ScrapFloat f:
                buf.Add(0xCB);
                var fbytes = BitConverter.GetBytes(f.Value);
                if (BitConverter.IsLittleEndian) Array.Reverse(fbytes);
                buf.AddRange(fbytes);
                break;

            case ScrapText t:
                WriteText(t.Value, buf);
                break;

            case ScrapBytes b:
                WriteBytes(b.Value, buf);
                break;

            case ScrapList l:
                WriteList(l, buf);
                break;

            case ScrapRecord r:
                WriteRecord(r, buf);
                break;

            case ScrapVariant v:
                WriteVariant(v, buf);
                break;

            default:
                throw new InvalidOperationException($"Cannot encode value type: {value.GetType().Name}");
        }
    }

    private static void WriteInt(long n, List<byte> buf)
    {
        if (n >= 0 && n <= 127)
        {
            buf.Add((byte)n);
        }
        else if (n >= -32 && n < 0)
        {
            buf.Add((byte)(0xE0 | (n + 32)));
        }
        else
        {
            buf.Add(0xD3);
            var ibytes = BitConverter.GetBytes(n);
            if (BitConverter.IsLittleEndian) Array.Reverse(ibytes);
            buf.AddRange(ibytes);
        }
    }

    private static void WriteText(string s, List<byte> buf)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= 31)
        {
            buf.Add((byte)(0xA0 | bytes.Length));
        }
        else if (bytes.Length <= 255)
        {
            buf.Add(0xD9);
            buf.Add((byte)bytes.Length);
        }
        else
        {
            buf.Add(0xDA);
            buf.Add((byte)(bytes.Length >> 8));
            buf.Add((byte)(bytes.Length & 0xFF));
        }
        buf.AddRange(bytes);
    }

    private static void WriteBytes(byte[] data, List<byte> buf)
    {
        if (data.Length <= 255)
        {
            buf.Add(0xC4);
            buf.Add((byte)data.Length);
        }
        else
        {
            buf.Add(0xC5);
            buf.Add((byte)(data.Length >> 8));
            buf.Add((byte)(data.Length & 0xFF));
        }
        buf.AddRange(data);
    }

    private static void WriteList(ScrapList l, List<byte> buf)
    {
        int count = l.Items.Count;
        if (count <= 15)
        {
            buf.Add((byte)(0x90 | count));
        }
        else
        {
            buf.Add(0xDC);
            buf.Add((byte)(count >> 8));
            buf.Add((byte)(count & 0xFF));
        }
        foreach (var item in l.Items)
            Write(item, buf);
    }

    private static void WriteRecord(ScrapRecord r, List<byte> buf)
    {
        var sorted = r.Fields.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        int count = sorted.Count;
        if (count <= 15)
        {
            buf.Add((byte)(0x80 | count));
        }
        else
        {
            buf.Add(0xDE);
            buf.Add((byte)(count >> 8));
            buf.Add((byte)(count & 0xFF));
        }
        foreach (var (key, val) in sorted)
        {
            WriteText(key, buf);
            Write(val, buf);
        }
    }

    private static void WriteVariant(ScrapVariant v, List<byte> buf)
    {
        // Encode: tag bytes + optional payload bytes
        var tagBytes = Encoding.UTF8.GetBytes(v.Tag);
        var payloadBuf = new List<byte>();
        if (v.Payload is not null)
            Write(v.Payload, payloadBuf);

        // ext format: 0xC7 + 1-byte total len + ext type (0x00) + tag-len(1) + tag-bytes + payload
        var extData = new List<byte>();
        extData.Add((byte)tagBytes.Length);
        extData.AddRange(tagBytes);
        extData.AddRange(payloadBuf);

        buf.Add(0xC7);
        buf.Add((byte)extData.Count);
        buf.Add(0x00); // ext type
        buf.AddRange(extData);
    }
}
