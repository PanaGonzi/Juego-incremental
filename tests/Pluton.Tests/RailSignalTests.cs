using Pluton.Networking;
using Pluton.Persistence;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>B-8: señales, bloques, desvíos y cruces.</summary>
public class RailSignalTests
{
    private const int Z = -50;

    private sealed class Yard
    {
        public required World W;
        public required PlayerState P;

        public void Do(Command c, int x, int z)
        {
            P.X = x * 2;
            P.Z = z * 2;
            var r = W.Apply(P, c);
            Assert.True(r.Ok, $"{c.Type} en {x},{z}: {r.Error}");
        }

        public string? Try(Command c, int x, int z)
        {
            P.X = x * 2;
            P.Z = z * 2;
            return W.Apply(P, c).Error;
        }

        public Machine Station(int x, int z)
        {
            Do(new Command { Type = CommandType.Place, Text = "station", A = x, B = z }, x, z);
            return W.Machines.Values.Last();
        }

        public RailSegment Rail(int x1, int z1, int x2, int z2, byte shape = 0)
        {
            Do(new Command { Type = CommandType.PlaceRail, A = x1, B = z1, C = x2, D = z2, Amount = shape }, x1, z1);
            return W.RailSegments.Values.Last();
        }

        public void Signal(int x, int z) => Do(new Command { Type = CommandType.ToggleSignal, A = x, B = z }, x, z);

        public long Drained;

        /// <summary>Avanza un tick y vacía los destinos, como lo haría la fábrica que consume lo que llega.</summary>
        public void Step(params Machine[] sinks)
        {
            W.Step();
            foreach (var m in sinks)
            {
                var qty = m.In.Get("ore");
                if (qty <= 0) continue;
                m.In.TryRemove("ore", qty);
                Drained += qty;
            }
        }

        public Train Train(Machine a, Machine b)
        {
            P.Bag.Add("ingot", 15);
            P.Bag.Add("parts", 5);
            Do(new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id }, a.X, a.Z);
            return W.Trains.Values.Last();
        }
    }

    private static Yard NewYard()
    {
        var (w, p) = Fixture.NewWorld("player-a", 40, -120);
        w.Upgrades.Add("tech_rail");
        w.Credits = 1_000_000;
        p.Bag.Add("recovered", 2000);
        p.Bag.Add("ingot", 2000);
        p.Bag.Add("parts", 500);
        w.Reindex();
        foreach (var d in w.DebrisById.Values.Where(d => d.Z is > -110 and < -10).ToList()) w.DebrisById.Remove(d.Id);
        w.Reindex();
        return new Yard { W = w, P = p };
    }

    /// <summary>Estación A en x=10 y B en x=64, ambas sobre z=-50. Devuelve los andenes unidos por la línea que se construya.</summary>
    private static (Yard y, Machine a, Machine b) TwoEnds()
    {
        var y = NewYard();
        var a = y.Station(10, Z - 2);
        var b = y.Station(60, Z - 2);
        Assert.Equal(Z, y.W.RailNodes[a.RailB].Z);
        Assert.Equal(14, y.W.RailNodes[a.RailB].X);
        Assert.Equal(60, y.W.RailNodes[b.RailA].X);
        return (y, a, b);
    }

    private static void Line(Yard y, params int[] xs)
    {
        for (var i = 0; i + 1 < xs.Length; i++)
        {
            var from = xs[i];
            for (; xs[i + 1] - from > 24; from += 24) y.Rail(from, Z, from + 24, Z);
            y.Rail(from, Z, xs[i + 1], Z);
        }
    }

    /// <summary>Ningún tramo ni nodo por el que pasen dos trenes a la vez, con cola incluida. Comprobado sobre lo que el servidor considera ocupado.</summary>
    private static void AssertExclusive(World w)
    {
        var seen = new Dictionary<int, int>();
        foreach (var t in w.Trains.Values)
        {
            if (t.State is not (TrainState.Moving or TrainState.AtSignal)) continue;
            if (t.PathIndex >= t.Path.Count) continue;
            var current = t.Path[t.PathIndex];
            Assert.False(seen.TryGetValue(current, out var other), $"tramo {current} con los trenes {other} y {t.Id}");
            seen[current] = t.Id;
            Assert.Equal(t.Id, w.HolderOf(current));
            foreach (var crossing in w.CrossingsOf(current))
                Assert.False(seen.TryGetValue(crossing, out var cross), $"cruce {current}/{crossing} con los trenes {cross} y {t.Id}");
        }
    }

    [Fact]
    public void Without_signals_a_train_reserves_its_whole_trip_as_before()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 30, 44, 60);
        a.In.Add("ore", 200);
        var t = y.Train(a, b);
        for (var i = 0; i < 300 && t.State != TrainState.Moving; i++) y.W.Step();
        Assert.Equal(TrainState.Moving, t.State);
        Assert.Equal(t.Path.Count, t.GrantEnd);
        Assert.All(t.Path, id => Assert.Equal(t.Id, y.W.HolderOf(id)));
    }

    [Fact]
    public void A_signal_splits_the_reservation_and_lets_a_second_train_follow()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 30, 44, 60);
        y.Signal(30, Z);
        y.Signal(44, Z);
        b.StationUnload = true;
        a.In.Add("ore", 800);
        var first = y.Train(a, b);
        var second = y.Train(a, b);

        var bothOnTrack = false;
        for (var i = 0; i < 5000 && y.Drained < 400; i++)
        {
            y.Step(b);
            AssertExclusive(y.W);
            if (first.State == TrainState.Moving) Assert.True(first.GrantEnd <= first.Path.Count);
            bothOnTrack |= first.State is TrainState.Moving or TrainState.AtSignal
                && second.State is TrainState.Moving or TrainState.AtSignal
                && first.HeadingToB == second.HeadingToB;
        }
        Assert.True(bothOnTrack, "con señales, dos trenes deben ir a la vez por la misma línea y en el mismo sentido");
        Assert.True(y.Drained >= 400);
        Assert.Equal(800, y.W.CountEverywhere("ore") + y.Drained);
    }

    [Fact]
    public void A_train_runs_through_a_green_signal_without_stopping()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 30, 60);
        y.Signal(30, Z);
        a.In.Add("ore", 200);
        var t = y.Train(a, b);
        for (var i = 0; i < 300 && t.State != TrainState.Moving; i++) y.W.Step();
        Assert.Equal(TrainState.Moving, t.State);
        for (var i = 0; i < 100 && t.State == TrainState.Moving; i++)
        {
            Assert.NotEqual(TrainState.AtSignal, t.State);
            y.W.Step();
        }
        Assert.NotEqual(TrainState.AtSignal, t.State);
    }

    [Fact]
    public void Head_on_trains_on_a_single_line_with_signals_never_deadlock()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 30, 44, 60);
        y.Signal(30, Z);
        y.Signal(44, Z);
        b.StationUnload = true;
        a.In.Add("ore", 800);
        y.Train(a, b);
        y.Train(b, a);
        y.Train(a, b);
        var waited = false;
        for (var i = 0; i < 40000 && y.Drained < 800; i++)
        {
            y.Step(b);
            AssertExclusive(y.W);
            waited |= y.W.Trains.Values.Any(t => t.WaitReason == "wait_block");
        }
        Assert.True(waited, "la prueba debe ejercer la espera");
        Assert.Equal(800, y.Drained);
    }

    /// <summary>Línea con un apartadero entre x=24 y x=44: dos vías, señales en las dos entradas.</summary>
    private static (Yard y, Machine a, Machine b, RailSegment straight, RailSegment curveIn, RailSegment curveOut) PassingLoop()
    {
        var (y, a, b) = TwoEnds();
        y.Rail(14, Z, 24, Z);
        var straight = y.Rail(24, Z, 44, Z);
        y.Rail(44, Z, 60, Z);
        var curveIn = y.Rail(24, Z, 34, Z + 10);
        var curveOut = y.Rail(34, Z + 10, 44, Z);
        y.Signal(24, Z);
        y.Signal(44, Z);
        return (y, a, b, straight, curveIn, curveOut);
    }

    [Fact]
    public void Opposing_trains_pass_each_other_on_a_passing_loop()
    {
        var (y, a, b, straight, curveIn, curveOut) = PassingLoop();
        b.StationUnload = true;
        a.In.Add("ore", 600);
        b.In.Add("ore", 0);
        var east = y.Train(a, b);
        // El segundo sale con su carga hacia A cuando el primero ya está en el apartadero.
        var west = y.Train(b, a);
        west.Cargo.Add("ore", 200);
        var total = y.W.CountEverywhere("ore");
        var crossed = false;
        for (var i = 0; i < 6000 && !crossed; i++)
        {
            y.W.Step();
            AssertExclusive(y.W);
            Assert.Equal(total, y.W.CountEverywhere("ore"));
            var onLoop = new[] { straight.Id, curveIn.Id, curveOut.Id };
            bool InLoop(Train t) => t.State is TrainState.Moving or TrainState.AtSignal && t.PathIndex < t.Path.Count && onLoop.Contains(t.Path[t.PathIndex]);
            crossed = InLoop(east) && InLoop(west);
        }
        Assert.True(crossed, $"los dos trenes deben estar a la vez en el apartadero: este {east.State} {east.WaitReason} {east.PathIndex}/{east.GrantEnd}/{east.Path.Count}; oeste {west.State} {west.WaitReason} {west.PathIndex}/{west.GrantEnd}/{west.Path.Count}");
    }

    [Fact]
    public void Loop_keeps_delivering_with_several_trains_in_both_directions()
    {
        var (y, a, b, _, _, _) = PassingLoop();
        b.StationUnload = true;
        a.In.Add("ore", 1200);
        var total = y.W.CountEverywhere("ore");
        y.Train(a, b);
        y.Train(b, a);
        y.Train(a, b);
        y.Train(b, a);
        for (var i = 0; i < 40000 && y.Drained < 1200; i++)
        {
            y.Step(b);
            AssertExclusive(y.W);
        }
        Assert.Equal(1200, y.Drained);
        Assert.Equal(total, y.W.CountEverywhere("ore") + y.Drained);
    }

    [Fact]
    public void Routes_are_deterministic_and_ties_go_to_the_lowest_segment_id()
    {
        var y = NewYard();
        var a = y.Station(10, Z - 2);
        var b = y.Station(60, Z - 2);
        y.Rail(14, Z, 24, Z);
        y.Rail(44, Z, 60, Z);
        // Dos vías de la misma longitud por lados opuestos.
        var north = y.Rail(24, Z, 34, Z - 10);
        var northOut = y.Rail(34, Z - 10, 44, Z);
        y.Rail(24, Z, 34, Z + 10);
        y.Rail(34, Z + 10, 44, Z);
        var path = y.W.FindPath(a.RailB, b.Id)!;
        Assert.Contains(north.Id, path);
        Assert.Contains(northOut.Id, path);
        var clone = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(y.W));
        Assert.Equal(path, clone.FindPath(a.RailB, b.Id));
    }

    [Fact]
    public void Trains_never_occupy_a_crossing_at_the_same_time()
    {
        var y = NewYard();
        var a = y.Station(10, Z - 2);
        var b = y.Station(60, Z - 2);
        Line(y, 14, 38, 60);
        var c = y.Station(30, Z - 32);
        var d = y.Station(30, Z + 30);
        var north = y.Rail(34, Z - 30, 40, Z - 30);
        y.Rail(40, Z - 30, 40, Z - 10);
        var cross = y.Rail(40, Z - 10, 40, Z + 10);
        y.Rail(40, Z + 10, 40, Z + 32);
        var south = y.Rail(40, Z + 32, 34, Z + 32);
        Assert.NotEmpty(y.W.CrossingsOf(cross.Id));
        b.StationUnload = true;
        d.StationUnload = true;
        a.In.Add("ore", 800);
        c.In.Add("ore", 800);
        y.Train(a, b);
        y.Train(c, d);
        var waited = false;
        for (var i = 0; i < 20000 && y.Drained < 1200; i++)
        {
            y.Step(b, d);
            AssertExclusive(y.W);
            waited |= y.W.Trains.Values.Any(t => t.WaitReason == "wait_block");
        }
        Assert.True(waited, "la prueba debe ejercer la espera en el cruce");
        Assert.True(y.Drained >= 1200);
        Assert.NotNull(north);
        Assert.NotNull(south);
    }

    [Fact]
    public void Diagonals_crossing_exactly_on_a_corner_are_a_crossing()
    {
        var y = NewYard();
        var one = y.Rail(20, Z, 30, Z + 10);
        var two = y.Rail(20, Z + 10, 30, Z);
        Assert.Contains(two.Id, y.W.CrossingsOf(one.Id));
    }

    [Fact]
    public void A_train_waits_at_the_signal_while_the_destination_station_works()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 44, 60);
        y.Signal(44, Z);
        b.StationUnload = true;
        a.In.Add("ore", 400);
        var first = y.Train(a, b);
        var second = y.Train(a, b);
        // Andén de destino lleno: el primero se queda descargando y la estación sigue ocupada.
        b.In.Add("ore", 400);
        var reason = "";
        for (var i = 0; i < 4000 && reason != "wait_station_busy"; i++)
        {
            y.W.Step();
            AssertExclusive(y.W);
            if (second.State == TrainState.AtSignal) reason = second.WaitReason;
        }
        Assert.Equal(TrainState.Unloading, first.State);
        Assert.Equal("wait_station_busy", reason);
        var limit = y.W.StopLimit(second);
        Assert.NotNull(limit);
        Assert.Equal(limit, second.PosMm);

        // La espera tiene un límite: si la estación no se libera, entra a la cola de la parada y deja libre la vía.
        var max = y.W.Content.Balance.Rail.StationWaitMaxTicks;
        for (var i = 0; i < max + 400 && second.State == TrainState.AtSignal; i++) y.W.Step();
        Assert.NotEqual(TrainState.AtSignal, second.State);
        for (var i = 0; i < 300 && second.State == TrainState.Moving; i++) y.W.Step();
        Assert.Equal(1, y.W.PlatformSlot(second));
        Assert.Equal(TrainState.Unloading, second.State);
    }

    [Fact]
    public void Leaving_train_blocked_by_the_one_at_the_signal_does_not_deadlock_a_single_line()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 44, 60);
        y.Signal(44, Z);
        b.StationUnload = true;
        a.In.Add("ore", 1000);
        y.Train(a, b);
        y.Train(a, b);
        y.Train(a, b);
        var total = y.W.CountEverywhere("ore");
        for (var i = 0; i < 40000 && y.Drained < 1000; i++)
        {
            y.Step(b);
            AssertExclusive(y.W);
        }
        Assert.Equal(1000, y.Drained);
        Assert.Equal(total, y.W.CountEverywhere("ore") + y.Drained);
    }

    [Fact]
    public void Save_and_load_while_stopped_at_a_signal_resumes_identically()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 44, 60);
        y.Signal(44, Z);
        b.StationUnload = true;
        b.In.Add("ore", 400);
        a.In.Add("ore", 400);
        y.Train(a, b);
        var second = y.Train(a, b);
        for (var i = 0; i < 4000 && second.State != TrainState.AtSignal; i++) y.W.Step();
        Assert.Equal(TrainState.AtSignal, second.State);

        var loaded = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(y.W));
        var copy = loaded.Trains[second.Id];
        Assert.Equal(second.GrantEnd, copy.GrantEnd);
        Assert.Equal(second.HoldTicks, copy.HoldTicks);
        Assert.Equal(y.W.HolderOf(second.Path[second.PathIndex]), loaded.HolderOf(second.Path[second.PathIndex]));
        Assert.Contains(loaded.RailNodes.Values, n => n.Signal);
        Assert.Equal(WorldCodec.Hash(y.W), WorldCodec.Hash(loaded));
        for (var i = 0; i < 2000; i++)
        {
            y.W.Step();
            loaded.Step();
            Assert.Equal(WorldCodec.Hash(y.W), WorldCodec.Hash(loaded));
        }
        foreach (var t in y.W.Trains.Values)
        {
            var other = loaded.Trains[t.Id];
            Assert.Equal((t.State, t.PathIndex, t.GrantEnd, t.PosMm, t.CurrentNode, t.Forward, t.Timer, t.HoldTicks), (other.State, other.PathIndex, other.GrantEnd, other.PosMm, other.CurrentNode, other.Forward, other.Timer, other.HoldTicks));
            Assert.Equal(t.Path, other.Path);
        }
    }

    [Fact]
    public void Real_v5_save_with_a_train_under_way_migrates_and_keeps_working()
    {
        var root = Path.Combine(Path.GetTempPath(), "pluton-signals-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SaveStore("test", root);
            var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "save-v5-alpha-0.5.0-qa5-rail.plsave"));
            Assert.Equal(5, BitConverter.ToInt32(original, 4));
            store.Write("real5", original);
            var w = store.Load("real5", Fixture.Content);
            Assert.Equal(original, File.ReadAllBytes(store.PathOf("real5") + ".v5.bak"));

            Assert.All(w.RailNodes.Values, n => Assert.False(n.Signal));
            var moving = w.Trains.Values.Single(t => t.State == TrainState.Moving);
            Assert.Equal(moving.Path.Count, moving.GrantEnd);
            Assert.All(moving.Path.Skip(moving.PathIndex), id => Assert.Equal(moving.Id, w.HolderOf(id)));
            Assert.Equal(TrainState.Waiting, w.Trains.Values.Single(t => t.Id != moving.Id).State);

            var destination = w.Machines[moving.StationB];
            var total = w.CountEverywhere("ore");
            for (var i = 0; i < 12000 && destination.In.Get("ore") < 240; i++) w.Step();
            Assert.Equal(240, destination.In.Get("ore"));
            Assert.Equal(total, w.CountEverywhere("ore"));

            store.Save("real5", w);
            Assert.Equal(WorldCodec.FormatVersion, BitConverter.ToInt32(File.ReadAllBytes(store.PathOf("real5")), 4));
            Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(store.Load("real5", Fixture.Content)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Placing_a_signal_costs_and_refunds_and_explains_errors()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 38, 60);
        var cost = y.W.Content.Balance.Rail.SignalCredits;
        var credits = y.W.Credits;

        Assert.Equal("err_signal_no_rail", y.Try(new Command { Type = CommandType.ToggleSignal, A = 30, B = Z + 7 }, 30, Z + 7));
        Assert.Equal("err_range", y.W.Apply(Far(y), new Command { Type = CommandType.ToggleSignal, A = 30, B = Z }).Error);
        Assert.Equal(credits, y.W.Credits);

        var segments = y.W.RailSegments.Count;
        y.Signal(30, Z);
        Assert.Equal(credits - cost, y.W.Credits);
        Assert.Equal(segments + 1, y.W.RailSegments.Count);
        var nodeId = y.W.RailNodes.Values.Single(n => n.X == 30 && n.Z == Z).Id;
        Assert.True(y.W.RailNodes[nodeId].Signal);
        Assert.Contains(nodeId, y.W.DirtyRailNodes);

        y.Signal(30, Z);
        Assert.False(y.W.RailNodes[nodeId].Signal);
        Assert.Equal(credits, y.W.Credits);

        y.W.Upgrades.Remove("tech_rail");
        Assert.Equal("err_locked", y.Try(new Command { Type = CommandType.ToggleSignal, A = 30, B = Z }, 30, Z));
    }

    private static PlayerState Far(Yard y)
    {
        var p = Fixture.AddPlayer(y.W, "far", 200, 200);
        return p;
    }

    [Fact]
    public void Removing_the_rail_under_a_signal_returns_its_cost()
    {
        var y = NewYard();
        var loose = y.Rail(20, Z, 30, Z);
        var credits = y.W.Credits;
        y.Signal(20, Z);
        y.Do(new Command { Type = CommandType.RemoveRail, A = loose.Id }, 20, Z);
        Assert.Equal(credits + 10 * y.W.Content.Balance.Rail.CreditsPerCell, y.W.Credits);
        Assert.Empty(y.W.RailNodes.Values.Where(n => n.X == 20 && n.Z == Z));
    }

    [Fact]
    public void Branching_from_the_middle_of_a_straight_creates_a_real_junction()
    {
        var (y, a, b) = TwoEnds();
        var main = y.Rail(14, Z, 38, Z);
        Line(y, 38, 60);
        var c = y.Station(30, Z - 16);
        var branch = y.Rail(30, Z - 14, 30, Z);
        Assert.False(y.W.RailSegments.ContainsKey(main.Id));
        var node = y.W.RailNodes.Values.Single(n => n.X == 30 && n.Z == Z);
        Assert.Equal(3, y.W.RailDegree(node.Id));
        Assert.NotNull(y.W.FindPath(a.RailB, c.Id));
        Assert.NotNull(y.W.FindPath(b.RailA, c.Id));
        Assert.NotNull(y.W.FindPath(a.RailB, b.Id));
        Assert.NotNull(branch);
        // La longitud se conserva al partir.
        Assert.Equal(46 * 2000, y.W.FindPath(a.RailB, b.Id)!.Sum(id => y.W.RailSegments[id].LengthMm));
    }

    [Fact]
    public void Junctions_cannot_split_a_platform_or_a_section_a_train_has_reserved()
    {
        var (y, a, b) = TwoEnds();
        var main = y.Rail(14, Z, 38, Z);
        Line(y, 38, 60);
        Assert.Equal("err_rail_platform", y.Try(new Command { Type = CommandType.PlaceRail, A = 12, B = Z, C = 12, D = Z + 10 }, 12, Z));
        a.In.Add("ore", 200);
        var t = y.Train(a, b);
        for (var i = 0; i < 300 && t.State != TrainState.Moving; i++) y.W.Step();
        Assert.Equal(TrainState.Moving, t.State);
        Assert.Equal("err_rail_occupied", y.Try(new Command { Type = CommandType.ToggleSignal, A = 40, B = Z }, 40, Z));
        Assert.Equal("err_rail_occupied", y.Try(new Command { Type = CommandType.PlaceRail, A = 50, B = Z, C = 50, D = Z + 8 }, 50, Z));
        Assert.True(y.W.RailSegments.ContainsKey(main.Id));
    }

    [Fact]
    public void A_removed_train_frees_the_track_it_had_reserved()
    {
        var (y, a, b) = TwoEnds();
        var main = y.Rail(14, Z, 38, Z);
        Line(y, 38, 60);
        a.In.Add("ore", 200);
        var t = y.Train(a, b);
        for (var i = 0; i < 300 && t.State != TrainState.Moving; i++) y.W.Step();
        Assert.Equal(t.Id, y.W.HolderOf(main.Id));
        y.Do(new Command { Type = CommandType.RemoveTrain, A = t.Id }, 40, Z);
        Assert.Equal(-1, y.W.HolderOf(main.Id));
    }

    [Fact]
    public void Signal_light_follows_the_adjacent_track_and_replicas_agree()
    {
        var (y, a, b) = TwoEnds();
        Line(y, 14, 30, 44, 60);
        y.Signal(44, Z);
        var node = y.W.RailNodes.Values.Single(n => n.X == 44 && n.Z == Z).Id;
        Assert.Equal(SignalLight.Clear, y.W.SignalLightOf(node));
        b.StationUnload = true;
        b.In.Add("ore", 400);
        a.In.Add("ore", 400);
        y.Train(a, b);
        var second = y.Train(a, b);
        var red = false;
        for (var i = 0; i < 4000 && second.State != TrainState.AtSignal; i++)
        {
            y.W.Step();
            red |= y.W.SignalLightOf(node) == SignalLight.Stop;
        }
        Assert.True(red);
        Assert.Equal(TrainState.AtSignal, second.State);
        Assert.Equal(SignalLight.Stop, y.W.SignalLightOf(node));
        var mirror = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(y.W));
        Assert.Equal(SignalLight.Stop, mirror.SignalLightOf(node));
    }

    /// <summary>
    /// Redes al azar con semilla fija: una línea con apartaderos y ramales, señales repartidas y trenes en los dos sentidos.
    /// Lo que se comprueba es lo que un jugador sufriría: ningún tramo compartido, carga conservada y ningún tren parado para siempre.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void Random_networks_with_competing_trains_never_stall(int seed) => RunRandomNetwork(seed);

    [Fact]
    public void Random_networks_really_exercise_signal_stops_and_detours()
    {
        var stops = 0;
        var detours = 0;
        for (var seed = 1; seed <= 12; seed++)
        {
            var (s, d) = RunRandomNetwork(seed);
            stops += s;
            detours += d;
        }
        Assert.True(stops > 100, $"paradas ante señal: {stops}");
        Assert.True(detours > 5, $"rutas por vía alternativa: {detours}");
    }

    private static (int stops, int detours) RunRandomNetwork(int seed)
    {
        var random = new Random(seed);
        var y = NewYard();
        var stations = new List<Machine> { y.Station(10, Z - 2), y.Station(84, Z - 2) };
        var x = 14;
        var waypoints = new List<int> { x };
        while (x < 84)
        {
            var step = Math.Min(84 - x, 10 + random.Next(4) * 4);
            if (84 - x - step is > 0 and < 8) step = 84 - x <= 24 ? 84 - x : step - 8;
            waypoints.Add(x + step);
            x += step;
        }
        for (var i = 0; i + 1 < waypoints.Count; i++)
        {
            var from = waypoints[i];
            var to = waypoints[i + 1];
            y.Rail(from, Z, to, Z);
            // Apartadero en algunos tramos.
            if ((to - from) % 2 == 0 && to - from >= 12 && random.Next(100) < 45)
            {
                var half = (to - from) / 2;
                y.Rail(from, Z, from + half, Z + half);
                y.Rail(from + half, Z + half, to, Z);
            }
            if (i + 2 < waypoints.Count && random.Next(100) < 70) y.Signal(to, Z);
        }
        // Una rama con su propia estación en una de las uniones.
        var branchAt = waypoints[1 + random.Next(waypoints.Count - 2)];
        var east = y.Station(branchAt + 2, Z - 30);
        y.Rail(branchAt, Z, branchAt, Z - 14);
        y.Rail(branchAt, Z - 14, branchAt, Z - 28);
        y.Rail(branchAt, Z - 28, branchAt + 2, Z - 28);
        stations.Add(east);

        for (var i = 0; i < stations.Count; i++)
        {
            stations[i].StationUnload = i == 1;
            stations[i].In.Add("ore", 400);
        }
        var total = y.W.CountEverywhere("ore");
        var count = 3 + random.Next(5);
        var trains = new List<Train>();
        for (var i = 0; i < count; i++)
        {
            var one = stations[random.Next(stations.Count)];
            var two = stations.First(s => s != one && random.Next(2) == 0 || s == stations[(stations.IndexOf(one) + 1) % stations.Count]);
            trains.Add(y.Train(one, two));
        }

        // Todos los trenes tienen que completar viajes; un tren detenido demasiado tiempo en la vía cuenta como atasco.
        var arrivals = trains.ToDictionary(t => t.Id, _ => 0);
        var wasHeading = trains.ToDictionary(t => t.Id, t => t.HeadingToB);
        var stopped = trains.ToDictionary(t => t.Id, _ => 0);
        var limit = y.W.Content.Balance.Rail.StationWaitMaxTicks + 6000;
        var stops = 0;
        var detours = 0;
        var wasStopped = trains.ToDictionary(t => t.Id, _ => false);
        var routed = trains.ToDictionary(t => t.Id, _ => new List<int>());
        for (var i = 0; i < 40000; i++)
        {
            y.Step(stations[1]);
            AssertExclusive(y.W);
            // Los orígenes no se agotan: lo que se mide es la circulación, no cuánta carga había.
            if (i % 200 == 0)
                foreach (var source in stations)
                {
                    if (source == stations[1] || source.In.Get("ore") >= 200) continue;
                    source.In.Add("ore", 200);
                    total += 200;
                }
            foreach (var t in trains)
            {
                if (t.HeadingToB != wasHeading[t.Id])
                {
                    wasHeading[t.Id] = t.HeadingToB;
                    arrivals[t.Id]++;
                }
                stopped[t.Id] = t.State == TrainState.AtSignal ? stopped[t.Id] + 1 : 0;
                if (t.State == TrainState.AtSignal && !wasStopped[t.Id]) stops++;
                wasStopped[t.Id] = t.State == TrainState.AtSignal;
                if (t.State is TrainState.Moving or TrainState.AtSignal && t.PathIndex < t.Path.Count && !routed[t.Id].SequenceEqual(t.Path))
                {
                    routed[t.Id] = t.Path.ToList();
                    var from = t.CurrentNode;
                    var shortest = y.W.FindPath(from, t.HeadingToB ? t.StationB : t.StationA);
                    if (shortest != null && !t.Path.Skip(t.PathIndex).SequenceEqual(shortest)) detours++;
                }
                Assert.True(stopped[t.Id] < limit, $"semilla {seed}: tren {t.Id} detenido ante una señal {stopped[t.Id]} ticks: {t.WaitReason}");
            }
        }
        Assert.Equal(total, y.W.CountEverywhere("ore") + y.Drained);
        foreach (var t in trains) Assert.True(arrivals[t.Id] >= 2, $"semilla {seed}: tren {t.Id} solo llegó {arrivals[t.Id]} veces, estado {t.State} {t.WaitReason}");
        Assert.Equal(WorldCodec.Hash(y.W), WorldCodec.Hash(WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(y.W))));
        return (stops, detours);
    }

    [Fact]
    public void Signals_and_stopped_trains_replicate_to_clients_with_matching_hashes()
    {
        var y = NewYard();
        var a = y.Station(10, Z - 2);
        var b = y.Station(60, Z - 2);
        Line(y, 14, 44, 60);
        b.StationUnload = true;
        b.In.Add("ore", 400);
        a.In.Add("ore", 400);
        y.W.Players.Remove(y.P.Key);
        y.W.ClearReplicationState();

        using var h = new NetHarness(new ServerOptions { CheckEveryTicks = 5 }, y.W);
        var one = h.Join("client-identity-aaaa");
        var two = h.Join("client-identity-bbbb");
        var server = h.Server.World.Players["client-identity-aaaa"];
        server.X = 88;
        server.Z = Z * 2;
        server.Bag.Add("ingot", 100);
        server.Bag.Add("parts", 100);
        h.Server.World.DirtyPlayers.Add(server.Key);
        h.Settle(100);
        void Wait(string stage, Func<bool> until, int ms = 8000)
        {
            try { h.Pump(until, ms); }
            catch (Exception e)
            {
                var trains = string.Join(" | ", h.Server.World.Trains.Values.Select(t => $"{t.Id}:{t.State}:{t.WaitReason}:{t.Cargo.Total}"));
                throw new Exception($"etapa {stage}; trenes {trains}", e);
            }
        }
        CommandResult? last = null;
        one.ResultReceived += (_, r) => last = r;

        // Por orden de cliente: la autoridad es el servidor.
        one.Send(new Command { Type = CommandType.ToggleSignal, A = 44, B = Z });
        Wait("resultado señal", () => last != null);
        Assert.True(last!.Value.Ok, last.Value.Error);
        Wait("réplica de la señal", () => two.World!.RailNodes.Values.Any(n => n.X == 44 && n.Z == Z && n.Signal));

        one.Send(new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id });
        one.Send(new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id });
        Wait("dos trenes", () => h.Server.World.Trains.Count == 2);
        var second = h.Server.World.Trains.Keys.Last();
        Wait("en la señal", () => h.Server.World.Trains[second].State == TrainState.AtSignal, 60000);
        Wait("réplica del tren en la señal", () => two.World!.Trains.TryGetValue(second, out var r) && r.State == TrainState.AtSignal);
        Assert.Equal(h.Server.World.Trains[second].GrantEnd, two.World!.Trains[second].GrantEnd);
        Assert.Equal(h.Server.World.Trains[second].Path, two.World.Trains[second].Path);
        Assert.Equal("wait_station_busy", two.World.Trains[second].WaitReason);
        var node = two.World.RailNodes.Values.Single(n => n.X == 44 && n.Z == Z).Id;
        Assert.Equal(SignalLight.Stop, two.World.SignalLightOf(node));

        var before = one.HashChecks;
        h.Pump(() => one.HashChecks >= before + 3 && two.HashChecks >= 3, 20000);
        Assert.Equal(0, one.HashMismatches);
        Assert.Equal(0, two.HashMismatches);
    }
}
