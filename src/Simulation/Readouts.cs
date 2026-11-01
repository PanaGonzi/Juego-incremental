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

/// <summary>Grupo del catálogo de construcción. Se deduce del tipo de máquina: no es un dato aparte que pueda desalinearse.</summary>
public enum MachineCategory : byte { Production, Extraction, Energy, Logistics, Storage }

/// <summary>Lo que devolvería desmontar algo ahora: mismas cuentas que el servidor. Con Error, no se puede desmontar.</summary>
public sealed class RemovalPreview
{
    public string? Error;
    public long Credits;
    /// <summary>Objetos que irían al depósito del planeta: coste, contenido y carga de cintas.</summary>
    public readonly Dictionary<string, long> Items = new();
    /// <summary>Cintas conectadas que se retirarían con la máquina.</summary>
    public int Belts;

    public void Add(string item, long qty)
    {
        if (qty > 0) Items[item] = Items.GetValueOrDefault(item) + qty;
    }
}

public sealed partial class World
{
    public static MachineCategory CategoryOf(MachineDef def) => def.Kind switch
    {
        MachineKind.Production => def.Node != null ? MachineCategory.Extraction : MachineCategory.Production,
        MachineKind.Generator or MachineKind.Radiator => MachineCategory.Energy,
        MachineKind.Station or MachineKind.Shuttle or MachineKind.CargoPad or MachineKind.Splitter or MachineKind.Merger => MachineCategory.Logistics,
        _ => MachineCategory.Storage,
    };

    /// <summary>Validez del lugar para este jugador, sin mirar el coste: el servidor y el fantasma usan esta misma regla.</summary>
    public string? PlacementSiteError(PlayerState p, string defId, int x, int z, int rot, out int nodeId)
    {
        var error = CheckPlacement(p.Planet, defId, x, z, rot, -1, out nodeId);
        if (error != null) return error;
        return InRange(p, x * (long)B.CellSizeMm, z * (long)B.CellSizeMm, B.BuildRangeMm) ? null : "err_range";
    }

    /// <summary>Motivo por el que este jugador no puede mover la máquina ahí, o null. Compartido con el servidor.</summary>
    public string? MoveMachineError(PlayerState p, int id, int x, int z, int rot)
    {
        if (!Machines.TryGetValue(id, out var m) || m.Planet != p.Planet) return "err_gone";
        if (m.Builtin || Content.Machines[m.Def].Kind == MachineKind.Station) return "err_builtin";
        var error = CheckPlacement(p.Planet, m.Def, x, z, rot, id, out _);
        if (error != null) return error;
        return InRange(p, x * (long)B.CellSizeMm, z * (long)B.CellSizeMm, B.BuildRangeMm) ? null : "err_range";
    }

    /// <summary>Motivo por el que este jugador no puede desmontar la máquina, o null. Compartido con el servidor.</summary>
    public string? RemoveMachineError(PlayerState p, int id)
    {
        if (!Machines.TryGetValue(id, out var m) || m.Planet != p.Planet) return "err_gone";
        if (m.Builtin) return "err_builtin";
        var (cx, cz) = MachineCenterMm(m);
        if (!InRange(p, cx, cz, B.BuildRangeMm)) return "err_range";
        foreach (var t in Trains.Values)
            if (t.StationA == id || t.StationB == id) return "err_rail_occupied";
        return DepotOf(m.Planet) == null ? "err_invalid" : null;
    }

    /// <summary>Coste, contenido y materia del ciclo en curso de la máquina, sin sus cintas.</summary>
    private void AddMachineRecovery(Machine m, RemovalPreview r)
    {
        var def = Content.Machines[m.Def];
        foreach (var (item, qty) in def.Cost) r.Add(item, qty * B.RefundPermille / 1000);
        r.Credits += def.Credits * B.RefundPermille / 1000;
        foreach (var kv in m.In.Items) r.Add(kv.Key, kv.Value);
        foreach (var kv in m.Out.Items) r.Add(kv.Key, kv.Value);
        if (m.Processing && def.Recipe != null)
            foreach (var (item, qty) in Content.Recipes[def.Recipe].Inputs) r.Add(item, qty);
    }

    /// <summary>Lo pagado y la carga real de una cinta, con la regla de DismantleBelt.</summary>
    private void AddBeltRecovery(Link l, RemovalPreview r)
    {
        r.Credits += l.Paid * B.RefundPermille / 1000;
        foreach (var item in l.Slots)
            if (item != null) r.Add(item, 1);
    }

    /// <summary>El saldo tiene tope: se muestra lo que entraría de verdad.</summary>
    private RemovalPreview CapCredits(RemovalPreview r)
    {
        r.Credits = Math.Min(r.Credits, Math.Max(0, B.MaxCredits - Credits));
        return r;
    }

    public RemovalPreview PreviewRemoveMachine(PlayerState p, int id)
    {
        var r = new RemovalPreview { Error = RemoveMachineError(p, id) };
        if (!Machines.TryGetValue(id, out var m)) return r;
        AddMachineRecovery(m, r);
        foreach (var l in Links.Values)
        {
            if (l.From != id && l.To != id) continue;
            AddBeltRecovery(l, r);
            r.Belts++;
        }
        return CapCredits(r);
    }

    public RemovalPreview PreviewRemoveBelt(PlayerState p, int id)
    {
        var r = new RemovalPreview();
        if (!Links.TryGetValue(id, out var l) || l.Planet != p.Planet)
        {
            r.Error = "err_gone";
            return r;
        }
        if (DepotOf(l.Planet) == null) r.Error = "err_invalid";
        AddBeltRecovery(l, r);
        return CapCredits(r);
    }

    public RemovalPreview PreviewRemoveRail(PlayerState p, int id)
    {
        var r = new RemovalPreview { Error = RemoveRailError(p, id) };
        if (RailSegments.TryGetValue(id, out var s)) r.Credits = RailRefund(s);
        return CapCredits(r);
    }

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
