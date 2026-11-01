using Pluton.Networking;
using Pluton.Simulation;
using Xunit;
using Xunit.Abstractions;

namespace Pluton.Tests;

/// <summary>Trazado largo: lo que el cliente calcula y envía es exactamente lo que se construye, o se rechaza sin coste.</summary>
public class BeltRouteTests
{
    private readonly ITestOutputHelper _out;
    public BeltRouteTests(ITestOutputHelper output) { _out = output; }

    private static World.BeltSearch Trace(World w, BeltRequest request, int budget = 2_000_000)
    {
        var search = new World.BeltSearch(w, request);
        search.Step(budget);
        Assert.True(search.Done);
        return search;
    }

    private static int Turns(IReadOnlyList<(int x, int z)> c)
    {
        var turns = 0;
        for (var i = 2; i < c.Count; i++)
            if ((c[i].x - c[i - 1].x, c[i].z - c[i - 1].z) != (c[i - 1].x - c[i - 2].x, c[i - 1].z - c[i - 2].z)) turns++;
        return turns;
    }

    private static List<(int x, int z)> PlanToEnd(BeltPlanner plan, int stamp = 0)
    {
        for (var i = 0; i < 20000 && plan.State is BeltPlanState.Idle or BeltPlanState.Searching; i++)
        {
            plan.Tick(stamp, 500);
            if (plan.State == BeltPlanState.Idle) break;
        }
        Assert.Equal(BeltPlanState.Ready, plan.State);
        return plan.Route!.ToList();
    }

    [Fact]
    public void Long_belt_across_the_planet_is_built_as_one_connection_and_items_cross_its_whole_length()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -85, -80);
        var b = BeltTests.Place(w, p, "storage_crate", 85, -80);
        var route = w.RouteBelt(a.Id, b.Id, 2_000_000);
        Assert.Null(route.Error);
        Assert.True(route.Cells.Count >= 165, $"{route.Cells.Count} celdas");
        var credits = w.Credits;
        Assert.True(w.Apply(p, World.LinkCommand(a.Id, b.Id, route.Cells)).Ok);
        var link = w.Links.Values.Single();
        Assert.Equal(route.Cells, link.Cells);
        Assert.Equal(credits - route.Credits, w.Credits);
        Assert.Equal(route.Cells.Count * 4, link.Slots.Length);

        a.In.Add("ore", 5);
        var transit = link.Slots.Length * 3;
        for (var i = 0; i < transit - 30; i++) w.Step();
        Assert.Equal(0, b.In.Get("ore")); // aún no ha podido recorrerla: la longitud es real
        Assert.Equal(5, w.CountEverywhere("ore"));
        for (var i = 0; i < 80; i++) w.Step();
        Assert.Equal(5, b.In.Get("ore"));
        Assert.Equal(0, link.Load);
    }

    [Fact]
    public void Route_avoids_a_long_wall_and_prefers_few_turns()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -40, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 40, -60);
        // Muro con un solo paso, lejos de la línea recta.
        for (var z = -85; z <= -30; z += 2)
        {
            if (z == -35) continue;
            w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "storage_crate", Planet = 0, X = 0, Z = z };
        }
        w.Reindex();
        var cells = Trace(w, new BeltRequest(a.Id, b.Id, Array.Empty<(int, int)>())).Cells;
        Assert.Contains(cells, c => c.x == 0 && c.z is -35 or -34);
        Assert.True(Turns(cells) <= 6, $"{Turns(cells)} curvas");
        Assert.Null(w.CheckBeltRoute(a.Id, b.Id, cells));
    }

    [Fact]
    public void Route_prefers_going_around_the_front_of_other_machines()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", 0, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 20, -60);
        // Fundición cuya salida queda justo en la línea recta entre ambos.
        BeltTests.Place(w, p, "smelter", 10, -58);
        var cells = Trace(w, new BeltRequest(a.Id, b.Id, Array.Empty<(int, int)>())).Cells;
        Assert.DoesNotContain(cells, c => c.z == -59 && c.x is 10 or 11);
    }

    [Fact]
    public void Connector_output_keeps_a_short_straight_stub_before_turning()
    {
        var (w, p) = BeltTests.Open();
        var sm = BeltTests.Place(w, p, "smelter", 0, -50);
        var target = BeltTests.Place(w, p, "storage_crate", 30, -70);
        var cells = Trace(w, new BeltRequest(sm.Id, target.Id, Array.Empty<(int, int)>())).Cells;
        // La salida mira al norte: las dos primeras celdas siguen esa dirección.
        Assert.Equal(cells[0].x, cells[1].x);
        Assert.True(cells[1].z < cells[0].z);
    }

    [Fact]
    public void Waypoints_are_respected_in_order_and_never_revisit_a_cell()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -30, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 30, -60);
        var points = new[] { (-10, -40), (15, -80) };
        var cells = Trace(w, new BeltRequest(a.Id, b.Id, points)).Cells;
        var first = cells.ToList().IndexOf(points[0]);
        var second = cells.ToList().IndexOf(points[1]);
        Assert.True(first >= 0 && second > first);
        Assert.Equal(cells.Count, cells.Distinct().Count());
        Assert.Null(w.CheckBeltRoute(a.Id, b.Id, cells));
    }

    [Fact]
    public void Planner_adds_and_removes_points_offers_distinct_alternatives_and_cancels_without_cost()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -20, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 20, -40);
        var plan = new BeltPlanner(w);
        plan.SetOrigin(a.Id, null);
        plan.SetDestination(b.Id, null);
        var direct = PlanToEnd(plan);

        // Alternativas: todas válidas y distintas; al agotarlas vuelve a la primera.
        var seen = new List<List<(int x, int z)>> { direct };
        for (var i = 0; i < 6; i++)
        {
            plan.NextAlternative();
            var r = PlanToEnd(plan);
            for (var k = 0; k < 40 && !plan.AlternativesComplete && r.SequenceEqual(seen[^1]); k++)
            {
                plan.Tick(0, 500);
                r = plan.Route!.ToList();
            }
            Assert.Null(w.CheckBeltRoute(a.Id, b.Id, r));
            if (r.SequenceEqual(direct)) break;
            Assert.DoesNotContain(seen, s => s.SequenceEqual(r));
            seen.Add(r);
        }
        Assert.True(seen.Count >= 2, "debe haber al menos una ruta distinta");
        Assert.Equal(seen.Count, plan.AlternativeCount);
        Assert.Equal(0, plan.AlternativeIndex);

        // Punto fijado: se respeta al recalcular y se puede retirar.
        Assert.Null(plan.AddWaypoint(0, -80));
        var via = PlanToEnd(plan);
        Assert.Contains((0, -80), via);
        Assert.True(plan.RemoveLastWaypoint());
        Assert.False(plan.RemoveLastWaypoint());
        Assert.Equal(direct, PlanToEnd(plan));

        // Punto sobre una máquina: rechazado sin cambiar nada.
        Assert.Equal("err_belt_waypoint", plan.AddWaypoint(b.X, b.Z));
        Assert.Empty(plan.Waypoints);

        // Cancelar: sin coste ni cinta.
        var credits = w.Credits;
        plan.Reset();
        Assert.False(plan.HasOrigin);
        Assert.Null(plan.Confirm());
        Assert.Equal(credits, w.Credits);
        Assert.Empty(w.Links);
    }

    [Fact]
    public void Planner_only_confirms_the_shown_route_and_recalculates_when_the_world_changes()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -10, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 10, -60);
        var plan = new BeltPlanner(w);
        plan.SetOrigin(a.Id, null);
        plan.SetDestination(b.Id, null);
        Assert.Null(plan.Confirm()); // calculando: el clic no construye
        plan.Tick(0, 1);
        Assert.Equal(BeltPlanState.Searching, plan.State);
        Assert.Null(plan.Confirm());
        var shown = PlanToEnd(plan);
        var command = plan.Confirm();
        Assert.Equal(shown, command!.Cells);
        Assert.Equal(a.Id, command.A);
        Assert.Equal(b.Id, command.B);

        // Otro jugador construye encima: la orden antigua se rechaza sin coste y la vista recalcula otra ruta.
        var mid = shown[shown.Count / 2];
        BeltTests.Place(w, p, "storage_crate", mid.x, mid.z);
        var credits = w.Credits;
        p.X = 0;
        Assert.Equal("err_belt_stale", w.Apply(p, command).Error);
        Assert.Equal(credits, w.Credits);
        plan.Tick(1, 1);
        Assert.Null(plan.Route == null || plan.State == BeltPlanState.Searching ? null : plan.Confirm());
        var again = PlanToEnd(plan, 1);
        Assert.NotEqual(shown, again);
        Assert.DoesNotContain(mid, again);
    }

    [Fact]
    public void Destination_and_connector_do_not_change_when_aim_moves_slightly()
    {
        var (w, p) = BeltTests.Open();
        var m = BeltTests.Place(w, p, "assembler", 0, -50);
        var plan = new BeltPlanner(w);
        var cell = 2f;
        // Conectores de entrada contiguos en la cara trasera (sur), separados 2 m.
        (int x, int z) c0 = (0, -47);
        var first = plan.PickPort(m, false, (c0.x + 0.5f) * cell, (c0.z + 0.5f) * cell, null);
        Assert.Equal(c0, first);
        // Se desplaza hasta casi la mitad entre dos conectores y un poco más: no salta.
        var stay = plan.PickPort(m, false, (c0.x + 1.1f) * cell, (c0.z + 0.5f) * cell, first);
        Assert.Equal(c0, stay);
        // Claramente sobre el siguiente: sí cambia.
        var moved = plan.PickPort(m, false, (c0.x + 1.6f) * cell, (c0.z + 0.5f) * cell, first);
        Assert.Equal((1, -47), moved);
        // Elección manual: recorre sin repetir hasta dar la vuelta.
        var ports = new HashSet<(int, int)>();
        var current = plan.NextPort(m, false, null);
        for (var i = 0; i < 20 && current != null && ports.Add(current.Value); i++) current = plan.NextPort(m, false, current);
        Assert.Equal(w.BeltPorts(m, false).Count(x => x.Free), ports.Count);
    }

    [Fact]
    public void Chosen_close_connectors_are_used_exactly()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "assembler", 0, -50);
        var b = BeltTests.Place(w, p, "assembler", 0, -60);
        // Salidas contiguas: cada elección da su propia cinta.
        var left = Trace(w, new BeltRequest(a.Id, b.Id, Array.Empty<(int, int)>(), FromPort: (0, -51), ToPort: (0, -57))).Cells;
        Assert.Equal((0, -51), left[0]);
        Assert.Equal((0, -57), left[^1]);
        Assert.True(w.Apply(p, World.LinkCommand(a.Id, b.Id, left)).Ok);
        var c = BeltTests.Place(w, p, "storage_crate", 10, -56);
        var next = Trace(w, new BeltRequest(a.Id, c.Id, Array.Empty<(int, int)>(), FromPort: (1, -51))).Cells;
        Assert.Equal((1, -51), next[0]);
        Assert.True(w.Apply(p, World.LinkCommand(a.Id, c.Id, next)).Ok);
        // El conector ocupado ya no se ofrece ni se puede forzar.
        Assert.Equal("err_belt_port_out", Trace(w, new BeltRequest(a.Id, c.Id, Array.Empty<(int, int)>(), FromPort: (0, -51))).Error);
    }

    [Fact]
    public void Tampered_routes_are_rejected_without_consuming_anything()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -10, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 10, -60);
        var wall = BeltTests.Place(w, p, "storage_crate", 0, -70);
        var good = w.RouteBelt(a.Id, b.Id).Cells.ToList();
        var credits = w.Credits;
        var hash = WorldCodec.Hash(w);

        string Try(List<(int x, int z)>? cells) => w.Apply(p, new Command { Type = CommandType.Link, A = a.Id, B = b.Id, Cells = cells }).Error;
        Assert.Equal("err_belt_path", Try(null)); // ya no hay búsqueda en el servidor
        Assert.Equal("err_belt_path", Try(new List<(int, int)>()));
        // Salto: celdas no contiguas.
        Assert.Equal("err_belt_path", Try(good.Where((_, i) => i != good.Count / 2).ToList()));
        // No empieza en un conector del origen, o no acaba en uno del destino.
        Assert.Equal("err_belt_path", Try(good.Skip(1).ToList()));
        Assert.Equal("err_belt_path", Try(good.SkipLast(1).ToList()));
        // Vuelve sobre sus pasos.
        var back = good.Take(3).Concat(new[] { good[1], good[2] }).Concat(good.Skip(3)).ToList();
        Assert.Equal("err_belt_path", Try(back));
        // Atraviesa una máquina.
        var through = new List<(int, int)>();
        for (var x = -8; x <= 9; x++) through.Add((x, -60));
        w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "storage_crate", Planet = 0, X = 0, Z = -60 };
        w.Reindex();
        Assert.Equal("err_belt_stale", Try(through));
        // Sobre un escombro.
        w.Machines.Remove(w.NextId - 1);
        w.DebrisById[w.NextId] = new Debris { Id = w.NextId++, Def = "scrap_pile", Planet = 0, X = 0, Z = -60, Hp = 3 };
        w.Reindex();
        Assert.Equal("err_belt_stale", Try(through));
        w.DebrisById.Remove(w.NextId - 1);
        w.Reindex();
        _ = wall;

        Assert.Equal(credits, w.Credits);
        Assert.Empty(w.Links);
        Assert.Equal(hash, WorldCodec.Hash(w));
    }

    [Fact]
    public void Crossing_rules_are_validated_on_the_received_route()
    {
        var (w, p) = BeltTests.Open();
        var west = BeltTests.Place(w, p, "storage_crate", -8, -60);
        var east = BeltTests.Place(w, p, "storage_crate", 8, -60);
        var first = BeltTests.Lay(w, p, west, east);
        var north = BeltTests.Place(w, p, "storage_crate", 0, -70);
        var south = BeltTests.Place(w, p, "storage_crate", 0, -50);
        var good = w.RouteBelt(north.Id, south.Id).Cells.ToList();
        var crossing = good.Intersect(first.Cells).Single();
        var i = good.IndexOf(crossing);

        // Una ruta que gira dentro del cruce es inválida.
        var bent = good.Take(i + 1).ToList();
        bent.Add((crossing.x + 1, crossing.z));
        bent.Add((crossing.x + 1, crossing.z + 1));
        for (var z = crossing.z + 1; z <= good[^1].z; z++) bent.Add((crossing.x, z));
        Assert.NotEqual("", w.Apply(p, World_Link(north, south, bent)).Error);
        Assert.Single(w.Links);

        Assert.True(w.Apply(p, World.LinkCommand(north.Id, south.Id, good)).Ok);
        Assert.True(w.IsBridge(w.Links.Values.Last(), i));
    }

    private static Command World_Link(Machine a, Machine b, List<(int x, int z)> cells) =>
        new() { Type = CommandType.Link, A = a.Id, B = b.Id, Cells = cells };

    [Fact]
    public void Missing_credits_refuse_the_exact_route_and_consume_nothing()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -40, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 40, -60);
        var route = w.RouteBelt(a.Id, b.Id, 2_000_000);
        w.Credits = route.Credits - 1;
        Assert.Equal("err_cost", w.Apply(p, World.LinkCommand(a.Id, b.Id, route.Cells)).Error);
        Assert.Equal(route.Credits - 1, w.Credits);
        Assert.Empty(w.Links);
        Assert.False(w.CanAfford(p, route.Credits, new Dictionary<string, long>()));
        w.Credits = route.Credits;
        Assert.True(w.Apply(p, World.LinkCommand(a.Id, b.Id, route.Cells)).Ok);
        Assert.Equal(0, w.Credits);
    }

    [Fact]
    public void Route_encoding_is_compact_and_round_trips_a_winding_path()
    {
        var cells = new List<(int x, int z)> { (-80, -80) };
        for (var i = 0; i < 300; i++) cells.Add((cells[^1].x + 1, cells[^1].z));
        var size = CommandCodec.EncodedRouteSize(cells);
        Assert.InRange(size, 1, 16);
        for (var i = 0; i < 600; i++)
            cells.Add(i % 2 == 0 ? (cells[^1].x, cells[^1].z + 1) : (cells[^1].x + 1, cells[^1].z));
        Assert.True(CommandCodec.EncodedRouteSize(cells) < Protocol.MaxClientPacket);
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            CommandCodec.Write(w, new Command { Type = CommandType.Link, A = 1, B = 2, Cells = cells });
        ms.Position = 0;
        Assert.Equal(cells, CommandCodec.Read(new BinaryReader(ms)).Cells);
    }

    [Fact]
    public void Search_cost_on_long_routes_fits_small_frame_slices()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -85, -85);
        var b = BeltTests.Place(w, p, "storage_crate", 85, 85);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var search = new World.BeltSearch(w, new BeltRequest(a.Id, b.Id, Array.Empty<(int, int)>()));
        var slowest = 0.0;
        var slices = 0;
        while (!search.Done)
        {
            var t0 = clock.Elapsed.TotalMilliseconds;
            search.Step(300);
            slowest = Math.Max(slowest, clock.Elapsed.TotalMilliseconds - t0);
            slices++;
        }
        _out.WriteLine($"Ruta de {search.Cells.Count} celdas: {search.Expansions} expansiones, {clock.Elapsed.TotalMilliseconds:F1} ms, {slices} porciones, la más lenta {slowest:F2} ms");
        Assert.Null(search.Error);
        Assert.True(search.Cells.Count > 300);
        Assert.True(slowest < 40, $"porción de {slowest:F1} ms");
    }

    [Fact]
    public void Unreachable_destination_reports_a_clear_error_after_bounded_slices()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", -60, -60);
        var b = BeltTests.Place(w, p, "storage_crate", 60, 60);
        // Recinto cerrado alrededor del destino, con sus conectores libres dentro.
        for (var i = -4; i <= 4; i += 2)
            foreach (var (x, z) in new[] { (60 + i, 56), (60 + i, 64), (56, 60 + i), (64, 60 + i) })
                if (!w.Machines.Values.Any(m => m.X == x && m.Z == z))
                    w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "storage_crate", Planet = 0, X = x, Z = z };
        w.Reindex();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var search = new World.BeltSearch(w, new BeltRequest(a.Id, b.Id, Array.Empty<(int, int)>()));
        var slices = 0;
        while (!search.Done) { search.Step(300); slices++; }
        _out.WriteLine($"Sin ruta: {search.Expansions} expansiones, {clock.Elapsed.TotalMilliseconds:F1} ms, {slices} porciones");
        Assert.Equal("err_belt_route", search.Error);
        // El presupuesto del servidor para Move acota lo mismo.
        var bounded = w.RouteBelt(a.Id, b.Id);
        Assert.Equal("err_belt_route", bounded.Error);
    }

    [Fact]
    public void Long_belts_replicate_with_acceptable_traffic()
    {
        // 10 cintas de ~170 celdas con carga escasa: el peor caso de tramos distintos por segundo.
        var (w, p) = BeltTests.Open();
        var extractors = new List<Link>();
        for (var i = 0; i < 10; i++)
        {
            var z = -85 + i * 5;
            var a = BeltTests.Place(w, p, "storage_crate", -85, z);
            var b = BeltTests.Place(w, p, "storage_crate", 85, z);
            extractors.Add(BeltTests.Lay(w, p, a, b));
            
        }
        var rng = new Random(7);
        var raw = 0L;
        var compressed = 0L;
        var hops = 0L;
        for (var second = 0; second < 30; second++)
        {
// Entrada irregular y escasa: el peor caso para codificar por tramos.
            foreach (var l in extractors)
                w.Machines[l.From].In.Add("ore", rng.Next(0, 4));
            for (var t = 0; t < 10; t++)
            {
                var clock = System.Diagnostics.Stopwatch.GetTimestamp();
                w.Step();
                hops += System.Diagnostics.Stopwatch.GetTimestamp() - clock;
            }
            using var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                foreach (var id in w.DirtySimSlots) WorldCodec.WriteSlotsCompact(bw, w.Links[id], w.Content);
            w.DirtySimSlots.Clear();
            raw += ms.Length;
            using var packed = new MemoryStream();
            using (var d = new System.IO.Compression.DeflateStream(packed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
                d.Write(ms.GetBuffer(), 0, (int)ms.Length);
            compressed += packed.Length;
        }
        var msPerTick = hops * 1000.0 / System.Diagnostics.Stopwatch.Frequency / 300;
        _out.WriteLine($"10 cintas largas ({extractors[0].Slots.Length} huecos): {raw / 30} B/s sin comprimir, {compressed / 30} B/s comprimido por cliente; tick {msPerTick:F3} ms");
        Assert.True(compressed / 30 < 20000, $"{compressed / 30} B/s");
        Assert.True(msPerTick < 10, $"{msPerTick} ms/tick");
    }
}

public class BeltRouteNetworkTests
{
    private const string KeyA = "client-identity-route-a";
    private const string KeyB = "client-identity-route-b";

    private static (uint a, uint b) Both(GameClient ca, Command a, GameClient cb, Command b) => (ca.Send(a), cb.Send(b));

    [Fact]
    public void Two_players_on_the_same_cells_one_wins_the_other_is_rejected_and_keeps_its_credits()
    {
        var (world, p) = BeltTests.Open("server-builder");
        var left = BeltTests.Place(world, p, "storage_crate", -10, -60);
        var right = BeltTests.Place(world, p, "storage_crate", 10, -60);
        var left2 = BeltTests.Place(world, p, "storage_crate", -10, -50);
        var right2 = BeltTests.Place(world, p, "storage_crate", 10, -50);
        world.Players.Remove("server-builder");
        world.ClearReplicationState();

        using var h = new NetHarness(new ServerOptions { CheckEveryTicks = 20 }, world);
        var a = h.Join(KeyA);
        var b = h.Join(KeyB);
        var pa = world.Players[KeyA];
        var pb = world.Players[KeyB];

        // Rutas previstas por cada cliente, que comparten celdas: ambas son válidas por separado.
        var ra = new List<(int x, int z)> { (-8, -60) };
        for (var x = -7; x <= 9; x++) ra.Add((x, -60));
        var rb = new List<(int x, int z)> { (-8, -50), (-7, -50), (-7, -51), (-7, -52), (-7, -53), (-7, -54), (-7, -55), (-7, -56), (-7, -57), (-7, -58), (-7, -59), (-7, -60) };
        for (var x = -6; x <= 9; x++) rb.Add((x, -60));
        rb.Add((9, -59));
        Assert.Null(a.World!.CheckBeltRoute(left.Id, right.Id, ra));

        var credits = world.Credits;
        CommandResult? resA = null, resB = null;
        var (ia, ib) = Both(a, World.LinkCommand(left.Id, right.Id, ra), b, new Command { Type = CommandType.Link, A = left2.Id, B = right.Id, Cells = rb });
        a.ResultReceived += (id, r) => { if (id == ia) resA = r; };
        b.ResultReceived += (id, r) => { if (id == ib) resB = r; };
        h.Pump(() => resA != null && resB != null);

// Gana quien llega antes al servidor; el otro recibe el motivo y no gasta nada.
        Assert.True(resA!.Value.Ok != resB!.Value.Ok);
        Assert.Equal("err_belt_stale", resA.Value.Ok ? resB.Value.Error : resA.Value.Error);
        Assert.Single(world.Links);
        var built = resA.Value.Ok ? ra : rb;
        Assert.Equal(built, world.Links.Values.Single().Cells);
        Assert.Equal(credits - built.Count * world.Content.Balance.BeltCreditsPerCell, world.Credits);
        _ = pa; _ = pb; _ = right2;

        // Ambos clientes ven lo mismo y siguen sincronizados.
        var checks = a.HashChecks;
        h.Pump(() => a.HashChecks >= checks + 2 && b.HashChecks >= checks + 2, 20000);
        Assert.Equal(built, b.World!.Links.Values.Single().Cells);
        Assert.Equal(0, a.HashMismatches + b.HashMismatches);
    }

    [Fact]
    public void Long_route_survives_network_save_load_and_reconnection_intact()
    {
        var root = Path.Combine(Path.GetTempPath(), "pluton-route-" + Guid.NewGuid());
        try
        {
            var store = new Pluton.Persistence.SaveStore("test-belts", root);
            var (world, p) = BeltTests.Open("server-builder");
                    var a = BeltTests.Place(world, p, "storage_crate", -80, -60);
            var b = BeltTests.Place(world, p, "storage_crate", 80, -60);
            world.Players.Remove("server-builder");
            world.ClearReplicationState();

            List<(int x, int z)> sent;
            using (var h = new NetHarness(new ServerOptions { CheckEveryTicks = 20, Store = store, SaveSlot = "ruta" }, world))
            {
                var c = h.Join(KeyA);
                world.Players[KeyA].Bag.Add("recovered", 1);
                // El cliente traza con su espejo y una curva intermedia real, como haría la herramienta.
                var plan = new BeltPlanner(c.World!);
                plan.SetOrigin(a.Id, null);
                plan.SetDestination(b.Id, null);
                Assert.Null(plan.AddWaypoint(0, -30));
                for (var i = 0; i < 2000 && plan.State != BeltPlanState.Ready; i++) plan.Tick(0, 1000);
                var command = plan.Confirm()!;
                sent = command.Cells!;
                Assert.True(sent.Count > 180);
                CommandResult? result = null;
                var id = c.Send(command);
                c.ResultReceived += (rid, r) => { if (rid == id) result = r; };
                h.Pump(() => result != null);
                Assert.True(result!.Value.Ok, result.Value.Error);
                h.Pump(() => c.World!.Links.Count == 1);
                Assert.Equal(sent, c.World!.Links.Values.Single().Cells);

                a.In.Add("ore", 500);
                world.DirtyMachines.Add(a.Id);
                h.Settle(400);

                // Reconexión: el trazado y la carga llegan con la instantánea.
                c.Disconnect();
                h.Pump(() => h.Server.ConnectedCount == 0);
                var again = h.Join(KeyA);
                var link = again.World!.Links.Values.Single();
                Assert.Equal(sent, link.Cells);
                Assert.True(link.Load > 0);
                var checks = again.HashChecks;
                h.Pump(() => again.HashChecks >= checks + 2, 20000);
                Assert.Equal(0, again.HashMismatches);
                h.Server.SaveBlocking();
            }

            var loaded = store.Load("ruta", Fixture.Content);
            var kept = loaded.Links.Values.Single();
            Assert.Equal(sent, kept.Cells);
            Assert.True(kept.Load > 0);
            Assert.Equal(500, loaded.CountEverywhere("ore"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Corrupt_route_in_a_command_kicks_only_the_sender()
    {
        using var h = new NetHarness();
        var good = h.Join(KeyA);
        var bad = h.Join(KeyB);
        var pw = new PacketWriter();
        var w = pw.Begin(MsgType.Command);
        w.Write(1u);
        w.Write((byte)CommandType.Link);
        w.Write(1); w.Write(2); w.Write(0); w.Write(0); w.Write(0L); w.Write("");
        w.Write7BitEncodedInt(3);
        w.Write(0); w.Write(0);
        w.Write7BitEncodedInt(int.MaxValue); // tramo imposible
        w.Write7BitEncodedInt(5);
        pw.Send(bad.Transport, 0, Channel.Reliable);
        h.Pump(() => bad.State == ClientState.Closed);
        Assert.Equal(ClientState.InGame, good.State);
        Assert.Empty(h.Server.World.Links);
    }
}
