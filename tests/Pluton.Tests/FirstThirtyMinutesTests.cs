using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pluton.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Pluton.Tests;

/// <summary>
/// Recorrido reproducible de los primeros 30 minutos, sin escena, con comandos válidos y ritmo humano declarado.
/// Detecta bloqueos, esperas y acarreos. No demuestra que el juego resulte divertido: nadie ha jugado esto.
/// </summary>
public class FirstThirtyMinutesTests
{
    // Perfil de ritmo. Velocidad y alcance salen del cliente (GameView); las pausas son una hipótesis declarada.
    private const double WalkMetersPerSecond = 7;   // andar sin Mayús; esprintar sería 11
    private const double DetourFactor = 1.2;        // rodear máquinas y escombros frente a la línea recta
    private const double CutReachMeters = 4.4;      // WorkReach: el cabezal solo trabaja de cerca
    private const double InteractReachMeters = 6;   // el servidor admite 8; se usa algo menos
    private const double BuildReachMeters = 12;     // se construye mirando de cerca, no al límite de 40
    private const double ReadObjectiveSeconds = 5;  // leer un objetivo nuevo
    private const double DecideSeconds = 3;         // pausa antes de cada acción con menú o panel
    private const double AimSeconds = 1;            // apuntar a cada escombro
    private const double QuickActionSeconds = 1.5;  // depositar o recoger con una tecla
    private const double RotateAndPlaceSeconds = 3; // elegir sitio y orientación del fantasma
    private const double BeltPortsSeconds = 6;      // elegir dos conectores y confirmar la ruta

    private readonly ITestOutputHelper _out;
    public FirstThirtyMinutesTests(ITestOutputHelper output) { _out = output; }

    private sealed record Tramo(double Start, double Seconds, string Label);

    private sealed class Opportunity
    {
        public required string Label;
        public required Func<bool> Ready;
        public required Action Run;
    }

    /// <summary>Jugador simulado: todo lo que hace consume el tiempo de simulación de su perfil.</summary>
    private sealed class Human
    {
        public readonly World W;
        public readonly PlayerState P;
        public readonly List<(double second, string name)> Hitos = new();
        public readonly List<(double second, string id)> Objectives = new();
        public readonly List<Tramo> Esperas = new();
        public readonly List<Opportunity> Opportunities = new();
        public readonly List<string> Failures = new();
        public double WalkSeconds, CutSeconds, DecideTotal, CarryTrips;
        private string? _openLabel;
        private long _openTick;
        private int _seenObjective;
        private bool _runningOpportunity;

        public Human(World w, PlayerState p) { W = w; P = p; }

        public double Seconds => W.Tick / (double)W.Content.Balance.TicksPerSecond;
        private int Ticks(double seconds) => (int)Math.Ceiling(seconds * W.Content.Balance.TicksPerSecond);

        public void Advance(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                W.Step();
                while (_seenObjective < W.ObjectiveIndex)
                {
                    Objectives.Add((Seconds, W.Content.Objectives[_seenObjective].Id));
                    _seenObjective++;
                    Busy(ReadObjectiveSeconds);
                }
            }
        }

        private void Busy(double seconds)
        {
            CloseWait();
            DecideTotal += seconds;
            Advance(Ticks(seconds));
        }

        public void Hito(string name)
        {
            if (Hitos.All(h => h.name != name)) Hitos.Add((Seconds, name));
        }

        private void CloseWait()
        {
            if (_openLabel == null) return;
            var seconds = (W.Tick - _openTick) / (double)W.Content.Balance.TicksPerSecond;
            if (seconds > 0) Esperas.Add(new Tramo(_openTick / (double)W.Content.Balance.TicksPerSecond, seconds, _openLabel));
            _openLabel = null;
        }

        public void Finish() => CloseWait();

        /// <summary>Espera real: se intenta antes cualquier otra decisión útil y, si no hay, corre el reloj.</summary>
        public void Idle(string label)
        {
            if (!_runningOpportunity)
                foreach (var o in Opportunities.ToList())
                {
                    if (!o.Ready()) continue;
                    Opportunities.Remove(o);
                    CloseWait();
                    _runningOpportunity = true;
                    o.Run();
                    _runningOpportunity = false;
                    return;
                }
            if (_openLabel != label)
            {
                CloseWait();
                _openLabel = label;
                _openTick = W.Tick;
            }
            Advance(1);
        }

        public void WaitFor(Func<bool> done, string label, double maxSeconds = 1800)
        {
            var limit = W.Tick + Ticks(maxSeconds);
            while (!done())
            {
                Assert.True(W.Tick < limit, $"Bloqueo: {label} no llega en {maxSeconds} s (minuto {Seconds / 60:F1}, créditos {W.Credits})");
                Idle(label);
            }
            CloseWait();
        }

        public void Walk(double xMeters, double zMeters, double reachMeters)
        {
            var dx = xMeters - P.X;
            var dz = zMeters - P.Z;
            var distance = Math.Sqrt(dx * dx + dz * dz);
            if (distance <= reachMeters) return;
            CloseWait();
            var travel = distance - reachMeters;
            var seconds = travel * DetourFactor / WalkMetersPerSecond;
            WalkSeconds += seconds;
            Advance(Ticks(seconds));
            P.X += (float)(dx / distance * travel);
            P.Z += (float)(dz / distance * travel);
        }

        public void WalkTo(Machine m, double reachMeters)
        {
            var (x, z) = W.MachineCenterMm(m);
            Walk(x / 1000.0, z / 1000.0, reachMeters);
        }

        public void Decide(double seconds = DecideSeconds) => Busy(seconds);

        public CommandResult Do(Command c, bool mustWork = true)
        {
            CloseWait();
            var r = W.Apply(P, c);
            if (mustWork) Assert.True(r.Ok, $"{c.Type} {c.Text} {c.A}: {r.Error} (minuto {Seconds / 60:F1}, créditos {W.Credits})");
            return r;
        }

        public void Cut(Debris d)
        {
            Walk((d.X + 0.5) * 2, (d.Z + 0.5) * 2, CutReachMeters);
            CloseWait();
            CutSeconds += AimSeconds;
            Advance(Ticks(AimSeconds));
            var start = W.Tick;
            while (W.DebrisById.ContainsKey(d.Id))
            {
                while (W.Tick < P.CutterReadyTick) Advance(1);
                Do(new Command { Type = CommandType.CutDebris, A = d.Id });
                Hito("primera acción");
                Advance(1);
            }
            CutSeconds += (W.Tick - start) / (double)W.Content.Balance.TicksPerSecond;
        }

        public void CutNearestScrap()
        {
            WaitFor(() => W.DebrisById.Values.Any(x => x.Planet == P.Planet && x.Def == "scrap_pile"), "chatarra renovada");
            var d = W.DebrisById.Values.Where(x => x.Planet == P.Planet && x.Def == "scrap_pile")
                .OrderBy(x => (x.X * 2 - P.X) * (x.X * 2 - P.X) + (x.Z * 2 - P.Z) * (x.Z * 2 - P.Z)).ThenBy(x => x.Id).First();
            Cut(d);
        }

        public void Reveal(ResourceNode n)
        {
            foreach (var d in W.DebrisById.Values.Where(d => d.CoversNode == n.Id).ToList()) Cut(d);
        }

        public long Have(string item) => P.Bag.Get(item) + W.DepotOf(P.Planet)!.In.Get(item);

        public void Quick(Machine m, CommandType type, string item = "", long amount = 0)
        {
            WalkTo(m, InteractReachMeters);
            Decide(QuickActionSeconds);
            Do(new Command { Type = type, A = m.Id, Text = item, Amount = amount }, mustWork: false);
            CarryTrips++;
        }

        public Machine Build(string def, int x, int z, int rot = 0)
        {
            Walk(x * 2.0, z * 2.0, BuildReachMeters);
            Decide(RotateAndPlaceSeconds);
            while (W.CheckPlacement(P.Planet, def, x, z, rot, -1, out _) == "err_debris")
            {
                var cells = W.Footprint(def, x, z, rot).ToHashSet();
                var d = W.DebrisById.Values.Where(d => d.Planet == P.Planet && cells.Contains((d.X, d.Z))).OrderBy(d => d.Id).First();
                Cut(d);
                Walk(x * 2.0, z * 2.0, BuildReachMeters);
            }
            Do(new Command { Type = CommandType.Place, Text = def, A = x, B = z, C = rot });
            Hito("primera máquina");
            return W.Machines.Values.Last();
        }

        public void Belt(Machine a, Machine b)
        {
            WalkTo(a, BuildReachMeters);
            Decide(BeltPortsSeconds);
            var route = W.RouteBelt(a.Id, b.Id, 200000);
            Assert.True(route.Error == null, $"Sin ruta {a.Def} → {b.Def}: {route.Error}");
            WaitFor(() => W.Credits >= route.Credits, "créditos para la cinta");
            Do(Fixture.LinkCmd(W, a, b));
            Hito("primera cinta");
        }

        public void Buy(string id)
        {
            Decide();
            Do(new Command { Type = CommandType.BuyUpgrade, Text = id });
        }
    }

    private sealed class Run
    {
        public required Human H;
        public required Machine Recycler, Depot, Terminal;
        public Machine? Smelter;
        public bool RecyclerBelt;
    }

    /// <summary>Reúne material recuperado con el ciclo real: cortar, llevar a la recicladora y recoger lo reciclado.</summary>
    private static void GatherRecovered(Run run, long amount)
    {
        var h = run.H;
        var r = run.Recycler;
        var limit = h.W.Tick + 18000;
        while (h.Have("recovered") < amount)
        {
            Assert.True(h.W.Tick < limit, $"Bloqueo reciclando (minuto {h.Seconds / 60:F1})");
            var scrapNeeded = 2 * (amount - h.Have("recovered") - r.Out.Get("recovered") - (r.In.Get("scrap") + (r.Processing ? 2 : 0)) / 2);
            if (!run.RecyclerBelt && r.Out.Get("recovered") > 0 && (r.Out.Get("recovered") + h.Have("recovered") >= amount || r.Out.Get("recovered") >= 10))
            {
                h.Quick(r, CommandType.Extract, "recovered");
                continue;
            }
            if (h.P.Bag.Get("scrap") >= 2 && h.W.InputRoom(r, "scrap") >= Math.Min(10, h.P.Bag.Get("scrap")))
            {
                h.Quick(r, CommandType.Insert, "scrap");
                continue;
            }
            if (scrapNeeded > 0 && h.P.Bag.Get("scrap") < 20)
            {
                h.CutNearestScrap();
                continue;
            }
            h.Idle("reciclado");
        }
    }

    /// <summary>Toma los lingotes acumulados en la terminal cuando un pago los necesita.</summary>
    private static void GatherFromTerminal(Run run, string item, long amount, string label)
    {
        var h = run.H;
        if (h.Have(item) >= amount) return;
        if (run.Terminal.Reserves.GetValueOrDefault(item) < amount)
        {
            h.WalkTo(run.Terminal, InteractReachMeters);
            h.Decide();
            h.Do(new Command { Type = CommandType.SetAutoSell, A = run.Terminal.Id, B = 1, Text = item, Amount = amount });
        }
        h.WaitFor(() => h.Have(item) + run.Terminal.In.Get(item) >= amount, label);
        h.Quick(run.Terminal, CommandType.Extract, item, amount - h.Have(item));
    }

    private static void Pay(Run run, long credits, Dictionary<string, long> cost, string label)
    {
        var h = run.H;
        foreach (var (item, qty) in cost)
        {
            if (item == "recovered") GatherRecovered(run, qty);
            else GatherFromTerminal(run, item, qty, label);
        }
        h.WaitFor(() => h.W.Credits >= credits, "créditos para " + label);
    }

    private static Machine BuildPaid(Run run, string def, int x, int z, int rot = 0)
    {
        var d = run.H.W.Content.Machines[def];
        Pay(run, d.Credits, d.Cost, def);
        return run.H.Build(def, x, z, rot);
    }

    private static void BuyPaid(Run run, string id)
    {
        var u = run.H.W.Content.Upgrades[id];
        Pay(run, u.Credits, u.Cost, id);
        run.H.Buy(id);
    }

    private static bool NearSegment(Debris d, (int x, int z) a, (int x, int z) b)
    {
        var (dx, dz) = (b.x - a.x, b.z - a.z);
        var t = Math.Clamp(((d.X - a.x) * dx + (d.Z - a.z) * dz) / (double)(dx * dx + dz * dz), 0, 1);
        var (px, pz) = (a.x + t * dx - d.X, a.z + t * dz - d.Z);
        return px * px + pz * pz <= 9;
    }

    /// <summary>La estación tiende su propio tramo de vía: se limpia también lo que lo taparía.</summary>
    private static Machine BuildStation(Run run, int x, int z)
    {
        var h = run.H;
        foreach (var d in h.W.DebrisById.Values.Where(d => d.Planet == 0 && d.CoversNode < 0 && d.X >= x - 1 && d.X <= x + 4 && d.Z >= z - 2 && d.Z <= z + 3).ToList()) h.Cut(d);
        return BuildPaid(run, "station", x, z);
    }

    private static Human PlayFirstThirtyMinutes()
    {
        var w = World.CreateNew(Fixture.Content);
        var p = Fixture.AddPlayer(w, "player-a");
        var h = new Human(w, p);
        var run = new Run { H = h, Recycler = Fixture.Find(w, "recycler"), Depot = Fixture.Find(w, "depot"), Terminal = Fixture.Find(w, "export_terminal") };
        var ore = w.Nodes.Values.Where(n => n.Planet == 0 && n.Item == "ore" && n.YieldPermille == 1000).OrderBy(n => n.X * n.X + n.Z * n.Z).ToList();

        // Mano: tres montones, reciclar y reunir material para la primera línea.
        h.Decide(ReadObjectiveSeconds);
        for (var i = 0; i < 3; i++) h.CutNearestScrap();
        GatherRecovered(run, 8);

        // Primera línea: se sigue el orden de los objetivos.
        h.Reveal(ore[0]);
        var ex1 = BuildPaid(run, "ore_extractor", ore[0].X, ore[0].Z, rot: 3);
        var smelter = BuildPaid(run, "smelter", 21, -3);
        run.Smelter = smelter;
        h.Belt(ex1, smelter);
        h.Belt(smelter, run.Terminal);

        // Mientras la línea arranca hay decisiones que no dependen de ella.
        h.Opportunities.Add(new Opportunity
        {
            Label = "cinta recicladora → depósito",
            Ready = () => w.Credits >= w.RouteBelt(run.Recycler.Id, run.Depot.Id, 200000).Credits + 20,
            Run = () =>
            {
                h.Belt(run.Recycler, run.Depot);
                run.RecyclerBelt = true;
            },
        });
        h.Opportunities.Add(new Opportunity
        {
            Label = "segundo extractor",
            Ready = () => w.CanAfford(p, w.Content.Machines["ore_extractor"].Credits + 40, w.Content.Machines["ore_extractor"].Cost),
            Run = () =>
            {
                h.Reveal(ore[1]);
                var ex2 = h.Build("ore_extractor", ore[1].X, ore[1].Z, rot: 3);
                h.Belt(ex2, smelter);
            },
        });

        h.WaitFor(() => run.Terminal.In.Get("ingot") > 0, "primer lingote por cinta");
        h.Hito("primera cadena automática");

        // Se abre el panel de la terminal: venta automática conservando lo que pide el contrato.
        h.WalkTo(run.Terminal, InteractReachMeters);
        h.Decide();
        h.Do(new Command { Type = CommandType.SetAutoSell, A = run.Terminal.Id, B = 1, Text = "ingot", Amount = 10 });
        h.WaitFor(() => w.Stats.GetValueOrDefault("sold_credits") > 0 || run.Terminal.In.Get("ingot") >= 10, "lingotes para vender");
        h.WaitFor(() => run.Terminal.In.Get("ingot") >= 10, "lingotes del contrato");
        h.Decide();
        h.Do(new Command { Type = CommandType.FulfillContract, Text = "c_first_ingots" });
        h.WalkTo(run.Terminal, InteractReachMeters);
        h.Decide();
        h.Do(new Command { Type = CommandType.SetAutoSell, A = run.Terminal.Id, B = 1, Text = "ingot", Amount = 0 });
        h.WaitFor(() => w.Stats.GetValueOrDefault("sold_credits") > 0, "primera venta");
        h.Hito("primer ingreso");

        // Primera mejora significativa: abre una receta nueva.
        BuyPaid(run, "tech_assembly");
        h.Hito("primera mejora significativa (Ensamblaje)");

        // Segunda cadena: lingotes a piezas, que valen más.
        // Se gira el fantasma hasta ver libre el conector de salida, como hace el jugador con la vista previa.
        var assemblerRot = Enumerable.Range(0, 4).First(rot => w.BeltPorts(0, "assembler", 10, -3, rot, true).Any(port => port.Free));
        var assembler = BuildPaid(run, "assembler", 10, -3, assemblerRot);
        // La salida primero: la cinta de entrada no debe taparle el conector.
        h.Belt(assembler, run.Terminal);
        h.Belt(smelter, assembler);
        BuildPaid(run, "rtg_generator", 8, 8);
        h.Opportunities.Add(new Opportunity
        {
            Label = "tercer extractor",
            Ready = () => w.CanAfford(p, w.Content.Machines["ore_extractor"].Credits + 40, w.Content.Machines["ore_extractor"].Cost),
            Run = () =>
            {
                h.Reveal(ore[2]);
                var ex3 = h.Build("ore_extractor", ore[2].X, ore[2].Z, rot: 1);
                h.Belt(ex3, smelter);
            },
        });
        h.WaitFor(() => w.Stats.GetValueOrDefault("produced.parts") >= 8, "piezas del objetivo");
        h.Hito("segunda cadena automática (piezas)");

        BuyPaid(run, "extract_speed");
        h.Hito("mejora de ritmo (Extracción)");
        BuyPaid(run, "tech_rail");
        h.Hito("licencia ferroviaria");

        // Larga distancia: solo se limpia lo que estorba al trazado, no el campo entero.
        var rich = w.Nodes.Values.Where(n => n.Planet == 0 && n.Item == "ore" && n.YieldPermille > 1000).OrderBy(n => n.X).ToList();
        var waypoints = new[] { (x: 25, z: -11), (x: 41, z: -27), (x: 45, z: -27), (x: 53, z: -35), (x: 55, z: -35) };
        var baseStation = BuildStation(run, 21, -13);
        h.Decide();
        h.Do(new Command { Type = CommandType.StationMode, A = baseStation.Id, B = 1 });
        foreach (var n in rich) h.Reveal(n);
        var mine = BuildStation(run, 55, -37);
        for (var i = 0; i + 1 < waypoints.Length; i++)
        {
            var (a, b) = (waypoints[i], waypoints[i + 1]);
            foreach (var d in w.DebrisById.Values.Where(d => d.Planet == 0 && d.CoversNode < 0 && NearSegment(d, a, b)).OrderBy(d => (d.X - a.x) * (d.X - a.x) + (d.Z - a.z) * (d.Z - a.z)).ToList()) h.Cut(d);
            h.Walk((a.x + b.x) * 1.0, (a.z + b.z) * 1.0, BuildReachMeters);
            h.Decide(RotateAndPlaceSeconds);
            w.CheckRail(0, a.x, a.z, b.x, b.z, 0, out var railCredits);
            h.WaitFor(() => w.Credits >= railCredits, "créditos para la vía");
            h.Do(new Command { Type = CommandType.PlaceRail, A = a.x, B = a.z, C = b.x, D = b.z });
        }
        h.Belt(baseStation, smelter);
        foreach (var n in rich)
        {
            var e = BuildPaid(run, "ore_extractor", n.X, n.Z);
            h.Belt(e, mine);
        }
        BuildPaid(run, "rtg_generator", 12, 8);
        var rail = w.Content.Balance.Rail;
        Pay(run, rail.TrainCredits, rail.TrainCost, "tren");
        h.WalkTo(mine, InteractReachMeters);
        h.Decide();
        h.Do(new Command { Type = CommandType.CreateTrain, A = mine.Id, B = baseStation.Id });
        h.Hito("primer tren");
        h.WaitFor(() => w.Stats.GetValueOrDefault("train_delivered") >= 40, "primera entrega por tren");
        h.Hito("primera entrega por tren");

        h.Finish();
        return h;
    }

    private static string Clock(double seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)seconds / 60:00}:{(int)seconds % 60:00}");

    [Fact]
    public void FirstThirtyMinutes_reaches_automation_and_upgrade_without_long_waits()
    {
        var h = PlayFirstThirtyMinutes();

        var report = new StringBuilder();
        report.AppendLine("HITOS");
        foreach (var (second, name) in h.Hitos) report.AppendLine($"  {Clock(second)}  {name}");
        report.AppendLine("OBJETIVOS");
        foreach (var (second, id) in h.Objectives) report.AppendLine($"  {Clock(second)}  {id}");
        report.AppendLine("ESPERAS MÁS LARGAS");
        foreach (var t in h.Esperas.OrderByDescending(t => t.Seconds).Take(10))
            report.AppendLine($"  {t.Seconds,6:F1} s  desde {Clock(t.Start)}  {t.Label}");
        report.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"TOTALES final {Clock(h.Seconds)}  espera {h.Esperas.Sum(t => t.Seconds):F0} s en {h.Esperas.Count} tramos  andar {h.WalkSeconds:F0} s  cortar {h.CutSeconds:F0} s  pausas {h.DecideTotal:F0} s  acarreos {h.CarryTrips}  créditos {h.W.Credits}"));
        _out.WriteLine(report.ToString());

        var names = h.Hitos.Select(x => x.name).ToList();
        foreach (var hito in new[] { "primera acción", "primera máquina", "primera cinta", "primera cadena automática", "primer ingreso", "primera mejora significativa (Ensamblaje)" })
            Assert.Contains(hito, names);
        Assert.True(h.Hitos.First(x => x.name.StartsWith("primera mejora")).second <= 30 * 60, "mejora significativa fuera de los 30 minutos");
        var longest = h.Esperas.OrderByDescending(t => t.Seconds).First();
        Assert.True(longest.Seconds <= 60, $"Espera de {longest.Seconds:F0} s: {longest.Label} (minuto {longest.Start / 60:F1})");
    }

    // ---------- Sin callejones ----------

    /// <summary>Hasta dónde llega la partida antes de gastarlo todo mal.</summary>
    public enum EarlyStage { Inicio, Reciclado, Extractor, Fundicion, Ensamblaje }

    private static Run StartRun()
    {
        var w = World.CreateNew(Fixture.Content);
        var p = Fixture.AddPlayer(w, "player-a");
        return new Run { H = new Human(w, p), Recycler = Fixture.Find(w, "recycler"), Depot = Fixture.Find(w, "depot"), Terminal = Fixture.Find(w, "export_terminal") };
    }

    private static Machine? Line(Run run, string def) => run.H.W.Machines.Values.FirstOrDefault(m => m.Def == def);

    private static List<ResourceNode> NearOre(World w) =>
        w.Nodes.Values.Where(n => n.Planet == 0 && n.Item == "ore" && n.YieldPermille == 1000).OrderBy(n => n.X * n.X + n.Z * n.Z).ToList();

    private static bool HasBelt(World w, Machine a, Machine b) => w.Links.Values.Any(l => l.From == a.Id && l.To == b.Id);

    /// <summary>Solo se gana a mano: cortar chatarra y venderla en la terminal.</summary>
    private static void EarnByHand(Run run, long credits)
    {
        var h = run.H;
        var limit = h.W.Tick + 36000;
        while (h.W.Credits < credits)
        {
            Assert.True(h.W.Tick < limit, $"Bloqueo: sin forma de ganar {credits} créditos (tiene {h.W.Credits})");
            if (h.P.Bag.Get("scrap") == 0) h.CutNearestScrap();
            h.Quick(run.Terminal, CommandType.Insert, "scrap");
            var stock = run.Terminal.In.Get("scrap");
            if (stock > 0) h.Do(new Command { Type = CommandType.Sell, A = run.Terminal.Id, Text = "scrap", Amount = stock });
        }
    }

    private static void PlaceWithHand(Run run, string def, int x, int z, int rot = 0)
    {
        var d = run.H.W.Content.Machines[def];
        GatherRecovered(run, d.Cost.GetValueOrDefault("recovered"));
        EarnByHand(run, d.Credits);
        run.H.Build(def, x, z, rot);
    }

    private static void BeltWithHand(Run run, Machine a, Machine b)
    {
        EarnByHand(run, run.H.W.RouteBelt(a.Id, b.Id, 200000).Credits);
        run.H.Belt(a, b);
    }

    /// <summary>Construye lo que falte de la primera línea y compra la siguiente tecnología desde lo que quede.</summary>
    private static void RecoverAndAdvance(Run run)
    {
        var h = run.H;
        var w = h.W;
        var ore = NearOre(w);
        if (!ore[0].Revealed) h.Reveal(ore[0]);
        if (Line(run, "ore_extractor") == null) PlaceWithHand(run, "ore_extractor", ore[0].X, ore[0].Z, 3);
        if (Line(run, "smelter") == null) PlaceWithHand(run, "smelter", 21, -3);
        var ex = Line(run, "ore_extractor")!;
        var smelter = Line(run, "smelter")!;
        if (!HasBelt(w, ex, smelter)) BeltWithHand(run, ex, smelter);
        if (!HasBelt(w, smelter, run.Terminal)) BeltWithHand(run, smelter, run.Terminal);

        var next = w.Upgrades.Contains("tech_assembly") ? "tech_rail" : "tech_assembly";
        var u = w.Content.Upgrades[next];
        var ingots = u.Cost["ingot"];
        h.WaitFor(() => run.Terminal.In.Get("ingot") >= ingots, "lingotes de la recuperación");
        h.Quick(run.Terminal, CommandType.Extract, "ingot", ingots);
        EarnByHand(run, u.Credits);
        h.Buy(next);
        Assert.Contains(next, w.Upgrades);
    }

    private static void ReachStage(Run run, EarlyStage stage)
    {
        var h = run.H;
        var w = h.W;
        if (stage == EarlyStage.Inicio) return;
        for (var i = 0; i < 3; i++) h.CutNearestScrap();
        GatherRecovered(run, 8);
        if (stage == EarlyStage.Reciclado) return;
        var ore = NearOre(w);
        h.Reveal(ore[0]);
        var ex = BuildPaid(run, "ore_extractor", ore[0].X, ore[0].Z, rot: 3);
        if (stage == EarlyStage.Extractor) return;
        var smelter = BuildPaid(run, "smelter", 21, -3);
        h.Belt(ex, smelter);
        if (stage == EarlyStage.Fundicion) return;
        h.Belt(smelter, run.Terminal);
        h.WaitFor(() => run.Terminal.In.Get("ingot") >= 10, "lingotes del contrato");
        h.Do(new Command { Type = CommandType.FulfillContract, Text = "c_first_ingots" });
        BuyPaidFromBelt(run);
    }

    private static void BuyPaidFromBelt(Run run)
    {
        var u = run.H.W.Content.Upgrades["tech_assembly"];
        run.H.WaitFor(() => run.Terminal.In.Get("ingot") >= u.Cost["ingot"], "lingotes de la tecnología");
        run.H.Quick(run.Terminal, CommandType.Extract, "ingot", u.Cost["ingot"]);
        EarnByHand(run, u.Credits);
        run.H.Buy("tech_assembly");
    }

    /// <summary>Gasto malo real con comandos válidos y, después, saldo y materiales a cero, mochila incluida.</summary>
    private static void SpendEverythingBadly(Run run, bool exhaustScrap)
    {
        var h = run.H;
        var w = h.W;
        var crate = w.Content.Machines["storage_crate"];
        for (var x = -60; x < -30 && w.CanAfford(h.P, crate.Credits, crate.Cost); x += 3)
        {
            if (w.CheckPlacement(0, "storage_crate", x, 40, 0, -1, out _) != null) continue;
            h.P.X = x * 2f;
            h.P.Z = 80;
            w.Apply(h.P, new Command { Type = CommandType.Place, Text = "storage_crate", A = x, B = 40 });
        }
        w.Credits = 0;
        foreach (var kv in h.P.Bag.Items.ToList()) h.P.Bag.TryRemove(kv.Key, kv.Value);
        foreach (var m in w.Machines.Values)
        {
            foreach (var kv in m.In.Items.ToList()) m.In.TryRemove(kv.Key, kv.Value);
            foreach (var kv in m.Out.Items.ToList()) m.Out.TryRemove(kv.Key, kv.Value);
        }
        if (exhaustScrap)
        {
            foreach (var d in w.DebrisById.Values.Where(d => d.Def == "scrap_pile").ToList()) w.DebrisById.Remove(d.Id);
            w.Reindex();
        }
    }

    [Theory]
    [InlineData(EarlyStage.Inicio, false)]
    [InlineData(EarlyStage.Reciclado, false)]
    [InlineData(EarlyStage.Extractor, false)]
    [InlineData(EarlyStage.Fundicion, false)]
    [InlineData(EarlyStage.Ensamblaje, false)]
    [InlineData(EarlyStage.Inicio, true)]
    [InlineData(EarlyStage.Reciclado, true)]
    [InlineData(EarlyStage.Extractor, true)]
    [InlineData(EarlyStage.Fundicion, true)]
    [InlineData(EarlyStage.Ensamblaje, true)]
    public void Spending_badly_at_any_early_stage_never_blocks_progress(EarlyStage stage, bool exhaustScrap)
    {
        var run = StartRun();
        ReachStage(run, stage);
        SpendEverythingBadly(run, exhaustScrap);
        Assert.Equal(0, run.H.W.Credits);
        RecoverAndAdvance(run);
    }

    [Fact]
    public void Scrap_comes_back_only_up_to_the_minimum_when_fields_are_exhausted()
    {
        var (w, _) = Fixture.NewWorld();
        foreach (var d in w.DebrisById.Values.Where(d => d.Def == "scrap_pile").ToList()) w.DebrisById.Remove(d.Id);
        w.Reindex();
        var renewal = w.Content.Balance.ScrapRenewal;
        Assert.True(renewal.MinPiles > 0 && renewal.IntervalTicks > 0);
        int Piles() => w.DebrisById.Values.Count(d => d.Planet == 0 && d.Def == "scrap_pile");

        for (var i = 0; i < renewal.IntervalTicks; i++) w.Step();
        Assert.Equal(1, Piles());
        Assert.NotEmpty(w.DirtyDebris);
        for (var i = 0; i < renewal.IntervalTicks * (renewal.MinPiles + 5); i++) w.Step();
        Assert.Equal(renewal.MinPiles, Piles());
    }

    [Fact]
    public void Dismantling_a_bad_purchase_returns_all_it_cost()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = w.Content.Machines["storage_crate"];
        var depot = Fixture.Find(w, "depot");
        depot.In.Add("recovered", crate.Cost["recovered"]);
        var credits = w.Credits;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = 12 }).Ok);
        Assert.Equal(credits - crate.Credits, w.Credits);
        Assert.Equal(0, depot.In.Get("recovered"));
        var built = w.Machines.Values.Last();
        Assert.True(w.Apply(p, new Command { Type = CommandType.Remove, A = built.Id }).Ok);
        Assert.Equal(credits, w.Credits);
        Assert.Equal(crate.Cost["recovered"], depot.In.Get("recovered"));
    }

    // ---------- Objetivos ----------

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { Path.GetDirectoryName(ContentDb.LocateDirectory())! }.Concat(parts).ToArray());

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Objective_texts_use_real_actions_and_targets_point_at_something_real(string language)
    {
        var source = File.ReadAllText(RepoFile("src", "Game", "Scripts", "GameView.cs"));
        var defaults = source[source.IndexOf("class Controls", StringComparison.Ordinal)..];
        var actions = Regex.Matches(defaults, "\\[\"([a-z_0-9]+)\"\\] = new\\[\\]").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains("interact", actions);

        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(ContentDb.LocateDirectory(), "loc", language + ".json")))!;
        var w = World.CreateNew(Fixture.Content);
        foreach (var o in Fixture.Content.Objectives)
        {
            Assert.True(table.TryGetValue("objective_" + o.Id, out var text), $"Sin texto: {o.Id}");
            foreach (Match token in Regex.Matches(text!, @"\{([a-z_]+)\}"))
                Assert.True(actions.Contains(token.Groups[1].Value), $"{o.Id}: acción inexistente {token.Value}");
            if (o.Target == null) continue;
            var kind = o.Target.Split(':');
            Assert.Equal(2, kind.Length);
            var known = kind[0] switch
            {
                "machine" => Fixture.Content.Machines.ContainsKey(kind[1]),
                "node" => w.Nodes.Values.Any(n => n.Item == kind[1]),
                "debris" => Fixture.Content.Balance.Debris.ContainsKey(kind[1]),
                _ => false,
            };
            Assert.True(known, $"{o.Id}: destino inexistente {o.Target}");
        }
    }

    // ---------- Cinta frente a tren: la decisión D-043 en cifras, leídas de los datos ----------

    [Fact]
    public void Belt_wins_short_distances_and_train_wins_long_ones_without_any_length_limit()
    {
        var b = Fixture.Content.Balance;
        var rail = b.Rail;
        var fixedCredits = 2 * Fixture.Content.Machines["station"].Credits + rail.TrainCredits;
        long BeltCost(long cells) => cells * b.BeltCreditsPerCell;
        long RailCost(long cells) => fixedCredits + cells * rail.CreditsPerCell;
        double TrainPerMinute(long cells) =>
            60.0 * rail.TrainCapacity / (2.0 * cells * b.CellSizeMm / rail.TrainSpeedMmPerTick / b.TicksPerSecond + 2.0 * rail.TrainCapacity / rail.TransferUnitsPerTick / b.TicksPerSecond);
        double BeltPerMinute = 60.0 * b.TicksPerSecond / b.LinkIntervalTicks;

        Assert.True(BeltCost(40) < RailCost(40), "a corta distancia gana la cinta en coste");
        Assert.True(BeltCost(180) > RailCost(180), "a larga distancia gana el tren en coste");
        Assert.True(TrainPerMinute(68) > 2 * BeltPerMinute, "a la distancia de los yacimientos ricos el tren lleva más del doble");
        Assert.True(TrainPerMinute(180) >= 0.95 * BeltPerMinute, "de borde a borde del planeta, un tren iguala una cinta");
    }
}
