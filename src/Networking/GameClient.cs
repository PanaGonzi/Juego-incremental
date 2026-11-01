using Pluton.Simulation;

namespace Pluton.Networking;

public enum ClientState { Connecting, Loading, InGame, Closed }

/// <summary>Cliente sin motor: lo usan tanto Godot como los procesos de carga, con el protocolo real.</summary>
public sealed class GameClient : IDisposable
{
    public readonly ContentDb Content;
    private readonly ITransport _transport;
    private readonly string _key;
    private readonly string _name;
    private readonly string _password;
    private readonly PacketWriter _pw = new();
    private MemoryStream? _snapshot;
    private int _serverPeer = -1;
    private uint _nextRequest = 1;
    private readonly SortedDictionary<uint, Command> _pending = new();

    public ClientState State { get; private set; } = ClientState.Connecting;
    public string CloseReason { get; private set; } = "";
    public World? World { get; private set; }
    public int LocalNum { get; private set; }
    public long LastPositionsTick { get; private set; }
    private int _hashMismatches, _hashChecks;
    public int HashMismatches => Volatile.Read(ref _hashMismatches);
    public int HashChecks => Volatile.Read(ref _hashChecks);
    public int PendingCount => _pending.Count;
    public int StructuralDeltas { get; private set; }
    public int SlotDeltas { get; private set; }

    public event Action<uint, CommandResult>? ResultReceived;
    public event Action<string, string>? ChatReceived;
    public event Action<string, string>? NoticeReceived;
    public event Action<float, float, float>? PositionCorrected;
    public event Action<int, float, float, float, float>? RemoteMoved;
    public event Action<int, byte>? RemoteAction;
    public event Action? WorldReady;
    public event Action? DeltaApplied;

    public PlayerState? LocalPlayer => World != null && World.Players.TryGetValue(_key, out var p) ? p : null;
    public string Key => _key;
    public int RoundTripMs => _serverPeer >= 0 ? _transport.RoundTripMs(_serverPeer) : 0;
    public ITransport Transport => _transport;

    public GameClient(ContentDb content, ITransport transport, string key, string name, string password = "")
    {
        Content = content;
        _transport = transport;
        _key = key;
        _name = name;
        _password = password;
        _transport.Connected += OnConnected;
        _transport.Received += OnReceived;
        _transport.Disconnected += (_, reason) => Close(CloseReason.Length > 0 ? CloseReason : (State == ClientState.Connecting ? "reject_unreachable" : "reject_lost"));
    }

    public void Poll() => _transport.Poll();

    private void Close(string reason)
    {
        if (CloseReason.Length == 0) CloseReason = reason;
        State = ClientState.Closed;
    }

    private void OnConnected(int peer)
    {
        _serverPeer = peer;
        var w = _pw.Begin(MsgType.Hello);
        w.Write(Protocol.Version);
        w.Write(Protocol.GameVersion);
        w.Write(Content.Revision);
        w.Write(_key);
        w.Write(_name);
        w.Write(_password);
        _pw.Send(_transport, peer, Channel.Reliable);
        State = ClientState.Loading;
    }

    /// <summary>Envía una intención. El ID permite reenviarla sin aplicarla dos veces tras una reconexión.</summary>
    public uint Send(Command command)
    {
        var id = _nextRequest++;
        _pending[id] = command;
        Transmit(id, command);
        return id;
    }

    private void Transmit(uint id, Command command)
    {
        if (_serverPeer < 0) return;
        var w = _pw.Begin(MsgType.Command);
        w.Write(id);
        CommandCodec.Write(w, command);
        _pw.Send(_transport, _serverPeer, Channel.Reliable);
    }

    public void SendMove(float x, float y, float z, float yaw, byte action = 0)
    {
        if (_serverPeer < 0 || State != ClientState.InGame) return;
        var w = _pw.Begin(MsgType.Move);
        w.Write(x); w.Write(y); w.Write(z); w.Write(yaw); w.Write(action);
        _pw.Send(_transport, _serverPeer, Channel.Unreliable);
    }

    public void SendChat(string text)
    {
        if (_serverPeer < 0) return;
        _pw.Begin(MsgType.ChatSend).Write(text);
        _pw.Send(_transport, _serverPeer, Channel.Reliable);
    }

    public void RequestSave()
    {
        if (_serverPeer < 0) return;
        _pw.Begin(MsgType.SaveRequest);
        _pw.Send(_transport, _serverPeer, Channel.Reliable);
    }

    public void Disconnect()
    {
        if (_serverPeer >= 0) _transport.Disconnect(_serverPeer);
        Close("reject_left");
    }

    private void OnReceived(int peer, byte[] data, int length)
    {
        using var r = new BinaryReader(new MemoryStream(data, 0, length));
        var type = (MsgType)r.ReadByte();
        switch (type)
        {
            case MsgType.Welcome:
                r.ReadString();
                LocalNum = r.ReadInt32();
                _snapshot = new MemoryStream();
                break;
            case MsgType.Reject:
                CloseReason = r.ReadString();
                State = ClientState.Closed;
                break;
            case MsgType.SnapshotChunk:
                r.ReadInt32();
                r.ReadInt32();
                var chunk = r.ReadBytes(length - (int)r.BaseStream.Position);
                _snapshot?.Write(chunk);
                break;
            case MsgType.SnapshotDone:
                FinishSnapshot(r.ReadInt32(), r.ReadString());
                break;
            case MsgType.Delta:
                if (World != null) { ApplyDelta(r); DeltaApplied?.Invoke(); }
                break;
            case MsgType.DeltaCompressed:
                if (World != null)
                {
                    using var inflated = new System.IO.Compression.DeflateStream(new MemoryStream(data, 1, length - 1), System.IO.Compression.CompressionMode.Decompress);
                    using var plain = new MemoryStream();
                    inflated.CopyTo(plain);
                    plain.Position = 0;
                    ApplyDelta(new BinaryReader(plain));
                    DeltaApplied?.Invoke();
                }
                break;
            case MsgType.Result:
                var id = r.ReadUInt32();
                var ok = r.ReadBoolean();
                var error = r.ReadString();
                _pending.Remove(id);
                ResultReceived?.Invoke(id, new CommandResult(ok, error));
                break;
            case MsgType.Positions:
                ReadPositions(r);
                break;
            case MsgType.Chat:
                var from = r.ReadString();
                ChatReceived?.Invoke(from, r.ReadString());
                break;
            case MsgType.Notice:
                var key = r.ReadString();
                NoticeReceived?.Invoke(key, r.ReadString());
                break;
            case MsgType.Correct:
                PositionCorrected?.Invoke(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                break;
            case MsgType.Check:
                r.ReadInt64();
                if (World != null)
                {
                    // Se copia el estado en este hilo; el resumen se calcula aparte para no afectar al frame.
                    var expected = r.ReadString();
                    var bytes = WorldCodec.HashableBytes(World);
                    Task.Run(() =>
                    {
                        if (expected != WorldCodec.HashOf(bytes)) Interlocked.Increment(ref _hashMismatches);
                        Interlocked.Increment(ref _hashChecks);
                    });
                }
                break;
        }
    }

    private void FinishSnapshot(int expected, string hash)
    {
        if (_snapshot == null || _snapshot.Length != expected)
        {
            Close("reject_sync");
            return;
        }
        World = WorldCodec.Deserialize(Content, _snapshot.ToArray());
        foreach (var p in World.Players.Values) p.Connected = true;
        _snapshot = null;
        if (WorldCodec.Hash(World) != hash)
        {
            Close("reject_sync");
            return;
        }
        State = ClientState.InGame;
        foreach (var (id, command) in _pending) Transmit(id, command);
        WorldReady?.Invoke();
    }

    private void ApplyDelta(BinaryReader r)
    {
        var world = World!;
        var upgrades = world.Upgrades.Count;
        if (r.ReadBoolean()) WorldCodec.ReadGlobal(r, world);
        var structural = world.Upgrades.Count != upgrades;
        var n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++) structural |= world.ApplyReplicatedMachine(WorldCodec.ReadMachine(r));
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++) world.Machines.Remove(r.ReadInt32());
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++)
        {
            var l = WorldCodec.ReadLink(r, Content);
            if (world.Links.TryGetValue(l.Id, out var known)) l.SlotsVersion = known.SlotsVersion + 1;
            world.Links[l.Id] = l;
        }
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++) world.Links.Remove(r.ReadInt32());
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var d = WorldCodec.ReadDebris(r);
            structural |= !world.DebrisById.ContainsKey(d.Id);
            world.DebrisById[d.Id] = d;
        }
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++) world.DebrisById.Remove(r.ReadInt32());
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++)
        {
            var node = WorldCodec.ReadNode(r);
            world.Nodes[node.Id] = node;
        }
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var p = WorldCodec.ReadPlayer(r);
            world.Players[p.Key] = p;
        }
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++)
        {
            var node = WorldCodec.ReadRailNode(r);
            world.RailNodes[node.Id] = node;
        }
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++) world.RailNodes.Remove(r.ReadInt32());
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++)
        {
            var s = WorldCodec.ReadRailSegment(r);
            world.RailSegments[s.Id] = s;
        }
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++) world.RailSegments.Remove(r.ReadInt32());
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var t = WorldCodec.ReadTrain(r, id => world.Trains.GetValueOrDefault(id));
            structural |= !world.Trains.ContainsKey(t.Id);
            world.Trains[t.Id] = t;
        }
        n = r.Read7BitEncodedInt();
        structural |= n > 0;
        for (var i = 0; i < n; i++) world.Trains.Remove(r.ReadInt32());
        if (r.ReadBoolean()) WorldCodec.ReadShipments(r, world);
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++) WorldCodec.ReadSlotsCompact(r, world);
        if (n > 0) SlotDeltas++;
        if (structural)
        {
            StructuralDeltas++;
            world.Reindex();
        }
    }

    private void ReadPositions(BinaryReader r)
    {
        var tick = r.ReadInt64();
        if (tick < LastPositionsTick) return;
        LastPositionsTick = tick;
        int count = r.ReadByte();
        for (var i = 0; i < count; i++)
        {
            int num = r.ReadUInt16();
            // Se leen antes de invocar: con ?.Invoke sin suscriptores los argumentos no se evalúan y se perdería el hilo del mensaje.
            float x = r.ReadSingle(), y = r.ReadSingle(), z = r.ReadSingle(), yaw = r.ReadSingle();
            var action = r.ReadByte();
            RemoteMoved?.Invoke(num, x, y, z, yaw);
            RemoteAction?.Invoke(num, action);
        }
    }

    public void Dispose() => _transport.Dispose();
}
