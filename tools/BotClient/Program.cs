using System.Diagnostics;
using System.Globalization;
using Pluton.Networking;
using Pluton.Simulation;

// Proceso cliente independiente con el protocolo real: sirve para carga y coherencia, no sustituye a jugar.
var map = new Dictionary<string, string>();
for (var i = 0; i < args.Length; i++)
    if (args[i].StartsWith("--", StringComparison.Ordinal))
        map[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";

var host = map.GetValueOrDefault("host", "127.0.0.1");
var port = int.Parse(map.GetValueOrDefault("port", "27777"), CultureInfo.InvariantCulture);
var seconds = int.Parse(map.GetValueOrDefault("seconds", "30"), CultureInfo.InvariantCulture);
var index = int.Parse(map.GetValueOrDefault("index", "0"), CultureInfo.InvariantCulture);
var behaviour = map.GetValueOrDefault("behaviour", "worker");
var reconnectEvery = int.Parse(map.GetValueOrDefault("reconnect-every", "0"), CultureInfo.InvariantCulture);
var key = map.GetValueOrDefault("key", $"bot-identity-{index:D8}");
var protocolLabel = $"bot{index:D2}";

NetSimulation? sim = null;
if (map.TryGetValue("latency", out var latency))
{
    var l = int.Parse(latency, CultureInfo.InvariantCulture);
    var jitter = int.Parse(map.GetValueOrDefault("jitter", "0"), CultureInfo.InvariantCulture);
    sim = new NetSimulation(l / 2, l / 2 + jitter, int.Parse(map.GetValueOrDefault("loss", "0"), CultureInfo.InvariantCulture));
}

var content = ContentDb.Load(map.GetValueOrDefault("content", ContentDb.LocateDirectory()));
var total = Stopwatch.StartNew();
long ok = 0, failed = 0, chats = 0, moves = 0, reconnects = 0, connectMsSum = 0, connects = 0;
int mismatches = 0, checks = 0;
long maxRtt = 0;
string closeReason = "";
var rng = new Random(index * 7919 + 13);

while (total.Elapsed.TotalSeconds < seconds)
{
    var connectStart = total.ElapsedMilliseconds;
    var client = new GameClient(content, LiteNetTransport.Dial(host, port, sim), key, protocolLabel);
    client.ResultReceived += (_, r) => { if (r.Ok) ok++; else failed++; };
    var sessionEnd = reconnectEvery > 0 ? total.Elapsed.TotalSeconds + reconnectEvery : double.MaxValue;
    var connected = false;
    float x = 0, z = 0, angle = (float)(rng.NextDouble() * Math.PI * 2);
    client.PositionCorrected += (cx, _, cz) => { x = cx; z = cz; };
    long nextMove = 0, nextAction = 0;

    while (total.Elapsed.TotalSeconds < seconds && total.Elapsed.TotalSeconds < sessionEnd)
    {
        client.Poll();
        if (client.State == ClientState.Closed) break;
        var now = total.ElapsedMilliseconds;

        if (client.State == ClientState.InGame)
        {
            if (!connected)
            {
                connected = true;
                connects++;
                connectMsSum += now - connectStart;
                var me = client.LocalPlayer!;
                x = me.X; z = me.Z;
            }
            maxRtt = Math.Max(maxRtt, client.RoundTripMs);

            if (now >= nextMove)
            {
                nextMove = now + 50;
                // Recorre el área inicial a velocidad legal.
                angle += (float)((rng.NextDouble() - 0.5) * 0.4);
                x += MathF.Cos(angle) * 0.3f;
                z += MathF.Sin(angle) * 0.3f;
                if (Math.Abs(x) > 60 || Math.Abs(z) > 60) angle += MathF.PI;
                client.SendMove(x, 1f, z, angle);
                moves++;
            }

            if (now >= nextAction)
            {
                nextAction = now + 250 + rng.Next(250);
                Act(client, behaviour, rng, ref x, ref z);
                if (rng.Next(200) == 0) { client.SendChat("hola desde " + protocolLabel); chats++; }
            }
        }
        Thread.Sleep(5);
    }

    client.Poll();
    mismatches += client.HashMismatches;
    checks += client.HashChecks;
    closeReason = client.CloseReason;
    if (client.State == ClientState.Closed && !connected)
    {
        // Rechazado o inaccesible antes de entrar: es un resultado final de este proceso.
        Report();
        Environment.Exit(closeReason == "reject_full" ? 3 : 4);
    }
    client.Disconnect();
    client.Poll();
    client.Dispose();
    if (total.Elapsed.TotalSeconds < seconds) reconnects++;
}

Report();
Environment.Exit(mismatches == 0 ? 0 : 5);

void Report() => Console.WriteLine(
    $"{protocolLabel} connects={connects} avgConnectMs={(connects == 0 ? 0 : connectMsSum / connects)} ok={ok} failed={failed} moves={moves} chats={chats} " +
    $"reconnects={reconnects} hashChecks={checks} hashMismatch={mismatches} maxRttMs={maxRtt} closeReason={closeReason}");

static void Act(GameClient client, string behaviour, Random rng, ref float x, ref float z)
{
    var world = client.World!;
    var me = client.LocalPlayer!;
    var cell = world.Content.Balance.CellSizeMm / 1000f;

    switch (behaviour)
    {
        case "cutter":
            // Disputa el escombro más cercano; varios bots compiten por los mismos.
            Debris? best = null;
            var bestDist = float.MaxValue;
            foreach (var d in world.DebrisById.Values)
            {
                var dx = d.X * cell - x;
                var dz = d.Z * cell - z;
                var dist = dx * dx + dz * dz;
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
            if (best == null) break;
            if (bestDist > 36)
            {
                // Se acerca a velocidad legal; el servidor rechazará un salto mayor.
                var len = MathF.Sqrt(bestDist);
                var step = Math.Min(len - 3, 3f);
                x += (best.X * cell - x) / len * step;
                z += (best.Z * cell - z) / len * step;
                client.SendMove(x, 1f, z, 0);
                break;
            }
            client.Send(new Command { Type = CommandType.CutDebris, A = best.Id });
            break;

        case "worker":
        default:
            var roll = rng.Next(100);
            if (roll < 30 && me.Bag.Total > 0)
            {
                var near = world.Machines.Values.ElementAt(rng.Next(world.Machines.Count));
                client.Send(new Command { Type = CommandType.Insert, A = near.Id });
            }
            else if (roll < 60)
            {
                var m = world.Machines.Values.ElementAt(rng.Next(world.Machines.Count));
                client.Send(new Command { Type = CommandType.Extract, A = m.Id });
            }
            else if (roll < 80 && world.DebrisById.Count > 0)
            {
                var d = world.DebrisById.Values.ElementAt(rng.Next(world.DebrisById.Count));
                client.Send(new Command { Type = CommandType.CutDebris, A = d.Id });
            }
            else
            {
                // Intenta colocar y a veces retirar, compitiendo por créditos y celdas.
                client.Send(new Command { Type = CommandType.Place, Text = "storage_crate", A = (int)(x / cell) + rng.Next(-6, 6), B = (int)(z / cell) + rng.Next(-6, 6), C = rng.Next(4) });
            }
            break;

        case "abuser":
            // Valores absurdos: el servidor debe rechazarlos sin caerse.
            client.Send(new Command { Type = CommandType.Sell, A = int.MaxValue, Text = "scrap", Amount = long.MaxValue });
            client.Send(new Command { Type = CommandType.Place, Text = "no_existe", A = int.MinValue, B = int.MaxValue });
            client.Send(new Command { Type = CommandType.SetRole, Text = me.Key, A = (int)Role.Owner });
            client.SendMove(float.NaN, float.PositiveInfinity, 0, 0);
            client.SendMove(99999f, 1f, 99999f, 0);
            break;
    }
}
