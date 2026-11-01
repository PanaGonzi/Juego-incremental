using Godot;
using Pluton.Networking;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>
/// Herramienta de cinta: conectores con histéresis, puntos fijados, alternativas y previsualización.
/// La lógica está en BeltPlanner (sin Godot y probada); aquí, aspecto, tiempo y entrada. Lo que se construye es
/// exactamente la última ruta mostrada: el clic nunca recalcula ni cambia el destino.
/// </summary>
public partial class BeltView
{
    private const double SliceSeconds = 0.002;    // presupuesto de búsqueda por frame
    private const float DestinationGrace = 0.25f; // perder la máquina de vista un instante no cambia el destino
    private const float OpenTargetDelay = 0.12f;  // al barrer el suelo se espera a que la puntería se asiente

    private BeltPlanner? _plan;
    private int _planRevision = -1;
    private uint _pending;
    private bool _reconfirm;
    private bool _manualPorts;
    private int _hoverMachine = -1;
    private (int x, int z)? _hoverPort;
    private float _destinationLost;
    private (int x, int z)? _openCandidate;
    private float _openCandidateAge;
    private bool _routeValid;
    private int _markerCount = -1;
    private readonly List<MeshInstance3D> _markers = new();
    private readonly System.Diagnostics.Stopwatch _slice = new();

    private MeshInstance3D? _preview;
    private MeshInstance3D? _ports;
    private string _portsKey = "";

    private static readonly Shader PreviewShader = new() { Code = """
        shader_type spatial;
        render_mode unshaded, cull_disabled;
        uniform vec3 tint = vec3(0.3, 1.0, 0.55);
        void fragment(){
          float x = abs(UV.x - 0.5) * 2.0;
          float c = fract((UV.y - TIME * 2.2) * 0.5 + x * 0.16);
          float chevron = smoothstep(0.0, 0.015, c) * (1.0 - smoothstep(0.09, 0.105, c)) * (1.0 - smoothstep(0.55, 0.62, x));
          ALBEDO = mix(tint, vec3(1.0), chevron * 0.8);
          ALPHA = 0.42 + chevron * 0.5;
        }
        """ };

    private static readonly ShaderMaterial PreviewOk = PreviewTint(new Color(0.3f, 1f, 0.55f));
    private static readonly ShaderMaterial PreviewBad = PreviewTint(new Color(1f, 0.35f, 0.22f));
    private static readonly ShaderMaterial PreviewOpen = PreviewTint(new Color(0.45f, 0.75f, 1f));

    private static ShaderMaterial PreviewTint(Color c)
    {
        var m = new ShaderMaterial { Shader = PreviewShader };
        m.SetShaderParameter("tint", c);
        return m;
    }

    private static readonly StandardMaterial3D PortMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    private static readonly StandardMaterial3D MarkerMaterial = new()
    {
        AlbedoColor = new Color(1f, 0.72f, 0.2f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public bool HasBeltOrigin => _plan?.HasOrigin == true;
    public BeltPlanner? Plan => _plan;
    public bool ManualPorts => _manualPorts;

    /// <summary>Cada frame con la herramienta activa: actualiza objetivo, búsqueda y dibujo. Devuelve el texto de ayuda.</summary>
    public string ToolFrame(World w, PlayerState me, string aimKind, int aimId, Vector3 aimPoint, Vector3? ground, int stamp, float delta, ref string status, ref Color color)
    {
        _plan ??= new BeltPlanner(w);
        var plan = _plan;
        var aimed = aimKind == "machine" ? w.Machines.GetValueOrDefault(aimId) : null;

        if (!plan.HasOrigin)
        {
            UpdateHover(aimed, true, aimPoint, w);
            ShowPorts(w, aimed, null, _hoverPort, stamp);
            ClearPreviewMesh();
            UpdateMarkers(w, plan);
            plan.Tick(stamp, 0);
            return Loc.T("prompt_link_from");
        }

        UpdateTarget(w, plan, aimed, aimPoint, ground, delta);
        _slice.Restart();
        do plan.Tick(stamp, 120);
        while (plan.State == BeltPlanState.Searching && _slice.Elapsed.TotalSeconds < SliceSeconds);

        var origin = w.Machines.GetValueOrDefault(plan.From);
        var destination = plan.To >= 0 ? w.Machines.GetValueOrDefault(plan.To) : null;
        if (origin == null)
        {
            plan.Reset();
            return Loc.T("prompt_link_from");
        }
        ShowPorts(w, destination ?? origin, destination != null ? origin : null, plan.EntryPort ?? plan.FromPort, stamp, plan.FromPort);
        UpdateMarkers(w, plan);
        UpdatePreviewMesh(w, plan);
        DescribePlan(w, me, plan, ref status, ref color);
        return Loc.T(plan.To >= 0 ? "prompt_link_to" : "prompt_link_way");
    }

    private void UpdateHover(Machine? m, bool output, Vector3 aim, World w)
    {
        var plan = _plan!;
        if (m == null)
        {
            _hoverMachine = -1;
            _hoverPort = null;
            return;
        }
        var same = m.Id == _hoverMachine;
        if (!same) _hoverPort = null;
        _hoverMachine = m.Id;
        // Con el ajuste desactivado, mover la vista no cambia el conector: solo la tecla de cambiar.
        if (!_manualPorts || _hoverPort == null) _hoverPort = plan.PickPort(m, output, aim.X, aim.Z, _hoverPort);
    }

    private void UpdateTarget(World w, BeltPlanner plan, Machine? aimed, Vector3 aimPoint, Vector3? ground, float delta)
    {
        if (aimed != null && aimed.Id != plan.From && w.CanReceiveByBelt(w.Content.Machines[aimed.Def]))
        {
            _destinationLost = 0;
            // Por omisión, la búsqueda elige la entrada más directa: apuntar a la máquina no obliga a rodearla (INC-10).
            // Elegir conector a mano, con su tecla, fija ese y deja de seguir a la puntería.
            if (_manualPorts)
            {
                UpdateHover(aimed, false, aimPoint, w);
                plan.SetDestination(aimed.Id, _hoverPort);
            }
            else plan.SetDestination(aimed.Id, null);
            return;
        }
        if (plan.To >= 0)
        {
            _destinationLost += delta;
            if (_destinationLost < DestinationGrace) return;
        }
        (int x, int z)? cell = null;
        if (ground is { } g && aimed == null)
        {
            var c = ((int)Mathf.Floor(g.X / Cell), (int)Mathf.Floor(g.Z / Cell));
            if (w.CellFreeForBelt(plan_Planet(w, plan), c.Item1, c.Item2)) cell = c;
        }
        if (cell != _openCandidate)
        {
            _openCandidate = cell;
            _openCandidateAge = 0;
        }
        _openCandidateAge += delta;
        // Mientras la puntería se mueve se mantiene lo que había, sin relanzar la búsqueda en cada celda.
        if (_openCandidateAge >= OpenTargetDelay || plan.To >= 0) plan.SetOpenTarget(_openCandidate);
    }

    private static int plan_Planet(World w, BeltPlanner plan) => w.Machines.TryGetValue(plan.From, out var m) ? m.Planet : 0;

    private void DescribePlan(World w, PlayerState me, BeltPlanner plan, ref string status, ref Color color)
    {
        color = Ui.Ink;
        _routeValid = false;
        if (_pending != 0)
        {
            status = Loc.T("belt_sending");
            return;
        }
        switch (plan.State)
        {
            case BeltPlanState.Searching:
                status = Loc.T("belt_tracing");
                return;
            case BeltPlanState.Failed:
                // Apuntar a una celda inservible mientras se traza no es un fallo que merezca aviso; sin destino no se construye nada.
                if (plan.To < 0) return;
                status = Loc.T(plan.Error ?? "err_belt_route");
                color = Ui.Bad;
                return;
            case BeltPlanState.Ready when plan.Route is { } route:
                var credits = route.Count * w.Content.Balance.BeltCreditsPerCell;
                var meters = route.Count * w.Content.Balance.CellSizeMm / 1000;
                var text = Loc.T("belt_preview", route.Count.ToString()).Replace("{1}", meters.ToString()).Replace("{2}", route.Count.ToString()).Replace("{3}", credits.ToString());
                var affordable = w.CanAfford(me, credits, new Dictionary<string, long>());
                if (!affordable) text += " · " + Loc.T("belt_missing", (credits - w.Credits).ToString());
                if (plan.AlternativeCount > 1) text += " · " + Loc.T("belt_alternative", (plan.AlternativeIndex + 1).ToString());
                if (_reconfirm && plan.To >= 0) text = Loc.T("belt_reconfirm") + " · " + text;
                status = text;
                color = !affordable ? Ui.Bad : plan.To >= 0 ? Ui.Good : Ui.Ink;
                _routeValid = affordable && plan.To >= 0;
                return;
            default:
                status = plan.Waypoints.Count > 0 ? Loc.T("belt_points", plan.Waypoints.Count.ToString()) : "";
                return;
        }
    }

    // ---------- Acciones del jugador ----------

    /// <summary>Clic. Devuelve una clave de texto para avisar, o null si no hay nada que decir.</summary>
    public string? ToolClick(GameClient client, string aimKind, int aimId, Vector3? ground)
    {
        var w = client.World!;
        _plan ??= new BeltPlanner(w);
        var plan = _plan;
        if (_pending != 0) return "belt_sending";
        var aimed = aimKind == "machine" ? w.Machines.GetValueOrDefault(aimId) : null;

        if (!plan.HasOrigin)
        {
            if (aimed == null) return null;
            if (!w.CanSendByBelt(w.Content.Machines[aimed.Def])) return "err_link_kind";
            if (_hoverMachine != aimed.Id || _hoverPort == null) return "err_belt_port_out";
            plan.SetOrigin(aimed.Id, _hoverPort);
            _hoverPort = null;
            _hoverMachine = -1;
            _reconfirm = false;
            Sfx.Play("deposit");
            return null;
        }

        if (aimed != null)
        {
            if (aimed.Id == plan.From) return null;
            // Solo se construye lo que se está mostrando, hacia la máquina y el conector mostrados.
            if (aimed.Id != plan.To) return null;
            if (plan.State == BeltPlanState.Searching) return "belt_tracing";
            if (plan.State == BeltPlanState.Failed) return plan.Error;
            if (!_routeValid && plan.Route is { } r && !w.CanAfford(client.LocalPlayer!, r.Count * w.Content.Balance.BeltCreditsPerCell, new Dictionary<string, long>())) return "err_cost";
            var command = plan.Confirm();
            if (command == null) return "belt_tracing";
            if (CommandCodec.EncodedRouteSize(command.Cells!) > Protocol.MaxClientPacket - 64) return "err_belt_complex";
            _pending = client.Send(command);
            Sfx.Play("build");
            return null;
        }

        if (ground is not { } g) return null;
        var error = plan.AddWaypoint(Mathf.FloorToInt(g.X / Cell), Mathf.FloorToInt(g.Z / Cell));
        if (error == null) Sfx.Play("deposit");
        return error;
    }

    /// <summary>Respuesta del servidor. Si se rechazó, se conserva lo elegido, se recalcula y se pide confirmar de nuevo.</summary>
    public void ToolResult(uint id, CommandResult result)
    {
        if (id != _pending || _pending == 0) return;
        _pending = 0;
        if (result.Ok)
        {
            // La herramienta sigue activa para tender la siguiente cinta.
            _plan?.Reset();
            _reconfirm = false;
            return;
        }
        _reconfirm = true;
        _plan?.Refresh();
    }

    /// <summary>Anula el trazado sin coste. Devuelve false si no había ninguno en curso.</summary>
    public bool ToolCancel()
    {
        if (_plan is not { HasOrigin: true }) return false;
        _plan.Reset();
        _reconfirm = false;
        _openCandidate = null;
        ClearPreviewMesh();
        return true;
    }

    public void ToolUndoPoint()
    {
        if (_plan?.RemoveLastWaypoint() == true) Sfx.Play("take");
    }

    public void ToolNextRoute()
    {
        if (_plan is { State: BeltPlanState.Ready }) _plan.NextAlternative();
    }

    /// <summary>Activa o desactiva el ajuste automático del conector. Devuelve el texto para avisar.</summary>
    public string ToolToggleSnap()
    {
        _manualPorts = !_manualPorts;
        return _manualPorts ? "belt_snap_off" : "belt_snap_on";
    }

    /// <summary>Elige a mano el siguiente conector de la máquina apuntada; deja de seguir a la puntería.</summary>
    public void ToolNextPort(World w, string aimKind, int aimId)
    {
        _plan ??= new BeltPlanner(w);
        if (aimKind != "machine" || !w.Machines.TryGetValue(aimId, out var m)) return;
        _manualPorts = true;
        var output = !_plan.HasOrigin;
        if (_hoverMachine != m.Id) _hoverPort = null;
        _hoverMachine = m.Id;
        _hoverPort = _plan.NextPort(m, output, _hoverPort);
        if (!output) _plan.SetDestination(m.Id, _hoverPort);
    }

    // ---------- Dibujo ----------

    private void ClearPreviewMesh()
    {
        _preview?.QueueFree();
        _preview = null;
        _planRevision = -1;
    }

    private void UpdatePreviewMesh(World w, BeltPlanner plan)
    {
        if (plan.Revision == _planRevision && _preview != null)
        {
            _preview.MaterialOverride = MaterialFor(plan);
            return;
        }
        // Mientras se calcula la siguiente se mantiene la anterior: sin parpadeos.
        if (plan.Route == null)
        {
            if (plan.State == BeltPlanState.Searching) return;
            ClearPreviewMesh();
            _planRevision = plan.Revision;
            return;
        }
        _planRevision = plan.Revision;
        _preview?.QueueFree();
        var points = RoutePath(w, plan.From, plan.To, plan.Route, i => w.BeltAt(plan_Planet(w, plan), plan.Route[i].x, plan.Route[i].z) >= 0);
        _preview = new MeshInstance3D { Mesh = PreviewMesh(points), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = MaterialFor(plan) };
        AddChild(_preview);
    }

    private ShaderMaterial MaterialFor(BeltPlanner plan) =>
        plan.State == BeltPlanState.Failed ? PreviewBad : plan.To < 0 ? PreviewOpen : _routeValid ? PreviewOk : PreviewBad;

    private static ArrayMesh PreviewMesh(Vector3[] p)
    {
        var g = new Geometry();
        var lift = Vector3.Up * 0.09f;
        var along = 0f;
        for (var i = 0; i < p.Length - 1; i++)
        {
            var s0 = Side(Tangent(p, i));
            var s1 = Side(Tangent(p, i + 1));
            var next = along + p[i].DistanceTo(p[i + 1]);
            g.Quad(p[i] + lift - s0 * HalfWidth, p[i] + lift + s0 * HalfWidth, p[i + 1] + lift + s1 * HalfWidth, p[i + 1] + lift - s1 * HalfWidth, Vector3.Up, Colors.White,
                new Vector2(0, along), new Vector2(1, along), new Vector2(1, next), new Vector2(0, next));
            along = next;
        }
        var mesh = new ArrayMesh();
        g.Commit(mesh, PreviewOk);
        return mesh;
    }

    private void UpdateMarkers(World w, BeltPlanner plan)
    {
        var points = plan.Waypoints;
        var key = points.Aggregate(points.Count, (h, c) => HashCode.Combine(h, c.x, c.z));
        if (key == _markerCount) return;
        _markerCount = key;
        foreach (var m in _markers) m.QueueFree();
        _markers.Clear();
        foreach (var (x, z) in points)
        {
            var center = new Vector3((x + 0.5f) * Cell, 0, (z + 0.5f) * Cell);
            center.Y = GroundHeight(center.X, center.Z);
            var pole = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 2.2f, RadialSegments = 8 }, MaterialOverride = MarkerMaterial, Position = center + Vector3.Up * 1.1f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            var head = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 12, Rings = 6 }, MaterialOverride = MarkerMaterial, Position = center + Vector3.Up * 2.3f, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(pole);
            AddChild(head);
            _markers.Add(pole);
            _markers.Add(head);
        }
    }

    /// <summary>Marca conectores: salida ámbar, entrada azul, ocupados en gris. El elegido se distingue con un marco blanco.</summary>
    private void ShowPorts(World w, Machine? m, Machine? other, (int x, int z)? selected, int stamp, (int x, int z)? otherSelected = null)
    {
        var key = m == null ? "" : $"{m.Id}|{m.Rot}|{other?.Id}|{selected}|{otherSelected}#{stamp}";
        if (key == _portsKey) return;
        _portsKey = key;
        _ports?.QueueFree();
        _ports = null;
        if (m == null) return;
        var g = new Geometry();
        var isDestination = _plan is { HasOrigin: true } && m.Id == _plan.To;
        var isOrigin = _plan is { HasOrigin: true } && m.Id == _plan.From;
        foreach (var output in new[] { true, false })
        {
            if (_plan is { HasOrigin: false } && !output) continue; // eligiendo origen, solo importan las salidas
            if (isDestination && output) continue;                  // eligiendo destino, solo las entradas
            if (isOrigin && !output) continue;                      // con el origen elegido, sus entradas no vienen al caso
            foreach (var p in PortIndicators.ForTool(w.BeltPorts(m, output), selected))
                if (!isOrigin || selected == (p.X, p.Z)) PortArrow(g, p, selected == (p.X, p.Z));
        }
        if (other != null)
            foreach (var p in w.BeltPorts(other, true))
                if (otherSelected == (p.X, p.Z)) PortArrow(g, p, true);
        if (g.V.Count == 0) return;
        var mesh = new ArrayMesh();
        g.Commit(mesh, PortMaterial);
        _ports = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ports);
    }

    private static void PortArrow(Geometry g, BeltPort p, bool selected)
    {
        var (dx, dz) = World.BeltDirs[p.Dir];
        var outward = new Vector3(dx, 0, dz);
        var dir = p.Output ? outward : -outward;
        var side = dir.Cross(Vector3.Up);
        var cell = new Vector3((p.X + 0.5f) * Cell, 0.07f + (p.Output ? 0.01f : 0f), (p.Z + 0.5f) * Cell);
        cell.Y += GroundHeight(cell.X, cell.Z);
        var center = cell - outward * (p.Output ? 0.45f : 0.2f);
        var color = p.Output ? new Color(1f, 0.66f, 0.15f, 0.9f) : new Color(0.25f, 0.7f, 1f, p.Side ? 0.55f : 0.9f);
        if (!p.Free) color = new Color(0.45f, 0.45f, 0.45f, 0.45f);
        // La elegida destaca por su marco y por su tamaño, no por llenar la celda: el resto queda por debajo.
        var scale = selected ? 1.15f : 0.72f;
        var tip = center + dir * 0.45f * scale;
        var tail = center - dir * 0.3f * scale;
        g.Quad(tail - side * 0.16f * scale, tail + side * 0.16f * scale, center + side * 0.16f * scale, center - side * 0.16f * scale, Vector3.Up, color);
        g.Quad(center - side * 0.42f * scale, center + side * 0.42f * scale, tip, tip, Vector3.Up, color);
        if (!selected) return;
        // Marco de la celda elegida.
        var c = cell + Vector3.Up * 0.015f;
        const float a = 0.92f;
        const float b = 0.78f;
        var white = new Color(1f, 1f, 1f, 0.95f);
        var r = Vector3.Right;
        var f = Vector3.Back;
        g.Quad(c - r * a - f * a, c + r * a - f * a, c + r * a - f * b, c - r * a - f * b, Vector3.Up, white);
        g.Quad(c - r * a + f * b, c + r * a + f * b, c + r * a + f * a, c - r * a + f * a, Vector3.Up, white);
        g.Quad(c - r * a - f * b, c - r * b - f * b, c - r * b + f * b, c - r * a + f * b, Vector3.Up, white);
        g.Quad(c + r * b - f * b, c + r * a - f * b, c + r * a + f * b, c + r * b + f * b, Vector3.Up, white);
    }

    public void HidePreview()
    {
        _plan?.Reset();
        _reconfirm = false;
        _openCandidate = null;
        _hoverMachine = -1;
        _hoverPort = null;
        ClearPreviewMesh();
        _ports?.QueueFree();
        _ports = null;
        _portsKey = "";
        if (_plan != null) UpdateMarkers(null!, _plan);
    }
}
