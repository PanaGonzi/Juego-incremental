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
    public void Transfers_can_target_a_single_item()
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        Fixture.StandAt(w, p, depot);
        depot.In.Add("ingot", 5);
        depot.In.Add("ore", 7);
        Assert.True(w.Apply(p, new Command { Type = CommandType.Extract, A = depot.Id, Text = "ore" }).Ok);
        Assert.Equal(7, p.Bag.Get("ore"));
        Assert.Equal(5, depot.In.Get("ingot"));
        Assert.Equal("err_nothing", w.Apply(p, new Command { Type = CommandType.Insert, A = depot.Id, Text = "ingot" }).Error);
        Assert.True(w.Apply(p, new Command { Type = CommandType.Insert, A = depot.Id, Text = "ore" }).Ok);
        Assert.Equal(7, depot.In.Get("ore"));
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
    public void Roles_follow_the_hierarchy()
    {
        var (w, owner) = Fixture.NewWorld("owner-key");
        owner.Role = Role.Owner;
        var admin = Fixture.AddPlayer(w, "admin-key");
        var guest = Fixture.AddPlayer(w, "guest-key");
        Command Set(string key, Role role) => new() { Type = CommandType.SetRole, Text = key, A = (int)role };
        Assert.True(w.Apply(owner, Set(admin.Key, Role.Admin)).Ok);
        Assert.True(w.Apply(admin, Set(guest.Key, Role.Visitor)).Ok);
        Assert.Equal(Role.Visitor, guest.Role);
        Assert.Equal("err_permission", w.Apply(guest, Set(guest.Key, Role.Builder)).Error);
        Assert.Equal("err_permission", w.Apply(admin, Set(owner.Key, Role.Visitor)).Error);
        Assert.Equal("err_permission", w.Apply(admin, Set(admin.Key, Role.Builder)).Error);
        Assert.Equal("err_permission", w.Apply(admin, Set(guest.Key, Role.Admin)).Error);
        Assert.True(w.Apply(owner, Set(admin.Key, Role.Builder)).Ok);
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

public class LocalizationTests
{
    private static Dictionary<string, string> Table(string lang) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(ContentDb.LocateDirectory(), "loc", lang + ".json")))!;

    [Fact]
    public void Languages_have_the_same_keys_and_placeholders()
    {
        var es = Table("es");
        var en = Table("en");
        Assert.Empty(es.Keys.Except(en.Keys));
        Assert.Empty(en.Keys.Except(es.Keys));
        var token = new System.Text.RegularExpressions.Regex(@"\{[a-z_0-9]+\}");
        foreach (var (key, value) in es)
            Assert.Equal(token.Matches(value).Select(m => m.Value).OrderBy(x => x), token.Matches(en[key]).Select(m => m.Value).OrderBy(x => x));
    }

    [Fact]
    public void Every_game_id_has_a_text()
    {
        var es = Table("es");
        var c = Fixture.Content;
        foreach (var id in c.Items.Keys) Assert.Contains("item_" + id, es.Keys);
        foreach (var id in c.Machines.Keys) Assert.Contains("machine_" + id, es.Keys);
        foreach (var id in c.Upgrades.Keys) { Assert.Contains("upgrade_" + id, es.Keys); Assert.Contains("upgrade_" + id + "_d", es.Keys); }
        foreach (var id in c.Contracts.Keys) Assert.Contains("contract_" + id, es.Keys);
        foreach (var o in c.Objectives) Assert.Contains("objective_" + o.Id, es.Keys);
        foreach (var s in Enum.GetNames<MachineStatus>()) Assert.Contains("status_" + s, es.Keys);
    }
}

public class EventTests
{
    private static Command Deliver => new() { Type = CommandType.FulfillContract, Text = "c_shadow_express" };

    [Fact]
    public void Activating_works_without_date_and_contract_is_locked_when_off()
    {
        var (w, p) = Fixture.NewWorld();
        var depot = Fixture.Find(w, "depot");
        depot.In.Add("ingot", 100);
        Assert.Equal("err_locked", w.Apply(p, Deliver).Error);
        p.Role = Role.Admin;
        Assert.True(w.Apply(p, new Command { Type = CommandType.SetEvent, Text = "halloween", A = 1 }).Ok);
        Assert.Equal("err_need_stats", w.Apply(p, Deliver).Error);
        Assert.Equal(100, depot.In.Get("ingot"));
    }

    [Fact]
    public void Only_admins_control_events()
    {
        var (w, p) = Fixture.NewWorld();
        Assert.Equal("err_permission", w.Apply(p, new Command { Type = CommandType.SetEvent, Text = "halloween", A = 1 }).Error);
        Assert.Empty(w.ActiveEvents);
    }

    [Fact]
    public void Reward_is_paid_once_and_survives_disabling_save_and_reactivation()
    {
        var (w, p) = Fixture.NewWorld();
        p.Role = Role.Owner;
        var depot = Fixture.Find(w, "depot");
        depot.In.Add("ingot", 100);
        w.SetEvent("halloween", true);
        var debrisAfterFirst = w.DebrisById.Count;
        w.Stats["cleared.shadow_scrap"] = 8;
        w.Stats["train_delivered"] = 120;
        var credits = w.Credits;
        Assert.True(w.Apply(p, Deliver).Ok);
        Assert.Equal("err_done", w.Apply(p, Deliver).Error);
        Assert.Equal(credits + 400, w.Credits);
        Assert.Contains("spectral_train", w.Cosmetics);

        w.SetEvent("halloween", false);
        var loaded = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Contains("spectral_train", loaded.Cosmetics);
        loaded.SetEvent("halloween", true);
        Assert.Equal(debrisAfterFirst, loaded.DebrisById.Count);
        Assert.Equal("err_done", loaded.Apply(loaded.Players[p.Key], Deliver).Error);
    }

    [Theory]
    [InlineData(10, 31, true)]
    [InlineData(11, 5, true)]
    [InlineData(11, 6, false)]
    [InlineData(10, 19, false)]
    public void Season_comes_from_data(int month, int day, bool expected)
    {
        Assert.Equal(expected, Fixture.Content.Events["halloween"].InSeason(new DateTime(2027, month, day)));
    }
}

public class OfflineTests
{
    private static World Factory()
    {
        var (w, p) = Fixture.NewWorld();
        var recycler = Fixture.Find(w, "recycler");
        var terminal = Fixture.Find(w, "export_terminal");
        recycler.In.Add("scrap", 20);
        terminal.In.Add("ingot", 200);
        terminal.AutoSell = true;
        terminal.Reserves["ingot"] = 50;
        w.SavedUnix = 1_000_000;
        return w;
    }

    [Fact]
    public void Applies_once_respecting_recipes_reserves_and_cap()
    {
        var w = Factory();
        var terminal = Fixture.Find(w, "export_terminal");
        var report = OfflineProgress.Apply(w, 1_000_000 + 3600 * 100);
        Assert.NotNull(report);
        Assert.Equal(8 * 3600, report!.SecondsApplied);
        Assert.Equal(50, terminal.In.Get("ingot"));
        var recycler = Fixture.Find(w, "recycler");
        // 20 de chatarra dan como máximo 10: nunca recursos imposibles, y la salida respeta su límite.
        Assert.True(recycler.Out.Get("recovered") <= 10);
        Assert.Equal(20, w.CountEverywhere("scrap") + w.CountEverywhere("recovered") * 2);
        Assert.Equal(150 * w.SellPrice("ingot"), report.CreditsEarned);

        var credits = w.Credits;
        Assert.Null(OfflineProgress.Apply(w, 1_000_000 + 3600 * 100));
        Assert.Equal(credits, w.Credits);
    }

    [Fact]
    public void Clock_moved_backwards_gives_nothing_and_does_not_rearm()
    {
        var w = Factory();
        var credits = w.Credits;
        Assert.Null(OfflineProgress.Apply(w, 999_000));
        Assert.Equal(credits, w.Credits);
        Assert.Equal(1_000_000, w.SavedUnix);
    }

    [Fact]
    public void Repeated_short_restarts_do_not_exceed_real_elapsed_time()
    {
        var a = Factory();
        var b = Factory();
        OfflineProgress.Apply(a, 1_000_000 + 7200);
        for (var i = 1; i <= 12; i++) OfflineProgress.Apply(b, 1_000_000 + 600 * i);
        Assert.Equal(a.Credits, b.Credits);
    }

    [Fact]
    public void Result_never_beats_real_time_simulation()
    {
        // Línea con energía y enlaces: lo aproximado debe quedar por debajo o igual.
        World Build()
        {
            var (w, p) = Fixture.NewWorld();
            var node = w.Nodes.Values.First(n => n.Item == "ore");
            node.Revealed = true;
            foreach (var d in w.DebrisById.Values.Where(d => d.CoversNode == node.Id).ToList()) w.DebrisById.Remove(d.Id);
            var ex = new Machine { Id = w.NextId++, Def = "ore_extractor", Planet = 0, X = node.X, Z = node.Z, NodeId = node.Id };
            w.Machines[ex.Id] = ex;
            var sm = new Machine { Id = w.NextId++, Def = "smelter", Planet = 0, X = node.X - 3, Z = node.Z };
            w.Machines[sm.Id] = sm;
            w.Links[w.NextId] = new Link { Id = w.NextId++, From = ex.Id, To = sm.Id, Planet = 0 };
            var depot = Fixture.Find(w, "depot");
            w.Reindex();
            w.SavedUnix = 1_000_000;
            return w;
        }
        var real = Build();
        for (var i = 0; i < 36000; i++) real.Step();
        var approx = Build();
        OfflineProgress.Apply(approx, 1_000_000 + 3600);
        Assert.True(approx.Stats.GetValueOrDefault("produced.ingot") <= real.Stats.GetValueOrDefault("produced.ingot"));
        Assert.True(approx.Stats.GetValueOrDefault("produced.ingot") > 0);
    }
}

public class ScenarioTests
{
    [Theory]
    [InlineData("small")]
    [InlineData("medium")]
    public void Measurement_worlds_run_and_conserve(string name)
    {
        var w = Scenarios.Build(Fixture.Content, name);
        var size = Scenarios.Sizes[name];
        Assert.InRange(w.Machines.Count, size.Machines, size.Machines + 20);
        Assert.Equal(size.Trains, w.Trains.Count);
        Assert.InRange(w.RailSegments.Count, size.RailSegments, size.RailSegments + size.Trains * 2 + 2);
        for (var i = 0; i < 3000; i++) w.Step();
        Assert.True(w.Stats["sold_credits"] > 0 || w.Stats["produced.parts"] > 0);
        Assert.True(w.Stats.GetValueOrDefault("train_delivered") > 0);
        var loaded = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
    }
}

public class MarsTests
{
    private static (World w, PlayerState p, Machine pad0, Machine pad1) Pads()
    {
        var (w, p) = Fixture.NewWorld("player-a", 40, 40);
        w.Upgrades.Add("tech_shuttle");
        w.Reindex();
        var pad0 = new Machine { Id = w.NextId++, Def = "cargo_pad", Planet = 0, X = 20, Z = 30 };
        var pad1 = new Machine { Id = w.NextId++, Def = "cargo_pad", Planet = 1, X = 20, Z = 20, StationUnload = true };
        w.Machines[pad0.Id] = pad0;
        w.Machines[pad1.Id] = pad1;
        w.Reindex();
        return (w, p, pad0, pad1);
    }

    [Fact]
    public void Cargo_is_never_in_two_places_and_arrives_once()
    {
        var (w, _, a, b) = Pads();
        a.In.Add("cryo_module", 500);
        var maxInFlight = 0L;
        for (var i = 0; i < 2000 && b.In.Get("cryo_module") < 500; i++)
        {
            w.Step();
            Assert.Equal(500, w.CountEverywhere("cryo_module"));
            maxInFlight = Math.Max(maxInFlight, w.Shipments.Values.Sum(s => s.Cargo.Total));
        }
        Assert.Equal(500, b.In.Get("cryo_module"));
        Assert.Equal(0, a.In.Get("cryo_module"));
        Assert.True(maxInFlight > 0);
        Assert.Empty(w.Shipments);
    }

    [Fact]
    public void Receiving_pad_does_not_send_back()
    {
        var (w, _, a, b) = Pads();
        b.In.Add("wafer", 50);
        for (var i = 0; i < 1000; i++) w.Step();
        Assert.Equal(50, b.In.Get("wafer"));
        Assert.Equal(0, a.In.Get("wafer"));
    }

    [Fact]
    public void Save_with_cargo_in_flight_does_not_duplicate()
    {
        var (w, _, a, b) = Pads();
        a.In.Add("wafer", 100);
        for (var i = 0; i < 3000 && w.Shipments.Count == 0; i++) w.Step();
        Assert.NotEmpty(w.Shipments);
        var loaded = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        Assert.Equal(100, loaded.CountEverywhere("wafer"));
        for (var i = 0; i < 1000; i++) loaded.Step();
        Assert.Equal(100, loaded.Machines[b.Id].In.Get("wafer"));
    }

    [Fact]
    public void Heat_slows_only_thermal_planets_and_radiators_fix_it()
    {
        long Wafers(int planet, bool radiator)
        {
            var (w, _) = Fixture.NewWorld();
            var refinery = new Machine { Id = w.NextId++, Def = "wafer_refinery", Planet = planet, X = 40, Z = 40 };
            w.Machines[refinery.Id] = refinery;
            if (radiator) w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "radiator", Planet = planet, X = 50, Z = 40 };
            w.Reindex();
            long made = 0;
            for (var i = 0; i < 3000; i++)
            {
                if (refinery.In.Get("silicon_ore") < 2) refinery.In.Add("silicon_ore", 20);
                w.Step();
                made += refinery.Out.Get("wafer");
                refinery.Out.Clear();
            }
            return made;
        }
        var marsHot = Wafers(1, false);
        var marsCool = Wafers(1, true);
        var pluto = Wafers(0, false);
        Assert.True(marsHot < marsCool / 2, $"sin refrigerar {marsHot}, refrigerada {marsCool}");
        Assert.Equal(pluto, marsCool);
    }

    [Fact]
    public void Removing_destination_pad_mid_flight_delivers_to_depot()
    {
        var (w, p, a, b) = Pads();
        a.In.Add("wafer", 30);
        for (var i = 0; i < 3000 && w.Shipments.Count == 0; i++) w.Step();
        Assert.NotEmpty(w.Shipments);
        w.Machines.Remove(b.Id);
        w.Reindex();
        for (var i = 0; i < 500 && w.Shipments.Count > 0; i++) w.Step();
        Assert.Equal(30, w.DepotOf(1)!.In.Get("wafer"));
    }
}

public class TravelTests
{
    private static (World w, PlayerState p) AtPad()
    {
        var (w, p) = Fixture.NewWorld();
        w.Upgrades.Add("tech_shuttle");
        w.Reindex();
        Fixture.StandAt(w, p, w.Machines.Values.First(m => m.Def == "shuttle_terminal" && m.Planet == 0));
        return (w, p);
    }

    private static Command Go(int planet) => new() { Type = CommandType.Travel, A = planet };

    [Fact]
    public void Travel_needs_upgrade_range_and_valid_destination()
    {
        var (w, p) = Fixture.NewWorld();
        Assert.Equal("err_locked", w.Apply(p, Go(1)).Error);
        w.Upgrades.Add("tech_shuttle");
        p.X = 150; p.Z = 150;
        Assert.Equal("err_range", w.Apply(p, Go(1)).Error);
        Fixture.StandAt(w, p, w.Machines.Values.First(m => m.Def == "shuttle_terminal" && m.Planet == 0));
        Assert.Equal("err_same_planet", w.Apply(p, Go(0)).Error);
        Assert.Equal("err_invalid", w.Apply(p, Go(7)).Error);
    }

    [Fact]
    public void Player_arrives_keeps_inventory_and_cannot_act_in_transit()
    {
        var (w, p) = AtPad();
        p.Bag.Add("ingot", 12);
        Assert.True(w.Apply(p, Go(1)).Ok);
        Assert.Equal("err_transit", w.Apply(p, Go(1)).Error);
        Assert.Equal("err_transit", w.Apply(p, new Command { Type = CommandType.Insert, A = 1 }).Error);
        for (var i = 0; i < w.Content.Balance.TravelTicks + 1; i++) w.Step();
        Assert.Equal(1, p.Planet);
        Assert.Equal(-1, p.TransitTo);
        Assert.Equal(12, p.Bag.Get("ingot"));
        Assert.Equal(20f, p.Z);
    }

    [Fact]
    public void Save_in_transit_resumes_without_losing_player_or_cargo()
    {
        var (w, p) = AtPad();
        p.Bag.Add("ingot", 12);
        Assert.True(w.Apply(p, Go(1)).Ok);
        for (var i = 0; i < 50; i++) w.Step();
        var loaded = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        var q = loaded.Players[p.Key];
        Assert.Equal(1, q.TransitTo);
        for (var i = 0; i < w.Content.Balance.TravelTicks; i++) loaded.Step();
        Assert.Equal(1, q.Planet);
        Assert.Equal(12, q.Bag.Get("ingot"));
    }

    [Fact]
    public void Factory_on_other_planet_keeps_producing_while_player_is_away()
    {
        var (w, p) = AtPad();
        var recycler = Fixture.Find(w, "recycler");
        recycler.In.Add("scrap", 20);
        Assert.True(w.Apply(p, Go(1)).Ok);
        for (var i = 0; i < 400; i++) w.Step();
        Assert.Equal(1, p.Planet);
        Assert.True(recycler.Out.Get("recovered") >= 8);
    }

    [Fact]
    public void Two_players_travel_independently_and_planets_have_separate_power_and_depots()
    {
        var (w, a) = AtPad();
        var b = Fixture.AddPlayer(w, "player-b");
        Fixture.StandAt(w, b, w.Machines.Values.First(m => m.Def == "shuttle_terminal" && m.Planet == 0));
        Assert.True(w.Apply(a, Go(1)).Ok);
        for (var i = 0; i < 300; i++) w.Step();
        Assert.Equal(1, a.Planet);
        Assert.Equal(0, b.Planet);
        var depots = w.Machines.Values.Where(m => m.Def == "depot").Select(m => m.Planet).ToList();
        Assert.Equal(new[] { 0, 1 }, depots);
        Assert.NotEqual(w.DepotOf(0)!.Id, w.DepotOf(1)!.Id);
        var marsDebris = w.DebrisById.Values.First(d => d.Planet == 1);
        b.X = marsDebris.X * 2f;
        b.Z = marsDebris.Z * 2f;
        Assert.Equal("err_gone", w.Apply(b, new Command { Type = CommandType.CutDebris, A = marsDebris.Id }).Error);
    }
}

public class RailTests
{
    private static (World w, PlayerState p, Machine a, Machine b) TwoStations()
    {
        var (w, p) = Fixture.NewWorld("player-a", 40, -60);
        w.Upgrades.Add("tech_rail");
        w.Credits = 100000;
        p.Bag.Add("recovered", 200);
        p.Bag.Add("ingot", 200);
        p.Bag.Add("parts", 50);
        w.Reindex();
        foreach (var d in w.DebrisById.Values.Where(d => Math.Abs(d.Z + 30) < 12).ToList())
            w.DebrisById.Remove(d.Id);
        w.Reindex();
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "station", A = 10, B = -34 }).Ok);
        var a = w.Machines.Values.Last();
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "station", A = 30, B = -34 }).Ok);
        var b = w.Machines.Values.Last();
        return (w, p, a, b);
    }

    private static void Connect(World w, PlayerState p) =>
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 14, B = -32, C = 30, D = -32 }).Ok);

    [Fact]
    public void Rail_shapes_are_validated_and_cost_credits_once()
    {
        var (w, p, _, _) = TwoStations();
        p.X = 0; p.Z = -88;
        // Nodos ricos y resto no afectan a esta validación geométrica.
        var credits = w.Credits;
        Assert.Equal("err_rail_shape", w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 0, B = -44, C = 3, D = -46 }).Error);
        Assert.Equal("err_rail_shape", w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 0, B = -44, C = 3, D = -46, Amount = 1 }).Error);
        Assert.Equal("err_rail_length", w.Apply(p, new Command { Type = CommandType.PlaceRail, A = -20, B = -44, C = 20, D = -44 }).Error);
        Assert.Equal(credits, w.Credits);
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 0, B = -44, C = 4, D = -44 }).Ok);
        Assert.Equal(credits - 4 * w.Content.Balance.Rail.CreditsPerCell, w.Credits);
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 4, B = -44, C = 8, D = -40, Amount = 1 }).Ok);
    }

    [Fact]
    public void Rail_cannot_cross_machines_and_machines_cannot_sit_on_rail()
    {
        var (w, p, _, _) = TwoStations();
        p.X = 20; p.Z = -92;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = 20, B = -46 }).Ok);
        Assert.Equal("err_rail_blocked", w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 18, B = -45, C = 24, D = -45 }).Error);
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 0, B = -48, C = 6, D = -48 }).Ok);
        Assert.Equal("err_blocked", w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = 2, B = -48 }).Error);
    }

    [Fact]
    public void Train_carries_real_cargo_between_stations_and_conserves_it()
    {
        var (w, p, a, b) = TwoStations();
        Connect(w, p);
        b.StationUnload = true;
        a.In.Add("ore", 100);
        Assert.True(w.Apply(p, new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id }).Ok);
        var train = w.Trains.Values.Single();
        var cap = w.Content.Balance.Rail.TrainCapacity;

        long Total() => w.CountEverywhere("ore");
        var total = Total();
        var maxCargo = 0L;
        for (var i = 0; i < 2000 && b.In.Get("ore") < 100; i++)
        {
            w.Step();
            maxCargo = Math.Max(maxCargo, train.Cargo.Total);
            Assert.Equal(total, Total());
        }
        Assert.Equal(100, b.In.Get("ore"));
        Assert.Equal(cap, maxCargo);
        Assert.Equal(100, w.Stats["train_delivered"]);
    }

    [Fact]
    public void Train_explains_why_it_waits()
    {
        var (w, p, a, b) = TwoStations();
        b.StationUnload = true;
        Assert.True(w.Apply(p, new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id }).Ok);
        var t = w.Trains.Values.Single();
        w.Step();
        Assert.Equal("wait_no_cargo", t.WaitReason);

        a.In.Add("ore", 1000);
        for (var i = 0; i < 40; i++) w.Step();
        Assert.Equal(TrainState.Waiting, t.State);
        Assert.Equal("wait_no_route", t.WaitReason);

        Connect(w, p);
        for (var i = 0; i < 300 && t.State != TrainState.Unloading; i++) w.Step();
        Assert.Equal(TrainState.Unloading, t.State);

        b.In.Add("ore", 400);
        for (var i = 0; i < 60; i++) w.Step();
        Assert.Equal("wait_station_full", t.WaitReason);
    }

    [Fact]
    public void Occupied_rail_cannot_be_removed_and_cut_route_stops_train_safely()
    {
        var (w, p, a, b) = TwoStations();
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 14, B = -32, C = 22, D = -32 }).Ok);
        var first = w.RailSegments.Values.Last();
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 22, B = -32, C = 30, D = -32 }).Ok);
        var second = w.RailSegments.Values.Last();
        b.StationUnload = true;
        a.In.Add("ore", 60);
        Assert.True(w.Apply(p, new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id }).Ok);
        var t = w.Trains.Values.Single();
        for (var i = 0; i < 1000 && (t.State != TrainState.Moving || t.Path[t.PathIndex] != first.Id); i++) w.Step();
        Assert.Equal(first.Id, t.Path[t.PathIndex]);

        Assert.Equal("err_rail_occupied", w.Apply(p, new Command { Type = CommandType.RemoveRail, A = first.Id }).Error);
        Assert.True(w.Apply(p, new Command { Type = CommandType.RemoveRail, A = second.Id }).Ok);
        for (var i = 0; i < 200 && t.State == TrainState.Moving; i++) w.Step();
        Assert.Equal(TrainState.Waiting, t.State);
        Assert.Equal("wait_no_route", t.WaitReason);
        Assert.Equal(60, w.CountEverywhere("ore"));

        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 22, B = -32, C = 30, D = -32 }).Ok);
        for (var i = 0; i < 400 && b.In.Get("ore") < 60; i++) w.Step();
        Assert.Equal(60, b.In.Get("ore"));
    }

    [Fact]
    public void Trains_never_share_a_segment_and_never_deadlock()
    {
        var (w, p, a, b) = TwoStations();
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 14, B = -32, C = 22, D = -32 }).Ok);
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 22, B = -32, C = 30, D = -32 }).Ok);
        b.StationUnload = true;
        a.In.Add("ore", 240);
        p.Bag.Add("ingot", 50);
        for (var i = 0; i < 3; i++)
            Assert.True(w.Apply(p, new Command { Type = CommandType.CreateTrain, A = i == 1 ? b.Id : a.Id, B = i == 1 ? a.Id : b.Id }).Ok);
        var blocked = false;
        for (var i = 0; i < 12000 && b.In.Get("ore") < 240; i++)
        {
            w.Step();
            var moving = w.Trains.Values.Where(t => t.State == TrainState.Moving).ToList();
            var current = moving.Where(t => t.PathIndex < t.Path.Count).Select(t => t.Path[t.PathIndex]).ToList();
            Assert.Equal(current.Count, current.Distinct().Count());
            blocked |= w.Trains.Values.Any(t => t.WaitReason == "wait_block");
        }
        Assert.True(blocked, "la prueba debe ejercer la espera por vía ocupada");
        Assert.Equal(240, b.In.Get("ore"));
        Assert.Equal(240, w.CountEverywhere("ore"));
    }

    [Fact]
    public void Removing_train_returns_cargo_and_cost()
    {
        var (w, p, a, b) = TwoStations();
        var depot = Fixture.Find(w, "depot");
        a.In.Add("ore", 30);
        Assert.True(w.Apply(p, new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id }).Ok);
        for (var i = 0; i < 40; i++) w.Step();
        Assert.Equal("err_rail_occupied", w.Apply(p, new Command { Type = CommandType.Remove, A = a.Id }).Error);
        var t = w.Trains.Values.Single();
        var cargo = t.Cargo.Get("ore");
        Assert.True(cargo > 0);
        Assert.True(w.Apply(p, new Command { Type = CommandType.RemoveTrain, A = t.Id }).Ok);
        Assert.Equal(cargo, depot.In.Get("ore"));
        Assert.Equal(30, w.CountEverywhere("ore"));
        Assert.Equal(15, depot.In.Get("ingot"));
    }

    [Fact]
    public void Trains_persist_through_save_mid_route_without_duplicating_cargo()
    {
        var (w, p, a, b) = TwoStations();
        Connect(w, p);
        b.StationUnload = true;
        a.In.Add("ore", 60);
        Assert.True(w.Apply(p, new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id }).Ok);
        var t = w.Trains.Values.Single();
        for (var i = 0; i < 1000 && (t.State != TrainState.Moving || t.PosMm < 3000); i++) w.Step();
        Assert.Equal(TrainState.Moving, t.State);

        var loaded = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        for (var i = 0; i < 500; i++) { w.Step(); loaded.Step(); }
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        Assert.Equal(60, loaded.CountEverywhere("ore"));
        Assert.Equal(60, loaded.Machines[b.Id].In.Get("ore"));
    }

    [Fact]
    public void Train_line_to_rich_nodes_delivers_more_than_all_near_extractors()
    {
        const int ticks = 6000;

        // Base: los tres yacimientos cercanos, consumo inmediato de lo producido.
        var (near, np) = Fixture.NewWorld("player-a");
        var nearNodes = near.Nodes.Values.Where(n => n.Item == "ore" && n.YieldPermille == 1000).ToList();
        foreach (var n in nearNodes)
        {
            n.Revealed = true;
            foreach (var d in near.DebrisById.Values.Where(d => d.CoversNode == n.Id).ToList()) near.DebrisById.Remove(d.Id);
            near.Machines[near.NextId] = new Machine { Id = near.NextId++, Def = "ore_extractor", Planet = 0, X = n.X, Z = n.Z, NodeId = n.Id };
        }
        near.Reindex();
        var nearExtractors = near.Machines.Values.Where(m => m.Def == "ore_extractor").ToList();
        long nearTotal = 0;
        for (var i = 0; i < ticks; i++)
        {
            near.Step();
            foreach (var m in nearExtractors)
            {
                nearTotal += m.Out.Get("ore");
                m.Out.Clear();
            }
        }

        // Línea de tren desde los yacimientos ricos hasta la base.
        var (w, p) = Fixture.NewWorld("player-a", 90, -60);
        w.Upgrades.Add("tech_rail");
        w.Credits = 100000;
        p.Bag.Add("recovered", 200);
        p.Bag.Add("ingot", 200);
        p.Bag.Add("parts", 50);
        foreach (var d in w.DebrisById.Values.ToList()) w.DebrisById.Remove(d.Id);
        var rich = w.Nodes.Values.Where(n => n.Item == "ore" && n.YieldPermille > 1000).ToList();
        foreach (var n in rich)
        {
            n.Revealed = true;
            w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "ore_extractor", Planet = 0, X = n.X, Z = n.Z, NodeId = n.Id };
        }
        w.Machines[w.NextId] = new Machine { Id = w.NextId++, Def = "rtg_generator", Planet = 0, X = 40, Z = -46 };
        w.Reindex();
        var extractors = w.Machines.Values.Where(m => m.Def == "ore_extractor").ToList();

        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "station", A = 30, B = -24 }).Ok);
        var baseStation = w.Machines.Values.Last();
        baseStation.StationUnload = true;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "station", A = 55, B = -37 }).Ok);
        var mine = w.Machines.Values.Last();
        foreach (var e in extractors) Assert.True(w.Apply(p, new Command { Type = CommandType.Link, A = e.Id, B = mine.Id }).Ok, "enlace");
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 34, B = -22, C = 47, D = -35 }).Ok);
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 47, B = -35, C = 55, D = -35 }).Ok);
        Assert.True(w.Apply(p, new Command { Type = CommandType.CreateTrain, A = mine.Id, B = baseStation.Id }).Ok);
        for (var i = 0; i < ticks; i++)
        {
            w.Step();
            baseStation.In.Clear();
        }
        var delivered = w.Stats.GetValueOrDefault("train_delivered");
        Assert.True(delivered >= nearTotal * 3 / 2, $"tren {delivered} frente a cercanos {nearTotal}");
    }

    [Fact]
    public void Rich_nodes_reached_by_train_outproduce_near_nodes()
    {
        var rich = Fixture.Content.Planets[0].Nodes.Where(n => n.YieldPermille > 1000).ToList();
        var near = Fixture.Content.Planets[0].Nodes.Where(n => n.Item == "ore" && n.YieldPermille == 1000).ToList();
        Assert.True(rich.Count >= 3);
        var cell = Fixture.Content.Balance.CellSizeMm;
        var linkCells = Fixture.Content.Balance.LinkMaxMm / cell;
        // Fuera del alcance de los enlaces desde la base: el tren es necesario.
        Assert.All(rich, n => Assert.True(Math.Max(Math.Abs(n.X), Math.Abs(n.Z)) > linkCells * 3));
        // Con todos los yacimientos ricos, más del doble de mineral que con los cercanos.
        var richRate = rich.Sum(n => n.YieldPermille);
        var nearRate = near.Sum(n => n.YieldPermille);
        Assert.True(richRate >= nearRate * 2);
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
    public void V1_save_migrates_keeping_a_copy_and_objective_progress()
    {
        var store = new SaveStore("test", _root);
        var (w, _) = Played();
        w.ObjectiveIndex = 10; // en v1: «o_cryo», ya pasado «o_parts»
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            // Formato v1 reconstruido: sin campos de vías en máquinas y yacimientos, sin listas ferroviarias.
            bw.Write(w.NextId);
            bw.Write(w.NextPlayerNum);
            bw.Write(w.OwnerKey);
            bw.Write7BitEncodedInt64(w.Tick);
            bw.Write7BitEncodedInt64(w.Credits);
            bw.Write7BitEncodedInt(w.ObjectiveIndex);
            bw.Write7BitEncodedInt(0);
            bw.Write7BitEncodedInt(0);
            bw.Write7BitEncodedInt(0);
            bw.Write7BitEncodedInt(w.Machines.Count);
            foreach (var m in w.Machines.Values)
            {
                bw.Write(m.Id); bw.Write(m.Def); bw.Write7BitEncodedInt(m.Planet); bw.Write(m.X); bw.Write(m.Z);
                bw.Write((byte)m.Rot); bw.Write(m.Builtin); bw.Write(m.NodeId);
                bw.Write7BitEncodedInt(m.In.Kinds);
                foreach (var kv in m.In.Items) { bw.Write(kv.Key); bw.Write7BitEncodedInt64(kv.Value); }
                bw.Write7BitEncodedInt(m.Out.Kinds);
                foreach (var kv in m.Out.Items) { bw.Write(kv.Key); bw.Write7BitEncodedInt64(kv.Value); }
                bw.Write(m.Processing); bw.Write7BitEncodedInt64(m.Progress); bw.Write((byte)m.Status); bw.Write(m.AutoSell);
                bw.Write7BitEncodedInt(0);
            }
            bw.Write7BitEncodedInt(0);
            bw.Write7BitEncodedInt(w.DebrisById.Count);
            foreach (var d in w.DebrisById.Values) WorldCodec.WriteDebris(bw, d);
            bw.Write7BitEncodedInt(w.Nodes.Count);
            foreach (var n in w.Nodes.Values)
            {
                bw.Write(n.Id); bw.Write(n.Item); bw.Write7BitEncodedInt(n.Planet); bw.Write(n.X); bw.Write(n.Z); bw.Write(n.Revealed);
            }
            bw.Write7BitEncodedInt(w.Players.Count);
            foreach (var pl in w.Players.Values)
            {
                bw.Write(pl.Key); bw.Write(pl.Name); bw.Write((byte)pl.Role); bw.Write7BitEncodedInt(pl.Planet); bw.Write7BitEncodedInt(pl.Num);
                bw.Write(pl.Connected); bw.Write(pl.X); bw.Write(pl.Y); bw.Write(pl.Z); bw.Write(pl.Yaw);
                bw.Write7BitEncodedInt(0);
            }
        }
        var encoded = SaveStore.Encode(ms.ToArray(), "antiguo", w.Tick, DateTime.UtcNow);
        BitConverter.GetBytes(1).CopyTo(encoded, 4);
        store.Write("vieja", encoded);

        var loaded = store.Load("vieja", Fixture.Content);
        Assert.Equal(13, loaded.ObjectiveIndex);
        Assert.Equal("o_cryo", Fixture.Content.Objectives[loaded.ObjectiveIndex].Id);
        Assert.Equal(777, loaded.Credits);
        Assert.True(loaded.Nodes.Values.Any(n => n.YieldPermille > 1000));
        Assert.Contains(loaded.Machines.Values, m => m.Def == "shuttle_terminal" && m.Planet == 0);
        Assert.Contains(loaded.Machines.Values, m => m.Def == "depot" && m.Planet == 1);
        Assert.True(File.Exists(store.PathOf("vieja") + ".v1.bak"));
        Assert.Equal(encoded, File.ReadAllBytes(store.PathOf("vieja") + ".v1.bak"));
    }

    [Fact]
    public void Real_save_from_alpha_0_1_0_loads_migrates_and_keeps_playing()
    {
        var store = new SaveStore("test", _root);
        var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "save-v1-alpha-0.1.0.plsave"));
        Assert.Equal(1, BitConverter.ToInt32(original, 4));
        store.Write("real", original);

        var world = store.Load("real", Fixture.Content);
        Assert.Contains(world.Machines.Values, m => m.Def == "smelter");
        Assert.Contains(world.Machines.Values, m => m.Def == "shuttle_terminal" && m.Planet == 0);
        Assert.Contains(world.Machines.Values, m => m.Def == "depot" && m.Planet == 1);
        Assert.Equal(original, File.ReadAllBytes(store.PathOf("real") + ".v1.bak"));
        var credits = world.Credits;
        for (var i = 0; i < 300; i++) world.Step();
        Assert.True(world.Credits >= credits);

        store.Save("real", world);
        Assert.Equal(WorldCodec.FormatVersion, BitConverter.ToInt32(File.ReadAllBytes(store.PathOf("real")), 4));
        Assert.Equal(WorldCodec.Hash(world), WorldCodec.Hash(store.Load("real", Fixture.Content)));
    }

    [Fact]
    public void Real_save_from_alpha_0_2_0_keeps_rails_and_trains()
    {
        var store = new SaveStore("test", _root);
        var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "save-v2-alpha-0.2.0-rail.plsave"));
        Assert.Equal(2, BitConverter.ToInt32(original, 4));
        store.Write("real2", original);
        var world = store.Load("real2", Fixture.Content);
        Assert.Single(world.Trains);
        Assert.True(world.RailSegments.Count >= 5);
        Assert.True(File.Exists(store.PathOf("real2") + ".v2.bak"));
        for (var i = 0; i < 600; i++) world.Step();
        Assert.Equal(WorldCodec.Hash(world), WorldCodec.Hash(WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(world))));
    }

    [Fact]
    public void Real_save_from_alpha_0_3_0_gains_mars_content()
    {
        var store = new SaveStore("test", _root);
        var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "save-v3-alpha-0.3.0-rail.plsave"));
        Assert.Equal(3, BitConverter.ToInt32(original, 4));
        store.Write("real3", original);
        var world = store.Load("real3", Fixture.Content);
        Assert.Single(world.Trains);
        Assert.Contains(world.Nodes.Values, n => n.Item == "silicon_ore" && n.Planet == 1);
        for (var i = 0; i < 300; i++) world.Step();
        Assert.Equal(WorldCodec.Hash(world), WorldCodec.Hash(WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(world))));
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
