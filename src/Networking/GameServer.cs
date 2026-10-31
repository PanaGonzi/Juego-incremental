using System.Diagnostics;
using Pluton.Persistence;
using Pluton.Simulation;

namespace Pluton.Networking;

public sealed class ServerOptions
{
    public int MaxPlayers = Protocol.MaxPlayers;
    public string Password = "";
    public string? HostKey;
    public int FlushEveryTicks = 2;
    public int CheckEveryTicks = 100;
    public int SimFlushEveryTicks = 10;
    public int PositionsEveryTicks = 1;
    public int AutosaveSeconds = 120;
    public string SaveSlot = "";
    public SaveStore? Store;
    public string JoinNoticeKey = "";
    public string JoinNoticeArg = "";
    public Action<string>? Log;
}

/// <summary>Sesión autoritativa: valida intenciones, avanza el mundo y replica cambios. Un único hilo la ejecuta.</summary>
public sealed class GameServer : IDisposable
{
    private sealed class Session
    {
        public int Peer;
        public PlayerState? Player;
        public bool Synced;
        public double Tokens = 80;
        public long LastRefillMs;
        public int Violations;
        public long LastMoveMs;
        public float LastX, LastZ;
        public int LastPlanet;
    }

    private sealed class RecentResults
    {
        public readonly Dictionary<uint, CommandResult> Map = new();
        public readonly Queue<uint> Order = new();
    }

    public readonly World World;
    private readonly ITransport _transport;
    private readonly ServerOptions _opt;
    private readonly Dictionary<int, Session> _sessions = new();
    private readonly Dictionary<string, RecentResults> _recent = new(StringComparer.Ordinal);
    private readonly List<Session> _pendingJoin = new();
    private readonly List<(int peer, long atMs)> _closing = new();
    private readonly PacketWriter _pw = new();
    private readonly MemoryStream _deltaStream = new();
    private readonly MemoryStream _compressed = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastAutosaveMs;
    private Task? _saveTask;

    public volatile bool Paused;
    public int ConnectedCount => _sessions.Values.Count(s => s.Player != null);
    public long CommandsApplied { get; private set; }
    public long CommandsRejected { get; private set; }
    public long Kicked { get; private set; }
    public long LastSnapshotBytes { get; private set; }
    public ITransport Transport => _transport;

    public GameServer(World world, ITransport transport, ServerOptions options)
    {
        World = world;
        _transport = transport;
        _opt = options;
        _transport.Connected += OnConnected;
        _transport.Received += OnReceived;
        _transport.Disconnected += OnDisconnected;
    }

    private void Log(string message) => _opt.Log?.Invoke(message);

    private void OnConnected(int peer)
    {
        _sessions[peer] = new Session { Peer = peer, LastRefillMs = _clock.ElapsedMilliseconds };
    }

    private void OnDisconnected(int peer, string reason)
    {
        if (!_sessions.Remove(peer, out var s)) return;
        _pendingJoin.Remove(s);
        if (s.Player != null)
        {
            s.Player.Connected = false;
            World.DirtyPlayers.Add(s.Player.Key);
            Log($"{s.Player.Name} desconectado ({reason})");
            Broadcast(MsgType.Notice, "notice_left", s.Player.Name);
        }
    }

    private void OnReceived(int peer, byte[] data, int length)
    {
        if (!_sessions.TryGetValue(peer, out var s)) return;
        if (length > Protocol.MaxClientPacket || length < 1) { Kick(s, "reject_invalid"); return; }
        try
        {
            using var r = new BinaryReader(new MemoryStream(data, 0, length));
            var type = (MsgType)r.ReadByte();
            if (s.Player == null)
            {
                if (type == MsgType.Hello) HandleHello(s, r);
                else Kick(s, "reject_invalid");
                return;
            }
            switch (type)
            {
                case MsgType.Command: HandleCommand(s, r); break;
                case MsgType.Move: HandleMove(s, r); break;
                case MsgType.ChatSend: HandleChat(s, r); break;
                case MsgType.SaveRequest:
                    if (s.Player.Role >= Role.Admin) SaveNow();
                    break;
                default: Violation(s); break;
            }
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or IOException or ArgumentException or OverflowException)
        {
            Log($"Paquete inválido de {peer}: {e.Message}");
            Kick(s, "reject_invalid");
        }
    }

    private void Violation(Session s)
    {
        if (++s.Violations > 20) Kick(s, "reject_invalid");
    }

    private void Kick(Session s, string reason)
    {
        Kicked++;
        CloseSession(s, reason);
    }

    /// <summary>
    /// Saca la sesión de inmediato y corta la conexión poco después: cortar en el acto
    /// descartaría el mensaje de rechazo que explica el motivo al jugador.
    /// </summary>
    private void CloseSession(Session s, string reason)
    {
        Reject(s.Peer, reason);
        _sessions.Remove(s.Peer);
        _pendingJoin.Remove(s);
        if (s.Player != null)
        {
            s.Player.Connected = false;
            World.DirtyPlayers.Add(s.Player.Key);
            s.Player = null;
        }
        _closing.Add((s.Peer, _clock.ElapsedMilliseconds + 500));
    }

    private void ProcessClosing()
    {
        if (_closing.Count == 0) return;
        var now = _clock.ElapsedMilliseconds;
        for (var i = _closing.Count - 1; i >= 0; i--)
        {
            if (now < _closing[i].atMs) continue;
            _transport.Disconnect(_closing[i].peer);
            _closing.RemoveAt(i);
        }
    }

    private void Reject(int peer, string reason)
    {
        _pw.Begin(MsgType.Reject).Write(reason);
        _pw.Send(_transport, peer, Channel.Reliable);
    }

    private static string CleanName(string name)
    {
        var chars = name.Where(c => !char.IsControl(c)).Take(Protocol.MaxNameLength).ToArray();
        var clean = new string(chars).Trim();
        return clean.Length == 0 ? "Ingeniero" : clean;
    }

    private void HandleHello(Session s, BinaryReader r)
    {
        var protocol = r.ReadInt32();
        var gameVersion = r.ReadString();
        var revision = r.ReadString();
        var key = r.ReadString();
        var name = CleanName(r.ReadString());
        var password = r.ReadString();

        if (protocol != Protocol.Version) { RejectAndClose(s, "reject_version"); return; }
        if (revision != World.Content.Revision) { RejectAndClose(s, "reject_content"); return; }
        if (key.Length is < 16 or > 64) { RejectAndClose(s, "reject_invalid"); return; }
        if (_opt.Password.Length > 0 && password != _opt.Password) { RejectAndClose(s, "reject_password"); return; }

        // La misma identidad sustituye a su sesión anterior en lugar de ocupar otra plaza.
        var previous = _sessions.Values.FirstOrDefault(o => o != s && o.Player?.Key == key);
        if (previous == null && ConnectedCount >= _opt.MaxPlayers) { RejectAndClose(s, "reject_full"); return; }
        if (previous != null) CloseSession(previous, "reject_replaced");

        if (!World.Players.TryGetValue(key, out var player))
        {
            var planet = World.Content.Planets[0];
            var cell = World.Content.Balance.CellSizeMm / 1000f;
            player = new PlayerState
            {
                Key = key,
                Num = World.NextPlayerNum++,
                Planet = planet.Id,
                X = (planet.Spawn.X + World.Players.Count % 4) * cell,
                Y = 1f,
                Z = planet.Spawn.Z * cell,
            };
            World.Players[key] = player;
        }
        player.Name = name;
        player.Connected = true;
        if (World.OwnerKey.Length == 0 || key == _opt.HostKey) World.OwnerKey = _opt.HostKey ?? key;
        if (key == World.OwnerKey) player.Role = Role.Owner;

        s.Player = player;
        s.LastX = player.X;
        s.LastZ = player.Z;
        s.LastPlanet = player.Planet;
        s.LastMoveMs = _clock.ElapsedMilliseconds;
        World.DirtyPlayers.Add(key);
        _pendingJoin.Add(s);
        Log($"{name} entra ({ConnectedCount}/{_opt.MaxPlayers})");
        Broadcast(MsgType.Notice, "notice_joined", name);
    }

    private void RejectAndClose(Session s, string reason)
    {
        Log($"Rechazado {s.Peer}: {reason}");
        CloseSession(s, reason);
    }

    private bool Spend(Session s)
    {
        var now = _clock.ElapsedMilliseconds;
        s.Tokens = Math.Min(80, s.Tokens + (now - s.LastRefillMs) * 0.04);
        s.LastRefillMs = now;
        if (s.Tokens < 1) { Violation(s); return false; }
        s.Tokens -= 1;
        return true;
    }

    private void HandleCommand(Session s, BinaryReader r)
    {
        var request = r.ReadUInt32();
        var command = CommandCodec.Read(r);
        if (command.Text.Length > 64) { Violation(s); return; }

        var recent = _recent.TryGetValue(s.Player!.Key, out var existing)
            ? existing
            : _recent[s.Player.Key] = new RecentResults();
        CommandResult result;
        if (recent.Map.TryGetValue(request, out var cached))
        {
            result = cached; // reintento: misma respuesta, sin volver a aplicar
        }
        else if (!Spend(s))
        {
            result = CommandResult.Fail("err_rate");
        }
        else
        {
            if (command.Type == CommandType.Kick)
                result = HandleKick(s, command.Text);
            else
                result = World.Apply(s.Player, command);
            if (result.Ok) CommandsApplied++; else CommandsRejected++;
            recent.Map[request] = result;
            recent.Order.Enqueue(request);
            if (recent.Order.Count > 256) recent.Map.Remove(recent.Order.Dequeue());
        }

        var w = _pw.Begin(MsgType.Result);
        w.Write(request);
        w.Write(result.Ok);
        w.Write(result.Error);
        _pw.Send(_transport, s.Peer, Channel.Reliable);
    }

    private CommandResult HandleKick(Session s, string targetKey)
    {
        if (s.Player!.Role < Role.Admin) return CommandResult.Fail("err_permission");
        var target = _sessions.Values.FirstOrDefault(o => o.Player?.Key == targetKey);
        if (target?.Player == null) return CommandResult.Fail("err_gone");
        if (target.Player.Role >= s.Player.Role) return CommandResult.Fail("err_permission");
        Kick(target, "reject_kicked");
        return CommandResult.Success;
    }

    private void HandleMove(Session s, BinaryReader r)
    {
        var x = r.ReadSingle();
        var y = r.ReadSingle();
        var z = r.ReadSingle();
        var yaw = r.ReadSingle();
        var p = s.Player!;
        if (p.TransitTo >= 0) return; // la lanzadera manda: se ignora hasta llegar
        if (p.Planet != s.LastPlanet)
        {
            // Acaba de llegar: la referencia de velocidad es el punto de llegada.
            s.LastPlanet = p.Planet;
            s.LastX = p.X;
            s.LastZ = p.Z;
            s.LastMoveMs = _clock.ElapsedMilliseconds;
        }
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(yaw)) { Violation(s); return; }

        var now = _clock.ElapsedMilliseconds;
        var dt = Math.Max(0.02f, (now - s.LastMoveMs) / 1000f);
        s.LastMoveMs = now;
        var dx = x - s.LastX;
        var dz = z - s.LastZ;
        var allowed = Protocol.MaxMoveSpeed * Math.Min(dt, 1.5f) + 2.5f;
        var half = World.Content.Planets.First(pl => pl.Id == p.Planet).HalfSizeCells * World.Content.Balance.CellSizeMm / 1000f;
        if (dx * dx + dz * dz > allowed * allowed || Math.Abs(x) > half + 40 || Math.Abs(z) > half + 40 || y is < -20 or > 200)
        {
            var w = _pw.Begin(MsgType.Correct);
            w.Write(p.X); w.Write(p.Y); w.Write(p.Z);
            _pw.Send(_transport, s.Peer, Channel.Reliable);
            return;
        }
        s.LastX = x;
        s.LastZ = z;
        p.X = x; p.Y = y; p.Z = z; p.Yaw = yaw;
    }

    private void HandleChat(Session s, BinaryReader r)
    {
        var text = new string(r.ReadString().Where(c => !char.IsControl(c)).Take(Protocol.MaxChatLength).ToArray()).Trim();
        if (text.Length == 0 || !Spend(s)) return;
        var w = _pw.Begin(MsgType.Chat);
        w.Write(s.Player!.Name);
        w.Write(text);
        SendAll(Channel.Reliable);
    }

    private void Broadcast(MsgType type, string key, string arg)
    {
        var w = _pw.Begin(type);
        w.Write(key);
        w.Write(arg);
        SendAll(Channel.Reliable);
    }

    private void SendAll(Channel channel)
    {
        foreach (var s in _sessions.Values)
            if (s.Synced) _pw.Send(_transport, s.Peer, channel);
    }

    /// <summary>Procesa red y, si procede, avanza un tick. La llama el bucle del hilo del servidor.</summary>
    public void Tick()
    {
        _transport.Poll();
        ProcessClosing();
        if (!Paused) World.Step();
        if (_pendingJoin.Count > 0) SendSnapshots();
        else if (World.Tick % _opt.FlushEveryTicks == 0) FlushDeltas();
        if (World.Tick % _opt.PositionsEveryTicks == 0) SendPositions();
        if (World.Tick % _opt.CheckEveryTicks == 0) SendCheck();
        Autosave();
    }

    private void FlushDeltas(bool everything = false)
    {
        // Lo cambiado por la simulación espera a su ciclo; lo pedido por un jugador sale de inmediato.
        if (everything || World.Tick % _opt.SimFlushEveryTicks == 0 || _opt.FlushEveryTicks >= _opt.SimFlushEveryTicks)
        {
            World.DirtyMachines.UnionWith(World.DirtySimMachines);
            World.DirtySimMachines.Clear();
            World.DirtyTrains.UnionWith(World.DirtySimTrains);
            World.DirtySimTrains.Clear();
            World.DirtyGlobal |= World.DirtyGlobalSim;
            World.DirtyGlobalSim = false;
        }
        if (World.DirtyMachines.Count == 0 && World.DirtyDebris.Count == 0 && World.DirtyNodes.Count == 0 &&
            World.DirtyLinks.Count == 0 && World.DirtyPlayers.Count == 0 && !World.DirtyGlobal &&
            World.RemovedMachines.Count == 0 && World.RemovedDebris.Count == 0 && World.RemovedLinks.Count == 0 &&
            World.DirtyRailNodes.Count == 0 && World.DirtyRailSegments.Count == 0 && World.DirtyTrains.Count == 0 &&
            World.RemovedRailNodes.Count == 0 && World.RemovedRailSegments.Count == 0 && World.RemovedTrains.Count == 0 && !World.DirtyShipments)
            return;

        _deltaStream.SetLength(0);
        using (var w = new BinaryWriter(_deltaStream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)MsgType.Delta);
            w.Write(World.DirtyGlobal);
            if (World.DirtyGlobal) WorldCodec.WriteGlobal(w, World);
            w.Write7BitEncodedInt(World.DirtyMachines.Count);
            foreach (var id in World.DirtyMachines) WorldCodec.WriteMachine(w, World.Machines[id]);
            w.Write7BitEncodedInt(World.RemovedMachines.Count);
            foreach (var (_, id) in World.RemovedMachines) w.Write(id);
            w.Write7BitEncodedInt(World.DirtyLinks.Count);
            foreach (var id in World.DirtyLinks) WorldCodec.WriteLink(w, World.Links[id]);
            w.Write7BitEncodedInt(World.RemovedLinks.Count);
            foreach (var (_, id) in World.RemovedLinks) w.Write(id);
            w.Write7BitEncodedInt(World.DirtyDebris.Count);
            foreach (var id in World.DirtyDebris) WorldCodec.WriteDebris(w, World.DebrisById[id]);
            w.Write7BitEncodedInt(World.RemovedDebris.Count);
            foreach (var (_, id) in World.RemovedDebris) w.Write(id);
            w.Write7BitEncodedInt(World.DirtyNodes.Count);
            foreach (var id in World.DirtyNodes) WorldCodec.WriteNode(w, World.Nodes[id]);
            w.Write7BitEncodedInt(World.DirtyPlayers.Count);
            foreach (var key in World.DirtyPlayers) WorldCodec.WritePlayer(w, World.Players[key]);
            w.Write7BitEncodedInt(World.DirtyRailNodes.Count);
            foreach (var id in World.DirtyRailNodes) WorldCodec.WriteRailNode(w, World.RailNodes[id]);
            w.Write7BitEncodedInt(World.RemovedRailNodes.Count);
            foreach (var id in World.RemovedRailNodes) w.Write(id);
            w.Write7BitEncodedInt(World.DirtyRailSegments.Count);
            foreach (var id in World.DirtyRailSegments) WorldCodec.WriteRailSegment(w, World.RailSegments[id]);
            w.Write7BitEncodedInt(World.RemovedRailSegments.Count);
            foreach (var id in World.RemovedRailSegments) w.Write(id);
            w.Write7BitEncodedInt(World.DirtyTrains.Count);
            foreach (var id in World.DirtyTrains) WorldCodec.WriteTrain(w, World.Trains[id], withPath: World.DirtyTrainPaths.Contains(id));
            w.Write7BitEncodedInt(World.RemovedTrains.Count);
            foreach (var id in World.RemovedTrains) w.Write(id);
            w.Write(World.DirtyShipments);
            if (World.DirtyShipments) WorldCodec.WriteShipments(w, World);
        }
        var buffer = _deltaStream.GetBuffer();
        var length = (int)_deltaStream.Length;
        // Se comprime una vez y se reutiliza para todos: el coste no crece con los clientes.
        if (length > 200)
        {
            _compressed.SetLength(0);
            _compressed.WriteByte((byte)MsgType.DeltaCompressed);
            using (var deflate = new System.IO.Compression.DeflateStream(_compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                deflate.Write(buffer, 1, length - 1);
            buffer = _compressed.GetBuffer();
            length = (int)_compressed.Length;
        }
        foreach (var s in _sessions.Values)
            if (s.Synced) _transport.Send(s.Peer, buffer, length, Channel.Reliable);

        World.DirtyMachines.Clear();
        World.DirtyDebris.Clear();
        World.DirtyNodes.Clear();
        World.DirtyLinks.Clear();
        World.DirtyPlayers.Clear();
        World.RemovedMachines.Clear();
        World.RemovedDebris.Clear();
        World.RemovedLinks.Clear();
        World.DirtyRailNodes.Clear();
        World.DirtyRailSegments.Clear();
        foreach (var id in World.DirtyTrains) World.DirtyTrainPaths.Remove(id);
        World.DirtyTrains.Clear();
        World.RemovedRailNodes.Clear();
        World.RemovedRailSegments.Clear();
        World.RemovedTrains.Clear();
        World.DirtyShipments = false;
        World.DirtyGlobal = false;
    }

    private void SendSnapshots()
    {
        // Justo tras vaciar los cambios: la instantánea corresponde a este tick y lo siguiente llega como delta.
        FlushDeltas(everything: true);
        var bytes = WorldCodec.Serialize(World);
        if (bytes.Length > Protocol.MaxSnapshotBytes) throw new InvalidOperationException("Instantánea demasiado grande");
        LastSnapshotBytes = bytes.Length;
        var hash = WorldCodec.Hash(World);
        foreach (var s in _pendingJoin)
        {
            var w = _pw.Begin(MsgType.Welcome);
            w.Write(s.Player!.Key);
            w.Write(s.Player.Num);
            _pw.Send(_transport, s.Peer, Channel.Reliable);

            var total = (bytes.Length + Protocol.SnapshotChunkSize - 1) / Protocol.SnapshotChunkSize;
            for (var i = 0; i < total; i++)
            {
                var offset = i * Protocol.SnapshotChunkSize;
                var size = Math.Min(Protocol.SnapshotChunkSize, bytes.Length - offset);
                w = _pw.Begin(MsgType.SnapshotChunk);
                w.Write(i);
                w.Write(total);
                w.Write(bytes, offset, size);
                _pw.Send(_transport, s.Peer, Channel.Reliable);
            }
            w = _pw.Begin(MsgType.SnapshotDone);
            w.Write(bytes.Length);
            w.Write(hash);
            _pw.Send(_transport, s.Peer, Channel.Reliable);
            s.Synced = true;
            if (_opt.JoinNoticeKey.Length > 0)
            {
                w = _pw.Begin(MsgType.Notice);
                w.Write(_opt.JoinNoticeKey);
                w.Write(_opt.JoinNoticeArg);
                _pw.Send(_transport, s.Peer, Channel.Reliable);
                _opt.JoinNoticeKey = "";
            }
        }
        _pendingJoin.Clear();
    }

    private void SendPositions()
    {
        foreach (var target in _sessions.Values)
        {
            if (!target.Synced) continue;
            var w = _pw.Begin(MsgType.Positions);
            w.Write(World.Tick);
            var countPos = _pw.Length;
            byte count = 0;
            w.Write(count);
            foreach (var s in _sessions.Values)
            {
                if (s == target || s.Player == null || s.Player.Planet != target.Player!.Planet || s.Player.TransitTo >= 0 || target.Player.TransitTo >= 0) continue;
                w.Write((ushort)s.Player.Num);
                w.Write(s.Player.X);
                w.Write(s.Player.Y);
                w.Write(s.Player.Z);
                w.Write(s.Player.Yaw);
                count++;
            }
            if (count == 0) continue;
            _pw.Buffer[countPos] = count;
            _pw.Send(_transport, target.Peer, Channel.Unreliable);
        }
    }

    private void SendCheck()
    {
        FlushDeltas(everything: true);
        var hash = WorldCodec.Hash(World);
        var w = _pw.Begin(MsgType.Check);
        w.Write(World.Tick);
        w.Write(hash);
        SendAll(Channel.Reliable);
    }

    public int RoundTrip(string playerKey)
    {
        foreach (var s in _sessions.Values)
            if (s.Player?.Key == playerKey) return _transport.RoundTripMs(s.Peer);
        return 0;
    }

    private void Autosave()
    {
        if (_opt.Store == null || _opt.SaveSlot.Length == 0 || _opt.AutosaveSeconds <= 0) return;
        var now = _clock.ElapsedMilliseconds;
        if (now - _lastAutosaveMs < _opt.AutosaveSeconds * 1000L) return;
        _lastAutosaveMs = now;
        SaveNow();
    }

    /// <summary>Serializa en este tick y escribe en segundo plano para no retrasar la simulación.</summary>
    public void SaveNow()
    {
        if (_opt.Store == null || _opt.SaveSlot.Length == 0) return;
        if (_saveTask is { IsCompleted: false }) return;
        World.SavedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bytes = WorldCodec.Serialize(World);
        var revision = World.Content.Revision;
        var tick = World.Tick;
        var store = _opt.Store;
        var slot = _opt.SaveSlot;
        _saveTask = Task.Run(() =>
        {
            try { store.Write(slot, SaveStore.Encode(bytes, revision, tick, DateTime.UtcNow)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Log("Fallo al guardar: " + e.Message); }
        });
    }

    /// <summary>Tras un fallo interno: copia en una ranura aparte, sin sustituir el último guardado válido.</summary>
    public void EmergencySave()
    {
        if (_opt.Store == null || _opt.SaveSlot.Length == 0) return;
        try { _opt.Store.Save(_opt.SaveSlot + "-emergencia", World); }
        catch (Exception e) { Log("Fallo en el guardado de emergencia: " + e.Message); }
    }

    public void SaveBlocking()
    {
        _saveTask?.Wait();
        if (_opt.Store != null && _opt.SaveSlot.Length > 0) _opt.Store.Save(_opt.SaveSlot, World);
    }

    public void NotifyShutdown()
    {
        foreach (var s in _sessions.Values) Reject(s.Peer, "reject_shutdown");
        Thread.Sleep(150);
        _transport.Poll();
    }

    public void Dispose() => _transport.Dispose();
}

/// <summary>Bucle de tick fijo con métricas. Compartido por el anfitrión integrado y el servidor dedicado.</summary>
public sealed class ServerRunner : IDisposable
{
    public readonly GameServer Server;
    private readonly Thread _thread;
    private volatile bool _running = true;
    private readonly object _metricsLock = new();
    private readonly List<double> _tickMs = new();
    public long TicksLate { get; private set; }
    public Exception? Fault { get; private set; }

    public ServerRunner(GameServer server)
    {
        Server = server;
        _thread = new Thread(Loop) { IsBackground = true, Name = "PlutonServer" };
    }

    public void Start() => _thread.Start();

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        var stepMs = 1000.0 / Server.World.Content.Balance.TicksPerSecond;
        var next = 0.0;
        try
        {
            while (_running)
            {
                var now = clock.Elapsed.TotalMilliseconds;
                if (now < next)
                {
                    Thread.Sleep(Math.Max(1, (int)(next - now)));
                    continue;
                }
                var t0 = clock.Elapsed.TotalMilliseconds;
                Server.Tick();
                var cost = clock.Elapsed.TotalMilliseconds - t0;
                lock (_metricsLock) _tickMs.Add(cost);
                next += stepMs;
                if (clock.Elapsed.TotalMilliseconds > next + stepMs * 5)
                {
                    TicksLate++;
                    next = clock.Elapsed.TotalMilliseconds; // sin espiral: se registra el retraso y se sigue
                }
            }
        }
        catch (Exception e)
        {
            Fault = e;
            Server.EmergencySave();
        }
    }

    /// <summary>Devuelve y reinicia las duraciones de tick medidas desde la última lectura.</summary>
    public double[] DrainTickTimes()
    {
        lock (_metricsLock)
        {
            var copy = _tickMs.ToArray();
            _tickMs.Clear();
            return copy;
        }
    }

    public void Stop(bool save)
    {
        _running = false;
        if (_thread.IsAlive) _thread.Join(5000);
        // Tras un fallo el mundo podría ser incoherente: no sustituye al último guardado válido.
        if (save && Fault == null) Server.SaveBlocking();
        Server.NotifyShutdown();
    }

    public void Dispose() => Server.Dispose();
}
