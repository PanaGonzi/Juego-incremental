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
        public Vector3 RotorAxis = Vector3.Up;
        public float RotorSpeed = 2.6f;
        public MeshInstance3D Lamp = null!;
        public Node3D? Piston;
        public Vector3 PistonRest;
        public MeshInstance3D Warn = null!;
        public string Key = "";
        public string Def = "";
        public float Height;
        public MachineStatus Status;
        public float Phase;
        public OmniLight3D? Glow;
        public bool Running => Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.Overheated;
    }

    private sealed class RemotePlayer
    {
        public Node3D Root = null!;
        public Vector3 Target;
        public float TargetYaw;
        public double LastSeen;
        public RemoteToolView Tool = null!;
        public byte Action;
    }

    public readonly GameSession Session;
    private readonly Main _main;
    private readonly Dictionary<int, MachineNode> _machines = new();
    private readonly Dictionary<int, MeshInstance3D> _debris = new();
    public readonly BeltView Belts = new();
    private readonly ChainOverlay _chain = new();
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
    private MeshInstance3D? _eventDecor;
    private AudioStreamPlayer3D? _eventAmbience;
    private OmniLight3D? _eventLight;
    public bool EventDecorVisible => _eventDecor != null;
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
    private GpuParticles3D _sparks = null!;
    private FirstPersonRig _rig = null!;
    private ImpactEffects _impacts = null!;
    private ToolAudio _toolAudio = null!;
    private InventoryUi _inv = null!;
    private Hotbar _hotbar = null!;
    private TargetBar _targetBar = null!;
    private CanvasLayer _uiLayer = null!;
    private readonly Dictionary<int, ImpactKind> _debrisKinds = new();
    private ShaderMaterial _ground = null!;
    private ShaderMaterial _sky = null!;
    private Scenery _scenery = null!;
    private int _viewPlanet = -1;

    // Trayecto en lanzadera: continuo, sin pantalla de carga. La niebla oculta el relevo de entornos.
    private Node3D? _shuttle;
    private MeshInstance3D? _globe;
    private bool _inFlight;
    private double _flightTime;
    private double _flightTotal;
    private int _flightTo;
    private bool _flightSwapped;
    private Label3D? _flightLabel;
    private const float FlightAltitude = 420f;
    private const float BaseFog = 0.0022f;
    public int ViewPlanet => _viewPlanet;
    public bool InFlight => _inFlight;
    public float MaxSyncMs;
    public float MaxPollMs;
    public int RenderedDebris => _debris.Count;
    public int RenderedMachines => _machines.Count;
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

    /// <summary>Distancia a la que el cabezal alcanza de verdad el objetivo. El servidor sigue validando su propio alcance.</summary>
    private const float WorkReach = 4.4f;
    private bool _stowed;
    private bool _working;
    private bool _suppressPrimary;
    private int _ignoreMotion;
    private const float MaxLookStep = 900f;
    private const ulong RemoveArmMs = 350;
    private ulong _removeArmedAt;
    private Vector2 _lookAccum;
    private float _wallDistance = 9f;
    private float _kick;
    private GeometryInstance3D? _highlighted;
    private List<string> _hotbarEntries = HotbarLayout.Default.ToList();

    private Tool _tool = Tool.Cutter;
    private string _buildDef = "";
    private int _buildRot;
    private Node3D? _ghost;
    private string _ghostDef = "";
    private readonly StandardMaterial3D _ghostOk = GhostMat(new Color(0.3f, 1f, 0.55f, 0.42f));
    private readonly StandardMaterial3D _ghostBad = GhostMat(new Color(1f, 0.3f, 0.2f, 0.42f));
    private readonly StandardMaterial3D _ghostLacking = GhostMat(new Color(1f, 0.72f, 0.2f, 0.42f));
    private MeshInstance3D? _cellMark;
    private string _cellMarkKey = "";
    private bool _stowedBeforeTool;
    private int _objectiveSeen = -1;
    private long _lastCredits;
    private (string kind, int id) _aim = ("", -1);
    private Vector3 _aimPoint;

    // Control por programa para las pruebas del cliente exportado.
    public Vector3? AutoWalkTarget;
    public bool AutoHoldPrimary;
    private float _autoStuck, _autoSidestep;
    public bool TestCapture;
    public Tool CurrentTool => _tool;
    public bool PanelIsOpen => _hud.PanelOpen || _inv.IsOpen;
    public bool InventoryOpen => _inv.IsOpen;
    public InventoryUi Inventory => _inv;
    public FirstPersonRig Rig => _rig;
    public bool IsWorking => _working;
    public bool IsStowed => _stowed;
    public IList<string> HotbarEntries => _hotbarEntries;
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
        BuildEnvironment();
        BuildPlayer();
        AddChild(Belts);
        AddChild(_chain);
        _chain.LimitChanged += text => _hud.Toast(text, Ui.Warn);
        _hotbarEntries = HotbarLayout.Normalize(_main.Prefs.Hotbar);
        AddChild(new MachineThumbnails());
        var layer = new CanvasLayer();
        _uiLayer = layer;
        AddChild(layer);
        _feedback = new ActionFeedback(_impacts, layer);
        AddChild(_feedback);
        Belts.BeltAdded += _feedback.BeltLinked;
        Belts.BeltRemoved += _feedback.BeltRemoved;
        _hud = new Hud(this);
        layer.AddChild(_hud);
        _hud.SetTool(0);
        _targetBar = new TargetBar();
        layer.AddChild(_targetBar);
        layer.AddChild(new PickupFeed(this));
        _inv = new InventoryUi(this);
        layer.AddChild(_inv);
        _hotbar = new Hotbar(this);
        layer.AddChild(_hotbar);

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
        client.RemoteAction += OnRemoteAction;
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
        _scenery = new Scenery(this, _main.Prefs);
        _env = _scenery.Env;
        _sun = _scenery.Sun;
        _ground = _scenery.GroundMat;
        _sky = _scenery.SkyMat;

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

    /// <summary>El relieve visual está en Terrain; la colisión sigue siendo el plano de la cuadrícula jugable.</summary>
    private void BuildTerrain()
    {
        var floor = new StaticBody3D();
        floor.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(Vector3.Up, 0) } });
        AddChild(floor);
        foreach (var (normal, distance) in new[] { (Vector3.Right, -Terrain.Walkable), (Vector3.Left, -Terrain.Walkable), (Vector3.Forward, -Terrain.Walkable), (Vector3.Back, -Terrain.Walkable) })
        {
            var wall = new StaticBody3D();
            wall.AddChild(new CollisionShape3D { Shape = new WorldBoundaryShape3D { Plane = new Plane(normal, distance) } });
            AddChild(wall);
        }
    }

    /// <summary>La decoración depende del planeta y se crea al mostrarlo: ver Scenery.SetPlanet.</summary>
    private void BuildScenery()
    {
    }

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

        _toolAudio = new ToolAudio(_main.Prefs);
        AddChild(_toolAudio);
        _rig = new FirstPersonRig { Sound = _toolAudio.Play };
        _camera.AddChild(_rig);
        _impacts = new ImpactEffects();
        AddChild(_impacts);
        _activity = new FactoryActivity();
        AddChild(_activity);
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
        SetPlanetLook(me.TransitTo >= 0 ? me.TransitTo : me.Planet);
        if (me.TransitTo >= 0) StartFlight(me);
        _lastCredits = Session.Client.World!.Credits;
        _objectiveSeen = Session.Client.World.ObjectiveIndex;
        UpdateMouseMode();
    }

    // ---------- Planetas y lanzadera ----------

    private void SetPlanetLook(int planet)
    {
        _viewPlanet = planet;
        var mars = planet == 1;
        _ambience ??= CreateAmbience();
        _ambience.PitchScale = mars ? 1.25f : 0.85f;
        _scenery.SetPlanet(planet);
    }

    private AudioStreamPlayer? _ambience;

    private AudioStreamPlayer CreateAmbience()
    {
        var player = new AudioStreamPlayer { Stream = Sfx.Wind, VolumeDb = Sfx.AmbienceDb, Autoplay = true };
        AddChild(player);
        return player;
    }

    /// <summary>Retira la representación del planeta anterior; su simulación sigue en el servidor.</summary>
    private void ClearWorldView()
    {
        _viewSynced = false;
        foreach (var id in _machines.Keys.ToList()) FreeMachine(id);
        _highlighted = null;
        foreach (var mesh in _debris.Values) mesh.QueueFree();
        _debris.Clear();
        _debrisKinds.Clear();
        foreach (var body in _debrisBodies.Values)
        {
            _pickables.Remove(body.GetInstanceId());
            body.QueueFree();
        }
        _debrisBodies.Clear();
        Belts.Clear();
        foreach (var mesh in _nodes.Values) mesh.QueueFree();
        _nodes.Clear();
        foreach (var mesh in _rails.Values) mesh.QueueFree();
        _rails.Clear();
        foreach (var t in _trains.Values)
        {
            t.Loco.QueueFree();
            t.Wagon.QueueFree();
        }
        _trains.Clear();
        foreach (var r in _remote.Values) r.Root.QueueFree();
        _remote.Clear();
    }

    private void StartFlight(PlayerState me)
    {
        var balance = Session.Client.World!.Content.Balance;
        _inFlight = true;
        _flightTo = me.TransitTo;
        _flightTotal = balance.TravelTicks / (double)balance.TicksPerSecond;
        _flightTime = Math.Clamp((Session.Client.World.Tick - me.TransitStartTick) / (double)balance.TicksPerSecond, 0, _flightTotal);
        _flightSwapped = _viewPlanet == _flightTo;
        SetTool(Tool.Cutter);
        _hud.ClosePanel();
        _inv.Close();

        _shuttle = new Node3D { Position = _body.GlobalPosition with { Y = 0.5f } };
        _shuttle.AddChild(new MeshInstance3D { Mesh = Models.ShuttleCabin(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _flightLabel = new Label3D
        {
            Text = $"{Loc.T("shuttle_title")} {Loc.T("planet_" + _flightTo)}",
            Position = new Vector3(0, 1.75f, -2.35f),
            PixelSize = 0.003f,
            FontSize = 40,
            OutlineSize = 6,
            Modulate = Ui.Amber,
        };
        _shuttle.AddChild(_flightLabel);
        var interior = new OmniLight3D { LightColor = new Color(1f, 0.85f, 0.65f), LightEnergy = 0.7f, OmniRange = 7, Position = new Vector3(0, 2.2f, 0) };
        _shuttle.AddChild(interior);
        AddChild(_shuttle);

        var mars = _flightTo == 1;
        _globe = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = mars ? new Color(0.62f, 0.3f, 0.16f) : new Color(0.6f, 0.56f, 0.52f),
                Roughness = 1,
                DisableFog = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_globe);
    }

    private static float Smooth(double t) => Mathf.SmoothStep(0f, 1f, (float)Math.Clamp(t, 0, 1));

    /// <summary>Despegue, crucero y descenso guiados por un único reloj; sin esperas añadidas ni cortes.</summary>
    private void UpdateFlight(double delta)
    {
        var client = Session.Client;
        var me = client.LocalPlayer!;
        _flightTime += delta;
        var p = _flightTime / _flightTotal;
        var arrived = me.TransitTo < 0 && me.Planet == _flightTo;
        if (p >= 1 && !arrived) p = 0.999; // destino aún no confirmado: el trayecto continúa, sin pantalla

        var y = p < 0.5 ? 0.5f + Smooth(p / 0.32) * FlightAltitude : 0.5f + (1 - Smooth((p - 0.68) / 0.32)) * FlightAltitude;
        var fog = BaseFog + (0.03f - BaseFog) * (p < 0.5 ? Smooth((p - 0.12) / 0.2) : 1 - Smooth((p - 0.68) / 0.2));
        _env.Environment.FogDensity = fog;
        var position = _shuttle!.Position;
        if (!_flightSwapped && p >= 0.5)
        {
            // Crucero con el suelo ya oculto: relevo de entorno. El nuevo aparece al disiparse la niebla.
            _flightSwapped = true;
            ClearWorldView();
            SetPlanetLook(_flightTo);
            _worldDirty = true;
            var planet = client.World!.Content.Planets.First(pl => pl.Id == _flightTo);
            var cell = client.World.Content.Balance.CellSizeMm / 1000f;
            position.X = planet.Spawn.X * cell;
            position.Z = planet.Spawn.Z * cell;
        }
        position.Y = y;
        _shuttle.Position = position;
        _body.GlobalPosition = position + new Vector3(0, 0.05f, 0.9f);
        _body.Velocity = Vector3.Zero;

        var globeAmount = Smooth((p - 0.28) / 0.3);
        var globeOut = 1 - Smooth((p - 0.62) / 0.1);
        _globe!.Visible = globeAmount * globeOut > 0.01f;
        if (_globe.Visible)
        {
            _globe.Position = position + new Vector3(0, -70 + 60 * (float)p, -900);
            var r = 40 + 330 * globeAmount;
            _globe.Scale = new Vector3(r, r, r);
            ((StandardMaterial3D)_globe.MaterialOverride).AlbedoColor = ((StandardMaterial3D)_globe.MaterialOverride).AlbedoColor with { A = globeOut };
        }

        if (p >= 1 && arrived)
        {
            _inFlight = false;
            _env.Environment.FogDensity = BaseFog;
            _body.GlobalPosition = new Vector3(me.X, 0.2f, me.Z);
            _shuttle.QueueFree();
            _shuttle = null;
            _globe.QueueFree();
            _globe = null;
        }
    }

    // ---------- Sincronización de representación ----------

    private void SyncWorld()
    {
        var w = Session.Client.World!;
        var seen = new HashSet<int>();
        foreach (var m in w.Machines.Values)
        {
            if (m.Planet != _viewPlanet) continue;
            seen.Add(m.Id);
            var key = $"{m.Def}|{m.X}|{m.Z}|{m.Rot}";
            if (!_machines.TryGetValue(m.Id, out var node) || node.Key != key)
            {
                var isNew = node == null;
                if (node != null) FreeMachine(m.Id);
                node = CreateMachine(m, key);
                _machines[m.Id] = node;
                // Solo lo que aparece con la partida ya en marcha: cargar el mundo no es construir.
                if (isNew && _viewSynced) _feedback.Placed(node.Root, w.Content.Machines[m.Def].W * Cell);
            }
            if (node.Status != m.Status)
            {
                node.Status = m.Status;
                node.Warn.Mesh = Models.StatusIcon(m.Status);
                node.Lamp.MaterialOverride = Models.LampState(m.Status);
                // La luz de trabajo dice de lejos si la máquina produce: encendida en marcha, en piloto si no.
                if (node.Glow != null) node.Glow.LightEnergy = FactoryActivity.GlowEnergy(node.Running);
            }
        }
        foreach (var id in _machines.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            if (_viewSynced && _machines.TryGetValue(id, out var gone)) _feedback.Removed(gone.Root.GlobalPosition);
            FreeMachine(id);
        }

        seen.Clear();
        foreach (var d in w.DebrisById.Values)
        {
            if (d.Planet != _viewPlanet) continue;
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
            BreakEffect(mesh.GlobalPosition, _debrisKinds.GetValueOrDefault(id));
            if (_highlighted == mesh) _highlighted = null;
            _debrisKinds.Remove(id);
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
            if (n.Planet != _viewPlanet || !n.Revealed || _nodes.ContainsKey(n.Id)) continue;
            var mesh = new MeshInstance3D { Mesh = Models.NodeMesh(n.Item), Position = new Vector3((n.X + 1) * Cell, 0, (n.Z + 1) * Cell) };
            AddChild(mesh);
            _nodes[n.Id] = mesh;
        }

        Belts.Sync(w, _viewPlanet, Session.Client.StructuralDeltas);

        seen.Clear();
        foreach (var s in w.RailSegments.Values)
        {
            if (s.Planet != _viewPlanet) continue;
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
            if (t.Planet != _viewPlanet) continue;
            seen.Add(t.Id);
            if (!_trains.ContainsKey(t.Id)) _trains[t.Id] = CreateTrainNode();
        }
        foreach (var id in _trains.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _trains[id].Loco.QueueFree();
            _trains[id].Wagon.QueueFree();
            _trains.Remove(id);
        }

        UpdateEvent(w);

        var objectiveDone = w.ObjectiveIndex != _objectiveSeen;
        if (objectiveDone)
        {
            _objectiveSeen = w.ObjectiveIndex;
            Sfx.Play("objective");
            _feedback.ObjectiveDone();
        }
        if (w.Upgrades.Count != _upgradesSeen)
        {
            if (w.Upgrades.Count > _upgradesSeen && _viewSynced) _feedback.UpgradeBought();
            _upgradesSeen = w.Upgrades.Count;
        }
        var sold = w.Stats.GetValueOrDefault("sold_credits");
        if (sold > _soldSeen)
        {
            // La cifra sube desde la terminal de exportación más cercana, si está a la vista.
            var player = _body.GlobalPosition;
            var terminal = _machines.Values.Where(n => n.Def == "export_terminal").OrderBy(n => n.Root.GlobalPosition.DistanceSquaredTo(player)).FirstOrDefault();
            if (_viewSynced) _feedback.Sold(sold - _soldSeen, terminal?.Root.GlobalPosition + Vector3.Up * ((terminal?.Height ?? 0) + 0.5f), player);
        }
        _soldSeen = sold;
        if (w.Credits > _lastCredits + 5 && !objectiveDone) Sfx.Play("sell");
        _lastCredits = w.Credits;
        _viewSynced = true;
    }

    private ActionFeedback _feedback = null!;
    private FactoryActivity _activity = null!;
    private bool _viewSynced;
    private int _upgradesSeen;
    private long _soldSeen;
    public ActionFeedback Feedback => _feedback;
    public FactoryActivity Activity => _activity;
    public ImpactEffects Impacts => _impacts;

    private MeshInstance3D? _objectiveBeam;
    public bool ObjectiveMarkerVisible => _objectiveBeam is { Visible: true };
    public float ObjectiveDistance { get; private set; } = -1;
    /// <summary>Destino del marcador del objetivo; el de la guía si falta algo. Null si no hay a dónde señalar.</summary>
    public Vector3? ObjectivePoint { get; private set; }
    public Vector3? DepotPoint { get; private set; }
    public ObjectiveGuide Guide { get; private set; } = new();
    public Camera3D Camera => _camera;
    /// <summary>Partida a la vista, sin pantalla de carga.</summary>
    public bool WorldShown => _worldReady && !_inFlight;
    /// <summary>La flecha y el rombo sobre la pantalla: solo con la vista libre, sin paneles delante.</summary>
    public bool PointerShown => WorldShown && !_paused && !_hud.PanelOpen && !_inv.IsOpen;
    /// <summary>Altura a la que empieza la barra rápida: los paneles terminan antes.</summary>
    public float HotbarTop => _hotbar.TopY;
    private ulong _guideAt;

    /// <summary>Haz de luz, brújula y flecha apuntan al mismo sitio: el destino del objetivo o de lo que le falta. Depende de lo que hay en el mundo, no de ver la baliza.</summary>
    private void UpdateObjectiveMarker(World w, PlayerState me)
    {
        _objectiveBeam ??= CreateObjectiveBeam();
        ObjectiveDistance = -1;
        ObjectivePoint = null;
        _objectiveBeam.Visible = false;
        var from = _body.GlobalPosition;
        var depot = w.DepotOf(me.Planet);
        if (depot != null)
        {
            var (dx, dz) = w.MachineCenterMm(depot);
            DepotPoint = new Vector3(dx / 1000f, 0, dz / 1000f);
        }
        else DepotPoint = null;
        if (_inFlight || w.ObjectiveIndex >= w.Content.Objectives.Count) return;
        // Cuatro veces por segundo basta: recorre máquinas y escombros.
        var now = Time.GetTicksMsec();
        if (now >= _guideAt)
        {
            _guideAt = now + 250;
            Guide = w.GuideObjective(me);
        }
        if (Guide.Target == null) return;
        var fx = (long)(from.X * 1000);
        var fz = (long)(from.Z * 1000);
        // Sin la máquina aún construida (o sin ella en este planeta), se señala el depósito para que la guía no se quede sin destino.
        var point = w.FindObjectiveTarget(Guide.Target, me.Planet, fx, fz) ?? w.FindObjectiveTarget("machine:depot", me.Planet, fx, fz);
        if (point == null) return;
        var at = new Vector3(point.Value.x / 1000f, 0, point.Value.z / 1000f);
        ObjectivePoint = at;
        ObjectiveDistance = new Vector2(at.X - from.X, at.Z - from.Z).Length();
        _objectiveBeam.Visible = ObjectiveDistance > 7;
        _objectiveBeam.GlobalPosition = at + Vector3.Up * 40;
    }

    private MeshInstance3D CreateObjectiveBeam()
    {
        var beam = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 80, RadialSegments = 8, Rings = 1 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1f, 0.67f, 0.2f, 0.4f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                DisableFog = true,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(beam);
        return beam;
    }

    private void UpdateEvent(World w)
    {
        var show = w.ActiveEvents.Contains("halloween") && _viewPlanet == 0;
        if (show && _eventDecor == null)
        {
            var decor = w.Content.Events["halloween"].Decor;
            var at = new Vector3(decor.X * Cell, 0, decor.Z * Cell);
            _eventDecor = new MeshInstance3D { Mesh = Models.HalloweenDecor(), Position = at };
            AddChild(_eventDecor);
            _eventLight = new OmniLight3D { LightColor = new Color(0.35f, 1f, 0.65f), LightEnergy = 1.6f * _main.Prefs.EventAmbience, OmniRange = 42, Position = at + Vector3.Up * 6 };
            AddChild(_eventLight);
            _eventAmbience = new AudioStreamPlayer3D { Stream = Sfx.Ghost, MaxDistance = 75, UnitSize = 10, Position = at + Vector3.Up, VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Sfx.MasterLinear * _main.Prefs.EventAmbience)) };
            AddChild(_eventAmbience);
            if (_main.Prefs.EventAmbience > 0.01f) _eventAmbience.Play();
        }
        else if (!show && _eventDecor != null)
        {
            _eventDecor.QueueFree();
            _eventDecor = null;
            _eventLight?.QueueFree();
            _eventLight = null;
            _eventAmbience?.QueueFree();
            _eventAmbience = null;
        }
        // Las recompensas son del mundo y permanecen aunque el evento se desactive.
        var spectral = w.EquippedCosmetics.Contains("spectral_train");
        foreach (var t in _trains.Values)
        {
            var material = spectral ? Models.Mat("spectral") : null;
            foreach (var child in t.Loco.GetChildren().Concat(t.Wagon.GetChildren()))
                if (child is MeshInstance3D mesh && mesh != t.Cargo) mesh.MaterialOverride = material;
        }
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
            Cargo = new MeshInstance3D { Mesh = Models.WagonLoad() },
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
        var root = new Node3D { Position = CenterOf(w, m), RotationDegrees = new Vector3(0, ModelYawDegrees(w.Content.Machines[m.Def], m.Rot), 0) };
        root.AddChild(new MeshInstance3D { Mesh = model.Body });
        var node = new MachineNode { Root = root, Key = key, Def = m.Def, Height = model.Height, Status = MachineStatus.Idle, Phase = GD.Randf() * 6f };

        if (model.Rotor != null)
        {
            node.Rotor = new MeshInstance3D { Mesh = model.Rotor, Position = model.RotorPos };
            node.RotorAxis = model.RotorAxis;
            node.RotorSpeed = model.RotorSpeed;
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

        node.Warn = new MeshInstance3D
        {
            Position = new Vector3(0, model.Height + 0.7f, 0),
            Scale = new Vector3(1.3f, 1.3f, 1.3f),
            VisibilityRangeEnd = 45,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(node.Warn);
        // Piloto de estado junto al icono con forma: el color nunca informa solo.
        node.Lamp = new MeshInstance3D { Mesh = Models.Lamp(), Position = model.LampPos, MaterialOverride = Models.LampState(m.Status), VisibilityRangeEnd = 70, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(node.Lamp);
        if (model.GlowPos is { } glow)
        {
            node.Glow = new OmniLight3D { Position = glow, LightColor = model.GlowColor, LightEnergy = FactoryActivity.GlowEnergy(false), LightSpecular = 0.3f, OmniRange = 7f, DistanceFadeEnabled = true, DistanceFadeBegin = 34, DistanceFadeLength = 12 };
            root.AddChild(node.Glow);
        }
        AddChild(root);
        return node;
    }

    private void FreeMachine(int id)
    {
        if (!_machines.Remove(id, out var node)) return;
        if (_highlighted != null && node.Root.IsAncestorOf(_highlighted)) _highlighted = null;
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
        _debrisKinds[d.Id] = ImpactOf(d.Def);
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

    /// <summary>Material de cada escombro a efectos de partículas y sonido.</summary>
    private static ImpactKind ImpactOf(string debrisDef) => debrisDef switch
    {
        "ice_block" or "ice_crust" => ImpactKind.Ice,
        "red_rock" => ImpactKind.Rock,
        _ => ImpactKind.Scrap,
    };

    private void BreakEffect(Vector3 at, ImpactKind kind)
    {
        _impacts.Burst(kind, at + Vector3.Up * 0.6f);
        Sfx.Play("break", 0.1f);
    }

    // ---------- Entrada ----------

    /// <summary>La tecla del inventario llega antes que la navegación por foco de la interfaz, que también usa Tab.</summary>
    public override void _Input(InputEvent e)
    {
        if (!_worldReady || _hud.ChatOpen || _paused || !e.IsActionPressed("inventory")) return;
        if (_hud.PanelOpen) _hud.ClosePanel();
        else if (!_inFlight) OpenInventory(InventoryTab.Items);
        GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_worldReady) return;
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // Al recuperar el ratón llega un salto acumulado que no es movimiento del jugador.
            if (_ignoreMotion > 0) return;
            // Un salto imposible de una sola vez no es movimiento del jugador: recuperar la ventana o el foco del sistema.
            if (motion.Relative.LengthSquared() > MaxLookStep * MaxLookStep) return;
            var s = 0.0022f * _main.Prefs.Sensitivity;
            _yaw -= motion.Relative.X * s;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * s, -1.5f, 1.5f);
            _lookAccum += motion.Relative;
            return;
        }
        if (_inv.IsOpen) return;
        if (e.IsActionPressed("pause"))
        {
            if (_hud.ChatOpen) _hud.CloseChat();
            else if (_hud.PanelOpen && !_paused) _hud.ClosePanel();
            else if (_paused && SettingsForm.Escape is { } back) back();
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
        if (e.IsActionPressed("players"))
        {
            if (_hud.PanelOpen) _hud.ClosePanel(); else _hud.OpenPlayers();
            return;
        }
        if (_hud.PanelOpen || _inFlight) return;

        if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel)
        {
            var from = _hotbarEntries.IndexOf(ActiveEntry);
            var next = HotbarLayout.Step(_hotbarEntries, from, wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1);
            // La rueda solo cambia de herramienta: nunca guarda la multiherramienta por repetir casilla.
            if (next >= 0 && next != from) ActivateEntry(_hotbarEntries[next]);
            return;
        }
        for (var i = 0; i < HotbarLayout.Slots; i++)
        {
            if (!e.IsActionPressed("hotbar_" + (i + 1))) continue;
            SelectHotbar(i);
            return;
        }

        if (e.IsActionPressed("tool_cutter")) ActivateEntry(HotbarLayout.Cutter);
        else if (e.IsActionPressed("tool_build")) OpenInventory(InventoryTab.Catalog);
        else if (e.IsActionPressed("tool_link")) SetTool(Tool.Link);
        else if (e.IsActionPressed("tool_remove")) SetTool(Tool.Remove);
        else if (e.IsActionPressed("tool_rail")) SetTool(Tool.Rail);
        else if (e.IsActionPressed("rotate"))
        {
            if (_tool == Tool.Link) Belts.ToolNextRoute();
            else if (_tool == Tool.Rail) _railShape = (byte)((_railShape + 1) % 3);
            else if (_tool == Tool.Build) _buildRot = (_buildRot + 1) % 4;
            if (_tool is Tool.Rail or Tool.Build) _rig.OnTap();
        }
        else if (_tool == Tool.Link && e.IsActionPressed("belt_undo")) Belts.ToolUndoPoint();
        else if (_tool == Tool.Link && e.IsActionPressed("belt_snap")) _hud.Toast(Loc.T(Belts.ToolToggleSnap()), Ui.Ink);
        else if (_tool == Tool.Link && e.IsActionPressed("belt_port")) Belts.ToolNextPort(Session.Client.World!, _aim.kind, _aim.id);
        else if (e.IsActionPressed("cancel")) Cancel();
        else if (e.IsActionPressed("interact")) Interact();
        else if (e.IsActionPressed("interact_insert")) AimedMachine(CommandType.Insert);
        else if (e.IsActionPressed("interact_take")) AimedMachine(CommandType.Extract);
        else if (e.IsActionPressed("interact_panel"))
        {
            if (_aim.kind == "machine") _hud.OpenMachine(_aim.id);
        }
        else if (e.IsActionPressed("chain_trace"))
        {
            // Una segunda pulsación apaga, se apunte a donde se apunte.
            if (_chain.Active) _chain.Clear();
            else if (_aim.kind == "machine") ToggleChain(_aim.id);
        }
        else if (e.IsActionPressed("primary"))
        {
            // Desmontar recién elegida no actúa: un clic que ya venía en camino no debe llevarse una máquina.
            if (_tool != Tool.Cutter && !(_tool == Tool.Remove && Time.GetTicksMsec() < _removeArmedAt)) Primary();
        }
        else if (e.IsActionPressed("secondary")) Cancel();
    }

    /// <summary>Al volver el foco a la ventana, el sistema entrega de golpe el movimiento acumulado.</summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusIn) _ignoreMotion = 3;
    }

    public int ChainSeed => _chain.Seed;
    public ChainOverlay ChainForTest => _chain;
    public LookReadout LookForTest => _hud.Look;
    public void OpenMachineForTest(int id) => _hud.OpenMachine(id);

    /// <summary>Enciende el resaltado de la cadena de una máquina; sobre la misma máquina, lo apaga.</summary>
    public void ToggleChain(int machine)
    {
        if (_chain.Seed == machine) _chain.Clear();
        else _chain.Follow(Session.Client.World!, machine);
    }

    /// <summary>
    /// Clic derecho o cancelar: primero anula el paso en curso; si no lo hay, deja las manos como estaban antes de la herramienta.
    /// No envía nada al servidor, no gira la pieza y con la multiherramienta en la mano no hace nada.
    /// </summary>
    private void Cancel()
    {
        if (_tool == Tool.Link && Belts.ToolCancel()) { }
        else if (_tool == Tool.Rail && _railPlacing) _railPlacing = false;
        else if (_tool != Tool.Cutter)
        {
            var stowed = _stowedBeforeTool;
            SetTool(Tool.Cutter);
            _stowed = stowed;
            RefreshHotbar();
        }
    }

    /// <summary>E: abre o actúa sobre lo apuntado. El gesto es breve y nunca bloquea la acción.</summary>
    private void Interact()
    {
        var w = Session.Client.World!;
        _rig.OnGesture();
        if (_aim.kind == "debris" && _tool == Tool.Cutter && !_stowed && w.DebrisById.TryGetValue(_aim.id, out var d))
        {
            // Recoger a mano: el servidor valida alcance y enfriamiento igual que con el cabezal.
            var at = new Vector3((d.X + 0.5f) * Cell, 0.6f, (d.Z + 0.5f) * Cell);
            if (_body.GlobalPosition.DistanceTo(at) <= WorkReach) Session.Client.Send(new Command { Type = CommandType.CutDebris, A = d.Id });
            return;
        }
        if (_aim.kind != "machine" || !w.Machines.TryGetValue(_aim.id, out var m)) return;
        if (!MachineInReach(w, m))
        {
            _hud.Toast(Loc.T("err_range"));
            Sfx.Play("error");
            return;
        }
        if (w.Content.Machines[m.Def].Kind is MachineKind.Generator or MachineKind.Shuttle or MachineKind.Radiator) _hud.OpenMachine(m.Id);
        else OpenInventory(InventoryTab.Items, m.Id);
    }

    private bool MachineInReach(World w, Machine m)
    {
        var (cx, cz) = w.MachineCenterMm(m);
        return new Vector2(cx / 1000f - _body.GlobalPosition.X, cz / 1000f - _body.GlobalPosition.Z).Length() <= w.Content.Balance.InteractRangeMm / 1000f;
    }

    // ---------- Barra rápida e inventario ----------

    /// <summary>Lo que corresponde a la barra rápida en este momento, o vacío si la herramienta está guardada.</summary>
    public string ActiveEntry => _stowed ? "" : _tool switch
    {
        Tool.Cutter => HotbarLayout.Cutter,
        Tool.Build => "build:" + _buildDef,
        Tool.Link => "tool:link",
        Tool.Remove => "tool:remove",
        Tool.Rail => "tool:rail",
        _ => "",
    };

    public void SelectHotbar(int index)
    {
        if (index < 0 || index >= _hotbarEntries.Count || _hotbarEntries[index].Length == 0) return;
        ActivateEntry(_hotbarEntries[index]);
        RefreshHotbar();
    }

    public void ActivateEntry(string entry)
    {
        switch (EntryInfo.ToolOf(entry))
        {
            case "cutter":
                // Repetir la casilla de la herramienta la guarda y libera las manos.
                if (_tool == Tool.Cutter && !_stowed)
                {
                    _stowed = true;
                    _hud.Toast(Loc.T("notice_stowed"), Ui.Ink);
                }
                else
                {
                    SetTool(Tool.Cutter);
                    _hud.ClearToast();
                }
                return;
            case "link": SetTool(Tool.Link); return;
            case "remove": SetTool(Tool.Remove); return;
            case "rail": SetTool(Tool.Rail); return;
        }
        var def = EntryInfo.DefOf(entry);
        var w = Session.Client.World;
        if (w == null || !w.Content.Machines.TryGetValue(def, out var d)) return;
        if (d.Unlock != null && !w.Upgrades.Contains(d.Unlock))
        {
            Notify(Loc.T("hotbar_locked"));
            return;
        }
        SelectBuild(def);
    }

    public void AssignHotbar(int index, string entry)
    {
        HotbarLayout.Assign(_hotbarEntries, index, entry);
        SaveHotbar();
    }

    public void ClearHotbar(int index)
    {
        HotbarLayout.Clear(_hotbarEntries, index);
        SaveHotbar();
    }

    private void SaveHotbar()
    {
        _main.Prefs.Hotbar = _hotbarEntries.ToList();
        _main.Prefs.Save();
        RefreshHotbar();
    }

    public void RefreshHotbar() => _hotbar?.Refresh();

    public void OpenInventory(InventoryTab tab, int machine = -1)
    {
        if (_inFlight) return;
        if (_hud.PanelOpen) _hud.ClosePanel();
        _inv.Open(tab, machine);
    }

    public void OnInventoryClosed()
    {
        UpdateMouseMode();
        RefreshHotbar();
    }

    public void OpenTechFromInventory()
    {
        _inv.Close();
        _hud.OpenTech();
    }

    public void OpenPlayersFromInventory()
    {
        _inv.Close();
        _hud.OpenPlayers();
    }

    public void OpenMachineFromInventory(int id)
    {
        _inv.Close();
        _hud.OpenMachine(id);
    }

    public void Notify(string text)
    {
        _hud.Toast(text);
        Sfx.Play("error");
    }

    public void RigGesture() => _rig.OnGesture();

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
        _hud.RefreshStaticText();
        // Sus botones llevan texto e indicación de tecla fijos: tras cambiar idioma o controles se rehace.
        var old = _inv;
        _inv = new InventoryUi(this);
        _uiLayer.AddChild(_inv);
        _uiLayer.MoveChild(_inv, old.GetIndex());
        old.QueueFree();
        if (_ambience != null) _ambience.VolumeDb = Sfx.AmbienceDb;
        if (_eventLight != null) _eventLight.LightEnergy = 1.6f * _main.Prefs.EventAmbience;
        if (_eventAmbience != null)
        {
            _eventAmbience.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Sfx.MasterLinear * _main.Prefs.EventAmbience));
            if (_main.Prefs.EventAmbience <= 0.01f) _eventAmbience.Stop();
            else if (!_eventAmbience.Playing) _eventAmbience.Play();
        }
        _camera.Fov = _main.Prefs.Fov;
        _sun.ShadowEnabled = _main.Prefs.Shadows;
        _scenery.ApplyQuality(_main.Prefs.Quality);
        _env.Environment.GlowEnabled = _main.Prefs.Glow;
    }

    public void UpdateMouseMode()
    {
        var free = _paused || _hud.PanelOpen || _hud.ChatOpen || _inv.IsOpen || !_worldReady;
        var mode = _main.Auto != null && !TestCapture ? Input.MouseModeEnum.Visible : free ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (mode == Input.MouseModeEnum.Captured && Input.MouseMode != mode)
        {
            // Recupera el control sin clics accidentales ni salto de cámara.
            _ignoreMotion = 3;
            _suppressPrimary = Input.IsActionPressed("primary");
        }
        Input.MouseMode = mode;
    }

    public void SetTool(Tool t)
    {
        if (_tool == Tool.Cutter && t != Tool.Cutter) _stowedBeforeTool = _stowed;
        if (t == Tool.Remove && _tool != Tool.Remove)
        {
            _removeArmedAt = Time.GetTicksMsec() + RemoveArmMs;
            _hud?.Toast(Loc.T("notice_remove_on"), Ui.Warn);
        }
        _tool = t;
        _stowed = false;
        _hud.SetTool((int)t);
        _railPlacing = false;
        Belts.HidePreview();
        _ground.SetShaderParameter("grid_strength", t is Tool.Build or Tool.Rail or Tool.Link ? 1f : 0f);
        if (t != Tool.Build) RemoveGhost();
        if (t != Tool.Rail) RemoveRailGhost();
        HideCellMark();
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
        _rig.OnGesture();
    }

    /// <summary>Acción principal de la herramienta activa sobre lo que apunta la cámara.</summary>
    public void Primary()
    {
        var client = Session.Client;
        if (_tool != Tool.Cutter) _rig.OnTap();
        switch (_tool)
        {
            case Tool.Cutter:
                if (_aim.kind == "debris")
                {
                    client.Send(new Command { Type = CommandType.CutDebris, A = _aim.id });
                    _toolAudio.Hit(_debrisKinds.GetValueOrDefault(_aim.id));
                    _rig.OnHit();
                    if (_main.Prefs.CameraBob && !Scenery.Calm) _kick = 0.0032f * _main.Prefs.BobIntensity;
                }
                break;
            case Tool.Build:
                // La pieza sigue activa tras colocar. Lo que se sabe inválido no se envía: se explica y se sigue en el modo.
                if (!TryGhostCell(out var x, out var z))
                {
                    Notify(Loc.T("build_aim_ground"));
                    break;
                }
                var buildDef = client.World!.Content.Machines[_buildDef];
                var buildError = client.World.PlacementSiteError(client.LocalPlayer!, _buildDef, x, z, _buildRot, out _);
                if (buildError == null && !client.World.CanAfford(client.LocalPlayer!, buildDef.Credits, buildDef.Cost)) buildError = "err_cost";
                if (buildError != null)
                {
                    Notify(Loc.T(buildError));
                    break;
                }
                client.Send(new Command { Type = CommandType.Place, Text = _buildDef, A = x, B = z, C = _buildRot });
                Sfx.Play("build");
                break;
            case Tool.Link:
                var beltHint = Belts.ToolClick(client, _aim.kind, _aim.id, TryGroundPoint(out var beltGround) ? beltGround : null);
                if (beltHint != null)
                {
                    _hud.Toast(Loc.T(beltHint));
                    Sfx.Play("error");
                }
                break;
            case Tool.Remove:
                var (removeKind, removeId) = RemoveTarget();
                if (removeKind.Length == 0) break;
                var removal = PreviewRemoval(client.World!, client.LocalPlayer!, removeKind, removeId);
                if (removal.Error != null)
                {
                    Notify(Loc.T(removal.Error));
                    break;
                }
                client.Send(new Command { Type = removeKind switch { "machine" => CommandType.Remove, "belt" => CommandType.Unlink, _ => CommandType.RemoveRail }, A = removeId });
                Sfx.Play("remove");
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
        Belts.ToolResult(id, result);
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
            // Herramienta del modelo externo. El modelo mira a +Z; la mano derecha queda en -X.
            var tool = new RemoteToolView { Position = new Vector3(-0.3f, 1.02f, 0.42f), RotationDegrees = new Vector3(0, 180, 0) };
            root.AddChild(tool);
            r = new RemotePlayer { Root = root, Tool = tool };
            _remote[num] = r;
        }
        r.Target = new Vector3(x, y, z);
        r.TargetYaw = yaw;
        r.LastSeen = _time;
    }

    private void OnRemoteAction(int num, byte action)
    {
        if (_remote.TryGetValue(num, out var r)) r.Action = action;
    }

    // ---------- Bucle ----------

    public override void _Process(double delta)
    {
        _time += delta;
        var client = Session.Client;
        var pollStart = Time.GetTicksUsec();
        client.Poll();
        MaxPollMs = Math.Max(MaxPollMs, (Time.GetTicksUsec() - pollStart) / 1000f);
        if (Session.Runner?.Fault is { } fault)
        {
            GD.PrintErr("Fallo del servidor local: " + fault);
            _main.EndSession(Loc.T("reject_server_fault"));
            return;
        }
        if (client.State == ClientState.Closed)
        {
            _main.EndSession(client.CloseReason == "reject_left" ? "" : Loc.T(client.CloseReason));
            return;
        }
        if (!_worldReady) return;
        if (_worldDirty)
        {
            _worldDirty = false;
            var t0 = Time.GetTicksUsec();
            SyncWorld();
            MaxSyncMs = Math.Max(MaxSyncMs, (Time.GetTicksUsec() - t0) / 1000f);
        }

        _head.Rotation = new Vector3(_pitch, 0, 0);
        _body.Rotation = new Vector3(0, _yaw, 0);

        var local = client.LocalPlayer;
        if (!_inFlight && local is { TransitTo: >= 0 }) StartFlight(local);
        if (!_inFlight && local != null && local.Planet != _viewPlanet)
        {
            // Llegó mientras se reconectaba u otra causa: se alinea sin dejarlo en el vacío.
            ClearWorldView();
            SetPlanetLook(local.Planet);
            _body.GlobalPosition = new Vector3(local.X, 0.2f, local.Z);
            _worldDirty = true;
        }
        if (_inFlight) UpdateFlight(delta);

        AnimateMachines((float)delta);
        Belts.Process(client.World!, _body.GlobalPosition, (float)delta);
        UpdateTrains(delta);
        if (local != null) UpdateObjectiveMarker(client.World!, local);
        UpdateRemote(delta);
        UpdateAudio(delta);
        UpdateTool(delta);
        UpdateRig((float)delta);

        _moveTimer -= delta;
        if (_moveTimer <= 0 && !_inFlight)
        {
            _moveTimer = 0.05;
            var p = _body.GlobalPosition;
            var equipped = _stowed ? PlayerAction.Stowed : _tool == Tool.Cutter ? PlayerAction.Cutter : PlayerAction.Device;
            client.SendMove(p.X, p.Y, p.Z, _yaw, PlayerAction.Pack(equipped, _working));
            var me = client.LocalPlayer;
            if (me != null) { me.X = p.X; me.Y = p.Y; me.Z = p.Z; }
        }
    }

    /// <summary>Alimenta los brazos con el estado del jugador. Solo representación.</summary>
    private void UpdateRig(float dt)
    {
        if (_ignoreMotion > 0) _ignoreMotion--;
        var hidden = _inFlight || _paused || _hud.PanelOpen || _hud.ChatOpen || _inv.IsOpen;
        var input = new FirstPersonRig.Input
        {
            Mode = _stowed || hidden ? RigMode.Stowed : _tool == Tool.Cutter ? RigMode.Cutter : RigMode.Device,
            Tool = _tool,
            Velocity = _body.Velocity,
            OnFloor = _body.IsOnFloor(),
            Sprinting = Input.IsActionPressed("sprint"),
            Working = _working,
            BobOn = _main.Prefs.CameraBob,
            BobScale = _main.Prefs.BobIntensity,
            Calm = Scenery.Calm,
            WallDistance = _wallDistance,
            Look = _lookAccum / Mathf.Max(dt, 0.001f) * 0.016f,
            Target = _working ? _aimPoint : null,
        };
        _lookAccum = Vector2.Zero;
        _rig.Tick(dt, input);
        _toolAudio.Motor(dt, _working);

        _kick = Mathf.MoveToward(_kick, 0f, dt * 0.03f);
        _camera.Rotation = new Vector3(_kick * Mathf.Sin((float)_time * 70f), _kick * 0.6f * Mathf.Sin((float)_time * 53f), 0);
    }

    private void UpdateHighlight(GeometryInstance3D? target, Color color)
    {
        if (_highlighted != target)
        {
            if (_highlighted != null && GodotObject.IsInstanceValid(_highlighted)) _highlighted.MaterialOverlay = null;
            _highlighted = target;
            if (target != null) target.MaterialOverlay = HighlightMaterial;
        }
        if (target == null) return;
        HighlightMaterial.SetShaderParameter("tint", new Vector3(color.R, color.G, color.B));
        // Al desmontar, el objetivo se tiñe entero: debe quedar inequívoco entre varias máquinas pegadas.
        HighlightMaterial.SetShaderParameter("fill", _tool == Tool.Remove ? 0.2f : 0f);
    }

    private static readonly ShaderMaterial HighlightMaterial = new() { Shader = new Shader { Code = """
        shader_type spatial;
        render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled;
        uniform vec3 tint = vec3(1.0, 0.67, 0.25);
        uniform float fill = 0.0;
        void fragment(){
          float rim = pow(1.0 - clamp(abs(dot(NORMAL, VIEW)), 0.0, 1.0), 4.0);
          ALBEDO = tint;
          ALPHA = rim * 0.35 + 0.004 + fill + 0.003 * sin(TIME * 5.0);
        }
        """ } };

    private void AnimateMachines(float delta)
    {
        var t = (float)_time;
        foreach (var node in _machines.Values)
        {
            var running = node.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.Overheated or MachineStatus.Generating or MachineStatus.Storing;
            if (node.Rotor != null && running) node.Rotor.RotateObjectLocal(node.RotorAxis, delta * node.RotorSpeed);
            if (node.Piston != null)
            {
                var offset = node.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.Overheated ? (Mathf.Sin(t * 3.2f + node.Phase) + 1f) * 0.3f : 0.6f;
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
            r.Tool.Tick((float)delta, PlayerAction.Equipped(r.Action), PlayerAction.IsWorking(r.Action));
        }
    }

    private void UpdateAudio(double delta)
    {
        _humTimer -= delta;
        if (_humTimer > 0) return;
        _humTimer = 0.5;
        var pos = _body.GlobalPosition;
        var running = _machines.Values.Where(n => n.Running).ToList();
        var nearest = running
            .OrderBy(n => n.Root.GlobalPosition.DistanceSquaredTo(pos))
            .Take(FactoryActivity.MaxSites)
            .ToList();
        // Mezcla limitada: la potencia conjunta se reparte entre las voces activas, sin sumar hasta saturar.
        var voices = Mathf.Min(nearest.Count, _hums.Count);
        var voiceDb = Sfx.MachineDb - 10f * Mathf.Log(Mathf.Max(voices, 1)) / Mathf.Log(10f) * 0.8f;
        for (var i = 0; i < _hums.Count; i++)
        {
            if (i < nearest.Count)
            {
                _hums[i].GlobalPosition = nearest[i].Root.GlobalPosition + Vector3.Up;
                _hums[i].VolumeDb = voiceDb;
                var loop = Sfx.LoopOf(nearest[i].Def);
                if (_hums[i].Stream != loop)
                {
                    _hums[i].Stop();
                    _hums[i].Stream = loop;
                }
                if (!_hums[i].Playing) _hums[i].Play();
            }
            else if (_hums[i].Playing)
            {
                _hums[i].Stop();
            }
        }
        var sites = new List<FactoryActivity.Site>(nearest.Count);
        foreach (var n in nearest) sites.Add(new FactoryActivity.Site(n.Root, n.Def, n.Height));
        _activity.Refresh(sites, Belts.NearestMoving(pos), pos, running.Count);
        UpdateVisibleStock(pos);
    }

    /// <summary>
    /// Lo que espera en cada boca, a la vista sobre la zapata de las máquinas de producción cercanas: entrada a un lado y salida al otro.
    /// Crece con la cantidad real en escala logarítmica, y como máximo seis piezas por boca.
    /// </summary>
    private void UpdateVisibleStock(Vector3 pos)
    {
        var stock = Belts.VisibleStock;
        stock.Clear();
        var w = Session.Client.World!;
        foreach (var (id, node) in _machines)
        {
            if (node.Root.GlobalPosition.DistanceSquaredTo(pos) > 26f * 26f || !w.Machines.TryGetValue(id, out var m)) continue;
            var def = w.Content.Machines[m.Def];
            if (def.Recipe == null || def.W < 2 || def.D < 2) continue;
            var halfW = def.W * Cell / 2;
            var halfD = def.D * Cell / 2;
            foreach (var (items, x, z) in new[] { (m.Out.Items, halfW - 0.62f, halfD - 0.22f), (m.In.Items, -(halfW - 0.62f), -(halfD - 0.22f)) })
            {
                var first = items.FirstOrDefault(kv => kv.Value > 0);
                if (first.Value <= 0) continue;
                var pieces = Mathf.Min(6, 1 + Mathf.FloorToInt(Mathf.Log(first.Value) / Mathf.Log(2f)));
                for (var i = 0; i < pieces; i++)
                {
                    // Tres en la fila y el resto encima, hacia la boca desde la esquina.
                    var along = (i % 3) * 0.36f * (x > 0 ? -1f : 1f);
                    stock.Add((first.Key, node.Root.GlobalTransform * new Vector3(x + along, 0.235f + (i / 3) * 0.13f, z)));
                }
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_worldReady || _inFlight) return;
        var dt = (float)delta;
        var dir = Vector3.Zero;
        var typing = _hud.ChatOpen || _hud.PanelOpen || _inv.IsOpen || _paused;
        if (AutoWalkTarget is { } target)
        {
            var flat = target - _body.GlobalPosition;
            flat.Y = 0;
            if (flat.Length() < 0.6f) AutoWalkTarget = null;
            else
            {
                dir = flat.Normalized();
                // Rodeo sencillo del recorrido de prueba: si deja de avanzar, se aparta un momento.
                _autoStuck = _body.GetRealVelocity().Length() < 1.5f ? _autoStuck + dt : 0;
                if (_autoStuck > 0.35f)
                {
                    _autoSidestep = 0.7f;
                    if (_body.IsOnFloor()) _body.Velocity = _body.Velocity with { Y = 4.2f };
                }
                if (_autoSidestep > 0)
                {
                    _autoSidestep -= dt;
                    dir = (dir + dir.Cross(Vector3.Up) * 1.6f).Normalized();
                    _autoStuck = 0;
                }
            }
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
            _camera.Position = new Vector3(0, Mathf.Sin(_bobPhase) * 0.035f * _main.Prefs.BobIntensity, 0);
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
        // Las cintas no tienen cuerpo físico: se apuntan por geometría si nada más próximo tapa el rayo.
        var belt = Belts.Pick(Session.Client.World!, from, (to - from).Normalized(), 34f, out var beltDistance);
        if (belt >= 0 && _aim.kind != "debris" && beltDistance < from.DistanceTo(_aimPoint))
        {
            _aim = ("belt", belt);
            _aimPoint = from + (to - from).Normalized() * beltDistance;
        }

        // Distancia al primer sólido delante de la cámara: gobierna la retracción de los brazos.
        var wallQuery = PhysicsRayQueryParameters3D.Create(from, from - _camera.GlobalBasis.Z * 1.3f, 1);
        var wall = GetWorld3D().DirectSpaceState.IntersectRay(wallQuery);
        _wallDistance = wall.Count > 0 ? from.DistanceTo((Vector3)wall["position"]) : 9f;
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

    private const float RailPickRadius = 1.1f;

    /// <summary>Hay que apuntar a la propia vía. Los tramos de una estación se retiran con ella.</summary>
    private int? NearestRail()
    {
        if (!TryGroundPoint(out var p)) return null;
        var w = Session.Client.World!;
        int? found = null;
        var best = RailPickRadius * RailPickRadius;
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
        var blocked = _hud.PanelOpen || _hud.ChatOpen || _paused || _inFlight || _inv.IsOpen;
        var status = "";
        var statusColor = Ui.Ink;
        var prompt = "";
        float? progress = null;
        var progressColor = Ui.Amber;
        GeometryInstance3D? highlight = null;
        var highlightColor = Ui.Amber;
        Machine? lookAt = null;

        if (_aim.kind == "machine" && w.Machines.TryGetValue(_aim.id, out var aimed))
        {
            // En producción, el estado va con icono y progreso en la lectura junto al punto de mira.
            lookAt = w.Content.Machines[aimed.Def].Kind == MachineKind.Production ? aimed : null;
            status = lookAt != null ? Loc.T("machine_" + aimed.Def) : $"{Loc.T("machine_" + aimed.Def)} — {Loc.T("status_" + aimed.Status)}";
            statusColor = lookAt != null ? Ui.Ink : aimed.Status is MachineStatus.NoInput or MachineStatus.OutputFull or MachineStatus.NoPower or MachineStatus.LowPower or MachineStatus.Overheated ? Ui.Bad : Ui.Good;
            var inReach = MachineInReach(w, aimed);
            prompt = Loc.T(inReach ? "prompt_interact" : "prompt_too_far");
            if (_machines.TryGetValue(aimed.Id, out var aimedNode)) highlight = aimedNode.Root.GetChildOrNull<GeometryInstance3D>(0);
            highlightColor = _tool == Tool.Remove ? Ui.Bad : inReach ? Ui.Amber : Ui.Dim;
        }
        else if (_aim.kind == "debris" && w.DebrisById.TryGetValue(_aim.id, out var aimedDebris))
        {
            var dropDef = w.Content.Balance.Debris[aimedDebris.Def];
            status = $"{Loc.T("item_" + dropDef.Drop)} · {aimedDebris.Hp} / {dropDef.Hp}";
            var debrisPos = new Vector3((aimedDebris.X + 0.5f) * Cell, 0.6f, (aimedDebris.Z + 0.5f) * Cell);
            var inReach = _body.GlobalPosition.DistanceTo(debrisPos) <= WorkReach;
            prompt = Loc.T(_stowed ? "prompt_stowed" : inReach ? "prompt_cut" : "prompt_too_far");
            progress = aimedDebris.Hp / (float)dropDef.Hp;
            progressColor = inReach ? Ui.Amber : Ui.Dim;
            highlight = _debris.GetValueOrDefault(aimedDebris.Id);
            highlightColor = inReach && !_stowed ? Ui.Amber : Ui.Dim;
        }
        else if (_aim.kind == "belt")
        {
            status = BeltView.Describe(w, _aim.id, out var beltStatus);
            statusColor = beltStatus is BeltStatus.BlockedFull or BeltStatus.BlockedNoPower or BeltStatus.SourceNoPower ? Ui.Bad : beltStatus == BeltStatus.Flowing ? Ui.Good : Ui.Ink;
        }

        if (_suppressPrimary && !Input.IsActionPressed("primary")) _suppressPrimary = false;
        var holding = !blocked && !_suppressPrimary && (AutoHoldPrimary || Input.IsActionPressed("primary"));
        var cutting = false;
        var impact = ImpactKind.Scrap;
        switch (_tool)
        {
            case Tool.Cutter:
                _cutTimer -= delta;
                if (holding && !_stowed && _aim.kind == "debris" && w.DebrisById.TryGetValue(_aim.id, out var target))
                {
                    // Solo se trabaja al alcance del cabezal; al soltar, alejarse o abrir un menú se interrumpe.
                    var targetPos = new Vector3((target.X + 0.5f) * Cell, 0.6f, (target.Z + 0.5f) * Cell);
                    if (_body.GlobalPosition.DistanceTo(targetPos) <= Mathf.Min(WorkReach, w.Content.Balance.CutterRangeMm / 1000f))
                    {
                        cutting = true;
                        impact = _debrisKinds.GetValueOrDefault(target.Id);
                        if (_cutTimer <= 0)
                        {
                            _cutTimer = w.Content.Balance.CutterCooldownTicks / (float)w.Content.Balance.TicksPerSecond + 0.01f;
                            Primary();
                        }
                    }
                }
                if (_stowed && _aim.kind == "") prompt = Loc.T("prompt_stowed");
                break;
            case Tool.Build:
                prompt = Loc.T("prompt_build");
                highlight = null;
                UpdateGhost(w, me, ref status, ref statusColor, ref prompt);
                break;
            case Tool.Link:
                prompt = Belts.ToolFrame(w, me, _aim.kind, _aim.id, _aimPoint, TryGroundPoint(out var beltAim) ? beltAim : null, client.StructuralDeltas, (float)delta, ref status, ref statusColor);
                break;
            case Tool.Remove:
                UpdateRemoval(w, me, ref status, ref statusColor, ref prompt, ref highlight, ref highlightColor);
                break;
            case Tool.Rail:
                prompt = Loc.T(_railPlacing ? "prompt_rail_to" : "prompt_rail_from");
                UpdateRailGhost(w, me, ref status, ref statusColor);
                break;
        }
        _working = cutting;
        _impacts.Work(cutting ? impact : null, _aimPoint, (_camera.GlobalPosition - _aimPoint).Normalized());
        UpdateHighlight(blocked ? null : highlight, highlightColor);
        if (_cellMark != null) _cellMark.Visible = !blocked;
        // Mientras trabaja, el cabezal ocupa esa zona: basta con el estado y la barra de vida.
        if (cutting) prompt = "";
        _targetBar.Set(blocked || _tool != Tool.Cutter ? null : progress, progressColor);
        _hud.SetPrompt(blocked ? "" : status, statusColor, blocked ? "" : prompt);
        if (lookAt != null && !blocked && _tool is Tool.Cutter or Tool.Remove) _hud.Look.Present(w, lookAt, delta);
        else _hud.Look.Visible = false;
        if (_chain.Active && w.Machines.TryGetValue(_chain.Seed, out var chainSeed) && chainSeed.Planet != me.Planet) _chain.Clear();
        _chain.Refresh(w, delta);
    }

    /// <summary>
    /// Orientación de la malla. Las de producción se modelan con la salida en +Z, pero sus puertos de cinta salen por −Z con
    /// Rot 0 (World.FrontDir): media vuelta las hace coincidir. Las demás reciben por cualquier cara y conservan su frente.
    /// </summary>
    public static int ModelYawDegrees(MachineDef def, int rot) => rot * 90 + (def.Kind == MachineKind.Production ? 180 : 0);

    /// <summary>Hacia dónde queda la salida modelada (+Z local) con este giro. La escena de pruebas la compara con los puertos.</summary>
    public static Vector3 ModelFront(MachineDef def, int rot) => Vector3.Back.Rotated(Vector3.Up, Mathf.DegToRad(ModelYawDegrees(def, rot)));

    public int BuildRotation => _buildRot;
    /// <summary>0 lejos, 1 pegado a la pieza: a esa distancia las flechas de los puertos también se atenúan.</summary>
    private float NearCellMarkFade { set { if (_cellMark != null) _cellMark.Transparency = value * 0.7f; _cellMarkFade = value; } }
    private float _cellMarkFade;
    public string StatusText => _hud.StatusText;

    private void UpdateGhost(World w, PlayerState me, ref string status, ref Color color, ref string prompt)
    {
        var def = w.Content.Machines[_buildDef];
        if (_ghostDef != _buildDef)
        {
            RemoveGhost();
            _ghostDef = _buildDef;
            _ghost = new Node3D();
            _ghost.AddChild(new MeshInstance3D { Mesh = Models.Machine(_buildDef).Body, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            AddChild(_ghost);
        }
        status = $"{Loc.T("machine_" + _buildDef)} — {Ui.Cost(def.Credits, def.Cost)}";
        color = Ui.Ink;
        if (!TryGhostCell(out var x, out var z))
        {
            _ghost!.Visible = false;
            HideCellMark();
            status += "\n" + Loc.T("build_aim_ground");
            return;
        }
        var (fw, fd) = _buildRot % 2 == 0 ? (def.W, def.D) : (def.D, def.W);
        _ghost!.Visible = true;
        _ghost.GlobalPosition = new Vector3((x + fw / 2f) * Cell, 0.02f, (z + fd / 2f) * Cell);
        _ghost.RotationDegrees = new Vector3(0, ModelYawDegrees(def, _buildRot), 0);

        var error = w.PlacementSiteError(me, _buildDef, x, z, _buildRot, out _);
        var afford = w.CanAfford(me, def.Credits, def.Cost);
        var ghostMaterial = error != null ? _ghostBad : afford ? _ghostOk : _ghostLacking;
        // De cerca la pieza llenaría la pantalla: se aclara al acercarse y, dentro de ella, queda solo la marca del suelo.
        var cam = _camera.GlobalPosition;
        var radius = Mathf.Max(fw, fd) * Cell / 2f;
        var away = new Vector2(cam.X - _ghost.GlobalPosition.X, cam.Z - _ghost.GlobalPosition.Z).Length() - radius;
        var fade = Mathf.Clamp((away - 1f) / 6f, 0f, 1f);
        var ghostMesh = (MeshInstance3D)_ghost.GetChild(0);
        ghostMesh.Visible = away > 0.2f;
        var tint = ghostMaterial.AlbedoColor;
        tint.A = Mathf.Lerp(0.06f, 0.42f, fade);
        ghostMaterial.AlbedoColor = tint;
        ghostMesh.MaterialOverride = ghostMaterial;
        NearCellMarkFade = 1f - fade;
        if (error != null) status += "\n" + Loc.T(error);
        if (!afford) status += "\n" + EntryInfo.MissingText(w, me, def);
        if (error != null || !afford) color = error != null ? Ui.Bad : Ui.Warn;

        // Las celdas exactas que ocuparía y los mismos puertos que tendrá la máquina: lo que se ve es lo que queda.
        var cellColor = error != null ? Ui.Bad : afford ? Ui.Good : Ui.Warn;
        var ports = w.BeltPorts(me.Planet, _buildDef, x, z, _buildRot, true).Concat(w.BeltPorts(me.Planet, _buildDef, x, z, _buildRot, false));
        ShowCellMark($"build|{_buildDef}|{x}|{z}|{_buildRot}|{cellColor.ToHtml()}|{Session.Client.StructuralDeltas}", w.Footprint(_buildDef, x, z, _buildRot), cellColor, ports);
        if (w.CanSendByBelt(def) || w.CanReceiveByBelt(def)) prompt += "\n" + Loc.T("build_ports_legend");
    }

    // ---------- Desmontar ----------

    /// <summary>Solo lo que está directamente en la mira; apuntar a escombros o a nada nunca selecciona algo cercano.</summary>
    private (string kind, int id) RemoveTarget()
    {
        if (_aim.kind is "machine" or "belt") return _aim;
        return _aim.kind.Length == 0 && NearestRail() is { } rail ? ("rail", rail) : ("", -1);
    }

    private static RemovalPreview PreviewRemoval(World w, PlayerState me, string kind, int id) => kind switch
    {
        "machine" => w.PreviewRemoveMachine(me, id),
        "belt" => w.PreviewRemoveBelt(me, id),
        _ => w.PreviewRemoveRail(me, id),
    };

    private void UpdateRemoval(World w, PlayerState me, ref string status, ref Color color, ref string prompt, ref GeometryInstance3D? highlight, ref Color highlightColor)
    {
        var (kind, id) = RemoveTarget();
        highlight = null;
        if (kind.Length == 0)
        {
            HideCellMark();
            status = "";
            prompt = Loc.T("prompt_remove_aim");
            return;
        }
        var preview = PreviewRemoval(w, me, kind, id);
        var name = Loc.T("belt_title");
        var cells = new List<(int x, int z)>();
        if (kind == "machine" && w.Machines.TryGetValue(id, out var machine))
        {
            name = Loc.T("machine_" + machine.Def);
            cells.AddRange(w.Footprint(machine.Def, machine.X, machine.Z, machine.Rot));
            if (_machines.TryGetValue(id, out var node)) highlight = node.Root.GetChildOrNull<GeometryInstance3D>(0);
        }
        else if (kind == "belt" && w.Links.TryGetValue(id, out var belt))
        {
            cells.AddRange(belt.Cells);
            if (cells.Count == 0 && w.Machines.TryGetValue(belt.From, out var from) && w.Machines.TryGetValue(belt.To, out var to))
            {
                // Cinta elevada: no ocupa celdas, se marca la recta que recorre sobre el suelo.
                var (ax, az) = w.MachineCenterMm(from);
                var (bx, bz) = w.MachineCenterMm(to);
                var steps = Mathf.Max(1, Mathf.CeilToInt(new Vector2(bx - ax, bz - az).Length() / (1000f * Cell)));
                for (var i = 0; i <= steps; i++)
                    cells.Add((Mathf.FloorToInt(Mathf.Lerp(ax, bx, i / (float)steps) / (1000f * Cell)), Mathf.FloorToInt(Mathf.Lerp(az, bz, i / (float)steps) / (1000f * Cell))));
            }
        }
        else if (kind == "rail" && w.RailSegments.TryGetValue(id, out var segment))
        {
            name = Loc.T("remove_rail");
            cells.AddRange(RailMath.Cells(w.RailNodes[segment.A], w.RailNodes[segment.B], segment.Shape));
            highlight = _rails.GetValueOrDefault(id);
        }

        highlightColor = preview.Error == null ? Ui.Bad : Ui.Dim;
        ShowCellMark($"remove|{kind}|{id}|{preview.Error}|{Session.Client.StructuralDeltas}", cells, preview.Error == null ? Ui.Bad : Ui.Dim);
        if (preview.Error != null)
        {
            status = $"{name}\n{Loc.T(preview.Error)}";
            color = Ui.Bad;
            prompt = Loc.T("prompt_remove_blocked");
            return;
        }
        status = Loc.T("remove_target", name);
        status += "\n" + (preview.Credits > 0 || preview.Items.Count > 0 ? Loc.T("remove_recover", Ui.Cost(preview.Credits, preview.Items)) : Loc.T("remove_nothing"));
        if (preview.Items.Count > 0) status += "\n" + Loc.T("remove_destination", Loc.T("machine_depot"));
        if (preview.Belts > 0) status += "\n" + Loc.T("remove_with_belts", preview.Belts.ToString());
        color = Ui.Warn;
        prompt = Loc.T("prompt_remove");
    }

    // ---------- Marca de celdas ----------

    private void HideCellMark()
    {
        _cellMark?.QueueFree();
        _cellMark = null;
        _cellMarkKey = "";
    }

    /// <summary>
    /// Resalta celdas exactas sobre el suelo con relleno y contorno, visibles aunque algo las tape, y las flechas de los puertos
    /// (mismo código de colores que BeltView; la forma indica el sentido sin depender del color). Solo se rehace si cambia la clave.
    /// </summary>
    private void ShowCellMark(string key, IEnumerable<(int x, int z)> cells, Color color, IEnumerable<BeltPort>? ports = null)
    {
        if (key == _cellMarkKey) return;
        HideCellMark();
        _cellMarkKey = key;
        const float Inset = 0.04f, Edge = 0.16f, Y = 0.05f;
        var set = cells.ToHashSet();
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            foreach (var v in new[] { a, b, c, a, c, d }) st.AddVertex(v);
        }
        void Rect(float x0, float z0, float x1, float z1) => Quad(new Vector3(x0, Y, z0), new Vector3(x1, Y, z0), new Vector3(x1, Y, z1), new Vector3(x0, Y, z1));

        st.SetColor(new Color(color.R, color.G, color.B, 0.26f));
        foreach (var (x, z) in set) Rect(x * Cell + Inset, z * Cell + Inset, (x + 1) * Cell - Inset, (z + 1) * Cell - Inset);
        st.SetColor(new Color(color.R, color.G, color.B, 0.95f));
        foreach (var (x, z) in set)
        {
            float x0 = x * Cell, z0 = z * Cell, x1 = x0 + Cell, z1 = z0 + Cell;
            if (!set.Contains((x, z - 1))) Rect(x0, z0, x1, z0 + Edge);
            if (!set.Contains((x, z + 1))) Rect(x0, z1 - Edge, x1, z1);
            if (!set.Contains((x - 1, z))) Rect(x0, z0, x0 + Edge, z1);
            if (!set.Contains((x + 1, z))) Rect(x1 - Edge, z0, x1, z1);
        }
        var anyPort = false;
        foreach (var p in ports ?? Enumerable.Empty<BeltPort>())
        {
            anyPort = true;
            var (dx, dz) = World.BeltDirs[p.Dir];
            var outward = new Vector3(dx, 0, dz);
            var dir = p.Output ? outward : -outward;
            var side = dir.Cross(Vector3.Up);
            var center = new Vector3((p.X + 0.5f) * Cell, Y + 0.01f, (p.Z + 0.5f) * Cell);
            var tail = center + dir * 0.05f;
            var neck = tail + dir * 0.45f;
            var tip = tail + dir * 0.9f;
            st.SetColor(!p.Free ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : p.Output ? new Color(1f, 0.66f, 0.15f, 0.95f) : new Color(0.25f, 0.7f, 1f, p.Side ? 0.6f : 0.95f));
            Quad(tail - side * 0.16f, tail + side * 0.16f, neck + side * 0.16f, neck - side * 0.16f);
            foreach (var v in new[] { neck - side * 0.42f, neck + side * 0.42f, tip }) st.AddVertex(v);
            if (p.Side || !p.Free) continue;
            // Aleta vertical: a ras de suelo la flecha casi no se ve de lejos.
            var lift = Vector3.Up * 0.55f;
            Quad(tail + lift - Vector3.Up * 0.12f, neck + lift - Vector3.Up * 0.12f, neck + lift + Vector3.Up * 0.12f, tail + lift + Vector3.Up * 0.12f);
            foreach (var v in new[] { neck + lift - Vector3.Up * 0.34f, neck + lift + Vector3.Up * 0.34f, tip + lift }) st.AddVertex(v);
        }
        if (set.Count == 0 && !anyPort) return;
        _cellMark = new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = CellMarkMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Transparency = _tool == Tool.Build ? _cellMarkFade * 0.7f : 0f };
        AddChild(_cellMark);
    }

    private static readonly StandardMaterial3D CellMarkMaterial = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        VertexColorUseAsAlbedo = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = true,
    };

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

    public void InjectRemoteForTest(Vector3 at, float yaw, byte action)
    {
        OnRemoteMoved(9000, at.X, at.Y, at.Z, yaw);
        OnRemoteAction(9000, action);
    }

    public void SetRailShape(byte shape) => _railShape = shape;
    public double FlightProgress => _inFlight ? _flightTime / _flightTotal : 0;
    public (string kind, int id) AimTarget => _aim;
    public Vector3? FirstRemotePlayer => _remote.Count > 0 ? _remote.Values.First().Root.GlobalPosition : null;
    public Vector3 PlayerPosition => _body.GlobalPosition;
    public void SetRotation(int rot) => _buildRot = rot;
    public void OpenTechPanel() => _hud.OpenTech();
    public void ClosePanelForTest()
    {
        _hud.ClosePanel();
        _inv.Close();
    }
    public void OpenMachinePanel(int id) => _hud.OpenMachine(id);
}

public static class Controls
{
    private static bool _done;
    private static Settings _prefs = null!;
    private static readonly Dictionary<string, Godot.Key[]> Defaults = new()
    {
        ["move_forward"] = new[] { Godot.Key.W },
        ["move_back"] = new[] { Godot.Key.S },
        ["move_left"] = new[] { Godot.Key.A },
        ["move_right"] = new[] { Godot.Key.D },
        ["sprint"] = new[] { Godot.Key.Shift },
        ["jump"] = new[] { Godot.Key.Space },
        ["chat"] = new[] { Godot.Key.Enter },
        ["inventory"] = new[] { Godot.Key.Tab },
        ["interact"] = new[] { Godot.Key.E },
        ["tech"] = new[] { Godot.Key.J },
        ["players"] = new[] { Godot.Key.P },
        ["hotbar_1"] = new[] { Godot.Key.Key1 },
        ["hotbar_2"] = new[] { Godot.Key.Key2 },
        ["hotbar_3"] = new[] { Godot.Key.Key3 },
        ["hotbar_4"] = new[] { Godot.Key.Key4 },
        ["hotbar_5"] = new[] { Godot.Key.Key5 },
        ["hotbar_6"] = new[] { Godot.Key.Key6 },
        ["hotbar_7"] = new[] { Godot.Key.Key7 },
        ["hotbar_8"] = new[] { Godot.Key.Key8 },
        ["hotbar_9"] = new[] { Godot.Key.Key9 },
        ["tool_cutter"] = new[] { Godot.Key.V },
        ["tool_build"] = new[] { Godot.Key.B, Godot.Key.Q },
        ["tool_link"] = new[] { Godot.Key.L },
        ["tool_remove"] = new[] { Godot.Key.X },
        ["tool_rail"] = new[] { Godot.Key.T },
        ["rotate"] = new[] { Godot.Key.R },
        ["belt_undo"] = new[] { Godot.Key.Backspace },
        ["belt_snap"] = new[] { Godot.Key.N },
        ["belt_port"] = new[] { Godot.Key.M },
        ["cancel"] = new[] { Godot.Key.Z },
        ["interact_insert"] = new[] { Godot.Key.F },
        ["interact_take"] = new[] { Godot.Key.G },
        ["interact_panel"] = new[] { Godot.Key.C },
        ["chain_trace"] = new[] { Godot.Key.H },
    };

    public static IEnumerable<string> Rebindable => Defaults.Keys;

    public static void Setup(Settings prefs)
    {
        _prefs = prefs;
        if (prefs.ControlsVersion < ControlsMigration.Current)
        {
            ControlsMigration.Migrate(prefs.Bindings, prefs.ControlsVersion);
            prefs.ControlsVersion = ControlsMigration.Current;
            prefs.Save();
        }
        if (_done) return;
        _done = true;
        InputMap.AddAction("pause");
        InputMap.ActionAddEvent("pause", new InputEventKey { PhysicalKeycode = Godot.Key.Escape });
        foreach (var action in Defaults.Keys)
        {
            InputMap.AddAction(action);
            Apply(action);
        }
        ResolveConflicts();
        Mouse("primary", MouseButton.Left);
        Mouse("secondary", MouseButton.Right);
    }

    private static void Apply(string action)
    {
        InputMap.ActionEraseEvents(action);
        if (_prefs.Bindings.TryGetValue(action, out var custom))
        {
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = (Godot.Key)custom });
            return;
        }
        foreach (var k in Defaults[action]) InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
    }

    /// <summary>Una reasignación del jugador siempre gana: la tecla deja de pertenecer a otro valor por omisión.</summary>
    private static void ResolveConflicts()
    {
        foreach (var (action, code) in _prefs.Bindings)
            foreach (var other in Defaults.Keys.Where(a => a != action))
                foreach (var e in InputMap.ActionGetEvents(other).OfType<InputEventKey>().ToList())
                    if (e.PhysicalKeycode == (Godot.Key)code) InputMap.ActionEraseEvent(other, e);
    }

    public static void Rebind(string action, Godot.Key key)
    {
        _prefs.Bindings[action] = (long)key;
        // Una tecla hace una sola cosa: quien la tuviera vuelve a sus valores si no chocan, o queda sin ella.
        foreach (var other in Defaults.Keys.Where(a => a != action).ToList())
        {
            if (!_prefs.Bindings.TryGetValue(other, out var bound) || bound != (long)key) continue;
            _prefs.Bindings.Remove(other);
            Apply(other);
        }
        Apply(action);
        foreach (var other in Defaults.Keys.Where(a => a != action))
            foreach (var e in InputMap.ActionGetEvents(other).OfType<InputEventKey>().ToList())
                if (e.PhysicalKeycode == key) InputMap.ActionEraseEvent(other, e);
        _prefs.Save();
    }

    public static void ResetAll()
    {
        _prefs.Bindings.Clear();
        foreach (var action in Defaults.Keys) Apply(action);
        _prefs.Save();
    }

    public static string KeyName(string action)
    {
        if (!InputMap.HasAction(action)) return action;
        var key = InputMap.ActionGetEvents(action).OfType<InputEventKey>().LastOrDefault();
        return key == null ? "—" : OS.GetKeycodeString(key.PhysicalKeycode);
    }

    public static string KeyNames(string action)
    {
        if (!InputMap.HasAction(action)) return action;
        return string.Join(" / ", InputMap.ActionGetEvents(action).OfType<InputEventKey>().Select(k => OS.GetKeycodeString(k.PhysicalKeycode)));
    }

    private static void Mouse(string action, MouseButton button)
    {
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = button });
    }
}
