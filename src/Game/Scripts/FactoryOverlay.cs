using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>Lectura junto al punto de mira al apuntar a una máquina de producción: receta, progreso y estado con icono y texto.</summary>
public partial class LookReadout : PanelContainer
{
    private readonly TextureRect _recipe;
    private readonly TextureRect _glyph;
    private readonly ProgressBar _bar;
    private readonly Label _state;
    private readonly Label _hint;
    private int _id = -1;
    private long _progress = -1;
    private double _since;
    private double _cycle = 1;
    private double _clock;
    private MachineStatus _shown = (MachineStatus)255;

    public bool Showing => Visible;
    public string StateText => _state.Text;
    public double BarValue => _bar.Value;

    public LookReadout()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.03f, 0.04f, 0.05f, 0.62f), new Color(0, 0, 0, 0), 0, 4, 8, 4));
        _recipe = Ui.Icon(Ui.Glyph("st_ok"), 22);
        _bar = Ui.Bar(0, 1, Ui.Good, 6);
        _bar.CustomMinimumSize = new Vector2(78, 6);
        _bar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _glyph = Ui.Icon(Ui.Glyph("st_ok"), 18);
        _state = Ui.Text("", UiTheme.Small, Ui.Ink);
        _hint = Ui.Text("", UiTheme.Caption, Ui.Dim);
        var row = Ui.Row(UiTheme.S, _recipe, _bar, _glyph, _state, _hint);
        row.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(row);
    }

    public void Present(World w, Machine m, double delta)
    {
        _clock += delta;
        var def = w.Content.Machines[m.Def];
        var recipe = w.Content.Recipes[def.Recipe!];
        if (_id != m.Id || _progress != m.Progress)
        {
            if (_id != m.Id) _recipe.Texture = ItemVisuals.IconOf(recipe.Outputs.Keys.Concat(recipe.Inputs.Keys).First());
            _id = m.Id;
            _progress = m.Progress;
            _since = _clock;
            _cycle = Math.Max(0.1, w.ReadMachine(m).CycleSeconds);
            _hint.Text = Controls.KeyName("chain_trace");
        }
        // Como en el panel: el servidor replica al cambiar de estado; entre medias se estima.
        var running = m.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.Overheated;
        var progress = w.ProgressOf(m) + (running ? (_clock - _since) / _cycle : 0);
        _bar.Value = m.Processing ? Math.Clamp(progress, 0, 1) : 0;
        if (_shown != m.Status)
        {
            _shown = m.Status;
            var stopped = m.Status is MachineStatus.NoInput or MachineStatus.OutputFull;
            var bad = m.Status is MachineStatus.NoPower or MachineStatus.LowPower or MachineStatus.Overheated;
            var color = bad ? Ui.Bad : stopped ? Ui.Warn : Ui.Good;
            _glyph.Texture = Ui.Glyph(Ui.StatusGlyph(m.Status));
            _state.Text = Loc.T("look_" + m.Status);
            _state.AddThemeColorOverride("font_color", color);
            _bar.AddThemeStyleboxOverride("fill", UiTheme.Box(color, new Color(0, 0, 0, 0), 0, 0, 0, 2));
        }
        Visible = true;
    }
}

/// <summary>
/// Resalta en el mundo la cadena de una máquina con su sentido y marca el eslabón limitante con su motivo.
/// Solo lee el World espejo; rehace las mallas si cambia la cadena, comprobándolo una vez por segundo.
/// </summary>
public partial class ChainOverlay : Node3D
{
    private const float Cell = 2f;
    private const float ChevronY = 1.02f;
    private const float BridgeLift = 1.0f;
    private const float ElevatedHeight = 4.7f;
    private static readonly Color Flow = new(1f, 0.74f, 0.2f, 0.9f);
    private static readonly Color Limit = new(1f, 0.26f, 0.2f, 1f);
    private static readonly Color MachineMark = new(1f, 0.74f, 0.2f, 0.75f);

    private static Shader? _shader;
    private readonly MultiMeshInstance3D _chevrons = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly MultiMeshInstance3D _frames = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly Label3D _label = new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        FixedSize = true,
        PixelSize = 0.00085f,
        FontSize = 34,
        OutlineSize = 10,
        Modulate = new Color(1f, 0.82f, 0.74f),
        OutlineModulate = new Color(0, 0, 0, 0.9f),
        Visible = false,
    };
    private readonly Sprite3D _icon = new()
    {
        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
        NoDepthTest = true,
        FixedSize = true,
        PixelSize = 0.0007f,
        Visible = false,
    };
    private double _wait;
    private string _signature = "";

    public int Seed { get; private set; } = -1;
    public bool Active => Seed >= 0;
    public ChainTrace? Current { get; private set; }
    /// <summary>Texto de lo señalado, también para el aviso al activar.</summary>
    public string Summary { get; private set; } = "";
    public int ChevronCount => _chevrons.Multimesh?.InstanceCount ?? 0;
    public event Action<string>? LimitChanged;

    public override void _Ready()
    {
        AddChild(_chevrons);
        AddChild(_frames);
        AddChild(_label);
        AddChild(_icon);
        _shader ??= new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled, shadows_disabled, blend_mix;
                varying float wave;
                void vertex() { wave = 0.45 + 0.55 * sin(TIME * 4.5 - float(INSTANCE_ID) * 0.75); }
                void fragment() { ALBEDO = COLOR.rgb; ALPHA = COLOR.a * mix(0.55, 1.0, wave); }
                """,
        };
        var material = new ShaderMaterial { Shader = _shader };
        _chevrons.MaterialOverride = material;
        _frames.MaterialOverride = material;
    }

    public void Follow(World w, int machine)
    {
        Seed = machine;
        _signature = "";
        _wait = 0;
        Refresh(w, 0);
    }

    public void Clear()
    {
        Seed = -1;
        Current = null;
        Summary = "";
        _signature = "";
        _chevrons.Multimesh = null;
        _frames.Multimesh = null;
        _label.Visible = false;
        _icon.Visible = false;
    }

    public void Refresh(World w, double delta)
    {
        if (!Active) return;
        _wait -= delta;
        if (_wait > 0) return;
        _wait = 1;
        var trace = w.TraceChain(Seed);
        if (trace == null)
        {
            Clear();
            return;
        }
        Current = trace;
        var signature = $"{string.Join(',', trace.Links)}|{string.Join(',', trace.Machines)}|{trace.Limit.Cause}|{trace.Limit.Machine}|{trace.Limit.Link}|{trace.Limit.Item}";
        if (signature == _signature) return;
        _signature = signature;
        Rebuild(w, trace);
        LimitChanged?.Invoke(Summary);
    }

    private void Rebuild(World w, ChainTrace trace)
    {
        var limit = trace.Limit;
        var placements = new List<(Transform3D at, Color color)>();
        foreach (var id in trace.Links)
        {
            if (!w.Links.TryGetValue(id, out var l)) continue;
            AddLink(w, l, id == limit.Link ? Limit : Flow, placements);
        }
        _chevrons.Multimesh = Multi(ChevronMesh(), placements);

        var marks = new List<(Transform3D at, Color color)>();
        foreach (var id in trace.Machines)
        {
            if (!w.Machines.TryGetValue(id, out var m)) continue;
            var def = w.Content.Machines[m.Def];
            var (wide, deep) = m.Rot % 2 == 0 ? (def.W, def.D) : (def.D, def.W);
            var (x, z) = w.MachineCenterMm(m);
            var hit = id == limit.Machine && limit.Link < 0;
            var size = hit ? 0.8f : 0.45f;
            marks.Add((new Transform3D(Basis.Identity.Scaled(new Vector3(wide * Cell + size, 1, deep * Cell + size)), new Vector3(x / 1000f, 0.26f, z / 1000f)), hit ? Limit : MachineMark));
        }
        _frames.Multimesh = Multi(FrameMesh(), marks);
        PlaceLabel(w, limit);
    }

    private static void AddLink(World w, Link l, Color color, List<(Transform3D, Color)> into)
    {
        if (l.Elevated || l.Cells.Count == 0)
        {
            if (!w.Machines.TryGetValue(l.From, out var a) || !w.Machines.TryGetValue(l.To, out var b)) return;
            var (ax, az) = w.MachineCenterMm(a);
            var (bx, bz) = w.MachineCenterMm(b);
            var from = new Vector3(ax / 1000f, ElevatedHeight, az / 1000f);
            var to = new Vector3(bx / 1000f, ElevatedHeight, bz / 1000f);
            var steps = Mathf.Max(1, (int)(from.DistanceTo(to) / Cell));
            for (var i = 0; i < steps; i++) into.Add((Oriented(from.Lerp(to, (i + 0.5f) / steps), to - from), color));
            return;
        }
        for (var i = 0; i < l.Cells.Count; i++)
        {
            var (x, z) = l.Cells[i];
            var (nx, nz) = i + 1 < l.Cells.Count ? l.Cells[i + 1] : (x + x - l.Cells[Math.Max(0, i - 1)].x, z + z - l.Cells[Math.Max(0, i - 1)].z);
            var direction = new Vector3(nx - x, 0, nz - z);
            if (direction == Vector3.Zero) direction = Vector3.Forward;
            var y = ChevronY + (w.IsBridge(l, i) ? BridgeLift : 0);
            into.Add((Oriented(new Vector3((x + 0.5f) * Cell, y, (z + 0.5f) * Cell), direction), color));
        }
    }

    private static Transform3D Oriented(Vector3 at, Vector3 direction) => new(Basis.LookingAt(direction.Normalized(), Vector3.Up), at);

    private void PlaceLabel(World w, ChainLimit limit)
    {
        if (limit.Cause == ChainCause.None)
        {
            Summary = Loc.T("chain_none");
            _label.Visible = false;
            _icon.Visible = false;
            return;
        }
        var position = Vector3.Zero;
        var name = "";
        if (w.Machines.TryGetValue(limit.Machine, out var m))
        {
            var (x, z) = w.MachineCenterMm(m);
            position = new Vector3(x / 1000f, Models.Machine(m.Def).Height + 1.4f, z / 1000f);
            name = Loc.T("machine_" + m.Def);
        }
        if (limit.Link >= 0 && w.Links.TryGetValue(limit.Link, out var l) && l.Cells.Count > 0)
        {
            var (cx, cz) = l.Cells[l.Cells.Count / 2];
            position = new Vector3((cx + 0.5f) * Cell, 2.2f, (cz + 0.5f) * Cell);
            name = Loc.T("tool_link");
        }
        var cause = Loc.T("chain_cause_" + limit.Cause);
        if (limit.Item.Length > 0) cause += $" · {Loc.T("item_" + limit.Item)}";
        Summary = $"{Loc.T("chain_limit")}: {name} — {cause}";
        _label.Text = $"{Loc.T("chain_limit")}\n{name}\n{cause}";
        _label.Position = position;
        _label.Visible = true;
        _icon.Texture = Ui.Glyph(limit.Cause switch
        {
            ChainCause.NoPower or ChainCause.LowPower => "st_power",
            ChainCause.Overheated => "st_heat",
            ChainCause.OutputFull => "st_full",
            ChainCause.BeltSaturated => "belt",
            _ => "st_wait",
        });
        _icon.Position = position + Vector3.Up * 1.1f;
        _icon.Visible = true;
    }

    private static MultiMesh Multi(Mesh mesh, List<(Transform3D at, Color color)> items)
    {
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh, InstanceCount = items.Count };
        for (var i = 0; i < items.Count; i++)
        {
            multi.SetInstanceTransform(i, items[i].at);
            multi.SetInstanceColor(i, items[i].color);
        }
        return multi;
    }

    private static ArrayMesh? _chevronMesh, _frameMesh;

    /// <summary>Punta de flecha plana que señala hacia −Z, el frente de Basis.LookingAt.</summary>
    private static ArrayMesh ChevronMesh() => _chevronMesh ??= Flat(new[]
    {
        (new Vector2(0, -0.62f), new Vector2(0.62f, 0.02f), new Vector2(0.62f, 0.42f), new Vector2(0, -0.22f)),
        (new Vector2(0, -0.62f), new Vector2(0, -0.22f), new Vector2(-0.62f, 0.42f), new Vector2(-0.62f, 0.02f)),
    });

    /// <summary>Marco de lado unidad: se escala a la huella de cada máquina.</summary>
    private static ArrayMesh FrameMesh()
    {
        const float o = 0.5f, i = 0.45f;
        return _frameMesh ??= Flat(new[]
        {
            (new Vector2(-o, -o), new Vector2(o, -o), new Vector2(o, -i), new Vector2(-o, -i)),
            (new Vector2(-o, i), new Vector2(o, i), new Vector2(o, o), new Vector2(-o, o)),
            (new Vector2(-o, -i), new Vector2(-i, -i), new Vector2(-i, i), new Vector2(-o, i)),
            (new Vector2(i, -i), new Vector2(o, -i), new Vector2(o, i), new Vector2(i, i)),
        });
    }

    private static ArrayMesh Flat((Vector2 a, Vector2 b, Vector2 c, Vector2 d)[] quads)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        st.SetNormal(Vector3.Up);
        void Vertex(Vector2 p) => st.AddVertex(new Vector3(p.X, 0, p.Y));
        foreach (var (a, b, c, d) in quads)
        {
            Vertex(a); Vertex(b); Vertex(c);
            Vertex(a); Vertex(c); Vertex(d);
        }
        return st.Commit();
    }
}
