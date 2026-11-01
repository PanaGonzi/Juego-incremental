using Godot;

namespace Pluton.Game;

/// <summary>Catálogo de máquinas y restos. Cada modelo cabe en su huella; las bocas de cinta van en ±Z. Mallas compartidas entre instancias.</summary>
public static partial class Models
{
    /// <summary>Malla estática y partes animadas de una máquina.</summary>
    public sealed class MachineModel
    {
        public ArrayMesh Body = null!;
        public ArrayMesh? Rotor;
        public Vector3 RotorPos;
        public Vector3 RotorAxis = Vector3.Up;
        public float RotorSpeed = 2.6f;
        public ArrayMesh? Piston;
        public Vector3 PistonPos;
        public float Height = 3f;
        public Vector3 LampPos;
        /// <summary>Luz cálida de la máquina (horno, pantalla): sin sombras y limitada en distancia.</summary>
        public Vector3? GlowPos;
        public Color GlowColor = new(1f, 0.55f, 0.22f);
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

    // ---------- Piezas reutilizables ----------

    /// <summary>Armario con puerta, tirador, rejilla y costuras.</summary>
    private static void Cabinet(Builder b, string mat, float x, float y, float z, float w, float h, float d, bool louver = true)
    {
        b.Box(mat, x, y, z, w, h, d);
        b.Box("steel_dark", x, y + h / 2 + 0.02f, z, w + 0.06f, 0.05f, d + 0.06f);
        b.Box("dark", x - w * 0.24f, y, z + d / 2 + 0.004f, 0.02f, h * 0.88f, 0.012f, bevel: 0);
        b.Box("steel", x - w * 0.18f, y, z + d / 2 + 0.018f, 0.03f, 0.2f, 0.03f, bevel: 0.006f);
        if (louver) b.Louvers("steel_dark", x + w * 0.14f, y + h * 0.15f, z + d / 2 + 0.02f, w * 0.46f, h * 0.4f, 5);
    }

    private static void Ladder(Builder b, float x, float z, float y0, float y1, bool alongX)
    {
        var o = alongX ? new Vector3(0.2f, 0, 0) : new Vector3(0, 0, 0.2f);
        var at = new Vector3(x, 0, z);
        b.Beam("steel_dark", at - o + Vector3.Up * y0, at - o + Vector3.Up * y1, 0.035f, 0.035f);
        b.Beam("steel_dark", at + o + Vector3.Up * y0, at + o + Vector3.Up * y1, 0.035f, 0.035f);
        for (var y = y0 + 0.2f; y < y1; y += 0.32f)
            b.Beam("steel", at - o + Vector3.Up * y, at + o + Vector3.Up * y, 0.025f, 0.025f);
    }

    private static void Railing(Builder b, Vector3 a, Vector3 c, float height = 0.9f)
    {
        var posts = Mathf.Max(1, Mathf.RoundToInt((c - a).Length() / 1.1f));
        for (var i = 0; i <= posts; i++)
        {
            var p = a.Lerp(c, i / (float)posts);
            b.Beam("signal", p, p + Vector3.Up * height, 0.04f, 0.04f);
        }
        b.Beam("signal", a + Vector3.Up * height, c + Vector3.Up * height, 0.045f, 0.045f);
        b.Beam("steel_dark", a + Vector3.Up * height * 0.5f, c + Vector3.Up * height * 0.5f, 0.03f, 0.03f);
    }

    private static void Flange(Builder b, string mat, Vector3 p, float r, float pitch = 0, float roll = 0) =>
        b.Cyl(mat, p.X, p.Y, p.Z, r, 0.06f, pitch, roll, 10);

    /// <summary>Plataforma circular de base con pernos.</summary>
    private static void BoltRing(Builder b, float x, float y, float z, float r, int count = 8)
    {
        for (var i = 0; i < count; i++)
        {
            var a = i * Mathf.Tau / count;
            b.Cyl("steel", x + Mathf.Cos(a) * r, y, z + Mathf.Sin(a) * r, 0.035f, 0.05f, sides: 6);
        }
    }

    private static MachineModel Extractor(string def)
    {
        var b = new Builder();
        var m = new MachineModel();
        var (leg, band, shaft) = def switch
        {
            "ice_extractor" => ("navy", "cyan", "chrome"),
            "silicon_extractor" => ("cream", "cyan", "steel"),
            _ => ("signal", "amber", "steel"),
        };
        b.Plinth(4, 4);
        // Boca del pozo con collarín.
        b.Lathe("steel_dark", 0, 0.22f, 0, new[] { (0f, 0.02f), (1.2f, 0.02f), (1.2f, 0.16f), (0.8f, 0.3f), (0.62f, 0.3f), (0f, 0.26f) }, 20);
        BoltRing(b, 0, 0.42f, 0, 1.0f, 10);
        // Torre de perforación cónica: cuatro patas, tres niveles de arriostrado.
        var foot = 1.52f;
        var head = 0.52f;
        const float top = 5f;
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
        {
            b.Beam(leg, new Vector3(sx * foot, 0.22f, sz * foot), new Vector3(sx * head, top, sz * head), 0.16f, 0.16f);
            b.Box("steel_dark", sx * foot, 0.3f, sz * foot, 0.36f, 0.14f, 0.36f);
        }
        for (var level = 0; level < 3; level++)
        {
            var t0 = 0.12f + level * 0.27f;
            var t1 = t0 + 0.27f;
            var w0 = Mathf.Lerp(foot, head, t0);
            var w1 = Mathf.Lerp(foot, head, t1);
            var y0 = Mathf.Lerp(0.22f, top, t0);
            var y1 = Mathf.Lerp(0.22f, top, t1);
            foreach (var (ax, az, cx, cz) in new[] { (-1f, 1f, 1f, 1f), (1f, 1f, 1f, -1f), (1f, -1f, -1f, -1f), (-1f, -1f, -1f, 1f) })
            {
                b.Beam("steel_dark", new Vector3(ax * w0, y0, az * w0), new Vector3(cx * w0, y0, cz * w0), 0.08f, 0.08f);
                b.Beam("steel_dark", new Vector3(ax * w0, y0, az * w0), new Vector3(cx * w1, y1, cz * w1), 0.05f, 0.05f);
            }
        }
        // Corona con motor de izado y polea.
        b.Box("hull", 0, 5.08f, 0, 1.5f, 0.2f, 1.5f);
        Cabinet(b, "panel", 0.15f, 5.42f, -0.1f, 1f, 0.48f, 0.72f, false);
        for (var i = -2; i <= 2; i++) b.Box("steel_dark", 0.15f + i * 0.16f, 5.42f, 0.28f, 0.03f, 0.4f, 0.06f, bevel: 0);
        b.Cyl("steel", -0.58f, 5.34f, 0, 0.24f, 0.14f, roll: 90, sides: 14);
        b.Box(band, 0, 5.2f, 0.76f, 0.9f, 0.05f, 0.015f, bevel: 0);
        // Grupo motor y caja reductora en la parte trasera.
        Cabinet(b, "hull", 0, 0.96f, -1.24f, 2f, 1.26f, 0.9f);
        b.Cyl("steel_dark", -0.5f, 1.7f, -1.24f, 0.26f, 0.26f, sides: 10);
        b.Cyl("steel", -0.5f, 1.9f, -1.24f, 0.09f, 0.14f, sides: 8);
        b.Tube("hose", 0.045f, 6, new Vector3(0.7f, 1.2f, -0.78f), new Vector3(0.82f, 1.8f, -0.9f), new Vector3(0.6f, 3f, -0.72f), new Vector3(0.5f, 4.9f, -0.5f));
        b.Tube("copper", 0.035f, 6, new Vector3(-0.7f, 1.25f, -0.78f), new Vector3(-0.55f, 1.25f, -0.4f), new Vector3(-0.45f, 0.6f, -0.3f));
        // Tolva de descarga hacia la boca frontal.
        b.Taper("panel", 0, 1.05f, 1.02f, 1.1f, 0.62f, 1.5f, 1f, 0.7f);
        b.Taper("dark", 0, 1.41f, 1.02f, 1.42f, 0.92f, 1.3f, 0.8f, 0.02f);
        b.Box(leg, 0, 1.05f, 1.54f, 1.12f, 0.1f, 0.03f, bevel: 0);
        b.Port(0, 1.96f, true);
        Ladder(b, 1.72f, 0, 0.25f, 3.5f, false);
        m.Body = b.Commit();

        var piston = new Builder();
        var pipe = def == "ice_extractor" ? "chrome" : shaft;
        piston.Cyl(pipe, 0, 0, 0, 0.11f, 3.5f, sides: 10);
        for (var y = -1.4f; y <= 1.4f; y += 0.7f) piston.Cyl("steel_dark", 0, y, 0, 0.16f, 0.1f, sides: 10);
        // Trépano: cono de corte con dientes.
        piston.Lathe("steel_dark", 0, -1.9f, 0, new[] { (0.06f, -0.4f), (0.34f, -0.08f), (0.38f, 0.14f), (0.13f, 0.28f) }, 8);
        for (var i = 0; i < 4; i++)
        {
            var a = i * Mathf.Tau / 4;
            piston.Box("steel", Mathf.Cos(a) * 0.3f, -2.05f, Mathf.Sin(a) * 0.3f, 0.09f, 0.22f, 0.09f, yaw: -Mathf.RadToDeg(a));
        }
        m.Piston = piston.Commit();
        m.PistonPos = new Vector3(0, 3.1f, 0);

        // Mesa rotatoria: su giro delata que la extracción está activa.
        var rotor = new Builder();
        rotor.Lathe("steel_dark", 0, 0, 0, new[] { (0.16f, -0.08f), (0.6f, -0.08f), (0.6f, 0.08f), (0.16f, 0.08f) }, 12);
        for (var i = 0; i < 3; i++)
        {
            var a = i * Mathf.Tau / 3;
            rotor.Box(leg, Mathf.Cos(a) * 0.4f, 0.1f, Mathf.Sin(a) * 0.4f, 0.32f, 0.07f, 0.1f, yaw: -Mathf.RadToDeg(a));
        }
        m.Rotor = rotor.Commit();
        m.RotorPos = new Vector3(0, 0.65f, 0);
        m.RotorSpeed = 3.4f;
        m.Height = 5.75f;
        m.LampPos = new Vector3(0.95f, 1.72f, -0.86f);
        return m;
    }

    private static MachineModel Smelter()
    {
        var b = new Builder();
        var m = new MachineModel();
        b.Plinth(4, 6);
        // Horno vertical con refractario y zunchos.
        const float fz = -0.85f;
        b.Lathe("hull", 0, 0.25f, fz, new[] { (0f, 0f), (1.42f, 0f), (1.42f, 0.3f), (1.34f, 0.36f), (1.34f, 2.5f), (1.05f, 3.25f), (0.6f, 3.5f), (0f, 3.5f) }, 24);
        foreach (var y in new[] { 0.95f, 1.6f, 2.25f }) b.Cyl("steel_dark", 0, y, fz, 1.37f, 0.1f, sides: 24);
        for (var i = 0; i < 8; i++)
        {
            var a = i * Mathf.Tau / 8 + 0.39f;
            b.Beam("steel_dark", new Vector3(Mathf.Cos(a) * 1.4f, 0.55f, fz + Mathf.Sin(a) * 1.4f), new Vector3(Mathf.Cos(a) * 1.4f, 2.5f, fz + Mathf.Sin(a) * 1.4f), 0.06f, 0.09f);
        }
        // Chimenea desplazada con sombrerete y tirantes.
        b.Tube("steel_dark", 0.34f, 12, new Vector3(0, 3.6f, fz), new Vector3(0, 4.05f, fz), new Vector3(0.55f, 4.5f, fz), new Vector3(0.55f, 5.1f, fz));
        b.Lathe("steel_dark", 0.55f, 5.1f, fz, new[] { (0.36f, 0f), (0.4f, 0.04f), (0.4f, 0.14f), (0.34f, 0.18f) }, 12);
        b.Beam("steel_dark", new Vector3(0.75f, 4.7f, fz), new Vector3(1.28f, 2.8f, fz + 0.2f), 0.04f, 0.04f);
        // Cuerpo delantero con puerta de colada.
        const float cz = 1.42f;
        b.Box("hull", 0, 1.3f, cz, 3.1f, 2.1f, 2.4f);
        b.Seams(0, 1.3f, cz, 3.1f, 2.1f, 2.4f);
        b.Box("signal", 0, 2.42f, cz, 3.2f, 0.14f, 2.5f);
        // Ventana de la colada: marco, barrotes y fuego visible.
        b.Box("steel_dark", 0, 1.74f, cz + 1.2f, 1.7f, 0.86f, 0.1f);
        b.Box("heat", 0, 1.74f, cz + 1.24f, 1.34f, 0.56f, 0.02f, bevel: 0);
        for (var i = -2; i <= 2; i++) b.Box("steel_dark", i * 0.26f, 1.74f, cz + 1.27f, 0.05f, 0.6f, 0.05f);
        b.Taper("steel_dark", 0, 2.2f, cz + 1.09f, 1.9f, 0.32f, 1.6f, 0.14f, 0.16f);
        // Armario de control, tuberías de refrigeración, ventilador.
        Cabinet(b, "panel", -1.68f, 1.05f, -0.1f, 0.5f, 1.5f, 1.15f, false);
        b.Louvers("steel_dark", -1.94f, 1.1f, -0.1f, 0.9f, 0.9f, 5, true);
        b.Tube("copper", 0.06f, 8, new Vector3(1.32f, 0.7f, 0.2f), new Vector3(1.75f, 0.7f, 0.2f), new Vector3(1.75f, 2.25f, 0.2f), new Vector3(1.75f, 2.25f, -0.45f), new Vector3(1.26f, 2.25f, -0.45f));
        b.Tube("copper", 0.06f, 8, new Vector3(1.32f, 1.1f, 0.5f), new Vector3(1.6f, 1.1f, 0.5f), new Vector3(1.6f, 2f, 0.5f), new Vector3(1.6f, 2f, -0.72f), new Vector3(1.29f, 2f, -0.72f));
        foreach (var at in new[] { new Vector3(1.75f, 1.2f, 0.2f), new Vector3(1.6f, 1.5f, 0.5f) }) Flange(b, "steel", at, 0.1f);
        Ladder(b, -1.04f, -2.2f, 0.25f, 3.1f, true);
        b.Port(0, 2.96f, true);
        b.Port(0, -2.96f, false);
        b.Taper("panel", 0, 1.3f, -2.5f, 1.05f, 0.62f, 1.3f, 0.8f, 0.6f);
        // Ventilador de techo del cuerpo delantero.
        b.Cyl("steel_dark", 0.55f, 2.54f, cz, 0.56f, 0.12f, sides: 16);
        var rotor = new Builder();
        for (var i = 0; i < 5; i++)
        {
            var a = i * 72f;
            var rad = Mathf.DegToRad(a);
            rotor.Box("panel", Mathf.Cos(rad) * 0.28f, 0, Mathf.Sin(rad) * 0.28f, 0.42f, 0.015f, 0.13f, yaw: -a, roll: 14, bevel: 0);
        }
        rotor.Cyl("signal", 0, 0, 0, 0.08f, 0.06f, sides: 8);
        m.Body = b.Commit();
        m.Rotor = rotor.Commit();
        m.RotorPos = new Vector3(0.55f, 2.63f, cz);
        m.RotorSpeed = 7f;
        m.Height = 5.3f;
        m.LampPos = new Vector3(-1.2f, 2.62f, 2.45f);
        m.GlowPos = new Vector3(0, 1.74f, cz + 1.8f);
        return m;
    }

    private static MachineModel Recycler()
    {
        var b = new Builder();
        var m = new MachineModel();
        b.Plinth(4, 4);
        b.Box("hull", 0, 1.05f, 0.25f, 3.15f, 1.65f, 2.75f);
        b.Seams(0, 1.05f, 0.25f, 3.15f, 1.65f, 2.75f);
        // Tolva de trituración abierta: se ve la boca y dentro los rodillos.
        b.Taper("hull", 0, 2.42f, -0.2f, 2.3f, 1.7f, 3.05f, 2.45f, 1.05f);
        b.Taper("dark", 0, 2.48f, -0.2f, 1.9f, 1.3f, 2.72f, 2.12f, 1f);
        b.Box("steel_dark", 0, 2.96f, 1.03f, 3.1f, 0.08f, 0.1f);
        b.Box("steel_dark", 0, 2.96f, -1.42f, 3.1f, 0.08f, 0.1f);
        b.Box("steel_dark", 1.52f, 2.96f, -0.2f, 0.1f, 0.08f, 2.5f);
        b.Box("steel_dark", -1.52f, 2.96f, -0.2f, 0.1f, 0.08f, 2.5f);
        // Motor lateral y carcasa de transmisión en señalización.
        b.Box("signal", 1.68f, 1.65f, 0, 0.3f, 0.95f, 1.8f);
        b.Cyl("hull", 1.72f, 1.12f, -0.55f, 0.32f, 0.72f, pitch: 90, sides: 12);
        for (var i = 0; i < 5; i++) b.Cyl("steel_dark", 1.72f, 1.12f, -0.87f + i * 0.16f, 0.35f, 0.04f, pitch: 90, sides: 12);
        b.Tube("copper", 0.06f, 8, new Vector3(-1.55f, 1.6f, 1f), new Vector3(-1.75f, 1.6f, 1f), new Vector3(-1.75f, 2.7f, 1f), new Vector3(-1.75f, 2.7f, 0.2f));
        // Panel frontal con rejilla.
        b.Louvers("steel_dark", -0.85f, 1.2f, 1.64f, 1f, 0.9f, 5);
        Cabinet(b, "panel", 1.05f, 1.15f, 1.72f, 0.8f, 1f, 0.16f, false);
        b.Box("screen", 1.05f, 1.3f, 1.81f, 0.48f, 0.3f, 0.012f, bevel: 0);
        b.Port(0, 1.96f, true);
        b.Port(0, -1.96f, false);
        // Rodillos trituradores: giran sobre su eje mientras hay material.
        var rotor = new Builder();
        rotor.Cyl("steel_dark", 0, 0, 0, 0.22f, 2.3f, roll: 90, sides: 10);
        for (var i = -5; i <= 5; i++)
        {
            var x = i * 0.2f;
            rotor.Cyl("steel", x, 0, 0, 0.3f, 0.08f, roll: 90, sides: 6);
            var a = i * 0.9f;
            rotor.Box("rustlight", x, Mathf.Sin(a) * 0.31f, Mathf.Cos(a) * 0.31f, 0.09f, 0.1f, 0.14f, pitch: Mathf.RadToDeg(-a));
        }
        m.Body = b.Commit();
        m.Rotor = rotor.Commit();
        m.RotorPos = new Vector3(0, 2.25f, -0.2f);
        m.RotorAxis = Vector3.Right;
        m.RotorSpeed = 4f;
        m.Height = 3.2f;
        m.LampPos = new Vector3(-1.2f, 1.95f, 1.62f);
        return m;
    }

    private static MachineModel Generator(string def)
    {
        var b = new Builder();
        var m = new MachineModel();
        b.Plinth(4, 4);
        if (def == "aux_core")
        {
            // Núcleo auxiliar: columna luminosa tras un cilindro de vidrio, con armazón.
            b.Lathe("hull", 0, 0.22f, 0, new[] { (0f, 0f), (1.55f, 0f), (1.55f, 0.28f), (1.28f, 0.72f), (1.05f, 1f), (0.74f, 1.02f), (0f, 1.02f) }, 8);
            b.Cyl("steel_dark", 0, 1.3f, 0, 0.72f, 0.1f, sides: 16);
            b.Cyl("cyan", 0, 2.2f, 0, 0.26f, 1.7f, sides: 12);
            b.Lathe("glass", 0, 1.3f, 0, new[] { (0.58f, 0f), (0.58f, 1.8f) }, 20);
            for (var i = 0; i < 4; i++)
            {
                var a = i * Mathf.Pi / 2 + Mathf.Pi / 4;
                var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                b.Beam("steel", c * 0.72f + Vector3.Up * 1.25f, c * 0.72f + Vector3.Up * 3.14f, 0.1f, 0.1f);
                b.Beam("steel_dark", c * 1.28f + Vector3.Up * 0.8f, c * 0.74f + Vector3.Up * 2.1f, 0.06f, 0.06f);
                b.Tube("copper", 0.04f, 6, c * 1.42f + Vector3.Up * 0.5f, c * 1.72f + Vector3.Up * 0.42f, c * 1.72f + Vector3.Up * 0.27f);
            }
            foreach (var y in new[] { 1.72f, 2.62f }) b.Lathe("steel_dark", 0, y, 0, new[] { (0.6f, -0.03f), (0.82f, -0.03f), (0.82f, 0.03f), (0.6f, 0.03f) }, 16);
            b.Lathe("panel", 0, 3.12f, 0, new[] { (0f, 0f), (0.86f, 0f), (0.86f, 0.1f), (0.5f, 0.24f), (0f, 0.24f) }, 16);
            b.Cyl("signal", 0, 3.42f, 0, 0.12f, 0.1f, sides: 10);
            m.Height = 3.5f;
            m.LampPos = new Vector3(1.05f, 1.14f, 1.05f);
            m.GlowPos = new Vector3(0, 1.8f, 0);
            m.GlowColor = new Color(0.4f, 0.85f, 1f);
        }
        else
        {
            // Generador termoeléctrico: cuerpo con aletas radiales, banda ámbar y turbina superior.
            b.Lathe("hull", 0, 0.22f, 0, new[] { (0f, 0f), (1.28f, 0f), (1.28f, 0.2f), (0.9f, 0.34f), (0.9f, 2.25f), (0.62f, 2.5f), (0f, 2.5f) }, 16);
            for (var i = 0; i < 16; i++)
            {
                var a = i * 22.5f;
                var rad = Mathf.DegToRad(a);
                b.Box(i % 4 == 0 ? "signal" : "panel", Mathf.Cos(rad) * 1.28f, 1.42f, Mathf.Sin(rad) * 1.28f, 0.76f, 1.7f, 0.05f, yaw: -a);
            }
            foreach (var y in new[] { 0.62f, 2.28f }) b.Lathe("steel_dark", 0, y, 0, new[] { (0.88f, -0.05f), (1.68f, -0.05f), (1.68f, 0.05f), (0.88f, 0.05f) }, 16);
            b.Lathe("amber", 0, 2.42f, 0, new[] { (0.66f, -0.05f), (0.76f, -0.05f), (0.76f, 0.05f), (0.66f, 0.05f) }, 16);
            b.Tube("hose", 0.05f, 6, new Vector3(1.5f, 0.3f, 1.5f), new Vector3(1.5f, 0.3f, 0.8f), new Vector3(1.15f, 0.45f, 0.5f));
            m.Height = 3f;
            m.LampPos = new Vector3(1.62f, 0.62f, 1.62f);
            m.GlowPos = new Vector3(0, 2.7f, 0);
            var rotor = new Builder();
            for (var i = 0; i < 6; i++)
            {
                var a = i * 60f;
                var rad = Mathf.DegToRad(a);
                rotor.Box("steel", Mathf.Cos(rad) * 0.27f, 0, Mathf.Sin(rad) * 0.27f, 0.4f, 0.02f, 0.12f, yaw: -a, roll: 18, bevel: 0);
            }
            rotor.Cyl("steel_dark", 0, 0, 0, 0.09f, 0.1f, sides: 8);
            m.Rotor = rotor.Commit();
            m.RotorPos = new Vector3(0, 2.8f, 0);
            m.RotorSpeed = 5f;
        }
        m.Body = b.Commit();
        return m;
    }

    private static MachineModel Depot()
    {
        var b = new Builder();
        var m = new MachineModel();
        b.Plinth(6, 6);
        // Nave: cuerpo con chapa ondulada y pilares en esquina.
        const float s = 5f;
        const float h = 2.6f;
        const float cy = 1.54f;
        b.Box("hull", 0, cy, 0, s, h, s);
        foreach (var side in new[] { 0, 1, 2, 3 })
        {
            for (var i = -10; i <= 10; i++)
            {
                if (side == 0 && Mathf.Abs(i) <= 7) continue;
                var off = i * 0.23f;
                var (x, z, wx, wz) = side switch
                {
                    0 => (off, s / 2, 0.09f, 0.04f),
                    1 => (off, -s / 2, 0.09f, 0.04f),
                    2 => (s / 2, off, 0.04f, 0.09f),
                    _ => (-s / 2, off, 0.04f, 0.09f),
                };
                b.Box("panel", x, cy, z, wx, h * 0.96f, wz, bevel: 0);
            }
        }
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            b.Box("signal", sx * (s / 2 + 0.03f), cy, sz * (s / 2 + 0.03f), 0.26f, h + 0.08f, 0.26f);
        // Portón de lamas con marco, y lucero sobre el dintel.
        b.Box("steel_dark", 0, 1.32f, s / 2 + 0.03f, 3.5f, 2.06f, 0.08f);
        for (var i = 0; i < 11; i++) b.Box("cream", 0, 0.48f + i * 0.17f, s / 2 + 0.09f, 3.2f, 0.15f, 0.05f, pitch: 10, bevel: 0.01f);
        b.Box("amber", 0, 2.5f, s / 2 + 0.03f, 1.8f, 0.09f, 0.04f, bevel: 0);
        b.Box("stripe", -1.9f, 0.5f, s / 2 + 0.06f, 0.14f, 0.56f, 0.03f, bevel: 0);
        b.Box("stripe", 1.9f, 0.5f, s / 2 + 0.06f, 0.14f, 0.56f, 0.03f, bevel: 0);
        // Cubierta con pretil, unidad de ventilación y depósito lateral.
        b.Box("steel_dark", 0, 2.9f, 0, s + 0.4f, 0.14f, s + 0.4f);
        b.Box("hull", 0, 3.02f, 0, s + 0.1f, 0.1f, s + 0.1f);
        Cabinet(b, "panel", -1f, 3.36f, -0.9f, 1.8f, 0.6f, 1.5f, false);
        b.Louvers("steel_dark", -1f, 3.36f, -0.13f, 1.4f, 0.42f, 4);
        b.Cyl("steel_dark", 1.3f, 3.14f, 0.6f, 0.75f, 0.14f, sides: 16);
        b.Tube("copper", 0.07f, 8, new Vector3(2.62f, 2.7f, -1.9f), new Vector3(2.8f, 2.7f, -1.9f), new Vector3(2.8f, 0.5f, -1.9f), new Vector3(2.62f, 0.5f, -1.9f));
        b.Tube("copper", 0.07f, 8, new Vector3(-2.62f, 2.7f, -1.9f), new Vector3(-2.8f, 2.7f, -1.9f), new Vector3(-2.8f, 0.5f, -1.9f), new Vector3(-2.62f, 0.5f, -1.9f));
        b.Port(0, -2.96f, false);
        var rotor = new Builder();
        for (var i = 0; i < 4; i++)
        {
            var a = i * 90f;
            var rad = Mathf.DegToRad(a);
            rotor.Box("steel", Mathf.Cos(rad) * 0.36f, 0, Mathf.Sin(rad) * 0.36f, 0.6f, 0.02f, 0.18f, yaw: -a, roll: 16, bevel: 0);
        }
        rotor.Cyl("signal", 0, 0, 0, 0.09f, 0.08f, sides: 8);
        m.Body = b.Commit();
        m.Rotor = rotor.Commit();
        m.RotorPos = new Vector3(1.3f, 3.25f, 0.6f);
        m.RotorSpeed = 3f;
        m.Height = 3.8f;
        m.LampPos = new Vector3(2.1f, 2.5f, 2.56f);
        m.GlowPos = new Vector3(0, 2.3f, 3.5f);
        return m;
    }

    private static MachineModel Terminal()
    {
        var b = new Builder();
        var m = new MachineModel();
        b.Plinth(6, 4);
        // Módulo de envío, oficina de control y mástil con antena orientable.
        b.Box("hull", -0.6f, 1.15f, -0.2f, 3.7f, 1.85f, 2.9f);
        b.Seams(-0.6f, 1.15f, -0.2f, 3.7f, 1.85f, 2.9f);
        b.Box("signal", -0.6f, 2.14f, -0.2f, 3.8f, 0.12f, 3f);
        // Consola frontal inclinada con pantallas reales.
        b.Taper("panel", 1.9f, 0.82f, 0.25f, 1.6f, 2.2f, 1.2f, 1.9f, 1.15f);
        b.Box("dark", 1.9f, 1.44f, 0.25f, 1.3f, 0.08f, 2f, pitch: 0, roll: 0);
        b.Box("screen", 1.9f, 1.5f, 0.75f, 1f, 0.02f, 0.8f, pitch: 14, bevel: 0);
        b.Box("amber", 1.9f, 1.5f, -0.2f, 1f, 0.02f, 0.5f, pitch: 14, bevel: 0);
        // Compuerta de carga.
        b.Box("steel_dark", -0.6f, 1.22f, 1.28f, 2.5f, 1.4f, 0.08f);
        for (var i = 0; i < 6; i++) b.Box("cream", -0.6f, 0.64f + i * 0.24f, 1.33f, 2.3f, 0.2f, 0.04f, bevel: 0.01f);
        // Contenedores apilados.
        Crate(b, -0.6f, 2.5f, -0.6f, 1.6f, 0.62f, 1.1f, "navy");
        Crate(b, -1.8f, 2.5f, -0.5f, 0.8f, 0.62f, 1.1f, "cream");
        // Mástil y parábola: la orientación cambia al vender.
        b.Cyl("steel", 1.9f, 2.3f, -0.9f, 0.1f, 1.8f, sides: 10);
        b.Cyl("steel_dark", 1.9f, 1.42f, -0.9f, 0.24f, 0.14f, sides: 12);
        b.Beam("steel_dark", new Vector3(1.9f, 2.3f, -0.9f), new Vector3(2.5f, 1.4f, -0.9f), 0.05f, 0.05f);
        b.Beam("steel_dark", new Vector3(1.9f, 2.3f, -0.9f), new Vector3(1.3f, 1.4f, -0.9f), 0.05f, 0.05f);
        b.Port(-0.6f, 1.96f, false);
        b.Port(-0.6f, -1.96f, false);
        var rotor = new Builder();
        rotor.Cyl("steel_dark", 0, 0, 0, 0.14f, 0.2f, sides: 10);
        rotor.Lathe("panel", 0, 0.1f, 0.26f, new[] { (0.05f, 0f), (0.5f, 0.1f), (0.9f, 0.32f), (0.86f, 0.36f), (0.46f, 0.15f), (0.05f, 0.05f) }, 20, pitch: 58);
        rotor.Beam("steel", new Vector3(0, 0.1f, 0.26f), new Vector3(0, 0.75f, 0.68f), 0.03f, 0.03f);
        rotor.Ball("amber", 0, 0.75f, 0.68f, 0.07f, 8, 4);
        m.Body = b.Commit();
        m.Rotor = rotor.Commit();
        m.RotorPos = new Vector3(1.9f, 3.3f, -0.9f);
        m.RotorSpeed = 0.5f;
        m.Height = 4.3f;
        m.LampPos = new Vector3(1.3f, 1.6f, 1.15f);
        m.GlowPos = new Vector3(1.9f, 2.2f, 1.2f);
        return m;
    }

    /// <summary>Mástil de señalización en celosía, con franja de aviso y baliza con capota.</summary>
    private static void PadMast(Builder b, float x, float z)
    {
        const float Half = 0.17f, Base = 0.3f, Top = 1.62f;
        b.Cyl("steel_dark", x, Base + 0.05f, z, 0.3f, 0.1f, sides: 8);
        var corners = new[] { (-Half, -Half), (Half, -Half), (Half, Half), (-Half, Half) };
        foreach (var (dx, dz) in corners) b.Box("steel_dark", x + dx, (Base + Top) / 2, z + dz, 0.06f, Top - Base, 0.06f, bevel: 0);
        for (var i = 0; i < corners.Length; i++)
        {
            var (ax, az) = corners[i];
            var (cx, cz) = corners[(i + 1) % corners.Length];
            b.Beam("steel_dark", new Vector3(x + ax, 0.42f, z + az), new Vector3(x + cx, 0.86f, z + cz), 0.03f, 0.03f);
            b.Beam("steel_dark", new Vector3(x + cx, 0.86f, z + cz), new Vector3(x + ax, 1.3f, z + az), 0.03f, 0.03f);
        }
        b.Box("signal", x, 1.42f, z, Half * 2 + 0.1f, 0.2f, Half * 2 + 0.1f, bevel: 0.015f);
        b.Cyl("steel_dark", x, Top + 0.03f, z, 0.14f, 0.06f, sides: 8);
        b.Ball("amber", x, Top + 0.13f, z, 0.075f, 8, 4);
        b.Cyl("steel_dark", x, Top + 0.24f, z, 0.13f, 0.03f, sides: 8);
    }

    private static void Crate(Builder b, float x, float y, float z, float w, float h, float d, string mat)
    {
        b.Box(mat, x, y, z, w, h, d);
        var n = Mathf.Max(2, Mathf.RoundToInt(w / 0.3f));
        for (var i = 0; i <= n; i++)
        {
            var px = x - w / 2 + i * w / n;
            b.Box(mat, px, y, z + d / 2 + 0.008f, 0.05f, h * 0.9f, 0.018f, bevel: 0);
            b.Box(mat, px, y, z - d / 2 - 0.008f, 0.05f, h * 0.9f, 0.018f, bevel: 0);
        }
        foreach (var sx in new[] { -1f, 1f })
        foreach (var sz in new[] { -1f, 1f })
            b.Box("steel_dark", x + sx * (w / 2), y, z + sz * (d / 2), 0.06f, h + 0.02f, 0.06f);
    }

    private static MachineModel Station()
    {
        var b = new Builder();
        var m = new MachineModel();
        // Andén alineado con su vía, que pasa por el borde +Z del volumen.
        b.Box("dark", 0, -0.05f, 0, 7.9f, 0.7f, 3.9f, bevel: 0.05f);
        b.Box("tread", 0, 0.42f, 0, 7.75f, 0.2f, 3.65f);
        b.Box("stripe", 0, 0.525f, 1.62f, 7.6f, 0.012f, 0.3f, bevel: 0);
        // Cabina con ventanal, marquesina y cartel.
        b.Box("hull", -2.5f, 1.6f, -0.72f, 2.6f, 2.1f, 1.9f);
        b.Seams(-2.5f, 1.6f, -0.72f, 2.6f, 2.1f, 1.9f);
        b.Box("steel_dark", -2.5f, 1.95f, 0.24f, 2.15f, 0.82f, 0.05f);
        b.Box("glass", -2.5f, 1.95f, 0.28f, 1.95f, 0.66f, 0.03f, bevel: 0);
        b.Box("screen", -2.5f, 1.72f, 0.2f, 1.7f, 0.1f, 0.02f, bevel: 0);
        b.Box("signal", -2.5f, 2.76f, -0.6f, 3f, 0.14f, 2.5f);
        b.Box("cream", -2.5f, 3.02f, -1f, 1.9f, 0.4f, 0.1f);
        b.Box("amber", -2.5f, 3.02f, -0.94f, 1.5f, 0.14f, 0.02f, bevel: 0);
        // Pórtico de carga con carro móvil sobre la vía.
        foreach (var x in new[] { -0.2f, 3.5f })
        {
            b.Box("signal", x, 2f, -1.45f, 0.2f, 3.2f, 0.2f);
            b.Beam("steel_dark", new Vector3(x, 3.4f, -1.45f), new Vector3(x, 3.4f, 1.3f), 0.2f, 0.26f);
            b.Beam("steel_dark", new Vector3(x, 2.4f, -1.45f), new Vector3(x, 3.32f, -0.3f), 0.08f, 0.08f);
        }
        b.Beam("steel", new Vector3(-0.2f, 3.3f, 0.9f), new Vector3(3.5f, 3.3f, 0.9f), 0.14f, 0.2f);
        b.Beam("steel", new Vector3(-0.2f, 3.3f, -1.1f), new Vector3(3.5f, 3.3f, -1.1f), 0.1f, 0.14f);
        // Tolva, apilado y barandilla del andén.
        b.Taper("hull", 1.65f, 1.15f, -0.95f, 1.1f, 1f, 1.55f, 1.4f, 1.1f);
        Crate(b, 3.2f, 0.95f, -0.9f, 1.3f, 0.86f, 1.05f, "navy");
        Railing(b, new Vector3(-3.72f, 0.52f, -1.68f), new Vector3(3.8f, 0.52f, -1.68f));
        m.Body = b.Commit();
        var piston = new Builder();
        piston.Box("hull", 0, 0, 0, 0.8f, 0.3f, 0.62f);
        piston.Cyl("steel", 0, -0.7f, 0, 0.05f, 1.2f, sides: 6);
        piston.Box("steel_dark", 0, -1.34f, 0, 0.6f, 0.1f, 0.5f);
        m.Piston = piston.Commit();
        m.PistonPos = new Vector3(1.65f, 3.05f, 0.9f);
        m.Height = 3.6f;
        m.LampPos = new Vector3(-1.1f, 2.6f, 0.26f);
        m.GlowPos = new Vector3(-2.5f, 2.2f, 1.4f);
        return m;
    }

    private static MachineModel GenericPress(string def)
    {
        var cryo = def == "cryo_assembler";
        var b = new Builder();
        var m = new MachineModel();
        b.Plinth(6, 6);
        b.Box("hull", 0, 0.65f, 0, 5.2f, 0.75f, 5.2f);
        b.Seams(0, 0.65f, 0, 5.2f, 0.75f, 5.2f);
        foreach (var (x, z) in new[] { (-2.3f, -2.3f), (2.3f, -2.3f), (-2.3f, 2.3f), (2.3f, 2.3f) })
        {
            b.Box(cryo ? "navy" : "signal", x, 2.5f, z, 0.34f, 3.2f, 0.34f);
            b.Beam("steel_dark", new Vector3(x, 3.6f, z), new Vector3(x * 0.5f, 4f, z), 0.1f, 0.1f);
        }
        b.Box("panel", 0, 4.12f, 0, 5.2f, 0.32f, 5.2f);
        b.Box(cryo ? "cyan" : "amber", 0, 4.32f, 0, 3f, 0.04f, 0.16f, bevel: 0);
        b.Box("tread", 0, 1.12f, 0, 3.2f, 0.2f, 3.2f);
        Cabinet(b, "panel", 2.1f, 1.7f, 0.7f, 0.7f, 1.5f, 1.2f, false);
        b.Box("screen", 2.1f, 2.1f, 1.31f, 0.46f, 0.38f, 0.015f, bevel: 0);
        b.Tube("hose", 0.05f, 6, new Vector3(1.6f, 3.96f, 0), new Vector3(1.2f, 3.4f, 0.4f), new Vector3(0.5f, 3.4f, 0.5f));
        if (cryo)
        {
            foreach (var x in new[] { -1.5f, 1.5f })
            {
                b.Cyl("chrome", x, 2.1f, -2f, 0.34f, 1.3f, sides: 14);
                b.Cyl("cyan", x, 2.1f, -2f, 0.36f, 0.08f, sides: 14);
            }
        }
        b.Port(0, 2.96f, true);
        b.Port(0, -2.96f, false);
        m.Body = b.Commit();
        m.Piston = new Builder()
            .Cyl("chrome", 0, 0.1f, 0, 0.2f, 2f, sides: 12)
            .Box(cryo ? "navy" : "hull", 0, -1.1f, 0, 1.5f, 0.36f, 1.5f)
            .Box("steel_dark", 0, -1.33f, 0, 1.1f, 0.1f, 1.1f)
            .Commit();
        m.PistonPos = new Vector3(0, 3f, 0);
        m.Height = 4.5f;
        m.LampPos = new Vector3(2.45f, 1.1f, 2.62f);
        return m;
    }

    /// <summary>
    /// Divisor y unificador: caja de una celda con bocas en las caras que usa cada uno. Se distinguen por la forma de la tapa
    /// (aleta o cúpula) y por las flechas, sin depender del color. El frente de la simulación es −Z.
    /// </summary>
    private static void Junction(Builder b, bool splitter)
    {
        b.Plinth(2, 2);
        b.Box("hull", 0, 0.68f, 0, 1.2f, 0.92f, 1.2f);
        b.Seams(0, 0.68f, 0, 1.2f, 0.92f, 1.2f);
        var faces = new (Vector3 dir, bool output)[]
        {
            (Vector3.Forward, true),
            (Vector3.Back, false),
            (Vector3.Right, splitter),
            (Vector3.Left, splitter),
        };
        foreach (var (dir, output) in faces)
        {
            var side = new Vector3(dir.Z, 0, -dir.X);
            b.Box("steel_dark", dir.X * 0.74f, 0.62f, dir.Z * 0.74f, Mathf.Abs(dir.X) * 0.28f + Mathf.Abs(dir.Z) * 0.82f, 0.62f, Mathf.Abs(dir.Z) * 0.28f + Mathf.Abs(dir.X) * 0.82f);
            b.Box(output ? "signal" : "cream", dir.X * 0.74f, 1.0f, dir.Z * 0.74f, Mathf.Abs(dir.X) * 0.32f + Mathf.Abs(dir.Z) * 0.9f, 0.1f, Mathf.Abs(dir.Z) * 0.32f + Mathf.Abs(dir.X) * 0.9f);
            // Punta de flecha sobre la tapa: hacia fuera si es salida, hacia el centro si es entrada.
            var flow = output ? dir : -dir;
            var tip = new Vector3(dir.X * 0.46f, 1.2f, dir.Z * 0.46f) + flow * 0.14f;
            b.Beam("amber", tip - flow * 0.22f + side * 0.22f, tip, 0.08f, 0.04f);
            b.Beam("amber", tip - flow * 0.22f - side * 0.22f, tip, 0.08f, 0.04f);
        }
        if (splitter) b.Box("steel", 0, 1.26f, 0, 0.1f, 0.22f, 0.44f);
        else b.Cyl("chrome", 0, 1.22f, 0, 0.2f, 0.12f, sides: 12, topRadius: 0.08f);
    }

    private static MachineModel BuildMachine(string def)
    {
        switch (def)
        {
            case "ore_extractor":
            case "ice_extractor":
            case "silicon_extractor":
                return Extractor(def);
            case "smelter": return Smelter();
            case "recycler": return Recycler();
            case "aux_core":
            case "rtg_generator":
                return Generator(def);
            case "depot": return Depot();
            case "export_terminal": return Terminal();
            case "station": return Station();
            case "assembler":
            case "cryo_assembler":
                return GenericPress(def);
        }
        var b = new Builder();
        var m = new MachineModel();
        switch (def)
        {
            case "chiller":
            {
                b.Plinth(4, 6);
                b.Box("panel", 0, 0.92f, 0.9f, 3.1f, 1.35f, 2.9f);
                b.Seams(0, 0.92f, 0.9f, 3.1f, 1.35f, 2.9f);
                b.Louvers("steel_dark", 0, 0.95f, 2.37f, 2.5f, 0.9f, 6);
                // Condensador: depósito vertical con escarcha y serpentín de cobre a la vista.
                b.Lathe("chrome", -0.75f, 0.6f, -1.35f, new[] { (0f, 0f), (0.72f, 0f), (0.78f, 0.12f), (0.78f, 2.5f), (0.5f, 3f), (0f, 3.08f) }, 16);
                foreach (var y in new[] { 1.15f, 2.05f, 2.95f }) b.Cyl("cyan", -0.75f, y, -1.35f, 0.8f, 0.09f, sides: 16);
                var coil = new List<Vector3>();
                for (var i = 0; i <= 28; i++)
                {
                    var angle = i * 0.62f;
                    coil.Add(new Vector3(0.75f + Mathf.Cos(angle) * 0.42f, 0.75f + i * 0.082f, -1.35f + Mathf.Sin(angle) * 0.42f));
                }
                b.Tube("copper", 0.05f, 6, coil.ToArray());
                b.Tube("copper", 0.07f, 8, new Vector3(-0.75f, 3.7f, -1.35f), new Vector3(-0.75f, 3.95f, -0.6f), new Vector3(0.75f, 3.95f, -0.6f), coil[^1]);
                // Ventilador carenado: se ve girar cuando la máquina trabaja.
                b.Lathe("steel_dark", 0, 1.6f, 1.05f, new[] { (0.98f, 0f), (1.16f, 0f), (1.16f, 0.34f), (0.98f, 0.34f) }, 22);
                b.Port(0, 2.96f, true);
                b.Port(0, -2.96f, false);
                m.Rotor = new Builder().Box("steel", 0, 0, 0, 1.85f, 0.02f, 0.26f, roll: 12, bevel: 0).Box("steel", 0, 0, 0, 0.26f, 0.02f, 1.85f, pitch: 12, bevel: 0).Cyl("cyan", 0, 0, 0, 0.12f, 0.08f, sides: 8).Commit();
                m.RotorPos = new Vector3(0, 1.76f, 1.05f);
                m.RotorSpeed = 6f;
                m.Height = 4.1f;
                m.LampPos = new Vector3(1.3f, 1.72f, 2.28f);
                m.GlowPos = new Vector3(0, 2.4f, 0);
                m.GlowColor = new Color(0.4f, 0.85f, 1f);
                break;
            }
            case "wafer_refinery":
            {
                b.Plinth(6, 6);
                // Sala limpia baja con ventana del proceso y, detrás, horno de difusión de tubos verticales con su chimenea.
                b.Box("cream", -0.55f, 0.95f, 0.65f, 3.9f, 1.45f, 3.5f);
                b.Seams(-0.55f, 0.95f, 0.65f, 3.9f, 1.45f, 3.5f);
                b.Box("steel_dark", -0.55f, 1.15f, 2.42f, 3.1f, 0.8f, 0.05f);
                b.Box("heat", -0.55f, 1.15f, 2.46f, 2.8f, 0.58f, 0.012f, bevel: 0);
                for (var i = -3; i <= 3; i++) b.Box("steel_dark", -0.55f + i * 0.4f, 1.15f, 2.49f, 0.04f, 0.62f, 0.03f, bevel: 0);
                foreach (var (x, z) in new[] { (-1.55f, -1.75f), (-0.45f, -1.75f), (0.65f, -1.75f) })
                {
                    b.Lathe("chrome", x, 0.5f, z, new[] { (0.46f, 0f), (0.5f, 0.06f), (0.5f, 3.1f), (0.32f, 3.38f), (0f, 3.44f) }, 14);
                    b.Cyl("cyan", x, 3.1f, z, 0.52f, 0.08f, sides: 14);
                    b.Cyl("steel_dark", x, 1.4f, z, 0.53f, 0.1f, sides: 14);
                }
                b.Beam("steel_dark", new Vector3(-2.05f, 2.95f, -1.75f), new Vector3(1.15f, 2.95f, -1.75f), 0.14f, 0.14f);
                b.Lathe("steel_dark", 1.95f, 0.45f, -1.45f, new[] { (0f, 0f), (0.72f, 0f), (0.72f, 1.25f), (0.3f, 1.85f), (0.26f, 4.65f), (0.34f, 4.7f), (0.34f, 4.85f), (0f, 4.85f) }, 12);
                b.Tube("copper", 0.07f, 8, new Vector3(0.65f, 2.95f, -1.75f), new Vector3(1.3f, 2.95f, -1.6f), new Vector3(1.7f, 2.5f, -1.45f));
                // Estante de obleas al lado del brazo.
                b.Box("steel_dark", 1.95f, 0.95f, 1f, 1.1f, 1.45f, 1.5f);
                for (var i = 0; i < 4; i++) b.Cyl("cyan", 1.95f, 0.5f + i * 0.33f, 1f, 0.42f, 0.04f, sides: 12);
                b.Cyl("steel_dark", -0.55f, 1.74f, 0.45f, 0.5f, 0.16f, sides: 14);
                b.Port(0, 2.96f, true);
                b.Port(0, -2.96f, false);
                // Brazo que lleva obleas: gira mientras trabaja.
                m.Rotor = new Builder()
                    .Beam("steel", new Vector3(0, 0.08f, 0), new Vector3(1.25f, 0.34f, 0), 0.14f, 0.14f)
                    .Beam("steel", new Vector3(1.25f, 0.34f, 0), new Vector3(1.25f, 0.06f, 0.5f), 0.1f, 0.1f)
                    .Cyl("cyan", 1.25f, 0.03f, 0.55f, 0.28f, 0.03f, sides: 12)
                    .Commit();
                m.RotorPos = new Vector3(-0.55f, 1.82f, 0.45f);
                m.RotorSpeed = 1.3f;
                m.Height = 5.3f;
                m.LampPos = new Vector3(-2.5f, 1.72f, 2.5f);
                m.GlowPos = new Vector3(-0.55f, 2, 3.4f);
                break;
            }
            case "nav_fab":
            {
                b.Plinth(6, 6);
                b.Box("navy", 0, 0.9f, 0, 5.2f, 1.2f, 5.2f);
                b.Seams(0, 0.9f, 0, 5.2f, 1.2f, 5.2f);
                b.Lathe("panel", 0, 1.5f, 0, new[] { (2f, 0f), (2f, 0.14f), (1.4f, 1.1f), (1f, 1.2f) }, 20);
                b.Ball("cyan", 0, 3.05f, 0, 0.62f, 16, 8);
                b.Ball("glass", 0, 3.05f, 0, 0.86f, 18, 8);
                foreach (var (x, z) in new[] { (-2.2f, -2.2f), (2.2f, -2.2f), (-2.2f, 2.2f), (2.2f, 2.2f) })
                    b.Beam("signal", new Vector3(x, 1.5f, z), new Vector3(x * 1f, 3.4f, z), 0.16f, 0.16f);
                b.Port(0, 2.96f, true);
                b.Port(0, -2.96f, false);
                m.Rotor = new Builder().Lathe("copper", 0, 0, 0, new[] { (2.2f, -0.05f), (2.42f, -0.05f), (2.42f, 0.05f), (2.2f, 0.05f) }, 28).Box("steel_dark", 0, 0, 0, 4.7f, 0.05f, 0.14f).Commit();
                m.RotorPos = new Vector3(0, 3.8f, 0);
                m.RotorSpeed = 1.4f;
                m.Height = 4.3f;
                m.LampPos = new Vector3(2.3f, 1.62f, 2.62f);
                m.GlowPos = new Vector3(0, 3.05f, 1.6f);
                m.GlowColor = new Color(0.4f, 0.85f, 1f);
                break;
            }
            case "radiator":
            case "cryo_radiator":
            {
                var depth = def == "radiator" ? 4f : 6f;
                var cryoFins = def != "radiator";
                b.Plinth(4, depth);
                b.Box("hull", 0, 0.5f, 0, 3.15f, 0.5f, depth - 0.85f);
                var fin = cryoFins ? "chrome" : "steel";
                var tip = cryoFins ? "cyan" : "signal";
                var rows = cryoFins ? 7 : 4;
                var span = (depth - 1.1f) / 2;
                foreach (var side in new[] { -1f, 1f })
                {
                    // Cada aleta va inclinada hacia fuera; entre ellas se ve el aire.
                    for (var i = -rows; i <= rows; i++)
                    {
                        var z = span * i / (float)rows;
                        b.Box(fin, side * 0.82f, 1.95f, z, 0.06f, 2.3f, 0.2f, roll: side * -22, bevel: 0);
                    }
                    b.Beam("copper", new Vector3(side * 0.42f, 0.82f, -span - 0.1f), new Vector3(side * 0.42f, 0.82f, span + 0.1f), 0.13f, 0.13f);
                    b.Beam("copper", new Vector3(side * 1.25f, 3.03f, -span - 0.1f), new Vector3(side * 1.25f, 3.03f, span + 0.1f), 0.11f, 0.11f);
                    b.Beam(tip, new Vector3(side * 1.25f, 3.16f, -span), new Vector3(side * 1.25f, 3.16f, span), 0.05f, 0.05f);
                    foreach (var end in new[] { -1f, 1f })
                        b.Beam("steel_dark", new Vector3(side * 0.3f, 0.75f, end * (span + 0.14f)), new Vector3(side * 1.36f, 3.1f, end * (span + 0.14f)), 0.1f, 0.1f);
                }
                // Colector central y ventilador: se ve girar mientras disipa.
                b.Lathe("steel_dark", 0, 2.25f, 0, new[] { (0f, 0f), (0.42f, 0f), (0.42f, 0.14f), (0.14f, 0.2f), (0f, 0.2f) }, 12);
                b.Beam("steel_dark", new Vector3(0, 0.75f, 0), new Vector3(0, 2.25f, 0), 0.18f, 0.18f);
                m.Rotor = new Builder().Box("steel", 0, 0, 0, 1.4f, 0.02f, 0.2f, roll: 14, bevel: 0).Box("steel", 0, 0, 0, 0.2f, 0.02f, 1.4f, pitch: 14, bevel: 0).Commit();
                m.RotorPos = new Vector3(0, 2.5f, 0);
                m.RotorSpeed = 5f;
                m.Height = 3.4f;
                m.LampPos = new Vector3(1.45f, 0.86f, depth / 2 - 0.48f);
                break;
            }
            case "cargo_pad":
            {
                b.Box("dark", 0, -0.05f, 0, 7.9f, 0.7f, 7.9f, bevel: 0.05f);
                b.Lathe("steel_dark", 0, 0.3f, 0, new[] { (0f, 0f), (3.6f, 0f), (3.6f, 0.14f), (3.4f, 0.22f), (0f, 0.22f) }, 32);
                b.Lathe("stripe", 0, 0.53f, 0, new[] { (2.75f, 0f), (3.2f, 0f) }, 32);
                b.Lathe("tread", 0, 0.52f, 0, new[] { (0f, 0f), (2.7f, 0f) }, 28);
                foreach (var (x, z) in new[] { (-3.4f, -3.4f), (3.4f, -3.4f), (-3.4f, 3.4f), (3.4f, 3.4f) })
                    PadMast(b, x, z);
                // Compuerta central y anclajes: se lee como plataforma de aterrizaje, no como una losa.
                b.Lathe("steel_dark", 0, 0.52f, 0, new[] { (1.25f, 0f), (1.3f, 0.05f), (1.2f, 0.07f), (0f, 0.07f) }, 24);
                b.Box("dark", 0, 0.6f, 0, 2.3f, 0.015f, 0.05f, bevel: 0);
                BoltRing(b, 0, 0.6f, 0, 1.08f, 12);
                b.Port(0, 3.9f, true);
                b.Port(0, -3.9f, false);
                m.Height = 2f;
                m.LampPos = new Vector3(3.4f, 1.72f, 3.4f);
                break;
            }
            case "shuttle_terminal":
            {
                b.Box("dark", 0, -0.05f, 0, 7.9f, 0.7f, 7.9f, bevel: 0.05f);
                b.Box("tread", 0, 0.33f, 0, 7.3f, 0.1f, 7.3f);
                foreach (var (x, z) in new[] { (-3.4f, -3.4f), (3.4f, -3.4f), (-3.4f, 3.4f), (3.4f, 3.4f) })
                {
                    b.Cyl("signal", x, 1f, z, 0.09f, 1.4f, sides: 8);
                    b.Ball("amber", x, 1.76f, z, 0.13f, 8, 4);
                }
                b.Box("stripe", 0, 0.39f, 3.3f, 6.4f, 0.015f, 0.22f, bevel: 0);
                b.Box("stripe", 0, 0.39f, -3.3f, 6.4f, 0.015f, 0.22f, bevel: 0);
                // Lanzadera posada: fuselaje, cabina, alas, toberas y patas.
                b.Lathe("cream", 0, 1.95f, 0, new[] { (0f, -2.9f), (0.9f, -2.7f), (1.35f, -1.6f), (1.42f, 0.9f), (1.05f, 2.2f), (0.6f, 2.8f), (0f, 2.95f) }, 18, pitch: 90);
                b.Box("glass", 0, 2.4f, 1.9f, 1.3f, 0.6f, 1f, pitch: -28, bevel: 0.02f);
                foreach (var side in new[] { -1f, 1f })
                {
                    b.Taper("hull", side * 2.2f, 1.65f, -1f, 1.6f, 2.6f, 1.4f, 1.2f, 0.16f, roll: side * -6);
                    b.Beam("steel_dark", new Vector3(side * 1.1f, 1.1f, -1.8f), new Vector3(side * 1.5f, 0.4f, -1.8f), 0.12f, 0.12f);
                    b.Beam("steel_dark", new Vector3(side * 1.1f, 1.1f, 1.4f), new Vector3(side * 1.5f, 0.4f, 1.4f), 0.12f, 0.12f);
                    b.Box("steel_dark", side * 1.5f, 0.42f, -1.8f, 0.5f, 0.05f, 0.6f);
                    b.Box("steel_dark", side * 1.5f, 0.42f, 1.4f, 0.5f, 0.05f, 0.6f);
                    b.Lathe("steel_dark", side * 0.7f, 1.95f, -3f, new[] { (0.24f, 0.25f), (0.48f, -0.25f), (0.42f, -0.25f), (0.2f, 0.2f) }, 12, pitch: 90);
                    b.Box("signal", side * 2.7f, 1.72f, -1.2f, 0.5f, 0.06f, 1.2f);
                }
                m.Height = 3.6f;
                m.LampPos = new Vector3(0, 3.42f, 0);
                m.GlowPos = new Vector3(0, 4, 3.4f);
                break;
            }
            case "splitter":
            case "merger":
                Junction(b, def == "splitter");
                if (def == "splitter")
                {
                    // Paleta desviadora que reparte a cada salida: gira mientras pasa material.
                    b.Cyl("steel_dark", 0, 1.2f, 0, 0.3f, 0.1f, sides: 12);
                    m.Rotor = new Builder().Box("amber", 0, 0, 0, 0.92f, 0.16f, 0.12f).Box("signal", 0.38f, 0, 0, 0.16f, 0.2f, 0.26f).Commit();
                    m.RotorPos = new Vector3(0, 1.34f, 0);
                    m.RotorSpeed = 3f;
                }
                else
                {
                    // Tolva de embudo: lo que entra por tres lados cae a una sola salida.
                    b.Lathe("chrome", 0, 1.14f, 0, new[] { (0.16f, 0f), (0.2f, 0.02f), (0.56f, 0.4f), (0.6f, 0.46f), (0.52f, 0.46f), (0.16f, 0.08f) }, 16);
                    b.Cyl("cyan", 0, 1.56f, 0, 0.61f, 0.05f, sides: 16);
                }
                m.Height = 1.7f;
                m.LampPos = new Vector3(0.68f, 1.18f, 0.68f);
                break;
            case "storage_crate":
            {
                b.Plinth(4, 4);
                Crate(b, 0, 1.03f, 0, 3.15f, 1.6f, 3.15f, "navy");
                b.Box("steel_dark", 0, 1.86f, 0, 3.25f, 0.08f, 3.25f);
                b.Box("hull", 0, 1.94f, 0, 2.6f, 0.1f, 2.6f);
                b.Box("amber", 0, 1.8f, 1.62f, 1.1f, 0.05f, 0.02f, bevel: 0);
                b.Port(0, 1.96f, true);
                b.Port(0, -1.96f, false);
                m.Height = 2.1f;
                m.LampPos = new Vector3(1.3f, 2.0f, 1.3f);
                break;
            }
            default:
                b.Box("panel", 0, 1f, 0, 3f, 2f, 3f);
                m.LampPos = new Vector3(0, 2.2f, 0);
                break;
        }
        m.Body = b.Commit();
        return m;
    }

    private static readonly Dictionary<string, ArrayMesh> DebrisCache = new();

    /// <summary>Chapa arrancada: pliegue vivo, torsión, ondas y contorno desgarrado. Nunca un rectángulo limpio.</summary>
    private static void ScrapSheet(Builder b, Rng r, string mat, Vector3 at, float size = 1f)
    {
        var crease = r.Range(0.25f, 0.7f) * (r.Next() < 0.5f ? -1 : 1);
        var creaseAt = r.Range(-0.2f, 0.25f);
        var twist = r.Range(-1.6f, 1.6f);
        var w = r.Range(0.5f, 0.85f) * size;
        var d = r.Range(0.6f, 1.05f) * size;
        var seed = r.Range(0, 100);
        var tf = new Transform3D(Basis.FromEuler(new Vector3(r.Range(-0.7f, 0.7f), r.Range(0, 6.28f), r.Range(-0.7f, 0.7f))), at);
        b.Sheet(mat, tf, w, d, 8, 9, (u, v) =>
        {
            var t = v / d;
            var fold = Mathf.Abs(t - creaseAt) * crease;
            var y = fold + u * t * twist + (Terrain.Noise3(new Vector3(u * 9 + seed, 1, v * 9)) - 0.5f) * 0.09f;
            var tear = Terrain.Noise3(new Vector3(u * 13 + seed, 5, v * 13));
            var edgeU = Mathf.Abs(u) / (w * 0.5f);
            var jag = 1 - Mathf.Pow(edgeU, 3) * tear * 0.45f;
            return new Vector3(u * (1 - tear * 0.38f * Mathf.Abs(t) * 2), y + 0.22f, v * jag);
        }, 0.025f, u => Terrain.Noise3(new Vector3(u * 17 + seed, 3, 0)) * 0.3f * size);
    }

    /// <summary>Viga en H retorcida: dos alas y alma siguiendo la misma curva.</summary>
    private static void BentBeam(Builder b, Rng r, string mat, Vector3 at)
    {
        var a = r.Range(0, Mathf.Tau);
        var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        var side = new Vector3(-dir.Z, 0, dir.X);
        var len = r.Range(0.9f, 1.5f);
        var kink = r.Range(0.25f, 0.6f);
        var lift = r.Range(0.2f, 0.65f);
        var p0 = at - dir * len * 0.6f + Vector3.Up * r.Range(0.06f, 0.2f);
        var p1 = at + side * kink * 0.5f + Vector3.Up * lift;
        var p2 = at + dir * len * 0.6f + side * kink + Vector3.Up * (lift * 0.2f + 0.08f);
        // Dos tramos rectos con arista viva: una viga doblada se quiebra en el pliegue, no se curva como una rama.
        var offset = side * 0.075f;
        foreach (var (s, e) in new[] { (p0, p1), (p1, p2) })
        {
            b.Beam(mat, s + offset, e + offset, 0.13f, 0.03f);
            b.Beam(mat, s - offset, e - offset, 0.13f, 0.03f);
            b.Beam(mat, s, e, 0.03f, 0.13f);
        }
    }

    /// <summary>Variantes de escombros: varias formas compartidas entre instancias.</summary>
    public static ArrayMesh Debris(string def, int variant)
    {
        var key = def + variant;
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var r = new Rng(StableHash(key));
        var b = new Builder();
        Vector3 Near(float spread) => new(r.Range(-spread, spread), 0, r.Range(-spread, spread));
        switch (def)
        {
            case "scrap_pile":
            case "shadow_scrap":
            {
                var dark = def == "shadow_scrap";
                // Tierra removida del color del suelo, medio enterrada y con terrones: el montón apoya sin un disco claro debajo.
                b.Rock(dark ? "rock" : "soil", 0, -0.03f, 0, 1.0f, 0.16f, 0.9f, variant + 3, 2);
                for (var i = 0; i < 7; i++)
                {
                    var clod = r.Range(0, Mathf.Tau);
                    var far = r.Range(0.72f, 1.12f);
                    b.Rock(dark ? "rock" : "soil", Mathf.Cos(clod) * far, 0, Mathf.Sin(clod) * far, r.Range(0.06f, 0.15f), r.Range(0.05f, 0.1f), r.Range(0.06f, 0.15f), variant * 11 + i + 5, 0);
                }
                // Mucho metal oxidado, poca pintura: el naranja queda como resto reconocible, no como masa.
                for (var i = 0; i < 6; i++)
                    ScrapSheet(b, r, i == 0 ? (dark ? "dark" : "signal") : i == 3 ? "steel_dark" : i % 2 == 0 ? "rust" : "rustlight", Near(0.55f), i == 0 ? 0.55f : r.Range(0.6f, 1f));
                BentBeam(b, r, dark ? "steel_dark" : "rust", Near(0.25f));
                BentBeam(b, r, dark ? "rust" : "steel_dark", Near(0.4f));
                if (variant != 1) BentBeam(b, r, "rustlight", Near(0.5f));
                // Bidón aplastado, o tubo arrancado con su brida.
                if (variant == 2)
                {
                    b.Tube("rustlight", 0.11f, 8, new Vector3(-0.6f, 0.15f, 0.3f), new Vector3(0, 0.3f, 0.45f), new Vector3(0.55f, 0.7f, 0.1f));
                    b.Cyl("steel_dark", 0.55f, 0.72f, 0.1f, 0.17f, 0.05f, 45, 30, 10);
                }
                else
                {
                    var c = Near(0.42f);
                    var phase = r.Range(0, 6f);
                    b.Warp = p =>
                    {
                        var q = p - c;
                        var squash = 0.72f + 0.28f * Mathf.Sin(Mathf.Atan2(q.Z, q.X) * 2 + phase);
                        return c + new Vector3(q.X * squash, q.Y, q.Z * (2 - squash));
                    };
                    b.Lathe(variant == 0 ? "dark" : "rustlight", c.X, 0.34f, c.Z, new[] { (0f, -0.3f), (0.26f, -0.3f), (0.28f, -0.26f), (0.28f, 0.26f), (0.26f, 0.3f), (0f, 0.3f) }, 12, roll: 12);
                    b.Warp = null;
                }
                if (dark) b.Ball("spectral", r.Range(-0.3f, 0.3f), 0.72f, r.Range(-0.3f, 0.3f), 0.22f, 8, 5);
                break;
            }
            case "red_rock":
                b.Rock("redrock", 0, 0.28f, 0, 0.95f, 0.72f, 0.82f, variant * 7 + 1, 2, 3f, r.Range(0, 90));
                b.Rock("redrock", r.Range(-0.55f, 0.55f), 0.12f, r.Range(-0.55f, 0.55f), 0.42f, 0.34f, 0.38f, variant * 7 + 4, 1);
                break;
            case "ice_block":
                b.Rock("ice", 0, 0.3f, 0, 0.82f, 0.86f, 0.72f, variant * 5 + 2, 1, 0, r.Range(0, 90));
                b.Rock("ice", r.Range(-0.55f, 0.55f), 0.14f, r.Range(-0.55f, 0.55f), 0.4f, 0.46f, 0.36f, variant * 5 + 6, 1);
                b.Rock("crust", 0, 0.03f, 0, 1.05f, 0.14f, 1f, variant * 5 + 9, 1);
                break;
            default:
                // Costra sobre un yacimiento: lámina ancha con fragmentos, que cubre la celda.
                b.Rock("crust", 0, 0.06f, 0, 1.16f, 0.42f, 1.16f, variant * 3 + 5, 2, 0, r.Range(0, 90));
                for (var i = 0; i < 3; i++) b.Rock("ice", r.Range(-0.6f, 0.6f), 0.3f, r.Range(-0.6f, 0.6f), r.Range(0.22f, 0.4f), r.Range(0.2f, 0.34f), r.Range(0.22f, 0.4f), variant * 3 + 8 + i, 1);
                break;
        }
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    private static int StableHash(string s)
    {
        var h = 17;
        foreach (var c in s) h = unchecked(h * 31 + c);
        return h;
    }

    public static ArrayMesh NodeMesh(string item)
    {
        var key = "node_" + item;
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var r = new Rng(StableHash(key));
        var b = new Builder();
        var mat = item == "ore" ? "ore" : item == "silicon_ore" ? "silicon" : "ice";
        // Yacimiento: suelo removido y vetas que asoman, reconocibles por forma además de color.
        b.Rock(item == "silicon_ore" ? "redrock" : "soil", 0, 0.01f, 0, 2.15f, 0.12f, 2.15f, StableHash(item) & 255, 2);
        for (var i = 0; i < 14; i++)
        {
            var a = r.Range(0, Mathf.Tau);
            var d = r.Range(0.9f, 1.9f);
            var x = Mathf.Cos(a) * d;
            var z = Mathf.Sin(a) * d;
            if (item == "ice")
                b.Rock(mat, x, 0.1f, z, r.Range(0.16f, 0.3f), r.Range(0.25f, 0.5f), r.Range(0.16f, 0.3f), i + 40, 0);
            else
                b.Cyl(mat, x, r.Range(0.18f, 0.3f), z, r.Range(0.1f, 0.2f), r.Range(0.4f, 0.8f), r.Range(-25, 25), r.Range(-25, 25), 5, 0.015f);
        }
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Mástil de señal ferroviaria: base, poste y cabeza con dos focos. Los focos son objetos aparte, para cambiarles el material. Asoman por todos los lados: la señal vale en los dos sentidos.</summary>
    public static ArrayMesh SignalMast()
    {
        const string key = "signal_mast";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        b.Box("regolith", 0, 0.12f, 0, 0.8f, 0.24f, 0.8f);
        b.Cyl("steel_dark", 0, 1.75f, 0, 0.09f, 3.1f, sides: 8);
        b.Box("steel_dark", 0, 3.65f, 0, 0.36f, 1.3f, 0.34f);
        b.Box("signal", 0, 2.8f, 0, 0.3f, 0.2f, 0.22f);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    private static StandardMaterial3D? _signalRed, _signalGreen;

    /// <summary>Focos de señal con menos brillo que los pilotos: a más energía, el rojo se ve rosa y deja de leerse como rojo.</summary>
    public static StandardMaterial3D SignalLamp(bool red)
    {
        if (red) return _signalRed ??= Dimmed("red");
        return _signalGreen ??= Dimmed("green");
        static StandardMaterial3D Dimmed(string name)
        {
            var m = (StandardMaterial3D)Materials.Lamp(name).Duplicate();
            m.EmissionEnergyMultiplier = 0.9f;
            return m;
        }
    }

    public static readonly Vector3 SignalRedLamp = new(0, 3.97f, 0);
    public static readonly Vector3 SignalGreenLamp = new(0, 3.37f, 0);

    /// <summary>Palanca de desvío junto a un nodo del que salen tres o más vías.</summary>
    public static ArrayMesh RailSwitch()
    {
        const string key = "rail_switch";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        b.Box("regolith", 0, 0.1f, 0, 0.9f, 0.2f, 0.7f);
        b.Box("steel_dark", 0, 0.42f, 0, 0.6f, 0.44f, 0.42f);
        b.Beam("signal", new Vector3(-0.12f, 0.62f, 0), new Vector3(0.28f, 1.35f, 0), 0.04f, 0.03f);
        b.Box("amber", 0.3f, 1.4f, 0, 0.3f, 0.16f, 0.06f);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Malla de un tramo de vía: balasto, traviesas y raíles sobre la curva real.</summary>
    public static ArrayMesh RailMesh(Func<double, Vector3> eval, float lengthM)
    {
        var b = new Builder();
        var steps = Mathf.Max(1, Mathf.RoundToInt(lengthM / 1.2f));
        var last = eval(0);
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var p = eval(t);
            var next = eval(Math.Min(1, t + 0.01));
            var prev = eval(Math.Max(0, t - 0.01));
            var dir = (next - prev).Normalized();
            var yaw = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
            b.Box("rust", p.X, 0.06f, p.Z, 2.2f, 0.1f, 0.26f, yaw, bevel: 0.015f);
            if (i == steps) continue;
            var q = eval((i + 1) / (double)steps);
            var mid = (p + q) / 2;
            var d = q - p;
            var side = new Vector3(d.Z, 0, -d.X).Normalized() * 0.72f;
            var segYaw = Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z));
            var len = d.Length() + 0.03f;
            b.Box("regolith", mid.X, -0.06f, mid.Z, 2.7f, 0.22f, len, segYaw, bevel: 0);
            b.Box("steel", mid.X + side.X, 0.17f, mid.Z + side.Z, 0.07f, 0.14f, len, segYaw, bevel: 0.015f);
            b.Box("steel", mid.X - side.X, 0.17f, mid.Z - side.Z, 0.07f, 0.14f, len, segYaw, bevel: 0.015f);
            last = q;
        }
        _ = last;
        return b.Commit();
    }

    private static void Bogie(Builder b, float z)
    {
        b.Box("steel_dark", 0, 0.5f, z, 1.5f, 0.2f, 1.9f);
        foreach (var wz in new[] { z - 0.55f, z + 0.55f })
        {
            b.Cyl("steel_dark", 0, 0.34f, wz, 0.06f, 1.74f, roll: 90, sides: 8);
            foreach (var x in new[] { -0.76f, 0.76f })
            {
                b.Cyl("steel", x, 0.34f, wz, 0.32f, 0.1f, roll: 90, sides: 14);
                b.Cyl("steel_dark", x * 1.06f, 0.34f, wz, 0.12f, 0.05f, roll: 90, sides: 8);
            }
        }
    }

    public static ArrayMesh Locomotive()
    {
        const string key = "loco";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        // Bastidor y bogies.
        b.Box("dark", 0, 0.76f, 0, 2.3f, 0.36f, 7.1f);
        Bogie(b, -2.3f);
        Bogie(b, 2.3f);
        b.Box("stripe", 0, 0.78f, 3.55f, 2.3f, 0.3f, 0.04f, bevel: 0);
        b.Box("steel_dark", 0, 0.64f, 3.62f, 0.4f, 0.22f, 0.3f);
        foreach (var x in new[] { -0.8f, 0.8f }) b.Cyl("steel", x, 0.84f, 3.68f, 0.12f, 0.22f, pitch: 90, sides: 10);
        // Capó motor con rejillas y escape.
        b.Box("signal", 0, 1.6f, -0.95f, 1.9f, 1.3f, 4.5f);
        b.Seams(0, 1.6f, -0.95f, 1.9f, 1.3f, 4.5f);
        foreach (var side in new[] { -1f, 1f })
        {
            b.Louvers("steel_dark", side * 0.97f, 1.66f, -0.3f, 1.7f, 0.74f, 6, true);
            b.Louvers("steel_dark", side * 0.97f, 1.66f, -2.2f, 1.2f, 0.74f, 6, true);
            b.Beam("steel", new Vector3(side * 1.15f, 1.2f, -3.2f), new Vector3(side * 1.15f, 1.2f, 1f), 0.03f, 0.03f);
        }
        b.Tube("steel_dark", 0.14f, 10, new Vector3(0, 2.2f, -2.4f), new Vector3(0, 2.72f, -2.4f));
        b.Cyl("rust", 0, 2.75f, -2.4f, 0.2f, 0.1f, sides: 10);
        // Cabina con ventanales de vidrio.
        b.Box("cream", 0, 2.05f, 2.35f, 2.35f, 2.2f, 2f);
        b.Box("glass", 0, 2.45f, 3.36f, 1.95f, 0.8f, 0.03f, bevel: 0);
        b.Box("steel_dark", 0, 2.45f, 3.34f, 2.1f, 0.94f, 0.02f, bevel: 0);
        foreach (var side in new[] { -1f, 1f })
        {
            b.Box("glass", side * 1.18f, 2.48f, 2.4f, 0.03f, 0.72f, 1.3f, bevel: 0);
            b.Box("steel_dark", side * 1.17f, 2.48f, 2.4f, 0.02f, 0.86f, 1.44f, bevel: 0);
            b.Box("amber", side * 0.72f, 1.2f, 3.39f, 0.36f, 0.16f, 0.03f, bevel: 0);
        }
        b.Box("signal", 0, 3.2f, 2.35f, 2.45f, 0.1f, 2.1f);
        b.Cyl("steel", 0.5f, 3.42f, 2.2f, 0.03f, 0.36f, sides: 6);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    public static ArrayMesh Wagon()
    {
        const string key = "wagon";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        b.Box("dark", 0, 0.76f, 0, 2.3f, 0.3f, 6.5f);
        Bogie(b, -2.2f);
        Bogie(b, 2.2f);
        // Tolva abierta: la carga real se ve dentro.
        b.Box("hull", 0, 1.24f, -3.02f, 2.3f, 0.72f, 0.1f);
        b.Box("hull", 0, 1.24f, 3.02f, 2.3f, 0.72f, 0.1f);
        foreach (var side in new[] { -1f, 1f })
        {
            b.Box("hull", side * 1.14f, 1.24f, 0, 0.1f, 0.72f, 6.1f);
            for (var i = -4; i <= 4; i++) b.Box("steel_dark", side * 1.19f, 1.24f, i * 0.72f, 0.03f, 0.74f, 0.08f, bevel: 0);
            b.Box("stripe", side * 1.2f, 1.55f, 0, 0.012f, 0.1f, 5.9f, bevel: 0);
        }
        b.Box("steel_dark", 0, 0.97f, 0, 2.2f, 0.06f, 6f);
        b.Box("steel_dark", 0, 0.66f, 3.34f, 0.3f, 0.16f, 0.3f);
        b.Box("steel_dark", 0, 0.66f, -3.34f, 0.3f, 0.16f, 0.3f);
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
        b.Box("tread", 0, -0.05f, 0, 3.2f, 0.1f, 5f);
        b.Box("hull", 0, 2.6f, 0, 3.2f, 0.12f, 5f);
        b.Box("hull", -1.6f, 1.3f, 0.3f, 0.1f, 2.5f, 4.4f);
        b.Box("hull", 1.6f, 1.3f, 0.3f, 0.1f, 2.5f, 4.4f);
        b.Box("hull", 0, 1.3f, 2.5f, 3.2f, 2.5f, 0.1f);
        b.Box("panel", 0, 0.35f, -2.3f, 3.1f, 0.7f, 0.5f, 0, 25);
        b.Box("signal", -1.5f, 1.3f, -2.4f, 0.14f, 2.5f, 0.14f);
        b.Box("signal", 1.5f, 1.3f, -2.4f, 0.14f, 2.5f, 0.14f);
        b.Box("panel", 0, 2.5f, -2.4f, 3.1f, 0.14f, 0.14f);
        b.Box("amber", -0.7f, 0.68f, -2.2f, 0.5f, 0.05f, 0.3f, 0, 25, bevel: 0);
        b.Box("cyan", 0.5f, 0.68f, -2.2f, 0.9f, 0.05f, 0.3f, 0, 25, bevel: 0);
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
            case Pluton.Simulation.MachineStatus.Overheated:
                // Termómetro
                Quad(0, 0.08f, 0.13f, 0.5f);
                Quad(0, -0.25f, 0.28f, 0.2f);
                Quad(0.16f, 0.24f, 0.12f, 0.05f);
                Quad(0.16f, 0.08f, 0.12f, 0.05f);
                material = IconBad;
                break;
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
                material = IconBad;
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

    /// <summary>Piloto de estado de la máquina: compartido; el color lo pone GameView con Materials.Lamp.</summary>
    public static ArrayMesh Lamp()
    {
        const string key = "lamp";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var q = new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 10, Rings = 5 };
        var a = new ArrayMesh();
        a.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, q.GetMeshArrays());
        DebrisCache[key] = a;
        return a;
    }

    /// <summary>Cómo se lee una máquina de lejos. El ritmo distingue los estados también sin ver el color.</summary>
    public enum MachineMood { Running, Waiting, Blocked }

    public static MachineMood Mood(Pluton.Simulation.MachineStatus status) => status switch
    {
        Pluton.Simulation.MachineStatus.Working or Pluton.Simulation.MachineStatus.Generating or Pluton.Simulation.MachineStatus.Cooling or Pluton.Simulation.MachineStatus.Storing or Pluton.Simulation.MachineStatus.Passing or Pluton.Simulation.MachineStatus.Ready => MachineMood.Running,
        Pluton.Simulation.MachineStatus.NoPower or Pluton.Simulation.MachineStatus.Overheated or Pluton.Simulation.MachineStatus.LowPower or Pluton.Simulation.MachineStatus.OutputFull => MachineMood.Blocked,
        _ => MachineMood.Waiting,
    };

    private static StandardMaterial3D? _lampWaiting, _lampBlocked;

    /// <summary>Material del piloto: verde fijo si funciona, ámbar lento si espera materiales, rojo más rápido si está bloqueada.</summary>
    public static StandardMaterial3D LampState(Pluton.Simulation.MachineStatus status) => Mood(status) switch
    {
        MachineMood.Running => Materials.Lamp("green"),
        MachineMood.Blocked => _lampBlocked ??= (StandardMaterial3D)Materials.Lamp("red").Duplicate(),
        _ => _lampWaiting ??= (StandardMaterial3D)Materials.Lamp("amber").Duplicate(),
    };

    /// <summary>Un solo cambio por fotograma en dos materiales compartidos, sea cual sea el número de máquinas. Con «Reducir efectos», fijos.</summary>
    public static void AnimateLamps(float time, bool calm)
    {
        if (_lampWaiting != null) _lampWaiting.EmissionEnergyMultiplier = calm ? 2.4f : 0.7f + 2.6f * (0.5f + 0.5f * Mathf.Sin(time * Mathf.Tau * 0.4f));
        if (_lampBlocked != null) _lampBlocked.EmissionEnergyMultiplier = calm ? 3.4f : 0.8f + 4f * (0.5f + 0.5f * Mathf.Sin(time * Mathf.Tau));
    }

    /// <summary>Decoración del evento: calabazas talladas y faroles. Sin colisión ni efecto en la economía.</summary>
    public static ArrayMesh HalloweenDecor()
    {
        const string key = "halloween";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var rng = new Rng(1031);
        var b = new Builder();
        for (var i = 0; i < 26; i++)
        {
            var a = rng.Range(0, Mathf.Tau);
            var r = rng.Range(8, 34);
            var x = Mathf.Cos(a) * r;
            var z = Mathf.Sin(a) * r;
            var s = rng.Range(0.35f, 0.7f);
            for (var lobe = 0; lobe < 7; lobe++)
            {
                var la = lobe * Mathf.Tau / 7;
                b.Ball("pumpkin", x + Mathf.Cos(la) * s * 0.42f, s * 0.62f, z + Mathf.Sin(la) * s * 0.42f, s * 0.62f, 8, 5, 0.95f);
            }
            b.Cyl("stem", x, s * 1.2f, z, 0.06f, 0.26f, sides: 6);
            // Cara orientada hacia el centro del ramal, apoyada en la superficie
            var fx = -Mathf.Cos(a);
            var fz = -Mathf.Sin(a);
            var sx = -fz;
            var sz = fx;
            var yaw = Mathf.RadToDeg(Mathf.Atan2(fx, fz));
            var depth = s * 0.98f;
            b.Box("amber", x + fx * depth + sx * s * 0.32f, s * 0.82f, z + fz * depth + sz * s * 0.32f, s * 0.2f, s * 0.2f, 0.05f, yaw, bevel: 0);
            b.Box("amber", x + fx * depth - sx * s * 0.32f, s * 0.82f, z + fz * depth - sz * s * 0.32f, s * 0.2f, s * 0.2f, 0.05f, yaw, bevel: 0);
            b.Box("amber", x + fx * depth, s * 0.42f, z + fz * depth, s * 0.62f, s * 0.11f, 0.05f, yaw, bevel: 0);
        }
        for (var i = 0; i < 12; i++)
        {
            var a = i * Mathf.Tau / 12;
            var x = Mathf.Cos(a) * 30;
            var z = Mathf.Sin(a) * 30;
            b.Cyl("dark", x, 1.5f, z, 0.08f, 3f, sides: 8);
            b.Box("dark", x, 3f, z, 0.9f, 0.08f, 0.08f);
            b.Ball("spectral", x + 0.4f, 2.75f, z, 0.17f, 8, 5);
        }
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    public static ArrayMesh Astronaut()
    {
        const string key = "astronaut";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        // Torso, mochila vital y extremidades redondeadas: se lee como persona a distancia.
        b.Lathe("suit", 0, 0.82f, 0, new[] { (0f, 0f), (0.19f, 0f), (0.25f, 0.2f), (0.27f, 0.5f), (0.19f, 0.62f), (0f, 0.62f) }, 10);
        b.Box("hull", 0, 1.18f, -0.26f, 0.4f, 0.52f, 0.2f);
        b.Box("signal", 0, 1.34f, -0.37f, 0.3f, 0.09f, 0.02f, bevel: 0);
        b.Ball("suit", 0, 1.6f, 0, 0.21f, 12, 7);
        b.Box("visor", 0, 1.61f, 0.17f, 0.26f, 0.13f, 0.08f);
        foreach (var side in new[] { -1f, 1f })
        {
            b.Tube("suit", 0.085f, 8, new Vector3(side * 0.13f, 0.82f, 0), new Vector3(side * 0.15f, 0.44f, 0.03f), new Vector3(side * 0.15f, 0.08f, 0));
            b.Box("rubber", side * 0.15f, 0.05f, 0.05f, 0.14f, 0.1f, 0.27f);
            // Brazo derecho (−X) con el codo doblado hasta la mano de la herramienta de RemoteToolView; el izquierdo, en postura de marcha.
            var hand = side < 0 ? new Vector3(-0.3f, 1.0f, 0.36f) : new Vector3(0.34f, 0.84f, 0.16f);
            var elbow = side < 0 ? new Vector3(-0.35f, 1.03f, 0.03f) : new Vector3(0.37f, 1.02f, 0.0f);
            b.Tube("suit", 0.065f, 8, new Vector3(side * 0.29f, 1.3f, 0), elbow, hand);
            b.Ball("hull", hand.X, hand.Y, hand.Z, 0.07f, 8, 4);
        }
        b.Box("amber", 0, 1.26f, 0.25f, 0.14f, 0.05f, 0.02f, bevel: 0);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }
}
