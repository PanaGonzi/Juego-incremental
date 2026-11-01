namespace Pluton.Simulation;

/// <summary>Estado legible de una cinta para la interfaz.</summary>
public enum BeltStatus : byte { Flowing, Empty, BlockedFull, BlockedNoPower, SourceIdle, SourceNoPower }

/// <summary>Salida de la cuadrícula junto a una cara de máquina. Dir es la dirección que sale de la máquina.</summary>
public readonly record struct BeltPort(int X, int Z, int Dir, bool Output, bool Side, bool Free);

/// <summary>Resultado del trazado: celdas ordenadas y coste, o clave del error.</summary>
public sealed record BeltRoute(IReadOnlyList<(int x, int z)> Cells, string? Error, long Credits);

/// <summary>Datos de lectura de una cinta. Función pura de World: no cambia nada ni depende de la vista.</summary>
public sealed record BeltInfo(
    int Id, int From, int To, BeltStatus Status, int Load, int Capacity, int ItemsPerMinute,
    IReadOnlyList<KeyValuePair<string, int>> Items, bool Elevated, int CellCount);

/// <summary>
/// Cintas abiertas: un Link con ruta en la cuadrícula y carga por huecos. Reglas en docs/DECISIONS.md (D-030).
/// Todo lo que se valida aquí se usa igual en el servidor y en la previsualización del cliente.
/// </summary>
public sealed partial class World
{
    /// <summary>Norte (-Z), este (+X), sur (+Z), oeste (-X). Coincide con girar la malla 90° por paso de Rot.</summary>
    public static readonly (int dx, int dz)[] BeltDirs = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    private readonly Dictionary<(int planet, int x, int z), (int a, int b)> _beltCells = new();
    private readonly HashSet<(int planet, int x, int z)> _nodeCells = new();
    private readonly Dictionary<int, Link[]> _beltsOut = new();

    /// <summary>Huecos cambiados por la simulación: se replican en el ciclo lento.</summary>
    public readonly HashSet<int> DirtySimSlots = new();
    /// <summary>Huecos listos para el siguiente delta.</summary>
    public readonly HashSet<int> DirtySlots = new();

    public static int FrontDir(int rot) => (4 - rot % 4) % 4;

    /// <summary>Solo los almacenes y estaciones sacan y reciben por cualquier cara.</summary>
    private static bool AnySide(MachineKind kind) => kind is MachineKind.Storage or MachineKind.Terminal or MachineKind.Station or MachineKind.CargoPad;

    public bool CanSendByBelt(MachineDef def) =>
        AnySide(def.Kind) || def.Kind == MachineKind.Production && Content.Recipes[def.Recipe!].Outputs.Count > 0;

    public bool CanReceiveByBelt(MachineDef def) =>
        AnySide(def.Kind) || def.Kind == MachineKind.Production && Content.Recipes[def.Recipe!].Inputs.Count > 0;

    /// <summary>
    /// Puertos de una colocación. Producción: salida al frente; entrada detrás y, con menos prioridad, por los lados
    /// (una fundición de dos celdas de ancho no recibiría cuatro cintas solo por detrás). Ver D-031.
    /// </summary>
    public IEnumerable<BeltPort> BeltPorts(int planet, string defId, int x, int z, int rot, bool output)
    {
        var def = Content.Machines[defId];
        if (output ? !CanSendByBelt(def) : !CanReceiveByBelt(def)) yield break;
        var (w, depth) = rot % 2 == 0 ? (def.W, def.D) : (def.D, def.W);
        var front = FrontDir(rot);
        for (var dir = 0; dir < 4; dir++)
        {
            var side = false;
            if (!AnySide(def.Kind))
            {
                if (output && dir != front) continue;
                if (!output && dir == front) continue;
                side = !output && dir != (front + 2) % 4;
            }
            var count = dir % 2 == 0 ? w : depth;
            for (var i = 0; i < count; i++)
            {
                var (px, pz) = dir switch
                {
                    0 => (x + i, z - 1),
                    1 => (x + w, z + i),
                    2 => (x + i, z + depth),
                    _ => (x - 1, z + i),
                };
                yield return new BeltPort(px, pz, dir, output, side, BeltCellFree(planet, px, pz));
            }
        }
    }

    public IEnumerable<BeltPort> BeltPorts(Machine m, bool output) => BeltPorts(m.Planet, m.Def, m.X, m.Z, m.Rot, output);

    internal bool GroundBlocked(int planet, int x, int z, int half) =>
        Math.Abs(x) >= half || Math.Abs(z) >= half ||
        _occupied.ContainsKey((planet, x, z)) || _debrisCells.ContainsKey((planet, x, z)) ||
        _railCells.ContainsKey((planet, x, z)) || _nodeCells.Contains((planet, x, z));

    private bool BeltCellFree(int planet, int x, int z) =>
        !_beltCells.ContainsKey((planet, x, z)) && !GroundBlocked(planet, x, z, Content.Planets.First(p => p.Id == planet).HalfSizeCells);

    /// <summary>Máquina que ocupa una celda, o -1.</summary>
    public int MachineAt(int planet, int x, int z) => _occupied.TryGetValue((planet, x, z), out var id) ? id : -1;

    /// <summary>Cinta que ocupa una celda; si hay cruce, la de abajo. -1 si no hay ninguna.</summary>
    public int BeltAt(int planet, int x, int z) =>
        _beltCells.TryGetValue((planet, x, z), out var use) ? (use.a >= 0 ? use.a : use.b) : -1;

    /// <summary>Cinta que pasa por encima en un cruce. -1 si no hay cruce.</summary>
    public int BridgeAt(int planet, int x, int z) =>
        _beltCells.TryGetValue((planet, x, z), out var use) && use.a >= 0 && use.b >= 0 ? use.b : -1;

    /// <summary>En un cruce sube la cinta más nueva: la regla no depende del orden de llegada de los datos.</summary>
    public bool IsBridge(Link l, int cellIndex)
    {
        if (l.Elevated || cellIndex < 0 || cellIndex >= l.Cells.Count) return false;
        var (x, z) = l.Cells[cellIndex];
        return BridgeAt(l.Planet, x, z) == l.Id;
    }

    private bool BeltBlocks(int planet, int x, int z, int ignoreMachine)
    {
        if (!_beltCells.TryGetValue((planet, x, z), out var use)) return false;
        if (ignoreMachine < 0) return true;
        return !BeltTouches(use.a, ignoreMachine) || !BeltTouches(use.b, ignoreMachine);
    }

    private bool BeltTouches(int linkId, int machine) =>
        linkId < 0 || Links.TryGetValue(linkId, out var l) && (l.From == machine || l.To == machine);

    private void RegisterBelt(Link l)
    {
        if (l.Elevated) return;
        foreach (var (x, z) in l.Cells)
        {
            var key = (l.Planet, x, z);
            if (!_beltCells.TryGetValue(key, out var use)) _beltCells[key] = (l.Id, -1);
            else _beltCells[key] = use.a < l.Id ? (use.a, l.Id) : (l.Id, use.a);
        }
    }

    private void UnregisterBelt(Link l)
    {
        if (l.Elevated) return;
        foreach (var (x, z) in l.Cells)
        {
            var key = (l.Planet, x, z);
            if (!_beltCells.TryGetValue(key, out var use)) continue;
            var other = use.a == l.Id ? use.b : use.b == l.Id ? use.a : use.a;
            if (other < 0) _beltCells.Remove(key);
            else _beltCells[key] = (other, -1);
        }
    }

    /// <summary>Recalcula listas derivadas tras añadir, retirar o reemplazar cintas.</summary>
    private void RefreshBelts()
    {
        _linkList = Links.Values.ToArray();
        _beltsOut.Clear();
        foreach (var group in _linkList.GroupBy(l => l.From)) _beltsOut[group.Key] = group.ToArray();
    }

    private void ReindexBelts()
    {
        _beltCells.Clear();
        _nodeCells.Clear();
        foreach (var n in Nodes.Values)
            for (var dx = 0; dx < 2; dx++)
            for (var dz = 0; dz < 2; dz++)
                _nodeCells.Add((n.Planet, n.X + dx, n.Z + dz));
        foreach (var l in Links.Values) RegisterBelt(l);
        RefreshBelts();
    }

    /// <summary>Un cruce solo pasa sobre un tramo recto ajeno, perpendicular y que no sea un extremo de esa cinta.</summary>
    internal bool CanCrossOver(int linkId, int x, int z, int dir)
    {
        if (!Links.TryGetValue(linkId, out var other) || other.Elevated) return false;
        var cells = other.Cells;
        var index = -1;
        for (var i = 0; i < cells.Count; i++)
            if (cells[i] == (x, z)) { index = i; break; }
        if (index <= 0 || index >= cells.Count - 1) return false;
        var (ax, az) = cells[index - 1];
        var (bx, bz) = cells[index + 1];
        var alongX = az == z && bz == z;
        var alongZ = ax == x && bx == x;
        if (!alongX && !alongZ) return false;
        return alongX ? dir % 2 == 0 : dir % 2 == 1;
    }

    private int SlotCount(int cells) => Math.Max(1, cells) * B.BeltSlotsPerCell;

    /// <summary>Longitud de una cinta elevada, que va recta de una máquina a otra sobre soportes.</summary>
    private int ElevatedCells(Machine a, Machine b)
    {
        var (ax, az) = MachineCenterMm(a);
        var (bx, bz) = MachineCenterMm(b);
        var dx = (double)(ax - bx);
        var dz = (double)(az - bz);
        return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / B.CellSizeMm));
    }

    private void ApplyRoute(Link l, IReadOnlyList<(int x, int z)> cells)
    {
        l.Elevated = false;
        l.Cells.Clear();
        l.Cells.AddRange(cells);
        l.Slots = new string?[SlotCount(cells.Count)];
        l.Load = 0;
    }

    /// <summary>
    /// Crea una cinta sin coste ni validación de ruta: solo para mundos de medición y pruebas,
    /// donde las celdas se conocen de antemano. La partida normal pasa por el comando Link.
    /// </summary>
    public Link AddBeltDirect(Machine a, Machine b, IReadOnlyList<(int x, int z)> cells)
    {
        var l = new Link { Id = NextId++, From = a.Id, To = b.Id, Planet = a.Planet };
        ApplyRoute(l, cells);
        Links[l.Id] = l;
        RegisterBelt(l);
        RefreshBelts();
        return l;
    }

    /// <summary>Solo para mundos de medición: deja un objeto real en un hueco libre.</summary>
    public bool SeedBeltSlot(Link l, int slot, string item)
    {
        if (slot < 0 || slot >= l.Slots.Length || l.Slots[slot] != null) return false;
        l.Slots[slot] = item;
        l.Load++;
        return true;
    }

    /// <summary>
    /// Construye exactamente la ruta recibida o la rechaza sin consumir nada. El servidor no busca ni sustituye nada aquí:
    /// así el coste de la orden está acotado por su tamaño y lo construido coincide con lo previsualizado.
    /// </summary>
    private CommandResult AddLink(PlayerState p, int from, int to, IReadOnlyList<(int x, int z)>? cells)
    {
        if (from == to) return CommandResult.Fail("err_invalid");
        if (!Machines.TryGetValue(from, out var a) || !Machines.TryGetValue(to, out var b)) return CommandResult.Fail("err_gone");
        if (a.Planet != p.Planet || b.Planet != p.Planet) return CommandResult.Fail("err_gone");
        var error = CheckBeltRoute(from, to, cells);
        if (error != null) return CommandResult.Fail(error);
        var credits = cells!.Count * B.BeltCreditsPerCell;
        if (!Pay(p, credits, new Dictionary<string, long>())) return CommandResult.Fail("err_cost");

        var link = new Link { Id = NextId++, From = from, To = to, Planet = p.Planet, Paid = credits };
        ApplyRoute(link, cells);
        Links[link.Id] = link;
        RegisterBelt(link);
        RefreshBelts();
        DirtyLinks.Add(link.Id);
        Bump("linked");
        return CommandResult.Success;
    }

    /// <summary>Celda donde se podría fijar un punto de paso.</summary>
    public bool CellFreeForBelt(int planet, int x, int z) => BeltCellFree(planet, x, z);

    private CommandResult RemoveLink(PlayerState p, int id)
    {
        if (!Links.TryGetValue(id, out var link) || link.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (DepotOf(link.Planet) == null) return CommandResult.Fail("err_invalid");
        DismantleBelt(link);
        return CommandResult.Success;
    }

    /// <summary>Regla de retirada: la carga y lo pagado vuelven al depósito y al saldo, aunque superen su capacidad.</summary>
    private void DismantleBelt(Link l)
    {
        ReturnBeltLoad(l);
        AddCredits(l.Paid * B.RefundPermille / 1000);
        UnregisterBelt(l);
        Links.Remove(l.Id);
        DirtyLinks.Remove(l.Id);
        DirtySlots.Remove(l.Id);
        DirtySimSlots.Remove(l.Id);
        RemovedLinks.Add((l.Planet, l.Id));
        RefreshBelts();
    }

    private void ReturnBeltLoad(Link l)
    {
        if (l.Load == 0) return;
        var depot = DepotOf(l.Planet);
        if (depot == null) return;
        for (var i = 0; i < l.Slots.Length; i++)
        {
            if (l.Slots[i] is not { } item) continue;
            depot.In.Add(item, 1);
            l.Slots[i] = null;
        }
        l.Load = 0;
        if (_inStep) DirtySimMachines.Add(depot.Id);
        else DirtyMachines.Add(depot.Id);
    }

    /// <summary>
    /// Mover una máquina reencamina sus cintas: la carga vuelve al depósito y se paga la diferencia de longitud.
    /// Sin ruta o sin saldo, la cinta se retira con devolución; una elevada sigue elevada.
    /// </summary>
    private void RerouteBeltsOf(int machine)
    {
        var touching = _linkList.Where(l => l.From == machine || l.To == machine).ToList();
        foreach (var l in touching) UnregisterBelt(l);
        foreach (var l in touching)
        {
            ReturnBeltLoad(l);
            var route = RouteBelt(l.From, l.To);
            var refund = l.Paid * B.RefundPermille / 1000;
            if (route.Error == null && Credits + refund >= route.Credits)
            {
                AddCredits(refund);
                Credits -= route.Credits;
                ApplyRoute(l, route.Cells);
                l.Paid = route.Credits;
                RegisterBelt(l);
                DirtyLinks.Add(l.Id);
                DirtySlots.Remove(l.Id);
                DirtySimSlots.Remove(l.Id);
                continue;
            }
            if (l.Elevated)
            {
                l.Slots = new string?[SlotCount(ElevatedCells(Machines[l.From], Machines[l.To]))];
                l.Load = 0;
                DirtyLinks.Add(l.Id);
                DirtySlots.Remove(l.Id);
                DirtySimSlots.Remove(l.Id);
                continue;
            }
            // Ya sin celdas registradas: DismantleBelt no debe liberar las de otra cinta.
            l.Elevated = true;
            DismantleBelt(l);
        }
        RefreshBelts();
    }

    // ---------- Simulación ----------

    private void StepBelts()
    {
        var speed = TimeScale * (1 + Mod("link_rate"));
        var interval = B.LinkIntervalTicks;
        foreach (var l in _linkList)
        {
            l.Accum += speed;
            if (l.Accum < interval) continue;
            var hops = l.Accum / interval;
            l.Accum %= interval;
            for (var i = 0; i < hops; i++)
                if (!HopBelt(l)) break; // sin movimiento posible, los saltos restantes no cambian nada
        }
    }

    private bool HopBelt(Link l)
    {
        if (!Machines.TryGetValue(l.From, out var a) || !Machines.TryGetValue(l.To, out var b)) return false;
        var slots = l.Slots;
        var moved = false;
        var last = slots.Length - 1;
        if (slots[last] is { } head && InputRoom(b, head) > 0)
        {
            b.In.Add(head, 1);
            slots[last] = null;
            l.Load--;
            DirtySimMachines.Add(b.Id);
            moved = true;
        }
        if (l.Load > 0)
            for (var i = last - 1; i >= 0; i--)
            {
                if (slots[i] == null || slots[i + 1] != null) continue;
                slots[i + 1] = slots[i];
                slots[i] = null;
                moved = true;
            }
        if (slots[0] == null && TryLoadBelt(l, a, b)) moved = true;
        if (moved) DirtySimSlots.Add(l.Id);
        return moved;
    }

    private Inventory BeltSource(Machine a) => Content.Machines[a.Def].Kind == MachineKind.Production ? a.Out : a.In;

    private bool TryLoadBelt(Link l, Machine a, Machine b)
    {
        var source = BeltSource(a);
        if (source.Kinds == 0) return false;
        string? item;
        var outs = _beltsOut.GetValueOrDefault(a.Id);
        if (outs is { Length: > 1 })
        {
            // Reparto rotatorio: carga la cinta servida hace más tiempo de entre las que pueden cargar ahora.
            Link? turn = null;
            long newest = 0;
            foreach (var other in outs)
            {
                newest = Math.Max(newest, other.Turn);
                if (other.Slots[0] != null || !Machines.TryGetValue(other.To, out var otherTo) || PickForBelt(other, source, otherTo) == null) continue;
                if (turn == null || other.Turn < turn.Turn) turn = other;
            }
            if (turn != l) return false;
            item = PickForBelt(l, source, b);
            l.Turn = newest + 1;
        }
        else
        {
            item = PickForBelt(l, source, b);
        }
        if (item == null) return false;
        source.TryRemove(item, 1);
        l.Slots[0] = item;
        l.Load++;
        DirtySimMachines.Add(a.Id);
        return true;
    }

    /// <summary>
    /// Elige qué cargar. Primero lo que cabrá en destino contando lo que ya viaja: así un objeto de sobra no bloquea a otro
    /// que hace falta (una receta de dos entradas nunca se atasca). Si no hay nada, y la cinta solo puede llevar un tipo,
    /// sigue cargando para que se vea la acumulación. Nunca carga lo que el destino no acepta.
    /// </summary>
    private string? PickForBelt(Link l, Inventory source, Machine b)
    {
        string? only = null;
        var kinds = 0;
        foreach (var kv in source.Items)
        {
            var room = InputRoom(b, kv.Key);
            if (!AcceptsOnBelt(b, kv.Key)) continue;
            if (room - OnBelt(l, kv.Key) > 0) return kv.Key;
            only = kv.Key;
            kinds++;
        }
        if (kinds != 1 || !SingleLane(b)) return null;
        return BeltCarriesOnly(l, only!) ? only : null;
    }

    private bool AcceptsOnBelt(Machine b, string item)
    {
        var def = Content.Machines[b.Def];
        return AnySide(def.Kind) || def.Kind == MachineKind.Production && Content.Recipes[def.Recipe!].Inputs.ContainsKey(item);
    }

    private bool SingleLane(Machine b)
    {
        var def = Content.Machines[b.Def];
        return AnySide(def.Kind) || def.Kind == MachineKind.Production && Content.Recipes[def.Recipe!].Inputs.Count == 1;
    }

    private static long OnBelt(Link l, string item)
    {
        if (l.Load == 0) return 0;
        long count = 0;
        foreach (var s in l.Slots)
            if (s == item) count++;
        return count;
    }

    private static bool BeltCarriesOnly(Link l, string item)
    {
        if (l.Load == 0) return true;
        foreach (var s in l.Slots)
            if (s != null && s != item) return false;
        return true;
    }

    /// <summary>Objetos por minuto a la velocidad actual, con las mejoras aplicadas.</summary>
    public int BeltItemsPerMinute() => 60 * B.TicksPerSecond * (1 + Mod("link_rate")) / B.LinkIntervalTicks;

    /// <summary>Huecos que avanza un objeto por segundo; la vista lo usa para predecir entre actualizaciones.</summary>
    public float BeltHopsPerSecond() => B.TicksPerSecond * (1 + Mod("link_rate")) / (float)B.LinkIntervalTicks;

    /// <summary>Lo que muestra la interfaz al apuntar a una cinta.</summary>
    public BeltInfo? InspectBelt(int id)
    {
        if (!Links.TryGetValue(id, out var l)) return null;
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in l.Slots)
            if (s != null) counts[s] = counts.GetValueOrDefault(s) + 1;
        return new BeltInfo(l.Id, l.From, l.To, BeltStatusOf(l), l.Load, l.Slots.Length, BeltItemsPerMinute(), counts.ToList(), l.Elevated, l.Elevated ? 0 : l.Cells.Count);
    }

    public BeltStatus BeltStatusOf(Link l)
    {
        if (!Machines.TryGetValue(l.From, out var a) || !Machines.TryGetValue(l.To, out var b)) return BeltStatus.Empty;
        var head = l.Slots[^1];
        if (head != null && InputRoom(b, head) <= 0)
            return b.Status is MachineStatus.NoPower ? BeltStatus.BlockedNoPower : BeltStatus.BlockedFull;
        if (l.Load > 0) return BeltStatus.Flowing;
        var source = BeltSource(a);
        foreach (var kv in source.Items)
            if (AcceptsOnBelt(b, kv.Key) && InputRoom(b, kv.Key) > 0) return BeltStatus.Flowing;
        if (Content.Machines[a.Def].Kind != MachineKind.Production) return BeltStatus.Empty;
        return a.Status == MachineStatus.NoPower ? BeltStatus.SourceNoPower : BeltStatus.SourceIdle;
    }

    /// <summary>
    /// Migración desde v4: cada enlace pasa a cinta enrutada por orden de ID. Sin ruta posible queda elevada, recta sobre soportes,
    /// sin ocupar celdas: se conserva la conexión y el jugador puede desmontarla y volver a tenderla. No cobra nada.
    /// </summary>
    public void MigrateLinksToBelts()
    {
        foreach (var l in Links.Values.ToList())
        {
            if (!Machines.TryGetValue(l.From, out var a) || !Machines.TryGetValue(l.To, out var b) || a.Planet != b.Planet)
            {
                Links.Remove(l.Id);
                continue;
            }
            var route = RouteBelt(l.From, l.To);
            if (route.Error == null)
            {
                ApplyRoute(l, route.Cells);
                RegisterBelt(l);
            }
            else
            {
                l.Elevated = true;
                l.Cells.Clear();
                l.Slots = new string?[SlotCount(ElevatedCells(a, b))];
                l.Load = 0;
            }
        }
        RefreshBelts();
    }

    /// <summary>Número de objetos en todas las cintas; para pruebas y métricas.</summary>
    public long ItemsOnBelts()
    {
        long total = 0;
        foreach (var l in Links.Values) total += l.Load;
        return total;
    }
}
