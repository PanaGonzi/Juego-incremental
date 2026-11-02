using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>
/// Dibuja las cintas y su carga real. Nunca modifica el World espejo: predice el avance en una copia propia
/// y la corrige al recibir los huecos del servidor (1 Hz). Sin cuerpos físicos ni un nodo por objeto.
/// </summary>
public partial class BeltView : Node3D
{
    private const float Cell = 2f;
    private const float BandY = 0.46f;       // superficie de la banda sobre el suelo
    private const float BridgeY = 1.42f;     // en un cruce, por encima de la carga de la otra cinta
    private const float ElevatedY = 3.7f;    // cintas migradas sin ruta: altura mínima
    private const float ElevatedClearance = 0.7f;
    private const float HalfWidth = 0.5f;
    private const float SlotLength = Cell / 4f;
    private const float MaxItemSize = 0.42f;
    private const int ChunkSlots = 64;       // 16 celdas por malla

    /// <summary>A partir de aquí la carga se omite: el estado económico sigue en el mundo y al apuntar se informa igual.</summary>
    private const float ItemRange = 120f;
    private const float AnimRange = 90f;

    private static readonly ShaderMaterial BandMaterial = MakeBand();

    private static ShaderMaterial MakeBand()
    {
        var band = new ShaderMaterial { Shader = new Shader { Code = """
        shader_type spatial;
        render_mode cull_back, diffuse_burley;
        uniform sampler2DArray albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2DArray detail_tex : filter_linear_mipmap_anisotropic, repeat_enable;
        instance uniform float offset = 0.0;
        instance uniform float jam = 0.0;
        instance uniform float pulse = -100.0;
        void fragment(){
          float x = abs(UV.x - 0.5) * 2.0;
          float along = UV.y - offset;
          // Goma real del atlas, en metros: se desplaza con la banda.
          vec3 uvw = vec3(UV.x / 0.8, along / 0.8, 2.0);
          vec4 tex = texture(albedo_tex, uvw);
          vec4 det = texture(detail_tex, uvw);
          // Tacos transversales cada 12,5 cm, con relieve por el mapa de normales; el centro está más gastado.
          float rib = fract(along * 8.0);
          float cleat = smoothstep(0.0, 0.1, rib) * (1.0 - smoothstep(0.26, 0.36, rib));
          float slope = (smoothstep(0.0, 0.1, rib) - smoothstep(0.26, 0.36, rib)) * (rib < 0.18 ? 1.0 : -1.0);
          float worn = 1.0 - smoothstep(0.1, 0.75, x);
          vec3 rubber = vec3(0.03, 0.03, 0.033) * (0.7 + tex.r * 1.1) * (1.0 + cleat * 0.45) + vec3(0.012, 0.011, 0.01) * worn;
          // Flecha pintada y desconchada: indica el sentido sin parecer un letrero luminoso.
          float c = fract(along * 0.5 + x * 0.16);
          float chevron = smoothstep(0.0, 0.012, c) * (1.0 - smoothstep(0.05, 0.062, c)) * (1.0 - smoothstep(0.5, 0.58, x));
          chevron *= smoothstep(0.25, 0.42, tex.a + 0.12 + jam);
          vec3 mark = mix(vec3(0.72, 0.46, 0.1), vec3(0.95, 0.14, 0.08), jam);
          ALBEDO = mix(rubber, mark, chevron * 0.8);
          ROUGHNESS = mix(0.86, 0.68, worn * 0.5 + cleat * 0.2);
          METALLIC = 0.0;
          SPECULAR = 0.28;
          NORMAL_MAP = vec3(det.r, det.g + slope * 0.22, 1.0);
          AO = mix(0.7, 1.0, cleat);
          float wave = exp(-pow((UV.y - pulse) * 1.1, 2.0));
          EMISSION = mark * chevron * (0.025 + jam * 0.45) + vec3(1.0, 0.62, 0.22) * wave * (1.0 - x * 0.5) * 0.9;
        }
        """ } };
        band.SetShaderParameter("albedo_tex", Materials.AlbedoArray);
        band.SetShaderParameter("detail_tex", Materials.DetailArray);
        return band;
    }

    /// <summary>Bastidor: pintura y desgaste del mismo atlas que las máquinas, proyectados en el mundo. Sigue siendo una sola superficie.</summary>
    private static readonly ShaderMaterial FrameMaterial = MakeFrame();

    private static ShaderMaterial MakeFrame()
    {
        var frame = new ShaderMaterial { Shader = new Shader { Code = """
        shader_type spatial;
        render_mode cull_back, diffuse_burley;
        uniform sampler2DArray albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
        uniform sampler2DArray detail_tex : filter_linear_mipmap_anisotropic, repeat_enable;
        uniform vec3 dust_color = vec3(0.055, 0.04, 0.036);
        varying vec3 wp;
        varying vec3 wn;
        void vertex(){
          wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
          wn = NORMAL;
        }
        void fragment(){
          vec3 an = abs(wn);
          vec2 uv = an.y > 0.6 ? wp.xz : an.x > an.z ? wp.zy : wp.xy;
          vec3 uvw = vec3(uv / 2.0, 0.0);
          vec4 a = texture(albedo_tex, uvw);
          vec4 d = texture(detail_tex, uvw);
          vec2 nmap = d.rg;
          float fine = 0.0;
          float close = 1.0 - smoothstep(2.5, 8.0, length(VERTEX));
          if (close > 0.001){
            vec3 uvn = vec3(uv / 0.38 + vec2(0.37, 0.11), 0.0);
            nmap += (texture(detail_tex, uvn).rg - 0.5) * close * 0.9;
            fine = (texture(albedo_tex, uvn).a - 0.5) * close;
          }
          vec3 col = a.rgb * COLOR.rgb * (1.0 + fine * 0.55);
          // Rozaduras: acero visible, sobre todo en lo que más se pisa y se golpea.
          float chip = smoothstep(0.6, 0.64, a.a * 0.72 + fine * 0.16 + 0.12);
          float metal = mix(0.12, 1.0, chip);
          float rough = mix(clamp(d.b * 1.1, 0.3, 1.0), 0.42, chip);
          col = mix(col, vec3(0.42, 0.42, 0.44), chip);
          float dust = clamp(smoothstep(0.75, 0.0, wp.y + (a.a - 0.5) * 0.5) * 0.7 + smoothstep(0.7, 0.95, wn.y) * (0.15 + a.a * 0.5), 0.0, 1.0) * 0.62;
          ALBEDO = mix(col, dust_color, dust);
          METALLIC = metal * (1.0 - dust);
          ROUGHNESS = mix(rough, 0.92, dust);
          SPECULAR = 0.42;
          NORMAL_MAP = vec3(nmap, 1.0);
          AO = d.a;
          AO_LIGHT_AFFECT = 0.25;
        }
        """ } };
        frame.SetShaderParameter("albedo_tex", Materials.AlbedoArray);
        frame.SetShaderParameter("detail_tex", Materials.DetailArray);
        frame.SetShaderParameter("dust_color", Materials.DustColor);
        Materials.DustChanged += () => frame.SetShaderParameter("dust_color", Materials.DustColor);
        return frame;
    }

    // Lineales: la misma paleta de GUIA_VISUAL.md, con gris cálido y amarillo de señalización.
    private static readonly Color Steel = new(0.2f, 0.2f, 0.195f);
    private static readonly Color DarkSteel = new(0.075f, 0.08f, 0.09f);
    private static readonly Color Support = new(0.52f, 0.34f, 0.045f);

    private struct VisualItem
    {
        public string Item;
        public float Pos;
        public float Err;
        public float Seed;   // -1..1, fijo para cada objeto: giro y desvío, para que la carga no parezca una fila clonada
    }

    private sealed class BeltVisual
    {
        public int Id;
        public string Key = "";
        public readonly List<(MeshInstance3D node, Vector3 center, float radius)> Chunks = new(); // tramos internos: la cinta sigue siendo una sola
        public Vector3[] Points = Array.Empty<Vector3>();
        public Vector3 Center;
        public float Radius;
        public int Version = -1;
        public bool Blocked;
        public float Offset;
        public float ShownJam = -1;
        public VisualItem[] Items = Array.Empty<VisualItem>();
        public int Count;
        public bool Elevated;
        public float Pulse = -1;
        public float Length;
    }

    private sealed class ItemBatch
    {
        public MultiMeshInstance3D Node = null!;
        public MultiMesh Multi = null!;
        public float[] Buffer = Array.Empty<float>();
        public int Count;
        public int Shown;
        public float Lift;
        public float Scale = 1;
    }

    private readonly Dictionary<int, BeltVisual> _belts = new();
    private readonly List<int> _elevated = new();
    private readonly Dictionary<string, ItemBatch> _batches = new();
    private int _planet = -1;
    private int _stamp = int.MinValue;

    /// <summary>Cinta nueva o retirada con la partida en marcha: la respuesta visual y sonora está en ActionFeedback.</summary>
    public event Action<Vector3>? BeltAdded;
    public event Action<Vector3>? BeltRemoved;

    public int DrawnItems { get; private set; }
    public int DrawnBelts => _belts.Count;

    /// <summary>Altura del suelo bajo un punto (x, z en metros). Único punto de enlace con el terreno; hoy, plano.</summary>
    public static Func<float, float, float> GroundHeight = (_, _) => 0f;

    // ---------- Sincronización de estructura ----------

    /// <summary>Se llama al cambiar la estructura o el planeta. Solo rehace las mallas de cintas que cambiaron.</summary>
    public void Sync(World w, int planet, int structuralStamp)
    {
        if (planet == _planet && structuralStamp == _stamp) return;
        if (planet != _planet) Clear();
        var fresh = planet != _planet; // cargar el planeta no es construir
        _planet = planet;
        _stamp = structuralStamp;
        var seen = new HashSet<int>();
        _elevated.Clear();
        foreach (var l in w.Links.Values)
        {
            if (l.Planet != planet || !w.Machines.ContainsKey(l.From) || !w.Machines.ContainsKey(l.To)) continue;
            seen.Add(l.Id);
            if (l.Elevated) _elevated.Add(l.Id);
            var key = KeyOf(w, l);
            if (_belts.TryGetValue(l.Id, out var v) && v.Key == key) continue;
            if (v != null) FreeChunks(v);
            var built = Build(w, l, key);
            _belts[l.Id] = built;
            if (v == null && !fresh)
            {
                if (!Scenery.Calm) built.Pulse = 0;
                BeltAdded?.Invoke(built.Center + Vector3.Up * BandY);
            }
        }
        foreach (var id in _belts.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            if (!fresh) BeltRemoved?.Invoke(_belts[id].Center + Vector3.Up * BandY);
            FreeChunks(_belts[id]);
            _belts.Remove(id);
        }
    }

    public void Clear()
    {
        foreach (var v in _belts.Values) FreeChunks(v);
        _belts.Clear();
        _elevated.Clear();
        _stock.Clear();
        foreach (var b in _batches.Values)
        {
            b.Count = 0;
            b.Multi.VisibleInstanceCount = 0;
            b.Shown = 0;
        }
        _planet = -1;
        _stamp = int.MinValue;
        HidePreview();
    }

    private static void FreeChunks(BeltVisual v)
    {
        foreach (var c in v.Chunks) c.node.QueueFree();
        v.Chunks.Clear();
    }

    private static string KeyOf(World w, Link l)
    {
        var a = w.Machines[l.From];
        var b = w.Machines[l.To];
        var sb = new System.Text.StringBuilder();
        sb.Append(a.X).Append(',').Append(a.Z).Append(',').Append(a.Rot).Append(';').Append(b.X).Append(',').Append(b.Z).Append(',').Append(b.Rot).Append(';').Append(l.Elevated).Append(';');
        for (var i = 0; i < l.Cells.Count; i++)
            sb.Append(l.Cells[i].x).Append(',').Append(l.Cells[i].z).Append(w.IsBridge(l, i) ? '^' : ' ');
        return sb.ToString();
    }

    private BeltVisual Build(World w, Link l, string key)
    {
        var points = l.Elevated ? ElevatedPath(w, l) : RoutePath(w, l.From, l.To, l.Cells, i => w.IsBridge(l, i));
        var visual = new BeltVisual
        {
            Id = l.Id,
            Key = key,
            Points = points,
            Elevated = l.Elevated,
            Items = new VisualItem[l.Slots.Length],
        };
        var min = points[0];
        var max = points[0];
        foreach (var p in points)
        {
            min = new Vector3(Mathf.Min(min.X, p.X), 0, Mathf.Min(min.Z, p.Z));
            max = new Vector3(Mathf.Max(max.X, p.X), 0, Mathf.Max(max.Z, p.Z));
        }
        visual.Center = (min + max) / 2;
        visual.Radius = (max - min).Length() / 2;
        for (var i = 0; i < points.Length - 1; i++) visual.Length += points[i].DistanceTo(points[i + 1]);

        // Una malla por tramo: la distancia de dibujo se mide desde cada uno y una cinta larga no desaparece entera.
        var along = 0f;
        for (var start = 0; start < points.Length - 1; start += ChunkSlots)
        {
            var end = Mathf.Min(start + ChunkSlots, points.Length - 1);
            var node = new MeshInstance3D
            {
                Mesh = BuildMesh(points, start, end, ref along, l.Elevated, l, w),
                VisibilityRangeEnd = 190f,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            };
            AddChild(node);
            var a = points[start];
            var b = points[end];
            visual.Chunks.Add((node, (a + b) / 2, a.DistanceTo(b) / 2 + 4f));
        }
        return visual;
    }

    // ---------- Trayectoria ----------

    /// <summary>Puntos en los límites de hueco: el índice i es el inicio del hueco i. Curvas por celda con Bézier cuadrática.</summary>
    private static Vector3[] RoutePath(World w, int from, int to, IReadOnlyList<(int x, int z)> cells, Func<int, bool> bridge)
    {
        var slotsPerCell = w.Content.Balance.BeltSlotsPerCell;
        var points = new Vector3[cells.Count * slotsPerCell + 1];
        var a = w.Machines[from];
        var entry = PortSide(w.BeltPorts(a, true), cells[0], cells.Count > 1 ? cells[1] : null);
        // Sin máquina de destino (previsualización hasta el suelo) sigue en la misma dirección.
        (int dx, int dz) exit = to >= 0
            ? PortSide(w.BeltPorts(w.Machines[to], false), cells[^1], cells.Count > 1 ? cells[^2] : null)
            : cells.Count > 1 ? (cells[^1].x - cells[^2].x, cells[^1].z - cells[^2].z) : (-entry.dx, -entry.dz);

        // Puente plano sobre la celda cruzada, con rampa en toda la celda vecina.
        bool High(int i) => i >= 0 && i < cells.Count && bridge(i);
        float EdgeHeight(int edge) => High(edge - 1) || High(edge) ? BridgeY : BandY;
        for (var k = 0; k < cells.Count; k++)
        {
            var center = new Vector2((cells[k].x + 0.5f) * Cell, (cells[k].z + 0.5f) * Cell);
            var inEdge = k == 0
                ? center + new Vector2(entry.dx, entry.dz) * Cell / 2
                : (center + new Vector2((cells[k - 1].x + 0.5f) * Cell, (cells[k - 1].z + 0.5f) * Cell)) / 2;
            var outEdge = k == cells.Count - 1
                ? center + new Vector2(exit.dx, exit.dz) * Cell / 2
                : (center + new Vector2((cells[k + 1].x + 0.5f) * Cell, (cells[k + 1].z + 0.5f) * Cell)) / 2;
            var hIn = EdgeHeight(k);
            var hOut = EdgeHeight(k + 1);
            for (var s = 0; s <= slotsPerCell; s++)
            {
                if (s == 0 && k > 0) continue;
                var t = s / (float)slotsPerCell;
                var u = 1 - t;
                var xz = inEdge * u * u + center * 2 * u * t + outEdge * t * t;
                var y = Mathf.Lerp(hIn, hOut, Mathf.SmoothStep(0, 1, t));
                points[k * slotsPerCell + s] = new Vector3(xz.X, y, xz.Y);
            }
        }
        ApplyGround(points);
        return points;
    }

    /// <summary>Sigue el suelo con pendiente suave: media móvil de dos celdas, sin quedar nunca por debajo de él.</summary>
    private static void ApplyGround(Vector3[] points)
    {
        var raw = new float[points.Length];
        var flat = true;
        for (var i = 0; i < points.Length; i++)
        {
            raw[i] = GroundHeight(points[i].X, points[i].Z);
            flat &= raw[i] == 0f;
        }
        if (flat) return;
        const int window = 8;
        for (var i = 0; i < points.Length; i++)
        {
            var sum = 0f;
            var count = 0;
            for (var k = Mathf.Max(0, i - window); k <= Mathf.Min(points.Length - 1, i + window); k++)
            {
                sum += raw[k];
                count++;
            }
            points[i].Y += Mathf.Max(sum / count, raw[i]);
        }
    }

    /// <summary>Lado de la celda que da a la máquina. Con varias caras posibles, la que deja seguir recto.</summary>
    private static (int dx, int dz) PortSide(IEnumerable<BeltPort> ports, (int x, int z) cell, (int x, int z)? neighbor)
    {
        (int dx, int dz)? any = null;
        foreach (var p in ports)
        {
            if (p.X != cell.x || p.Z != cell.z) continue;
            var (dx, dz) = World.BeltDirs[p.Dir];
            var side = (-dx, -dz);
            any ??= side;
            if (neighbor is { } n && (n.x - cell.x, n.z - cell.z) == (dx, dz)) return side;
        }
        return any ?? (0, 0);
    }

    private static Vector3[] ElevatedPath(World w, Link l)
    {
        var (ax, az) = w.MachineCenterMm(w.Machines[l.From]);
        var (bx, bz) = w.MachineCenterMm(w.Machines[l.To]);
        // Pasa por encima de la más alta de las dos: nunca atraviesa sus propias máquinas.
        var top = Mathf.Max(TopOf(w.Machines[l.From].Def), TopOf(w.Machines[l.To].Def));
        var y = Mathf.Max(ElevatedY, top + ElevatedClearance);
        var a = new Vector3(ax / 1000f, y, az / 1000f);
        var b = new Vector3(bx / 1000f, y, bz / 1000f);
        var points = new Vector3[l.Slots.Length + 1];
        for (var i = 0; i < points.Length; i++) points[i] = a.Lerp(b, i / (float)l.Slots.Length);
        return points;
    }

    private static float TopOf(string def)
    {
        var aabb = Models.Machine(def).Body.GetAabb();
        return aabb.Position.Y + aabb.Size.Y;
    }

    // ---------- Geometría ----------

    private sealed class Geometry
    {
        public readonly List<Vector3> V = new();
        public readonly List<float> T = new();
        public readonly List<Vector3> N = new();
        public readonly List<Vector2> Uv = new();
        public readonly List<Color> C = new();
        public readonly List<int> I = new();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color color, Vector2? uvA = null, Vector2? uvB = null, Vector2? uvC = null, Vector2? uvD = null)
        {
            var start = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            var tangent = (b - a).Normalized();
            for (var i = 0; i < 4; i++)
            {
                N.Add(n);
                C.Add(color);
                T.Add(tangent.X); T.Add(tangent.Y); T.Add(tangent.Z); T.Add(1f);
            }
            Uv.Add(uvA ?? Vector2.Zero); Uv.Add(uvB ?? Vector2.Zero); Uv.Add(uvC ?? Vector2.Zero); Uv.Add(uvD ?? Vector2.Zero);
            // Godot considera cara frontal la de sentido horario vista desde fuera.
            var clockwise = (b - a).Cross(c - a).Dot(n) < 0;
            if (clockwise) I.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            else I.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        /// <summary>Caja orientada: centro, medio tamaño y ejes.</summary>
        public void Box(Vector3 center, Vector3 half, Vector3 ax, Vector3 ay, Vector3 az, Color color)
        {
            var x = ax * half.X;
            var y = ay * half.Y;
            var z = az * half.Z;
            Quad(center + y - x - z, center + y + x - z, center + y + x + z, center + y - x + z, ay, color);
            Quad(center - y - x - z, center - y + x - z, center - y + x + z, center - y - x + z, -ay, color * 0.7f);
            Quad(center + x - y - z, center + x + y - z, center + x + y + z, center + x - y + z, ax, color);
            Quad(center - x - y - z, center - x + y - z, center - x + y + z, center - x - y + z, -ax, color);
            Quad(center + z - x - y, center + z + x - y, center + z + x + y, center + z - x + y, az, color);
            Quad(center - z - x - y, center - z + x - y, center - z + x + y, center - z - x + y, -az, color);
        }

        /// <summary>Cilindro de ocho caras a lo largo de axis: rodillos, tambores y ejes.</summary>
        public void Prism(Vector3 center, Vector3 axis, Vector3 up, float radius, float halfLength, Color color)
        {
            var side = axis.Cross(up).Normalized();
            Vector3 Rim(int i) => (up * Mathf.Cos(i * Mathf.Tau / 8) + side * Mathf.Sin(i * Mathf.Tau / 8)) * radius;
            for (var i = 0; i < 8; i++)
            {
                var r0 = Rim(i);
                var r1 = Rim(i + 1);
                Quad(center + r0 - axis * halfLength, center + r1 - axis * halfLength, center + r1 + axis * halfLength, center + r0 + axis * halfLength, (r0 + r1).Normalized(), color);
            }
            foreach (var sign in new[] { -1f, 1f })
            {
                var c = center + axis * (sign * halfLength);
                for (var i = 0; i < 3; i++) Quad(c + Rim(0), c + Rim(i * 2 + 1), c + Rim(i * 2 + 2), c + Rim(i * 2 + 3), axis * sign, color * 0.8f);
            }
        }

        public void Commit(ArrayMesh mesh, Material material)
        {
            if (V.Count == 0) return;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = V.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
            arrays[(int)Mesh.ArrayType.Tangent] = T.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV] = Uv.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = C.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = I.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
        }
    }

    private static Vector3 Tangent(Vector3[] p, int i)
    {
        var a = p[Mathf.Max(0, i - 1)];
        var b = p[Mathf.Min(p.Length - 1, i + 1)];
        var t = b - a;
        return t.LengthSquared() < 1e-6f ? Vector3.Forward : t.Normalized();
    }

    private static Vector3 Side(Vector3 tangent)
    {
        var flat = new Vector3(tangent.X, 0, tangent.Z);
        if (flat.LengthSquared() < 1e-6f) flat = Vector3.Forward;
        return flat.Normalized().Cross(Vector3.Up).Normalized();
    }

    private static bool MachineUnder(World w, int planet, Vector3 at, Vector3 side)
    {
        foreach (var sign in new[] { -1f, 1f })
        {
            var q = at + side * sign * (HalfWidth + 0.15f);
            if (w.MachineAt(planet, Mathf.FloorToInt(q.X / Cell), Mathf.FloorToInt(q.Z / Cell)) >= 0) return true;
        }
        return false;
    }

    private static ArrayMesh BuildMesh(Vector3[] p, int start, int end, ref float along, bool elevated, Link? link, World w)
    {
        var band = new Geometry();
        var frame = new Geometry();
        for (var i = start; i < end; i++)
        {
            var t0 = Tangent(p, i);
            var t1 = Tangent(p, i + 1);
            var s0 = Side(t0);
            var s1 = Side(t1);
            var next = along + p[i].DistanceTo(p[i + 1]);
            var n = s0.Cross(t0).Normalized();
            if (n.Y < 0) n = -n;
            band.Quad(p[i] - s0 * HalfWidth, p[i] + s0 * HalfWidth, p[i + 1] + s1 * HalfWidth, p[i + 1] - s1 * HalfWidth, n, Colors.White,
                new Vector2(0, along), new Vector2(1, along), new Vector2(1, next), new Vector2(0, next));
            // Bordes de la bandeja, a ambos lados: labio superior y cara exterior.
            foreach (var sign in new[] { -1f, 1f })
            {
                var o0 = p[i] + s0 * sign * (HalfWidth + 0.07f);
                var o1 = p[i + 1] + s1 * sign * (HalfWidth + 0.07f);
                var i0 = p[i] + s0 * sign * HalfWidth;
                var i1 = p[i + 1] + s1 * sign * HalfWidth;
                var lip = Vector3.Up * 0.05f;
                var skirt = Vector3.Down * 0.14f;
                frame.Quad(i0 + lip, o0 + lip, o1 + lip, i1 + lip, Vector3.Up, Steel);
                frame.Quad(o0 + lip, o0 + skirt, o1 + skirt, o1 + lip, s0 * sign, Steel);
                frame.Quad(i0 + lip, i0, i1, i1 + lip, -s0 * sign, DarkSteel);
                frame.Quad(o0 + skirt, i0 + skirt, i1 + skirt, o1 + skirt, Vector3.Down, DarkSteel);
            }
            // Fondo de la bandeja: oculta el hueco bajo la banda.
            frame.Quad(p[i] - s0 * HalfWidth + Vector3.Down * 0.14f, p[i] + s0 * HalfWidth + Vector3.Down * 0.14f, p[i + 1] + s1 * HalfWidth + Vector3.Down * 0.14f, p[i + 1] - s1 * HalfWidth + Vector3.Down * 0.14f, Vector3.Down, DarkSteel);
            along = next;
        }

        // Rodillos cada dos huecos, tambores en los extremos y soportes reutilizables.
        for (var i = start + start % 2; i <= end; i += 2)
        {
            var t = Tangent(p, i);
            var s = Side(t);
            var up = s.Cross(t).Normalized();
            if (up.Y < 0) up = -up;
            // Rodillo de retorno que asoma bajo el faldón, con su eje hasta el bastidor.
            frame.Prism(p[i] + Vector3.Down * 0.2f, s, up, 0.055f, HalfWidth + 0.02f, DarkSteel);
            frame.Prism(p[i] + Vector3.Down * 0.2f, s, up, 0.018f, HalfWidth + 0.085f, Steel);
        }
        foreach (var i in new[] { 0, p.Length - 1 }.Where(i => i >= start && i <= end))
        {
            // Remate: tambor sobre el que da la vuelta la banda, con dos cartelas y su eje. Sin caja encima de la goma.
            var t = Tangent(p, i);
            var s = Side(t);
            var outward = i == 0 ? -t : t;
            var axle = p[i] + Vector3.Down * 0.14f + outward * 0.03f;
            frame.Prism(axle, s, Vector3.Up, 0.135f, HalfWidth - 0.01f, DarkSteel);
            frame.Prism(axle, s, Vector3.Up, 0.028f, HalfWidth + 0.13f, Steel);
            foreach (var sign in new[] { -1f, 1f })
                frame.Box(axle + s * sign * (HalfWidth + 0.055f) + Vector3.Up * 0.02f, new Vector3(0.02f, 0.17f, 0.2f), s, Vector3.Up, t, Support);
        }
        if (elevated)
        {
            // Postes que apoyan los extremos sobre el techo de cada máquina.
            foreach (var i in new[] { 0, p.Length - 1 }.Where(i => i >= start && i <= end))
                frame.Box(p[i] + Vector3.Down * (ElevatedClearance / 2 + 0.1f), new Vector3(0.1f, ElevatedClearance / 2 + 0.05f, 0.1f), Vector3.Right, Vector3.Up, Vector3.Back, Support);
        }
        var step = elevated ? 16 : 4;
        var firstLeg = elevated ? 8 : 2;
        while (firstLeg < start) firstLeg += step;
        for (var i = firstLeg; i < end && i < p.Length - 1; i += step)
        {
            var t = Tangent(p, i);
            var s = Side(t);
            // Las elevadas pasan sobre máquinas: ahí no hay pata que atraviese nada.
            if (elevated && link != null && MachineUnder(w, link.Planet, p[i], s)) continue;
            var top = p[i].Y - 0.16f;
            var groundUnder = GroundHeight(p[i].X, p[i].Z);
            var legHalf = (top - groundUnder) / 2;
            var bridgeLeg = p[i].Y - groundUnder > BandY + 0.3f;
            var spread = HalfWidth + (elevated ? 0.14f : 0.1f);
            var width = elevated ? 0.11f : 0.04f;
            // Travesaño que recoge el bastidor: las patas ya no atraviesan el faldón.
            frame.Box(p[i] + Vector3.Down * 0.165f, new Vector3(spread + 0.03f, 0.025f, 0.045f), s, Vector3.Up, t, DarkSteel);
            foreach (var sign in new[] { -1f, 1f })
            {
                var foot = p[i] + s * sign * spread;
                foot.Y = GroundHeight(foot.X, foot.Z);
                var legColor = bridgeLeg || elevated ? Support : Steel;
                frame.Box(foot + Vector3.Up * legHalf, new Vector3(width * 0.6f, legHalf, width), s, Vector3.Up, t, legColor);
                // Cartela diagonal del bastidor a la pata, y zapata con dos pernos.
                var brace = p[i] + s * sign * (HalfWidth - 0.1f) + Vector3.Down * 0.17f;
                var knee = foot + Vector3.Up * Mathf.Max(0.08f, legHalf);
                var run = knee - brace;
                if (run.Length() > 0.05f && !elevated)
                {
                    var dir = run.Normalized();
                    frame.Box((brace + knee) / 2, new Vector3(0.012f, run.Length() / 2, 0.022f), dir.Cross(t).Normalized(), dir, t, legColor * 0.8f);
                }
                frame.Box(foot + Vector3.Up * 0.012f, new Vector3(width + 0.07f, 0.012f, width + 0.09f), s, Vector3.Up, t, DarkSteel);
                foreach (var along2 in new[] { -1f, 1f })
                    frame.Box(foot + t * along2 * (width + 0.05f) + Vector3.Up * 0.03f, new Vector3(0.016f, 0.012f, 0.016f), s, Vector3.Up, t, Steel);
            }
            if (bridgeLeg || elevated)
            {
                var cross = p[i];
                cross.Y = groundUnder + (top - groundUnder) * 0.55f;
                frame.Box(cross, new Vector3(spread, 0.035f, 0.035f), s, Vector3.Up, t, Support);
            }
        }

        var mesh = new ArrayMesh();
        band.Commit(mesh, BandMaterial);
        frame.Commit(mesh, FrameMaterial);
        return mesh;
    }

    // ---------- Carga: predicción y dibujo por instancias ----------

    private ItemBatch BatchOf(string item)
    {
        if (_batches.TryGetValue(item, out var b)) return b;
        var mesh = ItemVisuals.MeshOf(item);
        var aabb = mesh.GetAabb();
        var size = Mathf.Max(aabb.Size.X, aabb.Size.Z);
        var scale = size > MaxItemSize ? MaxItemSize / size : 1f;
        b = new ItemBatch
        {
            Multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = 0 },
            Lift = -aabb.Position.Y * scale + 0.015f,
            Scale = scale,
        };
        b.Node = new MultiMeshInstance3D { Multimesh = b.Multi, CustomAabb = new Aabb(new Vector3(-2000, -50, -2000), new Vector3(4000, 200, 4000)) };
        AddChild(b.Node);
        _batches[item] = b;
        return b;
    }

    private static VisualItem[] _reconciled = Array.Empty<VisualItem>();

    // Pasar una cadena crea en cada llamada un StringName con finalizador, y esto corre por tramo y frame.
    private static readonly StringName OffsetParam = "offset", PulseParam = "pulse", JamParam = "jam";

    /// <summary>Corrige la copia local con el estado real, conservando el objeto ya dibujado si es razonablemente el mismo.</summary>
    private static void Reconcile(BeltVisual v, Link l, World w)
    {
        v.Version = l.SlotsVersion;
        v.Blocked = w.BeltStatusOf(l) is BeltStatus.BlockedFull or BeltStatus.BlockedNoPower;
        if (v.Items.Length != l.Slots.Length) v.Items = new VisualItem[l.Slots.Length];
        var old = v.Items;
        var oldIndex = v.Count - 1;
        if (_reconciled.Length < l.Slots.Length) _reconciled = new VisualItem[l.Slots.Length];
        var result = _reconciled;
        var count = 0;
        // De la cabeza hacia la cola, emparejando por tipo y cercanía; cada objeto dibujado se usa una sola vez.
        for (var slot = l.Slots.Length - 1; slot >= 0; slot--)
        {
            if (l.Slots[slot] is not { } item) continue;
            var match = -1;
            for (var i = oldIndex; i >= 0; i--)
            {
                var gap = old[i].Pos - slot;
                if (gap < -6f) break;
                if (gap > 6f) continue;
                if (old[i].Item == item) { match = i; break; }
            }
            if (match >= 0)
            {
                result[count++] = new VisualItem { Item = item, Pos = old[match].Pos, Err = slot - old[match].Pos, Seed = old[match].Seed };
                oldIndex = match - 1;
            }
            else
            {
                result[count++] = new VisualItem { Item = item, Pos = slot, Err = 0, Seed = Mathf.Sin((slot + l.SlotsVersion) * 12.9898f) };
            }
        }
        // Se guarda de cola a cabeza para avanzar desde el final del arreglo.
        Array.Reverse(result, 0, count);
        Array.Copy(result, v.Items, count);
        v.Count = count;
    }

    /// <summary>Avanza la copia local con la misma regla que el servidor: un hueco de separación y cabeza detenida si está atascada.</summary>
    private static void Predict(BeltVisual v, float hops, float delta, int slots)
    {
        var items = v.Items;
        var advance = hops * delta;
        var limit = v.Blocked ? slots - 1f : float.MaxValue;
        for (var i = v.Count - 1; i >= 0; i--)
        {
            ref var it = ref items[i];
            var correction = it.Err * Mathf.Min(1f, delta * 5f);
            it.Err -= correction;
            it.Pos = Mathf.Min(it.Pos + advance + correction, limit);
            limit = it.Pos - 1f;
        }
        // Lo que rebasa el final ya ha entrado en la máquina.
        while (v.Count > 0 && items[v.Count - 1].Pos > slots - 0.5f)
            v.Count--;
    }

    /// <summary>Por frame: anima las cintas cercanas y escribe los búferes de instancias.</summary>
    public void Process(World w, Vector3 player, float delta)
    {
        foreach (var b in _batches.Values) b.Count = 0;
        var hops = w.BeltHopsPerSecond();
        var drawn = 0;
        foreach (var v in _belts.Values)
        {
            if (!w.Links.TryGetValue(v.Id, out var l)) continue;
            var dx = v.Center.X - player.X;
            var dz = v.Center.Z - player.Z;
            var distance = Mathf.Sqrt(dx * dx + dz * dz) - v.Radius;
            if (distance > ItemRange)
            {
                // Lejos no se dibuja, ni se predice ni se reconcilia: al acercarse parte del último estado real.
                v.Count = 0;
                v.Version = -1;
                continue;
            }
            if (v.Version != l.SlotsVersion) Reconcile(v, l, w);
            if (!v.Blocked) v.Offset += hops * SlotLength * delta;
            if (v.Pulse >= 0)
            {
                // Destello que recorre la cinta recién conectada, de la máquina de origen a la de destino.
                v.Pulse = Scenery.Calm || v.Pulse > v.Length + 3f ? -1 : v.Pulse + delta * Mathf.Max(14f, v.Length / 0.8f);
                foreach (var (node, _, _) in v.Chunks) node.SetInstanceShaderParameter(PulseParam, v.Pulse < 0 ? -100f : v.Pulse);
            }
            var jam = v.Blocked ? 1f : 0f;
            var jamChanged = jam != v.ShownJam;
            v.ShownJam = jam;
            foreach (var (node, center, radius) in v.Chunks)
            {
                if (jamChanged) node.SetInstanceShaderParameter(JamParam, jam);
                if (center.DistanceSquaredTo(player with { Y = center.Y }) < (AnimRange + radius) * (AnimRange + radius))
                    node.SetInstanceShaderParameter(OffsetParam, v.Offset);
            }
            Predict(v, hops, delta, l.Slots.Length);
            for (var i = 0; i < v.Count; i++)
            {
                if (Place(v, ref v.Items[i], player)) drawn++;
            }
        }
        DrawnItems = drawn;
        foreach (var (item, at) in _stock) WriteResting(BatchOf(item), at);
        foreach (var b in _batches.Values)
        {
            if (b.Count == 0 && b.Shown == 0) continue;
            b.Multi.Buffer = b.Buffer;
            b.Multi.VisibleInstanceCount = b.Count;
            b.Shown = b.Count;
        }
    }

    private readonly List<(string item, Vector3 at)> _stock = new();

    /// <summary>Existencias a la vista junto a las bocas de las máquinas cercanas. Aprovecha los mismos lotes de la carga: no añade dibujos por máquina.</summary>
    public List<(string item, Vector3 at)> VisibleStock => _stock;

    private static void WriteResting(ItemBatch b, Vector3 at)
    {
        if (b.Count >= b.Multi.InstanceCount)
        {
            var capacity = Mathf.Max(64, b.Multi.InstanceCount * 2);
            b.Multi.InstanceCount = capacity;
            Array.Resize(ref b.Buffer, capacity * 12);
        }
        var o = b.Count * 12;
        var s = b.Scale;
        var buffer = b.Buffer;
        buffer[o] = s; buffer[o + 1] = 0; buffer[o + 2] = 0; buffer[o + 3] = at.X;
        buffer[o + 4] = 0; buffer[o + 5] = s; buffer[o + 6] = 0; buffer[o + 7] = at.Y + b.Lift;
        buffer[o + 8] = 0; buffer[o + 9] = 0; buffer[o + 10] = s; buffer[o + 11] = at.Z;
        b.Count++;
    }

    private bool Place(BeltVisual v, ref VisualItem item, Vector3 player)
    {
        var at = v.Points[Mathf.Clamp((int)item.Pos, 0, v.Points.Length - 1)];
        var ax = at.X - player.X;
        var az = at.Z - player.Z;
        if (ax * ax + az * az > ItemRange * ItemRange) return false; // cinta larga: solo se dibuja la parte cercana
        var b = BatchOf(item.Item);
        if (b.Count >= b.Multi.InstanceCount)
        {
            var capacity = Mathf.Max(64, b.Multi.InstanceCount * 2);
            b.Multi.InstanceCount = capacity;
            Array.Resize(ref b.Buffer, capacity * 12);
        }
        // Pos es el índice del hueco; el objeto va en su centro.
        var s = Mathf.Clamp(item.Pos + 0.5f, 0f, v.Points.Length - 1.001f);
        var i = (int)s;
        var a = v.Points[i];
        var c = v.Points[i + 1];
        var p = a.Lerp(c, s - i);
        var t = (c - a).Normalized();
        var o = b.Count * 12;
        var scale = b.Scale;
        // Base que mira en el sentido de avance: X lateral, Y arriba, Z hacia atrás.
        var zx = -t.X;
        var zz = -t.Z;
        var lenXz = Mathf.Sqrt(zx * zx + zz * zz);
        if (lenXz < 1e-4f) { zx = 0; zz = 1; lenXz = 1; }
        zx /= lenXz;
        zz /= lenXz;
        // Desvío y giro propios del objeto, sin salirse de la banda: aproximación de primer orden, sin trigonometría por objeto.
        var side = item.Seed * 0.09f;
        p.X += zz * side;
        p.Z += -zx * side;
        var turn = item.Seed * 2.7f % 0.32f;
        (zx, zz) = (zx + zz * turn, zz - zx * turn);
        var norm = 1f / Mathf.Sqrt(1f + turn * turn);
        zx *= norm;
        zz *= norm;
        var buffer = b.Buffer;
        buffer[o] = zz * scale; buffer[o + 1] = 0; buffer[o + 2] = zx * scale; buffer[o + 3] = p.X;
        buffer[o + 4] = 0; buffer[o + 5] = scale; buffer[o + 6] = 0; buffer[o + 7] = p.Y + b.Lift;
        buffer[o + 8] = -zx * scale; buffer[o + 9] = 0; buffer[o + 10] = zz * scale; buffer[o + 11] = p.Z;
        b.Count++;
        return true;
    }

    // ---------- Apuntado e inspección ----------

    /// <summary>Cinta bajo el rayo, sin física: plano de la banda, plano de los puentes y segmentos elevados.</summary>
    public int Pick(World w, Vector3 from, Vector3 dir, float maxDistance, out float distance)
    {
        var nearest = float.MaxValue;
        var best = -1;
        void Consider(float y, Func<int, int, int> lookup)
        {
            if (dir.Y >= -0.01f) return;
            var t = (y - from.Y) / dir.Y;
            var guess = from + dir * t;
            t = (y + GroundHeight(guess.X, guess.Z) - from.Y) / dir.Y;
            if (t < 0 || t > maxDistance || t >= nearest) return;
            var p = from + dir * t;
            var x = Mathf.FloorToInt(p.X / Cell);
            var z = Mathf.FloorToInt(p.Z / Cell);
            var id = lookup(x, z);
            if (id < 0) return;
            best = id;
            nearest = t;
        }
        Consider(BridgeY, (x, z) => w.BridgeAt(_planet, x, z));
        Consider(BandY, (x, z) => w.BeltAt(_planet, x, z));
        foreach (var id in _elevated)
        {
            if (!_belts.TryGetValue(id, out var v)) continue;
            var closest = Geometry3D.GetClosestPointsBetweenSegments(from, from + dir * maxDistance, v.Points[0], v.Points[^1]);
            if (closest[0].DistanceSquaredTo(closest[1]) > 0.9f * 0.9f) continue;
            var t = closest[0].DistanceTo(from);
            if (t >= nearest) continue;
            best = id;
            nearest = t;
        }
        distance = nearest;
        return best;
    }

    /// <summary>Punto de las cintas con carga en marcha más cercanas: solo para situar su sonido. Hasta dos, por tramo.</summary>
    public IReadOnlyList<Vector3> NearestMoving(Vector3 player)
    {
        _moving.Clear();
        float best0 = float.MaxValue, best1 = float.MaxValue;
        Vector3 at0 = default, at1 = default;
        foreach (var v in _belts.Values)
        {
            if (v.Blocked || v.Count == 0) continue;
            foreach (var (_, center, radius) in v.Chunks)
            {
                var d = center.DistanceSquaredTo(player) - radius * radius;
                if (d < best0) { (best1, at1) = (best0, at0); (best0, at0) = (d, center); }
                else if (d < best1) { (best1, at1) = (d, center); }
            }
        }
        if (best0 < float.MaxValue) _moving.Add(at0);
        if (best1 < float.MaxValue) _moving.Add(at1);
        return _moving;
    }

    private readonly List<Vector3> _moving = new();

    /// <summary>Punto medio de la banda; para apuntar desde las pruebas.</summary>
    public Vector3 CenterOfBelt(int id) => _belts.TryGetValue(id, out var v) ? v.Points[v.Points.Length / 2] : Vector3.Zero;

    /// <summary>Texto de inspección: lee de World, no de la copia visual.</summary>
    public static string Describe(World w, int id, out BeltStatus status)
    {
        status = BeltStatus.Empty;
        if (w.InspectBelt(id) is not { } info) return "";
        status = info.Status;
        var title = Loc.T(info.Elevated ? "belt_elevated" : "belt_title");
        var from = w.Machines.TryGetValue(info.From, out var a) ? Loc.T("machine_" + a.Def) : "?";
        var to = w.Machines.TryGetValue(info.To, out var b) ? Loc.T("machine_" + b.Def) : "?";
        var items = info.Items.Count == 0 ? "" : string.Join(", ", info.Items.Select(kv => $"{Loc.T("item_" + kv.Key)} ×{kv.Value}")) + " · ";
        var load = Loc.T("belt_load", info.Load.ToString()).Replace("{1}", info.Capacity.ToString()).Replace("{2}", info.ItemsPerMinute.ToString());
        return $"{title}: {from} → {to} — {Loc.T("belt_status_" + info.Status)} · {items}{load}";
    }
}
