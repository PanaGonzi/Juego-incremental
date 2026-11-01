namespace Pluton.Simulation;

/// <summary>Ciclos completados en los últimos 60 s simulados, en cubetas de un segundo. Derivado: no se guarda.</summary>
public sealed class RateWindow
{
    public const int Seconds = 60;
    private readonly int[] _buckets = new int[Seconds];
    private readonly long _start;
    private long _second;
    private int _sum;

    public RateWindow(long second)
    {
        _start = second;
        _second = second;
    }

    private void Advance(long second)
    {
        if (second <= _second) return;
        var steps = Math.Min(second - _second, Seconds);
        for (var i = 1; i <= steps; i++)
        {
            var index = (int)((_second + i) % Seconds);
            _sum -= _buckets[index];
            _buckets[index] = 0;
        }
        _second = second;
    }

    public void Add(long second)
    {
        Advance(second);
        _buckets[(int)(_second % Seconds)]++;
        _sum++;
    }

    public (int cycles, int seconds) Read(long second)
    {
        Advance(second);
        return (_sum, (int)Math.Min(Seconds, _second - _start));
    }
}

/// <summary>Causa del eslabón que limita una cadena. Se traduce en la interfaz.</summary>
public enum ChainCause : byte { None, NoPower, LowPower, Overheated, NoMaterial, OutputFull, BeltSaturated, LowestCapacity }

/// <summary>Eslabón limitante: una máquina o, si la causa es la cinta, también esa cinta (si no, -1).</summary>
public sealed record ChainLimit(ChainCause Cause, int Machine, int Link, string Item)
{
    public static readonly ChainLimit Nothing = new(ChainCause.None, -1, -1, "");
}

/// <summary>Cadena de una máquina: lo que la alimenta y lo que depende de ella, hasta el primer almacén en cada sentido.</summary>
public sealed record ChainTrace(int Seed, IReadOnlyList<int> Machines, IReadOnlyList<int> Links, ChainLimit Limit);

/// <summary>Lecturas de gestión de la fábrica: producción real medida por máquina y seguimiento de cadenas.</summary>
public sealed partial class World
{
    private const int MinRateSeconds = 5;
    private const int MaxChainMachines = 512;

    /// <summary>Última medición por máquina. En el servidor se calcula; en el cliente llega por MsgType.Rates. Fuera de guardados y firmas.</summary>
    public readonly Dictionary<int, (int cycles, int seconds)> MeasuredRates = new();

    private long CurrentSecond => Tick / B.TicksPerSecond;

    /// <summary>Segundos por ciclo con las mejoras y el rendimiento del yacimiento, sin contar falta de energía.</summary>
    private double CycleSeconds(Machine m, RecipeDef recipe)
    {
        var yieldPermille = m.NodeId >= 0 && Nodes.TryGetValue(m.NodeId, out var node) ? node.YieldPermille : 1000;
        var speed = (1000 + Mod("speed." + recipe.Id)) * yieldPermille / 1000;
        return recipe.Ticks * 1000.0 / Math.Max(1, speed) / B.TicksPerSecond;
    }

    private void RecordCycle(Machine m) => (m.Window ??= new RateWindow(CurrentSecond)).Add(CurrentSecond);

    /// <summary>Solo servidor, una vez por segundo: actualiza las mediciones y deja en changes las que han cambiado.</summary>
    public void CollectRateChanges(List<(int id, int cycles, int seconds)> changes)
    {
        changes.Clear();
        var second = CurrentSecond;
        var producers = 0;
        foreach (var m in _machineList)
        {
            if (Content.Machines[m.Def].Kind != MachineKind.Production) continue;
            producers++;
            var read = (m.Window ??= new RateWindow(second)).Read(second);
            if (MeasuredRates.TryGetValue(m.Id, out var known) && known == read) continue;
            MeasuredRates[m.Id] = read;
            changes.Add((m.Id, read.cycles, read.seconds));
        }
        if (MeasuredRates.Count > producers)
            foreach (var id in MeasuredRates.Keys.Where(id => !Machines.ContainsKey(id)).ToList()) MeasuredRates.Remove(id);
    }

    /// <summary>Unidades por minuto de un objeto en esta máquina, medidas; null mientras no hay ventana suficiente.</summary>
    public double? MeasuredPerMinute(Machine m, string item)
    {
        if (!MeasuredRates.TryGetValue(m.Id, out var rate) || rate.seconds < MinRateSeconds) return null;
        var recipe = Content.Machines[m.Def].Recipe;
        if (recipe == null || !Content.Recipes[recipe].Outputs.TryGetValue(item, out var qty)) return null;
        return rate.cycles * qty * 60.0 / rate.seconds;
    }

    /// <summary>Fracción del ciclo en curso según el último estado conocido.</summary>
    public double ProgressOf(Machine m)
    {
        var recipe = Content.Machines[m.Def].Recipe;
        if (recipe == null || !m.Processing) return 0;
        return Math.Clamp(m.Progress / (Content.Recipes[recipe].Ticks * 1000.0), 0, 1);
    }

    // ---------- Seguimiento de cadenas ----------

    /// <summary>
    /// Recorre la cadena de una máquina y señala qué la limita. Función pura de datos replicados: da lo mismo en servidor y cliente.
    /// Primero sigue las paradas reales: de lo que falta hacia atrás y de lo que no sale hacia delante. Quien trabaja mientras otro espera
    /// es el limitante. Si todo trabaja, compara capacidades teóricas en proporción a la receta. Reglas en D-041.
    /// </summary>
    public ChainTrace? TraceChain(int machineId)
    {
        if (!Machines.TryGetValue(machineId, out var seed)) return null;
        var (machines, links) = CollectChain(seed);
        var limit = FindLimit(seed, machines) ?? CapacityLimit(seed, machines) ?? ChainLimit.Nothing;
        return new ChainTrace(machineId, machines, links, limit);
    }

    /// <summary>Un almacén desacopla: se incluye, pero no se sigue más allá; si no, la cadena sería toda la base.</summary>
    private (List<int> machines, List<int> links) CollectChain(Machine seed)
    {
        var order = new List<int> { seed.Id };
        var machines = new HashSet<int> { seed.Id };
        var links = new SortedSet<int>();
        var pending = new Queue<(int id, bool down)>();
        pending.Enqueue((seed.Id, true));
        var visited = new HashSet<(int, bool)>();
        while (pending.Count > 0 && order.Count < MaxChainMachines)
        {
            var (id, down) = pending.Dequeue();
            if (!visited.Add((id, down))) continue;
            if (down && _beltsOut.TryGetValue(id, out var outs))
                foreach (var l in outs) Follow(l, l.To, true);
            // Lo que alimenta a un consumidor de la cadena también la limita: se sube desde todo lo alcanzado hacia abajo.
            if (_beltsIn.TryGetValue(id, out var ins))
                foreach (var l in ins) Follow(l, l.From, false);
        }
        return (order, links.ToList());

        void Follow(Link l, int next, bool downstream)
        {
            if (!Machines.TryGetValue(next, out var m)) return;
            links.Add(l.Id);
            if (machines.Add(next)) order.Add(next);
            if (AnySide(Content.Machines[m.Def].Kind)) return;
            pending.Enqueue((next, downstream));
        }
    }

    private ChainLimit? FindLimit(Machine seed, List<int> machines)
    {
        var soft = DiagnoseStop(seed);
        if (soft != null && !IsSoft(soft)) return soft;
        foreach (var id in machines)
        {
            if (id == seed.Id) continue;
            var found = DiagnoseStop(Machines[id]);
            if (found == null) continue;
            if (!IsSoft(found)) return found;
            soft ??= found;
        }
        return soft;
    }

    /// <summary>Blando: nadie está averiado o aislado; es lo más lento de lo que está en marcha.</summary>
    private static bool IsSoft(ChainLimit l) => l.Cause is ChainCause.LowestCapacity or ChainCause.BeltSaturated;

    private ChainLimit? DiagnoseStop(Machine m)
    {
        var def = Content.Machines[m.Def];
        if (def.Kind != MachineKind.Production) return null;
        var seen = new HashSet<int>();
        switch (m.Status)
        {
            case MachineStatus.NoPower: return new ChainLimit(ChainCause.NoPower, m.Id, -1, "");
            case MachineStatus.LowPower: return new ChainLimit(ChainCause.LowPower, m.Id, -1, "");
            case MachineStatus.Overheated: return new ChainLimit(ChainCause.Overheated, m.Id, -1, "");
            case MachineStatus.NoInput: return WalkUp(m, MissingInput(m), seen);
            case MachineStatus.OutputFull: return WalkDown(m, BlockedOutput(m), seen);
            default: return null;
        }
    }

    private string MissingInput(Machine m)
    {
        foreach (var (item, qty) in Content.Recipes[Content.Machines[m.Def].Recipe!].Inputs)
            if (m.In.Get(item) < qty) return item;
        return "";
    }

    private string BlockedOutput(Machine m)
    {
        foreach (var (item, qty) in Content.Recipes[Content.Machines[m.Def].Recipe!].Outputs)
            if (m.Out.Get(item) + qty > B.OutputBuffer) return item;
        return "";
    }

    /// <summary>Quién podría aportar un objeto a una máquina, atravesando divisores y unificadores. La cinta es la que sale de esa fuente.</summary>
    private void SourcesOf(Machine to, string item, List<(Machine source, Link link)> found, HashSet<int> seen)
    {
        if (!_beltsIn.TryGetValue(to.Id, out var ins)) return;
        foreach (var l in ins)
        {
            if (!Machines.TryGetValue(l.From, out var a) || !seen.Add(l.Id)) continue;
            var def = Content.Machines[a.Def];
            if (IsJunction(def.Kind)) SourcesOf(a, item, found, seen);
            else if (AnySide(def.Kind) || def.Kind == MachineKind.Production && Content.Recipes[def.Recipe!].Outputs.ContainsKey(item)) found.Add((a, l));
        }
    }

    /// <summary>Quién podría recibir un objeto de una máquina, atravesando divisores y unificadores. La cinta es la que entra en ese destino.</summary>
    private void SinksOf(Machine from, string item, List<(Machine sink, Link link)> found, HashSet<int> seen)
    {
        if (!_beltsOut.TryGetValue(from.Id, out var outs)) return;
        foreach (var l in outs)
        {
            if (!Machines.TryGetValue(l.To, out var b) || !seen.Add(l.Id)) continue;
            var def = Content.Machines[b.Def];
            if (IsJunction(def.Kind)) SinksOf(b, item, found, seen);
            else if (AnySide(def.Kind) || def.Kind == MachineKind.Production && Content.Recipes[def.Recipe!].Inputs.ContainsKey(item)) found.Add((b, l));
        }
    }

    private static ChainLimit? Stronger(ChainLimit? current, ChainLimit? next)
    {
        if (next == null) return current;
        return current == null || IsSoft(current) && !IsSoft(next) ? next : current;
    }

    /// <summary>Le falta un objeto: mira por qué no llega. Sin nadie que lo aporte, ella misma es el eslabón.</summary>
    private ChainLimit? WalkUp(Machine m, string item, HashSet<int> seen)
    {
        if (!seen.Add(m.Id)) return null;
        var sources = new List<(Machine source, Link link)>();
        SourcesOf(m, item, sources, new HashSet<int>());
        if (sources.Count == 0) return new ChainLimit(ChainCause.NoMaterial, m.Id, -1, item);
        ChainLimit? best = null;
        foreach (var (source, link) in sources)
            best = Stronger(best, WhyShort(source, link, item, seen));
        return best;
    }

    private ChainLimit? WhyShort(Machine source, Link link, string item, HashSet<int> seen)
    {
        if (AnySide(Content.Machines[source.Def].Kind))
        {
            if (source.In.Get(item) == 0) return new ChainLimit(ChainCause.NoMaterial, source.Id, -1, item);
            return Saturated(link) ? new ChainLimit(ChainCause.BeltSaturated, link.To, link.Id, item) : null;
        }
        switch (source.Status)
        {
            case MachineStatus.NoPower: return new ChainLimit(ChainCause.NoPower, source.Id, -1, "");
            case MachineStatus.LowPower: return new ChainLimit(ChainCause.LowPower, source.Id, -1, "");
            case MachineStatus.Overheated: return new ChainLimit(ChainCause.Overheated, source.Id, -1, "");
            case MachineStatus.NoInput: return WalkUp(source, MissingInput(source), seen);
            // Salida llena en origen y falta en destino: lo que hay entre medias no da abasto.
            case MachineStatus.OutputFull: return new ChainLimit(ChainCause.BeltSaturated, link.To, link.Id, item);
            case MachineStatus.Working: return new ChainLimit(ChainCause.LowestCapacity, source.Id, -1, item);
            default: return null;
        }
    }

    /// <summary>No puede sacar un objeto: mira qué lo retiene. Sin nadie que lo reciba, ella misma es el eslabón.</summary>
    private ChainLimit? WalkDown(Machine m, string item, HashSet<int> seen)
    {
        if (!seen.Add(m.Id)) return null;
        var sinks = new List<(Machine sink, Link link)>();
        SinksOf(m, item, sinks, new HashSet<int>());
        if (sinks.Count == 0) return new ChainLimit(ChainCause.OutputFull, m.Id, -1, item);
        ChainLimit? best = null;
        foreach (var (sink, link) in sinks)
            best = Stronger(best, WhyBlocked(sink, link, item, seen));
        return best;
    }

    private ChainLimit? WhyBlocked(Machine sink, Link link, string item, HashSet<int> seen)
    {
        var saturated = InputRoom(sink, item) > 0 && Saturated(link);
        if (AnySide(Content.Machines[sink.Def].Kind))
        {
            if (InputRoom(sink, item) <= 0) return new ChainLimit(ChainCause.OutputFull, sink.Id, -1, item);
            return saturated ? new ChainLimit(ChainCause.BeltSaturated, sink.Id, link.Id, item) : null;
        }
        switch (sink.Status)
        {
            case MachineStatus.NoPower: return new ChainLimit(ChainCause.NoPower, sink.Id, -1, "");
            case MachineStatus.LowPower: return new ChainLimit(ChainCause.LowPower, sink.Id, -1, "");
            case MachineStatus.Overheated: return new ChainLimit(ChainCause.Overheated, sink.Id, -1, "");
            case MachineStatus.OutputFull: return WalkDown(sink, BlockedOutput(sink), seen);
            case MachineStatus.NoInput:
                var missing = MissingInput(sink);
                if (missing != item) return WalkUp(sink, missing, seen);
                return saturated ? new ChainLimit(ChainCause.BeltSaturated, sink.Id, link.Id, item) : null;
            case MachineStatus.Working:
                return saturated ? new ChainLimit(ChainCause.BeltSaturated, sink.Id, link.Id, item) : new ChainLimit(ChainCause.LowestCapacity, sink.Id, -1, item);
            default: return null;
        }
    }

    /// <summary>Casi llena mientras su destino tiene hueco: va a todo lo que da de sí.</summary>
    private static bool Saturated(Link l) => l.Load * 10 >= l.Slots.Length * 8;

    /// <summary>
    /// Todo en marcha: reparte el ritmo de la máquina de partida por la cadena según las recetas y halla el eslabón con menos margen.
    /// Sin un único mínimo no hay cuello que señalar.
    /// </summary>
    private ChainLimit? CapacityLimit(Machine seed, List<int> machines)
    {
        if (Content.Machines[seed.Def].Kind != MachineKind.Production) return null;
        var perSeedCycle = new Dictionary<int, double> { [seed.Id] = 1 };
        var pending = new Queue<Machine>();
        pending.Enqueue(seed);
        var capacities = new List<(double perMinute, ChainLimit limit)>();
        while (pending.Count > 0)
        {
            var m = pending.Dequeue();
            var recipe = Content.Recipes[Content.Machines[m.Def].Recipe!];
            var share = perSeedCycle[m.Id];
            capacities.Add((60 / CycleSeconds(m, recipe) / share, new ChainLimit(ChainCause.LowestCapacity, m.Id, -1, "")));
            foreach (var (item, need) in recipe.Inputs)
            {
                var sources = new List<(Machine source, Link link)>();
                SourcesOf(m, item, sources, new HashSet<int>());
                var producers = sources.Where(s => Content.Machines[s.source.Def].Kind == MachineKind.Production).ToList();
                foreach (var (source, link) in producers)
                {
                    var made = Content.Recipes[Content.Machines[source.Def].Recipe!].Outputs[item];
                    Visit(source, share * need / (made * producers.Count), link, share * need / producers.Count, item);
                }
            }
            foreach (var (item, made) in recipe.Outputs)
            {
                var sinks = new List<(Machine sink, Link link)>();
                SinksOf(m, item, sinks, new HashSet<int>());
                var consumers = sinks.Where(s => Content.Machines[s.sink.Def].Kind == MachineKind.Production).ToList();
                foreach (var (sink, link) in consumers)
                {
                    var need = Content.Recipes[Content.Machines[sink.Def].Recipe!].Inputs[item];
                    Visit(sink, share * made / (need * consumers.Count), link, share * made / consumers.Count, item);
                }
            }

            void Visit(Machine next, double nextShare, Link link, double itemsPerCycle, string item)
            {
                capacities.Add((BeltItemsPerMinute() / itemsPerCycle, new ChainLimit(ChainCause.BeltSaturated, link.To, link.Id, item)));
                if (perSeedCycle.TryAdd(next.Id, nextShare)) pending.Enqueue(next);
            }
        }
        if (perSeedCycle.Count < 2) return null;
        capacities.Sort((x, y) => x.perMinute.CompareTo(y.perMinute));
        var least = capacities[0];
        // Cada cinta se evalúa desde los dos extremos: repetir la misma cinta o máquina no es un empate.
        var second = capacities.FirstOrDefault(c => c.limit != least.limit);
        if (second.limit != null && second.perMinute <= least.perMinute * 1.01) return null;
        return least.limit;
    }
}
