using Pluton.Networking;
using Pluton.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Pluton.Tests;

/// <summary>Secuencia reproducible de principio a fin, jugada solo con comandos válidos.</summary>
public class PlaythroughTests
{
    private readonly ITestOutputHelper _out;
    public PlaythroughTests(ITestOutputHelper output) { _out = output; }

    /// <summary>Bot competente con una cadencia humana: cada acción de jugador consume tiempo de simulación.</summary>
    private sealed class Bot
    {
        public readonly World W;
        public readonly PlayerState P;
        public readonly List<string> Timeline = new();
        private int _nextObjective;
        private const int WalkTicks = 40;

        public Bot(World w, PlayerState p) { W = w; P = p; }

        public void Advance(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                W.Step();
                while (_nextObjective < W.ObjectiveIndex)
                {
                    Timeline.Add($"{W.Tick / 600.0,6:F1} min  {W.Content.Objectives[_nextObjective].Id}");
                    _nextObjective++;
                }
            }
        }

        public void Do(Command c, bool mustWork = true)
        {
            Advance(WalkTicks);
            var r = W.Apply(P, c);
            if (mustWork) Assert.True(r.Ok, $"{c.Type} {c.Text} {c.A}: {r.Error} (tick {W.Tick}, planeta {P.Planet}, créditos {W.Credits}, mochila [{string.Join(",", P.Bag.Items.Select(i => i.Key + "=" + i.Value))}], depósito [{string.Join(",", W.DepotOf(P.Planet)!.In.Items.Select(i => i.Key + "=" + i.Value))}])");
        }

        public void WalkTo(Machine m)
        {
            var (x, z) = W.MachineCenterMm(m);
            P.X = x / 1000f;
            P.Z = z / 1000f;
        }

        public void WalkToCell(int x, int z)
        {
            P.X = x * 2f;
            P.Z = z * 2f;
        }

        public void Cut(Debris d)
        {
            Advance(WalkTicks);
            P.X = d.X * 2f;
            P.Z = d.Z * 2f;
            while (W.DebrisById.ContainsKey(d.Id))
            {
                Advance(W.Content.Balance.CutterCooldownTicks);
                Assert.True(W.Apply(P, new Command { Type = CommandType.CutDebris, A = d.Id }).Ok);
            }
        }

        public void CutNearestScrap(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var d = W.DebrisById.Values.Where(x => x.Def == "scrap_pile")
                    .OrderBy(x => (x.X * 2 - P.X) * (x.X * 2 - P.X) + (x.Z * 2 - P.Z) * (x.Z * 2 - P.Z)).First();
                Cut(d);
            }
        }

        public Machine Build(string def, int x, int z, int rot = 0)
        {
            WalkToCell(x, z);
            Do(new Command { Type = CommandType.Place, Text = def, A = x, B = z, C = rot });
            return W.Machines.Values.Last();
        }

        public void Link(Machine a, Machine b) => Do(Fixture.LinkCmd(W, a, b));

        public void WaitUntil(Func<bool> condition, int maxTicks, string what)
        {
            for (var i = 0; i < maxTicks && !condition(); i++) Advance(1);
            Assert.True(condition(), $"No se alcanzó: {what} (tick {W.Tick})");
        }

        /// <summary>Limpia y recicla hasta tener en el depósito el material que necesita el siguiente paso.</summary>
        public void GatherRecovered(Machine recycler, Machine depot, long amount)
        {
            for (var round = 0; round < 40 && depot.In.Get("recovered") < amount; round++)
            {
                if (P.Bag.Get("scrap") < 2 && recycler.In.Get("scrap") < 2) CutNearestScrap(2);
                FeedRecycler(recycler);
                Advance(150);
            }
            Assert.True(depot.In.Get("recovered") >= amount, $"Sin material suficiente (tick {W.Tick})");
        }

        public void FeedRecycler(Machine recycler)
        {
            WalkTo(recycler);
            Do(new Command { Type = CommandType.Insert, A = recycler.Id }, mustWork: false);
        }
    }

    /// <summary>La campaña entera, solo con comandos válidos: cada coste se paga con lo producido en esta partida.</summary>
    [Fact]
    public void Full_campaign_reaches_final_objective_with_valid_commands_only()
    {
        var w = World.CreateNew(Fixture.Content);
        var p = Fixture.AddPlayer(w, "player-a");
        var bot = new Bot(w, p);
        var recycler = Fixture.Find(w, "recycler");
        var depot = Fixture.Find(w, "depot");
        var terminal = Fixture.Find(w, "export_terminal");
        var crates = new Dictionary<string, Machine>();

        void Reveal(ResourceNode n)
        {
            foreach (var d in w.DebrisById.Values.Where(d => d.CoversNode == n.Id).ToList()) bot.Cut(d);
        }
        void Clear(int planet, int x0, int z0, int x1, int z1)
        {
            foreach (var d in w.DebrisById.Values.Where(d => d.Planet == planet && d.CoversNode < 0 && d.X >= x0 && d.X <= x1 && d.Z >= z0 && d.Z <= z1).ToList()) bot.Cut(d);
        }
        void Carry(Machine from, Machine to)
        {
            bot.WalkTo(from);
            bot.Do(new Command { Type = CommandType.Extract, A = from.Id }, mustWork: false);
            bot.WalkTo(to);
            bot.Do(new Command { Type = CommandType.Insert, A = to.Id }, mustWork: false);
        }
        void Stock(string item, long qty)
        {
            if (item == "recovered") { bot.GatherRecovered(recycler, depot, qty); return; }
            bot.WaitUntil(() => crates[item].In.Get(item) + depot.In.Get(item) + p.Bag.Get(item) >= qty, 90000, "stock de " + item);
            if (depot.In.Get(item) + p.Bag.Get(item) < qty) Carry(crates[item], depot);
        }
        void Earn(long target)
        {
            for (var round = 0; round < 600 && w.Credits < target; round++)
            {
                bot.Advance(200);
                foreach (var item in new[] { "cryo_module", "parts", "ingot" })
                {
                    if (!crates.TryGetValue(item, out var crate) || crate.In.Get(item) < 25) continue;
                    Carry(crate, terminal);
                    foreach (var kv in terminal.In.Items.ToList())
                        bot.Do(new Command { Type = CommandType.Sell, A = terminal.Id, Text = kv.Key, Amount = kv.Value }, mustWork: false);
                    break;
                }
            }
            Assert.True(w.Credits >= target, $"créditos {w.Credits} de {target} (tick {w.Tick})");
        }
        void Afford(long credits, Dictionary<string, long> cost)
        {
            Earn(credits);
            foreach (var (item, qty) in cost) Stock(item, qty);
        }
        Machine Build(string def, int x, int z, int rot = 0)
        {
            var d = w.Content.Machines[def];
            if (p.Planet == 0) Afford(d.Credits, d.Cost);
            return bot.Build(def, x, z, rot);
        }
        void Buy(string id)
        {
            var u = w.Content.Upgrades[id];
            Afford(u.Credits, u.Cost);
            bot.Do(new Command { Type = CommandType.BuyUpgrade, Text = id });
        }
        void Fly(int planet)
        {
            var pad = w.Machines.Values.First(m => m.Def == "shuttle_terminal" && m.Planet == p.Planet);
            bot.WalkTo(pad);
            bot.Do(new Command { Type = CommandType.Travel, A = planet });
            bot.WaitUntil(() => p.Planet == planet && p.TransitTo < 0, 400, "llegada a " + planet);
        }

        // Plutón: primera línea, de chatarra a lingotes
        bot.Link(recycler, depot);
        var ore = w.Nodes.Values.Where(n => n.Item == "ore" && n.YieldPermille == 1000).OrderBy(n => n.X * n.X + n.Z * n.Z).ToList();
        foreach (var n in ore) Reveal(n);
        var ex1 = Build("ore_extractor", ore[0].X, ore[0].Z, rot: 3);
        var smelter = Build("smelter", 21, -3);
        bot.Link(ex1, smelter);
        crates["ingot"] = Build("storage_crate", 16, -2);
        bot.Link(smelter, crates["ingot"]);
        // El segundo yacimiento toca la fundición: se gira para que su salida quede libre.
        var ex2 = Build("ore_extractor", ore[1].X, ore[1].Z, rot: 3);
        bot.Link(ex2, smelter);
        Stock("ingot", 10);
        bot.Do(new Command { Type = CommandType.FulfillContract, Text = "c_first_ingots" });

        Buy("tech_assembly");
        var assembler = Build("assembler", 10, -3);
        bot.Link(crates["ingot"], assembler);
        crates["parts"] = Build("storage_crate", 5, -3);
        bot.Link(assembler, crates["parts"]);
        Build("rtg_generator", 8, 8);
        // Girado hacia la fundición: deja libre la fila norte, donde irá la estación.
        var ex3 = Build("ore_extractor", ore[2].X, ore[2].Z, rot: 1);
        bot.Link(ex3, smelter);
        var byAssembly = w.Tick / 600.0;

        // Ferrocarril hacia los yacimientos ricos
        Buy("tech_rail");
        Clear(0, 20, -40, 64, -7);
        var rich = w.Nodes.Values.Where(n => n.Item == "ore" && n.YieldPermille > 1000).OrderBy(n => n.X).ToList();
        foreach (var n in rich) Reveal(n);
        var baseStation = Build("station", 21, -9);
        bot.Do(new Command { Type = CommandType.StationMode, A = baseStation.Id, B = 1 });
        var mine = Build("station", 55, -37);
        Earn(200);
        bot.WalkToCell(35, -17);
        bot.Do(new Command { Type = CommandType.PlaceRail, A = 25, B = -7, C = 45, D = -27 });
        bot.WalkToCell(49, -31);
        bot.Do(new Command { Type = CommandType.PlaceRail, A = 45, B = -27, C = 53, D = -35 });
        bot.Do(new Command { Type = CommandType.PlaceRail, A = 53, B = -35, C = 55, D = -35 });
        // Tras tender la vía: la cinta rodea lo que ya está colocado.
        bot.Link(baseStation, smelter);
        foreach (var n in rich)
        {
            var e = Build("ore_extractor", n.X, n.Z);
            bot.Link(e, mine);
        }
        Build("rtg_generator", 12, 8);
        var rail = w.Content.Balance.Rail;
        Afford(rail.TrainCredits, rail.TrainCost);
        bot.WalkTo(mine);
        bot.Do(new Command { Type = CommandType.CreateTrain, A = mine.Id, B = baseStation.Id });
        bot.WaitUntil(() => w.Stats.GetValueOrDefault("train_delivered") >= 40, 12000, "entrega por tren");
        var byRail = w.Tick / 600.0;

        // Criogenia
        Buy("tech_cryo");
        var ice = w.Nodes.Values.Where(n => n.Item == "ice").OrderBy(n => n.X * n.X + n.Z * n.Z).ToList();
        Reveal(ice[0]);
        Clear(0, ice[0].X - 1, ice[0].Z - 1, ice[0].X + 10, ice[0].Z + 4);
        Clear(0, -7, -11, 4, -4);
        var iceEx = Build("ice_extractor", ice[0].X, ice[0].Z);
        var chiller = Build("chiller", ice[0].X + 3, ice[0].Z);
        bot.Link(iceEx, chiller);
        crates["coolant"] = Build("storage_crate", ice[0].X + 7, ice[0].Z);
        bot.Link(chiller, crates["coolant"]);
        var cryo = Build("cryo_assembler", 1, -8);
        bot.Link(crates["parts"], cryo);
        crates["cryo_module"] = Build("storage_crate", -5, -9);
        bot.Link(cryo, crates["cryo_module"]);
        Build("rtg_generator", 16, 8);
        void MakeModules(long total)
        {
            // El refrigerante va a mano hasta la ensambladora: está lejos de los enlaces.
            for (var i = 0; i < 200 && w.Stats.GetValueOrDefault("produced.cryo_module") < total; i++)
            {
                Carry(crates["coolant"], cryo);
                bot.Advance(500);
            }
            Assert.True(w.Stats.GetValueOrDefault("produced.cryo_module") >= total, $"módulos (tick {w.Tick})");
        }
        MakeModules(3);
        var byCryo = w.Tick / 600.0;

        // Marte: se viaja con material para la primera instalación
        Buy("tech_shuttle");
        var padPluto = Build("cargo_pad", -20, 10);
        Earn(4500);
        foreach (var (item, qty) in new[] { ("ingot", 140L), ("parts", 90L), ("recovered", 45L) })
        {
            Stock(item, qty);
            bot.WalkTo(depot);
            bot.Do(new Command { Type = CommandType.Extract, A = depot.Id }, mustWork: false);
        }
        Fly(1);
        var marsDepot = w.DepotOf(1)!;
        bot.WalkTo(marsDepot);
        bot.Do(new Command { Type = CommandType.Insert, A = marsDepot.Id }, mustWork: false);
        var silicon = w.Nodes.Values.Where(n => n.Item == "silicon_ore").OrderBy(n => n.X * n.X + n.Z * n.Z).Take(3).ToList();
        foreach (var n in silicon) Reveal(n);
        var sx = bot.Build("silicon_extractor", silicon[0].X, silicon[0].Z);
        Clear(1, 8, -20, 26, 14);
        var refinery = bot.Build("wafer_refinery", silicon[0].X + 3, silicon[0].Z + 3);
        bot.Link(sx, refinery);
        bot.Build("radiator", 10, -5);
        bot.Build("radiator", 10, -9);
        bot.Build("rtg_generator", 10, -1);
        var wafers = bot.Build("storage_crate", 13, -2);
        bot.Link(refinery, wafers);
        var padMars = bot.Build("cargo_pad", 8, 4);
        bot.WaitUntil(() => wafers.In.Get("wafer") >= 40, 24000, "obleas");
        Carry(wafers, marsDepot);

        // De vuelta a Plutón para enviar los módulos y reunir el contrato final
        Fly(0);
        MakeModules(14);
        Carry(crates["cryo_module"], padPluto);
        bot.Do(new Command { Type = CommandType.StationMode, A = padPluto.Id, B = 0 });
        Earn(2500);
        foreach (var (item, qty) in new[] { ("parts", 30L), ("recovered", 30L) })
        {
            Stock(item, qty);
            bot.WalkTo(depot);
            bot.Do(new Command { Type = CommandType.Extract, A = depot.Id }, mustWork: false);
        }
        Fly(1);
        bot.WaitUntil(() => padMars.In.Get("cryo_module") >= 6, 4000, "módulos en Marte");
        _out.WriteLine($"Llega: mochila [{string.Join(",", p.Bag.Items.Select(i => i.Key + "=" + i.Value))}] depósito [{string.Join(",", marsDepot.In.Items.Select(i => i.Key + "=" + i.Value))}]");
        bot.WalkTo(marsDepot);
        bot.Do(new Command { Type = CommandType.Insert, A = marsDepot.Id });
        _out.WriteLine($"Tras depositar: mochila [{string.Join(",", p.Bag.Items.Select(i => i.Key + "=" + i.Value))}] depósito [{string.Join(",", marsDepot.In.Items.Select(i => i.Key + "=" + i.Value))}]");
        bot.Do(new Command { Type = CommandType.FulfillContract, Text = "c_mars_samples" }, mustWork: false);
        Earn(0);
        // Mira al sur: entra por la fila que la separa de la plataforma y sale hacia el almacén.
        var fab = bot.Build("nav_fab", 9, 9, rot: 2);
        bot.Link(padMars, fab);
        var cores = bot.Build("storage_crate", 13, 10);
        bot.Link(fab, cores);
        var waferStock = bot.Build("storage_crate", 13, 4);
        Carry(marsDepot, waferStock);
        bot.Link(waferStock, fab);
        bot.Link(wafers, waferStock);
        bot.WaitUntil(() => cores.In.Get("nav_core") >= 6, 30000, "núcleos");
        bot.WalkTo(cores);
        bot.Do(new Command { Type = CommandType.Extract, A = cores.Id });

        Fly(0);
        MakeModules(22);
        bot.WalkTo(crates["cryo_module"]);
        bot.Do(new Command { Type = CommandType.Extract, A = crates["cryo_module"].Id });
        bot.Do(new Command { Type = CommandType.FulfillContract, Text = "c_ninth_planet" });
        bot.Advance(20);

        _out.WriteLine(string.Join("\n", bot.Timeline));
        _out.WriteLine($"Campaña: ensamblaje {byAssembly:F0} min, tren {byRail:F0} min, criogenia {byCryo:F0} min, final {w.Tick / 600.0:F0} min ({w.Tick / 36000.0:F1} h); créditos {w.Credits}; objetivo {w.ObjectiveIndex}/{w.Content.Objectives.Count}");
        Assert.Equal(w.Content.Objectives.Count, w.ObjectiveIndex);
        Assert.Equal(1, w.Stats["contract.c_ninth_planet"]);
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w))));
    }

    [Fact]
    public void New_game_reaches_all_objectives_without_debug_and_without_duplication()
    {
        var w = World.CreateNew(Fixture.Content);
        var p = Fixture.AddPlayer(w, "player-a");
        var bot = new Bot(w, p);
        var recycler = Fixture.Find(w, "recycler");
        var depot = Fixture.Find(w, "depot");
        var terminal = Fixture.Find(w, "export_terminal");

        // Limpieza y primer material
        bot.CutNearestScrap(6);
        bot.Link(recycler, depot);
        bot.FeedRecycler(recycler);
        bot.CutNearestScrap(8);
        bot.WaitUntil(() => depot.In.Get("recovered") >= 8 || recycler.In.Get("scrap") < 2, 1500, "material inicial");
        for (var round = 0; round < 8 && depot.In.Get("recovered") + p.Bag.Get("recovered") < 34; round++)
        {
            bot.FeedRecycler(recycler);
            bot.WaitUntil(() => recycler.In.Get("scrap") < 2 && !recycler.Processing, 1000, "reciclar");
        }

        // Primera línea: extractor -> fundición -> terminal
        var ore = w.Nodes.Values.Where(n => n.Item == "ore").OrderBy(n => n.X * n.X + n.Z * n.Z).ToList();
        Assert.Equal((18, -6), (ore[0].X, ore[0].Z));
        foreach (var d in w.DebrisById.Values.Where(d => d.CoversNode == ore[0].Id).ToList()) bot.Cut(d);
        var ex1 = bot.Build("ore_extractor", ore[0].X, ore[0].Z);
        var smelter = bot.Build("smelter", 21, -3);
        bot.Link(ex1, smelter);
        var midDepot = bot.Build("storage_crate", 16, -2);
        bot.Link(smelter, midDepot);
        bot.WaitUntil(() => midDepot.In.Get("ingot") >= 10, 3000, "10 lingotes");

        // Lleva lingotes a mano: venta y contrato
        bot.WalkTo(midDepot);
        bot.Do(new Command { Type = CommandType.Extract, A = midDepot.Id });
        bot.WalkTo(depot);
        bot.Do(new Command { Type = CommandType.Insert, A = depot.Id });
        bot.Do(new Command { Type = CommandType.FulfillContract, Text = "c_first_ingots" });

        bot.CutNearestScrap(4);
        bot.WalkTo(terminal);
        bot.Do(new Command { Type = CommandType.Insert, A = terminal.Id });
        foreach (var kv in terminal.In.Items.ToList())
            bot.Do(new Command { Type = CommandType.Sell, A = terminal.Id, Text = kv.Key, Amount = kv.Value });

        // Más producción para ensamblaje
        foreach (var node in ore.Skip(1).Take(2))
        {
            foreach (var d in w.DebrisById.Values.Where(d => d.CoversNode == node.Id).ToList()) bot.Cut(d);
        }
        bot.GatherRecovered(recycler, depot, 8);
        var ex2 = bot.Build("ore_extractor", ore[1].X, ore[1].Z, rot: 3);
        bot.Link(ex2, smelter);

        bot.WaitUntil(() => midDepot.In.Get("ingot") >= 30, 6000, "lingotes para ensamblaje");
        bot.WalkTo(midDepot);
        bot.Do(new Command { Type = CommandType.Extract, A = midDepot.Id });
        bot.WalkTo(terminal);
        bot.Do(new Command { Type = CommandType.Insert, A = terminal.Id });
        bot.Do(new Command { Type = CommandType.Sell, A = terminal.Id, Text = "ingot", Amount = 10 });
        bot.Do(new Command { Type = CommandType.Extract, A = terminal.Id });
        bot.WaitUntil(() => w.Credits >= 200, 6000, "créditos para ensamblaje");
        bot.Do(new Command { Type = CommandType.BuyUpgrade, Text = "tech_assembly" });

        bot.WaitUntil(() => midDepot.In.Get("ingot") >= 15 && w.Credits >= 150, 8000, "coste de la ensambladora");
        bot.WalkTo(midDepot);
        bot.Do(new Command { Type = CommandType.Extract, A = midDepot.Id });
        bot.GatherRecovered(recycler, depot, 16);
        var assembler = bot.Build("assembler", 10, -3);
        bot.Link(midDepot, assembler);
        var partsCrate = bot.Build("storage_crate", 5, -3);
        bot.Link(assembler, partsCrate);

        bot.WaitUntil(() => partsCrate.In.Get("parts") >= 8, 12000, "8 piezas");
        var a0_1Minutes = w.Tick / 600.0;

        _out.WriteLine(string.Join("\n", bot.Timeline));
        _out.WriteLine($"Ciclo A0.1 completo en {a0_1Minutes:F1} min de juego; créditos {w.Credits}; objetivo {w.ObjectiveIndex}/{w.Content.Objectives.Count}");

        // El alcance de A0.1 pide una partida nueva con unos 10-15 minutos de juego real.
        Assert.InRange(a0_1Minutes, 6, 30);
        Assert.True(w.Stats["contract.c_first_ingots"] == 1);

        // Sin duplicaciones: guardar y cargar conserva todo el estado.
        var loaded = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        // Conservación global: cada pieza proviene de lingotes reales.
        Assert.True(w.Stats["produced.ingot"] >= 2 * w.Stats["produced.parts"]);
    }
}

public class CoopCycleTests
{
    [Fact]
    public void Two_players_complete_the_same_economic_cycle_over_udp()
    {
        const string keyA = "client-identity-coop-a";
        const string keyB = "client-identity-coop-b";
        using var h = new NetHarness();
        var a = h.Join(keyA);
        var b = h.Join(keyB);
        var world = h.Server.World;
        var pa = world.Players[keyA];
        var pb = world.Players[keyB];
        var recycler = world.Machines.Values.First(m => m.Def == "recycler");
        var depot = world.Machines.Values.First(m => m.Def == "depot");
        var terminal = world.Machines.Values.First(m => m.Def == "export_terminal");

        void Stand(PlayerState p, Machine m)
        {
            var (x, z) = world.MachineCenterMm(m);
            p.X = x / 1000f;
            p.Z = z / 1000f;
        }
        void Run(GameClient c, Command cmd)
        {
            CommandResult? result = null;
            var id = c.Send(cmd);
            void Handler(uint rid, CommandResult r) { if (rid == id) result = r; }
            c.ResultReceived += Handler;
            h.Pump(() => result != null);
            c.ResultReceived -= Handler;
            Assert.True(result!.Value.Ok, $"{cmd.Type}: {result.Value.Error}");
        }

        // A limpia, B recicla: ambos contribuyen al mismo mundo.
        var debris = world.DebrisById.Values.Where(d => d.Def == "scrap_pile").Take(3).ToList();
        foreach (var d in debris)
        {
            pa.X = d.X * 2f;
            pa.Z = d.Z * 2f;
            while (world.DebrisById.ContainsKey(d.Id))
            {
                var attempt = new Command { Type = CommandType.CutDebris, A = d.Id };
                var id = a.Send(attempt);
                var done = false;
                void Handler(uint rid, CommandResult r) { if (rid == id) done = true; }
                a.ResultReceived += Handler;
                h.Pump(() => done);
                a.ResultReceived -= Handler;
            }
        }
        h.Settle(300);
        var scrap = world.Players[keyA].Bag.Get("scrap");
        Assert.True(scrap >= 30);
        Assert.Equal(scrap, b.World!.Players[keyA].Bag.Get("scrap"));

        // Transferencia por almacenamiento común, atómica y visible para ambos.
        Stand(pa, depot);
        Run(a, new Command { Type = CommandType.Insert, A = depot.Id });
        Assert.Equal(0, pa.Bag.Get("scrap"));
        Stand(pb, depot);
        Run(b, new Command { Type = CommandType.Extract, A = depot.Id });
        Assert.Equal(scrap, pb.Bag.Get("scrap"));
        Stand(pb, recycler);
        Run(b, new Command { Type = CommandType.Insert, A = recycler.Id });
        Stand(pb, recycler);
        Run(b, Fixture.LinkCmd(world, recycler, depot));
        Stand(pb, depot);
        Run(b, Fixture.LinkCmd(world, depot, terminal));

        // El servidor avanza hasta que la cadena produce y A vende lo que B fabricó.
        h.Pump(() => terminal.In.Get("recovered") >= 2, 30000);
        var before = world.Credits;
        var units = terminal.In.Get("recovered");
        Stand(pa, terminal);
        Run(a, new Command { Type = CommandType.Sell, A = terminal.Id, Text = "recovered", Amount = units });
        Assert.Equal(before + units * world.SellPrice("recovered"), world.Credits);

        // Con la fábrica en marcha solo es válida la firma enviada tras vaciar los cambios.
        var checksA = a.HashChecks;
        var checksB = b.HashChecks;
        h.Pump(() => a.HashChecks >= checksA + 2 && b.HashChecks >= checksB + 2, 20000);
        Assert.Equal(0, a.HashMismatches);
        Assert.Equal(0, b.HashMismatches);
        // Lo fabricado + lo restante no supera la chatarra original según la receta.
        Assert.True(world.Stats["produced.recovered"] * 2 <= scrap);
    }
}
