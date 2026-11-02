using Pluton.Simulation;

namespace Pluton.Networking;

public static class Protocol
{
    public const int Version = 9;
    public const string GameVersion = "0.5.0-qa7";
    public const int MaxPlayers = 12;
    public const int MaxClientPacket = 8192; // una ruta ocupa ~1 byte por tramo recto: miles de curvas caben
    public const int MaxRouteCells = 40000; // más que todas las celdas de un planeta: cota técnica, no de juego
    public const int MaxNameLength = 24;
    public const int MaxChatLength = 200;
    public const int SnapshotChunkSize = 16 * 1024;
    public const int MaxSnapshotBytes = 32 * 1024 * 1024;
    public const float MaxMoveSpeed = 14f;
}

/// <summary>Estado de acción de un jugador, solo para representarlo ante los demás. Viaja con la posición y no se guarda.</summary>
public static class PlayerAction
{
    public const byte Stowed = 0, Cutter = 1, Device = 2;
    private const byte WorkingBit = 0x80;

    public static byte Pack(byte equipped, bool working) => Sanitize((byte)(equipped | (working ? WorkingBit : 0)));
    public static byte Equipped(byte action) => (byte)(action & 3);
    public static bool IsWorking(byte action) => (action & WorkingBit) != 0;

    /// <summary>El valor lo envía el cliente: se normaliza para no reenviar a los demás nada que no signifique algo.</summary>
    public static byte Sanitize(byte raw) => (byte)(Math.Min(raw & 3, (int)Device) | (raw & WorkingBit));
}

public enum MsgType : byte
{
    // Cliente a servidor
    Hello = 1, Command = 2, Move = 3, ChatSend = 4, SaveRequest = 5,
    // Servidor a cliente
    Welcome = 20, Reject = 21, SnapshotChunk = 22, SnapshotDone = 23, Delta = 24, DeltaCompressed = 31,
    Result = 25, Positions = 26, Chat = 27, Check = 28, Correct = 29, Notice = 30, Rates = 32,
}

public static class CommandCodec
{
    public static void Write(BinaryWriter w, Command c)
    {
        w.Write((byte)c.Type);
        w.Write(c.A);
        w.Write(c.B);
        w.Write(c.C);
        w.Write(c.D);
        w.Write(c.Amount);
        w.Write(c.Text);
        if (c.Type == CommandType.Link) WriteRoute(w, c.Cells);
    }

    /// <summary>Tamaño que ocuparía la ruta: el cliente lo comprueba antes de enviar una orden que el servidor rechazaría.</summary>
    public static int EncodedRouteSize(IReadOnlyList<(int x, int z)> cells)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteRoute(w, cells);
        w.Flush();
        return (int)ms.Length;
    }

    /// <summary>Inicio y tramos rectos (dirección y longitud): una ruta de cientos de celdas con pocas curvas ocupa muy poco.</summary>
    private static void WriteRoute(BinaryWriter w, IReadOnlyList<(int x, int z)>? cells)
    {
        if (cells == null || cells.Count == 0)
        {
            w.Write7BitEncodedInt(0);
            return;
        }
        var runs = new List<(int dir, int length)>();
        for (var i = 1; i < cells.Count; i++)
        {
            var step = (cells[i].x - cells[i - 1].x, cells[i].z - cells[i - 1].z);
            var dir = Array.IndexOf(World.BeltDirs, step);
            if (dir < 0) throw new ArgumentException("Ruta no contigua");
            if (runs.Count > 0 && runs[^1].dir == dir) runs[^1] = (dir, runs[^1].length + 1);
            else runs.Add((dir, 1));
        }
        w.Write7BitEncodedInt(runs.Count + 1);
        w.Write(cells[0].x);
        w.Write(cells[0].z);
        foreach (var (dir, length) in runs) w.Write7BitEncodedInt(length << 2 | dir);
    }

    private static List<(int x, int z)>? ReadRoute(BinaryReader r)
    {
        var count = r.Read7BitEncodedInt();
        if (count == 0) return null;
        if (count < 0 || count > Protocol.MaxClientPacket) throw new InvalidDataException("Ruta demasiado larga");
        var x = r.ReadInt32();
        var z = r.ReadInt32();
        var cells = new List<(int x, int z)> { (x, z) };
        for (var i = 1; i < count; i++)
        {
            var run = r.Read7BitEncodedInt();
            var length = run >> 2;
            if (run < 0 || length < 1 || cells.Count + length > Protocol.MaxRouteCells) throw new InvalidDataException("Ruta inválida");
            var (dx, dz) = World.BeltDirs[run & 3];
            for (var k = 0; k < length; k++)
            {
                x += dx;
                z += dz;
                cells.Add((x, z));
            }
        }
        return cells;
    }

    public static Command Read(BinaryReader r)
    {
        var type = r.ReadByte();
        if (!Enum.IsDefined(typeof(CommandType), type)) throw new InvalidDataException("Comando desconocido");
        var c = new Command
        {
            Type = (CommandType)type,
            A = r.ReadInt32(),
            B = r.ReadInt32(),
            C = r.ReadInt32(),
            D = r.ReadInt32(),
            Amount = r.ReadInt64(),
            Text = r.ReadString(),
        };
        if (c.Type == CommandType.Link) c.Cells = ReadRoute(r);
        return c;
    }
}

/// <summary>Escritor reutilizable para no asignar un búfer por mensaje.</summary>
public sealed class PacketWriter
{
    private readonly MemoryStream _ms = new();
    public readonly BinaryWriter W;

    public PacketWriter() { W = new BinaryWriter(_ms); }

    public BinaryWriter Begin(MsgType type)
    {
        _ms.SetLength(0);
        W.Write((byte)type);
        return W;
    }

    public byte[] Buffer { get { W.Flush(); return _ms.GetBuffer(); } }
    public int Length { get { W.Flush(); return (int)_ms.Length; } }
    public void Send(ITransport t, int peer, Channel channel) => t.Send(peer, Buffer, Length, channel);
}
