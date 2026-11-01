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
}
