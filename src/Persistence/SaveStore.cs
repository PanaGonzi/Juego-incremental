using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Pluton.Simulation;

namespace Pluton.Persistence;

public sealed record SaveInfo(string Slot, string Path, int FormatVersion, string ContentRevision, long Tick, DateTime SavedUtc);

public class SaveException : Exception
{
    public SaveException(string message) : base(message) { }
}

/// <summary>
/// Guardados en datos de usuario, separados por perfil (user, dev, test).
/// Escritura atómica: archivo temporal, copia .bak del anterior y sustitución.
/// </summary>
public sealed class SaveStore
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PLSV");
    public string Directory { get; }

    public SaveStore(string profile = "user", string? rootOverride = null)
    {
        var root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlutonNovenoPlaneta");
        Directory = Path.Combine(root, profile, "saves");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string PathOf(string slot)
    {
        foreach (var c in slot)
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') throw new SaveException("Nombre de ranura inválido");
        if (slot.Length == 0 || slot.Length > 40) throw new SaveException("Nombre de ranura inválido");
        return Path.Combine(Directory, slot + ".plsave");
    }

    public static byte[] Encode(byte[] worldBytes, string contentRevision, long tick, DateTime savedUtc)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(worldBytes);
        var payload = compressed.ToArray();

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Magic);
        w.Write(WorldCodec.FormatVersion);
        w.Write(contentRevision);
        w.Write(tick);
        w.Write(savedUtc.ToBinary());
        w.Write(payload.Length);
        w.Write(payload);
        w.Write(SHA256.HashData(payload));
        w.Flush();
        return ms.ToArray();
    }

    public void Write(string slot, byte[] encoded)
    {
        var path = PathOf(slot);
        var temp = path + ".tmp";
        using (var f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            f.Write(encoded);
            f.Flush(flushToDisk: true);
        }
        if (File.Exists(path))
            File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(temp, path);
    }

    public void Save(string slot, World world)
    {
        world.SavedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Write(slot, Encode(WorldCodec.Serialize(world), world.Content.Revision, world.Tick, DateTime.UtcNow));
    }

    /// <summary>Carga una ranura; si está corrupta recurre al respaldo. Nunca sobrescribe un formato más nuevo.</summary>
    public World Load(string slot, ContentDb content)
    {
        var path = PathOf(slot);
        try
        {
            var data = File.ReadAllBytes(path);
            var version = BitConverter.ToInt32(data, 4);
            if (version < WorldCodec.FormatVersion && data.Length > 8)
            {
                // La partida antigua se conserva intacta antes de reescribirla en el formato nuevo.
                var copy = $"{path}.v{version}.bak";
                if (!File.Exists(copy)) File.WriteAllBytes(copy, data);
            }
            return Decode(data, content);
        }
        catch (NewerFormatException)
        {
            throw;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or SaveException or EndOfStreamException)
        {
            var bak = path + ".bak";
            if (!File.Exists(bak)) throw new SaveException($"Guardado dañado y sin respaldo: {e.Message}");
            return Decode(File.ReadAllBytes(bak), content);
        }
    }

    public sealed class NewerFormatException : SaveException
    {
        public NewerFormatException(int version) : base($"La partida usa el formato {version}, más nuevo que esta versión ({WorldCodec.FormatVersion}).") { }
    }

    public static World Decode(byte[] data, ContentDb content)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        if (!r.ReadBytes(4).SequenceEqual(Magic)) throw new SaveException("No es un guardado de PLUTON");
        var version = r.ReadInt32();
        if (version > WorldCodec.FormatVersion) throw new NewerFormatException(version);
        r.ReadString();
        r.ReadInt64();
        r.ReadInt64();
        var length = r.ReadInt32();
        if (length < 0 || length > data.Length) throw new SaveException("Longitud inválida");
        var payload = r.ReadBytes(length);
        var hash = r.ReadBytes(32);
        if (!SHA256.HashData(payload).SequenceEqual(hash)) throw new SaveException("Firma incorrecta");

        using var decompressed = new MemoryStream();
        using (var deflate = new DeflateStream(new MemoryStream(payload), CompressionMode.Decompress))
            deflate.CopyTo(decompressed);
        return WorldCodec.Deserialize(content, decompressed.ToArray(), version);
    }

    public IReadOnlyList<SaveInfo> List()
    {
        var result = new List<SaveInfo>();
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*.plsave"))
        {
            try
            {
                using var r = new BinaryReader(File.OpenRead(path));
                if (!r.ReadBytes(4).SequenceEqual(Magic)) continue;
                var version = r.ReadInt32();
                var revision = r.ReadString();
                var tick = r.ReadInt64();
                var saved = DateTime.FromBinary(r.ReadInt64());
                result.Add(new SaveInfo(Path.GetFileNameWithoutExtension(path), path, version, revision, tick, saved));
            }
            catch (Exception e) when (e is IOException or EndOfStreamException) { }
        }
        result.Sort((a, b) => b.SavedUtc.CompareTo(a.SavedUtc));
        return result;
    }
}
