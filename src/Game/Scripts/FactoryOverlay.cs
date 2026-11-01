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
            _hint.Text = Loc.T("look_chain_hint");
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
            _state.AddThemeColorOverride("font_color", color);
            _bar.AddThemeStyleboxOverride("fill", UiTheme.Box(color, new Color(0, 0, 0, 0), 0, 0, 0, 2));
        }
        // Parada sin materiales pero con producción lista: lo útil es saber que se puede recoger.
        var ready = m.Out.Total;
        var state = Loc.T("look_" + m.Status);
        if (ready > 0 && m.Status == MachineStatus.NoInput) state = $"{state} · {Loc.T("look_collect", ready.ToString())}";
        if (_state.Text != state) _state.Text = state;
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
    private static readonly Color Flow = new(1f, 0.78f, 0.1f, 1f);
    private static readonly Color Limit = new(1f, 0.26f, 0.2f, 1f);
    private static readonly Color MachineMark = new(1f, 0.74f, 0.2f, 0.75f);

    private static Shader? _shader;
    private readonly MultiMeshInstance3D _chevrons = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private readonly MultiMeshInstance3D _frames = new() { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    private double _wait;
    private string _signature = "";

    public int Seed { get; private set; } = -1;
    public bool Active => Seed >= 0;
    public ChainTrace? Current { get; private set; }
    /// <summary>Texto de lo señalado, también para el aviso al activar.</summary>
    public string Summary { get; private set; } = "";
    public int ChevronCount => _chevrons.Multimesh?.InstanceCount ?? 0;
    /// <summary>Lo que dibuja ChainTag en 2D: ahí el HUD puede reservarle sitio, cosa que un rótulo 3D no permite (INC-14).</summary>
    public bool TagShown { get; private set; }
    public Vector3 TagAnchor { get; private set; }
    public string TagText { get; private set; } = "";
    public string TagGlyph { get; private set; } = "st_wait";
    public event Action<string>? LimitChanged;

    public override void _Ready()
    {
        AddChild(_chevrons);
        AddChild(_frames);
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
        TagShown = false;
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
            TagShown = false;
            return;
        }
        var position = Vector3.Zero;
        var name = "";
        if (w.Machines.TryGetValue(limit.Machine, out var m))
        {
            var (x, z) = w.MachineCenterMm(m);
            position = new Vector3(x / 1000f, Models.Machine(m.Def).Height + 0.6f, z / 1000f);
            name = Loc.T("machine_" + m.Def);
        }
        if (limit.Link >= 0 && w.Links.TryGetValue(limit.Link, out var l) && l.Cells.Count > 0)
        {
            var (cx, cz) = l.Cells[l.Cells.Count / 2];
            position = new Vector3((cx + 0.5f) * Cell, 1.4f, (cz + 0.5f) * Cell);
            name = Loc.T("tool_link");
        }
        var cause = Loc.T("chain_cause_" + limit.Cause);
        if (limit.Item.Length > 0) cause += $" · {Loc.T("item_" + limit.Item)}";
        Summary = $"{Loc.T("chain_limit")}: {name} — {cause}";
        TagText = $"{Loc.T("chain_limit")}\n{name}\n{cause}";
        TagAnchor = position;
        TagGlyph = limit.Cause switch
        {
            ChainCause.NoPower or ChainCause.LowPower => "st_power",
            ChainCause.Overheated => "st_heat",
            ChainCause.OutputFull => "st_full",
            ChainCause.BeltSaturated => "belt",
            _ => "st_wait",
        };
        TagShown = true;
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

/// <summary>
/// Rótulo del eslabón limitante, en 2D y siempre dentro del hueco libre del HUD: entre el panel del objetivo y la barra rápida.
/// Si el eslabón queda fuera de ese hueco o a la espalda, el rótulo se queda en el borde con una punta hacia él.
/// </summary>
public partial class ChainTag : Control
{
    private const float Gap = 10;
    private const float PointerSize = 9;
    private readonly GameView _view;
    private readonly PanelContainer _box;
    private readonly TextureRect _glyph;
    private readonly Label _text;
    private string _glyphName = "";
    private Vector2 _pointerFrom;
    private Vector2 _pointerDir;

    public Rect2 TagRect => _box.Visible ? _box.GetGlobalRect() : new Rect2();
    public bool Showing => _box.Visible;
    public string Text => _text.Text;
    /// <summary>La punta solo se dibuja si el rótulo no puede estar junto al eslabón.</summary>
    public bool PointerShown { get; private set; }

    public ChainTag(GameView view)
    {
        _view = view;
        MouseFilter = MouseFilterEnum.Ignore;
        _box = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        _box.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.09f, 0.03f, 0.025f, 0.9f), new Color(1f, 0.26f, 0.2f, 0.95f), 1, 6, 10, 3));
        _glyph = Ui.Icon(Ui.Glyph("st_wait"), 28);
        _text = Ui.Text("", UiTheme.Small, new Color(1f, 0.86f, 0.78f));
        var row = Ui.Row(UiTheme.S, _glyph, _text);
        row.MouseFilter = MouseFilterEnum.Ignore;
        _box.AddChild(row);
        AddChild(_box);
    }

    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

    public override void _Process(double delta)
    {
        var chain = _view.ChainForTest;
        if (!chain.TagShown)
        {
            if (_box.Visible)
            {
                _box.Visible = false;
                PointerShown = false;
                QueueRedraw();
            }
            return;
        }
        if (_text.Text != chain.TagText) _text.Text = chain.TagText;
        if (_glyphName != chain.TagGlyph)
        {
            _glyphName = chain.TagGlyph;
            _glyph.Texture = Ui.Glyph(_glyphName);
        }
        _box.Visible = true;
        _box.ResetSize();

        var size = _box.Size;
        var safe = _view.HudSafeArea;
        var view = GetViewportRect().Size;
        var camera = _view.Camera;
        // La cámara devuelve píxeles de ventana; la interfaz puede estar escalada.
        var scale = view / ((Vector2)GetWindow().Size).Max(Vector2.One);
        var target = camera.UnprojectPosition(chain.TagAnchor) * scale;
        if (camera.IsPositionBehind(chain.TagAnchor))
        {
            // A la espalda la proyección no sirve: se señala hacia el lado por el que hay que girar.
            var side = camera.ToLocal(chain.TagAnchor).X >= 0 ? 1 : -1;
            target = view / 2 + new Vector2(side, 0.35f).Normalized() * view.Length();
        }
        var wanted = target - new Vector2(size.X / 2, size.Y + Gap);
        var at = new Vector2(
            Mathf.Clamp(wanted.X, safe.Position.X, Mathf.Max(safe.Position.X, safe.End.X - size.X)),
            Mathf.Clamp(wanted.Y, safe.Position.Y, Mathf.Max(safe.Position.Y, safe.End.Y - size.Y)));
        _box.Position = at;

        var box = new Rect2(at, size);
        var pushed = (at - wanted).Length() > 1;
        var inner = new Vector2(Mathf.Clamp(target.X, box.Position.X, box.End.X), Mathf.Clamp(target.Y, box.Position.Y, box.End.Y));
        var dir = (target - inner).LengthSquared() > 1 ? (target - inner).Normalized() : Vector2.Down;
        if (pushed != PointerShown || inner != _pointerFrom || dir != _pointerDir)
        {
            PointerShown = pushed;
            _pointerFrom = inner;
            _pointerDir = dir;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!PointerShown || !_box.Visible) return;
        var side = new Vector2(-_pointerDir.Y, _pointerDir.X);
        DrawColoredPolygon(new[] { _pointerFrom + _pointerDir * PointerSize * 1.5f, _pointerFrom + side * PointerSize, _pointerFrom - side * PointerSize }, new Color(1f, 0.26f, 0.2f, 0.95f));
    }
}
