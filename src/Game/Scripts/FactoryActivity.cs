using Godot;

namespace Pluton.Game;

/// <summary>
/// Actividad perceptible de la fábrica, solo representación. Un conjunto fijo de emisores, luces y voces se reparte cada medio
/// segundo entre las máquinas en marcha más cercanas: el coste no crece con el tamaño de la fábrica, y lo que se percibe sí.
/// </summary>
public partial class FactoryActivity : Node3D
{
    public const int MaxSites = 8;
    private const int MaxLights = 4;
    private const float Range = 38f;

    public readonly record struct Site(Node3D Root, string Def, float Height);

    public enum Puff { None, Smoke, Steam, Dust }

    private sealed class Vent
    {
        public GpuParticles3D Particles = null!;
        public Puff Kind;
    }

    private readonly List<Vent> _vents = new();
    private readonly List<OmniLight3D> _lights = new();
    private readonly List<AudioStreamPlayer3D> _beltVoices = new();
    private AudioStreamPlayer _bed = null!;
    private bool _calm;
    private float _bedLevel;

    /// <summary>Emisores y luces encendidos en el último reparto, para las comprobaciones.</summary>
    public int ActiveVents { get; private set; }
    public int ActiveLights { get; private set; }
    public int MaxVentAmount => _vents.Count == 0 ? 0 : _vents[0].Particles.Amount;
    public float BedDb => _bed.VolumeDb;

    /// <summary>Energía de la luz propia de la máquina: en marcha ilumina su entorno; parada queda en piloto.</summary>
    public static float GlowEnergy(bool running) => running ? 0.85f : 0.14f;

    public override void _Ready()
    {
        for (var i = 0; i < MaxSites; i++) _vents.Add(MakeVent());
        for (var i = 0; i < MaxLights; i++)
        {
            var light = new OmniLight3D { LightColor = new Color(1f, 0.72f, 0.42f), LightEnergy = 0.6f, LightSpecular = 0.2f, OmniRange = 8f, Visible = false, DistanceFadeEnabled = true, DistanceFadeBegin = 30, DistanceFadeLength = 8 };
            AddChild(light);
            _lights.Add(light);
        }
        for (var i = 0; i < 2; i++)
        {
            var voice = new AudioStreamPlayer3D { Stream = Sfx.Belt, MaxDistance = 18, UnitSize = 3.5f };
            AddChild(voice);
            _beltVoices.Add(voice);
        }
        _bed = new AudioStreamPlayer { Stream = Sfx.Factory, VolumeDb = -60 };
        AddChild(_bed);
        ApplyCalm();
    }

    /// <summary>Qué sale de cada máquina y dónde, en coordenadas del modelo. Las que no tienen proceso visible no emiten.</summary>
    private static (Puff kind, Vector3 at) VentOf(string def, float height) => def switch
    {
        "smelter" => (Puff.Smoke, new Vector3(0.55f, 5.35f, -0.85f)),
        "wafer_refinery" or "nav_fab" => (Puff.Smoke, new Vector3(0, height + 0.1f, 0)),
        "chiller" or "radiator" or "cryo_radiator" or "cryo_assembler" => (Puff.Steam, new Vector3(0, height + 0.05f, 0)),
        "assembler" => (Puff.Steam, new Vector3(0.6f, height + 0.05f, 0)),
        "recycler" => (Puff.Dust, new Vector3(0, height, 0)),
        "ore_extractor" or "ice_extractor" or "silicon_extractor" => (Puff.Dust, new Vector3(0, 0.5f, 0)),
        _ => (Puff.None, Vector3.Zero),
    };

    private GpuParticles3D Emitter()
    {
        var size = new Curve();
        size.AddPoint(new Vector2(0, 0.35f));
        size.AddPoint(new Vector2(1, 1f));
        var fade = new Gradient { Offsets = new[] { 0f, 0.18f, 1f }, Colors = new[] { new Color(1, 1, 1, 0), Colors.White, new Color(1, 1, 1, 0) } };
        var particles = new GpuParticles3D
        {
            Lifetime = 2.6f,
            LocalCoords = false,
            Emitting = false,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-8, -2, -8), new Vector3(16, 14, 16)),
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 14,
                Gravity = new Vector3(0.1f, 0, 0.06f),
                ScaleCurve = new CurveTexture { Curve = size },
                ScaleMin = 0.8f,
                ScaleMax = 1.25f,
                ColorRamp = new GradientTexture1D { Gradient = fade },
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.18f,
            },
            DrawPass1 = new QuadMesh
            {
                Size = new Vector2(2f, 2f),
                Material = new StandardMaterial3D
                {
                    AlbedoTexture = new GradientTexture2D
                    {
                        Gradient = new Gradient { Offsets = new[] { 0f, 0.5f, 1f }, Colors = new[] { new Color(1, 1, 1, 0.55f), new Color(1, 1, 1, 0.2f), new Color(1, 1, 1, 0) } },
                        Fill = GradientTexture2D.FillEnum.Radial,
                        FillFrom = new Vector2(0.5f, 0.5f),
                        FillTo = new Vector2(1f, 0.5f),
                        Width = 32,
                        Height = 32,
                    },
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha,
                    DistanceFadeMinDistance = 1f,
                    DistanceFadeMaxDistance = 4f,
                },
            },
        };
        return particles;
    }

    private Vent MakeVent()
    {
        var vent = new Vent { Particles = Emitter() };
        AddChild(vent.Particles);
        return vent;
    }

    private static void Tune(Vent vent, Puff kind)
    {
        if (vent.Kind == kind) return;
        vent.Kind = kind;
        var process = (ParticleProcessMaterial)vent.Particles.ProcessMaterial;
        var (color, min, max) = kind switch
        {
            Puff.Smoke => (new Color(0.42f, 0.4f, 0.4f, 0.62f), 0.7f, 1.3f),
            Puff.Steam => (new Color(0.78f, 0.84f, 0.9f, 0.46f), 0.5f, 1f),
            _ => (new Color(0.5f, 0.4f, 0.33f, 0.5f), 0.25f, 0.65f),
        };
        process.Color = color;
        process.InitialVelocityMin = min;
        process.InitialVelocityMax = max;
    }

    /// <summary>«Reducir efectos»: tres emisores con pocas partículas y sin movimiento de luces.</summary>
    private void ApplyCalm()
    {
        _calm = Scenery.Calm;
        foreach (var vent in _vents) vent.Particles.Amount = _calm ? 2 : 7;
    }

    /// <summary>Reparte los recursos entre los sitios en marcha, del más cercano al más lejano. Se llama cada 0,5 s.</summary>
    public void Refresh(IReadOnlyList<Site> sites, IReadOnlyList<Vector3> belts, Vector3 player, int runningTotal)
    {
        if (_calm != Scenery.Calm) ApplyCalm();
        var maxVents = _calm ? 3 : MaxSites;
        var ventUsed = 0;
        var lightUsed = 0;
        foreach (var site in sites)
        {
            if (site.Root.GlobalPosition.DistanceSquaredTo(player) > Range * Range) break;
            var (kind, local) = VentOf(site.Def, site.Height);
            if (kind != Puff.None && ventUsed < maxVents)
            {
                var vent = _vents[ventUsed++];
                Tune(vent, kind);
                vent.Particles.GlobalPosition = site.Root.GlobalTransform * local;
                vent.Particles.Visible = true;
                if (!vent.Particles.Emitting) vent.Particles.Emitting = true;
            }
            if (lightUsed < MaxLights)
            {
                // Foco de trabajo a la altura de la boca de salida, también en las máquinas sin luz propia.
                var light = _lights[lightUsed++];
                light.GlobalPosition = site.Root.GlobalTransform * new Vector3(0, Mathf.Min(site.Height, 2.6f), 2.4f);
                light.Visible = true;
            }
        }
        for (var i = ventUsed; i < _vents.Count; i++)
        {
            // Deja terminar las partículas en el aire y luego deja de dibujarse.
            var p = _vents[i].Particles;
            if (p.Emitting) p.Emitting = false;
            else if (p.Visible) p.Visible = false;
        }
        for (var i = lightUsed; i < _lights.Count; i++) _lights[i].Visible = false;
        ActiveVents = ventUsed;
        ActiveLights = lightUsed;

        for (var i = 0; i < _beltVoices.Count; i++)
        {
            var voice = _beltVoices[i];
            if (i < belts.Count && belts[i].DistanceSquaredTo(player) < 26f * 26f)
            {
                voice.GlobalPosition = belts[i];
                voice.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Sfx.MachineLinear * 0.7f));
                if (!voice.Playing) voice.Play();
            }
            else if (voice.Playing)
            {
                voice.Stop();
            }
        }

        // Fondo de fábrica: una sola voz que crece con el número de máquinas en marcha. Se oye que la base ha crecido.
        var target = Mathf.Clamp(Mathf.Log(1f + runningTotal) / Mathf.Log(2f) / 7f, 0f, 1f);
        _bedLevel = Mathf.Lerp(_bedLevel, target, 0.25f);
        if (_bedLevel < 0.02f)
        {
            if (_bed.Playing) _bed.Stop();
            return;
        }
        _bed.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Sfx.MachineLinear * 0.42f * _bedLevel));
        if (!_bed.Playing) _bed.Play();
    }
}

/// <summary>
/// Respuesta visual y sonora a las acciones importantes. Se dispara desde el estado replicado, no desde el clic:
/// confirma lo que el servidor aceptó, y también se ve y se oye lo que hacen los demás jugadores.
/// </summary>
public partial class ActionFeedback : Node3D
{
    private readonly ImpactEffects _impacts;
    private readonly CanvasLayer _ui;
    private ColorRect _flash = null!;
    private MeshInstance3D _ring = null!;
    private ShaderMaterial _ringMaterial = null!;
    private Label3D _creditLabel = null!;
    private Tween? _ringTween, _flashTween, _creditTween;

    public int Places { get; private set; }
    public int Links { get; private set; }
    public int Removals { get; private set; }
    public int Sales { get; private set; }
    public int UpgradeCount { get; private set; }
    public int Objectives { get; private set; }
    public int Flashes { get; private set; }

    public ActionFeedback(ImpactEffects impacts, CanvasLayer ui)
    {
        _impacts = impacts;
        _ui = ui;
    }

    public override void _Ready()
    {
        _flash = new ColorRect { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore, Material = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type canvas_item;
            uniform vec3 tint = vec3(1.0, 0.7, 0.3);
            uniform float amount = 0.0;
            void fragment(){
              float edge = smoothstep(0.32, 0.86, distance(UV, vec2(0.5)));
              COLOR = vec4(tint, amount * edge);
            }
            """ } } };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(_flash);
        _ui.MoveChild(_flash, 0);

        _ringMaterial = new ShaderMaterial { Shader = new Shader { Code = """
            shader_type spatial;
            render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled;
            uniform vec3 tint = vec3(1.0, 0.66, 0.25);
            uniform float grow = 0.0;
            void fragment(){
              float d = distance(UV, vec2(0.5)) * 2.0;
              float band = smoothstep(grow - 0.14, grow, d) * (1.0 - smoothstep(grow, grow + 0.05, d));
              ALBEDO = tint;
              ALPHA = band * (1.0 - grow) * 0.85;
            }
            """ } };
        _ring = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = Vector2.One, Material = _ringMaterial },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ring);

        _creditLabel = new Label3D { Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 54, PixelSize = 0.007f, OutlineSize = 12, Modulate = new Color(1f, 0.74f, 0.3f), Visible = false, NoDepthTest = true };
        AddChild(_creditLabel);
    }

    /// <summary>Anillo a ras de suelo que se abre desde la pieza. Con «Reducir efectos» no aparece.</summary>
    private void Ring(Vector3 at, float size, Color tint)
    {
        if (Scenery.Calm) return;
        _ringTween?.Kill();
        _ring.GlobalPosition = at with { Y = 0.08f };
        _ring.Scale = new Vector3(size, 1, size);
        _ringMaterial.SetShaderParameter("tint", new Vector3(tint.R, tint.G, tint.B));
        _ringMaterial.SetShaderParameter("grow", 0.1f);
        _ring.Visible = true;
        _ringTween = CreateTween();
        _ringTween.TweenMethod(Callable.From<float>(v => _ringMaterial.SetShaderParameter("grow", v)), 0.1f, 1f, 0.55f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _ringTween.TweenCallback(Callable.From(() => _ring.Visible = false));
    }

    /// <summary>Viñeta breve en los bordes: nunca tapa el centro de la vista. Con «Reducir efectos» no aparece.</summary>
    private void Flash(Color tint, float strength)
    {
        if (Scenery.Calm) return;
        Flashes++;
        _flashTween?.Kill();
        var material = (ShaderMaterial)_flash.Material;
        material.SetShaderParameter("tint", new Vector3(tint.R, tint.G, tint.B));
        material.SetShaderParameter("amount", strength);
        _flash.Visible = true;
        _flashTween = CreateTween();
        _flashTween.TweenMethod(Callable.From<float>(v => material.SetShaderParameter("amount", v)), strength, 0f, 0.7f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Quad);
        _flashTween.TweenCallback(Callable.From(() => _flash.Visible = false));
    }

    public void Placed(Node3D machine, float size)
    {
        Places++;
        var at = machine.GlobalPosition;
        Sfx.PlayAt("place", at + Vector3.Up);
        _impacts.Burst(ImpactKind.Rock, at + Vector3.Up * 0.25f);
        Ring(at, size * 2.4f, new Color(1f, 0.66f, 0.25f));
        if (Scenery.Calm) return;
        // Asienta con un pequeño rebote: se percibe el peso sin mover la cámara.
        machine.Scale = new Vector3(0.94f, 1.08f, 0.94f);
        var tween = machine.CreateTween();
        tween.TweenProperty(machine, "scale", Vector3.One, 0.28f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
    }

    public void BeltLinked(Vector3 at)
    {
        Links++;
        Sfx.PlayAt("belt_link", at);
    }

    public void BeltRemoved(Vector3 at)
    {
        Removals++;
        Sfx.PlayAt("dismantle", at, 0.1f);
        _impacts.Burst(ImpactKind.Scrap, at);
    }

    public void Removed(Vector3 at)
    {
        Removals++;
        Sfx.PlayAt("dismantle", at + Vector3.Up);
        _impacts.Burst(ImpactKind.Scrap, at + Vector3.Up * 0.8f);
        _impacts.Burst(ImpactKind.Rock, at + Vector3.Up * 0.25f);
        Ring(at, 7f, new Color(0.95f, 0.3f, 0.2f));
    }

    public void UpgradeBought()
    {
        UpgradeCount++;
        Sfx.Play("upgrade");
        Flash(new Color(1f, 0.72f, 0.28f), 0.3f);
    }

    public void ObjectiveDone()
    {
        Objectives++;
        Flash(new Color(0.45f, 0.95f, 0.65f), 0.2f);
    }

    /// <summary>Cifra real de la venta sobre la terminal, si se tiene a la vista. El sonido de venta ya lo da GameView.</summary>
    public void Sold(long credits, Vector3? terminal, Vector3 player)
    {
        Sales++;
        if (terminal is not { } at || at.DistanceSquaredTo(player) > 70f * 70f) return;
        // Con venta automática llegan varias por segundo: se deja terminar la cifra anterior.
        if (_creditTween != null && _creditTween.IsRunning()) return;
        _creditLabel.Text = $"+{credits} ¤";
        _creditLabel.GlobalPosition = at;
        _creditLabel.Modulate = _creditLabel.Modulate with { A = 1f };
        _creditLabel.Visible = true;
        _creditTween = CreateTween();
        _creditTween.SetParallel();
        if (!Scenery.Calm) _creditTween.TweenProperty(_creditLabel, "global_position", at + Vector3.Up * 1.6f, 1.3f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        _creditTween.TweenProperty(_creditLabel, "modulate:a", 0f, 0.5f).SetDelay(0.8f);
        _creditTween.SetParallel(false);
        _creditTween.TweenCallback(Callable.From(() => _creditLabel.Visible = false));
    }
}

/// <summary>Baliza de la torre de antena: destello lento que se ve de lejos; con «Reducir efectos» queda fija.</summary>
public partial class LandmarkBeacon : Node3D
{
    private StandardMaterial3D _core = null!, _halo = null!;
    private float _time;

    public override void _Ready()
    {
        _core = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.08f, 0.05f), EmissionEnabled = true, Emission = new Color(1f, 0.1f, 0.06f), EmissionEnergyMultiplier = 6f };
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.85f, Height = 1.7f, RadialSegments = 10, Rings = 5, Material = _core }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _halo = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            DisableFog = true,
            AlbedoColor = new Color(1f, 0.16f, 0.1f, 0.9f),
            AlbedoTexture = new GradientTexture2D
            {
                Gradient = new Gradient { Offsets = new[] { 0f, 0.3f, 1f }, Colors = new[] { Colors.White, new Color(1, 1, 1, 0.3f), new Color(1, 1, 1, 0) } },
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(1f, 0.5f),
                Width = 32,
                Height = 32,
            },
        };
        AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(9f, 9f), Material = _halo }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        // Ciclo de 2,4 s con transición suave: lejos del rango de parpadeo que puede molestar.
        var level = Scenery.Calm ? 0.8f : 0.12f + 0.88f * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(_time * Mathf.Tau / 2.4f)), 2f);
        _core.EmissionEnergyMultiplier = 0.8f + level * 6f;
        _halo.AlbedoColor = _halo.AlbedoColor with { A = level * 0.9f };
    }
}
