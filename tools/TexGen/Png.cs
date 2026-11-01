using System.IO.Compression;

namespace Pluton.TexGen;

/// <summary>Escritor PNG RGBA8 mínimo, sin dependencias.</summary>
public static class Png
{
    private static readonly uint[] Table = MakeTable();

    private static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream s, string type, byte[] body)
    {
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        var all = new byte[name.Length + body.Length];
        name.CopyTo(all, 0);
        body.CopyTo(all, name.Length);
        WriteBe(s, (uint)body.Length);
        s.Write(all);
        WriteBe(s, Crc(all));
    }

    private static void WriteBe(Stream s, uint v)
    {
        s.WriteByte((byte)(v >> 24));
        s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)v);
    }

    /// <summary>pixels: RGBA por filas, de arriba abajo.</summary>
    public static void Save(string path, int w, int h, byte[] pixels)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        file.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var head = new MemoryStream();
        WriteBe(head, (uint)w);
        WriteBe(head, (uint)h);
        head.Write(new byte[] { 8, 6, 0, 0, 0 });
        Chunk(file, "IHDR", head.ToArray());
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.SmallestSize, true))
        {
            // Filtro «Sub»: el ruido se comprime algo mejor que sin filtrar.
            var row = new byte[w * 4 + 1];
            row[0] = 1;
            for (var y = 0; y < h; y++)
            {
                var o = y * w * 4;
                for (var i = 0; i < w * 4; i++)
                    row[i + 1] = (byte)(pixels[o + i] - (i >= 4 ? pixels[o + i - 4] : 0));
                z.Write(row);
            }
        }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
    }
}
