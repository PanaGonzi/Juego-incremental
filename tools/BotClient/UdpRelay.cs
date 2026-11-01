using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

/// <summary>
/// Relé UDP real entre un cliente y el servidor: retrasa y pierde datagramas por sentido.
/// Las opciones de simulación de LiteNetLib no actúan en su paquete de release.
/// </summary>
sealed class UdpRelay : IDisposable
{
    private readonly UdpClient _front = new(new IPEndPoint(IPAddress.Loopback, 0));
    private readonly UdpClient _back = new();
    private readonly IPEndPoint _server;
    private IPEndPoint? _client;
    private readonly int _delayMs, _jitterMs, _lossPercent;
    private readonly Random _rng;
    private readonly ConcurrentQueue<(long due, byte[] data, bool toServer)> _queue = new();
    private readonly PriorityQueue<(byte[] data, bool toServer), long> _pending = new();
    private volatile bool _running = true;
    public long Dropped;
    public long Forwarded;
    /// <summary>Si existe, cada datagrama queda anotado con su propiedad y secuencia de LiteNetLib (BUG-005).</summary>
    public System.IO.StreamWriter? Trace;
    public int LocalPort => ((IPEndPoint)_front.Client.LocalEndPoint!).Port;

    public UdpRelay(string host, int port, int rttMs, int jitterMs, int lossPercent, int seed)
    {
        _server = new IPEndPoint(IPAddress.Parse(host), port);
        _back.Connect(_server);
        _delayMs = rttMs / 2;
        _jitterMs = jitterMs;
        _lossPercent = lossPercent;
        _rng = new Random(seed);
        new Thread(() => Receive(_front, true)) { IsBackground = true }.Start();
        new Thread(() => Receive(_back, false)) { IsBackground = true }.Start();
        new Thread(Pump) { IsBackground = true }.Start();
    }

    private void Receive(UdpClient socket, bool toServer)
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_running)
        {
            try
            {
                var data = socket.Receive(ref any);
                if (toServer) _client = any;
                int roll, jitter;
                lock (_rng)
                {
                    roll = _rng.Next(100);
                    jitter = _jitterMs > 0 ? _rng.Next(_jitterMs + 1) : 0;
                }
                var dropped = roll < _lossPercent;
                Note(data, toServer, dropped);
                if (dropped)
                {
                    Interlocked.Increment(ref Dropped);
                    continue;
                }
                _queue.Enqueue((Environment.TickCount64 + _delayMs + jitter, data, toServer));
            }
            catch (SocketException) when (_running) { }
            catch (Exception e) when (e is SocketException or ObjectDisposedException) { return; }
        }
    }

    private void Note(byte[] data, bool toServer, bool dropped)
    {
        if (Trace is not { } trace || data.Length < 1) return;
        var stamp = System.Diagnostics.Stopwatch.GetTimestamp();
        var side = toServer ? 'C' : 'S';
        void Line(int at, int length)
        {
            var sequence = length >= 3 ? BitConverter.ToUInt16(data, at + 1) : 0;
            var channel = length >= 4 ? data[at + 3] : 0;
            lock (trace) trace.WriteLine($"{stamp},{side},{(dropped ? 1 : 0)},{data[at] & 0x1F},{sequence},{channel},{length}");
        }
        const int Merged = 13;
        if ((data[0] & 0x1F) != Merged)
        {
            Line(0, data.Length);
            return;
        }
        for (var at = 1; at + 2 < data.Length;)
        {
            int size = BitConverter.ToUInt16(data, at);
            at += 2;
            if (size == 0 || at + size > data.Length) break;
            Line(at, size);
            at += size;
        }
    }

    private void Pump()
    {
        while (_running)
        {
            while (_queue.TryDequeue(out var e)) _pending.Enqueue((e.data, e.toServer), e.due);
            var now = Environment.TickCount64;
            while (_pending.TryPeek(out var item, out var due) && due <= now)
            {
                _pending.Dequeue();
                try
                {
                    if (item.toServer) _back.Send(item.data, item.data.Length);
                    else if (_client != null) _front.Send(item.data, item.data.Length, _client);
                    Forwarded++;
                }
                catch (Exception e) when (e is SocketException or ObjectDisposedException) { }
            }
            Thread.Sleep(1);
        }
    }

    public void Dispose()
    {
        _running = false;
        _front.Dispose();
        _back.Dispose();
    }
}
