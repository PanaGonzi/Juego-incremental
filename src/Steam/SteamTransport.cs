using System.Runtime.InteropServices;
using Pluton.Networking;
using Steamworks;

namespace Pluton.Steam;

/// <summary>
/// Transporte sobre Steam Networking Sockets: el mismo protocolo del juego, con relay de Valve cuando conecta por P2P.
/// Quien cree esta clase es responsable de haber inicializado SteamAPI y de llamar a Poll en un único hilo.
/// </summary>
public sealed class SteamTransport : ITransport
{
    private const int SendReliable = 8;
    private const int SendUnreliableNoNagle = 1;

    private readonly Dictionary<int, HSteamNetConnection> _peers = new();
    private readonly Dictionary<uint, int> _byHandle = new();
    private readonly List<int> _justClosed = new();
    private readonly IntPtr[] _messages = new IntPtr[64];
    private readonly Callback<SteamNetConnectionStatusChangedCallback_t>? _statusCallback;
    private readonly bool _pumpCallbacks;
    private HSteamListenSocket _listen = HSteamListenSocket.Invalid;
    private byte[] _buffer = new byte[4096];
    private byte[] _sendBuffer = new byte[4096];
    private GCHandle _sendPin;
    private int _nextPeer = 1;
    private long _sent, _received;

    public event Action<int>? Connected;
    public event Action<int, byte[], int>? Received;
    public event Action<int, string>? Disconnected;

    private SteamTransport(bool callbacks)
    {
        _pumpCallbacks = callbacks;
        if (callbacks) _statusCallback = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatusChanged);
    }

    /// <summary>Anfitrión accesible por SteamID a través de la red de relays.</summary>
    public static SteamTransport Listen(int virtualPort = 0)
    {
        var t = new SteamTransport(callbacks: true);
        t._listen = SteamNetworkingSockets.CreateListenSocketP2P(virtualPort, 0, Array.Empty<SteamNetworkingConfigValue_t>());
        return t;
    }

    public static SteamTransport Dial(CSteamID host, int virtualPort = 0)
    {
        var t = new SteamTransport(callbacks: true);
        var identity = new SteamNetworkingIdentity();
        identity.SetSteamID(host);
        t.Add(SteamNetworkingSockets.ConnectP2P(ref identity, virtualPort, 0, Array.Empty<SteamNetworkingConfigValue_t>()), announce: false);
        return t;
    }

    /// <summary>Conexiones ya abiertas (pares locales de prueba): se anuncian en el primer Poll.</summary>
    public static SteamTransport FromConnections(IEnumerable<HSteamNetConnection> connections)
    {
        var t = new SteamTransport(callbacks: false);
        foreach (var c in connections) t.Add(c, announce: false);
        t._pendingAnnounce = true;
        return t;
    }

    private bool _pendingAnnounce;

    private int Add(HSteamNetConnection connection, bool announce)
    {
        var peer = _nextPeer++;
        _peers[peer] = connection;
        _byHandle[connection.m_HSteamNetConnection] = peer;
        if (announce) Connected?.Invoke(peer);
        return peer;
    }

    private void OnStatusChanged(SteamNetConnectionStatusChangedCallback_t e)
    {
        var handle = e.m_hConn.m_HSteamNetConnection;
        switch (e.m_info.m_eState)
        {
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting:
                // Solo el anfitrión acepta conexiones entrantes; el saludo del juego valida al jugador.
                if (e.m_info.m_hListenSocket != HSteamListenSocket.Invalid)
                    SteamNetworkingSockets.AcceptConnection(e.m_hConn);
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected:
                if (_byHandle.TryGetValue(handle, out var known)) Connected?.Invoke(known);
                else Add(e.m_hConn, announce: true);
                break;
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer:
            case ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally:
                if (_byHandle.Remove(handle, out var peer))
                {
                    _peers.Remove(peer);
                    Disconnected?.Invoke(peer, e.m_info.m_szEndDebug ?? e.m_info.m_eState.ToString());
                }
                SteamNetworkingSockets.CloseConnection(e.m_hConn, 0, "", false);
                break;
        }
    }

    public void Poll()
    {
        if (_pumpCallbacks) SteamAPI.RunCallbacks();
        if (_pendingAnnounce)
        {
            _pendingAnnounce = false;
            foreach (var peer in _peers.Keys.ToList()) Connected?.Invoke(peer);
        }
        foreach (var (peer, connection) in _peers.ToList())
        {
            if (!_pumpCallbacks && SteamNetworkingSockets.GetConnectionInfo(connection, out var info) &&
                info.m_eState is ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
                    or ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                // Los pares locales no reciben callbacks de estado: se consulta directamente.
                _peers.Remove(peer);
                _byHandle.Remove(connection.m_HSteamNetConnection);
                SteamNetworkingSockets.CloseConnection(connection, 0, "", false);
                Disconnected?.Invoke(peer, info.m_szEndDebug ?? "cerrada");
                continue;
            }
            while (_peers.ContainsKey(peer))
            {
                var count = SteamNetworkingSockets.ReceiveMessagesOnConnection(connection, _messages, _messages.Length);
                if (count <= 0) break;
                for (var i = 0; i < count; i++)
                {
                    var message = SteamNetworkingMessage_t.FromIntPtr(_messages[i]);
                    var length = message.m_cbSize;
                    if (_buffer.Length < length) _buffer = new byte[length * 2];
                    Marshal.Copy(message.m_pData, _buffer, 0, length);
                    SteamNetworkingMessage_t.Release(_messages[i]);
                    _received += length;
                    Received?.Invoke(peer, _buffer, length);
                }
            }
        }
        foreach (var peer in _justClosed) Disconnected?.Invoke(peer, "local");
        _justClosed.Clear();
    }

    public void Send(int peer, byte[] data, int length, Channel channel)
    {
        if (!_peers.TryGetValue(peer, out var connection)) return;
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var flags = channel == Channel.Reliable ? SendReliable : SendUnreliableNoNagle;
            SteamNetworkingSockets.SendMessageToConnection(connection, pin.AddrOfPinnedObject(), (uint)length, flags, out _);
            _sent += length;
        }
        finally
        {
            pin.Free();
        }
    }

    public void Disconnect(int peer)
    {
        if (!_peers.Remove(peer, out var connection)) return;
        _byHandle.Remove(connection.m_HSteamNetConnection);
        SteamNetworkingSockets.CloseConnection(connection, 0, "", true);
        _justClosed.Add(peer);
    }

    public int RoundTripMs(int peer)
    {
        if (!_peers.TryGetValue(peer, out var connection)) return 0;
        var status = default(SteamNetConnectionRealTimeStatus_t);
        var lanes = default(SteamNetConnectionRealTimeLaneStatus_t);
        return SteamNetworkingSockets.GetConnectionRealTimeStatus(connection, ref status, 0, ref lanes) == EResult.k_EResultOK ? status.m_nPing : 0;
    }

    public long BytesSent => _sent;
    public long BytesReceived => _received;

    public void Dispose()
    {
        foreach (var connection in _peers.Values) SteamNetworkingSockets.CloseConnection(connection, 0, "", true);
        _peers.Clear();
        if (_listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(_listen);
        _statusCallback?.Dispose();
    }
}
