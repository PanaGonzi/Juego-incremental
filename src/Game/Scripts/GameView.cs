using Godot;
using Pluton.Networking;
using Pluton.Simulation;

namespace Pluton.Game;

public enum Tool { Cutter, Build, Link, Remove, Rail }

/// <summary>Representa el mundo replicado y convierte la entrada del jugador en intenciones. Nunca modifica el mundo.</summary>
public partial class GameView : Node3D
{
    private const float Cell = 2f;
    private const uint PickMask = 2;

    private sealed class MachineNode
    {
        public Node3D Root = null!;
        public Node3D? Rotor;
        public Node3D? Piston;
        public Vector3 PistonRest;
        public Label3D Warn = null!;
        public string Key = "";
        public MachineStatus Status;
        public float Phase;
    }

    private sealed class RemotePlayer
    {
        public Node3D Root = null!;
        public Vector3 Target;
        public float TargetYaw;
        public double LastSeen;
    }

    public readonly GameSession Session;
    private readonly Main _main;
    private readonly Dictionary<int, MachineNode> _machines = new();
    private readonly Dictionary<int, MeshInstance3D> _debris = new();
    private readonly Dictionary<int, MeshInstance3D> _links = new();
    private readonly Dictionary<int, MeshInstance3D> _nodes = new();
    private readonly Dictionary<int, RemotePlayer> _remote = new();
    private readonly Dictionary<int, MeshInstance3D> _rails = new();

    private sealed class TrainNode
    {
        public Node3D Loco = null!;
        public Node3D Wagon = null!;
        public MeshInstance3D Cargo = null!;
        public Label3D Label = null!;
        public long Distance;
        public int LastSegment = -1;
    }

    private readonly Dictionary<int, TrainNode> _trains = new();
    private int _railStartX, _railStartZ;
    private bool _railPlacing;
    private byte _railShape;
    private MeshInstance3D? _railGhost;
    private string _railGhostKey = "";
    private readonly Dictionary<ulong, (string kind, int id)> _pickables = new();
    private readonly Dictionary<int, StaticBody3D> _debrisBodies = new();

    private Hud _hud = null!;
    private CharacterBody3D _body = null!;
    private Node3D _head = null!;
    private Camera3D _camera = null!;
    private WorldEnvironment _env = null!;
    private DirectionalLight3D _sun = null!;
    private MeshInstance3D _beam = null!;
    private GpuParticles3D _sparks = null!;
    private ShaderMaterial _ground = null!;
    private Control? _loading;
    private readonly List<AudioStreamPlayer3D> _hums = new();
    private double _humTimer;

    private float _yaw, _pitch;
    private bool _worldReady;
    private bool _worldDirty;
    private bool _paused;
    private double _moveTimer;
    private double _cutTimer;
    private float _bobPhase;
    private double _time;

    private Tool _tool = Tool.Cutter;
    private string _buildDef = "";
    private int _buildRot;
    private Node3D? _ghost;
    private string _ghostDef = "";
    private readonly StandardMaterial3D _ghostOk = GhostMat(new Color(0.3f, 1f, 0.55f, 0.42f));
    private readonly StandardMaterial3D _ghostBad = GhostMat(new Color(1f, 0.3f, 0.2f, 0.42f));
    private int _linkFrom = -1;
    private int _objectiveSeen = -1;
    private long _lastCredits;
    private (string kind, int id) _aim = ("", -1);
    private Vector3 _aimPoint;

    // Control por programa para las pruebas del cliente exportado.
    public Vector3? AutoWalkTarget;
    public bool AutoHoldPrimary;
    public bool TestCapture;
    public Tool CurrentTool => _tool;
    public bool PanelIsOpen => _hud.PanelOpen;
    public bool IsPaused => _paused;
    public float Yaw => _yaw;

    public GameView(Main main, GameSession session)
    {
        _main = main;
        Session = session;
    }

    private static StandardMaterial3D GhostMat(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    public override void _Ready()
    {
        Controls.Setup();
        BuildEnvironment();
        BuildPlayer();
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Hud(this);
        layer.AddChild(_hud);
        _hud.SetTool(0);

        _loading = new PanelContainer();
        _loading.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _loading.GrowHorizontal = Control.GrowDirection.Both;
        _loading.GrowVertical = Control.GrowDirection.Both;
        _loading.AddChild(Ui.Text(Loc.T("connecting"), 26, Ui.Amber));
        _loading.Theme = Ui.Theme;
        layer.AddChild(_loading);

        var client = Session.Client;
        client.WorldReady += OnWorldReady;
        client.DeltaApplied += () => _worldDirty = true;
        client.ResultReceived += OnResult;
        client.ChatReceived += (from, text) => { _hud.AddChat($"{from}: {text}"); };
        client.NoticeReceived += (key, arg) => _hud.AddChat(Loc.T(key, arg));
        client.PositionCorrected += (x, y, z) => _body.GlobalPosition = new Vector3(x, y, z);
        client.RemoteMoved += OnRemoteMoved;
        for (var i = 0; i < 6; i++)
        {
            var hum = new AudioStreamPlayer3D { Stream = Sfx.Hum, MaxDistance = 28, UnitSize = 5, VolumeDb = Sfx.MachineDb, Autoplay = false };
            AddChild(hum);
            _hums.Add(hum);
        }
    }

    // ---------- Escena ----------

    private void BuildEnvironment()
    {
        var sky = new ShaderMaterial { Shader = new Shader { Code = SkyShader } };
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = sky, RadianceSize = Sky.RadianceSizeEnum.Size64 },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 1.0f,
            TonemapMode = Godot.Environment.ToneMapper.Agx,
            TonemapExposure = 1.1f,
            SsaoEnabled = true,
            SsaoRadius = 1.5f,
            SsaoIntensity = 2f,
            GlowEnabled = _main.Prefs.Glow,
            GlowIntensity = 0.7f,
            GlowBloom = 0.05f,
            FogEnabled = true,
            FogLightColor = new Color(0.22f, 0.28f, 0.42f),
            FogDensity = 0.0022f,
            FogSkyAffect = 0f,
        };
        _env = new WorldEnvironment { Environment = env };
        AddChild(_env);

        _sun = new DirectionalLight3D
        {
            LightColor = new Color(0.86f, 0.9f, 1f),
            LightEnergy = 1.25f,
            ShadowEnabled = _main.Prefs.Shadows,
            DirectionalShadowMaxDistance = 130,
            DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        };
        _sun.RotationDegrees = new Vector3(-28, -55, 0);
        AddChild(_sun);

        BuildTerrain();
        BuildScenery();

        var sparksMat = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0),
            Spread = 70,
            InitialVelocityMin = 2,
            InitialVelocityMax = 6,
            Gravity = new Vector3(0, -3, 0),
            ScaleMin = 0.4f,
            ScaleMax = 1f,
        };
        _sparks = new GpuParticles3D
        {
            Amount = 40,
            Lifetime = 0.7f,
            Emitting = false,
            ProcessMaterial = sparksMat,
            DrawPass1 = new BoxMesh { Size = new Vector3(0.035f, 0.035f, 0.035f), Material = Models.Mat("amber") },
        };
        AddChild(_sparks);
    }

    private void BuildTerrain()
    {
        const int size = 180;
        const float span = 1400f;
        var noise = new FastNoiseLite { Seed = 9, Frequency = 0.0035f, FractalOctaves = 4 };
        var half = 200f;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float Height(float x, float z)
        {
            var d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            var t = Mathf.Clamp((d - half) / 260f, 0, 1);
            return (noise.GetNoise2D(x, z) * 0.5f + 0.5f) * 95f * t * t;
        }
        var step = span / size;
        for (var ix = 0; ix < size; ix++)
        for (var iz = 0; iz < size; iz++)
        {
            var x0 = -span / 2 + ix * step;
            var z0 = -span / 2 + iz * step;
            var a = new Vector3(x0, Height(x0, z0), z0);
            var b = new Vector3(x0 + step, Height(x0 + step, z0), z0);
            var c = new Vector3(x0, Height(x0, z0 + step), z0 + step);
            var d = new Vector3(x0 + step, Height(x0 + step, z0 + step), z0 + step);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(b); st.AddVertex(d); st.AddVertex(c);
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        _ground = new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
        _ground.SetShaderParameter("grid_strength", 0f);
        var terrain = new MeshInstance3D { Mesh = mesh, MaterialOverride = _ground };
        AddChild(terrain);

        var floor = new StaticBody3D();
        floor.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(Vector3.Up, 0) } });
        AddChild(floor);
        foreach (var (normal, distance) in new[] { (Vector3.Right, -188f), (Vector3.Left, -188f), (Vector3.Forward, -188f), (Vector3.Back, -188f) })
        {
            var wall = new StaticBody3D();
            wall.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(normal, distance) } });
            AddChild(wall);
        }
    }

    /// <summary>Decoración sin colisión: guijarros instanciados y ruinas industriales fuera de la zona edificable.</summary>
    private void BuildScenery()
    {
        var rng = new RandomNumberGenerator { Seed = 1907 };
        var rock = new Models.Builder().Ball("rock", 0, 0, 0, 1f, 6, 4, 0.6f).Commit();
        const int count = 2600;
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = rock, InstanceCount = count };
        for (var i = 0; i < count; i++)
        {
            var s = Mathf.Pow(rng.Randf(), 4f) * 0.26f + 0.04f;
            var basis = Basis.FromEuler(new Vector3(rng.RandfRange(-0.4f, 0.4f), rng.RandfRange(0, 6.28f), rng.RandfRange(-0.4f, 0.4f))).Scaled(new Vector3(s, s, s));
            multi.SetInstanceTransform(i, new Transform3D(basis, new Vector3(rng.RandfRange(-195, 195), s * 0.15f, rng.RandfRange(-195, 195))));
        }
        AddChild(new MultiMeshInstance3D { Multimesh = multi, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        var ruin = new Models.Builder();
        ruin.Cyl("rustlight", 0, 14, 0, 7, 28, sides: 14);
        ruin.Box("rust", 0, 29, 0, 16, 2, 16, roll: 6);
        ruin.Box("hull", 14, 7, 0, 16, 14, 11);
        ruin.Box("rust", 14, 15, 0, 18, 1.2f, 13, pitch: -5);
        ruin.Box("dark", 26, 19, 3, 2, 38, 2, roll: -7);
        ruin.Box("dark", 8, 22, 5, 32, 1, 1, roll: 18);
        var ruinMesh = ruin.Commit();
        foreach (var (x, z, yaw) in new[] { (-235f, -180f, 20f), (240f, -120f, 130f), (250f, 170f, 250f), (-110f, 245f, 80f), (90f, -255f, 310f), (-260f, 90f, 175f) })
            AddChild(new MeshInstance3D { Mesh = ruinMesh, Position = new Vector3(x, -1, z), RotationDegrees = new Vector3(0, yaw, 0) });
    }

    private const string GroundShader = """
        shader_type spatial;
        render_mode cull_back;
        uniform float grid_strength = 0.0;
        varying vec3 wp;
        const mat2 ROT = mat2(vec2(0.8,0.6),vec2(-0.6,0.8));
        vec2 grad(vec2 i){ vec3 p3 = fract(vec3(i.xyx)*vec3(0.1031,0.1030,0.0973)); p3 += dot(p3,p3.yzx+33.33); float a = fract((p3.x+p3.y)*p3.z)*6.2831853; return vec2(cos(a),sin(a)); }
        float gnoise(vec2 p){ vec2 i=floor(p); vec2 f=fract(p); vec2 u=f*f*f*(f*(f*6.0-15.0)+10.0);
          return mix(mix(dot(grad(i),f),dot(grad(i+vec2(1,0)),f-vec2(1,0)),u.x),
                     mix(dot(grad(i+vec2(0,1)),f-vec2(0,1)),dot(grad(i+vec2(1,1)),f-vec2(1,1)),u.x),u.y)*0.7+0.5; }
        float fbm(vec2 p, int n){ float v=0.0; float a=0.5; for(int i=0;i<n;i++){ v+=a*gnoise(p); p=ROT*p*2.03+vec2(11.3,7.1); a*=0.5; } return v/(1.0-pow(0.5,float(n))); }
        void vertex(){ wp = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz; }
        void fragment(){
          float big = fbm(wp.xz*0.016,4);
          float mid = fbm(wp.xz*0.11,4);
          float fine = fbm(wp.xz*1.3,3);
          vec3 tholin = vec3(0.115,0.075,0.062);
          vec3 frost = vec3(0.50,0.54,0.61);
          float ice = smoothstep(0.46,0.60, big*0.58+mid*0.42);
          vec3 c = mix(tholin, frost, ice);
          c *= 0.88 + (fine-0.5)*0.45;
          ALBEDO = c;
          ROUGHNESS = mix(0.95,0.5,ice);
          SPECULAR = 0.3;
          vec2 g = abs(fract(wp.xz/2.0+0.5)-0.5)*2.0;
          float line = 1.0-smoothstep(0.0,0.05,min(g.x,g.y));
          float area = 1.0-smoothstep(170.0,182.0,max(abs(wp.x),abs(wp.z)));
          EMISSION = vec3(0.25,0.55,0.8)*line*grid_strength*area*0.5;
        }
        """;

    private const string SkyShader = """
        shader_type sky;
        float hash(vec3 p){ return fract(sin(dot(p, vec3(127.1,311.7,74.7))) * 43758.5453); }
        float hash2(vec2 p){ return fract(sin(dot(p, vec2(41.3,289.1))) * 43758.5453); }
        float vnoise(vec2 p){ vec2 i=floor(p); vec2 f=fract(p); f=f*f*(3.0-2.0*f);
          return mix(mix(hash2(i),hash2(i+vec2(1,0)),f.x),mix(hash2(i+vec2(0,1)),hash2(i+vec2(1,1)),f.x),f.y); }
        float stars(vec3 d, float scale, float density){
          vec3 g = d*scale; vec3 cell = floor(g); vec3 f = fract(g)-0.5;
          float h = hash(cell);
          if (h < density) return 0.0;
          vec3 off = vec3(hash(cell+3.1),hash(cell+5.7),hash(cell+7.3))-0.5;
          float dist = length(f-off*0.7);
          return smoothstep(0.09,0.0,dist)*(0.35+hash(cell+1.0)*hash(cell+1.0)*2.4);
        }
        void sky(){
          vec3 d = normalize(EYEDIR);
          vec3 c = mix(vec3(0.0016,0.0022,0.0052), vec3(0.012,0.016,0.034), pow(1.0-clamp(d.y,0.0,1.0),9.0));
          float band = pow(1.0-abs(dot(d,normalize(vec3(0.3,0.55,-0.75)))),7.0);
          c += vec3(0.02,0.018,0.034)*band;
          c += vec3(0.85,0.92,1.0)*(stars(d,170.0,0.975)+stars(d,330.0,0.986)*0.5)*(1.0-pow(1.0-clamp(d.y+0.02,0.0,1.0),12.0));
          vec3 sunDir = normalize(vec3(0.46,0.47,0.75));
          float sun = max(dot(d, sunDir),0.0);
          c += vec3(1.0,0.97,0.9)*(pow(sun,14000.0)*40.0 + pow(sun,900.0)*0.22);
          // Caronte: disco con terminador coherente con el Sol.
          vec3 mc = normalize(vec3(-0.34,0.34,-0.88)); float mr = 0.085;
          float cosd = dot(d,mc);
          if (cosd > 0.0){
            vec3 u = normalize(cross(vec3(0,1,0),mc)); vec3 v = cross(mc,u);
            vec2 q = vec2(dot(d,u),dot(d,v))/mr; float r2 = dot(q,q);
            if (r2 < 1.0){
              vec3 n = normalize(q.x*u + q.y*v - sqrt(1.0-r2)*mc);
              float lit = max(dot(n,sunDir),0.0);
              float t = vnoise(q*5.0+7.0)*0.6+vnoise(q*13.0)*0.4;
              vec3 alb = mix(vec3(0.20,0.18,0.17), vec3(0.46,0.44,0.43), t);
              float cap = smoothstep(0.55,0.95,q.y+t*0.25);
              alb = mix(alb, vec3(0.36,0.16,0.11), cap);
              c = alb*(lit*0.75+0.004);
            }
          }
          COLOR = c;
        }
        """;

    private void BuildPlayer()
    {
        _body = new CharacterBody3D { CollisionLayer = 4, CollisionMask = 1 };
        _body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.75f }, Position = new Vector3(0, 0.875f, 0) });
        _head = new Node3D { Position = new Vector3(0, 1.62f, 0) };
        _body.AddChild(_head);
        _camera = new Camera3D { Fov = _main.Prefs.Fov, Far = 3000, Near = 0.08f, Current = true };
        _head.AddChild(_camera);
        var lamp = new SpotLight3D { LightColor = new Color(1f, 0.93f, 0.8f), LightEnergy = 0.55f, SpotRange = 26, SpotAngle = 38, Position = new Vector3(0.25f, -0.2f, 0) };
        _camera.AddChild(lamp);
        AddChild(_body);

        _beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 1, RadialSegments = 6 },
            MaterialOverride = Models.Mat("amber"),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_beam);
    }

    private void OnWorldReady()
    {
        _worldReady = true;
        _loading?.QueueFree();
        _loading = null;
        var me = Session.Client.LocalPlayer!;
        _body.GlobalPosition = new Vector3(me.X, Mathf.Max(me.Y, 0.2f), me.Z);
        _yaw = me.Yaw;
        _head.Rotation = Vector3.Zero;
        _body.Rotation = new Vector3(0, _yaw, 0);
        _worldDirty = true;
        _lastCredits = Session.Client.World!.Credits;
        _objectiveSeen = Session.Client.World.ObjectiveIndex;
        UpdateMouseMode();
    }

    // ---------- Sincronización de representación ----------

    private void SyncWorld()
    {
        var w = Session.Client.World!;
        var seen = new HashSet<int>();
        foreach (var m in w.Machines.Values)
        {
            seen.Add(m.Id);
            var key = $"{m.Def}|{m.X}|{m.Z}|{m.Rot}";
            if (!_machines.TryGetValue(m.Id, out var node) || node.Key != key)
            {
                if (node != null) FreeMachine(m.Id);
                node = CreateMachine(m, key);
                _machines[m.Id] = node;
            }
            if (node.Status != m.Status)
            {
                node.Status = m.Status;
                var warn = m.Status switch
                {
                    MachineStatus.NoPower => "⚡",
                    MachineStatus.LowPower => "⚡",
                    MachineStatus.OutputFull => "▣",
                    MachineStatus.NoInput => "…",
                    _ => "",
                };
                node.Warn.Text = warn;
                node.Warn.Modulate = m.Status is MachineStatus.NoPower or MachineStatus.LowPower ? Ui.Bad : Ui.Amber;
            }
        }
        foreach (var id in _machines.Keys.Where(k => !seen.Contains(k)).ToList()) FreeMachine(id);

        seen.Clear();
        foreach (var d in w.DebrisById.Values)
        {
            seen.Add(d.Id);
            if (!_debris.TryGetValue(d.Id, out var mesh))
            {
                mesh = CreateDebris(d);
                _debris[d.Id] = mesh;
            }
            var def = w.Content.Balance.Debris[d.Def];
            var s = 0.45f + 0.55f * d.Hp / def.Hp;
            mesh.Scale = new Vector3(s, s, s);
        }
        foreach (var id in _debris.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var mesh = _debris[id];
            BreakEffect(mesh.GlobalPosition);
            mesh.QueueFree();
            _debris.Remove(id);
            if (_debrisBodies.Remove(id, out var body))
            {
                _pickables.Remove(body.GetInstanceId());
                body.QueueFree();
            }
        }

        foreach (var n in w.Nodes.Values)
        {
            if (!n.Revealed || _nodes.ContainsKey(n.Id)) continue;
            var mesh = new MeshInstance3D { Mesh = Models.NodeMesh(n.Item), Position = new Vector3((n.X + 1) * Cell, 0, (n.Z + 1) * Cell) };
            AddChild(mesh);
            _nodes[n.Id] = mesh;
        }

        seen.Clear();
        foreach (var l in w.Links.Values)
        {
            seen.Add(l.Id);
            if (!w.Machines.TryGetValue(l.From, out var a) || !w.Machines.TryGetValue(l.To, out var b)) continue;
            if (!_links.TryGetValue(l.Id, out var mesh))
            {
                mesh = CreateLink();
                _links[l.Id] = mesh;
            }
            PlaceLink(mesh, CenterOf(w, a) + Vector3.Up * 2.35f, CenterOf(w, b) + Vector3.Up * 2.35f);
        }
        foreach (var id in _links.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _links[id].QueueFree();
            _links.Remove(id);
        }

        seen.Clear();
        foreach (var s in w.RailSegments.Values)
        {
            seen.Add(s.Id);
            if (_rails.ContainsKey(s.Id) || !w.RailNodes.TryGetValue(s.A, out var ra) || !w.RailNodes.TryGetValue(s.B, out var rb)) continue;
            var mesh = new MeshInstance3D { Mesh = Models.RailMesh(t => RailPoint(ra, rb, s.Shape, t), s.LengthMm / 1000f) };
            AddChild(mesh);
            _rails[s.Id] = mesh;
        }
        foreach (var id in _rails.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _rails[id].QueueFree();
            _rails.Remove(id);
        }

        seen.Clear();
        foreach (var t in w.Trains.Values)
        {
            seen.Add(t.Id);
            if (!_trains.ContainsKey(t.Id)) _trains[t.Id] = CreateTrainNode();
        }
        foreach (var id in _trains.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _trains[id].Loco.QueueFree();
            _trains[id].Wagon.QueueFree();
            _trains.Remove(id);
        }

        if (w.ObjectiveIndex != _objectiveSeen)
        {
            _objectiveSeen = w.ObjectiveIndex;
            Sfx.Play("objective");
        }
        if (w.Credits > _lastCredits + 5) Sfx.Play("sell");
        _lastCredits = w.Credits;
    }

    private static Vector3 RailPoint(RailNode a, RailNode b, byte shape, double t)
    {
        var (x, z) = RailMath.Eval(a, b, shape, t);
        return new Vector3((float)x * Cell, 0, (float)z * Cell);
    }

    private TrainNode CreateTrainNode()
    {
        var node = new TrainNode
        {
            Loco = new Node3D(),
            Wagon = new Node3D(),
            Cargo = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.0f, 1f, 5.7f), Material = Models.Mat("rust") } },
            Label = new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 36, PixelSize = 0.007f, OutlineSize = 8, Position = new Vector3(0, 4.3f, 0), VisibilityRangeEnd = 60 },
        };
        node.Loco.AddChild(new MeshInstance3D { Mesh = Models.Locomotive() });
        node.Loco.AddChild(node.Label);
        node.Wagon.AddChild(new MeshInstance3D { Mesh = Models.Wagon() });
        node.Wagon.AddChild(node.Cargo);
        node.Cargo.Position = new Vector3(0, 0.8f, 0);
        AddChild(node.Loco);
        AddChild(node.Wagon);
        return node;
    }

    /// <summary>Sitúa el tren por distancia recorrida en su ruta. El vagón va 7,4 m por detrás de la locomotora.</summary>
    private void UpdateTrains(double delta)
    {
        var w = Session.Client.World!;
        var speed = w.Content.Balance.Rail.TrainSpeedMmPerTick * (double)w.Content.Balance.TicksPerSecond;
        foreach (var (id, node) in _trains)
        {
            if (!w.Trains.TryGetValue(id, out var t)) continue;
            // Entre actualizaciones del servidor se extrapola; la siguiente corrige.
            if (t.State == TrainState.Moving) t.PosMm += (long)(speed * delta);
            if (!TryTrainPose(w, t, 0, out var pos, out var dir)) continue;
            node.Loco.GlobalPosition = pos;
            node.Loco.LookAt(pos - dir, Vector3.Up);
            if (TryTrainPose(w, t, -7400, out var wagonPos, out var wagonDir))
            {
                node.Wagon.GlobalPosition = wagonPos;
                node.Wagon.LookAt(wagonPos - wagonDir, Vector3.Up);
            }
            var fill = Mathf.Clamp(t.Cargo.Total / (float)w.Content.Balance.Rail.TrainCapacity, 0, 1);
            node.Cargo.Visible = fill > 0;
            node.Cargo.Scale = new Vector3(1, Mathf.Max(0.05f, fill) * 1.2f, 1);
            var reason = t.WaitReason.Length > 0 ? $": {Loc.T(t.WaitReason)}" : "";
            node.Label.Text = $"{Loc.T("train_state_" + t.State)}{reason}\n{t.Cargo.Total}/{w.Content.Balance.Rail.TrainCapacity}";
        }
    }

    private static bool TryTrainPose(World w, Train t, long offsetMm, out Vector3 pos, out Vector3 dir)
    {
        pos = Vector3.Zero;
        dir = Vector3.Forward;
        var index = Math.Min(t.PathIndex, t.Path.Count - 1);
        var along = t.PosMm + offsetMm;
        if (t.State != TrainState.Moving || t.Path.Count == 0)
        {
            // Parado: se apoya en el último tramo recorrido, o en cualquiera del nodo.
            if (t.Path.Count == 0 || t.PathIndex == 0 && t.State == TrainState.Waiting)
            {
                var near = w.RailSegments.Values.FirstOrDefault(s => s.A == t.CurrentNode || s.B == t.CurrentNode);
                if (near == null) return false;
                var forward = near.A == t.CurrentNode;
                return Pose(w, near, forward, Math.Min(near.LengthMm, 7400 + Math.Abs(offsetMm)) - Math.Abs(offsetMm), out pos, out dir);
            }
            index = t.Path.Count - 1;
            if (!w.RailSegments.TryGetValue(t.Path[index], out var last)) return false;
            along = last.LengthMm + offsetMm;
        }

        if (!w.RailSegments.TryGetValue(t.Path[index], out var seg)) return false;
        // Recorre hacia atrás los tramos anteriores si el desplazamiento se sale del actual.
        while (along < 0 && index > 0)
        {
            index--;
            if (!w.RailSegments.TryGetValue(t.Path[index], out seg!)) return false;
            along += seg.LengthMm;
        }
        along = Math.Clamp(along, 0, seg.LengthMm);
        var isForward = SegmentForward(w, t, index);
        return Pose(w, seg, isForward, along, out pos, out dir);
    }

    private static bool SegmentForward(World w, Train t, int index)
    {
        // Reconstruye el sentido del tramo desde el nodo de origen de la ruta.
        var end = t.CurrentNode;
        var reached = t.State == TrainState.Moving ? t.PathIndex : t.Path.Count;
        for (var i = reached - 1; i >= index; i--)
        {
            if (!w.RailSegments.TryGetValue(t.Path[i], out var s)) return true;
            end = s.A == end ? s.B : s.A;
        }
        return w.RailSegments.TryGetValue(t.Path[index], out var seg) && seg.A == end;
    }

    private static bool Pose(World w, RailSegment s, bool forward, long along, out Vector3 pos, out Vector3 dir)
    {
        pos = Vector3.Zero;
        dir = Vector3.Forward;
        if (!w.RailNodes.TryGetValue(s.A, out var a) || !w.RailNodes.TryGetValue(s.B, out var b) || s.LengthMm == 0) return false;
        var frac = (double)along / s.LengthMm;
        var t = forward ? frac : 1 - frac;
        pos = RailPoint(a, b, s.Shape, t);
        var ahead = RailPoint(a, b, s.Shape, Math.Clamp(t + (forward ? 0.01 : -0.01), 0, 1));
        var back = RailPoint(a, b, s.Shape, Math.Clamp(t - (forward ? 0.01 : -0.01), 0, 1));
        var d = ahead - back;
        if (d.LengthSquared() > 0.00001f) dir = d.Normalized();
        return true;
    }

    private static Vector3 CenterOf(World w, Machine m)
    {
        var (x, z) = w.MachineCenterMm(m);
        return new Vector3(x / 1000f, 0, z / 1000f);
    }

    private MachineNode CreateMachine(Machine m, string key)
    {
        var w = Session.Client.World!;
        var model = Models.Machine(m.Def);
        var def = w.Content.Machines[m.Def];
        var root = new Node3D { Position = CenterOf(w, m), RotationDegrees = new Vector3(0, m.Rot * 90, 0) };
        root.AddChild(new MeshInstance3D { Mesh = model.Body });
        var node = new MachineNode { Root = root, Key = key, Status = MachineStatus.Idle, Phase = GD.Randf() * 6f };

        if (model.Rotor != null)
        {
            node.Rotor = new MeshInstance3D { Mesh = model.Rotor, Position = model.RotorPos };
            root.AddChild(node.Rotor);
        }
        if (model.Piston != null)
        {
            node.Piston = new MeshInstance3D { Mesh = model.Piston, Position = model.PistonPos };
            node.PistonRest = model.PistonPos;
            root.AddChild(node.Piston);
        }

        var body = new StaticBody3D { CollisionLayer = 1 | PickMask, CollisionMask = 0 };
        var shape = new BoxShape3D { Size = new Vector3(def.W * Cell - 0.2f, model.Height, def.D * Cell - 0.2f) };
        body.AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(0, model.Height / 2, 0) });
        root.AddChild(body);
        _pickables[body.GetInstanceId()] = ("machine", m.Id);

        node.Warn = new Label3D
        {
            Text = "",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 96,
            PixelSize = 0.012f,
            Position = new Vector3(0, model.Height + 0.7f, 0),
            OutlineSize = 16,
            VisibilityRangeEnd = 45,
            NoDepthTest = false,
        };
        root.AddChild(node.Warn);
        AddChild(root);
        return node;
    }

    private void FreeMachine(int id)
    {
        if (!_machines.Remove(id, out var node)) return;
        _pickables.Remove(((StaticBody3D)node.Root.GetChildren().First(c => c is StaticBody3D)).GetInstanceId());
        node.Root.QueueFree();
    }

    private MeshInstance3D CreateDebris(Debris d)
    {
        var pos = new Vector3((d.X + 0.5f) * Cell, 0, (d.Z + 0.5f) * Cell);
        var mesh = new MeshInstance3D
        {
            Mesh = Models.Debris(d.Def, d.Id % 3),
            Position = pos,
            RotationDegrees = new Vector3(0, d.Def == "ice_crust" ? 0 : d.Id * 37 % 360, 0),
            VisibilityRangeEnd = 220,
        };
        AddChild(mesh);
        var body = new StaticBody3D { CollisionLayer = PickMask, CollisionMask = 0, Position = pos };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.9f, 1.4f, 1.9f) }, Position = new Vector3(0, 0.7f, 0) });
        // Núcleo sólido menor que la zona de apuntado: impide meterse dentro sin atascar al caminar.
        var solid = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        solid.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = d.Def == "ice_crust" ? 0.95f : 0.7f, Height = 1.1f }, Position = new Vector3(0, 0.55f, 0) });
        body.AddChild(solid);
        AddChild(body);
        _pickables[body.GetInstanceId()] = ("debris", d.Id);
        _debrisBodies[d.Id] = body;
        return mesh;
    }

    private void BreakEffect(Vector3 at)
    {
        _sparks.GlobalPosition = at + Vector3.Up * 0.6f;
        _sparks.Restart();
        _sparks.Emitting = true;
        Sfx.Play("break", 0.1f);
    }

    private static readonly ShaderMaterial LinkMaterial = new() { Shader = new Shader { Code = """
        shader_type spatial;
        render_mode unshaded;
        void fragment(){
          float t = fract(UV.y*14.0 - TIME*1.8);
          float pulse = smoothstep(0.0,0.06,t)*(1.0-smoothstep(0.1,0.2,t));
          ALBEDO = mix(vec3(0.05,0.045,0.04), vec3(1.0,0.6,0.15), pulse);
          EMISSION = vec3(1.0,0.55,0.12)*pulse*2.5;
        }
        """ } };

    private MeshInstance3D CreateLink()
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.055f, Height = 1, RadialSegments = 8, Rings = 1 },
            MaterialOverride = LinkMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mesh);
        return mesh;
    }

    private static void PlaceLink(MeshInstance3D mesh, Vector3 a, Vector3 b)
    {
        var mid = (a + b) / 2;
        var dir = b - a;
        var length = Mathf.Max(0.01f, dir.Length());
        var y = dir / length;
        var x = y.Cross(Mathf.Abs(y.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(y);
        mesh.GlobalTransform = new Transform3D(new Basis(x, y * length, z), mid);
    }

    // ---------- Entrada ----------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_worldReady) return;
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            var s = 0.0022f * _main.Prefs.Sensitivity;
            _yaw -= motion.Relative.X * s;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * s, -1.5f, 1.5f);
            return;
        }
        if (e.IsActionPressed("pause"))
        {
            if (_hud.ChatOpen) _hud.CloseChat();
            else if (_hud.PanelOpen && !_paused) _hud.ClosePanel();
            else TogglePause();
            return;
        }
        if (_hud.ChatOpen || _paused) return;

        if (e.IsActionPressed("chat")) { _hud.OpenChat(); GetViewport().SetInputAsHandled(); return; }
        if (e.IsActionPressed("tech"))
        {
            if (_hud.PanelOpen) _hud.ClosePanel(); else _hud.OpenTech();
            return;
        }
        if (_hud.PanelOpen) return;

        if (e.IsActionPressed("tool_cutter")) SetTool(Tool.Cutter);
        else if (e.IsActionPressed("tool_build")) OpenBuildMenu();
        else if (e.IsActionPressed("tool_link")) SetTool(Tool.Link);
        else if (e.IsActionPressed("tool_remove")) SetTool(Tool.Remove);
        else if (e.IsActionPressed("tool_rail")) SetTool(Tool.Rail);
        else if (e.IsActionPressed("rotate"))
        {
            if (_tool == Tool.Rail) _railShape = (byte)((_railShape + 1) % 3);
            else _buildRot = (_buildRot + 1) % 4;
        }
        else if (e.IsActionPressed("cancel")) { SetTool(Tool.Cutter); _linkFrom = -1; }
        else if (e.IsActionPressed("interact_insert")) AimedMachine(CommandType.Insert);
        else if (e.IsActionPressed("interact_take")) AimedMachine(CommandType.Extract);
        else if (e.IsActionPressed("interact_panel"))
        {
            if (_aim.kind == "machine") _hud.OpenMachine(_aim.id);
        }
        else if (e.IsActionPressed("primary"))
        {
            if (_tool != Tool.Cutter) Primary();
        }
        else if (e.IsActionPressed("secondary"))
        {
            if (_tool == Tool.Link) _linkFrom = -1;
            if (_tool == Tool.Rail) _railPlacing = false;
        }
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (Session.IsSolo && Session.Runner != null) Session.Runner.Server.Paused = _paused;
        if (_paused)
        {
            _hud.OpenPause(Session, TogglePause, () =>
            {
                Session.Client.RequestSave();
                _hud.Toast(Loc.T("saved_ok"), Ui.Good);
            }, () => _main.EndSession(""), _main.Prefs, () => _hud.OpenSettings(_main.Prefs, () => { _paused = false; TogglePause(); ApplyPrefs(); }));
        }
        else
        {
            _hud.ClosePanel();
            ApplyPrefs();
        }
        UpdateMouseMode();
    }

    private void ApplyPrefs()
    {
        _camera.Fov = _main.Prefs.Fov;
        _sun.ShadowEnabled = _main.Prefs.Shadows;
        _env.Environment.GlowEnabled = _main.Prefs.Glow;
    }

    public void UpdateMouseMode()
    {
        var free = _paused || _hud.PanelOpen || _hud.ChatOpen || !_worldReady;
        Input.MouseMode = _main.Auto != null && !TestCapture ? Input.MouseModeEnum.Visible : free ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    public void SetTool(Tool t)
    {
        _tool = t;
        _linkFrom = -1;
        _hud.SetTool((int)t);
        _railPlacing = false;
        _ground.SetShaderParameter("grid_strength", t is Tool.Build or Tool.Rail ? 1f : 0f);
        if (t != Tool.Build) RemoveGhost();
        if (t != Tool.Rail) RemoveRailGhost();
    }

    private void OpenBuildMenu()
    {
        _hud.OpenBuild(def =>
        {
            SelectBuild(def);
            _hud.ClosePanel();
        });
    }

    public void SelectBuild(string def)
    {
        _buildDef = def;
        SetTool(Tool.Build);
    }

    private void AimedMachine(CommandType type)
    {
        if (_aim.kind != "machine") return;
        Session.Client.Send(new Command { Type = type, A = _aim.id });
        Sfx.Play(type == CommandType.Insert ? "deposit" : "take");
    }

    /// <summary>Acción principal de la herramienta activa sobre lo que apunta la cámara.</summary>
    public void Primary()
    {
        var client = Session.Client;
        switch (_tool)
        {
            case Tool.Cutter:
                if (_aim.kind == "debris")
                {
                    client.Send(new Command { Type = CommandType.CutDebris, A = _aim.id });
                    Sfx.Play("cut", 0.12f);
                }
                break;
            case Tool.Build:
                if (TryGhostCell(out var x, out var z))
                {
                    client.Send(new Command { Type = CommandType.Place, Text = _buildDef, A = x, B = z, C = _buildRot });
                    Sfx.Play("build");
                }
                break;
            case Tool.Link:
                if (_aim.kind != "machine") break;
                if (_linkFrom < 0) _linkFrom = _aim.id;
                else
                {
                    client.Send(new Command { Type = CommandType.Link, A = _linkFrom, B = _aim.id });
                    _linkFrom = -1;
                    Sfx.Play("deposit");
                }
                break;
            case Tool.Remove:
                if (_aim.kind == "machine")
                {
                    client.Send(new Command { Type = CommandType.Remove, A = _aim.id });
                    Sfx.Play("remove");
                }
                else if (NearestRail() is { } rail)
                {
                    client.Send(new Command { Type = CommandType.RemoveRail, A = rail });
                    Sfx.Play("remove");
                }
                break;
            case Tool.Rail:
                if (!TryGroundPoint(out var ground)) break;
                var (gx, gz) = RailSnap(ground);
                if (!_railPlacing)
                {
                    _railStartX = gx;
                    _railStartZ = gz;
                    _railPlacing = true;
                }
                else
                {
                    client.Send(new Command { Type = CommandType.PlaceRail, A = _railStartX, B = _railStartZ, C = gx, D = gz, Amount = _railShape });
                    Sfx.Play("build");
                    _railStartX = gx;
                    _railStartZ = gz;
                }
                break;
        }
    }

    private void OnResult(uint id, CommandResult result)
    {
        if (result.Ok) return;
        if (result.Error == "err_cooldown") return;
        _hud.Toast(Loc.T(result.Error));
        Sfx.Play("error");
    }

    private void OnRemoteMoved(int num, float x, float y, float z, float yaw)
    {
        if (!_remote.TryGetValue(num, out var r))
        {
            var root = new Node3D();
            root.AddChild(new MeshInstance3D { Mesh = Models.Astronaut() });
            var w = Session.Client.World;
            var name = w?.Players.Values.FirstOrDefault(p => p.Num == num)?.Name ?? "?";
            root.AddChild(new Label3D { Text = name, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.15f, 0), PixelSize = 0.008f, FontSize = 48, OutlineSize = 10 });
            AddChild(root);
            root.GlobalPosition = new Vector3(x, y, z);
            r = new RemotePlayer { Root = root };
            _remote[num] = r;
        }
        r.Target = new Vector3(x, y, z);
        r.TargetYaw = yaw;
        r.LastSeen = _time;
    }

    // ---------- Bucle ----------

    public override void _Process(double delta)
    {
        _time += delta;
        var client = Session.Client;
        client.Poll();
        if (client.State == ClientState.Closed)
        {
            _main.EndSession(client.CloseReason == "reject_left" ? "" : Loc.T(client.CloseReason));
            return;
        }
        if (!_worldReady) return;
        if (_worldDirty)
        {
            _worldDirty = false;
            SyncWorld();
        }

        _head.Rotation = new Vector3(_pitch, 0, 0);
        _body.Rotation = new Vector3(0, _yaw, 0);

        AnimateMachines((float)delta);
        UpdateTrains(delta);
        UpdateRemote(delta);
        UpdateAudio(delta);
        UpdateTool(delta);

        _moveTimer -= delta;
        if (_moveTimer <= 0)
        {
            _moveTimer = 0.05;
            var p = _body.GlobalPosition;
            client.SendMove(p.X, p.Y, p.Z, _yaw);
            var me = client.LocalPlayer;
            if (me != null) { me.X = p.X; me.Y = p.Y; me.Z = p.Z; }
        }
    }

    private void AnimateMachines(float delta)
    {
        var t = (float)_time;
        foreach (var node in _machines.Values)
        {
            var running = node.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.Generating or MachineStatus.Storing;
            if (node.Rotor != null && running) node.Rotor.RotateY(delta * 2.6f);
            if (node.Piston != null)
            {
                var offset = node.Status is MachineStatus.Working or MachineStatus.LowPower ? (Mathf.Sin(t * 3.2f + node.Phase) + 1f) * 0.3f : 0.6f;
                node.Piston.Position = node.PistonRest + new Vector3(0, offset - 0.3f, 0);
            }
        }
    }

    private void UpdateRemote(double delta)
    {
        foreach (var (num, r) in _remote.ToList())
        {
            if (_time - r.LastSeen > 3)
            {
                r.Root.QueueFree();
                _remote.Remove(num);
                continue;
            }
            // Interpolación exponencial entre muestras de 10 Hz.
            var k = 1f - Mathf.Exp((float)-delta * 12f);
            r.Root.GlobalPosition = r.Root.GlobalPosition.Lerp(r.Target, k);
            r.Root.Rotation = new Vector3(0, Mathf.LerpAngle(r.Root.Rotation.Y, r.TargetYaw + Mathf.Pi, k), 0);
        }
    }

    private void UpdateAudio(double delta)
    {
        _humTimer -= delta;
        if (_humTimer > 0) return;
        _humTimer = 0.5;
        var pos = _body.GlobalPosition;
        var nearest = _machines.Values
            .Where(n => n.Status is MachineStatus.Working or MachineStatus.LowPower)
            .OrderBy(n => n.Root.GlobalPosition.DistanceSquaredTo(pos))
            .Take(_hums.Count)
            .ToList();
        for (var i = 0; i < _hums.Count; i++)
        {
            if (i < nearest.Count)
            {
                _hums[i].GlobalPosition = nearest[i].Root.GlobalPosition + Vector3.Up;
                _hums[i].VolumeDb = Sfx.MachineDb;
                if (!_hums[i].Playing) _hums[i].Play();
            }
            else if (_hums[i].Playing)
            {
                _hums[i].Stop();
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_worldReady) return;
        var dt = (float)delta;
        var dir = Vector3.Zero;
        var typing = _hud.ChatOpen || _hud.PanelOpen || _paused;
        if (AutoWalkTarget is { } target)
        {
            var flat = target - _body.GlobalPosition;
            flat.Y = 0;
            if (flat.Length() < 0.6f) AutoWalkTarget = null;
            else dir = flat.Normalized();
        }
        else if (!typing)
        {
            var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
            dir = (_body.GlobalBasis * new Vector3(input.X, 0, input.Y)).Normalized();
        }
        var speed = !typing && Input.IsActionPressed("sprint") || AutoWalkTarget != null ? 11f : 7f;
        var v = _body.Velocity;
        var accel = _body.IsOnFloor() ? 40f : 8f;
        v.X = Mathf.MoveToward(v.X, dir.X * speed, accel * dt);
        v.Z = Mathf.MoveToward(v.Z, dir.Z * speed, accel * dt);
        v.Y -= 4.2f * dt; // gravedad baja de Plutón, ajustada para que saltar resulte agradable
        if (!typing && _body.IsOnFloor() && Input.IsActionJustPressed("jump")) v.Y = 4.2f;
        _body.Velocity = v;
        _body.MoveAndSlide();

        if (_main.Prefs.CameraBob && dir != Vector3.Zero && _body.IsOnFloor())
        {
            _bobPhase += dt * speed * 1.4f;
            _camera.Position = new Vector3(0, Mathf.Sin(_bobPhase) * 0.035f, 0);
        }
        else
        {
            _camera.Position = _camera.Position.Lerp(Vector3.Zero, dt * 8);
        }
        _camera.Fov = _main.Prefs.Fov;

        var from = _camera.GlobalPosition;
        var to = from - _camera.GlobalBasis.Z * 34f;
        var query = PhysicsRayQueryParameters3D.Create(from, to, PickMask);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        _aim = ("", -1);
        _aimPoint = to;
        if (hit.Count > 0)
        {
            _aimPoint = (Vector3)hit["position"];
            var collider = (GodotObject)hit["collider"];
            if (_pickables.TryGetValue(collider.GetInstanceId(), out var target2)) _aim = target2;
        }
    }

    private bool TryGroundPoint(out Vector3 point)
    {
        point = Vector3.Zero;
        var from = _camera.GlobalPosition;
        var dir = -_camera.GlobalBasis.Z;
        if (dir.Y >= -0.02f) return false;
        var t = -from.Y / dir.Y;
        if (t > 60f) return false;
        point = from + dir * t;
        return true;
    }

    private (int x, int z) RailSnap(Vector3 p)
    {
        var w = Session.Client.World!;
        var x = Mathf.RoundToInt(p.X / Cell);
        var z = Mathf.RoundToInt(p.Z / Cell);
        // Encaja en un nodo existente cercano para que unir tramos no dependa de la puntería.
        var best = 2.5f * Cell * 2.5f * Cell;
        foreach (var n in w.RailNodes.Values)
        {
            var d = new Vector2(n.X * Cell - p.X, n.Z * Cell - p.Z).LengthSquared();
            if (d < best)
            {
                best = d;
                x = n.X;
                z = n.Z;
            }
        }
        return (x, z);
    }

    private int? NearestRail()
    {
        if (!TryGroundPoint(out var p)) return null;
        var w = Session.Client.World!;
        int? found = null;
        var best = 2.5f * 2.5f;
        foreach (var s in w.RailSegments.Values)
        {
            if (s.OwnerStation >= 0 || !w.RailNodes.TryGetValue(s.A, out var a) || !w.RailNodes.TryGetValue(s.B, out var b)) continue;
            for (var i = 0; i <= 16; i++)
            {
                var q = RailPoint(a, b, s.Shape, i / 16.0);
                var d = new Vector2(q.X - p.X, q.Z - p.Z).LengthSquared();
                if (d < best)
                {
                    best = d;
                    found = s.Id;
                }
            }
        }
        return found;
    }

    private void RemoveRailGhost()
    {
        _railGhost?.QueueFree();
        _railGhost = null;
        _railGhostKey = "";
    }

    private void UpdateRailGhost(World w, PlayerState me, ref string status, ref Color color)
    {
        status = Loc.T("rail_shape_" + _railShape);
        if (!_railPlacing || !TryGroundPoint(out var p))
        {
            RemoveRailGhost();
            return;
        }
        var (x, z) = RailSnap(p);
        var error = w.CheckRail(me.Planet, _railStartX, _railStartZ, x, z, _railShape, out var credits);
        if (error == null && w.Credits < credits) error = "err_cost";
        status = error == null ? $"{Loc.T("rail_shape_" + _railShape)} — {credits} ¤" : Loc.T(error);
        color = error == null ? Ui.Ink : Ui.Bad;
        var key = $"{_railStartX},{_railStartZ},{x},{z},{_railShape}";
        if (key != _railGhostKey && (x != _railStartX || z != _railStartZ))
        {
            RemoveRailGhost();
            _railGhostKey = key;
            var a = new RailNode { X = _railStartX, Z = _railStartZ };
            var b = new RailNode { X = x, Z = z };
            var shape = _railShape;
            _railGhost = new MeshInstance3D
            {
                Mesh = Models.RailMesh(t => RailPoint(a, b, shape, t), RailMath.Length(a, b, shape, 2000) / 1000f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_railGhost);
        }
        if (_railGhost != null) _railGhost.MaterialOverride = error == null ? _ghostOk : _ghostBad;
    }

    /// <summary>Punto en el suelo al que apunta la cámara, hasta 32 m.</summary>
    private bool TryGhostCell(out int x, out int z)
    {
        x = z = 0;
        var from = _camera.GlobalPosition;
        var dir = -_camera.GlobalBasis.Z;
        if (dir.Y >= -0.02f) return false;
        var t = -from.Y / dir.Y;
        if (t > 32f) return false;
        var p = from + dir * t;
        var w = Session.Client.World!;
        var def = w.Content.Machines[_buildDef];
        var (fw, fd) = _buildRot % 2 == 0 ? (def.W, def.D) : (def.D, def.W);
        x = Mathf.RoundToInt(p.X / Cell - fw / 2f);
        z = Mathf.RoundToInt(p.Z / Cell - fd / 2f);
        if (def.Node != null)
        {
            // Encaja en el yacimiento más cercano para que colocarlo sea cómodo.
            foreach (var n in w.Nodes.Values)
            {
                if (n.Item == def.Node && n.Revealed && Mathf.Abs(n.X - x) <= 2 && Mathf.Abs(n.Z - z) <= 2)
                {
                    x = n.X;
                    z = n.Z;
                    break;
                }
            }
        }
        return true;
    }

    private void RemoveGhost()
    {
        _ghost?.QueueFree();
        _ghost = null;
        _ghostDef = "";
    }

    private void UpdateTool(double delta)
    {
        var client = Session.Client;
        var w = client.World!;
        var me = client.LocalPlayer!;
        var blocked = _hud.PanelOpen || _hud.ChatOpen || _paused;
        var status = "";
        var statusColor = Ui.Ink;
        var prompt = "";

        if (_aim.kind == "machine" && w.Machines.TryGetValue(_aim.id, out var aimed))
        {
            status = $"{Loc.T("machine_" + aimed.Def)} — {Loc.T("status_" + aimed.Status)}";
            statusColor = aimed.Status is MachineStatus.NoInput or MachineStatus.OutputFull or MachineStatus.NoPower or MachineStatus.LowPower ? Ui.Bad : Ui.Good;
            prompt = Loc.T("prompt_interact");
        }
        else if (_aim.kind == "debris" && w.DebrisById.TryGetValue(_aim.id, out var aimedDebris))
        {
            status = $"{Loc.T("item_" + w.Content.Balance.Debris[aimedDebris.Def].Drop)} · {aimedDebris.Hp}";
            prompt = Loc.T("prompt_cut");
        }

        var holding = !blocked && (AutoHoldPrimary || Input.IsActionPressed("primary"));
        var cutting = false;
        switch (_tool)
        {
            case Tool.Cutter:
                _cutTimer -= delta;
                if (holding && _aim.kind == "debris" && w.DebrisById.TryGetValue(_aim.id, out var target))
                {
                    var targetPos = new Vector3((target.X + 0.5f) * Cell, 0.6f, (target.Z + 0.5f) * Cell);
                    if (_body.GlobalPosition.DistanceTo(targetPos) <= w.Content.Balance.CutterRangeMm / 1000f)
                    {
                        cutting = true;
                        ShowBeam(_aimPoint);
                        if (_cutTimer <= 0)
                        {
                            _cutTimer = w.Content.Balance.CutterCooldownTicks / (float)w.Content.Balance.TicksPerSecond + 0.01f;
                            Primary();
                            _sparks.GlobalPosition = _aimPoint;
                            _sparks.Emitting = true;
                        }
                    }
                }
                break;
            case Tool.Build:
                prompt = Loc.T("prompt_build");
                status = $"{Loc.T("machine_" + _buildDef)} — {Ui.Cost(w.Content.Machines[_buildDef].Credits, w.Content.Machines[_buildDef].Cost)}";
                statusColor = Ui.Ink;
                UpdateGhost(w, me, ref status, ref statusColor);
                break;
            case Tool.Link:
                prompt = Loc.T(_linkFrom < 0 ? "prompt_link_from" : "prompt_link_to");
                break;
            case Tool.Remove:
                prompt = Loc.T("prompt_remove");
                break;
            case Tool.Rail:
                prompt = Loc.T(_railPlacing ? "prompt_rail_to" : "prompt_rail_from");
                UpdateRailGhost(w, me, ref status, ref statusColor);
                break;
        }
        _beam.Visible = cutting;
        _hud.SetPrompt(blocked ? "" : status, statusColor, blocked ? "" : prompt);
    }

    private void ShowBeam(Vector3 to)
    {
        var from = _camera.GlobalPosition + _camera.GlobalBasis * new Vector3(0.28f, -0.25f, -0.4f);
        var mid = (from + to) / 2;
        var dir = to - from;
        var length = Mathf.Max(0.05f, dir.Length());
        var y = dir / length;
        var x = y.Cross(Mathf.Abs(y.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(y);
        _beam.GlobalTransform = new Transform3D(new Basis(x, y * length, z), mid);
    }

    private void UpdateGhost(World w, PlayerState me, ref string status, ref Color color)
    {
        if (_ghostDef != _buildDef)
        {
            RemoveGhost();
            _ghostDef = _buildDef;
            _ghost = new Node3D();
            _ghost.AddChild(new MeshInstance3D { Mesh = Models.Machine(_buildDef).Body, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(_ghost);
        }
        if (!TryGhostCell(out var x, out var z))
        {
            _ghost!.Visible = false;
            return;
        }
        var def = w.Content.Machines[_buildDef];
        var (fw, fd) = _buildRot % 2 == 0 ? (def.W, def.D) : (def.D, def.W);
        _ghost!.Visible = true;
        _ghost.GlobalPosition = new Vector3((x + fw / 2f) * Cell, 0.02f, (z + fd / 2f) * Cell);
        _ghost.RotationDegrees = new Vector3(0, _buildRot * 90, 0);
        var error = w.CheckPlacement(me.Planet, _buildDef, x, z, _buildRot, -1, out _);
        if (error == null && !w.CanAfford(me, def.Credits, def.Cost)) error = "err_cost";
        var material = error == null ? _ghostOk : _ghostBad;
        ((MeshInstance3D)_ghost.GetChild(0)).MaterialOverride = material;
        if (error != null)
        {
            status = Loc.T(error);
            color = Ui.Bad;
        }
    }

    // ---------- Apuntado por programa para pruebas ----------

    public void AimAt(Vector3 point)
    {
        var from = _head.GlobalPosition;
        var d = point - from;
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
        _body.Rotation = new Vector3(0, _yaw, 0);
        _head.Rotation = new Vector3(_pitch, 0, 0);
    }

    public void SetRailShape(byte shape) => _railShape = shape;
    public (string kind, int id) AimTarget => _aim;
    public Vector3? FirstRemotePlayer => _remote.Count > 0 ? _remote.Values.First().Root.GlobalPosition : null;
    public Vector3 PlayerPosition => _body.GlobalPosition;
    public void SetRotation(int rot) => _buildRot = rot;
    public void OpenTechPanel() => _hud.OpenTech();
    public void ClosePanelForTest() => _hud.ClosePanel();
    public void OpenMachinePanel(int id) => _hud.OpenMachine(id);
}

public static class Controls
{
    private static bool _done;

    public static void Setup()
    {
        if (_done) return;
        _done = true;
        Key("move_forward", Godot.Key.W);
        Key("move_back", Godot.Key.S);
        Key("move_left", Godot.Key.A);
        Key("move_right", Godot.Key.D);
        Key("sprint", Godot.Key.Shift);
        Key("jump", Godot.Key.Space);
        Key("pause", Godot.Key.Escape);
        Key("chat", Godot.Key.Enter);
        Key("tech", Godot.Key.Tab);
        Key("tool_cutter", Godot.Key.Key1);
        Key("tool_build", Godot.Key.Key2, Godot.Key.B);
        Key("tool_link", Godot.Key.Key3, Godot.Key.L);
        Key("tool_remove", Godot.Key.Key4, Godot.Key.X);
        Key("tool_rail", Godot.Key.Key5, Godot.Key.T);
        Key("rotate", Godot.Key.R);
        Key("cancel", Godot.Key.Q);
        Key("interact_insert", Godot.Key.E);
        Key("interact_take", Godot.Key.F);
        Key("interact_panel", Godot.Key.C);
        Mouse("primary", MouseButton.Left);
        Mouse("secondary", MouseButton.Right);
    }

    private static void Key(string action, params Godot.Key[] keys)
    {
        InputMap.AddAction(action);
        foreach (var k in keys) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
    }

    private static void Mouse(string action, MouseButton button)
    {
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
