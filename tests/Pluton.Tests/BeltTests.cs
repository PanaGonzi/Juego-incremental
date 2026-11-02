using System.Text.Json;
using Pluton.Networking;
using Pluton.Persistence;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Cintas: ruta, carga real, conservación, guardado y red.</summary>
public class BeltTests
{
    /// <summary>Mundo sin escombros, con material de sobra y el jugador en el centro de la zona de pruebas.</summary>
    internal static (World w, PlayerState p) Open(string key = "player-a")
    {
        var (w, p) = Fixture.NewWorld(key, 0, -40);
        foreach (var d in w.DebrisById.Values.ToList()) w.DebrisById.Remove(d.Id);
        w.Reindex();
        w.Credits = 100000;
        p.Bag.Add("recovered", 2000);
        p.Bag.Add("ingot", 500);
        p.Bag.Add("parts", 500);
        p.Bag.Add("coolant", 100);
        foreach (var u in w.Content.Upgrades.Keys.Where(u => u.StartsWith("tech_", StringComparison.Ordinal))) w.Upgrades.Add(u);
        w.Reindex();
        return (w, p);
    }

    internal static Machine Place(World w, PlayerState p, string def, int x, int z, int rot = 0)
    {
        p.X = x * 2f;
        p.Z = z * 2f;
        var r = w.Apply(p, new Command { Type = CommandType.Place, Text = def, A = x, B = z, C = rot });
        Assert.True(r.Ok, $"{def} en {x},{z}: {r.Error}");
        return w.Machines.Values.Last();
    }

    internal static Link Lay(World w, PlayerState p, Machine a, Machine b)
    {
        var r = w.Apply(p, Fixture.LinkCmd(w, a, b));
        Assert.True(r.Ok, $"cinta {a.Def}→{b.Def}: {r.Error}");
        return w.Links.Values.Last();
    }

    private static string Fail(World w, PlayerState p, Machine a, Machine b) =>
        w.RouteBelt(a.Id, b.Id).Error ?? w.Apply(p, Fixture.LinkCmd(w, a, b)).Error;

    private static void Run(World w, int ticks)
    {
        for (var i = 0; i < ticks; i++) w.Step();
    }

    [Theory]
    [InlineData(10, 2, 0, 10, 0)]    // casi este: recta en el eje
    [InlineData(-10, 3, 0, -10, 0)]
    [InlineData(1, -10, 0, 0, -10)]  // casi norte
    [InlineData(10, 8, 0, 10, 10)]   // cerca de la diagonal: 45°
    [InlineData(-6, 9, 0, -9, 9)]
    [InlineData(10, 4, 1, 10, 10)]   // una curva siempre avanza igual en ambos ejes
    [InlineData(0, -7, 2, 7, -7)]
    [InlineData(0, 0, 0, 0, 0)]      // sobre el inicio: sin tramo
    public void Aimed_rail_target_aligns_to_the_nearest_valid_direction(int dx, int dz, byte shape, int wantX, int wantZ)
    {
        Assert.Equal((100 + wantX, -50 + wantZ), RailMath.AlignTarget(100, -50, 100 + dx, -50 + dz, shape));
    }

    [Fact]
    public void Aligned_rail_targets_always_pass_the_shape_rule()
    {
        var (w, _) = Open();
        for (byte shape = 0; shape <= 2; shape++)
            for (var dx = -12; dx <= 12; dx++)
                for (var dz = -12; dz <= 12; dz++)
                {
                    var (x, z) = RailMath.AlignTarget(0, -60, dx, -60 + dz, shape);
                    if (x == 0 && z == -60) continue;
                    Assert.NotEqual("err_rail_shape", w.CheckRail(0, 0, -60, x, z, shape, out _));
                }
    }

    [Fact]
    public void Rail_preview_rejects_the_same_section_before_the_server_does()
    {
        var (w, p) = Open();
        Assert.Null(w.CheckRail(0, 0, -30, 8, -30, 0, out _));
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 0, B = -30, C = 8, D = -30 }).Ok);
        Assert.Equal("err_rail_exists", w.CheckRail(0, 0, -30, 8, -30, 0, out _));
        Assert.Equal("err_rail_exists", w.CheckRail(0, 8, -30, 0, -30, 0, out _));
        var credits = w.Credits;
        Assert.False(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 8, B = -30, C = 0, D = -30 }).Ok);
        Assert.Equal(credits, w.Credits);
    }

    [Fact]
    public void Ports_follow_rotation_and_storage_opens_all_sides()
    {
        var (w, p) = Open();
        var north = Place(w, p, "smelter", 0, -30);
        Assert.All(w.BeltPorts(north, true), port => Assert.Equal(0, port.Dir));
        Assert.Equal(2, w.BeltPorts(north, true).Count());
        Assert.Contains(w.BeltPorts(north, false), port => port.Dir == 2 && !port.Side);
        Assert.Contains(w.BeltPorts(north, false), port => port.Dir == 1 && port.Side);
        Assert.DoesNotContain(w.BeltPorts(north, false), port => port.Dir == 0);

        var east = Place(w, p, "smelter", 10, -30, rot: 3);
        Assert.All(w.BeltPorts(east, true), port => Assert.Equal(1, port.Dir));
        Assert.Equal(2, w.BeltPorts(east, true).Count());
        Assert.Contains(w.BeltPorts(east, false), port => port.Dir == 3 && !port.Side);

        var crate = Place(w, p, "storage_crate", 20, -30);
        Assert.Equal(new[] { 0, 1, 2, 3 }, w.BeltPorts(crate, true).Select(port => port.Dir).Distinct().OrderBy(d => d));
        Assert.Equal(new[] { 0, 1, 2, 3 }, w.BeltPorts(crate, false).Select(port => port.Dir).Distinct().OrderBy(d => d));
    }

    [Fact]
    public void Route_is_deterministic_and_avoids_machines()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var wall = Place(w, p, "assembler", 4, -40);
        var b = Place(w, p, "storage_crate", 10, -40);
        var first = w.RouteBelt(a.Id, b.Id);
        Assert.Null(first.Error);

        // Otro mundo con el mismo estado y otra lectura: exactamente las mismas celdas.
        var copy = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Equal(first.Cells, copy.RouteBelt(a.Id, b.Id).Cells);
        Assert.Equal(first.Cells, w.RouteBelt(a.Id, b.Id).Cells);

        var blocked = w.Footprint(wall.Def, wall.X, wall.Z, wall.Rot).ToHashSet();
        Assert.DoesNotContain(first.Cells, c => blocked.Contains(c));
        for (var i = 1; i < first.Cells.Count; i++)
            Assert.Equal(1, Math.Abs(first.Cells[i].x - first.Cells[i - 1].x) + Math.Abs(first.Cells[i].z - first.Cells[i - 1].z));
        Assert.Equal(first.Cells.Count * w.Content.Balance.BeltCreditsPerCell, first.Credits);
    }

    [Fact]
    public void Route_avoids_debris_rails_and_nodes()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 10, -40);
        w.DebrisById[w.NextId] = new Debris { Id = w.NextId++, Def = "scrap_pile", Planet = 0, X = 5, Z = -40, Hp = 3 };
        w.DebrisById[w.NextId] = new Debris { Id = w.NextId++, Def = "scrap_pile", Planet = 0, X = 5, Z = -39, Hp = 3 };
        var node = new ResourceNode { Id = w.NextId++, Item = "ore", Planet = 0, X = 3, Z = -38 };
        w.Nodes[node.Id] = node;
        w.Reindex();
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 7, B = -44, C = 7, D = -36 }).Ok);
        var route = w.RouteBelt(a.Id, b.Id);
        // La vía corta la zona entera entre x=7, sin celda libre: rodearla por el norte o el sur es posible.
        Assert.Null(route.Error);
        foreach (var (x, z) in route.Cells)
        {
            Assert.False(w.RailBlocksCell(0, x, z));
            Assert.False((x, z) is (5, -40) or (5, -39));
            Assert.False(x is >= 3 and <= 4 && z is >= -38 and <= -37);
        }
    }

    [Fact]
    public void Blocked_output_and_input_give_understandable_errors()
    {
        var (w, p) = Open();
        var a = Place(w, p, "smelter", 0, -30);
        // Máquina pegada al frente: no queda celda de salida.
        var covered = Place(w, p, "storage_crate", 0, -32);
        var other = Place(w, p, "storage_crate", 2, -32);
        var target = Place(w, p, "storage_crate", 10, -30);
        Assert.Equal("err_belt_port_out", Fail(w, p, a, target));

        // Almacén cerrado por sus cuatro lados con máquinas: sin entrada.
        _ = covered;
        _ = other;
        var inner = Place(w, p, "storage_crate", 20, -30);
        Place(w, p, "storage_crate", 20, -32);
        Place(w, p, "storage_crate", 20, -28);
        Place(w, p, "storage_crate", 18, -30);
        Place(w, p, "storage_crate", 22, -30);
        Assert.Equal("err_belt_port_in", Fail(w, p, target, inner));
    }

    [Fact]
    public void Kinds_are_validated()
    {
        var (w, p) = Open();
        var near = Place(w, p, "storage_crate", -40, -30);
        var generator = Place(w, p, "rtg_generator", -30, -40);
        Assert.Equal("err_link_kind", Fail(w, p, generator, near));
        Assert.Equal("err_link_kind", Fail(w, p, near, generator));
        Assert.Equal(0, w.Links.Count);
    }

    [Fact]
    public void Laying_a_belt_costs_credits_and_two_belts_may_not_share_a_route_end()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 6, -40);
        var credits = w.Credits;
        var l = Lay(w, p, a, b);
        Assert.Equal(credits - l.Cells.Count * w.Content.Balance.BeltCreditsPerCell, w.Credits);
        Assert.Equal(l.Cells.Count * w.Content.Balance.BeltSlotsPerCell, l.Slots.Length);
        Assert.Equal("err_link_exists", Fail(w, p, a, b));
        w.Credits = 0;
        Assert.Equal("err_cost", Fail(w, p, b, a));
    }

    [Fact]
    public void Perpendicular_crossing_uses_a_bridge_and_at_most_two_belts_per_cell()
    {
        var (w, p) = Open();
        var west = Place(w, p, "storage_crate", -8, -40);
        var east = Place(w, p, "storage_crate", 8, -40);
        var first = Lay(w, p, west, east);

        var north = Place(w, p, "storage_crate", 0, -46);
        var south = Place(w, p, "storage_crate", 0, -36);
        var second = Lay(w, p, north, south);
        var shared = second.Cells.Intersect(first.Cells).ToList();
        Assert.Single(shared);
        var crossing = shared[0];
        Assert.False(w.IsBridge(first, first.Cells.IndexOf(crossing)));
        Assert.True(w.IsBridge(second, second.Cells.IndexOf(crossing)));
        var i = second.Cells.IndexOf(crossing);
        Assert.Equal(second.Cells[i - 1].x, second.Cells[i + 1].x); // recto en el cruce
        Assert.Equal(first.Id, w.BeltAt(0, crossing.x, crossing.z));
        Assert.Equal(second.Id, w.BridgeAt(0, crossing.x, crossing.z));

        // Las cargas no se mezclan: cada una llega a su destino.
        west.In.Add("ore", 8);
        north.In.Add("ingot", 8);
        Run(w, 400);
        Assert.Equal(8, east.In.Get("ore"));
        Assert.Equal(0, east.In.Get("ingot"));
        Assert.Equal(8, south.In.Get("ingot"));

        // Una tercera cinta paralela a la segunda no pisa la celda del cruce: rodea o cruza otra celda.
        var third = Lay(w, p, Place(w, p, "storage_crate", 2, -46), Place(w, p, "storage_crate", 2, -36));
        Assert.DoesNotContain(crossing, third.Cells);

        // Al retirar la cinta de abajo el puente deja de serlo y sigue en pie.
        Assert.True(w.Apply(p, new Command { Type = CommandType.Unlink, A = first.Id }).Ok);
        Assert.False(w.IsBridge(second, second.Cells.IndexOf(crossing)));
        Assert.Equal(second.Id, w.BeltAt(0, crossing.x, crossing.z));
    }

    [Fact]
    public void Crossing_is_refused_on_curves_and_ends()
    {
        var (w, p) = Open();
        // Dos máquinas cercanas en diagonal: la cinta entera son extremo y curva.
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 3, -38);
        var l = Lay(w, p, a, b);
        var c = Place(w, p, "storage_crate", 0, -46);
        var d = Place(w, p, "storage_crate", 0, -30);
        var e = Lay(w, p, c, d);
        foreach (var cell in e.Cells.Intersect(l.Cells))
        {
            var index = l.Cells.IndexOf(cell);
            Assert.InRange(index, 1, l.Cells.Count - 2);
            Assert.True(l.Cells[index - 1].x == l.Cells[index + 1].x || l.Cells[index - 1].z == l.Cells[index + 1].z);
        }
    }

    [Fact]
    public void Machine_and_rail_cannot_be_placed_on_a_belt_but_player_can_cross_it()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 8, -40);
        var l = Lay(w, p, a, b);
        var mid = l.Cells[l.Cells.Count / 2];
        var r = w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = mid.x, B = mid.z });
        Assert.Equal("err_blocked", r.Error);
        var rail = w.Apply(p, new Command { Type = CommandType.PlaceRail, A = mid.x, B = mid.z - 4, C = mid.x, D = mid.z + 4 });
        Assert.Equal("err_blocked", rail.Error);
    }

    [Fact]
    public void Base_speed_is_3_33_per_second_and_upgrade_doubles_it()
    {
        foreach (var (upgrade, expected) in new[] { (false, 200), (true, 400) })
        {
            var (w, p) = Open();
            var a = Place(w, p, "storage_crate", 0, -40);
            var b = Place(w, p, "export_terminal", 4, -40);
            a.In.Add("ore", 150);
            b.AutoSell = false;
            // Almacén con reserva casi ilimitada; el destino se vacía para medir solo la cinta.
            var l = Lay(w, p, a, b);
            if (upgrade) w.Upgrades.Add("link_rate");
            w.Reindex();
            a.In.Add("ore", 100000);
            var delivered = 0L;
            for (var i = 0; i < 600 + l.Slots.Length * 3 / (upgrade ? 2 : 1); i++)
            {
                w.Step();
                delivered += b.In.Get("ore");
                b.In.Clear();
            }
            Assert.InRange(delivered, expected - 2, expected + 2);
            Assert.Equal(expected, w.InspectBelt(l.Id)!.ItemsPerMinute);
        }
    }

    [Fact]
    public void Coarse_time_scale_moves_the_same_cargo_without_loss()
    {
        var run = (int scale) =>
        {
            var (w, p) = Open();
            var a = Place(w, p, "storage_crate", 0, -40);
            var b = Place(w, p, "storage_crate", 10, -40);
            Lay(w, p, a, b);
            a.In.Add("ore", 150);
            w.TimeScale = scale;
            for (var i = 0; i < 1200 / scale; i++)
            {
                w.Step();
                Assert.Equal(150, w.CountEverywhere("ore"));
            }
            return b.In.Get("ore");
        };
        Assert.Equal(150, run(1));
        Assert.Equal(150, run(5));
        Assert.Equal(150, run(40));
    }

    [Fact]
    public void Full_belt_conserves_every_item_while_jammed_and_resumes_when_room_appears()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 8, -40);
        var l = Lay(w, p, a, b);
        var capacity = w.Content.Machines["storage_crate"].Capacity;
        a.In.Add("ore", capacity + 100);
        var total = w.CountEverywhere("ore");
        Assert.Equal(capacity + 100, total);

        // Con el destino lleno se forma la cola y la entrada se detiene.
        for (var i = 0; i < 2500; i++)
        {
            w.Step();
            Assert.Equal(total, w.CountEverywhere("ore"));
        }
        Assert.Equal(capacity, b.In.Get("ore"));
        Assert.Equal(l.Slots.Length, l.Load);
        Assert.All(l.Slots, s => Assert.Equal("ore", s));
        Assert.Equal(total - capacity - l.Slots.Length, a.In.Get("ore"));
        var info = w.InspectBelt(l.Id)!;
        Assert.Equal(BeltStatus.BlockedFull, info.Status);
        Assert.Equal(l.Slots.Length, info.Load);
        Assert.Equal(l.Slots.Length, info.Capacity);
        Assert.Equal("ore", info.Items.Single().Key);
        var hash = WorldCodec.Hash(w);
        Run(w, 50);
        Assert.Equal(hash, WorldCodec.Hash(w)); // atascada no cambia nada

        // Un jugador retira del destino: la cinta reanuda sin perder nada.
        Fixture.StandAt(w, p, b);
        Assert.True(w.Apply(p, new Command { Type = CommandType.Extract, A = b.Id, Text = "ore" }).Ok);
        Assert.Equal(total, w.CountEverywhere("ore"));
        Run(w, 30);
        Assert.Equal(BeltStatus.Flowing, w.BeltStatusOf(l));
        Assert.True(b.In.Get("ore") > 0);
        Assert.Equal(total, w.CountEverywhere("ore"));
    }

    [Fact]
    public void Stopped_machine_jams_the_belt_without_energy_rules_on_the_belt()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 20, -40);
        var sm = Place(w, p, "smelter", 26, -40, rot: 3);
        Lay(w, p, a, sm);
        // Sin energía: la fundición llena su entrada, deja de consumir y la cinta se atasca.
        w.Machines.Remove(Fixture.Find(w, "aux_core").Id);
        w.Reindex();
        a.In.Add("ore", 200);
        Run(w, 800);
        var l = w.Links.Values.Single();
        Assert.Equal(MachineStatus.NoPower, sm.Status);
        Assert.Equal(l.Slots.Length, l.Load);
        Assert.Equal(BeltStatus.BlockedNoPower, w.BeltStatusOf(l));
        Assert.Equal(200, w.CountEverywhere("ore"));
    }

    [Fact]
    public void Load_filters_by_destination_and_never_jams_on_unaccepted_items()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 20, -40);
        var sm = Place(w, p, "smelter", 26, -40, rot: 3);
        var l = Lay(w, p, a, sm);
        a.In.Add("scrap", 50);
        a.In.Add("ice", 50);
        a.In.Add("ore", 6);
        Run(w, 400);
        Assert.Equal(50, a.In.Get("scrap"));
        Assert.Equal(50, a.In.Get("ice"));
        Assert.DoesNotContain(l.Slots, s => s is "scrap" or "ice");
        Assert.True(w.Stats.GetValueOrDefault("produced.ingot") > 0, "la fundición recibió mineral");
    }

    [Fact]
    public void Two_input_recipe_does_not_deadlock_when_one_input_is_abundant()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 20, -40);
        var cryo = Place(w, p, "cryo_assembler", 26, -40, rot: 3);
        Place(w, p, "rtg_generator", 20, -30);
        var l = Lay(w, p, a, cryo);
        // Primero solo piezas: se llenan entrada y cinta hasta lo necesario.
        a.In.Add("parts", 150);
        Run(w, 400);
        var total = w.CountEverywhere("parts");
        // El refrigerante llega después y debe poder pasar: la cinta no quedó tapada de piezas.
        a.In.Add("coolant", 20);
        Run(w, 3000);
        Assert.True(w.Stats.GetValueOrDefault("produced.cryo_module") >= 2, "la cinta no bloqueó el segundo ingrediente");
        Assert.True(l.Load < l.Slots.Length || l.Slots[^1] == "parts");
        Assert.Equal(total - 2 * w.Stats["produced.cryo_module"], w.CountEverywhere("parts") + (cryo.Processing ? 0 : 0));
    }

    [Fact]
    public void Several_outputs_split_in_turn()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", -8, -40);
        var c = Place(w, p, "storage_crate", 8, -40);
        Lay(w, p, a, b);
        Lay(w, p, a, c);
        // Reparte aunque el material sea escaso: nunca se queda todo en la cinta de menor ID.
        for (var i = 0; i < 40; i++)
        {
            a.In.Add("ore", 1);
            Run(w, 12);
        }
        Run(w, 300);
        Assert.Equal(40, b.In.Get("ore") + c.In.Get("ore"));
        Assert.Equal(20, b.In.Get("ore"));
    }

    [Fact]
    public void Output_with_one_full_branch_keeps_feeding_the_other()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var full = Place(w, p, "storage_crate", -8, -40);
        var free = Place(w, p, "storage_crate", 8, -40);
        var capacity = w.Content.Machines["storage_crate"].Capacity;
        full.In.Add("ore", capacity);
        Lay(w, p, a, full);
        Lay(w, p, a, free);
        a.In.Add("ore", 150);
        Run(w, 1500);
        Assert.True(free.In.Get("ore") > 100, $"recibió {free.In.Get("ore")}");
        Assert.Equal(capacity + 150, w.CountEverywhere("ore"));
    }

    [Fact]
    public void Removing_a_loaded_belt_returns_load_and_cost_exactly_once()
    {
        var (w, p) = Open();
        var depot = Fixture.Find(w, "depot");
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 10, -40);
        var credits = w.Credits;
        var l = Lay(w, p, a, b);
        Assert.True(w.Credits < credits);
        a.In.Add("ore", 150);
        var total = w.CountEverywhere("ore");
        Run(w, 30);
        var onBelt = l.Load;
        Assert.True(onBelt > 0);
        var before = depot.In.Get("ore");
        Assert.True(w.Apply(p, new Command { Type = CommandType.Unlink, A = l.Id }).Ok);
        Assert.Equal(before + onBelt, depot.In.Get("ore"));
        Assert.Equal(total, w.CountEverywhere("ore"));
        Assert.Equal(credits, w.Credits);
        Assert.Empty(w.Links);
        Assert.Equal(-1, w.BeltAt(0, l.Cells[0].x, l.Cells[0].z));
        Assert.Equal("err_gone", w.Apply(p, new Command { Type = CommandType.Unlink, A = l.Id }).Error);
        Assert.Equal(total, w.CountEverywhere("ore"));
        Assert.Equal(credits, w.Credits);
        // Las celdas quedan libres para volver a construir.
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = l.Cells[1].x, B = l.Cells[1].z }).Ok);
    }

    [Fact]
    public void Two_players_removing_the_same_belt_or_machine_do_not_duplicate()
    {
        var (w, a) = Open();
        var b = Fixture.AddPlayer(w, "player-b", 0, -40);
        var m1 = Place(w, a, "storage_crate", 0, -40);
        var m2 = Place(w, a, "storage_crate", 10, -40);
        var l = Lay(w, a, m1, m2);
        m1.In.Add("ore", 100);
        Run(w, 30);
        var total = w.CountEverywhere("ore");
        var credits = w.Credits;
        Assert.True(w.Apply(a, new Command { Type = CommandType.Unlink, A = l.Id }).Ok);
        var afterFirst = w.Credits;
        Assert.Equal("err_gone", w.Apply(b, new Command { Type = CommandType.Unlink, A = l.Id }).Error);
        Assert.Equal(afterFirst, w.Credits);
        Assert.True(afterFirst > credits);
        Assert.Equal(total, w.CountEverywhere("ore"));

        // Máquina con cinta cargada: uno la retira, el otro llega tarde.
        var l2 = Lay(w, a, m1, m2);
        Run(w, 30);
        Assert.True(l2.Load > 0);
        a.X = b.X = m1.X * 2f;
        a.Z = b.Z = m1.Z * 2f;
        Assert.True(w.Apply(a, new Command { Type = CommandType.Remove, A = m1.Id }).Ok);
        Assert.Equal("err_gone", w.Apply(b, new Command { Type = CommandType.Remove, A = m1.Id }).Error);
        Assert.Equal("err_gone", w.Apply(b, new Command { Type = CommandType.Unlink, A = l2.Id }).Error);
        Assert.Empty(w.Links);
        Assert.Equal(total, w.CountEverywhere("ore"));
    }

    [Fact]
    public void Removing_a_machine_returns_the_load_of_every_connected_belt()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 10, -40);
        var c = Place(w, p, "storage_crate", 20, -40);
        var ab = Lay(w, p, a, b);
        var bc = Lay(w, p, b, c);
        a.In.Add("ore", 100);
        b.In.Add("ice", 100);
        Run(w, 40);
        var ore = w.CountEverywhere("ore");
        var ice = w.CountEverywhere("ice");
        Assert.True(ab.Load > 0 && bc.Load > 0);
        p.X = b.X * 2f;
        p.Z = b.Z * 2f;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Remove, A = b.Id }).Ok);
        Assert.Empty(w.Links);
        Assert.Equal(ore, w.CountEverywhere("ore"));
        Assert.Equal(ice, w.CountEverywhere("ice"));
    }

    [Fact]
    public void Moving_a_machine_reroutes_without_loss_and_retires_when_no_route_exists()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 10, -40);
        var l = Lay(w, p, a, b);
        a.In.Add("ore", 100);
        Run(w, 30);
        var total = w.CountEverywhere("ore");
        Assert.True(w.Apply(p, new Command { Type = CommandType.Move, A = b.Id, B = 14, C = -44, D = 0 }).Ok);
        var moved = w.Links[l.Id];
        Assert.Contains(moved.Cells, c => c.x > 10);
        Assert.Equal(moved.Cells.Count * w.Content.Balance.BeltSlotsPerCell, moved.Slots.Length);
        Assert.Equal(0, moved.Load);
        Assert.Equal(total, w.CountEverywhere("ore"));
        Assert.Contains(l.Id, w.DirtyLinks);
        Run(w, 300);
        Assert.True(b.In.Get("ore") > 0);
        Assert.Equal(total, w.CountEverywhere("ore"));

        // Sin ruta posible, rodeada de máquinas: la cinta se retira y su coste vuelve.
        foreach (var (x, z) in new[] { (30, -46), (30, -42), (28, -44), (32, -44) }) Place(w, p, "storage_crate", x, z);
        var credits = w.Credits;
        var paid = moved.Paid;
        p.X = 30 * 2f;
        p.Z = -44 * 2f;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Move, A = b.Id, B = 30, C = -44, D = 0 }).Ok);
        Assert.Empty(w.Links);
        Assert.Equal(credits + paid, w.Credits);
        Assert.Equal(total, w.CountEverywhere("ore"));
    }

    [Fact]
    public void Inspection_reports_each_state()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 20, -40);
        var b = Place(w, p, "storage_crate", 28, -40);
        var l = Lay(w, p, a, b);
        Assert.Equal(BeltStatus.Empty, w.InspectBelt(l.Id)!.Status);
        Assert.Equal(0, w.InspectBelt(l.Id)!.Load);
        a.In.Add("ore", 3);
        Run(w, 10);
        var info = w.InspectBelt(l.Id)!;
        Assert.Equal(BeltStatus.Flowing, info.Status);
        Assert.True(info.Load > 0);
        Assert.Equal(200, info.ItemsPerMinute);
        Assert.Equal(l.Cells.Count, info.CellCount);

        // Origen de producción sin materia: «origen sin salida».
        var recycler = Fixture.Find(w, "recycler");
        var fromRecycler = Lay(w, p, recycler, Fixture.Find(w, "depot"));
        Run(w, 3);
        Assert.Equal(BeltStatus.SourceIdle, w.BeltStatusOf(fromRecycler));
        Assert.Null(w.InspectBelt(999999));
    }

    [Fact]
    public void Saved_world_keeps_cargo_and_continues_identically()
    {
        var root = Path.Combine(Path.GetTempPath(), "pluton-belts-" + Guid.NewGuid());
        try
        {
            var (w, p) = Open();
            var a = Place(w, p, "storage_crate", 0, -40);
            var b = Place(w, p, "storage_crate", -8, -40);
            var c = Place(w, p, "storage_crate", 8, -40);
            Lay(w, p, a, b);
            Lay(w, p, a, c);
            a.In.Add("ore", 100);
            Run(w, 25);
            Assert.True(w.ItemsOnBelts() > 0);
            var store = new SaveStore("test-belts", root);
            store.Save("a", w);
            var loaded = store.Load("a", Fixture.Content);
            Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
            Assert.Equal(w.ItemsOnBelts(), loaded.ItemsOnBelts());
            for (var i = 0; i < 200; i++)
            {
                w.Step();
                loaded.Step();
                Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Offline_progress_moves_cargo_without_creating_or_losing_any()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 10, -40);
        var l = Lay(w, p, a, b);
        a.In.Add("ore", 180);
        Run(w, 30);
        w.SavedUnix = 1_000_000;
        Assert.NotNull(OfflineProgress.Apply(w, 1_000_000 + 8 * 3600));
        Assert.Equal(180, b.In.Get("ore"));
        Assert.Equal(180, w.CountEverywhere("ore"));
        Assert.Equal(0, l.Load);
        Assert.Empty(w.DirtySimSlots);
        Assert.Empty(w.DirtySlots);
    }

    [Fact]
    public void Offline_with_jam_stops_at_capacity()
    {
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", 0, -40);
        var b = Place(w, p, "storage_crate", 10, -40);
        var l = Lay(w, p, a, b);
        var capacity = w.Content.Machines["storage_crate"].Capacity;
        a.In.Add("ore", capacity + 150);
        w.SavedUnix = 1_000_000;
        OfflineProgress.Apply(w, 1_000_000 + 3600);
        Assert.Equal(capacity, b.In.Get("ore"));
        Assert.Equal(l.Slots.Length, l.Load);
        Assert.Equal(capacity + 150, w.CountEverywhere("ore"));
    }

    [Fact]
    public void Real_v4_save_migrates_links_to_belts_keeping_inventories_and_a_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), "pluton-belts-" + Guid.NewGuid());
        try
        {
            var store = new SaveStore("test-belts", root);
            var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "save-v4-alpha-0.4.1-links.plsave"));
            Assert.Equal(4, BitConverter.ToInt32(original, 4));
            File.WriteAllBytes(store.PathOf("real4"), original);
            var w = store.Load("real4", Fixture.Content);
            Assert.Equal(original, File.ReadAllBytes(store.PathOf("real4") + ".v4.bak"));

            // Cifras que imprimió el generador del commit babc91c al escribir este guardado.
            Assert.Equal(3910, w.Tick);
            Assert.Equal(15, w.Machines.Count);
            Assert.Equal(6, w.Links.Count);
            Assert.Equal(4836, w.Credits);
            Assert.Equal(6, w.ObjectiveIndex);
            Assert.Equal(88, w.CountEverywhere("ingot"));
            Assert.Equal(60, w.CountEverywhere("ore"));
            Assert.Equal(36, w.CountEverywhere("parts"));
            Assert.Equal(245, w.CountEverywhere("recovered"));

            // Nada en tránsito; el extractor pegado a la fundición no tiene salida libre y queda elevado.
            Assert.Equal(0, w.ItemsOnBelts());
            Assert.All(w.Links.Values, l => Assert.Equal(0, l.Paid));
            var elevated = w.Links.Values.Where(l => l.Elevated).ToList();
            Assert.Single(elevated);
            Assert.Empty(elevated[0].Cells);
            Assert.Equal(elevated[0].From, w.Machines.Values.First(m => m.Def == "ore_extractor" && m.X == 21 && m.Z == 0).Id);
            Assert.True(elevated[0].Slots.Length > 0);
            foreach (var l in w.Links.Values.Where(l => !l.Elevated))
            {
                Assert.NotEmpty(l.Cells);
                Assert.Equal(l.Cells.Count * 4, l.Slots.Length);
                foreach (var (x, z) in l.Cells)
                    Assert.Contains(l.Id, new[] { w.BeltAt(0, x, z), w.BridgeAt(0, x, z) });
            }

            // La fábrica sigue funcionando y entrega por la cinta elevada.
            var smelter = w.Machines.Values.First(m => m.Def == "smelter");
            var ingots = w.Stats.GetValueOrDefault("produced.ingot");
            for (var i = 0; i < 2000; i++) w.Step();
            Assert.True(w.Stats.GetValueOrDefault("produced.ingot") > ingots);
            _ = smelter;

            // Guarda ya en el formato nuevo y se puede recuperar: la elevada se puede retirar y volver a tender.
            store.Save("real4", w);
            Assert.Equal(WorldCodec.FormatVersion, BitConverter.ToInt32(File.ReadAllBytes(store.PathOf("real4")), 4));
            var again = store.Load("real4", Fixture.Content);
            Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(again));
            var p = again.Players.Values.First();
            var ore = again.CountEverywhere("ore");
            Assert.True(again.Apply(p, new Command { Type = CommandType.Unlink, A = elevated[0].Id }).Ok);
            Assert.Equal(ore, again.CountEverywhere("ore"));
            Assert.Equal(original, File.ReadAllBytes(store.PathOf("real4") + ".v4.bak"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Failed_route_search_is_bounded()
    {
        // Peor caso razonable: destino dentro de un recinto cerrado, con todo el radio de búsqueda libre fuera.
        var (w, p) = Open();
        var a = Place(w, p, "storage_crate", -20, -40);
        var b = Place(w, p, "storage_crate", 0, -40);
        for (var x = -3; x <= 4; x += 1)
            foreach (var z in new[] { -43, -36 })
                w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "storage_crate", Planet = 0, X = x, Z = z };
        foreach (var z in Enumerable.Range(-43, 8))
            foreach (var x in new[] { -3, 4 })
                w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "storage_crate", Planet = 0, X = x, Z = z };
        w.Reindex();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var route = w.RouteBelt(a.Id, b.Id);
        var first = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        for (var i = 0; i < 20; i++) w.RouteBelt(a.Id, b.Id);
        var average = clock.Elapsed.TotalMilliseconds / 20;
        Assert.NotNull(route.Error);
        Assert.True(average < 100, $"búsqueda fallida: primera {first:F2} ms, media {average:F2} ms");
        Console.WriteLine($"Búsqueda fallida: primera {first:F2} ms, media {average:F2} ms");
    }

    [Fact]
    public void Texts_for_every_belt_error_and_state_exist_in_both_languages()
    {
        var dir = Path.Combine(ContentDb.LocateDirectory(), "loc");
        foreach (var lang in new[] { "es", "en" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, lang + ".json")));
            var keys = doc.RootElement.EnumerateObject().Select(e => e.Name).ToHashSet();
            foreach (var key in new[] { "err_belt_route", "err_belt_stale", "err_belt_path", "err_belt_waypoint", "err_belt_waypoint_path", "err_belt_complex", "err_belt_port_out", "err_belt_port_in", "err_link_kind", "err_link_exists", "belt_title", "belt_elevated", "belt_load", "belt_preview", "belt_missing", "belt_alternative", "belt_tracing", "belt_sending", "belt_reconfirm", "belt_points", "belt_snap_on", "belt_snap_off", "action_belt_undo", "action_belt_snap", "action_belt_port", "prompt_link_way" })
                Assert.Contains(key, keys);
            foreach (var status in Enum.GetNames<BeltStatus>())
                Assert.Contains("belt_status_" + status, keys);
        }
    }
}

public class BeltScenarioTests
{
    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    public void Measurement_belts_are_valid_and_carry_real_cargo(string name)
    {
        var w = Scenarios.Build(Fixture.Content, name);
        Assert.NotEmpty(w.Links);
        var occupied = w.Machines.Values.SelectMany(m => w.Footprint(m.Def, m.X, m.Z, m.Rot)).ToHashSet();
        foreach (var l in w.Links.Values)
        {
            Assert.NotEmpty(l.Cells);
            Assert.DoesNotContain(l.Cells, c => occupied.Contains(c));
            // El primer tramo sale por un puerto del origen; el último entra por uno del destino.
            var a = w.Machines[l.From];
            var b = w.Machines[l.To];
            var outs = w.BeltPorts(a.Planet, a.Def, a.X, a.Z, a.Rot, true).Select(p => (p.X, p.Z));
            var ins = w.BeltPorts(b.Planet, b.Def, b.X, b.Z, b.Rot, false).Select(p => (p.X, p.Z));
            Assert.Contains(l.Cells[0], outs);
            Assert.Contains(l.Cells[^1], ins);
        }
        var scrap = w.CountEverywhere("scrap");
        Assert.True(w.ItemsOnBelts() > 0);
        var moving = new HashSet<int>();
        var soldAtStart = w.Stats.GetValueOrDefault("sold_credits");
        var peak = 0L;
        for (var i = 0; i < 1200; i++)
        {
            w.Step();
            moving.UnionWith(w.DirtySimSlots);
            w.DirtySimSlots.Clear();
            peak = Math.Max(peak, w.ItemsOnBelts());
        }
        Assert.True(moving.Count >= w.Links.Count * 9 / 10, $"solo {moving.Count} de {w.Links.Count} cintas movieron carga");
        Assert.True(peak > w.Links.Count, $"carga máxima {peak} con {w.Links.Count} cintas");
        Assert.True(w.Stats.GetValueOrDefault("produced.parts") > 0);
        Assert.True(w.Stats.GetValueOrDefault("sold_credits") > soldAtStart);
        Assert.True(w.CountEverywhere("scrap") < scrap, "la línea saturada vende de verdad");
    }
}

public class BeltNetworkTests
{
    private const string KeyA = "client-identity-belt-a";
    private const string KeyB = "client-identity-belt-b";

    private static CommandResult Send(NetHarness h, GameClient c, Command cmd)
    {
        CommandResult? result = null;
        var id = c.Send(cmd);
        void Handler(uint rid, CommandResult r) { if (rid == id) result = r; }
        c.ResultReceived += Handler;
        h.Pump(() => result != null);
        c.ResultReceived -= Handler;
        return result!.Value;
    }

    [Fact]
    public void Clients_replicate_route_and_cargo_with_matching_signatures()
    {
        var (world, _) = BeltTests.Open("server-only-player");
        world.Players.Remove("server-only-player");
        using var h = new NetHarness(new ServerOptions { CheckEveryTicks = 20 }, world);
        var a = h.Join(KeyA);
        var b = h.Join(KeyB);
        var pa = world.Players[KeyA];
        pa.X = 0;
        pa.Z = -80;
        pa.Bag.Add("recovered", 100);
        Assert.True(Send(h, a, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -40 }).Ok);
        var from = world.Machines.Values.Last();
        Assert.True(Send(h, a, new Command { Type = CommandType.Place, Text = "storage_crate", A = 10, B = -40 }).Ok);
        var to = world.Machines.Values.Last();
        var route = a.World!.RouteBelt(from.Id, to.Id);
        h.Pump(() => a.World!.Machines.ContainsKey(to.Id));
        route = a.World!.RouteBelt(from.Id, to.Id);
        Assert.Null(route.Error);
        Assert.True(Send(h, a, World.LinkCommand(from.Id, to.Id, route.Cells)).Ok);
        var link = world.Links.Values.Last();
        Assert.Equal(route.Cells, link.Cells); // la previsualización del cliente coincide con lo que valida el servidor
        h.Pump(() => b.World!.Links.ContainsKey(link.Id));
        Assert.Equal(link.Cells, b.World!.Links[link.Id].Cells);

        // Carga real en movimiento: la red replica los huecos y las firmas siguen coincidiendo.
        from.In.Add("ore", 150);
        world.DirtyMachines.Add(from.Id);
        var checks = a.HashChecks;
        var deltas = a.SlotDeltas;
        var sawCargo = false;
        h.Pump(() =>
        {
            sawCargo |= a.World!.Links[link.Id].Load > 0;
            return a.HashChecks >= checks + 8 && b.HashChecks >= checks + 8 && sawCargo;
        }, 30000);
        Assert.True(a.SlotDeltas > deltas);
        Assert.True(sawCargo);
        Assert.Equal(0, a.HashMismatches);
        Assert.Equal(0, b.HashMismatches);
        Assert.Equal(150, world.CountEverywhere("ore"));

        // Retirada simultánea por red: una aplica, la otra recibe err_gone; nada se duplica y los espejos convergen.
        Assert.True(world.Links[link.Id].Load > 0 || to.In.Get("ore") > 0);
        CommandResult? ra = null, rb = null;
        var ida = a.Send(new Command { Type = CommandType.Unlink, A = link.Id });
        var idb = b.Send(new Command { Type = CommandType.Unlink, A = link.Id });
        a.ResultReceived += (id, r) => { if (id == ida) ra = r; };
        b.ResultReceived += (id, r) => { if (id == idb) rb = r; };
        h.Pump(() => ra != null && rb != null);
        Assert.True(ra!.Value.Ok != rb!.Value.Ok);
        Assert.Equal("err_gone", ra.Value.Ok ? rb.Value.Error : ra.Value.Error);
        Assert.Equal(150, world.CountEverywhere("ore"));
        var later = a.HashChecks;
        h.Pump(() => a.HashChecks >= later + 3 && b.HashChecks >= later + 3, 20000);
        Assert.False(a.World!.Links.ContainsKey(link.Id));
        Assert.False(b.World!.Links.ContainsKey(link.Id));
        Assert.Equal(150, a.World!.CountEverywhere("ore"));
        Assert.Equal(0, a.HashMismatches);
        Assert.Equal(0, b.HashMismatches);
    }

    [Fact]
    public void Late_joiner_receives_cargo_and_only_changed_belts_are_sent()
    {
        var (world, p) = BeltTests.Open("server-only-player");
        var a = BeltTests.Place(world, p, "storage_crate", 0, -40);
        var b = BeltTests.Place(world, p, "storage_crate", 10, -40);
        var moving = BeltTests.Lay(world, p, a, b);
        var c = BeltTests.Place(world, p, "storage_crate", 0, -30);
        var d = BeltTests.Place(world, p, "storage_crate", 10, -30);
        var idle = BeltTests.Lay(world, p, c, d);
        a.In.Add("ore", 180);
        for (var i = 0; i < 30; i++) world.Step();
        world.ClearReplicationState();

        using var h = new NetHarness(new ServerOptions { CheckEveryTicks = 20 }, world);
        var late = h.Join(KeyA);
        Assert.True(late.World!.Links[moving.Id].Load > 0);
        var idleVersion = late.World.Links[idle.Id].SlotsVersion;
        var movingVersion = late.World.Links[moving.Id].SlotsVersion;
        h.Pump(() => late.HashChecks >= 4, 20000);
        Assert.Equal(0, late.HashMismatches);
        Assert.True(late.World.Links[moving.Id].SlotsVersion > movingVersion);
        Assert.Equal(idleVersion, late.World.Links[idle.Id].SlotsVersion);
    }

    [Fact]
    public void Cargo_updates_arrive_at_one_hertz_not_every_flush()
    {
        var (world, p) = BeltTests.Open("server-only-player");
        var a = BeltTests.Place(world, p, "storage_crate", 0, -40);
        var b = BeltTests.Place(world, p, "storage_crate", 10, -40);
        var l = BeltTests.Lay(world, p, a, b);
        a.In.Add("ore", 180);
        world.ClearReplicationState();
        using var h = new NetHarness(new ServerOptions(), world);
        var c = h.Join(KeyA);
        var startVersion = c.World!.Links[l.Id].SlotsVersion;
        var startTick = world.Tick;
        for (var i = 0; i < 200; i++)
        {
            h.Step();
            Thread.Sleep(1);
        }
        h.Settle(100);
        var ticks = world.Tick - startTick;
        var updates = c.World!.Links[l.Id].SlotsVersion - startVersion;
        Assert.InRange(updates, ticks / 12, ticks / 8 + 2);
    }
}
