namespace Pluton.Simulation;

/// <summary>Lo que muestra una señal. Se deriva solo de estado replicado, así que todos los clientes ven lo mismo.</summary>
public enum SignalLight : byte { Clear, Stop }

/// <summary>
/// Reservas de bloques, señales y desvíos.
///
/// Una señal divide la ruta: el tren reserva hasta la siguiente señal o hasta el destino, nunca más. Sin señales reserva el trayecto
/// completo, como las partidas anteriores. Solo reservan los trenes en marcha o detenidos ante una señal; uno parado en una estación no
/// retiene vía, y por eso dos trenes en sentidos opuestos entre dos estaciones nunca se bloquean.
///
/// Un tren detenido ante una señal retiene vía mientras espera, y eso sí podría cerrar un ciclo. Antes de conceder un bloque parcial se
/// comprueba que exista un orden en el que todos los trenes en ruta puedan llegar a su destino (<see cref="IsSafe"/>). Si no existe, no
/// se concede, y el tren espera sin haber entrado donde bloquearía a otro.
/// </summary>
public sealed partial class World
{
    private enum PlanResult { NoRoute, Blocked, Granted }

    /// <summary>Tramos y nodos reservados, con el tren que los retiene. Comparten el espacio de ids de `NextId`.</summary>
    private readonly Dictionary<int, int> _holds = new();

    /// <summary>Tramos que se cruzan sin compartir un nodo: dos trenes no pueden estar a la vez en ellos.</summary>
    private readonly Dictionary<int, List<int>> _crossings = new();
    private readonly Dictionary<(int planet, int x, int z), List<int>> _footprints = new();

    private void IndexCrossings(RailSegment s)
    {
        var list = new List<int>();
        _crossings[s.Id] = list;
        foreach (var point in RailMath.Footprint(RailNodes[s.A], RailNodes[s.B], s.Shape))
        {
            var key = (s.Planet, point.x, point.z);
            if (!_footprints.TryGetValue(key, out var there)) _footprints[key] = there = new List<int>();
            foreach (var other in there)
            {
                if (list.Contains(other)) continue;
                var o = RailSegments[other];
                if (o.A == s.A || o.A == s.B || o.B == s.A || o.B == s.B) continue;
                list.Add(other);
                _crossings[other].Add(s.Id);
            }
            there.Add(s.Id);
        }
    }

    private void UnindexCrossings(RailSegment s, RailNode a, RailNode b)
    {
        foreach (var point in RailMath.Footprint(a, b, s.Shape))
        {
            var key = (s.Planet, point.x, point.z);
            if (!_footprints.TryGetValue(key, out var there)) continue;
            there.Remove(s.Id);
            if (there.Count == 0) _footprints.Remove(key);
        }
        if (_crossings.Remove(s.Id, out var list))
            foreach (var other in list)
                if (_crossings.TryGetValue(other, out var back)) back.Remove(s.Id);
    }

    public IReadOnlyList<int> CrossingsOf(int segmentId) => _crossings.TryGetValue(segmentId, out var list) ? list : Array.Empty<int>();

    /// <summary>Quién retiene un tramo o nodo, o -1. Para pruebas y lecturas del servidor; en un cliente no se mantiene entre reindexados.</summary>
    public int HolderOf(int id) => _holds.GetValueOrDefault(id, -1);

    private int TargetOf(Train t) => t.HeadingToB ? t.StationB : t.StationA;

    private static bool OnTrack(TrainState s) => s is TrainState.Moving or TrainState.AtSignal;

    private int OtherEnd(RailSegment s, int node) => s.A == node ? s.B : s.A;

    /// <summary>Nodo al inicio del tramo `index` de la ruta. Devuelve -1 si la ruta se ha cortado por el camino.</summary>
    private int NodeOfIndex(Train t, int index)
    {
        var node = t.CurrentNode;
        for (var i = t.PathIndex; i < index; i++)
        {
            if (i >= t.Path.Count || !RailSegments.TryGetValue(t.Path[i], out var s)) return -1;
            node = OtherEnd(s, node);
        }
        for (var i = t.PathIndex - 1; i >= index; i--)
        {
            if (i >= t.Path.Count || !RailSegments.TryGetValue(t.Path[i], out var s)) return -1;
            node = OtherEnd(s, node);
        }
        return node;
    }

    /// <summary>Hasta qué índice de la ruta llega el primer bloque: la primera señal por delante del origen, o el final.</summary>
    private int BlockEnd(int from, List<int> route)
    {
        var node = from;
        for (var i = 0; i + 1 < route.Count; i++)
        {
            node = OtherEnd(RailSegments[route[i]], node);
            if (RailNodes[node].Signal) return i + 1;
        }
        return route.Count;
    }

    private bool HeldByOther(int id, int train) => _holds.TryGetValue(id, out var owner) && owner != train;

    private bool SegmentHeldByOther(int segment, int train)
    {
        if (HeldByOther(segment, train)) return true;
        if (_crossings.TryGetValue(segment, out var list))
            foreach (var other in list)
                if (HeldByOther(other, train)) return true;
        return false;
    }

    private bool BlockFree(Train t, int from, List<int> route, int end)
    {
        var node = from;
        for (var i = 0; i < end; i++)
        {
            if (HeldByOther(node, t.Id) || SegmentHeldByOther(route[i], t.Id)) return false;
            node = OtherEnd(RailSegments[route[i]], node);
        }
        // Un bloque que acaba en una señal no retiene su nodo: el tren se detiene antes de pisarlo.
        return end < route.Count || !HeldByOther(node, t.Id);
    }

    private void HoldRange(Train t, int from, int to)
    {
        var node = NodeOfIndex(t, from);
        if (node < 0) return;
        for (var i = from; i < to && i < t.Path.Count; i++)
        {
            if (!RailSegments.TryGetValue(t.Path[i], out var s)) return;
            _holds[node] = t.Id;
            _holds[s.Id] = t.Id;
            node = OtherEnd(s, node);
        }
        if (to >= t.Path.Count) _holds[node] = t.Id;
    }

    /// <summary>Reconstruye lo que retiene un tren a partir de su estado guardado: por delante hasta el fin de su bloque, y lo que aún ocupa su cola.</summary>
    private void RestoreHolds(Train t)
    {
        if (!OnTrack(t.State)) return;
        HoldRange(t, t.PathIndex, t.GrantEnd);
        var length = (long)R.TrainLengthMm;
        var dist = t.PosMm;
        var node = t.CurrentNode;
        for (var i = t.PathIndex; ; i--)
        {
            if (dist < length) _holds[node] = t.Id;
            if (i == 0 || i > t.Path.Count || !RailSegments.TryGetValue(t.Path[i - 1], out var s)) break;
            if (dist >= length) break;
            _holds[s.Id] = t.Id;
            dist += s.LengthMm;
            node = OtherEnd(s, node);
        }
    }

    private bool ReleaseIfMine(int id, Train t)
    {
        if (!_holds.TryGetValue(id, out var owner) || owner != t.Id) return false;
        _holds.Remove(id);
        return true;
    }

    /// <summary>Libera lo que la cola del tren ya ha dejado atrás: hasta entonces, ninguno entra en lo que aún ocupa.</summary>
    private void ReleaseBehind(Train t)
    {
        var length = (long)R.TrainLengthMm;
        var dist = t.PosMm;
        var node = t.CurrentNode;
        for (var i = t.PathIndex; ; i--)
        {
            if (dist >= length) ReleaseIfMine(node, t);
            if (i == 0 || !RailSegments.TryGetValue(t.Path[i - 1], out var s)) break;
            if (dist >= length && !ReleaseIfMine(s.Id, t)) break;
            dist += s.LengthMm;
            node = OtherEnd(s, node);
        }
    }

    private void ReleaseAll(Train t)
    {
        var mine = new List<int>();
        foreach (var kv in _holds)
            if (kv.Value == t.Id) mine.Add(kv.Key);
        foreach (var id in mine) _holds.Remove(id);
    }

    /// <summary>Camino más corto con desempate por id de tramo: el mismo mundo da siempre la misma ruta, también tras cargar la partida.</summary>
    private List<int>? FindRoute(int fromNode, int station, Func<int, bool>? nodeBlocked, Func<int, bool>? segmentBlocked)
    {
        if (!Machines.TryGetValue(station, out var st)) return null;
        if (nodeBlocked != null && nodeBlocked(fromNode)) return null;
        var dist = new Dictionary<int, long> { [fromNode] = 0 };
        var via = new Dictionary<int, RailSegment>();
        var queue = new PriorityQueue<int, (long, int)>();
        queue.Enqueue(fromNode, (0, fromNode));
        var target = -1;
        while (queue.TryDequeue(out var node, out var priority))
        {
            var d = priority.Item1;
            if (d > dist[node]) continue;
            if (node == st.RailA || node == st.RailB) { target = node; break; }
            if (!_adjacency.TryGetValue(node, out var list)) continue;
            foreach (var s in list)
            {
                if (segmentBlocked != null && segmentBlocked(s.Id)) continue;
                var other = OtherEnd(s, node);
                if (nodeBlocked != null && nodeBlocked(other)) continue;
                var nd = d + s.LengthMm;
                if (dist.TryGetValue(other, out var old) && (old < nd || old == nd && via.TryGetValue(other, out var v) && v.Id <= s.Id)) continue;
                dist[other] = nd;
                via[other] = s;
                queue.Enqueue(other, (nd, other));
            }
        }
        if (target < 0) return null;
        var path = new List<int>();
        for (var n = target; n != fromNode;)
        {
            var s = via[n];
            path.Add(s.Id);
            n = OtherEnd(s, n);
        }
        path.Reverse();
        return path;
    }

    /// <summary>Camino más corto. Solo se recalcula al salir, ante una señal o si la ruta se corta, nunca cada tick.</summary>
    public List<int>? FindPath(int fromNode, int station) => FindRoute(fromNode, station, null, null);

    /// <summary>
    /// Elige la ruta y el bloque que se le pueden conceder a este tren desde un nodo. Primero la más corta; si su siguiente bloque está ocupado
    /// o concederlo no sería seguro, la más corta que no pase por nada retenido por otro: ese desvío siempre es seguro, porque este tren
    /// podría llegar primero sin esperar a nadie.
    /// </summary>
    private PlanResult Plan(Train t, int from, out List<int> route, out int end)
    {
        route = new List<int>();
        end = 0;
        var target = TargetOf(t);
        var shortest = FindPath(from, target);
        if (shortest == null) return PlanResult.NoRoute;
        var shortestEnd = BlockEnd(from, shortest);
        if (BlockFree(t, from, shortest, shortestEnd))
        {
            var whole = shortestEnd == shortest.Count || BlockFree(t, from, shortest, shortest.Count);
            if (whole || IsSafe(t, from, shortest, shortestEnd))
            {
                route = shortest;
                end = shortestEnd;
                return PlanResult.Granted;
            }
        }
        var free = FindRoute(from, target, id => HeldByOther(id, t.Id), id => SegmentHeldByOther(id, t.Id));
        if (free == null) return PlanResult.Blocked;
        route = free;
        end = BlockEnd(from, free);
        return PlanResult.Granted;
    }

    /// <summary>Lo que ocupa un tren detenido ante el final de su bloque: el tramo donde se para y lo que abarque su cola.</summary>
    private void CollectRest(List<int> segments, int count, int endNode, HashSet<int> into)
    {
        if (count <= 0) return;
        var node = endNode;
        var dist = (long)R.SignalStopMm;
        var length = (long)R.TrainLengthMm;
        for (var i = count - 1; i >= 0; i--)
        {
            if (!RailSegments.TryGetValue(segments[i], out var s)) return;
            into.Add(s.Id);
            dist = i == count - 1 ? Math.Min(dist, s.LengthMm) : dist;
            // `dist` es lo que hay entre la cola de este tramo y el morro: si ya supera la longitud, el resto queda libre.
            var remaining = i == count - 1 ? s.LengthMm - dist : s.LengthMm;
            length -= remaining;
            if (length <= 0) return;
            node = OtherEnd(s, node);
            into.Add(node);
        }
    }

    private sealed class Runner
    {
        public required Train Train;
        public required int EndNode;
        public required HashSet<int> Rest;
        public bool Stuck;
    }

    /// <summary>
    /// ¿Hay un orden en que todos los trenes en ruta terminen si se concede este bloque? Cada tren se imagina detenido al final de su
    /// bloque, retiene lo que ocupa ahí y puede elegir cualquier ruta libre desde ese punto. Al terminar uno, libera lo suyo.
    /// </summary>
    private bool IsSafe(Train candidate, int from, List<int> route, int end)
    {
        var runners = new List<Runner>();
        foreach (var other in _trainList)
        {
            if (other.Id == candidate.Id || other.Planet != candidate.Planet || !OnTrack(other.State)) continue;
            // Quien ya tiene reservado hasta su destino llegará sin pedir nada más.
            if (other.GrantEnd >= other.Path.Count) continue;
            var endNode = NodeOfIndex(other, other.GrantEnd);
            if (endNode < 0) continue;
            var rest = new HashSet<int>();
            CollectRest(other.Path, other.GrantEnd, endNode, rest);
            runners.Add(new Runner { Train = other, EndNode = endNode, Rest = rest });
        }
        var planned = new List<int>();
        if (OnTrack(candidate.State))
            for (var i = 0; i < candidate.GrantEnd && i < candidate.Path.Count; i++) planned.Add(candidate.Path[i]);
        var node = from;
        for (var i = 0; i < end; i++)
        {
            planned.Add(route[i]);
            node = OtherEnd(RailSegments[route[i]], node);
        }
        var mine = new HashSet<int>();
        CollectRest(planned, planned.Count, node, mine);
        runners.Add(new Runner { Train = candidate, EndNode = node, Rest = mine });

        // Sin ruta posible, ni con la vía vacía: no terminará, pero lo que ocupa sigue ocupado para los demás.
        foreach (var r in runners)
            r.Stuck = FindPath(r.EndNode, TargetOf(r.Train)) == null;

        var pending = runners.Where(r => !r.Stuck).ToList();
        var fixedHolds = new HashSet<int>();
        foreach (var r in runners)
            if (r.Stuck) fixedHolds.UnionWith(r.Rest);

        while (pending.Count > 0)
        {
            Runner? finished = null;
            foreach (var r in pending)
            {
                var blocked = new HashSet<int>(fixedHolds);
                foreach (var o in pending)
                    if (o != r) blocked.UnionWith(o.Rest);
                bool SegmentBlocked(int id)
                {
                    if (blocked.Contains(id)) return true;
                    if (_crossings.TryGetValue(id, out var list))
                        foreach (var c in list)
                            if (blocked.Contains(c)) return true;
                    return false;
                }
                if (FindRoute(r.EndNode, TargetOf(r.Train), blocked.Contains, SegmentBlocked) == null) continue;
                finished = r;
                break;
            }
            if (finished == null) return false;
            pending.Remove(finished);
        }
        return true;
    }

    /// <summary>Hay un tren trabajando en la estación de destino, o ya va hacia ella con el último bloque reservado.</summary>
    private bool StationBusy(Train t, int station)
    {
        foreach (var other in _trainList)
        {
            if (other.Id == t.Id) continue;
            if (other.State is TrainState.Loading or TrainState.Unloading && StationAt(other)?.Id == station) return true;
            if (other.State is TrainState.Moving or TrainState.AtSignal && TargetOf(other) == station && other.GrantEnd >= other.Path.Count) return true;
        }
        return false;
    }

    /// <summary>
    /// Pide el bloque siguiente para un tren que llega a su señal. Una estación ocupada solo hace esperar un tiempo limitado y solo a quien
    /// la tiene trabajando: si el ocupante ya quiere salir y no puede, entrar a la cola de la parada es lo que deja libre la vía.
    /// </summary>
    private bool TryExtend(Train t)
    {
        var from = NodeOfIndex(t, t.GrantEnd);
        if (from < 0)
        {
            SetWait(t, "wait_no_route");
            return false;
        }
        var plan = Plan(t, from, out var route, out var end);
        if (plan != PlanResult.Granted)
        {
            SetWait(t, plan == PlanResult.NoRoute ? "wait_no_route" : "wait_signal");
            return false;
        }
        if (end == route.Count && t.HoldTicks < R.StationWaitMaxTicks && StationBusy(t, TargetOf(t)))
        {
            SetWait(t, "wait_station_busy");
            return false;
        }
        t.Path.RemoveRange(t.GrantEnd, t.Path.Count - t.GrantEnd);
        t.Path.AddRange(route);
        DirtyTrainPaths.Add(t.Id);
        var start = t.GrantEnd;
        t.GrantEnd += end;
        HoldRange(t, start, t.GrantEnd);
        t.HoldTicks = 0;
        SetWait(t, "");
        DirtyTrains.Add(t.Id);
        return true;
    }

    /// <summary>Donde se detiene el morro en este tramo si no se le concede el siguiente bloque, o null si puede recorrerlo entero.</summary>
    public long? StopLimit(Train t)
    {
        if (t.PathIndex != t.GrantEnd - 1 || t.GrantEnd >= t.Path.Count) return null;
        if (!RailSegments.TryGetValue(t.Path[t.PathIndex], out var s)) return null;
        return Math.Max(0, s.LengthMm - R.SignalStopMm);
    }

    private void StepAtSignal(Train t)
    {
        t.Timer -= TimeScale;
        t.HoldTicks += TimeScale;
        if (t.Timer > 0) return;
        if (TryExtend(t))
        {
            t.State = TrainState.Moving;
            t.Timer = 0;
            DirtyTrains.Add(t.Id);
            return;
        }
        t.Timer = 5;
    }

    /// <summary>Tren que está ocupando este tramo ahora mismo, con el morro o con la cola. Solo usa estado replicado.</summary>
    private bool SegmentOccupied(int segmentId)
    {
        var length = (long)R.TrainLengthMm;
        foreach (var t in _trainList)
        {
            if (!OnTrack(t.State)) continue;
            if (t.PathIndex < t.Path.Count && t.Path[t.PathIndex] == segmentId) return true;
            var dist = t.PosMm;
            for (var i = Math.Min(t.PathIndex, t.Path.Count) - 1; i >= 0 && dist < length; i--)
            {
                if (t.Path[i] == segmentId) return true;
                if (!RailSegments.TryGetValue(t.Path[i], out var s)) break;
                dist += s.LengthMm;
            }
        }
        return false;
    }

    /// <summary>Tramo ocupado o concedido a un tren. Partirlo le dejaría sin vía bajo los pies.</summary>
    private bool SegmentGranted(int segmentId)
    {
        if (SegmentOccupied(segmentId)) return true;
        foreach (var t in _trainList)
        {
            if (!OnTrack(t.State)) continue;
            for (var i = t.PathIndex; i < t.GrantEnd && i < t.Path.Count; i++)
                if (t.Path[i] == segmentId) return true;
        }
        return false;
    }

    /// <summary>Tramo recto que pasa por este punto sin tener ahí un nodo.</summary>
    private RailSegment? SegmentThrough(int planet, int x, int z)
    {
        if (_nodeAt.ContainsKey((planet, x, z))) return null;
        foreach (var s in RailSegments.Values)
        {
            if (s.Planet != planet || s.Shape != 0) continue;
            var a = RailNodes[s.A];
            var b = RailNodes[s.B];
            var abx = b.X - a.X;
            var abz = b.Z - a.Z;
            var apx = x - a.X;
            var apz = z - a.Z;
            if (abx * apz != abz * apx) continue;
            var dot = abx * (long)apx + abz * (long)apz;
            if (dot <= 0 || dot >= abx * (long)abx + abz * (long)abz) continue;
            return s;
        }
        return null;
    }

    /// <summary>Por qué no se puede empalmar en este punto, o null. Compartido con la previsualización del cliente.</summary>
    private string? SplitError(int planet, int x, int z)
    {
        if (SegmentThrough(planet, x, z) is not { } s) return null;
        if (s.OwnerStation >= 0) return "err_rail_platform";
        return SegmentGranted(s.Id) ? "err_rail_occupied" : null;
    }

    /// <summary>Crea un nodo en mitad de un tramo recto. La longitud total se conserva, así que no cuesta ni devuelve nada.</summary>
    private void SplitAt(int planet, int x, int z)
    {
        if (SegmentThrough(planet, x, z) is not { } s) return;
        var a = RailNodes[s.A];
        var b = RailNodes[s.B];
        var mid = NodeAt(planet, x, z);
        AddSegment(planet, a, mid, 0, -1);
        AddSegment(planet, mid, b, 0, -1);
        RemoveSegment(s);
    }

    /// <summary>Por qué no se puede poner o quitar una señal aquí, o null. Compartido con la previsualización del cliente.</summary>
    public string? SignalError(PlayerState p, int x, int z, out long credits)
    {
        credits = 0;
        if (!Upgrades.Contains("tech_rail")) return "err_locked";
        var hasNode = _nodeAt.TryGetValue((p.Planet, x, z), out var nodeId);
        if (!hasNode && SegmentThrough(p.Planet, x, z) == null) return "err_signal_no_rail";
        if (!hasNode)
        {
            var split = SplitError(p.Planet, x, z);
            if (split != null) return split;
        }
        if (!InRange(p, x * (long)B.CellSizeMm, z * (long)B.CellSizeMm, B.BuildRangeMm)) return "err_range";
        if (!hasNode || !RailNodes[nodeId].Signal) credits = R.SignalCredits;
        return null;
    }

    private CommandResult ToggleSignal(PlayerState p, int x, int z)
    {
        var error = SignalError(p, x, z, out var credits);
        if (error != null) return CommandResult.Fail(error);
        if (credits > 0 && !Pay(p, credits, new Dictionary<string, long>())) return CommandResult.Fail("err_cost");
        SplitAt(p.Planet, x, z);
        var node = RailNodes[_nodeAt[(p.Planet, x, z)]];
        node.Signal = !node.Signal;
        if (!node.Signal) AddCredits(R.SignalCredits);
        else Bump("built.signal");
        DirtyRailNodes.Add(node.Id);
        return CommandResult.Success;
    }

    /// <summary>
    /// Rojo si un tren espera ante ella, o si está ocupado o concedido alguno de los tramos que salen de ella. La misma señal vale en los
    /// dos sentidos: verde quiere decir que los tramos contiguos están libres ahora, no que tu tren tenga ya permiso.
    /// </summary>
    public SignalLight SignalLightOf(int node)
    {
        if (!_adjacency.TryGetValue(node, out var list)) return SignalLight.Clear;
        foreach (var t in Trains.Values)
        {
            if (!OnTrack(t.State)) continue;
            if (t.State == TrainState.AtSignal && NodeOfIndex(t, t.GrantEnd) == node) return SignalLight.Stop;
            for (var i = t.PathIndex; i < t.GrantEnd && i < t.Path.Count; i++)
                foreach (var s in list)
                    if (s.Id == t.Path[i]) return SignalLight.Stop;
        }
        return SignalLight.Clear;
    }

    /// <summary>Número de vías que salen de un nodo; con tres o más hay un desvío que conviene señalar en el mundo.</summary>
    public int RailDegree(int node) => _adjacency.TryGetValue(node, out var list) ? list.Count : 0;
}
