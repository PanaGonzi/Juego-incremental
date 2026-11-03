using System.Text.Json;
using System.Text.RegularExpressions;
using Pluton.Game;
using Pluton.Networking;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Correcciones de la QA de primera experiencia (INC-01, 03, 04, 07 y 10): procedencia de materiales, guía y marcadores.</summary>
public class QaFixesTests
{
    private static Machine Crate(World w, PlayerState p)
    {
        p.Bag.Add("recovered", 6);
        w.Credits += 25;
        foreach (var d in w.DebrisById.Values.ToList()) w.DebrisById.Remove(d.Id);
        w.Reindex();
        return BeltTests.Place(w, p, "storage_crate", 20, -40);
    }

    // ---------- INC-01: una sola regla de procedencia ----------

    [Fact]
    public void Upgrade_takes_ingots_from_any_container_of_the_planet_as_contracts_do()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Crate(w, p);
        crate.In.Add("ingot", 10);
        w.Credits = 1000;
        w.DirtyMachines.Clear();
        w.DirtyPlayers.Clear();

        var result = w.Apply(p, new Command { Type = CommandType.BuyUpgrade, Text = "tech_assembly" });

        Assert.True(result.Ok, result.Error);
        Assert.Contains("tech_assembly", w.Upgrades);
        Assert.Equal(0, crate.In.Get("ingot"));
        Assert.Equal(800, w.Credits);
        Assert.Contains(crate.Id, w.DirtyMachines);
        Assert.Contains(p.Key, w.DirtyPlayers);
    }

    [Fact]
    public void Upgrade_pays_from_bag_first_and_splits_between_containers()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Crate(w, p);
        var depot = Fixture.Find(w, "depot");
        p.Bag.Add("ingot", 3);
        crate.In.Add("ingot", 4);
        depot.In.Add("ingot", 4);
        w.Credits = 1000;

        Assert.True(w.Apply(p, new Command { Type = CommandType.BuyUpgrade, Text = "tech_assembly" }).Ok);

        Assert.Equal(0, p.Bag.Get("ingot"));
        Assert.Equal(1, crate.In.Get("ingot") + depot.In.Get("ingot"));
    }

    [Fact]
    public void Stored_materials_go_before_goods_waiting_to_be_sold()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Crate(w, p);
        var terminal = Fixture.Find(w, "export_terminal");
        terminal.In.Add("ingot", 20);
        crate.In.Add("ingot", 12);
        w.Credits = 1000;

        Assert.True(w.Apply(p, new Command { Type = CommandType.BuyUpgrade, Text = "tech_assembly" }).Ok);

        Assert.Equal(20, terminal.In.Get("ingot"));
        Assert.Equal(2, crate.In.Get("ingot"));
    }

    [Fact]
    public void Failed_purchase_touches_nothing()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Crate(w, p);
        p.Bag.Add("ingot", 2);
        crate.In.Add("ingot", 7);
        w.Credits = 1000;

        var shortItems = w.Apply(p, new Command { Type = CommandType.BuyUpgrade, Text = "tech_assembly" });

        Assert.Equal("err_cost", shortItems.Error);
        Assert.Equal((2L, 7L, 1000L), (p.Bag.Get("ingot"), crate.In.Get("ingot"), w.Credits));
        Assert.DoesNotContain("tech_assembly", w.Upgrades);

        // Con objetos de sobra pero sin créditos tampoco se descuenta nada.
        crate.In.Add("ingot", 10);
        w.Credits = 10;
        Assert.Equal("err_cost", w.Apply(p, new Command { Type = CommandType.BuyUpgrade, Text = "tech_assembly" }).Error);
        Assert.Equal((2L, 17L, 10L), (p.Bag.Get("ingot"), crate.In.Get("ingot"), w.Credits));
    }

    [Fact]
    public void Containers_of_another_planet_and_machine_buffers_do_not_count()
    {
        var (w, p) = Fixture.NewWorld();
        w.DepotOf(1)!.In.Add("ingot", 100);
        Fixture.Find(w, "recycler").Out.Add("ingot", 100);

        Assert.Equal(0, w.PlanetStock(p, "ingot"));
    }

    [Fact]
    public void Report_warns_when_it_only_adds_up_with_what_the_terminal_will_sell()
    {
        var (w, p) = Fixture.NewWorld();
        var terminal = Fixture.Find(w, "export_terminal");
        var u = w.Content.Upgrades["tech_assembly"];
        var need = u.Cost["ingot"];
        w.Credits = u.Credits;
        p.Bag.Add("ingot", need - 4);
        terminal.In.Add("ingot", 6);
        terminal.AutoSell = true;
        terminal.Reserves["ingot"] = 1;

        var line = w.CheckCost(p, u.Credits, u.Cost).Lines.Single(l => l.Item == "ingot");
        Assert.Equal(5, line.OnSale);
        Assert.True(line.AtRisk);
        // Basta reservar lo que falta para que deje de estar en riesgo; sin venta automática, tampoco hay riesgo.
        terminal.Reserves["ingot"] = 4;
        Assert.False(w.CheckCost(p, u.Credits, u.Cost).Lines.Single(l => l.Item == "ingot").AtRisk);
        terminal.Reserves["ingot"] = 0;
        terminal.AutoSell = false;
        Assert.False(w.CheckCost(p, u.Credits, u.Cost).AtRisk.Any());
        Assert.True(w.CheckCost(p, u.Credits, u.Cost).Ok);
    }

    [Fact]
    public void Report_tells_exactly_what_is_missing_and_how_much_there_is()
    {
        var (w, p) = Fixture.NewWorld();
        var crate = Crate(w, p);
        crate.In.Add("ingot", 3);
        p.Bag.Add("ingot", 4);
        w.Credits = 150;
        var u = w.Content.Upgrades["tech_assembly"];

        var report = w.CheckCost(p, u.Credits, u.Cost);

        Assert.False(report.Ok);
        Assert.Equal(50, report.CreditsShort);
        var line = Assert.Single(report.Missing);
        Assert.Equal(("ingot", 7L, 10L, 3L), (line.Item, line.Have, line.Need, line.Short));
        // Lo que dice la interfaz y lo que acepta el servidor coinciden.
        Assert.Equal(report.Ok, w.CanAfford(p, u.Credits, u.Cost));
    }

    // ---------- INC-03: el objetivo dice cuánto falta y de dónde sacarlo ----------

    private static void GoToObjective(World w, string id) => w.ObjectiveIndex = w.Content.Objectives.FindIndex(o => o.Id == id);

    [Fact]
    public void Building_objective_short_of_material_points_to_the_scrap_when_nothing_is_ready()
    {
        var (w, p) = Fixture.NewWorld();
        GoToObjective(w, "o_smelter");
        w.Credits = 1000;
        p.Bag.Add("recovered", 7);

        var g = w.GuideObjective(p);

        Assert.True(g.Blocked);
        var line = Assert.Single(g.Cost!.Missing);
        Assert.Equal(("recovered", 5L), (line.Item, line.Short));
        Assert.Equal(GuideAction.Cut, g.Action);
        Assert.Equal("scrap", g.Item);
        Assert.Equal("debris:scrap_pile", g.Target);
        Assert.NotNull(w.FindObjectiveTarget(g.Target!, p.Planet, 0, 0));
    }

    [Fact]
    public void Guide_follows_the_chain_scrap_in_bag_then_waiting_then_collecting()
    {
        var (w, p) = Fixture.NewWorld();
        GoToObjective(w, "o_smelter");
        w.Credits = 1000;
        var recycler = Fixture.Find(w, "recycler");

        p.Bag.Add("scrap", 6);
        var deposit = w.GuideObjective(p);
        Assert.Equal((GuideAction.Deposit, "scrap", "machine:recycler"), (deposit.Action, deposit.Item, deposit.Target));

        p.Bag.TryRemove("scrap", 6);
        recycler.In.Add("scrap", 6);
        Assert.Equal(GuideAction.Wait, w.GuideObjective(p).Action);

        recycler.Out.Add("recovered", 3);
        var collect = w.GuideObjective(p);
        Assert.Equal((GuideAction.Collect, "recovered", "machine:recycler"), (collect.Action, collect.Item, collect.Target));
    }

    [Fact]
    public void Objective_with_everything_ready_keeps_its_own_target()
    {
        var (w, p) = Fixture.NewWorld();
        GoToObjective(w, "o_smelter");
        w.Credits = 1000;
        p.Bag.Add("recovered", 12);

        var g = w.GuideObjective(p);

        Assert.False(g.Blocked);
        Assert.Equal(w.Content.Objectives[w.ObjectiveIndex].Target, g.Target);
    }

    [Fact]
    public void Contract_and_upgrade_objectives_point_to_production_when_short_and_to_a_terminal_when_ready()
    {
        var (w, p) = Fixture.NewWorld();
        GoToObjective(w, "o_assembly");
        w.Credits = 1000;
        Fixture.Find(w, "depot").In.Add("ingot", 10);
        Assert.False(w.GuideObjective(p).Blocked);
        Assert.Equal("machine:export_terminal", w.GuideObjective(p).Target);

        Fixture.Find(w, "depot").In.TryRemove("ingot", 4);
        var g = w.GuideObjective(p);
        Assert.True(g.Blocked);
        Assert.Equal(4, g.Cost!.Missing.Single().Short);
    }

    [Fact]
    public void Credits_shortage_sends_to_the_terminal()
    {
        var (w, p) = Fixture.NewWorld();
        GoToObjective(w, "o_assembly");
        w.Credits = 20;
        p.Bag.Add("ingot", 10);

        var g = w.GuideObjective(p);

        Assert.Equal(180, g.Cost!.CreditsShort);
        Assert.Equal((GuideAction.Sell, "machine:export_terminal"), (g.Action, g.Target));
    }

    [Fact]
    public void Every_objective_has_somewhere_to_point()
    {
        foreach (var o in Fixture.Content.Objectives)
            Assert.False(string.IsNullOrEmpty(o.Target), $"{o.Id} sin destino para la brújula");
    }

    [Fact]
    public void Guide_is_only_a_reading_it_changes_nothing_replicated()
    {
        var (w, p) = Fixture.NewWorld();
        GoToObjective(w, "o_smelter");
        w.DirtyMachines.Clear();
        w.DirtyPlayers.Clear();

        w.GuideObjective(p);

        Assert.Empty(w.DirtyMachines);
        Assert.Empty(w.DirtyPlayers);
    }

    // ---------- INC-02: rumbos de la brújula ----------

    [Theory]
    [InlineData(0f, 0f)]                    // mirando a −Z
    [InlineData(-MathF.PI / 2, 90f)]        // el giro negativo mira a +X, el este
    [InlineData(MathF.PI, 180f)]
    [InlineData(MathF.PI / 2, 270f)]
    public void Heading_grows_clockwise_from_north(float yaw, float expected) =>
        Assert.Equal(expected, CompassMath.Heading(yaw), 0.01f);

    [Theory]
    [InlineData(0f, -10f, 0f)]
    [InlineData(10f, 0f, 90f)]
    [InlineData(0f, 10f, 180f)]
    [InlineData(-10f, 0f, 270f)]
    public void Bearing_matches_the_heading_convention(float x, float z, float expected) =>
        Assert.Equal(expected, CompassMath.Bearing(0, 0, x, z), 0.01f);

    [Theory]
    [InlineData(10f, 350f, 20f)]
    [InlineData(350f, 10f, -20f)]
    [InlineData(180f, 0f, 180f)]
    public void Relative_takes_the_short_way_and_positive_is_right(float bearing, float heading, float expected) =>
        Assert.Equal(expected, CompassMath.Relative(bearing, heading), 0.01f);

    // ---------- INC-04: textos y marcadores ----------

    private static Dictionary<string, string> Table(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(ContentDb.LocateDirectory(), "loc", language + ".json")))!;

    private static HashSet<string> ControlActions()
    {
        var source = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ContentDb.LocateDirectory())!, "src", "Game", "Scripts", "GameView.cs"));
        var actions = Regex.Matches(source, @"\[""([a-z_0-9]+)""\] = new\[\] \{ Godot\.Key").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Contains("hotbar_1", actions);
        Assert.Contains("interact_insert", actions);
        return actions;
    }

    [Fact]
    public void The_key_token_recognises_digits_but_leaves_numeric_arguments_alone()
    {
        Assert.Equal("hotbar_1", LocTokens.Action.Match("[{hotbar_1}] Sacar").Groups[1].Value);
        Assert.False(LocTokens.Action.IsMatch("Faltan {0} de {1}"));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Every_placeholder_in_every_string_resolves(string language)
    {
        var actions = ControlActions();
        var unresolved = new List<string>();
        foreach (var (key, text) in Table(language))
        {
            // Lo que el motor deja sin sustituir: ni acción ni argumento numérico.
            foreach (Match m in Regex.Matches(text, @"\{([^{}]*)\}"))
            {
                var token = m.Groups[1].Value;
                if (Regex.IsMatch(token, @"^\d+$")) continue;
                if (!LocTokens.Action.IsMatch(m.Value) || !actions.Contains(token)) unresolved.Add($"{key}: {m.Value}");
            }
        }
        Assert.True(unresolved.Count == 0, string.Join("\n", unresolved));
    }

    [Fact]
    public void Spanish_and_english_have_the_same_keys()
    {
        var es = Table("es").Keys.ToHashSet();
        var en = Table("en").Keys.ToHashSet();
        Assert.Empty(es.Except(en));
        Assert.Empty(en.Except(es));
    }

    // ---------- INC-07: un roce de la rueda no entra en Desmontar ----------

    [Fact]
    public void Wheel_never_lands_on_dismantle()
    {
        var slots = HotbarLayout.Default.ToList();
        var removal = slots.IndexOf("tool:remove");
        foreach (var direction in new[] { 1, -1 })
            for (var from = -1; from < slots.Count; from++)
                Assert.NotEqual(removal, HotbarLayout.Step(slots, from, direction));
    }

    [Fact]
    public void Wheel_still_moves_between_the_rest_and_can_leave_dismantle()
    {
        var slots = HotbarLayout.Default.ToList();
        var removal = slots.IndexOf("tool:remove");
        Assert.Equal(removal + 1, HotbarLayout.Step(slots, removal - 1, 1));
        Assert.Equal(removal - 1, HotbarLayout.Step(slots, removal + 1, -1));
        Assert.Equal(removal + 1, HotbarLayout.Step(slots, removal, 1));
    }

    // ---------- INC-10: la entrada más directa ----------

    private static List<(int x, int z)> Route(BeltPlanner plan)
    {
        for (var i = 0; i < 400 && plan.State is not (BeltPlanState.Ready or BeltPlanState.Failed); i++) plan.Tick(0, 4000);
        Assert.Equal(BeltPlanState.Ready, plan.State);
        return plan.Route!.ToList();
    }

    [Fact]
    public void Default_entry_is_the_closest_side_not_the_one_behind_the_machine()
    {
        var (w, p) = BeltTests.Open();
        // Fundición con su frente al norte (rot 0): la entrada trasera queda al sur; el origen llega por el oeste.
        var smelter = BeltTests.Place(w, p, "smelter", 20, -40);
        var crate = BeltTests.Place(w, p, "storage_crate", 8, -40);

        var auto = new BeltPlanner(w);
        auto.SetOrigin(crate.Id, null);
        auto.SetDestination(smelter.Id, null);
        var direct = Route(auto);

        var back = w.BeltPorts(smelter, false).First(q => !q.Side);
        var aimed = new BeltPlanner(w);
        aimed.SetOrigin(crate.Id, null);
        aimed.SetDestination(smelter.Id, (back.X, back.Z));
        var around = Route(aimed);

        Assert.True(direct.Count < around.Count, $"directa {direct.Count} frente a rodear {around.Count}");
        Assert.True(direct[^1].x < smelter.X, $"entra por el costado del origen: {direct[^1]}");
        Assert.Equal(direct[^1], auto.EntryPort);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Objective_five_no_longer_says_the_input_is_only_at_the_back(string language)
    {
        var text = Table(language)["objective_o_link"];
        Assert.DoesNotContain(language == "es" ? "entra por detrás" : "input at the back", text);
    }
}
