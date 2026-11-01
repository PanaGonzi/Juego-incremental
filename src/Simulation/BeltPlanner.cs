namespace Pluton.Simulation;

public enum BeltPlanState { Idle, Searching, Ready, Failed }

/// <summary>
/// Estado de la herramienta de cinta en el cliente, sin Godot: origen, destino, puntos fijados, alternativas y búsqueda por porciones.
/// Lo que se construye es exactamente la última ruta lista: nunca se recalcula al confirmar. Reglas en D-037 y D-038.
/// </summary>
public sealed class BeltPlanner
{
    /// <summary>Un conector nuevo debe estar al menos esto más cerca (metros) para sustituir al actual: evita saltos.</summary>
    public const float PortHysteresisM = 1.2f;
    private const int Strategies = 4;

    private readonly World _w;
    private readonly List<(int x, int z)> _waypoints = new();
    private readonly List<IReadOnlyList<(int x, int z)>> _routes = new();
    private World.BeltSearch? _search;
    private BeltRequest? _running;
    private int _strategy;
    private int _selected;
    private bool _wantNext;
    private int _stamp = int.MinValue;
    private bool _dirty;

    public int From { get; private set; } = -1;
    public (int x, int z)? FromPort { get; private set; }
    public int To { get; private set; } = -1;
    public (int x, int z)? ToPort { get; private set; }
    public (int x, int z)? OpenTarget { get; private set; }
    public IReadOnlyList<(int x, int z)> Waypoints => _waypoints;
    public BeltPlanState State { get; private set; } = BeltPlanState.Idle;
    public string? Error { get; private set; }
    public long Expansions { get; private set; }
    /// <summary>La ruta cambió desde que se mostró: la vista debe rehacer su malla.</summary>
    public int Revision { get; private set; }
    public int AlternativeCount => _routes.Count;
    public int AlternativeIndex => _selected;
    /// <summary>Ya se probaron todas las estrategias: AlternativeCount es definitivo.</summary>
    public bool AlternativesComplete => _strategy >= Strategies && !_wantNext;
    public IReadOnlyList<(int x, int z)>? Route => _routes.Count > _selected ? _routes[_selected] : null;
    public bool HasOrigin => From >= 0;
    public bool CanConfirm => State == BeltPlanState.Ready && To >= 0 && Route != null;

    public BeltPlanner(World world) { _w = world; }

    public void Reset()
    {
        From = -1;
        FromPort = null;
        _waypoints.Clear();
        ClearTarget();
        Invalidate();
        State = BeltPlanState.Idle;
        Error = null;
    }

    private void ClearTarget()
    {
        To = -1;
        ToPort = null;
        OpenTarget = null;
    }

    private void Invalidate()
    {
        _routes.Clear();
        _search = null;
        _running = null;
        _strategy = 0;
        _selected = 0;
        _wantNext = false;
        _dirty = true;
        State = BeltPlanState.Searching; // desde este instante, lo anterior no se puede confirmar
        Error = null;
        Revision++;
    }

    public void SetOrigin(int machine, (int x, int z)? port)
    {
        Reset();
        From = machine;
        FromPort = port;
    }

    /// <summary>Destino máquina y conector. Sin cambio no se reinicia la búsqueda.</summary>
    public void SetDestination(int machine, (int x, int z)? port)
    {
        if (To == machine && ToPort == port && OpenTarget == null) return;
        To = machine;
        ToPort = port;
        OpenTarget = null;
        Invalidate();
    }

    /// <summary>Sin máquina apuntada se previsualiza hasta el suelo; esa ruta no se puede construir.</summary>
    public void SetOpenTarget((int x, int z)? cell)
    {
        if (To < 0 && OpenTarget == cell) return;
        ClearTarget();
        OpenTarget = cell;
        Invalidate();
    }

    public string? AddWaypoint(int x, int z)
    {
        if (!HasOrigin) return "err_invalid";
        if (_w.BeltAt(_w.Machines.TryGetValue(From, out var m) ? m.Planet : 0, x, z) >= 0) return "err_belt_waypoint";
        if (m == null || !_w.CellFreeForBelt(m.Planet, x, z)) return "err_belt_waypoint";
        if (_waypoints.Contains((x, z))) return "err_belt_waypoint";
        _waypoints.Add((x, z));
        Invalidate();
        return null;
    }

    /// <summary>Descarta lo calculado y vuelve a trazar con lo mismo elegido: tras un rechazo del servidor.</summary>
    public void Refresh() => Invalidate();

    /// <summary>Retira el último punto. Devuelve false si no había ninguno.</summary>
    public bool RemoveLastWaypoint()
    {
        if (_waypoints.Count == 0) return false;
        _waypoints.RemoveAt(_waypoints.Count - 1);
        Invalidate();
        return true;
    }

    /// <summary>Pasa a la siguiente ruta realmente distinta; si no hay más, vuelve a la primera. Nunca ofrece duplicados.</summary>
    public void NextAlternative()
    {
        if (_routes.Count == 0) return;
        _wantNext = true;
        AdvanceSelection();
    }

    private void AdvanceSelection()
    {
        if (!_wantNext) return;
        if (_selected + 1 < _routes.Count)
        {
            _selected++;
            _wantNext = false;
            Revision++;
        }
        else if (_strategy >= Strategies)
        {
            _wantNext = false;
            if (_selected != 0)
            {
                _selected = 0;
                Revision++;
            }
        }
    }

    /// <summary>
    /// Avanza la búsqueda. Si la estructura del mundo cambió se descarta todo y se vuelve a calcular:
    /// lo que se muestra nunca se basa en un mundo anterior.
    /// </summary>
    public void Tick(int structuralStamp, int budget)
    {
        if (structuralStamp != _stamp)
        {
            _stamp = structuralStamp;
            if (HasOrigin) Invalidate();
        }
        if (!HasOrigin || To < 0 && OpenTarget == null)
        {
            State = BeltPlanState.Idle;
            Error = null;
            return;
        }
        if (_dirty)
        {
            _dirty = false;
            State = BeltPlanState.Searching;
            Error = null;
        }
        // Se calcula lo necesario: la primera ruta, o la siguiente si el jugador la pidió.
        if (_routes.Count > 0 && !_wantNext)
        {
            State = BeltPlanState.Ready;
            return;
        }
        if (_search == null)
        {
            if (_strategy >= Strategies)
            {
                AdvanceSelection();
                if (_routes.Count > 0) State = BeltPlanState.Ready;
                else if (State != BeltPlanState.Failed) State = BeltPlanState.Failed;
                return;
            }
            _running = new BeltRequest(From, To, _waypoints.ToList(), FromPort, ToPort, OpenTarget, Math.Min(_strategy, 2),
                _strategy == 3 ? _routes.SelectMany(r => r).ToHashSet() : null);
            _search = new World.BeltSearch(_w, _running);
        }
        if (_routes.Count == 0) State = BeltPlanState.Searching;
        var before = _search.Expansions;
        _search.Step(budget);
        Expansions += _search.Expansions - before;
        if (!_search.Done) return;

        var finished = _search;
        _search = null;
        var first = _strategy == 0;
        _strategy++;
        if (finished.Error != null)
        {
            if (first)
            {
                // Sin ruta con la estrategia normal no hay alternativa que valga: las demás solo cambian costes.
                Error = finished.Error;
                State = BeltPlanState.Failed;
                _strategy = Strategies;
                Revision++;
            }
            return;
        }
        var cells = finished.Cells.ToList();
        if (!_routes.Any(r => r.SequenceEqual(cells)))
        {
            _routes.Add(cells);
            if (first) Revision++;
        }
        if (_routes.Count > 0) State = _wantNext ? State : BeltPlanState.Ready;
        AdvanceSelection();
        if (!_wantNext && _routes.Count > 0) State = BeltPlanState.Ready;
    }

    /// <summary>Comando con lo previsualizado, sin recalcular nada. Null si no hay ruta confirmable.</summary>
    public Command? Confirm() => CanConfirm ? World.LinkCommand(From, To, Route!) : null;

    /// <summary>
    /// Conector más cercano a lo apuntado, con histéresis: el actual solo se sustituye si otro está claramente más cerca.
    /// Devuelve null si la máquina no tiene ninguno libre.
    /// </summary>
    public (int x, int z)? PickPort(Machine m, bool output, float aimXm, float aimZm, (int x, int z)? current)
    {
        var cell = _w.Content.Balance.CellSizeMm / 1000f;
        (int x, int z)? best = null;
        var bestDistance = float.MaxValue;
        var currentDistance = float.MaxValue;
        foreach (var p in _w.BeltPorts(m, output))
        {
            if (!p.Free) continue;
            var dx = (p.X + 0.5f) * cell - aimXm;
            var dz = (p.Z + 0.5f) * cell - aimZm;
            var distance = MathF.Sqrt(dx * dx + dz * dz) + (p.Side ? 0.4f : 0f);
            if (current == (p.X, p.Z)) currentDistance = distance;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (p.X, p.Z);
            }
        }
        if (current != null && currentDistance < float.MaxValue && currentDistance - bestDistance < PortHysteresisM) return current;
        return best;
    }

    /// <summary>Siguiente conector libre, por si el jugador desactiva el ajuste automático y elige a mano.</summary>
    public (int x, int z)? NextPort(Machine m, bool output, (int x, int z)? current)
    {
        var ports = _w.BeltPorts(m, output).Where(p => p.Free).Select(p => (p.X, p.Z)).Distinct().ToList();
        if (ports.Count == 0) return null;
        var index = current == null ? -1 : ports.IndexOf(current.Value);
        return ports[(index + 1) % ports.Count];
    }
}
