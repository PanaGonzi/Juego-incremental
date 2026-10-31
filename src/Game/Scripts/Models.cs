using Godot;

namespace Pluton.Game;

/// <summary>Modelos y materiales generados por código. Cada modelo agrupa geometría por material para reducir llamadas de dibujo.</summary>
public static class Models
{
    private static readonly Dictionary<string, StandardMaterial3D> Mats = new();

    public static StandardMaterial3D Mat(string name)
    {
        if (Mats.TryGetValue(name, out var m)) return m;
        m = new StandardMaterial3D();
        switch (name)
        {
            case "hull": Pbr(m, new Color(0.25f, 0.29f, 0.35f), 0.65f, 0.5f); break;
            case "panel": Pbr(m, new Color(0.42f, 0.47f, 0.53f), 0.45f, 0.55f); break;
            case "dark": Pbr(m, new Color(0.07f, 0.08f, 0.1f), 0.5f, 0.7f); break;
            case "rock": Pbr(m, new Color(0.2f, 0.17f, 0.16f), 0f, 0.95f); break;
            case "redrock": Pbr(m, new Color(0.42f, 0.19f, 0.11f), 0f, 0.92f); break;
            case "rust": Pbr(m, new Color(0.33f, 0.2f, 0.13f), 0.55f, 0.85f); break;
            case "rustlight": Pbr(m, new Color(0.47f, 0.37f, 0.28f), 0.5f, 0.75f); break;
            case "stripe": Pbr(m, new Color(0.9f, 0.62f, 0.1f), 0.15f, 0.6f); break;
            case "copper": Pbr(m, new Color(0.72f, 0.42f, 0.22f), 0.85f, 0.35f); break;
            case "amber": Glow(m, new Color(1f, 0.56f, 0.14f), 1.8f); break;
            case "cyan": Glow(m, new Color(0.3f, 0.85f, 1f), 1.7f); break;
            case "heat": Glow(m, new Color(1f, 0.36f, 0.08f), 1.8f); break;
            case "ice":
                Pbr(m, new Color(0.62f, 0.82f, 0.96f), 0.05f, 0.18f);
                m.EmissionEnabled = true;
                m.Emission = new Color(0.1f, 0.26f, 0.42f);
                m.EmissionEnergyMultiplier = 0.5f;
                break;
            case "crust":
                Pbr(m, new Color(0.75f, 0.84f, 0.92f), 0.02f, 0.42f);
                break;
            case "ore": Pbr(m, new Color(0.5f, 0.28f, 0.42f), 0.8f, 0.32f); m.EmissionEnabled = true; m.Emission = new Color(0.65f, 0.2f, 0.45f); m.EmissionEnergyMultiplier = 0.7f; break;
            case "suit": Pbr(m, new Color(0.86f, 0.87f, 0.84f), 0.1f, 0.55f); break;
            case "visor": Pbr(m, new Color(0.9f, 0.55f, 0.15f), 0.9f, 0.12f); break;
            default: Pbr(m, Colors.Magenta, 0, 1); break;
        }
        Mats[name] = m;
        return m;
    }

    private static void Pbr(StandardMaterial3D m, Color c, float metal, float rough)
    {
        m.AlbedoColor = c;
        m.Metallic = metal;
        m.Roughness = rough;
    }

    private static void Glow(StandardMaterial3D m, Color c, float energy)
    {
        m.AlbedoColor = c;
        m.EmissionEnabled = true;
        m.Emission = c;
        m.EmissionEnergyMultiplier = energy;
        m.Roughness = 0.4f;
    }

    /// <summary>Acumula primitivas por material y produce una sola malla.</summary>
    public sealed class Builder
    {
        private readonly Dictionary<string, SurfaceTool> _tools = new();

        private SurfaceTool Tool(string mat)
        {
            if (!_tools.TryGetValue(mat, out var st))
            {
                st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                st.SetMaterial(Mat(mat));
                _tools[mat] = st;
            }
            return st;
        }

        private static Transform3D Tf(float x, float y, float z, float yawDeg = 0, float pitchDeg = 0, float rollDeg = 0)
        {
            var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(pitchDeg), Mathf.DegToRad(yawDeg), Mathf.DegToRad(rollDeg)));
            return new Transform3D(basis, new Vector3(x, y, z));
        }

        public Builder Box(string mat, float x, float y, float z, float w, float h, float d, float yaw = 0, float pitch = 0, float roll = 0)
        {
            Tool(mat).AppendFrom(new BoxMesh { Size = new Vector3(w, h, d) }, 0, Tf(x, y, z, yaw, pitch, roll));
            return this;
        }

        public Builder Cyl(string mat, float x, float y, float z, float radius, float h, float pitch = 0, float roll = 0, int sides = 12, float? topRadius = null)
        {
            Tool(mat).AppendFrom(new CylinderMesh { TopRadius = topRadius ?? radius, BottomRadius = radius, Height = h, RadialSegments = sides, Rings = 1 }, 0, Tf(x, y, z, 0, pitch, roll));
            return this;
        }

        public Builder Ball(string mat, float x, float y, float z, float radius, int sides = 10, int rings = 6, float scaleY = 1f, float yaw = 0)
        {
            var tf = Tf(x, y, z, yaw).ScaledLocal(new Vector3(1, scaleY, 1));
            Tool(mat).AppendFrom(new SphereMesh { Radius = radius, Height = radius * 2, RadialSegments = sides, Rings = rings }, 0, tf);
            return this;
        }

        /// <summary>Base común: plataforma oscura con franja de seguridad.</summary>
        public Builder Plinth(float w, float d)
        {
            Box("dark", 0, 0.1f, 0, w - 0.1f, 0.2f, d - 0.1f);
            Box("stripe", 0, 0.21f, d / 2 - 0.2f, w - 0.3f, 0.03f, 0.14f);
            Box("stripe", 0, 0.21f, -d / 2 + 0.2f, w - 0.3f, 0.03f, 0.14f);
            return this;
        }

        /// <summary>Costuras y refuerzos que dan escala a un volumen liso.</summary>
        public Builder Seams(float x, float y, float z, float w, float h, float d)
        {
            for (var i = -1; i <= 1; i += 2)
            {
                Box("dark", x + i * w / 6, y, z + d / 2 + 0.012f, 0.05f, h * 0.98f, 0.03f);
                Box("dark", x + i * w / 6, y, z - d / 2 - 0.012f, 0.05f, h * 0.98f, 0.03f);
                Box("dark", x + w / 2 + 0.012f, y, z + i * d / 6, 0.03f, h * 0.98f, 0.05f);
                Box("dark", x - w / 2 - 0.012f, y, z + i * d / 6, 0.03f, h * 0.98f, 0.05f);
            }
            Box("panel", x, y + h / 2 - 0.1f, z, w + 0.1f, 0.09f, d + 0.1f);
            Box("panel", x, y - h / 2 + 0.1f, z, w + 0.1f, 0.09f, d + 0.1f);
            return this;
        }

        public ArrayMesh Commit()
        {
            var mesh = new ArrayMesh();
            foreach (var st in _tools.Values) st.Commit(mesh);
            return mesh;
        }
    }

    /// <summary>Malla estática y partes animadas de una máquina.</summary>
    public sealed class MachineModel
    {
        public ArrayMesh Body = null!;
        public ArrayMesh? Rotor;
        public Vector3 RotorPos;
        public Vector3 RotorAxis = Vector3.Up;
        public ArrayMesh? Piston;
        public Vector3 PistonPos;
        public float Height = 3f;
        public Vector3 LampPos;
    }

    private static readonly Dictionary<string, MachineModel> Cache = new();

    public static MachineModel Machine(string def)
    {
        if (!Cache.TryGetValue(def, out var model))
        {
            model = BuildMachine(def);
            Cache[def] = model;
        }
        return model;
    }

    private static MachineModel BuildMachine(string def)
    {
        var b = new Builder();
        var m = new MachineModel();
        switch (def)
        {
            case "depot":
                b.Plinth(6, 6);
                b.Box("hull", 0, 1.5f, 0, 5.2f, 2.6f, 5.2f);
                b.Seams(0, 1.5f, 0, 5.2f, 2.6f, 5.2f);
                b.Box("panel", 0, 1.5f, 2.63f, 4.4f, 2f, 0.06f);
                b.Box("dark", 0, 1.3f, 2.68f, 3f, 1.4f, 0.05f);
                for (var i = 0; i < 5; i++) b.Box("panel", -1.2f + i * 0.6f, 1.3f, 2.72f, 0.08f, 1.3f, 0.04f);
                b.Box("panel", 0, 2.95f, 0, 5.5f, 0.3f, 5.5f);
                b.Box("hull", 0, 3.4f, 0, 2.4f, 0.6f, 2.4f);
                b.Cyl("copper", 2.3f, 1.5f, -2.3f, 0.14f, 2.8f);
                b.Cyl("copper", -2.3f, 1.5f, -2.3f, 0.14f, 2.8f);
                m.Height = 3.8f;
                m.LampPos = new Vector3(0, 3.85f, 0);
                break;
            case "aux_core":
                b.Plinth(4, 4);
                b.Cyl("hull", 0, 0.8f, 0, 1.5f, 1.2f, sides: 8);
                b.Cyl("panel", 0, 1.5f, 0, 1.2f, 0.2f, sides: 8);
                b.Cyl("cyan", 0, 2.3f, 0, 0.45f, 1.4f);
                for (var i = 0; i < 4; i++)
                {
                    var a = i * Mathf.Pi / 2 + Mathf.Pi / 4;
                    b.Box("dark", Mathf.Cos(a) * 0.72f, 2.3f, Mathf.Sin(a) * 0.72f, 0.16f, 1.6f, 0.16f);
                }
                b.Cyl("panel", 0, 3.1f, 0, 0.8f, 0.2f, sides: 8);
                m.Height = 3.4f;
                m.LampPos = new Vector3(1.4f, 1.6f, 1.4f);
                break;
            case "rtg_generator":
                b.Plinth(4, 4);
                b.Cyl("hull", 0, 1.4f, 0, 1.0f, 2.2f, sides: 10);
                for (var i = 0; i < 8; i++)
                {
                    var a = i * Mathf.Tau / 8;
                    b.Box("panel", Mathf.Cos(a) * 1.3f, 1.4f, Mathf.Sin(a) * 1.3f, 0.7f, 2f, 0.06f, -Mathf.RadToDeg(a));
                }
                b.Cyl("amber", 0, 2.65f, 0, 0.35f, 0.3f);
                m.Height = 3f;
                m.LampPos = new Vector3(1.5f, 0.5f, 1.5f);
                break;
            case "recycler":
                b.Plinth(4, 4);
                b.Box("hull", 0, 1.1f, 0.3f, 3.2f, 1.8f, 2.6f);
                b.Seams(0, 1.1f, 0.3f, 3.2f, 1.8f, 2.6f);
                b.Box("rust", 0, 2.4f, -0.7f, 2.4f, 1.1f, 1.8f, 0, -18);
                b.Box("dark", 0, 1.1f, 1.62f, 1.8f, 0.7f, 0.06f);
                b.Cyl("copper", 1.4f, 2.3f, 0.9f, 0.13f, 1.8f);
                m.Rotor = new Builder().Cyl("panel", 0, 0, 0, 0.75f, 0.2f, sides: 6).Box("stripe", 0, 0.11f, 0, 1.3f, 0.02f, 0.18f).Commit();
                m.RotorPos = new Vector3(0, 2.08f, 0.6f);
                m.Height = 3.2f;
                m.LampPos = new Vector3(-1.4f, 2.1f, 1.3f);
                break;
            case "ore_extractor":
            case "ice_extractor":
                b.Plinth(4, 4);
                b.Box("hull", 0, 0.8f, 0, 3.4f, 1.1f, 3.4f);
                b.Seams(0, 0.8f, 0, 3.4f, 1.1f, 3.4f);
                b.Box("dark", 0, 1.1f, 0, 1.3f, 1.6f, 1.3f);
                foreach (var (x, z) in new[] { (-1.3f, -1.3f), (1.3f, -1.3f), (-1.3f, 1.3f), (1.3f, 1.3f) })
                    b.Box(def == "ore_extractor" ? "panel" : "copper", x, 3f, z, 0.22f, 4.6f, 0.22f);
                b.Box("panel", 0, 5.2f, 0, 3f, 0.3f, 3f);
                b.Box("stripe", 0, 5.4f, 0, 2.2f, 0.06f, 0.3f);
                m.Piston = new Builder()
                    .Cyl(def == "ore_extractor" ? "copper" : "cyan", 0, 0, 0, 0.32f, 3.4f)
                    .Cyl("dark", 0, -1.9f, 0, 0.5f, 0.5f, sides: 8, topRadius: 0.5f)
                    .Commit();
                m.PistonPos = new Vector3(0, 3.1f, 0);
                m.Height = 5.6f;
                m.LampPos = new Vector3(1.5f, 1.5f, 1.72f);
                break;
            case "smelter":
                b.Plinth(4, 6);
                b.Box("hull", 0, 1.2f, 0.4f, 3.2f, 2f, 4.2f);
                b.Seams(0, 1.2f, 0.4f, 3.2f, 2f, 4.2f);
                b.Box("dark", 0, 1.2f, 2.53f, 1.6f, 1.1f, 0.06f);
                b.Box("heat", 0, 1.1f, 2.58f, 1.2f, 0.7f, 0.04f);
                b.Cyl("rustlight", 0.8f, 3.6f, -1f, 0.55f, 3f, topRadius: 0.42f);
                b.Cyl("dark", 0.8f, 5.15f, -1f, 0.5f, 0.1f);
                b.Cyl("copper", -1f, 2.6f, -1.2f, 0.28f, 1.1f);
                b.Box("panel", 0, 2.3f, 0.4f, 3.4f, 0.2f, 4.4f);
                m.Height = 5.3f;
                m.LampPos = new Vector3(-1.4f, 2.5f, 2.4f);
                break;
            case "chiller":
                b.Plinth(4, 6);
                b.Box("panel", 0, 1.1f, 0, 3.2f, 1.8f, 4.8f);
                b.Seams(0, 1.1f, 0, 3.2f, 1.8f, 4.8f);
                for (var i = 0; i < 3; i++) b.Cyl("cyan", -0.9f + i * 0.9f, 2.7f, -0.8f, 0.34f, 1.6f);
                for (var i = 0; i < 5; i++) b.Box("dark", 0, 0.7f + i * 0.2f, 2.42f, 2.6f, 0.07f, 0.04f);
                m.Rotor = new Builder().Cyl("hull", 0, 0, 0, 0.9f, 0.15f).Box("dark", 0, 0.08f, 0, 1.6f, 0.03f, 0.25f).Box("dark", 0, 0.08f, 0, 0.25f, 0.03f, 1.6f).Commit();
                m.RotorPos = new Vector3(0, 2.1f, 1.2f);
                m.Height = 3.6f;
                m.LampPos = new Vector3(1.4f, 2.1f, 2.3f);
                break;
            case "assembler":
            case "cryo_assembler":
                b.Plinth(6, 6);
                b.Box("hull", 0, 0.7f, 0, 5.2f, 0.8f, 5.2f);
                foreach (var (x, z) in new[] { (-2.3f, -2.3f), (2.3f, -2.3f), (-2.3f, 2.3f), (2.3f, 2.3f) })
                    b.Box("panel", x, 2.5f, z, 0.3f, 3.2f, 0.3f);
                b.Box("panel", 0, 4.1f, 0, 5.2f, 0.3f, 5.2f);
                b.Box(def == "assembler" ? "stripe" : "cyan", 0, 4.3f, 0, 3f, 0.06f, 0.2f);
                b.Box("dark", 0, 1.2f, 0, 3f, 0.2f, 3f);
                m.Piston = new Builder().Cyl("copper", 0, 0, 0, 0.26f, 2.2f).Box("hull", 0, -1.2f, 0, 1.4f, 0.4f, 1.4f).Commit();
                m.PistonPos = new Vector3(0, 3f, 0);
                m.Height = 4.5f;
                m.LampPos = new Vector3(2.3f, 1.3f, 2.65f);
                break;
            case "export_terminal":
                b.Plinth(6, 4);
                b.Box("hull", 0, 1f, 0, 5f, 1.6f, 3.2f);
                b.Seams(0, 1f, 0, 5f, 1.6f, 3.2f);
                b.Box("panel", -1f, 2.3f, 0, 2.6f, 1f, 2.6f);
                b.Box("amber", 1.7f, 1.6f, 1.63f, 1.2f, 0.5f, 0.04f);
                b.Cyl("panel", 1.6f, 2.4f, -0.4f, 0.14f, 1.7f);
                m.Rotor = new Builder()
                    .Cyl("panel", 0, 0, 0, 0.05f, 0.5f, pitch: 0, topRadius: 1.0f, sides: 14)
                    .Ball("amber", 0, 0.45f, 0, 0.09f)
                    .Commit();
                m.RotorPos = new Vector3(1.6f, 3.4f, -0.4f);
                m.Height = 4.3f;
                m.LampPos = new Vector3(-2.2f, 1.9f, 1.63f);
                break;
            case "station":
                // Andén alineado con su vía, que pasa por el borde +Z del volumen.
                b.Box("dark", 0, 0.15f, 0, 7.9f, 0.3f, 3.9f);
                b.Box("panel", 0, 0.4f, 0, 7.6f, 0.2f, 3.6f);
                b.Box("stripe", 0, 0.52f, 1.65f, 7.6f, 0.03f, 0.22f);
                b.Box("hull", -2.2f, 1.5f, -0.7f, 2.8f, 2f, 1.9f);
                b.Seams(-2.2f, 1.5f, -0.7f, 2.8f, 2f, 1.9f);
                b.Box("amber", -2.2f, 1.9f, 0.27f, 1.8f, 0.4f, 0.04f);
                b.Cyl("panel", 2.8f, 1.8f, -1f, 0.1f, 2.6f);
                b.Box("panel", 2.3f, 3.1f, -1f, 1.2f, 0.12f, 1.8f);
                b.Box("rust", 0.8f, 1f, -0.9f, 1.3f, 1f, 1.3f, 12);
                m.Height = 3.4f;
                m.LampPos = new Vector3(-0.5f, 1.2f, 0.3f);
                break;
            case "shuttle_terminal":
                b.Box("dark", 0, 0.15f, 0, 7.9f, 0.3f, 7.9f);
                b.Box("panel", 0, 0.34f, 0, 7.2f, 0.08f, 7.2f);
                foreach (var (x, z) in new[] { (-3.4f, -3.4f), (3.4f, -3.4f), (-3.4f, 3.4f), (3.4f, 3.4f) })
                {
                    b.Cyl("panel", x, 1f, z, 0.1f, 1.4f);
                    b.Ball("amber", x, 1.8f, z, 0.14f);
                }
                b.Box("stripe", 0, 0.4f, 3.3f, 6.4f, 0.03f, 0.2f);
                b.Box("stripe", 0, 0.4f, -3.3f, 6.4f, 0.03f, 0.2f);
                // Lanzadera posada
                b.Box("hull", 0, 1.9f, 0, 2.8f, 2.2f, 5.2f);
                b.Seams(0, 1.9f, 0, 2.8f, 2.2f, 5.2f);
                b.Box("cyan", 0, 2.4f, 2.62f, 2f, 0.8f, 0.05f);
                b.Box("panel", 0, 3.1f, -0.5f, 1.4f, 0.3f, 3.2f);
                b.Box("hull", -2.1f, 1.3f, -1.2f, 1.6f, 0.2f, 2.4f);
                b.Box("hull", 2.1f, 1.3f, -1.2f, 1.6f, 0.2f, 2.4f);
                b.Cyl("copper", -0.8f, 1.9f, -2.8f, 0.45f, 0.7f, 90);
                b.Cyl("copper", 0.8f, 1.9f, -2.8f, 0.45f, 0.7f, 90);
                m.Height = 3.4f;
                m.LampPos = new Vector3(0, 3.4f, 0);
                break;
            case "storage_crate":
                b.Plinth(4, 4);
                b.Box("hull", 0, 1f, 0, 3.2f, 1.6f, 3.2f);
                for (var i = -1; i <= 1; i++) b.Box("panel", i * 1f, 1f, 1.63f, 0.14f, 1.6f, 0.05f);
                b.Box("panel", 0, 1.85f, 0, 3.4f, 0.14f, 3.4f);
                m.Height = 2.1f;
                m.LampPos = new Vector3(1.3f, 2f, 1.3f);
                break;
            default:
                b.Box("panel", 0, 1f, 0, 3f, 2f, 3f);
                m.LampPos = new Vector3(0, 2.2f, 0);
                break;
        }
        m.Body = b.Commit();
        return m;
    }

    /// <summary>Variantes de escombros: varias formas compartidas entre instancias.</summary>
    public static ArrayMesh Debris(string def, int variant)
    {
        var key = def + variant;
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var rng = new RandomNumberGenerator { Seed = (ulong)key.GetHashCode(StringComparison.Ordinal) ^ 0x51ED27u };
        var b = new Builder();
        float R(float a, float c) => rng.RandfRange(a, c);
        if (def == "scrap_pile")
        {
            for (var i = 0; i < 7; i++)
                b.Box(i % 3 == 0 ? "rustlight" : "rust", R(-0.7f, 0.7f), R(0.15f, 0.7f), R(-0.7f, 0.7f), R(0.3f, 1.1f), R(0.08f, 0.5f), R(0.2f, 0.9f), R(0, 180), R(-35, 35), R(-35, 35));
            b.Cyl("hull", R(-0.4f, 0.4f), 0.5f, R(-0.4f, 0.4f), 0.1f, 1.5f, R(50, 85), R(-30, 30), sides: 6);
        }
        else if (def == "red_rock")
        {
            for (var i = 0; i < 4; i++)
                b.Ball("redrock", R(-0.5f, 0.5f), R(0.3f, 0.6f), R(-0.5f, 0.5f), R(0.4f, 0.75f), 5, 3, R(0.7f, 1.2f), R(0, 90));
        }
        else if (def == "ice_block")
        {
            for (var i = 0; i < 4; i++)
                b.Ball("ice", R(-0.5f, 0.5f), R(0.3f, 0.7f), R(-0.5f, 0.5f), R(0.4f, 0.8f), 5, 3, R(0.8f, 1.5f), R(0, 90));
        }
        else
        {
            // Costra sobre un yacimiento: placa ancha y baja que cubre la celda.
            b.Box("crust", 0, 0.2f, 0, 2.05f, 0.5f, 2.05f, 0, R(-3, 3), R(-3, 3));
            for (var i = 0; i < 3; i++)
                b.Ball("crust", R(-0.6f, 0.6f), 0.45f, R(-0.6f, 0.6f), R(0.3f, 0.55f), 5, 3, 0.8f, R(0, 90));
        }
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    private static readonly Dictionary<string, ArrayMesh> DebrisCache = new();

    public static ArrayMesh NodeMesh(string item)
    {
        var key = "node_" + item;
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var rng = new RandomNumberGenerator { Seed = (ulong)key.GetHashCode(StringComparison.Ordinal) ^ 0x71u };
        var b = new Builder();
        var mat = item == "ore" ? "ore" : "ice";
        b.Cyl("dark", 0, 0.03f, 0, 2.1f, 0.06f, sides: 16);
        for (var i = 0; i < 12; i++)
        {
            var a = rng.RandfRange(0, Mathf.Tau);
            var r = rng.RandfRange(1.0f, 1.9f);
            b.Cyl(mat, Mathf.Cos(a) * r, 0.25f, Mathf.Sin(a) * r, rng.RandfRange(0.1f, 0.24f), rng.RandfRange(0.3f, 0.9f), rng.RandfRange(-25, 25), rng.RandfRange(-25, 25), 5, 0.02f);
        }
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Malla de un tramo de vía: traviesas y dos raíles sobre la curva real.</summary>
    public static ArrayMesh RailMesh(Func<double, Vector3> eval, float lengthM)
    {
        var b = new Builder();
        var steps = Mathf.Max(1, Mathf.RoundToInt(lengthM / 1.2f));
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var p = eval(t);
            var next = eval(Math.Min(1, t + 0.01));
            var prev = eval(Math.Max(0, t - 0.01));
            var dir = (next - prev).Normalized();
            var yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
            b.Box("rust", p.X, 0.07f, p.Z, 2.1f, 0.12f, 0.34f, yaw);
            if (i == steps) continue;
            var q = eval((i + 1) / (double)steps);
            var mid = (p + q) / 2;
            var d = (q - p);
            var side = new Vector3(d.Z, 0, -d.X).Normalized() * 0.72f;
            var segYaw = Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z));
            var len = d.Length() + 0.03f;
            b.Box("panel", mid.X + side.X, 0.19f, mid.Z + side.Z, 0.11f, 0.13f, len, segYaw);
            b.Box("panel", mid.X - side.X, 0.19f, mid.Z - side.Z, 0.11f, 0.13f, len, segYaw);
        }
        return b.Commit();
    }

    public static ArrayMesh Locomotive()
    {
        const string key = "loco";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        b.Box("dark", 0, 0.55f, 0, 2.2f, 0.5f, 7f);
        b.Box("hull", 0, 1.6f, -0.6f, 2.3f, 1.7f, 4.6f);
        b.Seams(0, 1.6f, -0.6f, 2.3f, 1.7f, 4.6f);
        b.Box("panel", 0, 2.1f, 2.5f, 2.5f, 2.6f, 1.9f);
        b.Box("cyan", 0, 2.6f, 3.47f, 2f, 0.7f, 0.05f);
        b.Box("stripe", 0, 1f, 3.48f, 2.2f, 0.3f, 0.05f);
        b.Box("amber", -0.8f, 1.25f, 3.5f, 0.3f, 0.2f, 0.05f);
        b.Box("amber", 0.8f, 1.25f, 3.5f, 0.3f, 0.2f, 0.05f);
        b.Cyl("copper", 0, 2.75f, -2f, 0.28f, 0.8f);
        foreach (var z in new[] { -2.4f, 2.4f })
        foreach (var x in new[] { -0.75f, 0.75f })
            b.Cyl("dark", x, 0.32f, z, 0.32f, 0.14f, 0, 90, 10);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    public static ArrayMesh Wagon()
    {
        const string key = "wagon";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        b.Box("dark", 0, 0.55f, 0, 2.2f, 0.45f, 6.4f);
        b.Box("hull", 0, 1f, -3f, 2.3f, 0.5f, 0.12f);
        b.Box("hull", 0, 1f, 3f, 2.3f, 0.5f, 0.12f);
        b.Box("hull", -1.14f, 1f, 0, 0.12f, 0.5f, 6.1f);
        b.Box("hull", 1.14f, 1f, 0, 0.12f, 0.5f, 6.1f);
        foreach (var z in new[] { -2.2f, 2.2f })
        foreach (var x in new[] { -0.75f, 0.75f })
            b.Cyl("dark", x, 0.32f, z, 0.32f, 0.14f, 0, 90, 10);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Cabina que rodea la cámara durante el trayecto. El frente queda abierto como ventanal.</summary>
    public static ArrayMesh ShuttleCabin()
    {
        const string key = "cabin";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        b.Box("dark", 0, -0.05f, 0, 3.2f, 0.1f, 5f);
        b.Box("hull", 0, 2.6f, 0, 3.2f, 0.12f, 5f);
        b.Box("hull", -1.6f, 1.3f, 0.3f, 0.1f, 2.5f, 4.4f);
        b.Box("hull", 1.6f, 1.3f, 0.3f, 0.1f, 2.5f, 4.4f);
        b.Box("hull", 0, 1.3f, 2.5f, 3.2f, 2.5f, 0.1f);
        b.Box("panel", 0, 0.35f, -2.3f, 3.1f, 0.7f, 0.5f, 0, 25);
        b.Box("panel", -1.5f, 1.3f, -2.4f, 0.14f, 2.5f, 0.14f);
        b.Box("panel", 1.5f, 1.3f, -2.4f, 0.14f, 2.5f, 0.14f);
        b.Box("panel", 0, 2.5f, -2.4f, 3.1f, 0.14f, 0.14f);
        b.Box("amber", -0.7f, 0.68f, -2.2f, 0.5f, 0.05f, 0.3f, 0, 25);
        b.Box("cyan", 0.5f, 0.68f, -2.2f, 0.9f, 0.05f, 0.3f, 0, 25);
        b.Box("dark", 0, 0.45f, 0.5f, 1.1f, 0.9f, 0.9f);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    private static StandardMaterial3D IconMaterial(Color color) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        BillboardKeepScale = true,
    };

    private static readonly StandardMaterial3D IconBad = IconMaterial(new Color(1f, 0.35f, 0.25f));
    private static readonly StandardMaterial3D IconWarn = IconMaterial(new Color(1f, 0.72f, 0.2f));

    /// <summary>Iconos de estado con forma propia, para no comunicar solo con color. Se comparten entre máquinas.</summary>
    public static ArrayMesh? StatusIcon(Pluton.Simulation.MachineStatus status)
    {
        var key = "icon_" + status;
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(float x, float y, float w, float h, float rotDeg = 0)
        {
            var q = new QuadMesh { Size = new Vector2(w, h) };
            st.AppendFrom(q, 0, new Transform3D(Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(rotDeg))), new Vector3(x, y, 0)));
        }
        StandardMaterial3D material;
        switch (status)
        {
            case Pluton.Simulation.MachineStatus.NoPower:
            case Pluton.Simulation.MachineStatus.LowPower:
                // Rayo
                Quad(0.07f, 0.2f, 0.16f, 0.46f, -20);
                Quad(-0.07f, -0.2f, 0.16f, 0.46f, -20);
                Quad(0, 0, 0.42f, 0.13f, 12);
                material = IconBad;
                break;
            case Pluton.Simulation.MachineStatus.OutputFull:
                // Caja llena con marco
                Quad(0, 0.3f, 0.66f, 0.08f);
                Quad(0, -0.3f, 0.66f, 0.08f);
                Quad(-0.3f, 0, 0.08f, 0.66f);
                Quad(0.3f, 0, 0.08f, 0.66f);
                Quad(0, 0, 0.36f, 0.36f);
                material = IconWarn;
                break;
            case Pluton.Simulation.MachineStatus.NoInput:
                // Tres puntos: esperando material
                Quad(-0.26f, 0, 0.15f, 0.15f);
                Quad(0, 0, 0.15f, 0.15f);
                Quad(0.26f, 0, 0.15f, 0.15f);
                material = IconWarn;
                break;
            default:
                return null;
        }
        st.SetMaterial(material);
        mesh = st.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    public static ArrayMesh Astronaut()
    {
        const string key = "astronaut";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        b.Box("suit", 0, 1.05f, 0, 0.55f, 0.7f, 0.32f);
        b.Box("hull", 0, 1.1f, -0.25f, 0.42f, 0.55f, 0.22f);
        b.Ball("suit", 0, 1.6f, 0, 0.24f);
        b.Box("visor", 0, 1.62f, 0.17f, 0.3f, 0.16f, 0.12f);
        b.Box("suit", -0.15f, 0.42f, 0, 0.2f, 0.75f, 0.24f);
        b.Box("suit", 0.15f, 0.42f, 0, 0.2f, 0.75f, 0.24f);
        b.Box("suit", -0.36f, 1.0f, 0.02f, 0.14f, 0.7f, 0.16f);
        b.Box("suit", 0.36f, 1.0f, 0.02f, 0.14f, 0.7f, 0.16f);
        b.Box("amber", 0, 1.25f, 0.17f, 0.16f, 0.06f, 0.02f);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }
}

/// <summary>Sonido sintetizado en memoria; no hay archivos de audio externos.</summary>
public static class Sfx
{
    private static readonly Dictionary<string, AudioStreamWav> Streams = new();
    private static Node _host = null!;
    private static Settings _prefs = null!;
    private const int Rate = 22050;

    public static void Setup(Node host, Settings prefs)
    {
        _host = host;
        _prefs = prefs;
        Streams["cut"] = Make(0.09f, t => Noise() * 0.5f * Fade(t, 0.09f) + Mathf.Sin(t * 2200) * 0.2f * Fade(t, 0.09f));
        Streams["break"] = Make(0.35f, t => (Noise() * 0.7f + Mathf.Sin(t * 500 * (1 - t)) * 0.5f) * Fade(t, 0.35f));
        Streams["build"] = Make(0.3f, t => (Mathf.Sin(t * 620 * (1.3f - t)) * 0.7f + Noise() * 0.2f) * Fade(t, 0.3f));
        Streams["remove"] = Make(0.3f, t => Mathf.Sin(t * 900 * (1f - t * 1.8f)) * 0.5f * Fade(t, 0.3f));
        Streams["deposit"] = Make(0.13f, t => Mathf.Sin(t * 2600) * 0.45f * Fade(t, 0.13f));
        Streams["take"] = Make(0.13f, t => Mathf.Sin(t * 1900 + t * t * 5000) * 0.45f * Fade(t, 0.13f));
        Streams["sell"] = Make(0.4f, t => (Mathf.Sin(t * 5200) * 0.3f + Mathf.Sin(t * (t < 0.12f ? 4400 : 6600)) * 0.35f) * Fade(t, 0.4f));
        Streams["error"] = Make(0.16f, t => Mathf.Sign(Mathf.Sin(t * 950)) * 0.2f * Fade(t, 0.16f));
        Streams["objective"] = Make(0.55f, t => (Mathf.Sin(t * 3300) + Mathf.Sin(t * (t < 0.18f ? 3300 : t < 0.36f ? 4150 : 4950))) * 0.25f * Fade(t, 0.55f));
        Streams["hum"] = MakeLoop(1f, t => Mathf.Sin(t * Mathf.Tau * 60) * 0.3f + Mathf.Sin(t * Mathf.Tau * 180) * 0.1f + Mathf.Sin(t * Mathf.Tau * 2) * Mathf.Sin(t * Mathf.Tau * 240) * 0.08f);
    }

    private static float Fade(float t, float len) => Mathf.Clamp(1f - t / len, 0, 1);
    private static readonly RandomNumberGenerator Rng = new() { Seed = 42 };
    private static float Noise() => Rng.RandfRange(-1, 1);

    private static AudioStreamWav Make(float seconds, Func<float, float> fn)
    {
        var frames = (int)(seconds * Rate);
        var data = new byte[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var s = (short)(Mathf.Clamp(fn(i / (float)Rate), -1, 1) * 32000);
            data[i * 2] = (byte)(s & 0xFF);
            data[i * 2 + 1] = (byte)(s >> 8);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false };
    }

    private static AudioStreamWav MakeLoop(float seconds, Func<float, float> fn)
    {
        var s = Make(seconds, fn);
        s.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        s.LoopEnd = (int)(seconds * Rate);
        return s;
    }

    public static AudioStream Hum => Streams["hum"];
    public static float MachineDb => Mathf.LinearToDb(Mathf.Max(0.001f, _prefs.MasterVolume * _prefs.MachineVolume));

    public static void Play(string name, float pitchVariation = 0f)
    {
        if (!Streams.TryGetValue(name, out var stream)) return;
        var p = new AudioStreamPlayer
        {
            Stream = stream,
            VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, _prefs.MasterVolume * _prefs.SfxVolume)),
            PitchScale = 1f + (pitchVariation > 0 ? Rng.RandfRange(-pitchVariation, pitchVariation) : 0),
        };
        _host.AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }
}
