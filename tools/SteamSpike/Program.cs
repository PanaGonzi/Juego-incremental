using System.Diagnostics;
using Pluton.Networking;
using Pluton.Simulation;
using Pluton.Steam;
using Steamworks;

// Prueba técnica: AppID 480 es el ejemplo público de Valve (Spacewar), NO el del producto.
// Sesión real del juego (servidor autoritativo + 12 clientes) sobre Steam Networking Sockets,
// en una máquina y una cuenta. No demuestra la conexión entre cuentas ni el paso por relays.
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "steam_appid.txt"), "480");
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

if (!SteamAPI.Init())
{
    Console.WriteLine("RESULTADO steam_init=FALLO (¿Steam abierto?)");
    return 1;
}
Console.WriteLine($"steam_init=OK appid={SteamUtils.GetAppID()} loggedOn={SteamUser.BLoggedOn()}");

SteamNetworkingUtils.InitRelayNetworkAccess();
var clock = Stopwatch.StartNew();
SteamRelayNetworkStatus_t status = default;
while (clock.Elapsed.TotalSeconds < 25)
{
    SteamAPI.RunCallbacks();
    if (SteamNetworkingUtils.GetRelayNetworkStatus(out status) == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current) break;
    Thread.Sleep(100);
}
Console.WriteLine($"relay_status={status.m_eAvail} seconds={clock.Elapsed.TotalSeconds:F1} debug='{status.m_debugMsg}'");

const int clients = 12;
var content = ContentDb.Load(ContentDb.LocateDirectory());
var serverEnds = new List<HSteamNetConnection>();
var clientEnds = new List<HSteamNetConnection>();
for (var i = 0; i < clients; i++)
{
    var idA = new SteamNetworkingIdentity();
    var idB = new SteamNetworkingIdentity();
    if (!SteamNetworkingSockets.CreateSocketPair(out var a, out var b, true, ref idA, ref idB))
    {
        Console.WriteLine("RESULTADO socket_pair=FALLO");
        SteamAPI.Shutdown();
        return 2;
    }
    serverEnds.Add(a);
    clientEnds.Add(b);
}

var world = World.CreateNew(content);
var server = new Pluton.Networking.GameServer(world, SteamTransport.FromConnections(serverEnds), new ServerOptions { Log = m => Console.WriteLine("[servidor] " + m), CheckEveryTicks = 20 });
var players = new List<GameClient>();
for (var i = 0; i < clients; i++)
    players.Add(new GameClient(content, SteamTransport.FromConnections(new[] { clientEnds[i] }), $"steam-test-identity-{i:D4}", $"steam{i:D2}"));

var ok = 0;
var failed = 0;
foreach (var p in players) p.ResultReceived += (_, r) => { if (r.Ok) ok++; else failed++; };

void Pump(int ms, Action? each = null)
{
    var t = Stopwatch.StartNew();
    while (t.ElapsedMilliseconds < ms)
    {
        server.Tick();
        foreach (var p in players) p.Poll();
        each?.Invoke();
        Thread.Sleep(5);
    }
}

var joinClock = Stopwatch.StartNew();
Pump(4000);
var joined = players.Count(p => p.State == ClientState.InGame);
Console.WriteLine($"clientes_en_partida={joined}/{clients} conectados_servidor={server.ConnectedCount} ms={joinClock.ElapsedMilliseconds}");

// Competencia real por escombros, con movimiento por el canal no fiable.
var rng = new Random(7);
foreach (var p in players)
{
    var debris = world.DebrisById.Values.Where(d => d.Planet == 0).OrderBy(_ => rng.Next()).First();
    var me = world.Players[p.Key];
    me.X = debris.X * 2f;
    me.Z = debris.Z * 2f;
}
var sinceAction = 0;
Pump(15000, () =>
{
    if (++sinceAction % 20 != 0) return;
    foreach (var p in players)
    {
        if (p.World == null || p.LocalPlayer == null) continue;
        var me = world.Players[p.Key];
        var target = p.World.DebrisById.Values.Where(d => d.Planet == 0).OrderBy(d => (d.X * 2f - me.X) * (d.X * 2f - me.X) + (d.Z * 2f - me.Z) * (d.Z * 2f - me.Z)).FirstOrDefault();
        if (target == null) continue;
        me.X = target.X * 2f;
        me.Z = target.Z * 2f;
        p.SendMove(me.X, 1, me.Z, 0);
        p.Send(new Command { Type = CommandType.CutDebris, A = target.Id });
    }
});

var mismatches = players.Sum(p => p.HashMismatches);
var checks = players.Sum(p => p.HashChecks);
Console.WriteLine($"acciones ok={ok} rechazadas={failed} firmas={checks} divergencias={mismatches} creditos={world.Credits} enviado_kib={server.Transport.BytesSent / 1024} recibido_kib={server.Transport.BytesReceived / 1024}");

foreach (var p in players.Take(1)) p.Disconnect();
Pump(1500);
Console.WriteLine($"tras_desconectar_uno conectados={server.ConnectedCount}");
var pass = joined == clients && mismatches == 0 && checks > 0 && ok > 0 && server.ConnectedCount == clients - 1;
foreach (var p in players) p.Dispose();
server.Dispose();
SteamAPI.Shutdown();
Console.WriteLine($"RESULTADO {(pass ? "CORRECTO" : "FALLO")}");
return pass ? 0 : 3;
