using System.Globalization;
using System.Text;
using Pluton.Networking;
using Pluton.Persistence;
using Pluton.Simulation;

// Servidor dedicado sin interfaz. El guardado autoritativo vive aquí.
var args2 = ParseArgs(args);
var port = int.Parse(args2.GetValueOrDefault("port", "27777"), CultureInfo.InvariantCulture);
var slot = args2.GetValueOrDefault("world", "dedicated");
var profile = args2.GetValueOrDefault("profile", "server");
var maxPlayers = Math.Clamp(int.Parse(args2.GetValueOrDefault("max-players", "12"), CultureInfo.InvariantCulture), 1, Protocol.MaxPlayers);
var metricsPath = args2.GetValueOrDefault("metrics", "");
var autosave = int.Parse(args2.GetValueOrDefault("autosave", "120"), CultureInfo.InvariantCulture);
var stopFile = args2.GetValueOrDefault("stop-file", "");
var loadTestUnlock = args2.ContainsKey("load-test-unlock");

void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

var content = ContentDb.Load(args2.GetValueOrDefault("content", ContentDb.LocateDirectory()));
var store = new SaveStore(profile, args2.GetValueOrDefault("data-root"));

World world;
if (File.Exists(store.PathOf(slot)))
{
    world = store.Load(slot, content);
    Log($"Mundo '{slot}' cargado en tick {world.Tick}");
}
else
{
    world = World.CreateNew(content);
    Log($"Mundo nuevo '{slot}'");
}

if (args2.TryGetValue("load-test-scenario", out var scenario))
{
    // Ensayo: reemplaza el mundo por uno de medición. No se guarda sobre partidas normales porque exige su propio perfil.
    world = Scenarios.Build(content, scenario);
    Log($"Escenario {scenario}: {world.Machines.Count} máquinas, {world.Trains.Count} trenes, {world.RailSegments.Count} tramos, instantánea {WorldCodec.Serialize(world).Length / 1024} KiB");
}

if (loadTestUnlock)
{
    // Solo para ensayos de carga: habilita los viajes sin jugar la progresión.
    world.Upgrades.Add("tech_shuttle");
    world.Reindex();
}

var transport = LiteNetTransport.Listen(port);
var server = new GameServer(world, transport, new ServerOptions
{
    MaxPlayers = maxPlayers,
    Password = args2.GetValueOrDefault("password", ""),
    Store = store,
    SaveSlot = slot,
    AutosaveSeconds = autosave,
    Log = Log,
});
var runner = new ServerRunner(server);
runner.Start();
Log($"PLUTON servidor {Protocol.GameVersion} protocolo {Protocol.Version} contenido {content.Revision} en UDP {port}, máximo {maxPlayers}");

var quit = new ManualResetEventSlim();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Set(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => quit.Set();

if (metricsPath.Length > 0)
    File.WriteAllText(metricsPath, "unix_s,tick,players,tick_avg_ms,tick_p99_ms,tick_max_ms,late,sent_kbps,recv_kbps,working_set_mb,gc_heap_mb,applied,rejected\n");

long lastSent = 0, lastRecv = 0;
var sinceMetrics = 0;
while (!quit.Wait(500))
{
    if (stopFile.Length > 0 && File.Exists(stopFile)) break;
    if (++sinceMetrics < 10 && runner.Fault == null) continue;
    sinceMetrics = 0;
    if (runner.Fault != null)
    {
        Log("Fallo fatal del servidor: " + runner.Fault);
        Environment.ExitCode = 2;
        break;
    }
    var ticks = runner.DrainTickTimes();
    if (ticks.Length == 0 || metricsPath.Length == 0) continue;
    Array.Sort(ticks);
    var sent = transport.BytesSent;
    var recv = transport.BytesReceived;
    var line = string.Join(",",
        DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        server.World.Tick,
        server.ConnectedCount,
        ticks.Average().ToString("F3", CultureInfo.InvariantCulture),
        ticks[(int)(ticks.Length * 0.99)].ToString("F3", CultureInfo.InvariantCulture),
        ticks[^1].ToString("F3", CultureInfo.InvariantCulture),
        runner.TicksLate,
        ((sent - lastSent) * 8 / 5000.0).ToString("F1", CultureInfo.InvariantCulture),
        ((recv - lastRecv) * 8 / 5000.0).ToString("F1", CultureInfo.InvariantCulture),
        (Environment.WorkingSet / 1048576.0).ToString("F1", CultureInfo.InvariantCulture),
        (GC.GetTotalMemory(false) / 1048576.0).ToString("F1", CultureInfo.InvariantCulture),
        server.CommandsApplied,
        server.CommandsRejected);
    lastSent = sent;
    lastRecv = recv;
    File.AppendAllText(metricsPath, line + "\n", Encoding.UTF8);
}

Log("Cerrando: guardando mundo");
runner.Stop(save: runner.Fault == null);
runner.Dispose();
Log("Cerrado");

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var map = new Dictionary<string, string>();
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--", StringComparison.Ordinal)) continue;
        var key = argv[i][2..];
        map[key] = i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal) ? argv[++i] : "true";
    }
    return map;
}
