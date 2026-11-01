namespace Pluton.Simulation;

/// <summary>Qué trazar. Sin máquina de destino, OpenTarget da una celda libre: previsualiza hacia donde se apunta, pero no se construye.</summary>
public sealed record BeltRequest(
    int From,
    int To,
    IReadOnlyList<(int x, int z)> Waypoints,
    (int x, int z)? FromPort = null,
    (int x, int z)? ToPort = null,
    (int x, int z)? OpenTarget = null,
    int Strategy = 0,
    IReadOnlyCollection<(int x, int z)>? Avoid = null);

public sealed partial class World
{
    /// <summary>
    /// A* determinista por tramos entre puntos fijados, troceable: Step avanza un presupuesto de expansiones y se puede repartir
    /// entre frames sin congelar. Solo lee el mundo; si este cambia, quien la usa debe descartarla. Reglas en D-037.
    /// </summary>
    public sealed class BeltSearch
    {
        private const int StepCost = 10;
        private const int TurnCost = 14;
        private const int CrossCost = 8;
        private const int SidePortCost = 16;
        private const int StubTurnCost = 20; // girar nada más salir o justo antes de entrar en un conector
        private const int BesideMachineCost = 5;
        private const int FrontOfMachineCost = 14; // delante o detrás de una máquina de producción: es donde irá su cinta
        private const int WalkwayCost = 6;
        private const int WalkwayRadius = 3;
        private const int BiasCost = 3;
        private const int AvoidCost = 12;

        private readonly World _w;
        private readonly BeltRequest _req;
        private readonly int _planet, _half, _spawnX, _spawnZ, _side;
        private readonly List<(int x, int z)> _path = new();
        private readonly HashSet<(int x, int z)> _used = new();
        private readonly Dictionary<int, int> _cellCost = new();
        private readonly Dictionary<int, (int g, int parent)> _best = new();
        private readonly PriorityQueue<int, long> _open = new();
        private readonly Dictionary<(int x, int z), (int extra, int into)> _goals = new();
        private int _leg;
        private int _minX, _maxX, _minZ, _maxZ;
        private int _goalKey = -1, _goalCost = int.MaxValue;
        private readonly HashSet<int> _starts = new();

        public bool Done { get; private set; }
        public string? Error { get; private set; }
        public long Expansions { get; private set; }
        public IReadOnlyList<(int x, int z)> Cells => _path;

        public BeltSearch(World world, BeltRequest request)
        {
            _w = world;
            _req = request;
            if (!world.Machines.TryGetValue(request.From, out var a)) { Fail("err_gone"); return; }
            _planet = a.Planet;
            var planet = world.Content.Planets.First(p => p.Id == _planet);
            _half = planet.HalfSizeCells;
            _side = 2 * _half + 1;
            _spawnX = planet.Spawn.X;
            _spawnZ = planet.Spawn.Z;
            if (request.To >= 0)
            {
                if (request.To == request.From) { Fail("err_invalid"); return; }
                if (!world.Machines.TryGetValue(request.To, out var b) || b.Planet != _planet) { Fail("err_gone"); return; }
                if (!world.CanSendByBelt(world.Content.Machines[a.Def]) || !world.CanReceiveByBelt(world.Content.Machines[b.Def])) { Fail("err_link_kind"); return; }
            }
            foreach (var w in request.Waypoints)
                if (!Free(w.x, w.z)) { Fail("err_belt_waypoint"); return; }
            var outputs = world.BeltPorts(a, true).Where(p => p.Free && (request.FromPort == null || request.FromPort == (p.X, p.Z))).ToList();
            if (outputs.Count == 0) { Fail("err_belt_port_out"); return; }
            BeginLeg();
            if (Done) return;
            foreach (var p in outputs) Seed(p.X, p.Z, p.Dir, 0);
        }

        private void Fail(string error)
        {
            Error = error;
            Done = true;
        }

        private bool Free(int x, int z) => !_w.GroundBlocked(_planet, x, z, _half) && !_w._beltCells.ContainsKey((_planet, x, z));

        private int Key(int x, int z, int d) => ((x + _half) * _side + z + _half) * 4 + d;
        private (int x, int z, int d) Decode(int key)
        {
            var d = key & 3;
            var cell = key >> 2;
            return (cell / _side - _half, cell % _side - _half, d);
        }

        /// <summary>Prepara el destino del tramo actual: el siguiente punto fijado o, en el último, los conectores de entrada.</summary>
        private void BeginLeg()
        {
            _goals.Clear();
            _best.Clear();
            _open.Clear();
            _starts.Clear();
            _goalKey = -1;
            _goalCost = int.MaxValue;
            if (_leg < _req.Waypoints.Count)
            {
                var w = _req.Waypoints[_leg];
                _goals[(w.x, w.z)] = (0, -1);
            }
            else if (_req.To >= 0)
            {
                var b = _w.Machines[_req.To];
                foreach (var p in _w.BeltPorts(b, false))
                {
                    if (!p.Free || _used.Contains((p.X, p.Z)) || _req.ToPort != null && _req.ToPort != (p.X, p.Z)) continue;
                    var extra = p.Side ? SidePortCost : 0;
                    if (!_goals.TryGetValue((p.X, p.Z), out var known) || extra < known.extra) _goals[(p.X, p.Z)] = (extra, (p.Dir + 2) % 4);
                }
                if (_goals.Count == 0) { Fail("err_belt_port_in"); return; }
            }
            else if (_req.OpenTarget is { } open)
            {
                if (!Free(open.x, open.z) || _used.Contains(open)) { Fail("err_belt_waypoint"); return; }
                _goals[open] = (0, -1);
            }
            else
            {
                Fail("err_invalid");
                return;
            }
            _minX = _goals.Keys.Min(g => g.x);
            _maxX = _goals.Keys.Max(g => g.x);
            _minZ = _goals.Keys.Min(g => g.z);
            _maxZ = _goals.Keys.Max(g => g.z);
        }

        private int Heuristic(int x, int z) =>
            (Math.Max(0, Math.Max(_minX - x, x - _maxX)) + Math.Max(0, Math.Max(_minZ - z, z - _maxZ))) * StepCost;

        /// <summary>Además de la distancia, la curva inevitable: sin esto, en campo abierto se explora todo el rectángulo.</summary>
        private int Remaining(int x, int z, int d)
        {
            var needX = x < _minX || x > _maxX;
            var needZ = z < _minZ || z > _maxZ;
            var alongX = d % 2 == 1;
            var turnsNeeded = needX && needZ || needX && !alongX || needZ && alongX;
            return Heuristic(x, z) + (turnsNeeded ? TurnCost : 0);
        }

        private void Seed(int x, int z, int d, int cost)
        {
            var key = Key(x, z, d);
            if (_best.ContainsKey(key)) return;
            _best[key] = (cost + StepCost, -1);
            _starts.Add(key);
            _open.Enqueue(key, Priority(cost + StepCost + Remaining(x, z, d), key));
        }

        // f, luego lo que falta (prefiere lo más avanzado: en campo abierto evita explorar todas las rutas iguales) y la celda como desempate total.
        private long Priority(int f, int key)
        {
            var (x, z, _) = Decode(key);
            return ((long)f << 34) | ((long)Math.Min(Heuristic(x, z), 16383) << 20) | (uint)key;
        }

        /// <summary>Penalización de ocupar esta celda: rodear puertos, máquinas y la zona de aparición da recorridos comprensibles.</summary>
        private int CellCost(int x, int z)
        {
            var code = (x + _half) * _side + z + _half;
            if (_cellCost.TryGetValue(code, out var cost)) return cost;
            cost = 0;
            for (var d = 0; d < 4; d++)
            {
                var (dx, dz) = BeltDirs[d];
                if (!_w._occupied.TryGetValue((_planet, x + dx, z + dz), out var id) || id == _req.From || id == _req.To) continue;
                if (!_w.Machines.TryGetValue(id, out var m)) continue;
                var def = _w.Content.Machines[m.Def];
                // La celda está en la cara opuesta a la dirección d, vista desde la máquina.
                var face = (d + 2) % 4;
                var front = FrontDir(m.Rot);
                var frontOrBack = def.Kind == MachineKind.Production && (face == front || face == (front + 2) % 4);
                cost = Math.Max(cost, frontOrBack ? FrontOfMachineCost : BesideMachineCost);
            }
            if (Math.Abs(x - _spawnX) <= WalkwayRadius && Math.Abs(z - _spawnZ) <= WalkwayRadius) cost += WalkwayCost;
            if (_req.Avoid != null && _req.Avoid.Contains((x, z))) cost += AvoidCost;
            _cellCost[code] = cost;
            return cost;
        }

        /// <summary>Estrategias 1 y 2: invierten el orden de los ejes y dan la otra forma de L; la distancia real no cambia.</summary>
        private int Bias(int x, int z, int d)
        {
            if (_req.Strategy == 1 && d % 2 == 0 && (x < _minX || x > _maxX)) return BiasCost;
            if (_req.Strategy == 2 && d % 2 == 1 && (z < _minZ || z > _maxZ)) return BiasCost;
            return 0;
        }

        /// <summary>Avanza hasta agotar el presupuesto. Devuelve true al terminar, con Cells o con Error.</summary>
        public bool Step(int budget)
        {
            for (var i = 0; i < budget && !Done; i++)
            {
                if (!_open.TryDequeue(out var key, out var priority))
                {
                    FinishLeg();
                    continue;
                }
                var f = (int)(priority >> 34);
                if (f >= _goalCost)
                {
                    FinishLeg();
                    continue;
                }
                Expansions++;
                Expand(key, f);
            }
            return Done;
        }

        private void Expand(int key, int f)
        {
            var (x, z, d) = Decode(key);
            var g = _best[key].g;
            if (g + Remaining(x, z, d) < f) return; // entrada obsoleta
            var crossing = _w._beltCells.ContainsKey((_planet, x, z));
            if (!crossing && _goals.TryGetValue((x, z), out var goal))
            {
                var total = g + goal.extra + (goal.into >= 0 && goal.into != d ? StubTurnCost : 0);
                if (total < _goalCost)
                {
                    _goalCost = total;
                    _goalKey = key;
                }
            }
            var isStart = _leg == 0 && _starts.Contains(key);
            for (var turn = -1; turn <= 1; turn++)
            {
                if (crossing && turn != 0) continue; // dentro de un cruce no se gira
                var nd = (d + turn + 4) % 4;
                var nx = x + BeltDirs[nd].dx;
                var nz = z + BeltDirs[nd].dz;
                if (_w.GroundBlocked(_planet, nx, nz, _half) || _used.Contains((nx, nz))) continue;
                var cost = g + StepCost + CellCost(nx, nz) + Bias(x, z, nd);
                if (turn != 0) cost += TurnCost + (isStart ? StubTurnCost : 0);
                if (_w._beltCells.TryGetValue((_planet, nx, nz), out var use))
                {
                    if (use.a >= 0 && use.b >= 0) continue; // como máximo dos cintas en una celda
                    if (!_w.CanCrossOver(use.a >= 0 ? use.a : use.b, nx, nz, nd)) continue;
                    cost += CrossCost;
                }
                var next = Key(nx, nz, nd);
                if (_best.TryGetValue(next, out var seen) && seen.g <= cost) continue;
                _best[next] = (cost, key);
                _open.Enqueue(next, Priority(cost + Remaining(nx, nz, nd), next));
            }
        }

        private void FinishLeg()
        {
            if (_goalKey < 0)
            {
                Fail(_leg < _req.Waypoints.Count ? "err_belt_waypoint_path" : "err_belt_route");
                return;
            }
            var leg = new List<(int x, int z)>();
            for (var k = _goalKey; k >= 0; k = _best[k].parent)
            {
                var (x, z, _) = Decode(k);
                leg.Add((x, z));
            }
            leg.Reverse();
            // El punto fijado es a la vez final de un tramo e inicio del siguiente.
            for (var i = _path.Count > 0 ? 1 : 0; i < leg.Count; i++)
            {
                _path.Add(leg[i]);
                _used.Add(leg[i]);
            }
            var arrival = Decode(_goalKey).d;
            var end = _path[^1];
            var lastLeg = _leg >= _req.Waypoints.Count;
            _leg++;
            if (lastLeg)
            {
                Done = true;
                return;
            }
            BeginLeg();
            if (Done) return;
            _used.Remove(end);
            Seed(end.x, end.z, arrival, 0);
            _used.Add(end);
        }
    }

    /// <summary>Presupuesto de la búsqueda en el servidor, solo para migración y Move: su coste queda acotado.</summary>
    public const int ServerRouteBudget = 12000;

    /// <summary>Búsqueda de una vez, con presupuesto. El trazado del jugador usa BeltSearch por porciones desde el cliente.</summary>
    public BeltRoute RouteBelt(int fromId, int toId, int budget = ServerRouteBudget)
    {
        var search = new BeltSearch(this, new BeltRequest(fromId, toId, Array.Empty<(int, int)>()));
        search.Step(budget);
        if (!search.Done) return new BeltRoute(Array.Empty<(int, int)>(), "err_belt_route", 0);
        return search.Error != null
            ? new BeltRoute(Array.Empty<(int, int)>(), search.Error, 0)
            : new BeltRoute(search.Cells.ToList(), null, search.Cells.Count * B.BeltCreditsPerCell);
    }

    /// <summary>Comando con la ruta ya trazada: es lo único que construye una cinta.</summary>
    public static Command LinkCommand(int from, int to, IReadOnlyList<(int x, int z)> cells) =>
        new() { Type = CommandType.Link, A = from, B = to, Cells = cells.ToList() };

    /// <summary>
    /// Valida exactamente la ruta recibida, sin sustituirla: conectores, adyacencia, celdas libres y cruces.
    /// La usa el servidor al construir y el cliente para saber si lo previsualizado sigue siendo válido.
    /// </summary>
    public string? CheckBeltRoute(int from, int to, IReadOnlyList<(int x, int z)>? cells)
    {
        if (from == to) return "err_invalid";
        if (!Machines.TryGetValue(from, out var a) || !Machines.TryGetValue(to, out var b) || a.Planet != b.Planet) return "err_gone";
        if (!CanSendByBelt(Content.Machines[a.Def]) || !CanReceiveByBelt(Content.Machines[b.Def])) return "err_link_kind";
        foreach (var l in _linkList)
            if (l.From == from && l.To == to) return "err_link_exists";
        if (cells == null || cells.Count == 0) return "err_belt_path";

        var first = cells[0];
        var last = cells[^1];
        if (!BeltPorts(a, true).Any(p => (p.X, p.Z) == first)) return "err_belt_path";
        if (!BeltPorts(b, false).Any(p => (p.X, p.Z) == last)) return "err_belt_path";

        var planet = a.Planet;
        var half = Content.Planets.First(p => p.Id == planet).HalfSizeCells;
        var seen = new HashSet<(int, int)>();
        for (var i = 0; i < cells.Count; i++)
        {
            var (x, z) = cells[i];
            if (i > 0 && Math.Abs(x - cells[i - 1].x) + Math.Abs(z - cells[i - 1].z) != 1) return "err_belt_path";
            if (!seen.Add((x, z))) return "err_belt_path";
            if (GroundBlocked(planet, x, z, half)) return "err_belt_stale";
            if (!_beltCells.TryGetValue((planet, x, z), out var use)) continue;
            // Sobre otra cinta solo se puede pasar recto, y nunca empezar ni terminar ahí.
            if (i == 0 || i == cells.Count - 1 || use.a >= 0 && use.b >= 0) return "err_belt_stale";
            var dx = cells[i + 1].x - cells[i - 1].x;
            var dz = cells[i + 1].z - cells[i - 1].z;
            if (dx != 0 && dz != 0) return "err_belt_stale";
            var dir = dz < 0 ? 0 : dx > 0 ? 1 : dz > 0 ? 2 : 3;
            if (!CanCrossOver(use.a >= 0 ? use.a : use.b, x, z, dir)) return "err_belt_stale";
        }
        return null;
    }
}
