using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;

namespace Pluton.Networking;

public enum Channel : byte { Reliable = 0, Unreliable = 1 }

/// <summary>
/// Frontera entre las reglas de sesión y el medio de transporte (LAN/IP, memoria local y, en el futuro, Steam).
/// Los eventos se disparan únicamente dentro de Poll, en el hilo que lo llama.
/// </summary>
public interface ITransport : IDisposable
{
    event Action<int>? Connected;
    event Action<int, byte[], int>? Received;
    event Action<int, string>? Disconnected;
    void Poll();
    void Send(int peer, byte[] data, int length, Channel channel);
    void Disconnect(int peer);
    int RoundTripMs(int peer);
    long BytesSent { get; }
    long BytesReceived { get; }
}

public sealed class LiteNetTransport : ITransport
{
    public const string ConnectionKey = "pluton-v1";
    private const int MaxConnections = 32;

    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<int, NetPeer> _peers = new();
    private byte[] _buffer = new byte[2048];

    public event Action<int>? Connected;
    public event Action<int, byte[], int>? Received;
    public event Action<int, string>? Disconnected;

    private LiteNetTransport()
    {
        _net = new NetManager(_listener)
        {
            ChannelsCount = 2,
            AutoRecycle = true,
            DisconnectTimeout = 8000,
            EnableStatistics = true,
            IPv6Enabled = false,
        };
        _listener.ConnectionRequestEvent += request =>
        {
            if (_net.ConnectedPeersCount < MaxConnections) request.AcceptIfKey(ConnectionKey);
            else request.Reject();
        };
        _listener.PeerConnectedEvent += peer =>
        {
            _peers[peer.Id] = peer;
            Connected?.Invoke(peer.Id);
        };
        _listener.PeerDisconnectedEvent += (peer, info) =>
        {
            _peers.Remove(peer.Id);
            Disconnected?.Invoke(peer.Id, info.Reason.ToString());
        };
        _listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
        {
            var length = reader.AvailableBytes;
            if (_buffer.Length < length) _buffer = new byte[length * 2];
            reader.GetBytes(_buffer, length);
            Received?.Invoke(peer.Id, _buffer, length);
        };
    }

    public static LiteNetTransport Listen(int port)
    {
        var t = new LiteNetTransport();
        if (!t._net.Start(port)) throw new SocketException((int)SocketError.AddressAlreadyInUse);
        return t;
    }

    public static LiteNetTransport Dial(string host, int port)
    {
        var t = new LiteNetTransport();
        t._net.Start();
        t._net.Connect(host, port, ConnectionKey);
        return t;
    }

    public void Poll() => _net.PollEvents();

    public void Send(int peer, byte[] data, int length, Channel channel)
    {
        if (!_peers.TryGetValue(peer, out var p)) return;
        var method = channel == Channel.Reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced;
        p.Send(data, 0, length, (byte)channel, method);
    }

    public void Disconnect(int peer)
    {
        if (_peers.TryGetValue(peer, out var p)) p.Disconnect();
    }

    public int RoundTripMs(int peer) => _peers.TryGetValue(peer, out var p) ? p.RoundTripTime : 0;
    public long BytesSent => _net.Statistics.BytesSent;
    public long BytesReceived => _net.Statistics.BytesReceived;

    public void Dispose() => _net.Stop();
}

/// <summary>Conexión en memoria para modo individual y anfitrión: sin sockets ni Internet.</summary>
public sealed class LoopbackHub
{
    public readonly LoopbackEnd ServerEnd;
    public readonly LoopbackEnd ClientEnd;

    public LoopbackHub()
    {
        ServerEnd = new LoopbackEnd();
        ClientEnd = new LoopbackEnd();
        ServerEnd.Other = ClientEnd;
        ClientEnd.Other = ServerEnd;
    }

    public void Open()
    {
        ServerEnd.Enqueue(LoopbackEnd.Kind.Connect, null);
        ClientEnd.Enqueue(LoopbackEnd.Kind.Connect, null);
    }
}

public sealed class LoopbackEnd : ITransport
{
    public const int LocalPeer = 1000;
    public enum Kind { Connect, Data, Close }

    private readonly ConcurrentQueue<(Kind kind, byte[]? data)> _inbox = new();
    internal LoopbackEnd? Other;
    private long _sent, _received;

    public event Action<int>? Connected;
    public event Action<int, byte[], int>? Received;
    public event Action<int, string>? Disconnected;

    internal void Enqueue(Kind kind, byte[]? data) => _inbox.Enqueue((kind, data));

    public void Poll()
    {
        while (_inbox.TryDequeue(out var e))
        {
            switch (e.kind)
            {
                case Kind.Connect: Connected?.Invoke(LocalPeer); break;
                case Kind.Close: Disconnected?.Invoke(LocalPeer, "local"); break;
                default:
                    _received += e.data!.Length;
                    Received?.Invoke(LocalPeer, e.data, e.data.Length);
                    break;
            }
        }
    }

    public void Send(int peer, byte[] data, int length, Channel channel)
    {
        var copy = new byte[length];
        Buffer.BlockCopy(data, 0, copy, 0, length);
        _sent += length;
        Other?.Enqueue(Kind.Data, copy);
    }

    public void Disconnect(int peer)
    {
        Other?.Enqueue(Kind.Close, null);
        Enqueue(Kind.Close, null);
    }

    public int RoundTripMs(int peer) => 0;
    public long BytesSent => _sent;
    public long BytesReceived => _received;
    public void Dispose() { }
}

/// <summary>Reúne varios transportes de servidor (anfitrión: memoria local + red) bajo un único espacio de pares.</summary>
public sealed class CompositeTransport : ITransport
{
    private readonly ITransport _a;
    private readonly ITransport _b;
    private const int Offset = 100000;

    public event Action<int>? Connected;
    public event Action<int, byte[], int>? Received;
    public event Action<int, string>? Disconnected;

    public CompositeTransport(ITransport local, ITransport network)
    {
        _a = local;
        _b = network;
        _a.Connected += p => Connected?.Invoke(p);
        _a.Received += (p, d, l) => Received?.Invoke(p, d, l);
        _a.Disconnected += (p, r) => Disconnected?.Invoke(p, r);
        _b.Connected += p => Connected?.Invoke(p + Offset);
        _b.Received += (p, d, l) => Received?.Invoke(p + Offset, d, l);
        _b.Disconnected += (p, r) => Disconnected?.Invoke(p + Offset, r);
    }

    public void Poll() { _a.Poll(); _b.Poll(); }

    public void Send(int peer, byte[] data, int length, Channel channel)
    {
        if (peer >= Offset) _b.Send(peer - Offset, data, length, channel);
        else _a.Send(peer, data, length, channel);
    }

    public void Disconnect(int peer)
    {
        if (peer >= Offset) _b.Disconnect(peer - Offset);
        else _a.Disconnect(peer);
    }

    public int RoundTripMs(int peer) => peer >= Offset ? _b.RoundTripMs(peer - Offset) : _a.RoundTripMs(peer);
    public long BytesSent => _a.BytesSent + _b.BytesSent;
    public long BytesReceived => _a.BytesReceived + _b.BytesReceived;
    public void Dispose() { _a.Dispose(); _b.Dispose(); }
}
