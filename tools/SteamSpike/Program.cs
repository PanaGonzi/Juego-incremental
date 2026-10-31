using System.Runtime.InteropServices;
using Steamworks;

// Prueba técnica: AppID 480 es el ejemplo público de Valve (Spacewar), NO el del producto.
// Valida binding + SDK + acceso a relays + canal fiable y no fiable, en una sola máquina y cuenta.
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "steam_appid.txt"), "480");
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

if (!SteamAPI.Init())
{
    Console.WriteLine("RESULTADO steam_init=FALLO (¿Steam abierto?)");
    return 1;
}
Console.WriteLine($"steam_init=OK appid={SteamUtils.GetAppID()} loggedOn={SteamUser.BLoggedOn()}");

SteamNetworkingUtils.InitRelayNetworkAccess();
var clock = System.Diagnostics.Stopwatch.StartNew();
SteamRelayNetworkStatus_t status = default;
while (clock.Elapsed.TotalSeconds < 25)
{
    SteamAPI.RunCallbacks();
    var a = SteamNetworkingUtils.GetRelayNetworkStatus(out status);
    if (a == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current) break;
    Thread.Sleep(100);
}
Console.WriteLine($"relay_status={status.m_eAvail} seconds={clock.Elapsed.TotalSeconds:F1} debug='{status.m_debugMsg}'");

// Par de sockets por la pila real de Steam Networking Sockets, usando loopback de red.
var idA = new SteamNetworkingIdentity();
var idB = new SteamNetworkingIdentity();
var pair = SteamNetworkingSockets.CreateSocketPair(out var a1, out var b1, true, ref idA, ref idB);
Console.WriteLine($"socket_pair={pair}");
var failures = 0;
if (pair)
{
    foreach (var (label, flags) in new[] { ("reliable", 8), ("unreliable", 0) })
    {
        var payload = System.Text.Encoding.UTF8.GetBytes("pluton-" + label);
        var h = GCHandle.Alloc(payload, GCHandleType.Pinned);
        var send = SteamNetworkingSockets.SendMessageToConnection(a1, h.AddrOfPinnedObject(), (uint)payload.Length, flags, out _);
        h.Free();
        var got = false;
        var messages = new IntPtr[8];
        var t = System.Diagnostics.Stopwatch.StartNew();
        while (t.ElapsedMilliseconds < 3000 && !got)
        {
            SteamAPI.RunCallbacks();
            var n = SteamNetworkingSockets.ReceiveMessagesOnConnection(b1, messages, messages.Length);
            for (var i = 0; i < n; i++)
            {
                var msg = SteamNetworkingMessage_t.FromIntPtr(messages[i]);
                var buffer = new byte[msg.m_cbSize];
                Marshal.Copy(msg.m_pData, buffer, 0, msg.m_cbSize);
                got |= System.Text.Encoding.UTF8.GetString(buffer) == "pluton-" + label;
                SteamNetworkingMessage_t.Release(messages[i]);
            }
            Thread.Sleep(5);
        }
        Console.WriteLine($"channel={label} send={send} received={got} ms={t.ElapsedMilliseconds}");
        if (!got) failures++;
    }
    SteamNetworkingSockets.CloseConnection(a1, 0, "fin", false);
    SteamNetworkingSockets.CloseConnection(b1, 0, "fin", false);
}

// Lobby de amigos: se crea y se abandona de inmediato para confirmar la llamada.
var lobbyDone = false;
var result = EResult.k_EResultFail;
ulong lobby = 0;
using (var cr = CallResult<LobbyCreated_t>.Create((r, io) => { lobbyDone = true; result = r.m_eResult; lobby = r.m_ulSteamIDLobby; }))
{
    cr.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, 12));
    var t = System.Diagnostics.Stopwatch.StartNew();
    while (!lobbyDone && t.Elapsed.TotalSeconds < 10)
    {
        SteamAPI.RunCallbacks();
        Thread.Sleep(50);
    }
}
Console.WriteLine($"lobby_private_12 done={lobbyDone} result={result}");
if (lobby != 0) SteamMatchmaking.LeaveLobby(new CSteamID(lobby));

SteamAPI.Shutdown();
Console.WriteLine($"RESULTADO failures={failures}");
return failures;
