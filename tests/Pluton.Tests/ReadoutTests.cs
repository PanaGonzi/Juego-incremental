using Pluton.Networking;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Lecturas del panel de máquina: la causa de parada y las cifras salen del estado real.</summary>
public class ReadoutTests
{
    [Fact]
    public void SinMaterialIndicaQueFaltaYCuanto()
    {
        var (w, _) = Fixture.NewWorld();
        var m = Fixture.Find(w, "recycler");
        w.Step();
        Assert.Equal(MachineStatus.NoInput, m.Status);
        var read = w.ReadMachine(m);
        var recipe = w.Content.Recipes["recycle_scrap"];
        var input = recipe.Inputs.First();
        Assert.Equal(StopCause.MissingInput, read.Cause);
        Assert.Equal(input.Key, read.CauseItem);
        Assert.Equal(0, read.CauseHave);
        Assert.Equal(input.Value, read.CauseNeed);
        Assert.Equal(0, read.LinksIn);
    }

    [Fact]
    public void SalidaLlenaIndicaObjetoYLimite()
    {
        var (w, _) = Fixture.NewWorld();
        var m = Fixture.Find(w, "recycler");
        var recipe = w.Content.Recipes["recycle_scrap"];
        foreach (var (item, qty) in recipe.Inputs) m.In.Add(item, qty * 3);
        var output = recipe.Outputs.First().Key;
        m.Out.Add(output, w.Content.Balance.OutputBuffer);
        w.Step();
        Assert.Equal(MachineStatus.OutputFull, m.Status);
        var read = w.ReadMachine(m);
        Assert.Equal(StopCause.OutputFull, read.Cause);
        Assert.Equal(output, read.CauseItem);
        Assert.Equal(w.Content.Balance.OutputBuffer, read.CauseNeed);
    }

    [Fact]
    public void TeoricaCoincideConLaProduccionMedidaAPlenaMarcha()
    {
        var (w, _) = Fixture.NewWorld();
        var m = Fixture.Find(w, "recycler");
        var recipe = w.Content.Recipes["recycle_scrap"];
        foreach (var (item, qty) in recipe.Inputs) m.In.Add(item, qty * 1000);
        var output = recipe.Outputs.First().Key;
        var read = w.ReadMachine(m);
        var perMinute = read.Theoretical.Single(t => t.item == output).perMinute;
        var ticksPerMinute = w.Content.Balance.TicksPerSecond * 60;
        var before = w.ProducedTotal(output);
        for (var i = 0; i < ticksPerMinute; i++)
        {
            w.Step();
            m.Out.Clear();
        }
        var measured = w.ProducedTotal(output) - before;
        Assert.InRange(measured, perMinute * 0.9, perMinute * 1.1);
    }

    [Fact]
    public void EnergiaDelPlanetaSumaGeneracion()
    {
        var (w, _) = Fixture.NewWorld();
        var core = Fixture.Find(w, "aux_core");
        var (supply, demand) = w.PowerEstimate(core.Planet);
        Assert.Equal(w.Content.Machines["aux_core"].PowerGen, supply);
        Assert.Equal(0, demand);
    }

    private static Dictionary<string, long> Snapshot(Machine depot) => depot.In.Items.ToDictionary(kv => kv.Key, kv => kv.Value);

    private static void AssertDepotGained(RemovalPreview preview, Dictionary<string, long> before, Machine depot)
    {
        var delta = new Dictionary<string, long>();
        foreach (var (item, qty) in depot.In.Items)
        {
            var change = qty - before.GetValueOrDefault(item);
            if (change != 0) delta[item] = change;
        }
        Assert.Equal(preview.Items.OrderBy(kv => kv.Key), delta.OrderBy(kv => kv.Key));
    }

    private static Link LoadedBelt(World w, PlayerState p, Machine from, Machine to)
    {
        var recipe = w.Content.Machines[to.Def].Recipe;
        from.In.Add(recipe != null ? w.Content.Recipes[recipe].Inputs.First().Key : "recovered", 150);
        var belt = BeltTests.Lay(w, p, from, to);
        for (var i = 0; i < 400 && belt.Load == 0; i++) w.Step();
        Assert.True(belt.Load > 0, "la cinta debe llevar carga para que la prueba diga algo");
        return belt;
    }

    [Fact]
    public void DesmontarMaquinaDevuelveExactamenteLoPrevisto()
    {
        var (w, p) = BeltTests.Open();
        var source = BeltTests.Place(w, p, "storage_crate", 0, -30);
        var smelter = BeltTests.Place(w, p, "smelter", 10, -30);
        var belt = LoadedBelt(w, p, source, smelter);
        smelter.In.Add("scrap", 3);
        smelter.Out.Add("ingot", 2);
        smelter.Processing = true;

        var preview = w.PreviewRemoveMachine(p, smelter.Id);
        Assert.Null(preview.Error);
        Assert.Equal(1, preview.Belts);
        Assert.True(preview.Credits >= w.Content.Machines["smelter"].Credits + belt.Paid);
        var depot = w.DepotOf(p.Planet)!;
        var before = Snapshot(depot);
        var credits = w.Credits;

        Assert.True(w.Apply(p, new Command { Type = CommandType.Remove, A = smelter.Id }).Ok);

        Assert.Equal(preview.Credits, w.Credits - credits);
        AssertDepotGained(preview, before, depot);
    }

    [Fact]
    public void DesmontarCintaYViaDevuelveLoPrevisto()
    {
        var (w, p) = BeltTests.Open();
        var source = BeltTests.Place(w, p, "storage_crate", 0, -30);
        var target = BeltTests.Place(w, p, "storage_crate", 10, -30);
        var belt = LoadedBelt(w, p, source, target);

        var preview = w.PreviewRemoveBelt(p, belt.Id);
        Assert.Null(preview.Error);
        Assert.NotEmpty(preview.Items);
        var depot = w.DepotOf(p.Planet)!;
        var before = Snapshot(depot);
        var credits = w.Credits;
        Assert.True(w.Apply(p, new Command { Type = CommandType.Unlink, A = belt.Id }).Ok);
        Assert.Equal(preview.Credits, w.Credits - credits);
        AssertDepotGained(preview, before, depot);

        p.X = 28;
        p.Z = -64;
        Assert.True(w.Apply(p, new Command { Type = CommandType.PlaceRail, A = 14, B = -32, C = 30, D = -32 }).Ok);
        var rail = w.RailSegments.Values.Last();
        var railPreview = w.PreviewRemoveRail(p, rail.Id);
        Assert.Null(railPreview.Error);
        Assert.True(railPreview.Credits > 0);
        credits = w.Credits;
        Assert.True(w.Apply(p, new Command { Type = CommandType.RemoveRail, A = rail.Id }).Ok);
        Assert.Equal(railPreview.Credits, w.Credits - credits);
    }

    [Fact]
    public void LaPrevisionEnUnaCopiaCoincideConLaDelServidor()
    {
        var (w, p) = BeltTests.Open();
        var source = BeltTests.Place(w, p, "storage_crate", 0, -30);
        var smelter = BeltTests.Place(w, p, "smelter", 10, -30);
        LoadedBelt(w, p, source, smelter);
        smelter.Out.Add("ingot", 2);

        var copy = WorldCodec.Deserialize(Fixture.Content, WorldCodec.Serialize(w));
        var expected = w.PreviewRemoveMachine(p, smelter.Id);
        var mirrored = copy.PreviewRemoveMachine(copy.Players[p.Key], smelter.Id);
        Assert.Equal(expected.Credits, mirrored.Credits);
        Assert.Equal(expected.Items.OrderBy(kv => kv.Key), mirrored.Items.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void DosJugadoresDesmontandoLaMismaMaquinaNoDuplicanLaDevolucion()
    {
        var (w, a) = BeltTests.Open();
        var b = Fixture.AddPlayer(w, "player-b");
        var crate = BeltTests.Place(w, a, "storage_crate", 0, -30);
        b.X = a.X;
        b.Z = a.Z;
        crate.In.Add("ingot", 7);
        var preview = w.PreviewRemoveMachine(b, crate.Id);
        Assert.Null(preview.Error);
        var depot = w.DepotOf(a.Planet)!;
        var before = Snapshot(depot);
        var credits = w.Credits;

        Assert.True(w.Apply(a, new Command { Type = CommandType.Remove, A = crate.Id }).Ok);
        var second = w.Apply(b, new Command { Type = CommandType.Remove, A = crate.Id });

        Assert.False(second.Ok);
        Assert.Equal("err_gone", second.Error);
        Assert.Equal(preview.Credits, w.Credits - credits);
        AssertDepotGained(preview, before, depot);
    }

    [Fact]
    public void LoInstaladoEnLaBaseExplicaPorQueNoSeDesmonta()
    {
        var (w, p) = BeltTests.Open();
        var depot = w.DepotOf(p.Planet)!;
        Fixture.StandAt(w, p, depot);
        var preview = w.PreviewRemoveMachine(p, depot.Id);
        Assert.Equal("err_builtin", preview.Error);
        Assert.Equal(preview.Error, w.Apply(p, new Command { Type = CommandType.Remove, A = depot.Id }).Error);
    }

    [Fact]
    public void ElLugarDeColocacionCoincideConElServidorTambienAlAlcance()
    {
        var (w, p) = BeltTests.Open();
        p.X = 0;
        p.Z = -300;
        var far = w.PlacementSiteError(p, "storage_crate", 0, -30, 0, out _);
        Assert.Equal("err_range", far);
        Assert.Equal(far, w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -30 }).Error);
        p.Z = -60;
        Assert.Null(w.PlacementSiteError(p, "storage_crate", 0, -30, 0, out _));
        Assert.True(w.Apply(p, new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -30 }).Ok);
        Assert.Equal("err_blocked", w.PlacementSiteError(p, "storage_crate", 0, -30, 0, out _));
    }

    [Theory]
    [InlineData("smelter", MachineCategory.Production)]
    [InlineData("ore_extractor", MachineCategory.Extraction)]
    [InlineData("rtg_generator", MachineCategory.Energy)]
    [InlineData("radiator", MachineCategory.Energy)]
    [InlineData("station", MachineCategory.Logistics)]
    [InlineData("cargo_pad", MachineCategory.Logistics)]
    [InlineData("storage_crate", MachineCategory.Storage)]
    [InlineData("export_terminal", MachineCategory.Storage)]
    public void LaCategoriaDelCatalogoSaleDelTipoDeMaquina(string def, MachineCategory expected) =>
        Assert.Equal(expected, World.CategoryOf(Fixture.Content.Machines[def]));
}

/// <summary>Dos clientes reales pidiendo el mismo desmontaje a la vez, con la previsión hecha sobre su copia.</summary>
public class RemovalNetworkTests
{
    [Fact]
    public void Dos_clientes_desmontan_a_la_vez_y_solo_uno_recibe_devolucion()
    {
        var (world, _) = BeltTests.Open("server-only-player");
        world.Players.Remove("server-only-player");
        using var h = new NetHarness(new ServerOptions(), world);
        var a = h.Join("clave-construccion-a");
        var b = h.Join("clave-construccion-b");
        foreach (var key in new[] { "clave-construccion-a", "clave-construccion-b" })
        {
            world.Players[key].X = 0;
            world.Players[key].Z = -60;
            world.Players[key].Bag.Add("recovered", 50);
        }
        CommandResult? placed = null;
        var placeId = a.Send(new Command { Type = CommandType.Place, Text = "storage_crate", A = 0, B = -30 });
        void OnPlaced(uint id, CommandResult r) { if (id == placeId) placed = r; }
        a.ResultReceived += OnPlaced;
        h.Pump(() => placed != null);
        a.ResultReceived -= OnPlaced;
        Assert.True(placed!.Value.Ok, placed.Value.Error);
        var crate = world.Machines.Values.Last();
        h.Pump(() => a.World!.Machines.ContainsKey(crate.Id) && b.World!.Machines.ContainsKey(crate.Id) && a.LocalPlayer != null && b.LocalPlayer != null);

        var preview = b.World!.PreviewRemoveMachine(world.Players["clave-construccion-b"], crate.Id);
        Assert.Null(preview.Error);
        var depot = world.DepotOf(0)!;
        var recoveredBefore = depot.In.Get("recovered");
        var credits = world.Credits;

        var results = new List<CommandResult>();
        var idA = a.Send(new Command { Type = CommandType.Remove, A = crate.Id });
        var idB = b.Send(new Command { Type = CommandType.Remove, A = crate.Id });
        void OnA(uint id, CommandResult r) { if (id == idA) results.Add(r); }
        void OnB(uint id, CommandResult r) { if (id == idB) results.Add(r); }
        a.ResultReceived += OnA;
        b.ResultReceived += OnB;
        h.Pump(() => results.Count == 2);

        Assert.Single(results, r => r.Ok);
        Assert.Single(results, r => r.Error == "err_gone");
        Assert.Equal(preview.Items["recovered"], depot.In.Get("recovered") - recoveredBefore);
        Assert.Equal(preview.Credits, world.Credits - credits);
    }
}
