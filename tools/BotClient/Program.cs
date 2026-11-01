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

UdpRelay? relay = null;
if (map.TryGetValue("latency", out var latency))
{
    var l = int.Parse(latency, CultureInfo.InvariantCulture);
    var jitter = int.Parse(map.GetValueOrDefault("jitter", "0"), CultureInfo.InvariantCulture);
    relay = new UdpRelay(host, port, l, jitter, int.Parse(map.GetValueOrDefault("loss", "0"), CultureInfo.InvariantCulture), index + 1);
    host = "127.0.0.1";
    port = relay.LocalPort;
}

var content = ContentDb.Load(map.GetValueOrDefault("content", ContentDb.LocateDirectory()));
var total = Stopwatch.StartNew();
long ok = 0, failed = 0, chats = 0, moves = 0, reconnects = 0, connectMsSum = 0, connects = 0;
int mismatches = 0, checks = 0;
long maxRtt = 0;
var commandMs = new List<long>();
string closeReason = "";
var rng = new Random(index * 7919 + 13);
var plan0 = new BotPlan();
var errors = new Dictionary<string, int>();

while (total.Elapsed.TotalSeconds < seconds)
{
    var connectStart = total.ElapsedMilliseconds;
    var client = new GameClient(content, LiteNetTransport.Dial(host, port), key, protocolLabel);
    // Respuesta de la aplicación, medida aparte del RTT que informa el transporte.
    var sentAt = new Dictionary<uint, long>();
    client.CommandSent += id => sentAt[id] = total.ElapsedMilliseconds;
    client.ResultReceived += (id, r) =>
    {
        if (sentAt.Remove(id, out var at)) commandMs.Add(total.ElapsedMilliseconds - at);
        if (r.Ok) ok++;
        else
        {
            failed++;
            errors[r.Error] = errors.GetValueOrDefault(r.Error) + 1;
        }
    };
    var sessionEnd = reconnectEvery > 0 ? total.Elapsed.TotalSeconds + reconnectEvery : double.MaxValue;
    var connected = false;
    float x = 0, z = 0, angle = (float)(rng.NextDouble() * Math.PI * 2);
    var plan = plan0;
    plan.Target = null;
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
                if (plan.Arrived && client.LocalPlayer is { } arrived)
                {
                    plan.Arrived = false;
                    x = arrived.X;
                    z = arrived.Z;
                }
                // Camina hacia su objetivo a velocidad legal.
                if (plan.Target is { } goal)
                {
                    var gx = goal.x - x;
                    var gz = goal.z - z;
                    var len = MathF.Sqrt(gx * gx + gz * gz);
                    if (len > 3.5f)
                    {
                        x += gx / len * 0.55f;
                        z += gz / len * 0.55f;
                    }
                }
                client.SendMove(x, 1f, z, angle);
                moves++;
            }

            if (now >= nextAction)
            {
                nextAction = now + 250 + rng.Next(250);
                Act(client, behaviour, rng, x, z, plan);
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

long CommandPercentile(int p)
{
    if (commandMs.Count == 0) return 0;
    commandMs.Sort();
    return commandMs[Math.Min(commandMs.Count - 1, commandMs.Count * p / 100)];
}

void Report() => Console.WriteLine(
    $"{protocolLabel} cmdN={commandMs.Count} cmdP50={CommandPercentile(50)} cmdP99={CommandPercentile(99)} cmdMax={CommandPercentile(100)} connects={connects} avgConnectMs={(connects == 0 ? 0 : connectMsSum / connects)} ok={ok} failed={failed} moves={moves} chats={chats} " +
    $"reconnects={reconnects} hashChecks={checks} hashMismatch={mismatches} maxRttMs={maxRtt} relayDropped={relay?.Dropped ?? 0} relayForwarded={relay?.Forwarded ?? 0} travels={plan0.Travels} closeReason={closeReason} errores=[{string.Join(",", errors.OrderByDescending(e => e.Value).Take(4).Select(e => e.Key + ":" + e.Value))}]");

static void Act(GameClient client, string behaviour, Random rng, float x, float z, BotPlan plan)
{
    var world = client.World!;
    var me = client.LocalPlayer!;
    var cell = world.Content.Balance.CellSizeMm / 1000f;

    if (behaviour == "abuser")
    {
        // Valores absurdos: el servidor debe rechazarlos sin caerse.
        client.Send(new Command { Type = CommandType.Sell, A = int.MaxValue, Text = "scrap", Amount = long.MaxValue });
        client.Send(new Command { Type = CommandType.Place, Text = "no_existe", A = int.MinValue, B = int.MaxValue });
        client.Send(new Command { Type = CommandType.SetRole, Text = me.Key, A = (int)Role.Owner });
        client.Send(new Command { Type = CommandType.PlaceRail, A = int.MinValue, B = 0, C = int.MaxValue, D = 0, Amount = 99 });
        client.SendMove(float.NaN, float.PositiveInfinity, 0, 0);
        client.SendMove(99999f, 1f, 99999f, 0);
        return;
    }

    if (me.TransitTo >= 0)
    {
        plan.WasTraveling = true;
        return;
    }
    if (plan.WasTraveling)
    {
        // Ya en el destino: retoma desde la posición que marca el servidor.
        plan.WasTraveling = false;
        plan.Travels++;
        plan.Arrived = true;
        plan.Target = null;
    }

    // Ciclo de jugador: limpiar, reciclar, llevar al depósito y vender. Compite por lo mismo que los demás.
    Machine Find(string def) => world.Machines.Values.First(m => m.Def == def && m.Planet == me.Planet);
    bool Near((float x, float z) target, float range) => (target.x - x) * (target.x - x) + (target.z - z) * (target.z - z) < range * range;
    (float x, float z) Center(Machine m)
    {
        var (mx, mz) = world.MachineCenterMm(m);
        return (mx / 1000f, mz / 1000f);
    }

    if (behaviour == "traveler")
    {
        // Viaja una y otra vez entre planetas, independiente del resto de la sala.
        var pad = Find("shuttle_terminal");
        plan.Target = Center(pad);
        if (Near(plan.Target.Value, 6))
            client.Send(new Command { Type = CommandType.Travel, A = me.Planet == 0 ? 1 : 0 });
        return;
    }

    if (me.Planet != 0)
    {
        // Los bots de producción solo operan en Plutón.
        var back = Find("shuttle_terminal");
        plan.Target = Center(back);
        if (Near(plan.Target.Value, 6)) client.Send(new Command { Type = CommandType.Travel, A = 0 });
        return;
    }

    switch (plan.Step % 5)
    {
        case 0:
            Debris? best = null;
            var bestDist = float.MaxValue;
            foreach (var d in world.DebrisById.Values)
            {
                if (d.Planet != me.Planet) continue;
                var dx = (d.X + 0.5f) * cell - x;
                var dz = (d.Z + 0.5f) * cell - z;
                var dist = dx * dx + dz * dz;
                if (dist < bestDist) { bestDist = dist; best = d; }
            }
            if (best == null || me.Bag.Get("scrap") >= 30) { plan.Step++; break; }
            if (bestDist > 36) { plan.Target = ((best.X + 0.5f) * cell, (best.Z + 0.5f) * cell); break; }
            client.Send(new Command { Type = CommandType.CutDebris, A = best.Id });
            break;
        case 1:
            var recycler = Find("recycler");
            plan.Target = Center(recycler);
            if (!Near(plan.Target!.Value, 6)) break;
            if (me.Bag.Get("scrap") > 0) client.Send(new Command { Type = CommandType.Insert, A = recycler.Id, Text = "scrap" });
            if (!world.Links.Values.Any(l => l.From == recycler.Id))
                client.Send(World.LinkCommand(recycler.Id, Find("depot").Id, world.RouteBelt(recycler.Id, Find("depot").Id).Cells));
            plan.Step++;
            break;
        case 2:
            var depot = Find("depot");
            plan.Target = Center(depot);
            if (!Near(plan.Target!.Value, 6)) break;
            if (depot.In.Get("recovered") > 0) client.Send(new Command { Type = CommandType.Extract, A = depot.Id, Text = "recovered" });
            if (depot.In.Get("scrap") > 0 && me.Bag.Get("scrap") < 30) client.Send(new Command { Type = CommandType.Extract, A = depot.Id, Text = "scrap" });
            plan.Step++;
            break;
        case 3:
            var terminal = Find("export_terminal");
            plan.Target = Center(terminal);
            if (!Near(plan.Target!.Value, 6)) break;
            if (me.Bag.Get("recovered") > 0) client.Send(new Command { Type = CommandType.Insert, A = terminal.Id, Text = "recovered" });
            foreach (var kv in terminal.In.Items.ToList())
                client.Send(new Command { Type = CommandType.Sell, A = terminal.Id, Text = kv.Key, Amount = kv.Value });
            plan.Step++;
            break;
        default:
            // Construye y desmonta en una zona disputada por varios bots.
            var own = world.Machines.Values.Where(m => m.Def == "storage_crate").ToList();
            // Tiende y retira cintas con carga real desde el depósito: varios bots compiten por las mismas.
            var fed = world.Links.Values.Where(l => own.Any(m => m.Id == l.To)).ToList();
            if (fed.Count > 3 && rng.Next(3) == 0)
            {
                client.Send(new Command { Type = CommandType.Unlink, A = fed[rng.Next(fed.Count)].Id });
                plan.Step++;
                break;
            }
            if (own.Count > 0 && rng.Next(2) == 0)
            {
                var crate = own[rng.Next(own.Count)];
                if (!world.Links.Values.Any(l => l.To == crate.Id))
                {
                    client.Send(World.LinkCommand(Find("depot").Id, crate.Id, world.RouteBelt(Find("depot").Id, crate.Id).Cells));
                    plan.Step++;
                    break;
                }
            }
            if (own.Count > 4)
            {
                var old = own[rng.Next(own.Count)];
                plan.Target = Center(old);
                if (Near(plan.Target!.Value, 20)) client.Send(new Command { Type = CommandType.Remove, A = old.Id });
            }
            else
            {
                client.Send(new Command { Type = CommandType.Place, Text = "storage_crate", A = (int)(x / cell) + rng.Next(-8, 8), B = (int)(z / cell) + rng.Next(-8, 8), C = rng.Next(4) });
            }
            plan.Step++;
            break;
    }
}

sealed class BotPlan
{
    public int Step;
    public (float x, float z)? Target;
    public bool WasTraveling;
    public bool Arrived;
    public int Travels;
}