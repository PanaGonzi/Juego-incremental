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
    private const float ElevatedY = 3.7f;    // cintas migradas sin ruta: sobre las máquinas
    private const float HalfWidth = 0.5f;
    private const float SlotLength = Cell / 4f;
    private const float MaxItemSize = 0.42f;

    /// <summary>A partir de aquí la carga se omite: el estado económico sigue en el mundo y al apuntar se informa igual.</summary>
    private const float ItemRange = 120f;
    private const float AnimRange = 90f;

    private static readonly ShaderMaterial BandMaterial = new() { Shader = new Shader { Code = """
        shader_type spatial;
        instance uniform float offset = 0.0;
        instance uniform float jam = 0.0;
        void fragment(){
          float x = abs(UV.x - 0.5) * 2.0;
          float along = UV.y - offset;
          float c = fract(along * 0.5 + x * 0.16);
          float chevron = smoothstep(0.0, 0.012, c) * (1.0 - smoothstep(0.06, 0.072, c)) * (1.0 - smoothstep(0.5, 0.58, x));
          float ribs = step(0.5, fract(along * 7.0)) * 0.012;
          vec3 rubber = vec3(0.05, 0.05, 0.055) + ribs;
          vec3 mark = mix(vec3(0.85, 0.56, 0.14), vec3(0.95, 0.14, 0.08), jam);
          ALBEDO = mix(rubber, mark, chevron * 0.75);
          ROUGHNESS = 0.82;
          METALLIC = 0.0;
          EMISSION = mark * chevron * (0.06 + jam * 0.45);
        }
        """ } };

    private static readonly StandardMaterial3D FrameMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        Roughness = 0.55f,
        Metallic = 0.5f,
    };

    private static readonly Color Steel = new(0.34f, 0.36f, 0.39f);
    private static readonly Color DarkSteel = new(0.17f, 0.18f, 0.2f);
    private static readonly Color Support = new(0.72f, 0.56f, 0.16f);

    private struct VisualItem
    {
        public string Item;
        public float Pos;
        public float Err;
    }

    private sealed class BeltVisual
    {
        public int Id;
        public string Key = "";
        public MeshInstance3D Mesh = null!;
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

    public int DrawnItems { get; private set; }
    public int DrawnBelts => _belts.Count;

    // ---------- Sincronización de estructura ----------

    /// <summary>Se llama al cambiar la estructura o el planeta. Solo rehace las mallas de cintas que cambiaron.</summary>
    public void Sync(World w, int planet, int structuralStamp)
    {
        if (planet == _planet && structuralStamp == _stamp) return;
        if (planet != _planet) Clear();
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
            if (v != null) v.Mesh.QueueFree();
            _belts[l.Id] = Build(w, l, key);
        }
        foreach (var id in _belts.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            _belts[id].Mesh.QueueFree();
            _belts.Remove(id);
        }
    }

    public void Clear()
    {
        foreach (var v in _belts.Values) v.Mesh.QueueFree();
        _belts.Clear();
        _elevated.Clear();
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
        var mesh = BuildMesh(points, l.Elevated, l.Elevated ? null : l, w);
        var node = new MeshInstance3D
        {
            Mesh = mesh,
            VisibilityRangeEnd = 190f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(node);
        var min = points[0];
        var max = points[0];
        foreach (var p in points)
        {
            min = new Vector3(Mathf.Min(min.X, p.X), 0, Mathf.Min(min.Z, p.Z));
            max = new Vector3(Mathf.Max(max.X, p.X), 0, Mathf.Max(max.Z, p.Z));
        }
        return new BeltVisual
        {
            Id = l.Id,
            Key = key,
            Mesh = node,
            Points = points,
            Center = (min + max) / 2,
            Radius = (max - min).Length() / 2,
            Elevated = l.Elevated,
            Items = new VisualItem[l.Slots.Length],
        };
    }

    // ---------- Trayectoria ----------

    /// <summary>Puntos en los límites de hueco: el índice i es el inicio del hueco i. Curvas por celda con Bézier cuadrática.</summary>
    private static Vector3[] RoutePath(World w, int from, int to, IReadOnlyList<(int x, int z)> cells, Func<int, bool> bridge)
    {
        var slotsPerCell = w.Content.Balance.BeltSlotsPerCell;
        var points = new Vector3[cells.Count * slotsPerCell + 1];
        var a = w.Machines[from];
        var b = w.Machines[to];
        var entry = PortSide(w.BeltPorts(a, true), cells[0], cells.Count > 1 ? cells[1] : null);
        var exit = PortSide(w.BeltPorts(b, false), cells[^1], cells.Count > 1 ? cells[^2] : null);

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
        return points;
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
        var a = new Vector3(ax / 1000f, ElevatedY, az / 1000f);
        var b = new Vector3(bx / 1000f, ElevatedY, bz / 1000f);
        var points = new Vector3[l.Slots.Length + 1];
        for (var i = 0; i < points.Length; i++) points[i] = a.Lerp(b, i / (float)l.Slots.Length);
        return points;
    }

    // ---------- Geometría ----------

    private sealed class Geometry
    {
        public readonly List<Vector3> V = new();
        public readonly List<Vector3> N = new();
        public readonly List<Vector2> Uv = new();
        public readonly List<Color> C = new();
        public readonly List<int> I = new();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Color color, Vector2? uvA = null, Vector2? uvB = null, Vector2? uvC = null, Vector2? uvD = null)
        {
            var start = V.Count;
            V.Add(a); V.Add(b); V.Add(c); V.Add(d);
            for (var i = 0; i < 4; i++) { N.Add(n); C.Add(color); }
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

        public void Commit(ArrayMesh mesh, Material material)
        {
            if (V.Count == 0) return;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = V.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = N.ToArray();
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

    private static ArrayMesh BuildMesh(Vector3[] p, bool elevated, Link? link, World w)
    {
        var band = new Geometry();
        var frame = new Geometry();
        var along = 0f;
        for (var i = 0; i < p.Length - 1; i++)
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
        for (var i = 0; i < p.Length; i += 2)
        {
            var t = Tangent(p, i);
            var s = Side(t);
            var up = s.Cross(t).Normalized();
            if (up.Y < 0) up = -up;
            frame.Box(p[i] + Vector3.Down * 0.19f, new Vector3(HalfWidth + 0.05f, 0.05f, 0.05f), s, up, t, DarkSteel);
        }
        foreach (var i in new[] { 0, p.Length - 1 })
        {
            var t = Tangent(p, i);
            var s = Side(t);
            frame.Box(p[i] + Vector3.Down * 0.05f, new Vector3(HalfWidth + 0.1f, 0.13f, 0.13f), s, Vector3.Up, t, Support);
        }
        var step = elevated ? 16 : 4;
        for (var i = elevated ? 8 : 2; i < p.Length - 1; i += step)
        {
            var t = Tangent(p, i);
            var s = Side(t);
            var top = p[i].Y - 0.16f;
            var legHalf = top / 2;
            var bridgeLeg = p[i].Y > BandY + 0.3f;
            var spread = HalfWidth + 0.02f + (elevated ? 0.12f : 0f);
            var width = elevated ? 0.11f : 0.05f;
            foreach (var sign in new[] { -1f, 1f })
            {
                var foot = p[i] + s * sign * spread;
                foot.Y = 0;
                frame.Box(foot + Vector3.Up * legHalf, new Vector3(width, legHalf, width), s, Vector3.Up, t, bridgeLeg || elevated ? Support : Steel);
                frame.Box(foot + Vector3.Up * 0.02f, new Vector3(width + 0.09f, 0.02f, width + 0.09f), s, Vector3.Up, t, DarkSteel);
            }
            if (bridgeLeg || elevated)
            {
                var cross = p[i];
                cross.Y = top * 0.55f;
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

    /// <summary>Corrige la copia local con el estado real, conservando el objeto ya dibujado si es razonablemente el mismo.</summary>
    private static void Reconcile(BeltVisual v, Link l, World w)
    {
        v.Version = l.SlotsVersion;
        v.Blocked = w.BeltStatusOf(l) is BeltStatus.BlockedFull or BeltStatus.BlockedNoPower;
        if (v.Items.Length != l.Slots.Length) v.Items = new VisualItem[l.Slots.Length];
        var old = v.Items;
        var oldIndex = v.Count - 1;
        var result = new VisualItem[l.Slots.Length];
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
                result[count++] = new VisualItem { Item = item, Pos = old[match].Pos, Err = slot - old[match].Pos };
                oldIndex = match - 1;
            }
            else
            {
                result[count++] = new VisualItem { Item = item, Pos = slot, Err = 0 };
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
            if (v.Version != l.SlotsVersion) Reconcile(v, l, w);
            if (distance > ItemRange)
            {
                // Lejos no se dibuja ni predice: al acercarse parte del último estado real.
                v.Count = 0;
                v.Version = -1;
                continue;
            }
            if (!v.Blocked) v.Offset += hops * SlotLength * delta;
            if (distance < AnimRange)
            {
                v.Mesh.SetInstanceShaderParameter("offset", v.Offset);
                var jam = v.Blocked ? 1f : 0f;
                if (jam != v.ShownJam)
                {
                    v.ShownJam = jam;
                    v.Mesh.SetInstanceShaderParameter("jam", jam);
                }
            }
            Predict(v, hops, delta, l.Slots.Length);
            for (var i = 0; i < v.Count; i++)
            {
                Place(v, ref v.Items[i]);
                drawn++;
            }
        }
        DrawnItems = drawn;
        foreach (var b in _batches.Values)
        {
            if (b.Count == 0 && b.Shown == 0) continue;
            b.Multi.Buffer = b.Buffer;
            b.Multi.VisibleInstanceCount = b.Count;
            b.Shown = b.Count;
        }
    }

    private void Place(BeltVisual v, ref VisualItem item)
    {
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
        var buffer = b.Buffer;
        buffer[o] = zz * scale; buffer[o + 1] = 0; buffer[o + 2] = zx * scale; buffer[o + 3] = p.X;
        buffer[o + 4] = 0; buffer[o + 5] = scale; buffer[o + 6] = 0; buffer[o + 7] = p.Y + b.Lift;
        buffer[o + 8] = -zx * scale; buffer[o + 9] = 0; buffer[o + 10] = zz * scale; buffer[o + 11] = p.Z;
        b.Count++;
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

    // ---------- Previsualización y puertos ----------

    private MeshInstance3D? _preview;
    private MeshInstance3D? _ports;
    private string _previewKey = "";
    private string _portsKey = "";
    private BeltRoute? _route;
    private Vector3[]? _previewPoints;

    private static readonly StandardMaterial3D PreviewOk = PreviewMaterial(new Color(0.3f, 1f, 0.55f, 0.55f));
    private static readonly StandardMaterial3D PreviewBad = PreviewMaterial(new Color(1f, 0.3f, 0.2f, 0.55f));

    private static StandardMaterial3D PreviewMaterial(Color c) => new()
    {
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private static readonly StandardMaterial3D PortMaterial = new()
    {
        VertexColorUseAsAlbedo = true,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = false,
    };

    /// <summary>
    /// Ruta entre el origen elegido y la máquina apuntada, calculada con la misma función que usa el servidor.
    /// Solo se recalcula si cambia el mundo o el destino. Devuelve la clave del error, o null si se puede tender.
    /// </summary>
    public string? UpdatePreview(World w, PlayerState me, int from, int to, int structuralStamp, ref string status)
    {
        var key = $"{from}>{to}#{structuralStamp}";
        if (key != _previewKey)
        {
            _previewKey = key;
            _route = w.RouteBelt(from, to);
            _preview?.QueueFree();
            _preview = null;
            _previewPoints = null;
            if (_route.Error == null && _route.Cells.Count > 0)
                _previewPoints = RoutePath(w, from, to, _route.Cells, _ => false);
            else if (w.Machines.TryGetValue(from, out var a) && w.Machines.TryGetValue(to, out var b))
                _previewPoints = StraightPreview(w, a, b);
            if (_previewPoints != null)
            {
                _preview = new MeshInstance3D { Mesh = PreviewMesh(_previewPoints), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                AddChild(_preview);
            }
        }
        var error = _route!.Error;
        if (error == null && !w.CanAfford(me, _route.Credits, new Dictionary<string, long>())) error = "err_cost";
        if (_preview != null) _preview.MaterialOverride = error == null ? PreviewOk : PreviewBad;
        if (_route.Error == null)
            status = Loc.T("belt_preview", _route.Cells.Count.ToString()).Replace("{1}", _route.Credits.ToString());
        if (error != null) status = Loc.T(error);
        return error;
    }

    private static Vector3[] StraightPreview(World w, Machine a, Machine b)
    {
        var (ax, az) = w.MachineCenterMm(a);
        var (bx, bz) = w.MachineCenterMm(b);
        return new[] { new Vector3(ax / 1000f, 1.1f, az / 1000f), new Vector3(bx / 1000f, 1.1f, bz / 1000f) };
    }

    private static ArrayMesh PreviewMesh(Vector3[] p)
    {
        var g = new Geometry();
        for (var i = 0; i < p.Length - 1; i++)
        {
            var s0 = Side(Tangent(p, i));
            var s1 = Side(Tangent(p, i + 1));
            g.Quad(p[i] - s0 * HalfWidth, p[i] + s0 * HalfWidth, p[i + 1] + s1 * HalfWidth, p[i + 1] - s1 * HalfWidth, Vector3.Up, Colors.White);
        }
        // Flechas de sentido sobre la ruta.
        for (var i = 2; i + 2 < p.Length; i += 4)
        {
            var t = (p[i + 1] - p[i]).Normalized();
            var s = Side(t);
            var tip = p[i] + t * 0.42f + Vector3.Up * 0.03f;
            var back = p[i] - t * 0.1f + Vector3.Up * 0.03f;
            g.Quad(back - s * 0.3f, tip, tip, back + s * 0.3f, Vector3.Up, Colors.White);
        }
        var mesh = new ArrayMesh();
        g.Commit(mesh, PreviewOk);
        return mesh;
    }

    /// <summary>Marca los puertos de una máquina: salida ámbar, entrada azul; atenuados si están ocupados.</summary>
    public void ShowPorts(World w, Machine? m, int structuralStamp)
    {
        var key = m == null ? "" : $"{m.Id}|{m.X}|{m.Z}|{m.Rot}#{structuralStamp}";
        if (key == _portsKey) return;
        _portsKey = key;
        _ports?.QueueFree();
        _ports = null;
        if (m == null) return;
        var g = new Geometry();
        foreach (var output in new[] { true, false })
            foreach (var p in w.BeltPorts(m, output))
                PortArrow(g, p);
        if (g.V.Count == 0) return;
        var mesh = new ArrayMesh();
        g.Commit(mesh, PortMaterial);
        _ports = new MeshInstance3D { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ports);
    }

    private static void PortArrow(Geometry g, BeltPort p)
    {
        var (dx, dz) = World.BeltDirs[p.Dir];
        var outward = new Vector3(dx, 0, dz);
        var dir = p.Output ? outward : -outward;
        var side = dir.Cross(Vector3.Up);
        var center = new Vector3((p.X + 0.5f) * Cell, 0.07f + (p.Output ? 0.01f : 0f), (p.Z + 0.5f) * Cell) - outward * (p.Output ? 0.45f : 0.2f);
        var color = p.Output ? new Color(1f, 0.66f, 0.15f, 0.9f) : new Color(0.25f, 0.7f, 1f, p.Side ? 0.55f : 0.9f);
        if (!p.Free) color = new Color(0.45f, 0.45f, 0.45f, 0.45f);
        var tip = center + dir * 0.45f;
        var tail = center - dir * 0.3f;
        g.Quad(tail - side * 0.16f, tail + side * 0.16f, center + side * 0.16f, center - side * 0.16f, Vector3.Up, color);
        g.Quad(center - side * 0.42f, center + side * 0.42f, tip, tip, Vector3.Up, color);
    }

    public void HidePreview()
    {
        _preview?.QueueFree();
        _preview = null;
        _previewKey = "";
        _route = null;
        _previewPoints = null;
        _ports?.QueueFree();
        _ports = null;
        _portsKey = "";
    }

    /// <summary>Oculta solo la ruta (al dejar de apuntar a un destino), sin perder las marcas de puertos.</summary>
    public void HideRoute()
    {
        _preview?.QueueFree();
        _preview = null;
        _previewKey = "";
        _route = null;
    }
}
