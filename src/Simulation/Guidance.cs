namespace Pluton.Simulation;

/// <summary>Una línea de coste: cuánto hay disponible y cuánto se pide.</summary>
public sealed record CostLine(string Item, long Have, long Need, long OnSale = 0)
{
    public long Short => Math.Max(0, Need - Have);
    /// <summary>Alcanza solo contando lo que una terminal con venta automática puede vender antes de pagar.</summary>
    public bool AtRisk => Short == 0 && Have - OnSale < Need;
}

/// <summary>Qué falta exactamente para pagar un coste, con la misma regla que aplica el servidor al cobrarlo.</summary>
public sealed class CostReport
{
    public long CreditsHave { get; init; }
    public long CreditsNeed { get; init; }
    public required IReadOnlyList<CostLine> Lines { get; init; }
    public long CreditsShort => Math.Max(0, CreditsNeed - CreditsHave);
    public IEnumerable<CostLine> Missing => Lines.Where(l => l.Short > 0);
    public bool Ok => CreditsShort == 0 && Lines.All(l => l.Short == 0);
    public IEnumerable<CostLine> AtRisk => Lines.Where(l => l.AtRisk);
}

/// <summary>Qué hacer para reunir lo que falta en el objetivo actual.</summary>
public enum GuideAction { None, Cut, Deposit, Collect, Wait, Sell }

/// <summary>El objetivo actual leído desde el mundo: qué le falta al jugador, cómo conseguirlo y a dónde señalar.</summary>
public sealed class ObjectiveGuide
{
    public CostReport? Cost { get; init; }
    public GuideAction Action { get; init; }
    /// <summary>El objeto que hay que reunir, cortar o depositar según la acción.</summary>
    public string Item { get; init; } = "";
    /// <summary>La máquina implicada en la acción, si la hay.</summary>
    public string Machine { get; init; } = "";
    /// <summary>Destino del marcador: machine:ID, node:OBJETO o debris:ID; null si no hay lugar al que señalar.</summary>
    public string? Target { get; init; }
    public bool Blocked => Cost is { Ok: false };
}

public static class CompassMath
{
    /// <summary>Rumbo de la vista en grados, 0 al norte (−Z) y creciendo hacia el este (+X). El giro de Godot crece al contrario.</summary>
    public static float Heading(float yawRadians) => Normalize(-yawRadians * 180f / MathF.PI);

    /// <summary>Rumbo desde un punto a otro, con el mismo criterio.</summary>
    public static float Bearing(float fromX, float fromZ, float toX, float toZ) => Normalize(MathF.Atan2(toX - fromX, -(toZ - fromZ)) * 180f / MathF.PI);

    /// <summary>Ángulo de un rumbo respecto a la vista, entre −180 y 180; positivo a la derecha.</summary>
    public static float Relative(float bearing, float heading)
    {
        var d = Normalize(bearing - heading);
        return d > 180f ? d - 360f : d;
    }

    private static float Normalize(float degrees) => (degrees % 360f + 360f) % 360f;
}

public sealed partial class World
{
    private const int GuideDepth = 3;

    private bool CountsAsPlanetStock(Machine m, int planet) =>
        m.Planet == planet && Content.Machines[m.Def].Kind is MachineKind.Terminal or MachineKind.Storage or MachineKind.Station or MachineKind.CargoPad;

    /// <summary>
    /// Lo disponible para cualquier pago: mochila más los contenedores del planeta (depósito, cajas, terminales, estaciones y plataformas).
    /// La misma regla sirve para contratos, mejoras y construcciones. Ver D-045.
    /// </summary>
    public long PlanetStock(PlayerState p, string item)
    {
        var have = p.Bag.Get(item);
        foreach (var m in _machineList)
            if (CountsAsPlanetStock(m, p.Planet))
                have += m.In.Get(item);
        return have;
    }

    /// <summary>Lo que las terminales con venta automática del planeta tienen por encima de su reserva: se venderá si nadie lo usa antes.</summary>
    public long OnSale(int planet, string item)
    {
        long total = 0;
        foreach (var m in _machineList)
            if (m.Planet == planet && m.AutoSell && Content.Machines[m.Def].Kind == MachineKind.Terminal)
                total += Math.Max(0, m.In.Get(item) - m.Reserves.GetValueOrDefault(item));
        return total;
    }

    /// <summary>Desglose de un coste con lo disponible ahora: la interfaz nunca calcula otra regla.</summary>
    public CostReport CheckCost(PlayerState p, long credits, IReadOnlyDictionary<string, long> cost) => new()
    {
        CreditsHave = Credits,
        CreditsNeed = credits,
        Lines = cost.Select(kv => new CostLine(kv.Key, PlanetStock(p, kv.Key), kv.Value, OnSale(p.Planet, kv.Key))).ToList(),
    };

    /// <summary>
    /// Descuenta de la mochila, luego de los almacenes y por último de lo que está en tránsito o a la venta
    /// (terminales, estaciones y plataformas). Quien llama ya comprobó que alcanza.
    /// </summary>
    private void TakeFromPlanet(PlayerState p, IReadOnlyDictionary<string, long> cost)
    {
        foreach (var (item, qty) in cost)
        {
            var left = qty;
            var fromBag = Math.Min(left, p.Bag.Get(item));
            if (fromBag > 0) p.Bag.TryRemove(item, fromBag);
            left -= fromBag;
            foreach (var m in _machineList.Where(m => CountsAsPlanetStock(m, p.Planet)).OrderBy(m => Content.Machines[m.Def].Kind == MachineKind.Storage ? 0 : 1))
            {
                if (left == 0) break;
                var take = Math.Min(left, m.In.Get(item));
                if (take == 0) continue;
                m.In.TryRemove(item, take);
                DirtyMachines.Add(m.Id);
                left -= take;
            }
        }
        DirtyPlayers.Add(p.Key);
    }

    /// <summary>Lee el objetivo actual: solo lectura, sin coste de red; el cliente lo calcula sobre su espejo.</summary>
    public ObjectiveGuide GuideObjective(PlayerState p)
    {
        if (ObjectiveIndex >= Content.Objectives.Count) return new ObjectiveGuide();
        var o = Content.Objectives[ObjectiveIndex];
        var need = ObjectiveCost(o, p.Planet);
        if (need == null) return new ObjectiveGuide { Target = o.Target };
        var report = CheckCost(p, need.Value.credits, need.Value.items);
        if (report.Ok) return new ObjectiveGuide { Cost = report, Target = o.Target };

        var first = report.Missing.FirstOrDefault();
        if (first != null)
        {
            var step = TraceSource(p, first.Item, 0);
            if (step != null) return new ObjectiveGuide { Cost = report, Action = step.Action, Item = step.Item, Machine = step.Machine, Target = step.Target };
            return new ObjectiveGuide { Cost = report, Item = first.Item, Target = o.Target };
        }
        // Solo faltan créditos: se ganan vendiendo si hay dónde, y si no, cortando chatarra.
        if (Machines.Values.Any(m => m.Planet == p.Planet && m.Def == "export_terminal"))
            return new ObjectiveGuide { Cost = report, Action = GuideAction.Sell, Machine = "export_terminal", Target = "machine:export_terminal" };
        return new ObjectiveGuide { Cost = report, Target = o.Target };
    }

    /// <summary>Coste que el objetivo exige por sí mismo, deducido de su estadística. Null si no pide nada que se pueda reunir.</summary>
    private (long credits, Dictionary<string, long> items)? ObjectiveCost(ObjectiveDef o, int planet)
    {
        var dot = o.Stat.IndexOf('.');
        if (dot <= 0) return null;
        var (kind, id) = (o.Stat[..dot], o.Stat[(dot + 1)..]);
        switch (kind)
        {
            case "built" when Content.Machines.TryGetValue(id, out var def):
                return (def.Credits, def.Cost);
            case "upgrade" when Content.Upgrades.TryGetValue(id, out var u):
                return (u.Credits, u.Cost);
            case "contract" when Content.Contracts.TryGetValue(id, out var c):
                return (0, c.Items);
            case "produced":
                // Producir algo sin tener la máquina es primero construirla.
                var producer = Content.Machines.Values.FirstOrDefault(d => d.Recipe != null && Content.Recipes[d.Recipe].Outputs.ContainsKey(id));
                if (producer == null || Machines.Values.Any(m => m.Planet == planet && m.Def == producer.Id)) return null;
                return (producer.Credits, producer.Cost);
            default:
                return null;
        }
    }

    private sealed record GuideStep(GuideAction Action, string Item, string Machine, string Target);

    /// <summary>Sigue la cadena hacia atrás: recoger lo ya hecho, esperar o alimentar la máquina, y por último cortar el escombro de origen.</summary>
    private GuideStep? TraceSource(PlayerState p, string item, int depth)
    {
        if (depth > GuideDepth) return null;
        var producers = _machineList.Where(m => m.Planet == p.Planet && Content.Machines[m.Def].Kind == MachineKind.Production).ToList();
        foreach (var m in producers)
            if (m.Out.Get(item) > 0)
                return new GuideStep(GuideAction.Collect, item, m.Def, "machine:" + m.Def);

        foreach (var m in producers)
        {
            var recipe = Content.Recipes[Content.Machines[m.Def].Recipe!];
            if (!recipe.Outputs.ContainsKey(item)) continue;
            var fed = recipe.Inputs.All(kv => m.In.Get(kv.Key) >= kv.Value) || _linkList.Any(l => l.To == m.Id);
            if (fed) return new GuideStep(GuideAction.Wait, item, m.Def, "machine:" + m.Def);
            foreach (var (input, qty) in recipe.Inputs)
            {
                if (m.In.Get(input) >= qty) continue;
                if (p.Bag.Get(input) > 0) return new GuideStep(GuideAction.Deposit, input, m.Def, "machine:" + m.Def);
                return TraceSource(p, input, depth + 1) ?? new GuideStep(GuideAction.Deposit, input, m.Def, "machine:" + m.Def);
            }
        }

        foreach (var (debris, def) in Content.Balance.Debris)
            if (def.Drop == item && DebrisById.Values.Any(d => d.Planet == p.Planet && d.Def == debris))
                return new GuideStep(GuideAction.Cut, item, "", "debris:" + debris);
        return null;
    }
}
