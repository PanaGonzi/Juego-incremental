using Pluton.Simulation;

namespace Pluton.Networking;

public static class Protocol
{
    public const int Version = 4;
    public const string GameVersion = "0.4.1";
    public const int MaxPlayers = 12;
    public const int MaxClientPacket = 2048;
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
    Result = 25, Positions = 26, Chat = 27, Check = 28, Correct = 29, Notice = 30,
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
    }

    public static Command Read(BinaryReader r)
    {
        var type = r.ReadByte();
        if (!Enum.IsDefined(typeof(CommandType), type)) throw new InvalidDataException("Comando desconocido");
        return new Command
        {
            Type = (CommandType)type,
            A = r.ReadInt32(),
            B = r.ReadInt32(),
            C = r.ReadInt32(),
            D = r.ReadInt32(),
            Amount = r.ReadInt64(),
            Text = r.ReadString(),
        };
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
