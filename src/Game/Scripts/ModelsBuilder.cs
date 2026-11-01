using Godot;

namespace Pluton.Game;

public static partial class Models
{
    /// <summary>
    /// Geometría procedimental con biseles, tangentes y atributos de material por vértice.
    /// Las UV van en metros: la densidad de textura es la misma en todas las piezas.
    /// </summary>
    public sealed class Builder
    {
        private sealed class Part
        {
            public readonly List<Vector3> V = new(), N = new();
            public readonly List<float> T = new();
            public readonly List<Color> C = new();
            public readonly List<Vector2> Uv = new(), Uv2 = new();
            public readonly List<int> I = new();
        }

        private readonly Dictionary<Materials.Pass, Part> _parts = new();
        private Materials.Surface _s;
        private Part _p = null!;
        private Vector2 _attr;
        private float _tile = 1;

        /// <summary>Deformación opcional de las siguientes piezas: abolladuras, chapa retorcida.</summary>
        public Func<Vector3, Vector3>? Warp;

        public int Triangles => _parts.Values.Sum(p => p.I.Count) / 3;

        private void Use(string mat)
        {
            _s = Materials.Spec(mat);
            if (!_parts.TryGetValue(_s.Pass, out var part))
            {
                part = new Part();
                _parts[_s.Pass] = part;
            }
            _p = part;
            var layer = (int)_s.Layer + 0.02f + 0.96f * Mathf.Clamp(_s.Metal, 0, 1);
            var emit = Mathf.Floor(_s.Emission * 4) + 0.02f + 0.96f * Mathf.Clamp(_s.Rough, 0, 1);
            _attr = new Vector2(layer, emit);
            _tile = 1f / Materials.TileOf(_s.Layer);
        }

        private int Vert(Transform3D tf, Vector3 pos, Vector3 normal, Vector3 tangent, Vector3 vdir, Vector2 uvMetres, float wear)
        {
            var world = tf * pos;
            if (Warp != null) world = Warp(world);
            var n = (tf.Basis * normal).Normalized();
            var t = (tf.Basis * tangent).Normalized();
            var v = tf.Basis * vdir;
            // Godot reconstruye la binormal con cross(N,T)*w; el verde del mapa apunta hacia V decreciente.
            var w = n.Cross(t).Dot(v) > 0 ? -1f : 1f;
            _p.V.Add(world);
            _p.N.Add(n);
            _p.T.Add(t.X); _p.T.Add(t.Y); _p.T.Add(t.Z); _p.T.Add(w);
            var a = _s.Pass == Materials.Pass.Opaque ? Mathf.Max(wear, _s.Wear) : _s.Tint.A;
            _p.C.Add(new Color(_s.Tint.R, _s.Tint.G, _s.Tint.B, a));
            _p.Uv.Add(uvMetres * _tile);
            _p.Uv2.Add(_attr);
            return _p.V.Count - 1;
        }

        /// <summary>Triángulo orientado solo: el orden de Godot es horario visto desde fuera.</summary>
        private void Tri(int a, int b, int c)
        {
            var v = _p.V;
            var geo = (v[b] - v[a]).Cross(v[c] - v[a]);
            if (geo.LengthSquared() < 1e-14f) return;
            var avg = _p.N[a] + _p.N[b] + _p.N[c];
            if (geo.Dot(avg) > 0) { _p.I.Add(a); _p.I.Add(c); _p.I.Add(b); }
            else { _p.I.Add(a); _p.I.Add(b); _p.I.Add(c); }
        }

        private void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }

        private static Transform3D Tf(float x, float y, float z, float yawDeg = 0, float pitchDeg = 0, float rollDeg = 0)
        {
            var basis = Basis.FromEuler(new Vector3(Mathf.DegToRad(pitchDeg), Mathf.DegToRad(yawDeg), Mathf.DegToRad(rollDeg)));
            return new Transform3D(basis, new Vector3(x, y, z));
        }

        private static readonly Vector3[] Axes = { Vector3.Right, Vector3.Up, Vector3.Back };

        private static float Comp(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

        /// <summary>Caja biselada. Las aristas llevan máscara de desgaste y normales suavizadas: atrapan la luz.</summary>
        public Builder Box(string mat, float x, float y, float z, float w, float h, float d, float yaw = 0, float pitch = 0, float roll = 0, float bevel = -1)
        {
            Use(mat);
            BoxAt(Tf(x, y, z, yaw, pitch, roll), new Vector3(w, h, d) / 2, bevel, new Vector2(x + z * 0.37f, y * 0.71f + z * 0.53f));
            return this;
        }

        private void BoxAt(Transform3D tf, Vector3 half, float bevel, Vector2 uvOffset)
        {
            var minSide = Mathf.Min(half.X, Mathf.Min(half.Y, half.Z)) * 2;
            var b = bevel >= 0 ? bevel : Mathf.Min(0.035f, minSide * 0.22f);
            if (minSide < 0.03f) b = 0;
            b = Mathf.Min(b, minSide * 0.45f);
            var inner = new Vector3(half.X - b, half.Y - b, half.Z - b);

            // Caras.
            for (var axis = 0; axis < 3; axis++)
            {
                var ua = (axis + 1) % 3;
                var va = (axis + 2) % 3;
                // Las texturas con dirección (estratos, peligro) leen mejor con V hacia abajo en las caras verticales.
                if (axis != 1 && ua == 1) (ua, va) = (va, ua);
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    var n = Axes[axis] * sign;
                    var u = Axes[ua];
                    var v = Axes[va];
                    var hu = Comp(inner, ua);
                    var hv = Comp(inner, va);
                    var c = n * Comp(half, axis);
                    int V(float su, float sv)
                    {
                        var p = c + u * (su * hu) + v * (sv * hv);
                        return Vert(tf, p, n, u, v, new Vector2(Comp(p, ua), Comp(p, va)) + uvOffset, 0);
                    }
                    Quad(V(-1, -1), V(1, -1), V(1, 1), V(-1, 1));
                }
            }
            if (b <= 0) return;

            // Aristas.
            for (var c = 0; c < 3; c++)
            {
                var a0 = (c + 1) % 3;
                var a1 = (c + 2) % 3;
                var ca = Axes[c];
                for (var s0 = -1; s0 <= 1; s0 += 2)
                for (var s1 = -1; s1 <= 1; s1 += 2)
                {
                    var n0 = Axes[a0] * s0;
                    var n1 = Axes[a1] * s1;
                    int V(int end, bool first)
                    {
                        var p = ca * (end * Comp(inner, c))
                            + n0 * (first ? Comp(half, a0) : Comp(inner, a0))
                            + n1 * (first ? Comp(inner, a1) : Comp(half, a1));
                        var n = first ? n0 : n1;
                        var t = ca;
                        var vd = n.Cross(t);
                        return Vert(tf, p, n, t, vd, new Vector2(Comp(p, c), Comp(p, a0) + Comp(p, a1)) + uvOffset, 1);
                    }
                    Quad(V(-1, true), V(1, true), V(1, false), V(-1, false));
                }
            }

            // Esquinas.
            for (var sx = -1; sx <= 1; sx += 2)
            for (var sy = -1; sy <= 1; sy += 2)
            for (var sz = -1; sz <= 1; sz += 2)
            {
                int V(int axis)
                {
                    var p = new Vector3(sx * (axis == 0 ? half.X : inner.X), sy * (axis == 1 ? half.Y : inner.Y), sz * (axis == 2 ? half.Z : inner.Z));
                    var n = axis == 0 ? new Vector3(sx, 0, 0) : axis == 1 ? new Vector3(0, sy, 0) : new Vector3(0, 0, sz);
                    var t = axis == 0 ? Vector3.Up : Vector3.Right;
                    return Vert(tf, p, n, t, n.Cross(t), new Vector2(p.X + p.Z, p.Y) + uvOffset, 1);
                }
                Tri(V(0), V(1), V(2));
            }
        }

        /// <summary>Caja que une dos puntos: largueros, diagonales, barandillas.</summary>
        public Builder Beam(string mat, Vector3 a, Vector3 b, float w, float h)
        {
            Use(mat);
            var d = b - a;
            var length = d.Length();
            if (length < 0.001f) return this;
            var z = d / length;
            var x = (Mathf.Abs(z.Y) > 0.95f ? Vector3.Right : Vector3.Up).Cross(z).Normalized();
            var y = z.Cross(x);
            BoxAt(new Transform3D(new Basis(x, y, z), (a + b) / 2), new Vector3(w, h, length) / 2, -1, new Vector2(a.X + a.Z, a.Y));
            return this;
        }

        /// <summary>Tronco de pirámide: tolvas, capotas, bases inclinadas.</summary>
        public Builder Taper(string mat, float x, float y, float z, float wBottom, float dBottom, float wTop, float dTop, float h, float yaw = 0, float pitch = 0, float roll = 0)
        {
            Use(mat);
            var tf = Tf(x, y, z, yaw, pitch, roll);
            var bottom = new[] { new Vector3(-wBottom / 2, -h / 2, -dBottom / 2), new Vector3(wBottom / 2, -h / 2, -dBottom / 2), new Vector3(wBottom / 2, -h / 2, dBottom / 2), new Vector3(-wBottom / 2, -h / 2, dBottom / 2) };
            var top = new[] { new Vector3(-wTop / 2, h / 2, -dTop / 2), new Vector3(wTop / 2, h / 2, -dTop / 2), new Vector3(wTop / 2, h / 2, dTop / 2), new Vector3(-wTop / 2, h / 2, dTop / 2) };
            var uvBase = new Vector2(x, z);
            for (var i = 0; i < 4; i++)
            {
                var j = (i + 1) % 4;
                var n = (bottom[j] - bottom[i]).Cross(top[i] - bottom[i]).Normalized();
                if (n.Dot(bottom[i] + top[j]) < 0) n = -n;
                var t = (bottom[j] - bottom[i]).Normalized();
                var v = n.Cross(t);
                int V(Vector3 p, float wear) => Vert(tf, p, n, t, v, new Vector2(p.Dot(t), p.Dot(v)) + uvBase, wear);
                Quad(V(bottom[i], 0), V(bottom[j], 0), V(top[j], 0.6f), V(top[i], 0.6f));
            }
            foreach (var (ring, ny) in new[] { (top, 1f), (bottom, -1f) })
            {
                var n = new Vector3(0, ny, 0);
                int V(Vector3 p) => Vert(tf, p, n, Vector3.Right, Vector3.Back, new Vector2(p.X, p.Z) + uvBase, 0);
                Quad(V(ring[0]), V(ring[1]), V(ring[2]), V(ring[3]));
            }
            return this;
        }

        /// <summary>Superficie de revolución: el perfil va de abajo arriba como (radio, altura). Los ángulos marcados se conservan.</summary>
        public Builder Lathe(string mat, float x, float y, float z, (float r, float y)[] profile, int sides = 16, float pitch = 0, float roll = 0, float yaw = 0)
        {
            Use(mat);
            var tf = Tf(x, y, z, yaw, pitch, roll);
            var count = profile.Length - 1;
            var segN = new Vector2[count];
            var lengths = new float[count];
            for (var i = 0; i < count; i++)
            {
                var d = new Vector2(profile[i + 1].r - profile[i].r, profile[i + 1].y - profile[i].y);
                lengths[i] = d.Length();
                segN[i] = lengths[i] > 0 ? new Vector2(d.Y, -d.X) / lengths[i] : Vector2.Right;
            }
            var along = 0f;
            var circ = Mathf.Max(0.05f, profile.Max(p => p.r)) * Mathf.Tau;
            for (var i = 0; i < count; i++)
            {
                if (lengths[i] <= 0) continue;
                // Se suaviza con el vecino si el pliegue es leve; si no, arista viva.
                var n0 = i > 0 && lengths[i - 1] > 0 && segN[i - 1].Dot(segN[i]) > 0.55f ? (segN[i - 1] + segN[i]).Normalized() : segN[i];
                var n1 = i < count - 1 && lengths[i + 1] > 0 && segN[i + 1].Dot(segN[i]) > 0.55f ? (segN[i + 1] + segN[i]).Normalized() : segN[i];
                var wear = lengths[i] < 0.09f ? 1f : 0f;
                int prev0 = -1, prev1 = -1;
                for (var s = 0; s <= sides; s++)
                {
                    var a = s * Mathf.Tau / sides;
                    var c = Mathf.Cos(a);
                    var sn = Mathf.Sin(a);
                    var radial = new Vector3(c, 0, sn);
                    var tan = new Vector3(-sn, 0, c);
                    var n3a = (radial * n0.X + Vector3.Up * n0.Y).Normalized();
                    var n3b = (radial * n1.X + Vector3.Up * n1.Y).Normalized();
                    var u = s / (float)sides * circ;
                    var v0 = Vert(tf, radial * profile[i].r + Vector3.Up * profile[i].y, n3a, tan, -Vector3.Up, new Vector2(u, -along), wear);
                    var v1 = Vert(tf, radial * profile[i + 1].r + Vector3.Up * profile[i + 1].y, n3b, tan, -Vector3.Up, new Vector2(u, -along - lengths[i]), wear);
                    if (s > 0) Quad(prev0, v0, v1, prev1);
                    prev0 = v0;
                    prev1 = v1;
                }
                along += lengths[i];
            }
            return this;
        }

        public Builder Cyl(string mat, float x, float y, float z, float radius, float h, float pitch = 0, float roll = 0, int sides = 12, float? topRadius = null)
        {
            var rt = topRadius ?? radius;
            var b = Mathf.Min(0.03f, Mathf.Min(h * 0.2f, Mathf.Min(radius, Mathf.Max(rt, 0.001f)) * 0.3f));
            if (b < 0.004f) b = 0;
            var profile = b > 0
                ? new[] { (0f, -h / 2), (radius - b, -h / 2), (radius, -h / 2 + b), (rt, h / 2 - b), (rt - b, h / 2), (0f, h / 2) }
                : new[] { (0f, -h / 2), (radius, -h / 2), (rt, h / 2), (0f, h / 2) };
            return Lathe(mat, x, y, z, profile, sides, pitch, roll);
        }

        public Builder Ball(string mat, float x, float y, float z, float radius, int sides = 10, int rings = 6, float scaleY = 1f, float yaw = 0)
        {
            var profile = new (float r, float y)[rings + 1];
            for (var i = 0; i <= rings; i++)
            {
                var a = -Mathf.Pi / 2 + i * Mathf.Pi / rings;
                profile[i] = (Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * scaleY);
            }
            return Lathe(mat, x, y, z, profile, sides, 0, 0, yaw);
        }

        /// <summary>Tubo a lo largo de una polilínea con codos redondeados: tuberías, mangueras y cables.</summary>
        public Builder Tube(string mat, float radius, int sides, params Vector3[] path)
        {
            Use(mat);
            var pts = new List<Vector3> { path[0] };
            for (var i = 1; i < path.Length - 1; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                var c = path[i + 1];
                var fillet = Mathf.Min(Mathf.Min((b - a).Length(), (c - b).Length()) * 0.45f, radius * 3f);
                var p0 = b + (a - b).Normalized() * fillet;
                var p1 = b + (c - b).Normalized() * fillet;
                for (var k = 0; k <= 4; k++)
                {
                    var t = k / 4f;
                    pts.Add(p0.Lerp(b, t).Lerp(b.Lerp(p1, t), t));
                }
            }
            pts.Add(path[^1]);
            var up = Mathf.Abs((pts[1] - pts[0]).Normalized().Y) > 0.9f ? Vector3.Right : Vector3.Up;
            var dir = (pts[1] - pts[0]).Normalized();
            var side = up.Cross(dir).Normalized();
            var len = 0f;
            var tf = Transform3D.Identity;
            var last = new int[sides + 1];
            var circ = radius * Mathf.Tau;
            for (var i = 0; i < pts.Count; i++)
            {
                var nd = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).Normalized() : dir;
                var avg = (dir + nd).Normalized();
                side = (side - avg * side.Dot(avg)).Normalized();
                var bin = avg.Cross(side);
                var ring = new int[sides + 1];
                for (var s = 0; s <= sides; s++)
                {
                    var a = s * Mathf.Tau / sides;
                    var n = side * Mathf.Cos(a) + bin * Mathf.Sin(a);
                    ring[s] = Vert(tf, pts[i] + n * radius, n, avg, n.Cross(avg), new Vector2(len, s / (float)sides * circ), 0);
                    if (i > 0 && s > 0) Quad(last[s - 1], ring[s - 1], ring[s], last[s]);
                }
                last = ring;
                if (i < pts.Count - 1) len += (pts[i + 1] - pts[i]).Length();
                dir = nd;
            }
            return this;
        }

        /// <summary>Chapa delgada deformada: dos caras y borde irregular. Base de la chatarra.</summary>
        public Builder Sheet(string mat, Transform3D tf, float w, float d, int nw, int nd, Func<float, float, Vector3> shape, float thickness = 0.02f, Func<float, float>? rim = null)
        {
            Use(mat);
            Vector3 P(int i, int j)
            {
                var u = i / (float)nw;
                var v = j / (float)nd;
                var edge = rim?.Invoke(u) ?? 0;
                return shape((u - 0.5f) * w, (v - 0.5f) * (d - edge * 2) );
            }
            foreach (var sign in new[] { 1f, -1f })
            {
                var idx = new int[nw + 1, nd + 1];
                for (var i = 0; i <= nw; i++)
                for (var j = 0; j <= nd; j++)
                {
                    var p = P(i, j);
                    var du = P(Math.Min(i + 1, nw), j) - P(Math.Max(i - 1, 0), j);
                    var dv = P(i, Math.Min(j + 1, nd)) - P(i, Math.Max(j - 1, 0));
                    var n = dv.Cross(du).Normalized() * sign;
                    var tangent = du.Normalized();
                    var edgeWear = i == 0 || j == 0 || i == nw || j == nd ? 1f : 0f;
                    idx[i, j] = Vert(tf, p + n * (thickness / 2), n, tangent, n.Cross(tangent), new Vector2(p.X, p.Z), edgeWear);
                }
                for (var i = 0; i < nw; i++)
                for (var j = 0; j < nd; j++)
                    Quad(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);
            }
            return this;
        }

        private static readonly Vector3[] IcoV = MakeIcoVerts();
        private static readonly int[] IcoI =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };

        private static Vector3[] MakeIcoVerts()
        {
            var t = (1 + Mathf.Sqrt(5)) / 2;
            return new[]
            {
                new Vector3(-1,t,0), new Vector3(1,t,0), new Vector3(-1,-t,0), new Vector3(1,-t,0),
                new Vector3(0,-1,t), new Vector3(0,1,t), new Vector3(0,-1,-t), new Vector3(0,1,-t),
                new Vector3(t,0,-1), new Vector3(t,0,1), new Vector3(-t,0,-1), new Vector3(-t,0,1),
            }.Select(v => v.Normalized()).ToArray();
        }

        /// <summary>Roca facetada: icosaedro deformado, planos de fractura y estratos opcionales. Nunca una caja ni una esfera.</summary>
        public Builder Rock(string mat, float x, float y, float z, float rx, float ry, float rz, int seed, int subdiv = 1, float strata = 0, float yaw = 0)
        {
            Use(mat);
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            for (var i = 0; i < IcoI.Length; i += 3) tris.Add((IcoV[IcoI[i]], IcoV[IcoI[i + 1]], IcoV[IcoI[i + 2]]));
            for (var s = 0; s < subdiv; s++)
            {
                var next = new List<(Vector3, Vector3, Vector3)>(tris.Count * 4);
                foreach (var (a, b, c) in tris)
                {
                    var ab = ((a + b) / 2).Normalized();
                    var bc = ((b + c) / 2).Normalized();
                    var ca = ((c + a) / 2).Normalized();
                    next.Add((a, ab, ca));
                    next.Add((b, bc, ab));
                    next.Add((c, ca, bc));
                    next.Add((ab, bc, ca));
                }
                tris = next;
            }
            var planes = new (Vector3 n, float d)[5];
            var rng = new Rng(seed);
            for (var i = 0; i < planes.Length; i++)
            {
                var n = new Vector3(rng.Range(-1, 1), rng.Range(-0.4f, 1), rng.Range(-1, 1)).Normalized();
                planes[i] = (n, rng.Range(0.62f, 0.92f));
            }
            var off = new Vector3(seed * 1.37f, seed * 0.73f, seed * 2.11f);
            Vector3 Shape(Vector3 p)
            {
                var q = p * (0.78f + 0.5f * Terrain.Noise3(p * 2.3f + off) + 0.16f * Terrain.Noise3(p * 6.1f + off));
                foreach (var (n, d) in planes)
                {
                    var over = q.Dot(n) - d;
                    if (over > 0) q -= n * over;
                }
                if (strata > 0)
                {
                    // Estratos: escalones horizontales desplazados, con cornisas.
                    var band = q.Y * strata;
                    var fl = Mathf.Floor(band);
                    var t = band - fl;
                    var ledge = Mathf.SmoothStep(0.72f, 1f, t);
                    q.Y = (fl + ledge) / strata;
                    var shift = 0.86f + 0.14f * Mathf.Sin(fl * 12.9f + seed);
                    q.X *= shift;
                    q.Z *= shift;
                }
                return new Vector3(q.X * rx, Mathf.Max(q.Y * ry, -ry * 0.25f), q.Z * rz);
            }
            var tf = Tf(x, y, z, yaw);
            foreach (var (a0, b0, c0) in tris)
            {
                var a = Shape(a0);
                var b = Shape(b0);
                var c = Shape(c0);
                var n = (b - a).Cross(c - a);
                if (n.LengthSquared() < 1e-10f) continue;
                n = n.Normalized();
                if (n.Dot(a + b + c) < 0) n = -n;
                // Proyección por cara: los estratos de la textura quedan horizontales.
                var absN = n.Abs();
                Vector3 uDir = absN.Y > absN.X && absN.Y > absN.Z ? Vector3.Right : absN.X > absN.Z ? Vector3.Back : Vector3.Right;
                Vector3 vDir = absN.Y > absN.X && absN.Y > absN.Z ? Vector3.Back : Vector3.Down;
                var tan = (uDir - n * uDir.Dot(n)).Normalized();
                var vd = n.Cross(tan);
                if (vd.Dot(vDir) < 0) vd = -vd;
                int V(Vector3 p) => Vert(tf, p, n, tan, vd, new Vector2(p.Dot(uDir) + seed * 0.31f, p.Dot(vDir) + seed * 0.17f), 0);
                Tri(V(a), V(b), V(c));
            }
            return this;
        }

        /// <summary>Base común: zapata de hormigón pintado con faldón que entra en el suelo, chapa y franjas de peligro.</summary>
        public Builder Plinth(float w, float d)
        {
            Box("dark", 0, -0.1f, 0, w - 0.06f, 0.6f, d - 0.06f, bevel: 0.05f);
            Box("tread", 0, 0.205f, 0, w - 0.28f, 0.03f, d - 0.28f);
            Box("stripe", 0, 0.21f, d / 2 - 0.2f, w - 0.5f, 0.03f, 0.2f, bevel: 0);
            Box("stripe", 0, 0.21f, -d / 2 + 0.2f, w - 0.5f, 0.03f, 0.2f, bevel: 0);
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
            {
                Box("steel_dark", sx * (w / 2 - 0.2f), 0.16f, sz * (d / 2 - 0.2f), 0.34f, 0.12f, 0.34f);
                Cyl("steel", sx * (w / 2 - 0.2f), 0.25f, sz * (d / 2 - 0.2f), 0.06f, 0.08f, sides: 6);
            }
            return this;
        }

        /// <summary>Costuras y refuerzos que dan escala a un volumen liso.</summary>
        public Builder Seams(float x, float y, float z, float w, float h, float d)
        {
            for (var i = -1; i <= 1; i += 2)
            {
                Box("dark", x + i * w / 6, y, z + d / 2 + 0.006f, 0.035f, h * 0.96f, 0.012f, bevel: 0);
                Box("dark", x + i * w / 6, y, z - d / 2 - 0.006f, 0.035f, h * 0.96f, 0.012f, bevel: 0);
                Box("dark", x + w / 2 + 0.006f, y, z + i * d / 6, 0.012f, h * 0.96f, 0.035f, bevel: 0);
                Box("dark", x - w / 2 - 0.006f, y, z + i * d / 6, 0.012f, h * 0.96f, 0.035f, bevel: 0);
            }
            Box("steel_dark", x, y + h / 2 - 0.05f, z, w + 0.08f, 0.09f, d + 0.08f);
            Box("steel_dark", x, y - h / 2 + 0.05f, z, w + 0.08f, 0.09f, d + 0.08f);
            return this;
        }

        /// <summary>Rejilla de lamas: ventilación que se lee a distancia.</summary>
        public Builder Louvers(string mat, float x, float y, float z, float w, float h, int count, bool faceX = false, string frame = "steel_dark")
        {
            for (var i = 0; i < count; i++)
            {
                var ly = y - h / 2 + (i + 0.5f) * h / count;
                if (faceX) Box(mat, x, ly, z, 0.05f, h / count * 0.62f, w, pitch: 0, roll: 30, bevel: 0);
                else Box(mat, x, ly, z, w, h / count * 0.62f, 0.05f, pitch: 30, bevel: 0);
            }
            if (faceX)
            {
                Box(frame, x, y, z, 0.03f, h + 0.08f, w + 0.08f, bevel: 0);
            }
            else
            {
                Box(frame, x, y, z, w + 0.08f, h + 0.08f, 0.03f, bevel: 0);
            }
            return this;
        }

        /// <summary>Boca de carga en una cara ±Z: salida al frente (+Z), entrada atrás (−Z). Coincide con los puertos de cinta.</summary>
        public Builder Port(float x, float z, bool output, float y = 0.62f)
        {
            var dir = z >= 0 ? 1f : -1f;
            Box("steel_dark", x, y, z - dir * 0.06f, 1.08f, 0.84f, 0.2f);
            Box("dark", x, y, z + dir * 0.045f, 0.82f, 0.58f, 0.02f, bevel: 0);
            Box(output ? "signal" : "cream", x, y + 0.5f, z - dir * 0.02f, 1.08f, 0.14f, 0.14f);
            // Flecha: indica el sentido del flujo también sin color.
            var pointing = output ? 1f : -1f;
            Box("amber", x, y + 0.5f, z + dir * 0.055f, 0.34f, 0.04f, 0.012f, bevel: 0);
            Box("amber", x + 0.12f, y + 0.5f + pointing * 0.03f, z + dir * 0.055f, 0.1f, 0.03f, 0.012f, roll: -45 * pointing, bevel: 0);
            Box("amber", x - 0.12f, y + 0.5f + pointing * 0.03f, z + dir * 0.055f, 0.1f, 0.03f, 0.012f, roll: 45 * pointing, bevel: 0);
            return this;
        }

        public ArrayMesh Commit()
        {
            var mesh = new ArrayMesh();
            foreach (var (pass, p) in _parts.OrderBy(kv => kv.Key))
            {
                if (p.I.Count == 0) continue;
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = p.V.ToArray();
                arrays[(int)Mesh.ArrayType.Normal] = p.N.ToArray();
                arrays[(int)Mesh.ArrayType.Tangent] = p.T.ToArray();
                arrays[(int)Mesh.ArrayType.Color] = p.C.ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = p.Uv.ToArray();
                arrays[(int)Mesh.ArrayType.TexUV2] = p.Uv2.ToArray();
                arrays[(int)Mesh.ArrayType.Index] = p.I.ToArray();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Materials.ForPass(pass));
            }
            return mesh;
        }
    }

    /// <summary>Generador determinista y ligero para repartir piezas: no depende del motor.</summary>
    public sealed class Rng
    {
        private uint _s;
        public Rng(int seed) { _s = (uint)seed * 2654435761u + 0x9E3779B9u; if (_s == 0) _s = 1; }

        public float Next()
        {
            _s ^= _s << 13;
            _s ^= _s >> 17;
            _s ^= _s << 5;
            return (_s & 0xFFFFFF) / 16777216f;
        }

        public float Range(float a, float b) => a + (b - a) * Next();
    }
}
