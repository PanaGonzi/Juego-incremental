using Godot;

namespace Pluton.Game;

public enum RigMode { Stowed, Cutter, Device }

public enum ImpactKind { Scrap, Ice, Rock }

/// <summary>Materiales de brazos y herramienta. La versión de primera persona usa su propio FOV y se acerca a la cámara para no atravesar el mundo.</summary>
internal sealed class RigMaterials
{
    public const float ViewFov = 56f;
    public const float ClipScale = 0.42f;

    private static RigMaterials? _view, _world;
    public static RigMaterials View => _view ??= new RigMaterials(true);
    public static RigMaterials World => _world ??= new RigMaterials(false);

    public readonly StandardMaterial3D Cloth, ClothDark, Band, Rubber, RubberGrip, Steel, DarkSteel, Paint, PaintDark, Led, Screen, Gold;
    private readonly bool _view3d;

    private RigMaterials(bool viewmodel)
    {
        _view3d = viewmodel;
        var weave = WeaveNormal();
        var grain = GrainNormal();
        Cloth = Make(new Color(0.62f, 0.64f, 0.63f), 0f, 0.93f, weave, 60f);
        ClothDark = Make(new Color(0.2f, 0.22f, 0.25f), 0f, 0.9f, weave, 60f);
        Band = Make(new Color(0.92f, 0.5f, 0.1f), 0f, 0.62f, weave, 60f);
        Rubber = Make(new Color(0.055f, 0.058f, 0.065f), 0f, 0.72f, grain, 34f);
        RubberGrip = Make(new Color(0.09f, 0.085f, 0.08f), 0f, 0.82f, grain, 90f);
        Steel = Make(new Color(0.62f, 0.64f, 0.67f), 0.92f, 0.34f);
        DarkSteel = Make(new Color(0.2f, 0.21f, 0.23f), 0.86f, 0.42f);
        Paint = Make(new Color(0.86f, 0.4f, 0.07f), 0.12f, 0.42f);
        PaintDark = Make(new Color(0.13f, 0.15f, 0.18f), 0.25f, 0.5f);
        Gold = Make(new Color(0.8f, 0.62f, 0.25f), 0.9f, 0.3f);
        Led = Make(new Color(0.2f, 1f, 0.55f), 0f, 0.3f);
        Led.EmissionEnabled = true;
        Led.Emission = new Color(0.2f, 1f, 0.55f);
        Led.EmissionEnergyMultiplier = 2.2f;
        Screen = Make(new Color(0.03f, 0.05f, 0.06f), 0f, 0.18f);
        Screen.EmissionEnabled = true;
        Screen.Emission = new Color(1f, 0.67f, 0.25f);
        Screen.EmissionEnergyMultiplier = 1.25f;
        Screen.EmissionOperator = BaseMaterial3D.EmissionOperatorEnum.Multiply;
        Screen.EmissionTexture = ScreenGrid();
        Screen.Uv1Triplanar = true;
        Screen.Uv1Scale = new Vector3(14f, 14f, 14f);
    }

    private StandardMaterial3D Make(Color albedo, float metal, float rough, Texture2D? normal = null, float scale = 1f)
    {
        var m = new StandardMaterial3D { AlbedoColor = albedo, Metallic = metal, Roughness = rough };
        if (normal != null)
        {
            m.NormalEnabled = true;
            m.NormalTexture = normal;
            m.NormalScale = 0.65f;
            m.Uv1Triplanar = true;
            m.Uv1Scale = new Vector3(scale, scale, scale);
        }
        if (_view3d)
        {
            m.UseFovOverride = true;
            m.FovOverride = ViewFov;
            m.UseZClipScale = true;
            m.ZClipScale = ClipScale;
        }
        return m;
    }

    /// <summary>Retícula de plano técnico para la pantalla de la consola: se lee como una interfaz, no como una lámpara.</summary>
    private static Texture2D ScreenGrid()
    {
        const int size = 64;
        var image = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var major = x % 32 <= 1 || y % 32 <= 1;
            var minor = x % 8 == 0 || y % 8 == 0;
            var v = major ? 1f : minor ? 0.42f : 0.14f;
            image.SetPixel(x, y, new Color(v, v, v));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    private static Texture2D WeaveNormal() => NormalFrom((x, y) =>
    {
        var u = x / 32f * Mathf.Tau * 4;
        var v = y / 32f * Mathf.Tau * 4;
        return Mathf.Sin(u) * Mathf.Sin(v) * ((x / 4 + y / 4) % 2 == 0 ? 1f : -1f);
    });

    private static Texture2D GrainNormal()
    {
        var rng = new RandomNumberGenerator { Seed = 77 };
        var h = new float[32 * 32];
        for (var i = 0; i < h.Length; i++) h[i] = rng.Randf();
        return NormalFrom((x, y) => h[(y & 31) * 32 + (x & 31)] * 0.7f);
    }

    private static Texture2D NormalFrom(Func<int, int, float> height)
    {
        const int size = 32;
        var image = Image.CreateEmpty(size, size, true, Image.Format.Rgba8);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dx = height((x + 1) % size, y) - height((x + size - 1) % size, y);
            var dy = height(x, (y + 1) % size) - height(x, (y + size - 1) % size);
            var n = new Vector3(-dx, -dy, 2f).Normalized();
            image.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f));
        }
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }
}

/// <summary>Volúmenes redondeados por secciones. Un solo primitivo cubre biseles, dedos, mangas y carcasas, y agrupa por material.</summary>
internal sealed class MeshKit
{
    public readonly record struct Ring(Vector3 Center, float Rx, float Ry, float Exp = 2f);

    private readonly List<(Material material, SurfaceTool tool)> _surfaces = new();
    private uint _group;
    public Transform3D Xf = Transform3D.Identity;

    private SurfaceTool Tool(Material m)
    {
        foreach (var (material, tool) in _surfaces)
            if (material == m) return tool;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(m);
        _surfaces.Add((m, st));
        return st;
    }

    private void Tri(SurfaceTool st, Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc)
    {
        var flip = Xf.Basis.Determinant() < 0;
        st.SetUV(ua);
        st.AddVertex(Xf * a);
        st.SetUV(flip ? uc : ub);
        st.AddVertex(Xf * (flip ? c : b));
        st.SetUV(flip ? ub : uc);
        st.AddVertex(Xf * (flip ? b : c));
    }

    /// <summary>Superficie continua por una serie de secciones de superelipse.</summary>
    public void Loft(Material m, IReadOnlyList<Ring> rings, int sides = 16, Vector3? up = null)
    {
        if (rings.Count < 2) return;
        var st = Tool(m);
        st.SetSmoothGroup(++_group);
        var points = new Vector3[rings.Count][];
        var normal = Vector3.Zero;
        for (var i = 0; i < rings.Count; i++)
        {
            var prev = rings[Math.Max(i - 1, 0)].Center;
            var next = rings[Math.Min(i + 1, rings.Count - 1)].Center;
            var t = (next - prev).Normalized();
            if (i == 0)
            {
                var hint = up ?? (Mathf.Abs(t.Y) > 0.9f ? Vector3.Right : Vector3.Up);
                normal = (hint - t * hint.Dot(t)).Normalized();
            }
            else
            {
                normal = (normal - t * normal.Dot(t)).Normalized();
            }
            var right = normal.Cross(t);
            var ring = rings[i];
            points[i] = new Vector3[sides];
            for (var k = 0; k < sides; k++)
            {
                var a = k / (float)sides * Mathf.Tau;
                var e = 2f / ring.Exp;
                var cx = Mathf.Cos(a);
                var cy = Mathf.Sin(a);
                var x = Mathf.Sign(cx) * Mathf.Pow(Mathf.Abs(cx), e) * ring.Rx;
                var y = Mathf.Sign(cy) * Mathf.Pow(Mathf.Abs(cy), e) * ring.Ry;
                points[i][k] = ring.Center + right * x + normal * y;
            }
        }
        for (var i = 0; i < rings.Count - 1; i++)
        for (var k = 0; k < sides; k++)
        {
            var k1 = (k + 1) % sides;
            var uA = new Vector2(k / (float)sides, i);
            var uB = new Vector2((k + 1) / (float)sides, i);
            var uC = new Vector2(k / (float)sides, i + 1);
            var uD = new Vector2((k + 1) / (float)sides, i + 1);
            Tri(st, points[i][k], uA, points[i + 1][k], uC, points[i + 1][k1], uD);
            Tri(st, points[i][k], uA, points[i + 1][k1], uD, points[i][k1], uB);
        }
        var first = rings[0].Center;
        var last = rings[^1].Center;
        for (var k = 0; k < sides; k++)
        {
            var k1 = (k + 1) % sides;
            Tri(st, first, Vector2.Zero, points[0][k], Vector2.Zero, points[0][k1], Vector2.Zero);
            Tri(st, last, Vector2.One, points[^1][k1], Vector2.One, points[^1][k], Vector2.One);
        }
    }

    /// <summary>Caja con aristas redondeadas, orientada con la base indicada.</summary>
    public void RBox(Material m, Vector3 center, Vector3 size, float bevel, Basis? basis = null, float exp = 5f)
    {
        var b = basis ?? Basis.Identity;
        var z = b * Vector3.Back;
        var half = size / 2;
        var rings = new List<Ring>();
        void Add(float offset, float inset) => rings.Add(new Ring(center + z * offset, half.X - inset, half.Y - inset, exp));
        const int steps = 3;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps * Mathf.Pi / 2;
            Add(-half.Z + bevel * (1 - Mathf.Cos(t)), bevel * (1 - Mathf.Sin(t)));
        }
        for (var i = steps; i >= 0; i--)
        {
            var t = i / (float)steps * Mathf.Pi / 2;
            Add(half.Z - bevel * (1 - Mathf.Cos(t)), bevel * (1 - Mathf.Sin(t)));
        }
        Loft(m, rings, 24, b * Vector3.Up);
    }

    /// <summary>Cilindro con bisel entre dos puntos.</summary>
    public void Cyl(Material m, Vector3 a, Vector3 b, float r, float bevel = 0.002f, int sides = 18, float rEnd = -1f)
    {
        var end = rEnd < 0 ? r : rEnd;
        var d = (b - a).Normalized();
        Loft(m, new[]
        {
            new Ring(a, Math.Max(r - bevel, 0.0005f), Math.Max(r - bevel, 0.0005f)),
            new Ring(a + d * bevel * 0.3f, r - bevel * 0.3f, r - bevel * 0.3f),
            new Ring(a + d * bevel, r, r),
            new Ring(b - d * bevel, end, end),
            new Ring(b - d * bevel * 0.3f, end - bevel * 0.3f, end - bevel * 0.3f),
            new Ring(b, Math.Max(end - bevel, 0.0005f), Math.Max(end - bevel, 0.0005f)),
        }, sides);
    }

    /// <summary>Tubo redondeado por una curva Catmull-Rom con radio variable.</summary>
    public void Tube(Material m, IReadOnlyList<Vector3> path, IReadOnlyList<float> radius, int sides = 12, int sub = 4, float flatten = 1f, Vector3? up = null)
    {
        var rings = new List<Ring>();
        var tip0 = (path[0] - path[1]).Normalized();
        var r0 = radius[0];
        foreach (var k in new[] { 0.98f, 0.82f, 0.5f, 0.1f })
        {
            var s = Mathf.Sqrt(1 - k * k);
            rings.Add(new Ring(path[0] + tip0 * r0 * k, r0 * s, r0 * s * flatten));
        }
        for (var i = 0; i < path.Count - 1; i++)
        {
            var p0 = path[Math.Max(i - 1, 0)];
            var p1 = path[i];
            var p2 = path[i + 1];
            var p3 = path[Math.Min(i + 2, path.Count - 1)];
            for (var j = 0; j < sub; j++)
            {
                var t = j / (float)sub;
                var r = Mathf.Lerp(radius[i], radius[i + 1], t);
                rings.Add(new Ring(p1.CubicInterpolate(p2, p0, p3, t), r, r * flatten));
            }
        }
        var endR = radius[^1];
        var tip1 = (path[^1] - path[^2]).Normalized();
        foreach (var k in new[] { 0f, 0.5f, 0.82f, 0.98f })
        {
            var s = Mathf.Sqrt(1 - k * k);
            rings.Add(new Ring(path[^1] + tip1 * endR * k, endR * s, endR * s * flatten));
        }
        Loft(m, rings, sides, up);
    }

    /// <summary>Perfil plano extruido a lo largo de Z, con normales de cara dura en las tapas.</summary>
    public void Extrude(Material m, IReadOnlyList<Vector2> outline, float z0, float z1)
    {
        var st = Tool(m);
        st.SetSmoothGroup(uint.MaxValue);
        var center0 = new Vector3(0, 0, z0);
        var center1 = new Vector3(0, 0, z1);
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            var a0 = new Vector3(a.X, a.Y, z0);
            var b0 = new Vector3(b.X, b.Y, z0);
            var a1 = new Vector3(a.X, a.Y, z1);
            var b1 = new Vector3(b.X, b.Y, z1);
            Tri(st, a0, Vector2.Zero, a1, Vector2.Zero, b1, Vector2.One);
            Tri(st, a0, Vector2.Zero, b1, Vector2.One, b0, Vector2.One);
            Tri(st, center0, Vector2.Zero, a0, Vector2.Zero, b0, Vector2.One);
            Tri(st, center1, Vector2.Zero, b1, Vector2.One, a1, Vector2.Zero);
        }
    }

    public ArrayMesh Commit()
    {
        var mesh = new ArrayMesh();
        foreach (var (_, st) in _surfaces)
        {
            st.GenerateNormals();
            st.GenerateTangents();
            st.Commit(mesh);
        }
        return mesh;
    }
}

/// <summary>Geometría original de la multiherramienta de recuperación, la consola de obra, los brazos y los guantes.</summary>
internal static class RigModels
{
    // Espacio de la mano: X a lo largo de los nudillos (índice en -X), +Y el dorso, -Z hacia donde apuntan los dedos extendidos.
    private static readonly float[] FingerX = { -0.031f, -0.0105f, 0.0105f, 0.03f };
    private static readonly float[] FingerSize = { 1f, 1.07f, 1f, 0.82f };
    private static readonly float[] FingerR = { 0.0108f, 0.0112f, 0.0106f, 0.0094f };

    public sealed record HandPose(float[][] Curl, Vector3[] Thumb);

    public static readonly Vector3 GripCenter = new(0, -0.031f, -0.044f);

    public static readonly HandPose Grip = new(
        new[] { new[] { 42f, 112f, 178f }, new[] { 50f, 122f, 190f }, new[] { 54f, 128f, 195f }, new[] { 58f, 132f, 198f } },
        new[] { new Vector3(-0.036f, -0.012f, 0.022f), new Vector3(-0.056f, -0.03f, -0.004f), new Vector3(-0.05f, -0.052f, -0.03f), new Vector3(-0.03f, -0.066f, -0.05f) });

    public static readonly HandPose Point = new(
        new[] { new[] { 4f, 14f, 24f }, new[] { 62f, 150f, 225f }, new[] { 66f, 156f, 230f }, new[] { 70f, 160f, 235f } },
        new[] { new Vector3(-0.036f, -0.012f, 0.022f), new Vector3(-0.05f, -0.03f, -0.006f), new Vector3(-0.038f, -0.044f, -0.03f), new Vector3(-0.018f, -0.046f, -0.042f) });

    public static readonly HandPose Cradle = new(
        new[] { new[] { 24f, 62f, 96f }, new[] { 26f, 66f, 102f }, new[] { 28f, 68f, 106f }, new[] { 30f, 72f, 110f } },
        new[] { new Vector3(-0.036f, -0.008f, 0.022f), new Vector3(-0.06f, -0.004f, 0.004f), new Vector3(-0.072f, -0.002f, -0.018f), new Vector3(-0.078f, -0.006f, -0.04f) });

    public static readonly HandPose Open = new(
        new[] { new[] { 10f, 26f, 40f }, new[] { 12f, 30f, 44f }, new[] { 16f, 34f, 48f }, new[] { 20f, 38f, 52f } },
        new[] { new Vector3(-0.036f, -0.008f, 0.022f), new Vector3(-0.062f, -0.016f, 0.002f), new Vector3(-0.078f, -0.024f, -0.018f), new Vector3(-0.088f, -0.03f, -0.036f) });

    public static void Hand(MeshKit k, RigMaterials mt, HandPose pose)
    {
        // Palma: un volumen que se ensancha de la muñeca a los nudillos.
        k.Loft(mt.Rubber, new[]
        {
            new MeshKit.Ring(new Vector3(0, 0, 0.056f), 0.024f, 0.017f, 2.4f),
            new MeshKit.Ring(new Vector3(0, -0.001f, 0.036f), 0.03f, 0.019f, 2.6f),
            new MeshKit.Ring(new Vector3(0, -0.002f, 0.012f), 0.038f, 0.0215f, 2.8f),
            new MeshKit.Ring(new Vector3(0, -0.003f, -0.016f), 0.0425f, 0.0215f, 3f),
            new MeshKit.Ring(new Vector3(0, -0.003f, -0.036f), 0.0445f, 0.0175f, 3f),
            new MeshKit.Ring(new Vector3(0, -0.002f, -0.05f), 0.042f, 0.0125f, 2.6f),
            new MeshKit.Ring(new Vector3(0, -0.001f, -0.056f), 0.034f, 0.006f, 2.2f),
        }, 22);
        // Base del pulgar y almohadilla exterior de la palma.
        k.Tube(mt.Rubber, new[] { new Vector3(-0.024f, -0.012f, 0.036f), new Vector3(-0.034f, -0.018f, 0.012f), new Vector3(-0.034f, -0.018f, -0.012f) }, new[] { 0.015f, 0.018f, 0.014f }, 12, 3);
        k.Tube(mt.Rubber, new[] { new Vector3(0.028f, -0.012f, 0.032f), new Vector3(0.036f, -0.014f, 0.004f), new Vector3(0.036f, -0.012f, -0.02f) }, new[] { 0.012f, 0.014f, 0.011f }, 10, 3);
        // Refuerzo pintado sobre el dorso y tira de nudillos: el guante deja de parecer una pieza lisa.
        k.RBox(mt.PaintDark, new Vector3(0, 0.0165f, 0.006f), new Vector3(0.058f, 0.008f, 0.062f), 0.0035f, Basis.FromEuler(new Vector3(-0.06f, 0, 0)));
        k.RBox(mt.Paint, new Vector3(-0.012f, 0.0212f, 0.012f), new Vector3(0.01f, 0.0022f, 0.032f), 0.001f, Basis.FromEuler(new Vector3(-0.06f, 0, 0)));
        k.RBox(mt.ClothDark, new Vector3(0, 0.0125f, -0.038f), new Vector3(0.082f, 0.0085f, 0.016f), 0.0035f);

        for (var f = 0; f < 4; f++) Finger(k, mt, f, pose.Curl[f]);

        var thumbR = new[] { 0.0142f, 0.0132f, 0.0122f, 0.0105f };
        k.Tube(mt.Rubber, pose.Thumb, thumbR, 12, 4);
        var tipDir = (pose.Thumb[3] - pose.Thumb[2]).Normalized();
        k.Tube(mt.RubberGrip, new[] { pose.Thumb[3] - tipDir * 0.012f, pose.Thumb[3] + tipDir * 0.002f }, new[] { 0.0112f, 0.0112f }, 10, 2);

        // Puño del guante con correa de ajuste.
        k.Loft(mt.ClothDark, new[]
        {
            new MeshKit.Ring(new Vector3(0, 0, 0.05f), 0.0245f, 0.0185f, 2.3f),
            new MeshKit.Ring(new Vector3(0, 0, 0.058f), 0.0285f, 0.0215f, 2.3f),
            new MeshKit.Ring(new Vector3(0, 0, 0.082f), 0.0315f, 0.0255f, 2.2f),
            new MeshKit.Ring(new Vector3(0, 0, 0.09f), 0.0335f, 0.0275f, 2.2f),
            new MeshKit.Ring(new Vector3(0, 0, 0.093f), 0.031f, 0.025f, 2.2f),
        }, 18);
        k.Loft(mt.Rubber, new[]
        {
            new MeshKit.Ring(new Vector3(0, 0, 0.064f), 0.0298f, 0.0226f, 2.3f),
            new MeshKit.Ring(new Vector3(0, 0, 0.065f), 0.031f, 0.0238f, 2.3f),
            new MeshKit.Ring(new Vector3(0, 0, 0.074f), 0.0322f, 0.025f, 2.3f),
            new MeshKit.Ring(new Vector3(0, 0, 0.075f), 0.031f, 0.0238f, 2.3f),
        }, 18);
        k.RBox(mt.Steel, new Vector3(-0.004f, 0.0258f, 0.0695f), new Vector3(0.016f, 0.0035f, 0.012f), 0.001f);
    }

    private static void Finger(MeshKit k, RigMaterials mt, int f, float[] curl)
    {
        var size = FingerSize[f];
        var r = FingerR[f];
        var lengths = new[] { 0.04f * size, 0.027f * size, 0.022f * size };
        var at = new Vector3(FingerX[f], f == 3 ? -0.005f : -0.003f, f == 1 ? -0.05f : f == 3 ? -0.044f : -0.048f);
        var path = new List<Vector3> { at + new Vector3(0, 0, 0.008f), at };
        var radius = new List<float> { r * 1.02f, r * 1.05f };
        Vector3 Dir(float deg)
        {
            var a = Mathf.DegToRad(deg);
            return new Vector3(0, -Mathf.Sin(a), -Mathf.Cos(a));
        }
        var outward = new List<(Vector3 at, Vector3 dir, Vector3 outside, float len)>();
        for (var j = 0; j < 3; j++)
        {
            var dir = Dir(curl[j]);
            var mid = at + dir * lengths[j] * 0.5f;
            var end = at + dir * lengths[j];
            path.Add(mid);
            radius.Add(r * (j == 2 ? 0.92f : 0.97f));
            path.Add(end);
            radius.Add(r * (j == 2 ? 0.9f : 1.04f));
            outward.Add((mid, dir, Dir(curl[j] - 90f), lengths[j]));
            at = end;
        }
        k.Tube(mt.Rubber, path, radius, 10, 2);
        // Refuerzo en la falange exterior y yema con goma de agarre.
        var first = outward[0];
        k.RBox(mt.ClothDark, first.at + first.outside * r * 0.84f, new Vector3(r * 1.5f, r * 0.42f, first.len * 0.72f), r * 0.2f, BasisFor(first.dir, first.outside));
        var second = outward[1];
        k.RBox(mt.ClothDark, second.at + second.outside * r * 0.78f, new Vector3(r * 1.36f, r * 0.36f, second.len * 0.62f), r * 0.18f, BasisFor(second.dir, second.outside));
        var tip = outward[2];
        k.Tube(mt.RubberGrip, new[] { tip.at - tip.dir * tip.len * 0.22f, tip.at + tip.dir * tip.len * 0.42f }, new[] { r * 0.95f, r * 0.9f }, 9, 2);
    }

    private static Basis BasisFor(Vector3 forward, Vector3 up)
    {
        var z = -forward.Normalized();
        var x = up.Cross(z).Normalized();
        return new Basis(x, z.Cross(x), z);
    }

    /// <summary>Base con las columnas X e Y dadas y Z calculada. Sirve para orientar una mano sobre un eje de agarre.</summary>
    private static Basis Frame(Vector3 x, Vector3 yHint)
    {
        var nx = x.Normalized();
        var z = nx.Cross(yHint).Normalized();
        return new Basis(nx, z.Cross(nx), z);
    }

    /// <summary>Manga de traje desde el puño hasta el codo: arrugas por variación de radio y banda reflectante.</summary>
    private static void Sleeve(MeshKit k, RigMaterials mt, Vector3 wrist, Vector3 wristDir, Vector3 elbow)
    {
        k.Xf = Transform3D.Identity;
        var bend = wrist + wristDir * 0.14f;
        Vector3 At(float t) => wrist.CubicInterpolate(elbow, wrist - wristDir * 0.2f, elbow + (elbow - bend).Normalized() * 0.2f, t);
        var rings = new List<MeshKit.Ring>();
        var up = (wristDir.Cross(Vector3.Right).Cross(wristDir)).Normalized();
        rings.Add(new MeshKit.Ring(At(0f), 0.024f, 0.022f));
        rings.Add(new MeshKit.Ring(At(0.004f), 0.0345f, 0.0305f));
        const int steps = 26;
        for (var i = 1; i <= steps; i++)
        {
            var t = i / (float)steps;
            var r = Mathf.Lerp(0.0355f, 0.066f, Mathf.Pow(t, 0.8f));
            // Pliegues más marcados cerca de la muñeca, donde se acumula la tela.
            var fold = Mathf.Sin(t * 42f + 0.6f) * 0.0024f * (1.15f - t) + Mathf.Sin(t * 17f) * 0.0018f;
            rings.Add(new MeshKit.Ring(At(t), r + fold, (r + fold) * 0.9f));
        }
        k.Loft(mt.Cloth, rings, 18, up);

        // Banda reflectante y costura: rompen la manga y dan escala.
        void Band(Material m, float t0, float t1, float extra)
        {
            var band = new List<MeshKit.Ring>();
            foreach (var (t, grow) in new[] { (t0, 0f), (t0 + 0.002f, extra), (t1 - 0.002f, extra), (t1, 0f) })
            {
                var r = Mathf.Lerp(0.0355f, 0.066f, Mathf.Pow(t, 0.8f)) + grow;
                band.Add(new MeshKit.Ring(At(t), r, r * 0.9f));
            }
            k.Loft(m, band, 18, up);
        }
        Band(mt.Band, 0.27f, 0.34f, 0.0036f);
        Band(mt.ClothDark, 0.09f, 0.12f, 0.0032f);
    }

    /// <summary>Mano derecha en la empuñadura y su brazo, en el espacio de la herramienta.</summary>
    public static ArrayMesh RightArmOnGrip(RigMaterials mt)
    {
        var k = new MeshKit();
        var basis = Frame(new Vector3(0, -0.97f, -0.24f), new Vector3(1f, 0.22f, 0f));
        var origin = new Vector3(0, -0.005f, 0.006f) - basis * GripCenter;
        k.Xf = new Transform3D(basis, origin);
        Hand(k, mt, Grip);
        var wrist = k.Xf * new Vector3(0, 0, 0.088f);
        Sleeve(k, mt, wrist, (basis * Vector3.Back).Normalized(), new Vector3(0.115f, -0.335f, 0.475f));
        return k.Commit();
    }

    /// <summary>Mano izquierda en el asa lateral y su brazo, en el espacio de la herramienta.</summary>
    public static ArrayMesh LeftArmOnHandle(RigMaterials mt)
    {
        var k = new MeshKit();
        var mirror = new Basis(new Vector3(-1, 0, 0), Vector3.Up, Vector3.Back);
        var near = new Vector3(-0.078f, 0.06f, -0.15f);
        var far = new Vector3(-0.182f, 0.022f, -0.128f);
        var center = (near + far) / 2;
        var basis = Frame((near - far), new Vector3(0, 0.62f, 0.78f));
        var grip = new Vector3(-GripCenter.X, GripCenter.Y, GripCenter.Z);
        k.Xf = new Transform3D(basis * mirror, center - basis * grip);
        Hand(k, mt, Grip);
        var wrist = k.Xf * new Vector3(0, 0, 0.088f);
        Sleeve(k, mt, wrist, (basis * Vector3.Back).Normalized(), new Vector3(-0.6f, -0.385f, 0.25f));
        return k.Commit();
    }

    /// <summary>Mano izquierda abierta para recoger, en un espacio propio: el gesto la mueve entera.</summary>
    public static ArrayMesh LeftArmReaching(RigMaterials mt)
    {
        var k = new MeshKit();
        var mirror = new Basis(new Vector3(-1, 0, 0), Vector3.Up, Vector3.Back);
        var basis = Frame(new Vector3(0.93f, 0.2f, -0.3f), new Vector3(-0.2f, 0.8f, 0.55f));
        k.Xf = new Transform3D(basis * mirror, Vector3.Zero);
        Hand(k, mt, Open);
        var wrist = k.Xf * new Vector3(0, 0, 0.088f);
        Sleeve(k, mt, wrist, (basis * Vector3.Back).Normalized(), new Vector3(-0.22f, -0.42f, 0.42f));
        return k.Commit();
    }

    /// <summary>Consola de obra sujeta por la mano izquierda, que la sostiene por debajo con el pulgar sobre el borde.</summary>
    public static ArrayMesh LeftArmOnConsole(RigMaterials mt)
    {
        var k = new MeshKit();
        var mirror = new Basis(new Vector3(-1, 0, 0), Vector3.Up, Vector3.Back);
        var basis = Frame(new Vector3(0.15f, 0.05f, -0.98f), new Vector3(-0.32f, -0.94f, 0.05f));
        k.Xf = new Transform3D(basis * mirror, new Vector3(-0.054f, -0.052f, 0.012f));
        Hand(k, mt, Cradle);
        var wrist = k.Xf * new Vector3(0, 0, 0.088f);
        Sleeve(k, mt, wrist, (basis * Vector3.Back).Normalized(), new Vector3(-0.34f, 0.05f, 0.62f));
        return k.Commit();
    }

    /// <summary>Mano derecha que señala sobre la pantalla, en el espacio de la consola.</summary>
    public static ArrayMesh RightArmPointing(RigMaterials mt)
    {
        var k = new MeshKit();
        var basis = Frame(new Vector3(0.35f, 0.42f, -0.73f), new Vector3(-0.3f, 0.85f, 0.35f));
        k.Xf = new Transform3D(basis, new Vector3(0.125f, 0.062f, 0.055f));
        Hand(k, mt, Point);
        var wrist = k.Xf * new Vector3(0, 0, 0.088f);
        Sleeve(k, mt, wrist, (basis * Vector3.Back).Normalized(), new Vector3(0.46f, 0.117f, 0.54f));
        return k.Commit();
    }

    /// <summary>Cuerpo de la multiherramienta: empuñadura de goma, carcasa pintada, motor y asa lateral. El origen es la empuñadura y apunta a -Z.</summary>
    public static ArrayMesh ToolBody(RigMaterials mt)
    {
        var k = new MeshKit();
        // Empuñadura con surcos para los dedos, inclinada hacia atrás.
        k.Loft(mt.RubberGrip, new[]
        {
            new MeshKit.Ring(new Vector3(0, 0.05f, -0.018f), 0.021f, 0.03f, 2.8f),
            new MeshKit.Ring(new Vector3(0, 0.03f, -0.01f), 0.0215f, 0.0295f, 2.8f),
            new MeshKit.Ring(new Vector3(0, 0.016f, -0.006f), 0.0195f, 0.027f, 2.8f),
            new MeshKit.Ring(new Vector3(0, 0.004f, -0.002f), 0.0215f, 0.0295f, 2.8f),
            new MeshKit.Ring(new Vector3(0, -0.012f, 0.002f), 0.0195f, 0.027f, 2.8f),
            new MeshKit.Ring(new Vector3(0, -0.026f, 0.006f), 0.0215f, 0.0295f, 2.8f),
            new MeshKit.Ring(new Vector3(0, -0.042f, 0.011f), 0.0195f, 0.027f, 2.8f),
            new MeshKit.Ring(new Vector3(0, -0.058f, 0.015f), 0.0225f, 0.031f, 2.8f),
            new MeshKit.Ring(new Vector3(0, -0.07f, 0.019f), 0.0255f, 0.034f, 3f),
            new MeshKit.Ring(new Vector3(0, -0.078f, 0.021f), 0.02f, 0.028f, 3f),
        }, 18, Vector3.Forward);
        // Carcasa principal.
        k.Loft(mt.Paint, new[]
        {
            new MeshKit.Ring(new Vector3(0, 0.086f, 0.082f), 0.018f, 0.024f, 3f),
            new MeshKit.Ring(new Vector3(0, 0.086f, 0.076f), 0.03f, 0.038f, 3.2f),
            new MeshKit.Ring(new Vector3(0, 0.086f, 0.05f), 0.0355f, 0.0445f, 3.4f),
            new MeshKit.Ring(new Vector3(0, 0.087f, 0.0f), 0.038f, 0.047f, 3.4f),
            new MeshKit.Ring(new Vector3(0, 0.088f, -0.07f), 0.0385f, 0.0465f, 3.4f),
            new MeshKit.Ring(new Vector3(0, 0.09f, -0.135f), 0.0365f, 0.042f, 3.2f),
            new MeshKit.Ring(new Vector3(0, 0.09f, -0.168f), 0.033f, 0.037f, 3f),
            new MeshKit.Ring(new Vector3(0, 0.09f, -0.174f), 0.025f, 0.029f, 2.6f),
        }, 24, Vector3.Up);
        // Tapa superior oscura con carril y atornillado.
        k.RBox(mt.PaintDark, new Vector3(0, 0.1345f, -0.02f), new Vector3(0.05f, 0.0065f, 0.13f), 0.0028f);
        k.RBox(mt.Steel, new Vector3(0, 0.1395f, -0.02f), new Vector3(0.016f, 0.004f, 0.112f), 0.0012f);
        foreach (var (x, z) in new[] { (-0.019f, 0.036f), (0.019f, 0.036f), (-0.019f, -0.076f), (0.019f, -0.076f) })
            k.Cyl(mt.Steel, new Vector3(x, 0.1372f, z), new Vector3(x, 0.14f, z), 0.0034f, 0.001f, 8);
        k.RBox(mt.Led, new Vector3(0.0145f, 0.1385f, 0.022f), new Vector3(0.006f, 0.002f, 0.012f), 0.0008f);
        // Paneles laterales y rendijas de ventilación.
        foreach (var side in new[] { -1f, 1f })
        {
            k.RBox(mt.PaintDark, new Vector3(side * 0.0365f, 0.09f, -0.03f), new Vector3(0.0042f, 0.058f, 0.095f), 0.0018f);
            for (var i = 0; i < 5; i++)
                k.RBox(mt.DarkSteel, new Vector3(side * 0.0392f, 0.09f, -0.066f + i * 0.018f), new Vector3(0.002f, 0.036f, 0.0065f), 0.0008f);
        }
        // Batería trasera con contactos.
        k.RBox(mt.PaintDark, new Vector3(0, 0.082f, 0.112f), new Vector3(0.066f, 0.076f, 0.05f), 0.008f);
        k.RBox(mt.Steel, new Vector3(0, 0.082f, 0.0855f), new Vector3(0.056f, 0.066f, 0.005f), 0.0015f);
        k.RBox(mt.Band, new Vector3(0, 0.1045f, 0.1385f), new Vector3(0.04f, 0.009f, 0.002f), 0.0007f);
        // Gatillo y guarda.
        k.Tube(mt.DarkSteel, new[] { new Vector3(0, 0.04f, -0.042f), new Vector3(0, 0.022f, -0.05f), new Vector3(0, 0.006f, -0.044f) }, new[] { 0.0052f, 0.0048f, 0.0042f }, 8, 3, 1f, Vector3.Right);
        k.Tube(mt.Steel, new[] { new Vector3(0, 0.046f, -0.066f), new Vector3(0, 0.01f, -0.072f), new Vector3(0, -0.006f, -0.04f), new Vector3(0, -0.002f, -0.024f) }, new[] { 0.0032f, 0.0032f, 0.0032f, 0.0032f }, 8, 3);
        // Aletas del motor y cuello del cabezal.
        for (var i = 0; i < 5; i++)
        {
            var z = -0.176f - i * 0.0085f;
            k.Cyl(mt.DarkSteel, new Vector3(0, 0.09f, z), new Vector3(0, 0.09f, z - 0.0052f), 0.0335f - i * 0.0013f, 0.0011f, 22);
            k.Cyl(mt.Steel, new Vector3(0, 0.09f, z - 0.0052f), new Vector3(0, 0.09f, z - 0.0085f), 0.0255f, 0.0005f, 16);
        }
        k.Cyl(mt.Steel, new Vector3(0, 0.09f, -0.218f), new Vector3(0, 0.09f, -0.232f), 0.029f, 0.003f, 22);
        k.Cyl(mt.DarkSteel, new Vector3(0, 0.09f, -0.232f), new Vector3(0, 0.09f, -0.262f), 0.0225f, 0.002f, 18, 0.019f);
        // Manguera de refrigeración que une batería y cuello.
        k.Tube(mt.Rubber, new[]
        {
            new Vector3(0.024f, 0.122f, 0.104f), new Vector3(0.044f, 0.142f, 0.05f), new Vector3(0.05f, 0.134f, -0.06f),
            new Vector3(0.046f, 0.122f, -0.16f), new Vector3(0.028f, 0.106f, -0.236f),
        }, new[] { 0.0064f, 0.0064f, 0.0064f, 0.0064f, 0.0064f }, 8, 5);
        foreach (var at in new[] { new Vector3(0.024f, 0.122f, 0.104f), new Vector3(0.028f, 0.106f, -0.236f) })
            k.Cyl(mt.Steel, at + new Vector3(0, -0.006f, 0), at + new Vector3(0, 0.008f, 0), 0.0085f, 0.0015f, 10);
        // Asa lateral para la mano izquierda: abrazadera, vástago y empuñadura de goma.
        k.Cyl(mt.DarkSteel, new Vector3(0, 0.09f, -0.135f), new Vector3(0, 0.09f, -0.158f), 0.0405f, 0.002f, 24);
        k.Cyl(mt.Steel, new Vector3(-0.036f, 0.081f, -0.147f), new Vector3(-0.08f, 0.061f, -0.15f), 0.0095f, 0.0015f, 12);
        var near = new Vector3(-0.078f, 0.06f, -0.15f);
        var far = new Vector3(-0.182f, 0.022f, -0.128f);
        var d = (far - near).Normalized();
        var handle = new List<MeshKit.Ring>
        {
            new(near - d * 0.002f, 0.015f, 0.015f),
            new(near, 0.0205f, 0.0205f),
            new(near + d * 0.006f, 0.0205f, 0.0205f),
        };
        for (var i = 1; i <= 8; i++)
        {
            var t = i / 9f;
            var r = 0.0185f + Mathf.Sin(t * Mathf.Pi * 8f) * 0.0008f;
            handle.Add(new MeshKit.Ring(near + (far - near) * t, r, r));
        }
        handle.Add(new MeshKit.Ring(far - d * 0.004f, 0.0225f, 0.0225f));
        handle.Add(new MeshKit.Ring(far, 0.0235f, 0.0235f));
        handle.Add(new MeshKit.Ring(far + d * 0.004f, 0.018f, 0.018f));
        k.Loft(mt.RubberGrip, handle, 16);
        return k.Commit();
    }

    /// <summary>Tramo telescópico de longitud unidad a lo largo de -Z: se escala para extender el cabezal.</summary>
    public static ArrayMesh BoomStage(Material m, float r)
    {
        var k = new MeshKit();
        k.Loft(m, new[]
        {
            new MeshKit.Ring(Vector3.Zero, r, r),
            new MeshKit.Ring(new Vector3(0, 0, -1), r, r),
        }, 14);
        return k.Commit();
    }

    /// <summary>Caja reductora y protector del cabezal: no giran. El origen es el extremo del cuello.</summary>
    public static ArrayMesh HeadHousing(RigMaterials mt)
    {
        var k = new MeshKit();
        k.Loft(mt.DarkSteel, new[]
        {
            new MeshKit.Ring(new Vector3(0, 0, 0.002f), 0.012f, 0.012f),
            new MeshKit.Ring(new Vector3(0, 0, 0f), 0.0255f, 0.0255f),
            new MeshKit.Ring(new Vector3(0, 0, -0.012f), 0.03f, 0.03f),
            new MeshKit.Ring(new Vector3(0, 0, -0.038f), 0.037f, 0.037f),
            new MeshKit.Ring(new Vector3(0, 0, -0.05f), 0.034f, 0.034f),
            new MeshKit.Ring(new Vector3(0, 0, -0.053f), 0.02f, 0.02f),
        }, 22);
        k.Cyl(mt.Steel, new Vector3(0, 0, -0.018f), new Vector3(0, 0, -0.026f), 0.0335f, 0.0015f, 24);
        for (var i = 0; i < 6; i++)
        {
            var a = i / 6f * Mathf.Tau;
            var p = new Vector3(Mathf.Cos(a) * 0.029f, Mathf.Sin(a) * 0.029f, -0.0265f);
            k.Cyl(mt.Gold, p, p + new Vector3(0, 0, -0.003f), 0.0028f, 0.0008f, 6);
        }
        // Protector pintado: media corona sobre el disco, abierta hacia el objetivo.
        const int steps = 22;
        var arc = new List<MeshKit.Ring>();
        for (var i = 0; i <= steps; i++)
        {
            var a = Mathf.DegToRad(-20f + 220f * i / steps);
            var edge = i == 0 || i == steps ? 0.35f : i == 1 || i == steps - 1 ? 0.8f : 1f;
            arc.Add(new MeshKit.Ring(new Vector3(Mathf.Cos(a) * 0.111f, Mathf.Sin(a) * 0.111f, -0.074f), 0.0056f * edge, 0.033f * edge, 3.4f));
        }
        k.Loft(mt.Paint, arc, 12, Vector3.Forward);
        // Brazos que unen el protector a la caja.
        foreach (var deg in new[] { 20f, 90f, 160f })
        {
            var a = Mathf.DegToRad(deg);
            k.Tube(mt.PaintDark, new[] { new Vector3(Mathf.Cos(a) * 0.03f, Mathf.Sin(a) * 0.03f, -0.036f), new Vector3(Mathf.Cos(a) * 0.08f, Mathf.Sin(a) * 0.08f, -0.056f), new Vector3(Mathf.Cos(a) * 0.109f, Mathf.Sin(a) * 0.109f, -0.058f) }, new[] { 0.0072f, 0.0062f, 0.0058f }, 8, 3);
        }
        return k.Commit();
    }

    /// <summary>Corona giratoria: disco dentado, garras hacia delante y una marca que deja leer el giro.</summary>
    public static ArrayMesh HeadRotor(RigMaterials mt)
    {
        var k = new MeshKit();
        const int teeth = 18;
        var outline = new List<Vector2>();
        for (var i = 0; i < teeth; i++)
        {
            var a = i / (float)teeth * Mathf.Tau;
            var step = Mathf.Tau / teeth;
            outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.086f);
            outline.Add(new Vector2(Mathf.Cos(a + step * 0.34f), Mathf.Sin(a + step * 0.34f)) * 0.102f);
            outline.Add(new Vector2(Mathf.Cos(a + step * 0.5f), Mathf.Sin(a + step * 0.5f)) * 0.094f);
        }
        k.Extrude(mt.Steel, outline, 0f, -0.0042f);
        // Plato central con ranuras: la cara trasera es la que ve el jugador.
        k.Cyl(mt.DarkSteel, new Vector3(0, 0, 0.0028f), new Vector3(0, 0, -0.0052f), 0.074f, 0.0014f, 32);
        k.Cyl(mt.Steel, new Vector3(0, 0, 0.0044f), new Vector3(0, 0, 0.0f), 0.028f, 0.0012f, 20);
        for (var i = 0; i < 3; i++)
        {
            var a = i / 3f * Mathf.Tau;
            var r = Basis.FromEuler(new Vector3(0, 0, a));
            k.RBox(i == 0 ? mt.Paint : mt.Steel, r * new Vector3(0.052f, 0, 0.0032f), new Vector3(0.036f, 0.012f, 0.0018f), 0.0007f, r);
            // Garra: nace del disco y se curva hacia delante.
            k.Tube(mt.Steel, new[]
            {
                r * new Vector3(0.056f, 0, -0.004f), r * new Vector3(0.064f, 0.004f, -0.02f), r * new Vector3(0.07f, 0.012f, -0.04f), r * new Vector3(0.066f, 0.02f, -0.055f),
            }, new[] { 0.0105f, 0.0085f, 0.0062f, 0.0026f }, 8, 3, 0.7f);
        }
        k.Cyl(mt.Gold, new Vector3(0, 0, 0.0062f), new Vector3(0, 0, 0.0028f), 0.0098f, 0.0009f, 12);
        return k.Commit();
    }

    /// <summary>Consola de obra: proyector de planos de mano. El origen es su centro y la pantalla mira a +Y.</summary>
    public static ArrayMesh Console(RigMaterials mt)
    {
        var k = new MeshKit();
        k.RBox(mt.PaintDark, new Vector3(0, 0, 0), new Vector3(0.152f, 0.03f, 0.104f), 0.008f);
        k.RBox(mt.Paint, new Vector3(0, -0.0065f, 0.0f), new Vector3(0.158f, 0.021f, 0.11f), 0.0065f);
        k.RBox(mt.Rubber, new Vector3(-0.073f, 0.003f, 0.012f), new Vector3(0.02f, 0.033f, 0.084f), 0.008f);
        k.RBox(mt.DarkSteel, new Vector3(0.008f, 0.0156f, -0.002f), new Vector3(0.122f, 0.0022f, 0.086f), 0.001f);
        k.RBox(mt.Screen, new Vector3(0.008f, 0.0172f, -0.002f), new Vector3(0.112f, 0.0014f, 0.076f), 0.0005f);
        // Proyector frontal, antena corta y botones.
        k.Cyl(mt.Steel, new Vector3(0.022f, 0.002f, -0.05f), new Vector3(0.022f, 0.002f, -0.074f), 0.0135f, 0.002f, 16, 0.016f);
        k.Cyl(mt.Screen, new Vector3(0.022f, 0.002f, -0.074f), new Vector3(0.022f, 0.002f, -0.0755f), 0.012f, 0.0004f, 14);
        k.Cyl(mt.DarkSteel, new Vector3(-0.04f, 0.006f, -0.052f), new Vector3(-0.04f, 0.006f, -0.066f), 0.0065f, 0.001f, 10);
        k.Tube(mt.Rubber, new[] { new Vector3(0.066f, 0.008f, -0.044f), new Vector3(0.072f, 0.038f, -0.06f), new Vector3(0.074f, 0.064f, -0.066f) }, new[] { 0.0035f, 0.0028f, 0.0024f }, 8, 3);
        foreach (var x in new[] { -0.012f, 0.012f, 0.036f })
            k.RBox(mt.Steel, new Vector3(x, 0.0172f, 0.0465f), new Vector3(0.016f, 0.0028f, 0.007f), 0.0009f);
        return k.Commit();
    }

    /// <summary>Herramienta y mano para el personaje visto por otros. Solo un brazo: el modelo del cuerpo aporta el resto.</summary>
    public static ArrayMesh RemoteForearm()
    {
        var mt = RigMaterials.World;
        var k = new MeshKit();
        k.Tube(mt.Cloth, new[] { new Vector3(0, 0.02f, 0.34f), new Vector3(0, -0.01f, 0.2f), new Vector3(0, -0.01f, 0.09f) }, new[] { 0.075f, 0.06f, 0.044f }, 12, 3);
        k.Tube(mt.Rubber, new[] { new Vector3(0, -0.01f, 0.08f), new Vector3(0, -0.005f, 0.0f) }, new[] { 0.048f, 0.05f }, 12, 2);
        return k.Commit();
    }
}

/// <summary>
/// Brazos y herramienta de primera persona. Todo es representación local: recibe el estado del jugador y nunca decide nada del juego.
/// La animación es procedimental y por capas: postura, equipar, respiración, paso, inercia, salto, trabajo y gestos.
/// </summary>
public partial class FirstPersonRig : Node3D
{
    public struct Input
    {
        public RigMode Mode;
        public Tool Tool;
        public Vector3 Velocity;
        public bool OnFloor, Sprinting, Working, BobOn;
        public float BobScale, WallDistance;
        public Vector2 Look;
        public Vector3? Target;
    }

    // Posturas de reposo en el espacio de la cámara.
    private static readonly Vector3 ToolPos = new(0.335f, -0.25f, -0.58f);
    private static readonly Vector3 ToolRot = new(0.06f, 0.3f, -0.03f);
    private static readonly Vector3 DevicePos = new(-0.01f, -0.165f, -0.44f);
    private static readonly Vector3 DeviceRot = new(0.86f, 0.045f, 0.06f);
    private static readonly Vector3 ReachPos = new(-0.17f, -0.2f, -0.42f);
    private const float NeckZ = -0.262f, NeckY = 0.09f, MaxBoom = 2.6f;

    private Node3D _sway = null!, _tool = null!, _device = null!, _leftTool = null!, _rightPoint = null!, _reach = null!;
    private Node3D _head = null!, _rotor = null!;
    private MeshInstance3D _stage1 = null!, _stage2 = null!;

    private RigMode _shown = RigMode.Stowed;
    private Tool _shownTool = Game.Tool.Cutter;
    private float _equip, _time, _walkPhase, _walk, _run, _work, _spin, _boom, _boomV;
    private Vector2 _sway2, _swayV;
    private float _air, _land, _landV, _retract, _hit, _hitV, _tap, _gesture = 1f, _toolSwitch, _aspectShift;
    private Vector3 _shake;
    private bool _wasOnFloor = true;
    private float _lastVy;
    private readonly RandomNumberGenerator _rng = new() { Seed = 4242 };
    private Color _screenColor = new(1f, 0.67f, 0.25f);

    public RigMode ShownMode => _shown;
    public float EquipAmount => _equip;

    public override void _Ready()
    {
        var mt = RigMaterials.View;
        _sway = new Node3D();
        AddChild(_sway);

        _tool = new Node3D { Visible = false };
        _sway.AddChild(_tool);
        Part(_tool, RigModels.ToolBody(mt));
        Part(_tool, RigModels.RightArmOnGrip(mt));
        _leftTool = new Node3D();
        _tool.AddChild(_leftTool);
        Part(_leftTool, RigModels.LeftArmOnHandle(mt));
        _stage1 = Part(_tool, RigModels.BoomStage(mt.DarkSteel, 0.0165f));
        _stage2 = Part(_tool, RigModels.BoomStage(mt.Steel, 0.0115f));
        _stage1.Position = new Vector3(0, NeckY, NeckZ);
        _head = new Node3D { Position = new Vector3(0, NeckY, NeckZ) };
        _tool.AddChild(_head);
        Part(_head, RigModels.HeadHousing(mt));
        _rotor = new Node3D { Position = new Vector3(0, 0, -0.058f) };
        _head.AddChild(_rotor);
        Part(_rotor, RigModels.HeadRotor(mt));

        _device = new Node3D { Visible = false };
        _sway.AddChild(_device);
        Part(_device, RigModels.Console(mt));
        Part(_device, RigModels.LeftArmOnConsole(mt));
        _rightPoint = new Node3D();
        _device.AddChild(_rightPoint);
        Part(_rightPoint, RigModels.RightArmPointing(mt));

        _reach = new Node3D { Visible = false };
        _sway.AddChild(_reach);
        Part(_reach, RigModels.LeftArmReaching(mt));
    }

    private static MeshInstance3D Part(Node3D parent, Mesh mesh)
    {
        var part = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(part);
        return part;
    }

    /// <summary>Un golpe del cabezal, sincronizado con el comando enviado al servidor.</summary>
    public void OnHit()
    {
        _hitV += 7f;
        _shake = new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), 0) * 0.0035f;
    }

    /// <summary>Pulsación sobre la consola: el dedo baja a la pantalla.</summary>
    public void OnTap() => _tap = 1f;

    /// <summary>Gesto breve de recoger o interactuar. Nunca bloquea: se puede solapar con cualquier otra acción.</summary>
    public void OnGesture()
    {
        if (_gesture >= 0.6f) _gesture = 0f;
    }

    public void Tick(float dt, in Input i)
    {
        _time += dt;
        UpdateMode(dt, i);
        Layers(dt, i);
        PoseTool(dt, i);
        PoseDevice(dt, i);
        PoseReach(dt);
    }

    private void UpdateMode(float dt, in Input i)
    {
        var deviceToolChange = i.Mode == RigMode.Device && _shown == RigMode.Device && i.Tool != _shownTool;
        if (deviceToolChange)
        {
            // Mismo dispositivo: la pantalla cambia de función con un pequeño giro, sin guardarlo.
            _shownTool = i.Tool;
            _toolSwitch = 1f;
            Sound?.Invoke("rig_click");
        }
        if (i.Mode != _shown)
        {
            _equip = Mathf.MoveToward(_equip, 0f, dt * 6.5f);
            if (_equip <= 0.001f)
            {
                _shown = i.Mode;
                _shownTool = i.Tool;
                if (_shown != RigMode.Stowed) Sound?.Invoke("rig_equip");
            }
        }
        else
        {
            _equip = Mathf.MoveToward(_equip, _shown == RigMode.Stowed ? 0f : 1f, dt * 3.6f);
        }
        _tool.Visible = _shown == RigMode.Cutter && _equip > 0.001f;
        _device.Visible = _shown == RigMode.Device && _equip > 0.001f;
        _toolSwitch = Mathf.MoveToward(_toolSwitch, 0f, dt * 4f);
    }

    /// <summary>Capas comunes: respiración, paso, inercia del ratón, salto, aterrizaje y proximidad a una pared.</summary>
    private void Layers(float dt, in Input i)
    {
        var flat = new Vector2(i.Velocity.X, i.Velocity.Z).Length();
        var strength = i.BobOn ? i.BobScale : 0f;
        var moving = i.OnFloor ? Mathf.Clamp(flat / 7f, 0f, 1.5f) : 0f;
        _walk = Mathf.Lerp(_walk, moving, 1f - Mathf.Exp(-dt * 9f));
        _run = Mathf.Lerp(_run, i.Sprinting && flat > 8f && i.OnFloor && !i.Working ? 1f : 0f, 1f - Mathf.Exp(-dt * 7f));
        _walkPhase += dt * flat * 1.25f;

        // Inercia del ratón con muelle: la herramienta se retrasa un poco al girar.
        var target = new Vector2(Mathf.Clamp(-i.Look.X * 0.0006f, -0.032f, 0.032f), Mathf.Clamp(i.Look.Y * 0.0006f, -0.026f, 0.026f));
        _swayV += ((target - _sway2) * 120f - _swayV * 18f) * dt;
        _sway2 += _swayV * dt;

        // Salto y aterrizaje.
        if (!i.OnFloor) _lastVy = i.Velocity.Y;
        if (i.OnFloor && !_wasOnFloor && _lastVy < -1f) _landV -= Mathf.Clamp(-_lastVy * 0.16f, 0.2f, 1.1f);
        _wasOnFloor = i.OnFloor;
        var airTarget = i.OnFloor ? 0f : Mathf.Clamp(-i.Velocity.Y * 0.0075f, -0.028f, 0.03f);
        _air = Mathf.Lerp(_air, airTarget, 1f - Mathf.Exp(-dt * 9f));
        _landV += (-_land * 160f - _landV * 15f) * dt;
        _land += _landV * dt;

        // Retracción cerca de paredes: nunca a golpes.
        var near = Mathf.Clamp(1f - (i.WallDistance - 0.42f) / 0.62f, 0f, 1f);
        _retract = Mathf.Lerp(_retract, near, 1f - Mathf.Exp(-dt * 10f));

        var breath = Mathf.Sin(_time * 1.32f);
        var breath2 = Mathf.Sin(_time * 0.71f + 1.2f);
        var stepX = Mathf.Cos(_walkPhase) * 0.0068f * _walk * strength;
        var stepY = -Mathf.Abs(Mathf.Sin(_walkPhase)) * 0.0078f * _walk * strength;
        var roll = Mathf.Cos(_walkPhase) * 0.012f * _walk * strength;
        _sway.Position = new Vector3(stepX + _sway2.X + breath2 * 0.0007f, stepY + _sway2.Y + breath * 0.0017f + _air + _land * 0.05f, _land * 0.012f + _retract * 0.1f);
        _sway.Rotation = new Vector3(breath * 0.0028f + _land * 0.05f - _sway2.Y * 0.7f + _retract * 0.42f, -_sway2.X * 0.8f, roll + _sway2.X * 0.45f);

        // En ultrapanorámica la herramienta se separa un poco del centro para seguir fuera de la zona útil.
        var size = GetViewport().GetVisibleRect().Size;
        var aspect = size.Y > 0 ? size.X / size.Y : 1.78f;
        _aspectShift = Mathf.Clamp((aspect - 1.78f) * 0.07f, 0f, 0.065f);
    }

    private void PoseTool(float dt, in Input i)
    {
        var working = i.Working && _shown == RigMode.Cutter;
        _work = Mathf.Lerp(_work, working ? 1f : 0f, 1f - Mathf.Exp(-dt * (working ? 13f : 7f)));
        _spin = Mathf.MoveToward(_spin, working ? 46f : 0f, dt * (working ? 150f : 34f));
        _rotor.RotateZ(_spin * dt);
        _hitV += (-_hit * 420f - _hitV * 26f) * dt;
        _hit += _hitV * dt;
        _shake = _shake.Lerp(Vector3.Zero, 1f - Mathf.Exp(-dt * 22f));
        if (!_tool.Visible) return;

        var e = Mathf.SmoothStep(0f, 1f, _equip);
        var low = (1f - e);
        var pos = ToolPos + new Vector3(_aspectShift + low * 0.05f, -low * 0.42f, low * 0.12f);
        var rot = ToolRot + new Vector3(-low * 1.05f, low * 0.34f, -low * 0.3f);
        pos += new Vector3(0.022f, -0.04f, 0.035f) * _run;
        rot += new Vector3(-0.2f, 0.42f, -0.2f) * _run;
        var basis = Basis.FromEuler(rot);

        var boomTarget = 0f;
        if (i.Target is { } global && working)
        {
            var local = _sway.ToLocal(global);
            var head = pos + basis * new Vector3(0, NeckY, NeckZ - 0.1f);
            var to = local - head;
            boomTarget = Mathf.Clamp(to.Length() - 0.16f, 0f, MaxBoom);
            var aim = (local - pos - basis * new Vector3(0, NeckY, 0)).Normalized();
            var current = (basis * Vector3.Forward).Normalized();
            var q = new Quaternion(current, aim);
            basis = new Basis(Quaternion.Identity.Slerp(q, _work * 0.88f)) * basis;
        }
        _boomV += ((boomTarget - _boom) * 90f - _boomV * 17f) * dt;
        _boom = Mathf.Clamp(_boom + _boomV * dt, 0f, MaxBoom);

        // Golpeo rítmico: el cabezal entra y sale y el cuerpo recibe un eco pequeño.
        var strike = Mathf.Max(0f, _hit) * 0.34f + Mathf.Sin(_time * 21f) * 0.012f * _work;
        var head2 = _boom + strike * _work;
        pos += basis * new Vector3(0, -0.012f, -0.018f) * _work;
        pos += basis * new Vector3(0, 0, _hit * 0.016f) + _shake;
        _tool.Transform = new Transform3D(basis * Basis.FromEuler(new Vector3(_hit * 0.018f, 0, 0)), pos);

        _head.Position = new Vector3(0, NeckY, NeckZ - head2);
        var extended = head2 > 0.004f;
        _stage1.Visible = _stage2.Visible = extended;
        if (extended)
        {
            var first = head2 * 0.52f + 0.01f;
            _stage1.Scale = new Vector3(1, 1, first);
            _stage2.Position = new Vector3(0, NeckY, NeckZ - first + 0.012f);
            _stage2.Scale = new Vector3(1, 1, Mathf.Max(0.01f, head2 - first + 0.012f));
        }

        // Durante un gesto la mano izquierda suelta el asa y recoge; vuelve sola.
        var off = GestureCurve();
        _leftTool.Position = new Vector3(-0.05f, -0.26f, 0.08f) * off;
        _leftTool.Visible = off < 0.92f;
    }

    private void PoseDevice(float dt, in Input i)
    {
        _tap = Mathf.MoveToward(_tap, 0f, dt * 4.5f);
        if (!_device.Visible) return;
        var e = Mathf.SmoothStep(0f, 1f, _equip);
        var low = 1f - e;
        // Variación por función: colocar en alto y estable, desmontar más cerrado y bajo, vías más abierto, cinta girada hacia las máquinas.
        var (offset, turn, color) = _shownTool switch
        {
            Game.Tool.Link => (new Vector3(0.012f, 0.004f, 0f), new Vector3(-0.03f, 0.16f, 0.07f), new Color(0.35f, 0.82f, 1f)),
            Game.Tool.Remove => (new Vector3(-0.02f, -0.016f, 0.012f), new Vector3(0.1f, -0.13f, -0.08f), new Color(1f, 0.32f, 0.22f)),
            Game.Tool.Rail => (new Vector3(0.025f, -0.008f, 0f), new Vector3(-0.08f, 0.1f, 0.03f), new Color(0.72f, 0.9f, 0.45f)),
            _ => (Vector3.Zero, Vector3.Zero, new Color(1f, 0.67f, 0.25f)),
        };
        _screenColor = _screenColor.Lerp(color, 1f - Mathf.Exp(-dt * 10f));
        RigMaterials.View.Screen.Emission = _screenColor;

        var flip = Mathf.Sin(_toolSwitch * Mathf.Pi);
        var pos = DevicePos + offset + new Vector3(_aspectShift * 0.3f - low * 0.03f, -low * 0.36f - flip * 0.012f, low * 0.1f);
        var rot = DeviceRot + turn + new Vector3(-low * 0.95f - flip * 0.14f, low * 0.2f, low * 0.25f);
        pos += new Vector3(0.015f, -0.03f, 0.02f) * _run;
        rot += new Vector3(-0.15f, 0.2f, 0.1f) * _run;
        _device.Transform = new Transform3D(Basis.FromEuler(rot), pos);

        var press = Mathf.Sin(_tap * Mathf.Pi);
        var idle = Mathf.Sin(_time * 0.9f);
        _rightPoint.Position = new Vector3(idle * 0.0016f, -press * 0.019f + idle * 0.0013f, press * 0.003f);
        _rightPoint.Rotation = new Vector3(press * 0.15f, 0, 0);
    }

    private void PoseReach(float dt)
    {
        _gesture = Mathf.MoveToward(_gesture, 1f, dt / 0.34f);
        var amount = GestureCurve();
        _reach.Visible = amount > 0.01f;
        if (!_reach.Visible) return;
        var p = ReachPos + new Vector3(-_aspectShift * 0.3f, 0, 0) + new Vector3(0.05f, -0.34f, 0.1f) * (1f - amount);
        _reach.Transform = new Transform3D(Basis.FromEuler(new Vector3(0.42f - amount * 0.28f, -0.2f, 0.25f * (1f - amount))), p);
    }

    /// <summary>0 reposo, 1 mano extendida. Rápida al salir y algo más lenta al volver.</summary>
    private float GestureCurve()
    {
        if (_gesture >= 1f) return 0f;
        return _gesture < 0.35f ? Mathf.SmoothStep(0f, 1f, _gesture / 0.35f) : Mathf.SmoothStep(1f, 0f, (_gesture - 0.35f) / 0.65f);
    }

    public Action<string>? Sound;
}

/// <summary>Partículas de trabajo y rotura según el material, limitadas por emisor. Solo representación local.</summary>
public partial class ImpactEffects : Node3D
{
    private readonly Dictionary<ImpactKind, GpuParticles3D> _flow = new();
    private readonly Dictionary<ImpactKind, GpuParticles3D> _burst = new();

    public override void _Ready()
    {
        foreach (var kind in Enum.GetValues<ImpactKind>())
        {
            _flow[kind] = Emitter(kind, false);
            _burst[kind] = Emitter(kind, true);
        }
    }

    private GpuParticles3D Emitter(ImpactKind kind, bool burst)
    {
        var (color, emission, speedMin, speedMax, gravity, size, life, spread) = kind switch
        {
            ImpactKind.Ice => (new Color(0.78f, 0.9f, 1f, 0.75f), 0.12f, 0.5f, 2.2f, -1.1f, 0.05f, 0.62f, 80f),
            ImpactKind.Rock => (new Color(0.34f, 0.2f, 0.15f), 0f, 1.2f, 3.2f, -3.6f, 0.045f, 0.6f, 60f),
            _ => (new Color(1f, 0.62f, 0.24f), 2.6f, 1.8f, 4.6f, -3.1f, 0.022f, 0.46f, 65f),
        };
        var scale = new Curve();
        scale.AddPoint(new Vector2(0, 1));
        scale.AddPoint(new Vector2(0.7f, 0.8f));
        scale.AddPoint(new Vector2(1, 0));
        var process = new ParticleProcessMaterial
        {
            Direction = Vector3.Up,
            Spread = spread,
            InitialVelocityMin = speedMin * (burst ? 1.25f : 1f),
            InitialVelocityMax = speedMax * (burst ? 1.25f : 1f),
            Gravity = new Vector3(0, gravity, 0),
            ScaleMin = 0.55f,
            ScaleMax = burst ? 1.8f : 1.2f,
            ScaleCurve = new CurveTexture { Curve = scale },
            AngleMin = -180,
            AngleMax = 180,
            AngularVelocityMin = -400,
            AngularVelocityMax = 400,
            DampingMin = kind == ImpactKind.Ice ? 1.2f : 0.2f,
            DampingMax = kind == ImpactKind.Ice ? 2.6f : 0.6f,
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = kind == ImpactKind.Ice ? 0.3f : 0.8f,
            Transparency = kind == ImpactKind.Ice ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
            ShadingMode = kind == ImpactKind.Rock ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        if (emission > 0)
        {
            material.EmissionEnabled = true;
            material.Emission = color;
            material.EmissionEnergyMultiplier = emission;
        }
        Mesh mesh = kind switch
        {
            ImpactKind.Ice => new QuadMesh { Size = new Vector2(size, size), Material = BillboardOf(material) },
            ImpactKind.Rock => new BoxMesh { Size = new Vector3(size, size * 0.7f, size * 0.85f), Material = material },
            _ => new BoxMesh { Size = new Vector3(size * 0.45f, size * 0.45f, size * 2.6f), Material = material },
        };
        var emitter = new GpuParticles3D
        {
            Amount = burst ? 26 : 14,
            Lifetime = life,
            OneShot = burst,
            Explosiveness = burst ? 0.95f : 0.1f,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = process,
            DrawPass1 = mesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-6, -6, -6), new Vector3(12, 12, 12)),
        };
        AddChild(emitter);
        return emitter;
    }

    private static StandardMaterial3D BillboardOf(StandardMaterial3D m)
    {
        m.BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles;
        m.VertexColorUseAsAlbedo = true;
        return m;
    }

    /// <summary>Emisión continua mientras el cabezal trabaja, o nada si kind es nulo.</summary>
    public void Work(ImpactKind? kind, Vector3 at, Vector3 towards)
    {
        foreach (var (k, emitter) in _flow)
        {
            var on = k == kind;
            if (on)
            {
                emitter.GlobalPosition = at + towards * 0.08f;
                ((ParticleProcessMaterial)emitter.ProcessMaterial).Direction = (towards + Vector3.Up * 0.8f).Normalized();
            }
            if (emitter.Emitting != on) emitter.Emitting = on;
        }
    }

    public void Burst(ImpactKind kind, Vector3 at)
    {
        var emitter = _burst[kind];
        emitter.GlobalPosition = at;
        emitter.Restart();
        emitter.Emitting = true;
    }
}

/// <summary>Sonido propio de la herramienta, sintetizado en memoria: motor en bucle y golpe según el material.</summary>
public partial class ToolAudio : Node
{
    private const int Rate = 22050;
    private readonly Settings _prefs;
    private readonly RandomNumberGenerator _rng = new() { Seed = 919 };
    private readonly Dictionary<string, AudioStreamWav> _streams = new();
    private AudioStreamPlayer _motor = null!;
    private float _motorLevel;

    public ToolAudio(Settings prefs) { _prefs = prefs; }

    private float Volume => _prefs.MasterVolume * _prefs.SfxVolume;

    public override void _Ready()
    {
        var motor = new byte[Rate * 2];
        // Bucle exacto de un segundo: parciales enteros y un zumbido de engranajes.
        for (var i = 0; i < Rate; i++)
        {
            var t = i / (float)Rate;
            var s = Mathf.Sin(t * Mathf.Tau * 110) * 0.3f + Mathf.Sin(t * Mathf.Tau * 220) * 0.2f + Mathf.Sin(t * Mathf.Tau * 661) * 0.12f + Mathf.Sin(t * Mathf.Tau * 1320) * 0.05f;
            s *= 0.75f + 0.25f * Mathf.Sin(t * Mathf.Tau * 25);
            Write(motor, i, s);
        }
        var loop = new AudioStreamWav { Data = motor, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopEnd = Rate };
        _motor = new AudioStreamPlayer { Stream = loop, VolumeDb = -60 };
        AddChild(_motor);

        _streams["hit_scrap"] = Burst(0.11f, 2600, 0.5f, 18f);
        _streams["hit_ice"] = Burst(0.1f, 900, 0.9f, 26f);
        _streams["hit_rock"] = Burst(0.12f, 420, 0.8f, 22f);
        _streams["rig_equip"] = Burst(0.16f, 340, 0.25f, 14f);
        _streams["rig_click"] = Burst(0.05f, 2100, 0.2f, 40f);
    }

    private AudioStreamWav Burst(float seconds, float tone, float noise, float decay)
    {
        var frames = (int)(seconds * Rate);
        var data = new byte[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var t = i / (float)Rate;
            var env = Mathf.Exp(-t * decay) * Mathf.Clamp(t * 900f, 0f, 1f);
            Write(data, i, (Mathf.Sin(t * Mathf.Tau * tone) * (1 - noise) + _rng.RandfRange(-1, 1) * noise) * env * 0.75f);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate };
    }

    private static void Write(byte[] data, int i, float sample)
    {
        var s = (short)(Mathf.Clamp(sample, -1, 1) * 32000);
        data[i * 2] = (byte)(s & 0xFF);
        data[i * 2 + 1] = (byte)(s >> 8);
    }

    public void Play(string name)
    {
        if (!_streams.TryGetValue(name, out var stream)) return;
        var p = new AudioStreamPlayer { Stream = stream, VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Volume * 0.8f)), PitchScale = _rng.RandfRange(0.9f, 1.1f) };
        AddChild(p);
        p.Finished += p.QueueFree;
        p.Play();
    }

    public void Hit(ImpactKind kind) => Play(kind switch { ImpactKind.Ice => "hit_ice", ImpactKind.Rock => "hit_rock", _ => "hit_scrap" });

    /// <summary>El motor sube y baja de régimen; se apaga solo si no se trabaja.</summary>
    public void Motor(float dt, bool working)
    {
        _motorLevel = Mathf.MoveToward(_motorLevel, working ? 1f : 0f, dt * (working ? 6f : 3.5f));
        if (_motorLevel <= 0.001f)
        {
            if (_motor.Playing) _motor.Stop();
            return;
        }
        if (!_motor.Playing) _motor.Play();
        _motor.PitchScale = 0.55f + _motorLevel * 0.55f;
        _motor.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.001f, Volume * 0.42f * _motorLevel));
    }
}

/// <summary>Representación externa de la herramienta en otros jugadores: un conjunto ligero que solo reacciona al estado replicado.</summary>
public partial class RemoteToolView : Node3D
{
    private Node3D _toolRoot = null!, _rotor = null!, _deviceRoot = null!, _forearm = null!;
    private float _work, _spin, _time;
    private byte _equipped = 255;

    public override void _Ready()
    {
        var mt = RigMaterials.World;
        _forearm = new Node3D();
        AddChild(_forearm);
        _forearm.AddChild(new MeshInstance3D { Mesh = RigModels.RemoteForearm() });
        _toolRoot = new Node3D { Scale = Vector3.One * 1.25f };
        _forearm.AddChild(_toolRoot);
        _toolRoot.AddChild(new MeshInstance3D { Mesh = RigModels.ToolBody(mt) });
        var head = new Node3D { Position = new Vector3(0, 0.09f, -0.262f) };
        _toolRoot.AddChild(head);
        head.AddChild(new MeshInstance3D { Mesh = RigModels.HeadHousing(mt) });
        _rotor = new Node3D { Position = new Vector3(0, 0, -0.058f) };
        head.AddChild(_rotor);
        _rotor.AddChild(new MeshInstance3D { Mesh = RigModels.HeadRotor(mt) });
        _deviceRoot = new Node3D { Position = new Vector3(0, 0.02f, -0.1f), Scale = Vector3.One * 1.25f, Rotation = new Vector3(0.7f, 0, 0) };
        _forearm.AddChild(_deviceRoot);
        _deviceRoot.AddChild(new MeshInstance3D { Mesh = RigModels.Console(mt) });
    }

    public void Tick(float dt, byte equipped, bool working)
    {
        _time += dt;
        if (equipped != _equipped)
        {
            _equipped = equipped;
            _forearm.Visible = equipped != Networking.PlayerAction.Stowed;
            _toolRoot.Visible = equipped == Networking.PlayerAction.Cutter;
            _deviceRoot.Visible = equipped == Networking.PlayerAction.Device;
        }
        _work = Mathf.Lerp(_work, working ? 1f : 0f, 1f - Mathf.Exp(-dt * 9f));
        _spin = Mathf.MoveToward(_spin, working ? 46f : 0f, dt * 90f);
        _rotor.RotateZ(_spin * dt);
        _forearm.Position = new Vector3(0, Mathf.Sin(_time * 20f) * 0.012f * _work, -Mathf.Sin(_time * 20f) * 0.03f * _work - _work * 0.05f);
        _forearm.Rotation = new Vector3(-_work * 0.12f, 0, 0);
    }
}
