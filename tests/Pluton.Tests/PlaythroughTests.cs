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
            if (mustWork) Assert.True(r.Ok, $"{c.Type} {c.Text} {c.A}: {r.Error} (tick {W.Tick})");
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

        public Machine Build(string def, int x, int z)
        {
            WalkToCell(x, z);
            Do(new Command { Type = CommandType.Place, Text = def, A = x, B = z });
            return W.Machines.Values.Last();
        }

        public void Link(Machine a, Machine b) => Do(new Command { Type = CommandType.Link, A = a.Id, B = b.Id });

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
        var ex2 = bot.Build("ore_extractor", ore[1].X, ore[1].Z);
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
        Run(b, new Command { Type = CommandType.Link, A = recycler.Id, B = depot.Id });
        Stand(pb, depot);
        Run(b, new Command { Type = CommandType.Link, A = depot.Id, B = terminal.Id });

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
