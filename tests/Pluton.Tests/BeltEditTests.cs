using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Insertar un divisor o unificador en una cinta tendida, y mover máquinas, sin perder ni duplicar nada.</summary>
public class BeltEditTests
{
    private static (World w, PlayerState p, Machine a, Machine b, Link l) StraightBelt(int cells = 8)
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", 0, -40);
        var width = w.Content.Machines["storage_crate"].W;
        var b = BeltTests.Place(w, p, "storage_crate", width + cells, -40);
        p.X = (width + cells / 2) * 2f;
        var route = Enumerable.Range(0, cells).Select(i => (width + i, -40)).ToList();
        Assert.True(w.Apply(p, new Command { Type = CommandType.Link, A = a.Id, B = b.Id, Cells = route }).Ok);
        return (w, p, a, b, w.Links.Values.Last());
    }

    private static long Everything(World w, string item) => w.CountEverywhere(item);

    [Theory]
    [InlineData("splitter")]
    [InlineData("merger")]
    public void Inserting_keeps_load_in_place_and_conserves_items_and_credits(string def)
    {
        var (w, p, a, b, l) = StraightBelt();
        foreach (var slot in new[] { 0, 5, 12, 13, 20, 31 }) Assert.True(w.SeedBeltSlot(l, slot, "scrap"));
        var (x, z) = l.Cells[3];
        var paid = l.Paid;
        var items = Everything(w, "scrap");
        var machineCost = w.Content.Machines[def].Credits;
        var credits = w.Credits;

        var result = w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = def, A = x, B = z });
        Assert.True(result.Ok, result.Error);

        var junction = w.Machines.Values.Last();
        Assert.Equal(def, junction.Def);
        Assert.Equal(junction.Id, w.MachineAt(0, x, z));
        var head = w.Links[l.Id];
        var tail = w.Links.Values.Single(o => o.From == junction.Id);
        Assert.Equal(a.Id, head.From);
        Assert.Equal(junction.Id, head.To);
        Assert.Equal(b.Id, tail.To);
        Assert.Equal(new[] { 0, 5 }, Occupied(head));
        // 12 y 13 estaban en la celda de la pieza: pasan a los primeros huecos libres del tramo siguiente; 20 y 31 conservan su sitio.
        Assert.Equal(new[] { 0, 1, 4, 15 }, Occupied(tail));
        Assert.Equal(items, Everything(w, "scrap"));
        Assert.Equal(paid, head.Paid + tail.Paid);
        Assert.Equal(credits - machineCost, w.Credits);
        Assert.Equal(8, head.Cells.Count + tail.Cells.Count + 1);
        Assert.Contains(w.BeltPorts(junction, false), port => (port.X, port.Z) == head.Cells[^1]);
        Assert.Contains(w.BeltPorts(junction, true), port => (port.X, port.Z) == tail.Cells[0]);
        Assert.Contains(junction.Id, w.DirtyMachines);
        Assert.Contains(head.Id, w.DirtyLinks);
        Assert.Contains(tail.Id, w.DirtyLinks);
    }

    private static int[] Occupied(Link l) => Enumerable.Range(0, l.Slots.Length).Where(i => l.Slots[i] != null).ToArray();

    [Fact]
    public void A_full_belt_sends_only_what_does_not_fit_to_the_depot()
    {
        var (w, p, _, _, l) = StraightBelt();
        for (var i = 0; i < l.Slots.Length; i++) Assert.True(w.SeedBeltSlot(l, i, "scrap"));
        var items = Everything(w, "scrap");
        var depot = w.DepotOf(0)!;
        var inDepot = depot.In.Get("scrap");
        var (x, z) = l.Cells[3];
        Assert.True(w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "splitter", A = x, B = z }).Ok);
        Assert.Equal(items, Everything(w, "scrap"));
        Assert.Equal(inDepot + w.Content.Balance.BeltSlotsPerCell, depot.In.Get("scrap"));
    }

    [Theory]
    [InlineData("splitter")]
    [InlineData("merger")]
    public void A_junction_only_says_it_is_passing_while_load_reaches_it(string def)
    {
        var (w, p, a, _, l) = StraightBelt();
        var (x, z) = l.Cells[4];
        Assert.True(w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = def, A = x, B = z }).Ok);
        var junction = w.Machines.Values.Single(m => m.Def == def);
        w.Step();
        Assert.Equal(MachineStatus.Idle, junction.Status);

        a.In.Add("scrap", 20);
        var passed = false;
        for (var i = 0; i < 300 && !passed; i++)
        {
            w.Step();
            passed = junction.Status == MachineStatus.Passing;
        }
        Assert.True(passed);

        for (var i = 0; i < 3000; i++) w.Step();
        Assert.Equal(MachineStatus.Idle, junction.Status);
    }

    [Fact]
    public void A_junction_with_nowhere_to_send_load_does_not_say_it_is_passing()
    {
        var (w, p) = BeltTests.Open();
        var junction = BeltTests.Place(w, p, "splitter", 0, -40);
        junction.In.Add("scrap", 1);
        w.Step();
        Assert.Equal(MachineStatus.Idle, junction.Status);
    }

    [Fact]
    public void The_factory_keeps_running_and_conserves_after_inserting()
    {
        var (w, p, a, b, l) = StraightBelt();
        a.In.Add("scrap", 40);
        for (var i = 0; i < 40; i++) w.Step();
        var (x, z) = l.Cells[4];
        var items = Everything(w, "scrap");
        Assert.True(w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "splitter", A = x, B = z }).Ok);
        for (var i = 0; i < 1500; i++)
        {
            w.Step();
            Assert.Equal(items, Everything(w, "scrap"));
        }
        Assert.Equal(items, b.In.Get("scrap") + a.In.Get("scrap") + w.DepotOf(0)!.In.Get("scrap"));
        Assert.True(b.In.Get("scrap") >= 40 - 4);
    }

    [Fact]
    public void Inserted_junction_survives_a_save_with_its_load()
    {
        var (w, p, _, _, l) = StraightBelt();
        Assert.True(w.SeedBeltSlot(l, 20, "scrap"));
        var (x, z) = l.Cells[2];
        Assert.True(w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "merger", A = x, B = z }).Ok);
        var copy = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(copy));
        Assert.Equal(1, copy.Links.Values.Sum(o => o.Load));
    }

    [Theory]
    [InlineData(0, "err_junction_end")]
    [InlineData(7, "err_junction_end")]
    public void Ends_are_rejected_without_charging(int cell, string expected)
    {
        var (w, p, _, _, l) = StraightBelt();
        var (x, z) = l.Cells[cell];
        var credits = w.Credits;
        var machines = w.Machines.Count;
        var result = w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "splitter", A = x, B = z });
        Assert.False(result.Ok);
        Assert.Equal(expected, result.Error);
        Assert.Equal(credits, w.Credits);
        Assert.Equal(machines, w.Machines.Count);
    }

    [Fact]
    public void Other_pieces_locks_range_and_empty_cells_are_rejected()
    {
        var (w, p, _, _, l) = StraightBelt();
        var (x, z) = l.Cells[3];
        Assert.Equal("err_invalid", w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "smelter", A = x, B = z }).Error);
        Assert.Equal("err_gone", w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "splitter", A = x, B = z + 3 }).Error);
        w.Upgrades.Remove("tech_logistics");
        Assert.Equal("err_locked", w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "splitter", A = x, B = z }).Error);
        w.Upgrades.Add("tech_logistics");
        p.X = 400;
        Assert.Equal("err_range", w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "splitter", A = x, B = z }).Error);
        Assert.Single(w.Links.Values, o => o.Id == l.Id && o.Cells.Count == 8);
    }

    [Fact]
    public void Lack_of_materials_leaves_the_belt_untouched()
    {
        var (w, p, _, _, l) = StraightBelt();
        p.Bag.TryRemove("recovered", p.Bag.Get("recovered"));
        var depot = w.DepotOf(0)!;
        depot.In.TryRemove("recovered", depot.In.Get("recovered"));
        foreach (var m in w.Machines.Values) m.In.TryRemove("recovered", m.In.Get("recovered"));
        var (x, z) = l.Cells[3];
        Assert.Equal("err_cost", w.Apply(p, new Command { Type = CommandType.InsertJunction, Text = "splitter", A = x, B = z }).Error);
        Assert.Equal(8, l.Cells.Count);
        Assert.Equal(-1, w.MachineAt(0, x, z));
    }

    [Fact]
    public void Junction_on_a_bend_takes_the_rotation_its_ports_need()
    {
        var (w, p) = BeltTests.Open();
        var a = BeltTests.Place(w, p, "storage_crate", 0, -40);
        var x0 = w.Content.Machines["storage_crate"].W;
        var b = BeltTests.Place(w, p, "storage_crate", x0 + 2, -35);
        p.X = (x0 + 2) * 2f;
        p.Z = -38 * 2f;
        var route = new List<(int, int)> { (x0, -40), (x0 + 1, -40), (x0 + 2, -40), (x0 + 2, -39), (x0 + 2, -38), (x0 + 2, -37), (x0 + 2, -36) };
        Assert.True(w.Apply(p, new Command { Type = CommandType.Link, A = a.Id, B = b.Id, Cells = route }).Ok);
        var (bendX, bendZ) = route[2];

        foreach (var def in new[] { "merger", "splitter" })
        {
            Assert.Null(w.JunctionOnBeltError(p, def, bendX, bendZ, out _, out var rot));
            var inputs = w.BeltPorts(0, def, bendX, bendZ, rot, false).Select(port => (port.X, port.Z));
            var outputs = w.BeltPorts(0, def, bendX, bendZ, rot, true).Select(port => (port.X, port.Z));
            Assert.Contains(route[1], inputs);
            Assert.Contains(route[3], outputs);
        }
    }

    [Fact]
    public void Move_error_is_the_same_rule_the_server_applies()
    {
        var (w, p, a, b, _) = StraightBelt();
        Assert.Null(w.MoveMachineError(p, a.Id, 0, -46, 0));
        Assert.Equal("err_gone", w.MoveMachineError(p, 999999, 0, -46, 0));
        var depot = w.DepotOf(0)!;
        Assert.Equal("err_builtin", w.MoveMachineError(p, depot.Id, 0, -46, 0));
        // Sobre su propia cinta sí se puede, porque se traza de nuevo; sobre otra máquina, no.
        Assert.NotNull(w.MoveMachineError(p, a.Id, b.X, b.Z, 0));
        var far = p.X;
        p.X = 400;
        Assert.Equal("err_range", w.MoveMachineError(p, a.Id, 0, -46, 0));
        p.X = far;
        var error = w.MoveMachineError(p, a.Id, 0, -46, 0);
        Assert.Equal(error == null, w.Apply(p, new Command { Type = CommandType.Move, A = a.Id, B = 0, C = -46, D = 0 }).Ok);
    }
}
