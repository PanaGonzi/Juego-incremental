namespace Pluton.Simulation;

/// <summary>Causa concreta por la que una máquina de producción no rinde. Se traduce en la interfaz.</summary>
public enum StopCause : byte { None, MissingInput, OutputFull, NoPower, LowPower, Overheated }

/// <summary>Lectura de una máquina para el panel. Solo datos replicados: sirve igual en servidor y cliente.</summary>
public sealed class MachineReadout
{
    public StopCause Cause;
    /// <summary>Objeto que falta o que llena la salida, con su cantidad y la necesaria.</summary>
    public string CauseItem = "";
    public long CauseHave;
    public long CauseNeed;
    /// <summary>Enlaces de entrada y de salida conectados; explican por qué falta material o no se vacía.</summary>
    public int LinksIn;
    public int LinksOut;
    /// <summary>Segundos por ciclo con las mejoras y el rendimiento del yacimiento, sin contar falta de energía.</summary>
    public double CycleSeconds;
    /// <summary>Unidades por minuto de cada salida a pleno rendimiento.</summary>
    public readonly List<(string item, double perMinute)> Theoretical = new();
    public long PowerSupply;
    public long PowerDemand;
}

public sealed partial class World
{
    /// <summary>Energía del planeta según estados replicados: el cliente no conoce los acumulados internos del servidor.</summary>
    public (long supply, long demand) PowerEstimate(int planet)
    {
        long supply = 0, demand = 0;
        foreach (var m in Machines.Values)
        {
            if (m.Planet != planet) continue;
            var d = Content.Machines[m.Def];
            supply += d.PowerGen;
            if (m.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.NoPower or MachineStatus.Overheated) demand += d.Power;
        }
        return (supply, demand);
    }

    /// <summary>Unidades producidas de un objeto en todo el mundo desde el inicio de la partida.</summary>
    public long ProducedTotal(string item) => Stats.GetValueOrDefault("produced." + item);

    /// <summary>Cuántas máquinas del mundo fabrican este objeto: reparte la producción real medida.</summary>
    public int ProducersOf(string item)
    {
        var count = 0;
        foreach (var m in Machines.Values)
        {
            var recipe = Content.Machines[m.Def].Recipe;
            if (recipe != null && Content.Recipes[recipe].Outputs.ContainsKey(item)) count++;
        }
        return count;
    }

    /// <summary>Lo disponible para un contrato: mochila más terminales, almacenes, estaciones y plataformas del planeta.</summary>
    public long ContractStock(PlayerState p, string item)
    {
        var have = p.Bag.Get(item);
        foreach (var m in Machines.Values)
            if (m.Planet == p.Planet && Content.Machines[m.Def].Kind is MachineKind.Terminal or MachineKind.Storage or MachineKind.Station or MachineKind.CargoPad)
                have += m.In.Get(item);
        return have;
    }

    public MachineReadout ReadMachine(Machine m)
    {
        var def = Content.Machines[m.Def];
        var r = new MachineReadout();
        (r.PowerSupply, r.PowerDemand) = PowerEstimate(m.Planet);
        foreach (var l in Links.Values)
        {
            if (l.To == m.Id) r.LinksIn++;
            if (l.From == m.Id) r.LinksOut++;
        }
        if (def.Kind != MachineKind.Production || def.Recipe == null) return r;

        var recipe = Content.Recipes[def.Recipe];
        r.CycleSeconds = CycleSeconds(m, recipe);
        foreach (var (item, qty) in recipe.Outputs)
            r.Theoretical.Add((item, qty * 60.0 / r.CycleSeconds));

        switch (m.Status)
        {
            case MachineStatus.NoInput:
                r.Cause = StopCause.MissingInput;
                foreach (var (item, qty) in recipe.Inputs)
                {
                    if (m.In.Get(item) >= qty) continue;
                    r.CauseItem = item;
                    r.CauseHave = m.In.Get(item);
                    r.CauseNeed = qty;
                    break;
                }
                break;
            case MachineStatus.OutputFull:
                r.Cause = StopCause.OutputFull;
                foreach (var (item, qty) in recipe.Outputs)
                {
                    if (m.Out.Get(item) + qty <= B.OutputBuffer) continue;
                    r.CauseItem = item;
                    r.CauseHave = m.Out.Get(item);
                    r.CauseNeed = B.OutputBuffer;
                    break;
                }
                break;
            case MachineStatus.NoPower: r.Cause = StopCause.NoPower; break;
            case MachineStatus.LowPower: r.Cause = StopCause.LowPower; break;
            case MachineStatus.Overheated: r.Cause = StopCause.Overheated; break;
        }
        return r;
    }
}
