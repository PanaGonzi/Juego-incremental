namespace Pluton.Simulation;

/// <summary>Geometría de vías. El estado guarda solo nodos, forma y longitud; la curva se evalúa al vuelo.</summary>
public static class RailMath
{
    public static (double x, double z) Control(RailNode a, RailNode b, byte shape) =>
        shape == 1 ? (b.X, a.Z) : (a.X, b.Z);

    /// <summary>Posición en unidades de celda para t en [0,1].</summary>
    public static (double x, double z) Eval(RailNode a, RailNode b, byte shape, double t)
    {
        if (shape == 0) return (a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
        var (cx, cz) = Control(a, b, shape);
        var u = 1 - t;
        return (u * u * a.X + 2 * u * t * cx + t * t * b.X, u * u * a.Z + 2 * u * t * cz + t * t * b.Z);
    }

    public static long Length(RailNode a, RailNode b, byte shape, int cellMm)
    {
        const int steps = 24;
        double total = 0;
        var (px, pz) = Eval(a, b, shape, 0);
        for (var i = 1; i <= steps; i++)
        {
            var (x, z) = Eval(a, b, shape, i / (double)steps);
            total += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
            px = x;
            pz = z;
        }
        return (long)Math.Round(total * cellMm);
    }

    /// <summary>Celdas que atraviesa un trazado; sirve para validar contra máquinas y escombros.</summary>
    public static HashSet<(int x, int z)> Cells(RailNode a, RailNode b, byte shape)
    {
        var cells = new HashSet<(int, int)>();
        var steps = (Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) + 1) * 8;
        for (var i = 0; i <= steps; i++)
        {
            var (x, z) = Eval(a, b, shape, i / (double)steps);
            // Se excluyen los puntos justo en una esquina: tocan cuatro celdas sin ocuparlas.
            var fx = x - Math.Floor(x);
            var fz = z - Math.Floor(z);
            if ((fx < 0.02 || fx > 0.98) && (fz < 0.02 || fz > 0.98)) continue;
            cells.Add(((int)Math.Floor(x), (int)Math.Floor(z)));
        }
        return cells;
    }
}

public sealed partial class World
{
    public readonly SortedDictionary<int, RailNode> RailNodes = new();
    public readonly SortedDictionary<int, RailSegment> RailSegments = new();
    public readonly SortedDictionary<int, Train> Trains = new();

    /// <summary>Máquinas cambiadas por la simulación, sin acción de jugador: se replican a menor frecuencia.</summary>
    public readonly HashSet<int> DirtySimMachines = new();
    public readonly HashSet<int> DirtyRailNodes = new();
    public readonly HashSet<int> DirtyRailSegments = new();
    public readonly HashSet<int> DirtyTrains = new();
    public readonly HashSet<int> DirtySimTrains = new();
    public readonly HashSet<int> DirtyTrainPaths = new();
    public readonly List<int> RemovedRailNodes = new();
    public readonly List<int> RemovedRailSegments = new();
    public readonly List<int> RemovedTrains = new();

    private readonly Dictionary<(int planet, int x, int z), int> _railCells = new();
    private readonly Dictionary<(int planet, int x, int z), int> _nodeAt = new();
    private readonly Dictionary<int, List<RailSegment>> _adjacency = new();
    private Train[] _trainList = Array.Empty<Train>();
    private readonly Dictionary<int, int> _reservedSegments = new();

    private RailDef R => Content.Balance.Rail;

    public void ReindexRail()
    {
        _railCells.Clear();
        _nodeAt.Clear();
        _adjacency.Clear();
        foreach (var n in RailNodes.Values) _nodeAt[(n.Planet, n.X, n.Z)] = n.Id;
        foreach (var s in RailSegments.Values)
        {
            AddAdjacent(s);
            foreach (var c in RailMath.Cells(RailNodes[s.A], RailNodes[s.B], s.Shape))
                _railCells[(s.Planet, c.x, c.z)] = _railCells.GetValueOrDefault((s.Planet, c.x, c.z)) + 1;
        }
        _trainList = Trains.Values.ToArray();
        _reservedSegments.Clear();
        foreach (var t in _trainList)
        {
            if (t.State != TrainState.Moving) continue;
            for (var i = t.PathIndex; i < t.Path.Count; i++) _reservedSegments[t.Path[i]] = t.Id;
        }
    }

    private void ReleaseReservations(Train t)
    {
        foreach (var id in t.Path)
            if (_reservedSegments.TryGetValue(id, out var owner) && owner == t.Id) _reservedSegments.Remove(id);
    }

    private void AddAdjacent(RailSegment s)
    {
        if (!_adjacency.TryGetValue(s.A, out var a)) _adjacency[s.A] = a = new List<RailSegment>();
        a.Add(s);
        if (!_adjacency.TryGetValue(s.B, out var b)) _adjacency[s.B] = b = new List<RailSegment>();
        b.Add(s);
    }

    public bool RailBlocksCell(int planet, int x, int z) => _railCells.ContainsKey((planet, x, z));

    private RailNode NodeAt(int planet, int x, int z)
    {
        if (_nodeAt.TryGetValue((planet, x, z), out var id)) return RailNodes[id];
        var node = new RailNode { Id = NextId++, Planet = planet, X = x, Z = z };
        RailNodes[node.Id] = node;
        _nodeAt[(planet, x, z)] = node.Id;
        DirtyRailNodes.Add(node.Id);
        return node;
    }

    private RailSegment AddSegment(int planet, RailNode a, RailNode b, byte shape, int ownerStation)
    {
        var s = new RailSegment
        {
            Id = NextId++,
            Planet = planet,
            A = a.Id,
            B = b.Id,
            Shape = shape,
            LengthMm = RailMath.Length(a, b, shape, B.CellSizeMm),
            OwnerStation = ownerStation,
        };
        RailSegments[s.Id] = s;
        AddAdjacent(s);
        foreach (var c in RailMath.Cells(a, b, shape))
            _railCells[(planet, c.x, c.z)] = _railCells.GetValueOrDefault((planet, c.x, c.z)) + 1;
        DirtyRailSegments.Add(s.Id);
        return s;
    }

    private void RemoveSegment(RailSegment s)
    {
        RailSegments.Remove(s.Id);
        _reservedSegments.Remove(s.Id);
        DirtyRailSegments.Remove(s.Id);
        RemovedRailSegments.Add(s.Id);
        _adjacency[s.A].Remove(s);
        _adjacency[s.B].Remove(s);
        foreach (var c in RailMath.Cells(RailNodes[s.A], RailNodes[s.B], s.Shape))
        {
            var key = (s.Planet, c.x, c.z);
            var left = _railCells.GetValueOrDefault(key) - 1;
            if (left <= 0) _railCells.Remove(key);
            else _railCells[key] = left;
        }
        DropNodeIfUnused(s.A);
        DropNodeIfUnused(s.B);
    }

    private void DropNodeIfUnused(int id)
    {
        if (_adjacency.TryGetValue(id, out var list) && list.Count > 0) return;
        foreach (var t in _trainList)
            if (t.CurrentNode == id) return;
        if (!RailNodes.Remove(id, out var node)) return;
        _nodeAt.Remove((node.Planet, node.X, node.Z));
        _adjacency.Remove(id);
        DirtyRailNodes.Remove(id);
        RemovedRailNodes.Add(id);
    }

    /// <summary>Validación común: la previsualización del cliente usa exactamente la misma regla.</summary>
    public string? CheckRail(int planet, int x1, int z1, int x2, int z2, byte shape, out long credits)
    {
        credits = 0;
        var dx = Math.Abs(x1 - x2);
        var dz = Math.Abs(z1 - z2);
        if (dx == 0 && dz == 0 || shape > 2) return "err_rail_shape";
        if (shape == 0)
        {
            if (!(dx == 0 || dz == 0 || dx == dz)) return "err_rail_shape";
            if (Math.Max(dx, dz) > R.MaxStraightCells) return "err_rail_length";
        }
        else
        {
            if (dx != dz) return "err_rail_shape";
            if (dx > R.MaxCurveCells) return "err_rail_length";
        }
        var half = Content.Planets.First(pl => pl.Id == planet).HalfSizeCells;
        if (Math.Abs(x1) >= half || Math.Abs(z1) >= half || Math.Abs(x2) >= half || Math.Abs(z2) >= half) return "err_bounds";

        var a = new RailNode { X = x1, Z = z1 };
        var b = new RailNode { X = x2, Z = z2 };
        var cells = RailMath.Cells(a, b, shape);
        foreach (var c in cells)
            if (_occupied.ContainsKey((planet, c.x, c.z)) || _debrisCells.ContainsKey((planet, c.x, c.z))) return "err_rail_blocked";
        foreach (var c in cells)
            if (_beltCells.ContainsKey((planet, c.x, c.z))) return "err_blocked";
        credits = cells.Count * R.CreditsPerCell;
        return null;
    }

    private CommandResult PlaceRail(PlayerState p, int x1, int z1, int x2, int z2, long shape)
    {
        if (shape is < 0 or > 2) return CommandResult.Fail("err_rail_shape");
        if (!Upgrades.Contains("tech_rail")) return CommandResult.Fail("err_locked");
        var error = CheckRail(p.Planet, x1, z1, x2, z2, (byte)shape, out var credits);
        if (error != null) return CommandResult.Fail(error);
        if (!InRange(p, x1 * (long)B.CellSizeMm, z1 * (long)B.CellSizeMm, B.BuildRangeMm) &&
            !InRange(p, x2 * (long)B.CellSizeMm, z2 * (long)B.CellSizeMm, B.BuildRangeMm)) return CommandResult.Fail("err_range");
        if (!Pay(p, credits, new Dictionary<string, long>())) return CommandResult.Fail("err_cost");

        var a = NodeAt(p.Planet, x1, z1);
        var b = NodeAt(p.Planet, x2, z2);
        foreach (var s in RailSegments.Values)
            if ((s.A == a.Id && s.B == b.Id || s.A == b.Id && s.B == a.Id) && s.Shape == shape)
            {
                AddCredits(credits);
                return CommandResult.Fail("err_link_exists");
            }
        AddSegment(p.Planet, a, b, (byte)shape, -1);
        Bump("built.rail");
        return CommandResult.Success;
    }

    private bool SegmentInUse(int segmentId)
    {
        foreach (var t in _trainList)
            if (t.State == TrainState.Moving && t.PathIndex < t.Path.Count && t.Path[t.PathIndex] == segmentId) return true;
        return false;
    }

    private CommandResult RemoveRail(PlayerState p, int id)
    {
        if (!RailSegments.TryGetValue(id, out var s) || s.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (s.OwnerStation >= 0) return CommandResult.Fail("err_builtin");
        if (SegmentInUse(id)) return CommandResult.Fail("err_rail_occupied");
        var a = RailNodes[s.A];
        if (!InRange(p, a.X * (long)B.CellSizeMm, a.Z * (long)B.CellSizeMm, B.BuildRangeMm + 40000)) return CommandResult.Fail("err_range");
        var refund = RailMath.Cells(a, RailNodes[s.B], s.Shape).Count * R.CreditsPerCell;
        RemoveSegment(s);
        AddCredits(refund);
        return CommandResult.Success;
    }

    /// <summary>Crea los nodos y el tramo propios de una estación recién colocada.</summary>
    private void AttachStation(Machine m)
    {
        var (a, b) = StationRailEnds(m.Def, m.X, m.Z, m.Rot);
        var na = NodeAt(m.Planet, a.x, a.z);
        var nb = NodeAt(m.Planet, b.x, b.z);
        m.RailA = na.Id;
        m.RailB = nb.Id;
        AddSegment(m.Planet, na, nb, 0, m.Id);
    }

    public (( int x, int z) a, (int x, int z) b) StationRailEnds(string def, int x, int z, int rot)
    {
        var d = Content.Machines[def];
        return rot % 2 == 0
            ? ((x, z + d.D), (x + d.W, z + d.D))
            : ((x + d.D, z), (x + d.D, z + d.W));
    }

    private CommandResult StationMode(PlayerState p, int id, bool unload)
    {
        if (!Machines.TryGetValue(id, out var m) || m.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (Content.Machines[m.Def].Kind is not (MachineKind.Station or MachineKind.CargoPad)) return CommandResult.Fail("err_invalid");
        m.StationUnload = unload;
        DirtyMachines.Add(id);
        return CommandResult.Success;
    }

    private CommandResult CreateTrain(PlayerState p, int stationA, int stationB)
    {
        if (stationA == stationB) return CommandResult.Fail("err_invalid");
        if (!Machines.TryGetValue(stationA, out var a) || !Machines.TryGetValue(stationB, out var b)) return CommandResult.Fail("err_gone");
        if (a.Planet != p.Planet || b.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (Content.Machines[a.Def].Kind != MachineKind.Station || Content.Machines[b.Def].Kind != MachineKind.Station) return CommandResult.Fail("err_invalid");
        if (!Pay(p, R.TrainCredits, R.TrainCost)) return CommandResult.Fail("err_cost");
        var t = new Train { Id = NextId++, Planet = p.Planet, StationA = stationA, StationB = stationB, CurrentNode = a.RailA };
        Trains[t.Id] = t;
        _trainList = Trains.Values.ToArray();
        DirtyTrains.Add(t.Id);
        DirtyTrainPaths.Add(t.Id);
        Bump("built.train");
        return CommandResult.Success;
    }

    private CommandResult RemoveTrain(PlayerState p, int id)
    {
        if (!Trains.TryGetValue(id, out var t) || t.Planet != p.Planet) return CommandResult.Fail("err_gone");
        var depot = DepotOf(t.Planet);
        if (depot == null) return CommandResult.Fail("err_invalid");
        foreach (var kv in t.Cargo.Items) depot.In.Add(kv.Key, kv.Value);
        foreach (var (item, qty) in R.TrainCost) depot.In.Add(item, qty * B.RefundPermille / 1000);
        AddCredits(R.TrainCredits * B.RefundPermille / 1000);
        DirtyMachines.Add(depot.Id);
        Trains.Remove(id);
        _trainList = Trains.Values.ToArray();
        DirtyTrains.Remove(id);
        DirtySimTrains.Remove(id);
        DirtyTrainPaths.Remove(id);
        RemovedTrains.Add(id);
        return CommandResult.Success;
    }

    /// <summary>Camino más corto. Solo se recalcula al salir o si la ruta se corta, nunca cada tick.</summary>
    public List<int>? FindPath(int fromNode, int station)
    {
        if (!Machines.TryGetValue(station, out var st)) return null;
        var dist = new Dictionary<int, long> { [fromNode] = 0 };
        var via = new Dictionary<int, RailSegment>();
        var queue = new PriorityQueue<int, long>();
        queue.Enqueue(fromNode, 0);
        var target = -1;
        while (queue.TryDequeue(out var node, out var d))
        {
            if (d > dist[node]) continue;
            if (node == st.RailA || node == st.RailB) { target = node; break; }
            if (!_adjacency.TryGetValue(node, out var list)) continue;
            foreach (var s in list)
            {
                var other = s.A == node ? s.B : s.A;
                var nd = d + s.LengthMm;
                if (dist.TryGetValue(other, out var old) && old <= nd) continue;
                dist[other] = nd;
                via[other] = s;
                queue.Enqueue(other, nd);
            }
        }
        if (target < 0) return null;
        var path = new List<int>();
        for (var n = target; n != fromNode;)
        {
            var s = via[n];
            path.Add(s.Id);
            n = s.A == n ? s.B : s.A;
        }
        path.Reverse();
        return path;
    }

    private void SetWait(Train t, string reason)
    {
        if (t.WaitReason == reason) return;
        t.WaitReason = reason;
        DirtyTrains.Add(t.Id);
    }

    private void Depart(Train t)
    {
        var target = t.HeadingToB ? t.StationB : t.StationA;
        var path = FindPath(t.CurrentNode, target);
        if (path == null)
        {
            if (t.State != TrainState.Waiting)
            {
                t.State = TrainState.Waiting;
                DirtyTrains.Add(t.Id);
            }
            SetWait(t, "wait_no_route");
            t.Timer = 10;
            return;
        }
        // Se reserva el trayecto completo o no se sale: nadie espera con vía retenida, así que no hay interbloqueos.
        foreach (var id in path)
        {
            if (!_reservedSegments.TryGetValue(id, out var owner) || owner == t.Id) continue;
            if (t.State != TrainState.Waiting)
            {
                t.State = TrainState.Waiting;
                DirtyTrains.Add(t.Id);
            }
            SetWait(t, "wait_block");
            t.Timer = 5;
            return;
        }
        ReleaseReservations(t);
        foreach (var id in path) _reservedSegments[id] = t.Id;
        t.Path.Clear();
        t.Path.AddRange(path);
        DirtyTrainPaths.Add(t.Id);
        t.PathIndex = 0;
        t.PosMm = 0;
        t.State = TrainState.Moving;
        t.WaitReason = "";
        t.Timer = 0;
        DirtyTrains.Add(t.Id);
        if (path.Count == 0) Arrive(t);
        else EnterSegment(t);
    }

    private void EnterSegment(Train t)
    {
        if (!RailSegments.TryGetValue(t.Path[t.PathIndex], out var s)) return;
        t.Forward = s.A == t.CurrentNode;
    }

    private void Arrive(Train t)
    {
        var station = Machines[t.HeadingToB ? t.StationB : t.StationA];
        t.HeadingToB = !t.HeadingToB;
        t.State = station.StationUnload ? TrainState.Unloading : TrainState.Loading;
        t.Timer = 0;
        t.WaitReason = "";
        DirtyTrains.Add(t.Id);
    }

    /// <summary>La estación donde está parado: la del tramo anterior a invertir el destino.</summary>
    private Machine? StationAt(Train t)
    {
        // Tras llegar, HeadingToB ya apunta al siguiente destino; el tren está en el otro extremo.
        var id = t.HeadingToB ? t.StationA : t.StationB;
        return Machines.TryGetValue(id, out var m) ? m : null;
    }

    private void StepTrains()
    {
        foreach (var t in _trainList)
        {
            switch (t.State)
            {
                case TrainState.Moving: StepMoving(t); break;
                case TrainState.Loading: StepLoading(t); break;
                case TrainState.Unloading: StepUnloading(t); break;
                case TrainState.Waiting:
                    t.Timer -= TimeScale;
                    if (t.Timer <= 0) Depart(t);
                    break;
            }
        }
    }

    private void StepMoving(Train t)
    {
        t.PosMm += R.TrainSpeedMmPerTick * TimeScale;
        if (Tick % 20 == 0 || TimeScale > 1) DirtySimTrains.Add(t.Id);
        while (t.State == TrainState.Moving)
        {
            if (!RailSegments.TryGetValue(t.Path[t.PathIndex], out var s))
            {
                // Tramo retirado por delante: se detiene en el nodo y lo explica.
                t.PosMm = 0;
                t.State = TrainState.Waiting;
                t.Timer = 10;
                ReleaseReservations(t);
                SetWait(t, "wait_no_route");
                DirtyTrains.Add(t.Id);
                return;
            }
            if (t.PosMm < s.LengthMm) return;
            t.PosMm -= s.LengthMm;
            if (_reservedSegments.TryGetValue(s.Id, out var holder) && holder == t.Id) _reservedSegments.Remove(s.Id);
            t.CurrentNode = t.Forward ? s.B : s.A;
            t.PathIndex++;
            DirtyTrains.Add(t.Id);
            if (t.PathIndex >= t.Path.Count)
            {
                t.PosMm = 0;
                Arrive(t);
                return;
            }
            EnterSegment(t);
        }
    }

    private void StepLoading(Train t)
    {
        var station = StationAt(t);
        if (station == null) return;
        if (station.StationUnload)
        {
            t.State = TrainState.Unloading;
            DirtyTrains.Add(t.Id);
            return;
        }
        var budget = Math.Min(R.TransferUnitsPerTick * TimeScale, R.TrainCapacity - t.Cargo.Total);
        foreach (var kv in station.In.Items.ToList())
        {
            if (budget <= 0) break;
            var qty = Math.Min(budget, kv.Value);
            station.In.TryRemove(kv.Key, qty);
            t.Cargo.Add(kv.Key, qty);
            budget -= qty;
            DirtySimMachines.Add(station.Id);
            DirtySimTrains.Add(t.Id);
        }
        if (t.Cargo.Total >= R.TrainCapacity) { Depart(t); return; }
        if (t.Cargo.Total == 0)
        {
            SetWait(t, "wait_no_cargo");
            return;
        }
        SetWait(t, "");
        t.Timer += TimeScale;
        if (t.Timer >= R.MaxLoadWaitTicks) Depart(t);
    }

    private void StepUnloading(Train t)
    {
        var station = StationAt(t);
        if (station == null) return;
        if (!station.StationUnload)
        {
            // El modo cambió mientras estaba parado: pasa a cargar con lo que lleva.
            t.State = TrainState.Loading;
            DirtyTrains.Add(t.Id);
            return;
        }
        var budget = R.TransferUnitsPerTick * TimeScale;
        var moved = false;
        foreach (var kv in t.Cargo.Items.ToList())
        {
            if (budget <= 0) break;
            var qty = Math.Min(Math.Min(budget, kv.Value), InputRoom(station, kv.Key));
            if (qty <= 0) continue;
            t.Cargo.TryRemove(kv.Key, qty);
            station.In.Add(kv.Key, qty);
            budget -= qty;
            moved = true;
            Bump("train_delivered", qty);
        }
        if (moved)
        {
            DirtySimMachines.Add(station.Id);
            DirtySimTrains.Add(t.Id);
        }
        if (t.Cargo.Total == 0) { Depart(t); return; }
        SetWait(t, moved ? "" : "wait_station_full");
    }
}
