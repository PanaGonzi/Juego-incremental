using Pluton.Persistence;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

public class Fixture
{
    public static readonly ContentDb Content = ContentDb.Load(ContentDb.LocateDirectory());

    public static (World world, PlayerState player) NewWorld(string key = "player-a", float? x = null, float? z = null)
    {
        var w = World.CreateNew(Content);
        var p = AddPlayer(w, key, x, z);
        return (w, p);
    }

    public static PlayerState AddPlayer(World w, string key, float? x = null, float? z = null)
    {
        var p = new PlayerState { Key = key, Num = w.NextPlayerNum++, Name = key, X = x ?? 0, Z = z ?? 28, Connected = true };
        w.Players[key] = p;
        return p;
    }

    public static Machine Find(World w, string def) => w.Machines.Values.First(m => m.Def == def);

    public static void StandAt(World w, PlayerState p, Machine m)
    {
        var (x, z) = w.MachineCenterMm(m);
        p.X = x / 1000f;
        p.Z = z / 1000f;
    }

    public static void Reveal(World w, PlayerState p, ResourceNode node)
    {
        foreach (var d in w.DebrisById.Values.Where(d => d.CoversNode == node.Id).ToList())
        {
            p.X = d.X * 2f;
            p.Z = d.Z * 2f;
            while (w.DebrisById.ContainsKey(d.Id))
            {
                w.Step();
                w.Step();
                Assert.True(w.Apply(p, new Command { Type = CommandType.CutDebris, A = d.Id }).Ok);
            }
        }
    }
}

public class SimulationTests
{
    [Fact]
    public void Content_loads_and_new_world_is_stable()
    {
        var a = World.CreateNew(Fixture.Content);
        var b = World.CreateNew(Fixture.Content);
        Assert.Equal(WorldCodec.Hash(a), WorldCodec.Hash(b));
        Assert.True(a.DebrisById.Count > 50);
    }

    [Fact]
    public void Cutting_gives_finite_material_and_removes_debris()
    {
        var (w, p) = Fixture.NewWorld();
        var d = w.DebrisById.Values.First(x => x.Def == "scrap_pile");
        p.X = d.X * 2f; p.Z = d.Z * 2f;
        var def = w.Content.Balance.Debris[d.Def];
        var hits = 0;
        while (w.DebrisById.ContainsKey(d.Id))
        {
            w.Step(); w.Step();
            Assert.True(w.Apply(p, new Command { Type = CommandType.CutDebris, A = d.Id }).Ok);
            hits++;
        }
        Assert.Equal(def.Hp, hits);
        Assert.Equal(def.Hp * def.PerHit + def.Final, p.Bag.Get(def.Drop));
        Assert.False(w.Apply(p, new Command { Type = CommandType.CutDebris, A = d.Id }).Ok);
    }

    [Fact]
    public void Cutting_respects_cooldown_and_range()
    {
        var (w, p) = Fixture.NewWorld();
        var d = w.DebrisById.Values.First(x => x.Def == "scrap_pile");
        p.X = d.X * 2f + 500; p.Z = d.Z * 2f;
        Assert.Equal("err_range", w.Apply(p, new Command { Type = CommandType.CutDebris, A = d.Id }).Error);
        p.X = d.X * 2f;
        Assert.True(w.Apply(p, new Command { Type = CommandType.CutDebris, A = d.Id }).Ok);
        Assert.Equal("err_cooldown", w.Apply(p, new Command { Type = CommandType.CutDebris, A = d.Id }).Error);
    }

    [Fact]
    public void Place_is_transactional_when_cost_is_missing()
    {
        var (w, p) = Fixture.NewWorld();
        var before = w.Credits;
        var count = w.Machines.Count;
        p.X = 0; p.Z = 0;
        var r = w.Apply(p, new Command { Type = CommandType.Place, Text = "rtg_generator", A = 0, B = -8 });
        Assert.Equal("err_cost", r.Error);
        Assert.Equal(before, w.Credits);
        Assert.Equal(count, w.Machines.Count);
    }

    [Fact]
    public void Remove_returns_cost_and_contents_without_loss()
    {
        var (w, p) = Fixture.NewWorld();
        p.X = 0; p.Z = 0;
        p.Bag.Add("recovered", 6);
        var credits = w.Credits;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -8 }).Ok);
        var crate = w.Machines.Values.Last();
        crate.In.Add("ingot", 7);
        Assert.Equal(credits - w.Content.Machines["storage_crate"].Credits, w.Credits);

        Assert.True(w.Apply(p, new Command { Type = CommandType.Remove, A = crate.Id }).Ok);
        var depot = Fixture.Find(w, "depot");
        Assert.Equal(credits, w.Credits);
        Assert.Equal(6, depot.In.Get("recovered"));
        Assert.Equal(7, depot.In.Get("ingot"));
    }

    [Fact]
    public void Production_conserves_materials_by_recipe()
    {
        var (w, p) = Fixture.NewWorld();
        var recycler = Fixture.Find(w, "recycler");
        Fixture.StandAt(w, p, recycler);
        p.Bag.Add("scrap", 8);
        Assert.True(w.Apply(p, new Command { Type = CommandType.Insert, A = recycler.Id }).Ok);
        Assert.Equal(0, p.Bag.Get("scrap"));
        for (var i = 0; i < 400; i++) w.Step();
        Assert.Equal(4, recycler.Out.Get("recovered"));
        Assert.Equal(0, w.CountEverywhere("scrap"));
        Assert.Equal(MachineStatus.NoInput, recycler.Status);
    }

    [Fact]
    public void Machine_without_power_stops_and_reports_it()
    {
        var (w, p) = Fixture.NewWorld();
        var recycler = Fixture.Find(w, "recycler");
        w.Machines.Remove(Fixture.Find(w, "aux_core").Id);
        w.Reindex();
        recycler.In.Add("scrap", 4);
        for (var i = 0; i < 100; i++) w.Step();
        Assert.Equal(MachineStatus.NoPower, recycler.Status);
        Assert.Equal(0, recycler.Out.Get("recovered"));
    }

    [Fact]
    public void Full_output_blocks_machine()
    {
        var (w, _) = Fixture.NewWorld();
        var recycler = Fixture.Find(w, "recycler");
        recycler.Out.Add("recovered", w.Content.Balance.OutputBuffer);
        recycler.In.Add("scrap", 4);
        for (var i = 0; i < 100; i++) w.Step();
        Assert.Equal(MachineStatus.OutputFull, recycler.Status);
        Assert.Equal(4, recycler.In.Get("scrap"));
    }

    [Fact]
    public void Low_power_slows_production_instead_of_multiplying_it()
    {
        var (w, p) = Fixture.NewWorld();
        var a = Fixture.Find(w, "recycler");
        // Sin energía suficiente, más máquinas no equivalen a más producción.
        for (var i = 0; i < 9; i++)
        {
            var m = new Machine { Id = w.NextId++, Def = "recycler", Planet = 0, X = 30 + i * 3, Z = -60 };
            w.Machines[m.Id] = m;
        }
        w.Reindex();
        foreach (var m in w.Machines.Values.Where(m => m.Def == "recycler")) m.In.Add("scrap", 8);
        for (var i = 0; i < 31; i++) w.Step();
        Assert.Equal(MachineStatus.LowPower, a.Status);
        Assert.Equal(0, a.Out.Get("recovered"));
        for (var i = 0; i < 40; i++) w.Step();
        Assert.True(a.Out.Get("recovered") >= 1);
    }

    [Fact]
    public void Link_moves_output_respecting_range_and_capacity()
    {
        var (w, p) = Fixture.NewWorld();
        var recycler = Fixture.Find(w, "recycler");
        var depot = Fixture.Find(w, "depot");
        p.X = 0; p.Z = 0;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Link, A = recycler.Id, B = depot.Id }).Ok);
        Assert.False(w.Apply(p, new Command { Type = CommandType.Link, A = recycler.Id, B = depot.Id }).Ok);
        recycler.Out.Add("recovered", 3);
        for (var i = 0; i < 30; i++) w.Step();
        Assert.Equal(3, depot.In.Get("recovered"));
        Assert.Equal(0, recycler.Out.Get("recovered"));
    }

    [Fact]
    public void Contract_pays_once_and_consumes_once()
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        depot.In.Add("ingot", 25);
        var credits = w.Credits;
        var reward = w.Content.Contracts["c_first_ingots"].Credits;
        var cmd = new Command { Type = CommandType.FulfillContract, Text = "c_first_ingots" };
        Assert.True(w.Apply(p, cmd).Ok);
        Assert.Equal("err_done", w.Apply(p, cmd).Error);
        Assert.Equal(credits + reward, w.Credits);
        Assert.Equal(15, depot.In.Get("ingot"));
    }

    [Fact]
    public void Contract_with_partial_stock_changes_nothing()
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        depot.In.Add("ingot", 9);
        Assert.Equal("err_cost", w.Apply(p, new Command { Type = CommandType.FulfillContract, Text = "c_first_ingots" }).Error);
        Assert.Equal(9, depot.In.Get("ingot"));
    }

    [Fact]
    public void Two_players_cannot_spend_the_same_balance()
    {
        var (w, a) = Fixture.NewWorld("player-a", 0, 0);
        var b = Fixture.AddPlayer(w, "player-b", 0, 0);
        w.Credits = 25;
        a.Bag.Add("recovered", 6);
        b.Bag.Add("recovered", 6);
        Assert.True(w.Apply(a, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -8 }).Ok);
        Assert.Equal("err_cost", w.Apply(b, new Command { Type = CommandType.Place, Text = "storage_crate", A = 4, B = -8 }).Error);
        Assert.Equal(0, w.Credits);
        Assert.Equal(6, b.Bag.Get("recovered"));
    }

    [Fact]
    public void Two_players_cannot_build_on_the_same_cell_or_cut_twice_for_double_reward()
    {
        var (w, a) = Fixture.NewWorld("player-a", 0, 0);
        var b = Fixture.AddPlayer(w, "player-b", 0, 0);
        w.Credits = 1000;
        a.Bag.Add("recovered", 12);
        b.Bag.Add("recovered", 12);
        Assert.True(w.Apply(a, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -8 }).Ok);
        Assert.Equal("err_blocked", w.Apply(b, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -8 }).Error);

        var d = w.DebrisById.Values.First(x => x.Def == "scrap_pile");
        d.Hp = 1;
        a.X = b.X = d.X * 2f;
        a.Z = b.Z = d.Z * 2f;
        Assert.True(w.Apply(a, new Command { Type = CommandType.CutDebris, A = d.Id }).Ok);
        Assert.Equal("err_gone", w.Apply(b, new Command { Type = CommandType.CutDebris, A = d.Id }).Error);
    }

    [Fact]
    public void Terminal_auto_sell_keeps_reserve()
    {
        var (w, p) = Fixture.NewWorld();
        var terminal = Fixture.Find(w, "export_terminal");
        terminal.In.Add("ingot", 30);
        Assert.True(w.Apply(p, new Command { Type = CommandType.SetAutoSell, A = terminal.Id, B = 1, Text = "ingot", Amount = 10 }).Ok);
        for (var i = 0; i < 2000; i++) w.Step();
        Assert.Equal(10, terminal.In.Get("ingot"));
    }

    [Fact]
    public void Disabled_auto_sell_does_not_sell()
    {
        var (w, _) = Fixture.NewWorld();
        var terminal = Fixture.Find(w, "export_terminal");
        terminal.In.Add("ingot", 30);
        var credits = w.Credits;
        for (var i = 0; i < 200; i++) w.Step();
        Assert.Equal(30, terminal.In.Get("ingot"));
        Assert.Equal(credits, w.Credits);
    }

    [Fact]
    public void Visitor_cannot_build_and_guest_cannot_promote_itself()
    {
        var (w, p) = Fixture.NewWorld();
        p.Role = Role.Visitor;
        Assert.Equal("err_permission", w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate" }).Error);
        p.Role = Role.Builder;
        Assert.Equal("err_permission", w.Apply(p, new Command { Type = CommandType.SetRole, Text = p.Key, A = (int)Role.Admin }).Error);
    }

    [Fact]
    public void Extractor_requires_a_revealed_node()
    {
        var (w, p) = Fixture.NewWorld();
        var node = w.Nodes.Values.First(n => n.Item == "ore");
        p.X = node.X * 2f; p.Z = node.Z * 2f;
        p.Bag.Add("recovered", 20);
        var place = new Command { Type = CommandType.Place, Text = "ore_extractor", A = node.X, B = node.Z };
        Assert.Equal("err_debris", w.Apply(p, place).Error);
        Fixture.Reveal(w, p, node);
        Assert.True(node.Revealed);
        Assert.True(w.Apply(p, place).Ok);
        for (var i = 0; i < 60; i++) w.Step();
        Assert.True(w.Machines.Values.Last().Out.Get("ore") >= 1);
    }

    [Fact]
    public void Invalid_content_is_rejected_whole()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pluton-" + Guid.NewGuid());
        Directory.CreateDirectory(temp);
        foreach (var f in Directory.GetFiles(ContentDb.LocateDirectory())) File.Copy(f, Path.Combine(temp, Path.GetFileName(f)));
        File.WriteAllText(Path.Combine(temp, "recipes.json"), "[{\"id\":\"x\",\"ticks\":10,\"inputs\":{\"nada\":1},\"outputs\":{}}]");
        Assert.Throws<ContentException>(() => ContentDb.Load(temp));
        File.WriteAllText(Path.Combine(temp, "recipes.json"), "no json");
        Assert.Throws<ContentException>(() => ContentDb.Load(temp));
        Directory.Delete(temp, true);
    }
}

public class SaveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pluton-saves-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private (World, PlayerState) Played()
    {
        var (w, p) = Fixture.NewWorld();
        var r = Fixture.Find(w, "recycler");
        Fixture.StandAt(w, p, r);
        p.Bag.Add("scrap", 12);
        w.Apply(p, new Command { Type = CommandType.Insert, A = r.Id });
        w.Credits = 777;
        for (var i = 0; i < 45; i++) w.Step();
        return (w, p);
    }

    [Fact]
    public void Round_trip_preserves_hash_and_continues_identically()
    {
        var store = new SaveStore("test", _root);
        var (w, _) = Played();
        store.Save("a", w);
        var loaded = store.Load("a", Fixture.Content);
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        for (var i = 0; i < 200; i++) { w.Step(); loaded.Step(); }
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        Assert.Equal(w.Tick, loaded.Tick);
    }

    [Fact]
    public void Corrupt_save_falls_back_to_backup()
    {
        var store = new SaveStore("test", _root);
        var (w, _) = Played();
        store.Save("a", w);
        var hashFirst = WorldCodec.Hash(w);
        w.Credits += 5;
        store.Save("a", w);
        var path = store.PathOf("a");
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        Assert.Equal(hashFirst, WorldCodec.Hash(store.Load("a", Fixture.Content)));
    }

    [Fact]
    public void Interrupted_write_leaves_previous_save_intact()
    {
        var store = new SaveStore("test", _root);
        var (w, _) = Played();
        store.Save("a", w);
        var path = store.PathOf("a");
        File.WriteAllBytes(path + ".tmp", new byte[] { 1, 2, 3 });
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(store.Load("a", Fixture.Content)));
    }

    [Fact]
    public void Newer_format_is_refused_without_touching_file()
    {
        var store = new SaveStore("test", _root);
        var (w, _) = Played();
        var encoded = SaveStore.Encode(WorldCodec.Serialize(w), "x", 1, DateTime.UtcNow);
        BitConverter.GetBytes(WorldCodec.FormatVersion + 1).CopyTo(encoded, 4);
        store.Write("nuevo", encoded);
        var before = File.ReadAllBytes(store.PathOf("nuevo"));
        Assert.Throws<SaveStore.NewerFormatException>(() => store.Load("nuevo", Fixture.Content));
        Assert.Equal(before, File.ReadAllBytes(store.PathOf("nuevo")));
    }

    [Fact]
    public void Save_load_cycles_do_not_duplicate_materials()
    {
        var store = new SaveStore("test", _root);
        var (w, _) = Played();
        var total = w.CountEverywhere("scrap") + w.CountEverywhere("recovered") * 2;
        for (var i = 0; i < 5; i++)
        {
            store.Save("a", w);
            w = store.Load("a", Fixture.Content);
            for (var t = 0; t < 40; t++) w.Step();
            Assert.Equal(total, w.CountEverywhere("scrap") + w.CountEverywhere("recovered") * 2);
        }
    }
}
