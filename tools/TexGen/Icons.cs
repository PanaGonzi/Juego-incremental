namespace Pluton.TexGen;

/// <summary>Iconos de 64 px dibujados como facetas: la silueta identifica al objeto incluso sin color.</summary>
public static class Icons
{
    private const int Final = 64;
    private const int Ss = 4;
    private const int N = Final * Ss;

    private sealed class Canvas
    {
        public readonly float[] R = new float[N * N], G = new float[N * N], B = new float[N * N], A = new float[N * N];

        public void Set(int i, (float r, float g, float b) c, float a = 1)
        {
            R[i] += (c.r - R[i]) * a;
            G[i] += (c.g - G[i]) * a;
            B[i] += (c.b - B[i]) * a;
            A[i] = Math.Max(A[i], a);
        }

        /// <summary>Polígono en unidades de 64 con degradado vertical (luz desde arriba).</summary>
        public void Poly((float r, float g, float b) top, (float r, float g, float b) bottom, params float[] p)
        {
            float minY = 99, maxY = -99;
            for (var i = 1; i < p.Length; i += 2) { minY = Math.Min(minY, p[i]); maxY = Math.Max(maxY, p[i]); }
            for (var yi = (int)(minY * Ss); yi <= (int)(maxY * Ss) && yi < N; yi++)
            {
                if (yi < 0) continue;
                var y = (yi + 0.5f) / Ss;
                var t = maxY > minY ? (y - minY) / (maxY - minY) : 0;
                var c = (top.r + (bottom.r - top.r) * t, top.g + (bottom.g - top.g) * t, top.b + (bottom.b - top.b) * t);
                for (var xi = 0; xi < N; xi++)
                {
                    var x = (xi + 0.5f) / Ss;
                    var inside = false;
                    for (int i = 0, j = p.Length - 2; i < p.Length; j = i, i += 2)
                        if (p[i + 1] > y != p[j + 1] > y && x < (p[j] - p[i]) * (y - p[i + 1]) / (p[j + 1] - p[i + 1]) + p[i])
                            inside = !inside;
                    if (inside) Set(yi * N + xi, c);
                }
            }
        }

        public void Disc(float cx, float cy, float r, (float r, float g, float b) top, (float r, float g, float b) bottom, float hole = 0)
        {
            for (var yi = 0; yi < N; yi++)
            for (var xi = 0; xi < N; xi++)
            {
                var x = (xi + 0.5f) / Ss - cx;
                var y = (yi + 0.5f) / Ss - cy;
                var d = MathF.Sqrt(x * x + y * y);
                if (d > r || d < hole) continue;
                var t = (y + r) / (2 * r);
                Set(yi * N + xi, (top.r + (bottom.r - top.r) * t, top.g + (bottom.g - top.g) * t, top.b + (bottom.b - top.b) * t));
            }
        }

        public void Line((float r, float g, float b) c, float width, float x0, float y0, float x1, float y1)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            var nx = -dy / len * width / 2;
            var ny = dx / len * width / 2;
            Poly(c, c, x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny);
        }

        /// <summary>Contorno oscuro y reducción: el icono se lee sobre cualquier panel.</summary>
        public byte[] Finish(float outline = 1.6f)
        {
            var radius = (int)(outline * Ss);
            var edge = new float[N * N];
            for (var y = 0; y < N; y++)
            for (var x = 0; x < N; x++)
            {
                if (A[y * N + x] <= 0) continue;
                for (var j = -radius; j <= radius; j += 2)
                for (var i = -radius; i <= radius; i += 2)
                {
                    if (i * i + j * j > radius * radius) continue;
                    var yy = y + j;
                    var xx = x + i;
                    if (yy >= 0 && yy < N && xx >= 0 && xx < N) edge[yy * N + xx] = 1;
                }
            }
            var result = new byte[Final * Final * 4];
            for (var y = 0; y < Final; y++)
            for (var x = 0; x < Final; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (var j = 0; j < Ss; j++)
                for (var i = 0; i < Ss; i++)
                {
                    var k = (y * Ss + j) * N + x * Ss + i;
                    var alpha = A[k];
                    var e = edge[k] * 0.92f;
                    var total = alpha + e * (1 - alpha);
                    r += R[k] * alpha + 0.02f * e * (1 - alpha);
                    g += G[k] * alpha + 0.025f * e * (1 - alpha);
                    b += B[k] * alpha + 0.035f * e * (1 - alpha);
                    a += total;
                }
                var o = (y * Final + x) * 4;
                var inv = a > 0 ? 1 / a : 0;
                result[o] = (byte)(Math.Clamp(r * inv, 0, 1) * 255);
                result[o + 1] = (byte)(Math.Clamp(g * inv, 0, 1) * 255);
                result[o + 2] = (byte)(Math.Clamp(b * inv, 0, 1) * 255);
                result[o + 3] = (byte)(Math.Clamp(a / (Ss * Ss), 0, 1) * 255);
            }
            return result;
        }
    }

    private static (float, float, float) C(float r, float g, float b) => (r, g, b);

    private static readonly Dictionary<string, Action<Canvas>> All = new()
    {
        ["scrap"] = c =>
        {
            // Chapa rota y barra doblada sobre un montón.
            c.Poly(C(0.62f, 0.38f, 0.22f), C(0.36f, 0.2f, 0.12f), 6, 50, 12, 34, 22, 40, 28, 24, 40, 32, 50, 28, 58, 50, 44, 56, 26, 58);
            c.Poly(C(0.8f, 0.52f, 0.3f), C(0.5f, 0.3f, 0.17f), 16, 26, 34, 12, 42, 18, 38, 24, 46, 30, 30, 40, 24, 34, 20, 36);
            c.Line(C(0.66f, 0.7f, 0.74f), 5f, 8, 16, 26, 22);
            c.Line(C(0.5f, 0.54f, 0.6f), 5f, 26, 22, 34, 40);
            c.Poly(C(0.2f, 0.13f, 0.1f), C(0.2f, 0.13f, 0.1f), 30, 46, 40, 42, 44, 50, 34, 52);
        },
        ["recovered"] = c =>
        {
            // Pila de tres chapas limpias vista en isométrica.
            foreach (var dy in new[] { 18f, 8f, -2f })
            {
                c.Poly(C(0.34f, 0.42f, 0.5f), C(0.24f, 0.3f, 0.36f), 10, 34 + dy, 32, 46 + dy, 32, 51 + dy, 10, 39 + dy);
                c.Poly(C(0.26f, 0.32f, 0.4f), C(0.18f, 0.22f, 0.28f), 32, 46 + dy, 54, 34 + dy, 54, 39 + dy, 32, 51 + dy);
                c.Poly(C(0.72f, 0.8f, 0.86f), C(0.56f, 0.64f, 0.72f), 10, 34 + dy, 32, 22 + dy, 54, 34 + dy, 32, 46 + dy);
            }
        },
        ["ore"] = c =>
        {
            // Trozos angulosos de mineral: tres rocas facetadas.
            c.Poly(C(0.5f, 0.28f, 0.5f), C(0.3f, 0.16f, 0.32f), 6, 52, 8, 36, 20, 28, 32, 34, 36, 54);
            c.Poly(C(0.72f, 0.42f, 0.7f), C(0.46f, 0.24f, 0.46f), 8, 36, 20, 28, 26, 40, 14, 46);
            c.Poly(C(0.56f, 0.3f, 0.56f), C(0.32f, 0.18f, 0.34f), 28, 54, 30, 30, 42, 18, 56, 28, 58, 52);
            c.Poly(C(0.86f, 0.54f, 0.82f), C(0.56f, 0.3f, 0.56f), 30, 30, 42, 18, 48, 32, 36, 40);
            c.Poly(C(0.4f, 0.22f, 0.42f), C(0.24f, 0.13f, 0.26f), 36, 40, 48, 32, 58, 52, 40, 54);
        },
        ["ingot"] = c =>
        {
            // Lingote trapezoidal clásico.
            c.Poly(C(0.98f, 0.72f, 0.36f), C(0.84f, 0.54f, 0.22f), 16, 24, 40, 14, 56, 26, 34, 36);
            c.Poly(C(0.8f, 0.5f, 0.2f), C(0.56f, 0.32f, 0.12f), 16, 24, 34, 36, 28, 54, 6, 40);
            c.Poly(C(0.62f, 0.37f, 0.14f), C(0.4f, 0.22f, 0.08f), 34, 36, 56, 26, 58, 42, 28, 54);
            c.Line(C(1f, 0.88f, 0.6f), 1.6f, 20, 24, 38, 17);
        },
        ["parts"] = c =>
        {
            // Engranaje: forma inconfundible a cualquier tamaño.
            const int teeth = 8;
            for (var i = 0; i < teeth; i++)
            {
                var a = i * MathF.Tau / teeth;
                var d = 0.26f;
                float X(float r, float t) => 32 + MathF.Cos(t) * r;
                float Y(float r, float t) => 32 + MathF.Sin(t) * r;
                c.Poly(C(0.8f, 0.84f, 0.88f), C(0.5f, 0.54f, 0.6f), X(19, a - d), Y(19, a - d), X(28, a - d * 0.6f), Y(28, a - d * 0.6f), X(28, a + d * 0.6f), Y(28, a + d * 0.6f), X(19, a + d), Y(19, a + d));
            }
            c.Disc(32, 32, 21, C(0.78f, 0.82f, 0.86f), C(0.44f, 0.48f, 0.54f), 8);
            c.Disc(32, 32, 14, C(0.52f, 0.56f, 0.62f), C(0.7f, 0.74f, 0.78f), 8);
        },
        ["ice"] = c =>
        {
            // Bloque de hielo tallado, cara superior clara.
            c.Poly(C(0.93f, 0.98f, 1f), C(0.76f, 0.9f, 1f), 12, 22, 34, 8, 54, 20, 32, 32);
            c.Poly(C(0.6f, 0.82f, 0.98f), C(0.38f, 0.62f, 0.86f), 12, 22, 32, 32, 30, 58, 10, 42);
            c.Poly(C(0.44f, 0.68f, 0.9f), C(0.24f, 0.46f, 0.72f), 32, 32, 54, 20, 54, 44, 30, 58);
            c.Line(C(0.96f, 1f, 1f), 1.5f, 18, 28, 26, 42);
            c.Line(C(0.86f, 0.96f, 1f), 1.2f, 42, 30, 38, 46);
        },
        ["coolant"] = c =>
        {
            // Bidón con líquido visible y asa.
            c.Poly(C(0.34f, 0.4f, 0.46f), C(0.2f, 0.24f, 0.3f), 24, 6, 40, 6, 40, 14, 24, 14);
            c.Poly(C(0.68f, 0.74f, 0.8f), C(0.42f, 0.48f, 0.54f), 14, 18, 20, 12, 44, 12, 50, 18, 50, 58, 14, 58);
            c.Poly(C(0.3f, 0.96f, 0.96f), C(0.06f, 0.56f, 0.72f), 18, 30, 46, 30, 46, 54, 18, 54);
            c.Poly(C(0.7f, 1f, 1f), C(0.3f, 0.9f, 0.96f), 18, 30, 46, 30, 46, 33, 18, 33);
            c.Disc(28, 44, 3, C(0.8f, 1f, 1f), C(0.5f, 0.95f, 1f));
            c.Disc(37, 38, 2, C(0.8f, 1f, 1f), C(0.5f, 0.95f, 1f));
        },
        ["cryo_module"] = c =>
        {
            // Cartucho cilíndrico con bandas y núcleo azul.
            c.Poly(C(0.62f, 0.68f, 0.74f), C(0.3f, 0.34f, 0.4f), 20, 8, 44, 8, 50, 14, 50, 50, 44, 56, 20, 56, 14, 50, 14, 14);
            c.Poly(C(0.26f, 0.78f, 1f), C(0.08f, 0.36f, 0.76f), 19, 20, 45, 20, 45, 44, 19, 44);
            c.Disc(32, 32, 7.5f, C(0.92f, 1f, 1f), C(0.5f, 0.86f, 1f));
            c.Poly(C(1f, 0.72f, 0.3f), C(0.84f, 0.54f, 0.16f), 14, 14, 50, 14, 50, 18, 14, 18);
            c.Poly(C(1f, 0.72f, 0.3f), C(0.84f, 0.54f, 0.16f), 14, 46, 50, 46, 50, 50, 14, 50);
        },
        ["silicon_ore"] = c =>
        {
            // Racimo de cristales en punta, oscuros y azulados.
            c.Poly(C(0.34f, 0.46f, 0.66f), C(0.14f, 0.2f, 0.34f), 26, 58, 22, 22, 32, 6, 42, 22, 38, 58);
            c.Poly(C(0.58f, 0.74f, 0.94f), C(0.26f, 0.38f, 0.58f), 26, 58, 22, 22, 32, 6, 32, 58);
            c.Poly(C(0.3f, 0.42f, 0.6f), C(0.12f, 0.18f, 0.3f), 8, 58, 8, 36, 14, 26, 22, 36, 24, 58);
            c.Poly(C(0.52f, 0.68f, 0.88f), C(0.22f, 0.32f, 0.5f), 40, 58, 42, 40, 50, 28, 56, 40, 56, 58);
        },
        ["wafer"] = c =>
        {
            // Disco fino con retícula y muesca.
            c.Disc(32, 34, 25, C(0.2f, 0.26f, 0.4f), C(0.1f, 0.12f, 0.2f));
            c.Disc(32, 32, 25, C(0.62f, 0.72f, 0.9f), C(0.3f, 0.36f, 0.56f));
            for (var i = -2; i <= 2; i++)
            {
                c.Line(C(0.16f, 0.22f, 0.38f), 1.3f, 32 + i * 8, 13, 32 + i * 8, 51);
                c.Line(C(0.16f, 0.22f, 0.38f), 1.3f, 13, 32 + i * 8, 51, 32 + i * 8);
            }
            c.Poly(C(0.86f, 0.92f, 1f), C(0.5f, 0.6f, 0.8f), 16, 22, 24, 14, 32, 12, 22, 26);
        },
        ["nav_core"] = c =>
        {
            // Esfera anaranjada dentro de un armazón hexagonal.
            c.Poly(C(0.56f, 0.62f, 0.68f), C(0.26f, 0.3f, 0.36f), 32, 4, 56, 18, 56, 46, 32, 60, 8, 46, 8, 18);
            c.Poly(C(0.14f, 0.16f, 0.2f), C(0.08f, 0.09f, 0.12f), 32, 11, 50, 22, 50, 42, 32, 53, 14, 42, 14, 22);
            c.Disc(32, 32, 14, C(1f, 0.82f, 0.36f), C(0.9f, 0.34f, 0.08f));
            c.Disc(27, 26, 4, C(1f, 0.97f, 0.84f), C(1f, 0.85f, 0.5f));
        },
        // Indicadores de interfaz: forma propia, no solo color.
        ["st_ok"] = c =>
        {
            c.Line(C(0.5f, 0.95f, 0.65f), 8, 12, 34, 26, 48);
            c.Line(C(0.5f, 0.95f, 0.65f), 8, 26, 48, 52, 18);
        },
        ["st_wait"] = c =>
        {
            foreach (var x in new[] { 14f, 32f, 50f }) c.Disc(x, 32, 6.5f, C(1f, 0.8f, 0.34f), C(0.94f, 0.62f, 0.16f));
        },
        ["st_full"] = c =>
        {
            c.Poly(C(1f, 0.76f, 0.32f), C(0.86f, 0.56f, 0.16f), 8, 10, 56, 10, 56, 54, 8, 54);
            c.Poly(C(0.14f, 0.11f, 0.07f), C(0.14f, 0.11f, 0.07f), 14, 16, 50, 16, 50, 48, 14, 48);
            c.Poly(C(1f, 0.8f, 0.36f), C(0.9f, 0.6f, 0.2f), 20, 22, 44, 22, 44, 42, 20, 42);
        },
        ["st_power"] = c =>
        {
            c.Poly(C(1f, 0.56f, 0.46f), C(0.92f, 0.34f, 0.26f), 36, 4, 12, 36, 28, 36, 22, 60, 52, 24, 34, 24, 44, 4);
        },
        ["st_heat"] = c =>
        {
            c.Poly(C(0.94f, 0.94f, 0.94f), C(0.7f, 0.7f, 0.72f), 26, 8, 38, 8, 38, 40, 26, 40);
            c.Disc(32, 46, 12, C(1f, 0.5f, 0.38f), C(0.9f, 0.26f, 0.18f));
            c.Poly(C(1f, 0.5f, 0.38f), C(1f, 0.46f, 0.34f), 29, 16, 35, 16, 35, 42, 29, 42);
            c.Line(C(0.94f, 0.94f, 0.94f), 3, 42, 14, 50, 14);
            c.Line(C(0.94f, 0.94f, 0.94f), 3, 42, 24, 50, 24);
        },
        ["energy"] = c =>
        {
            c.Poly(C(1f, 0.9f, 0.46f), C(1f, 0.64f, 0.16f), 36, 4, 12, 36, 28, 36, 22, 60, 52, 24, 34, 24, 44, 4);
        },
        ["credit"] = c =>
        {
            c.Disc(32, 32, 25, C(1f, 0.82f, 0.4f), C(0.82f, 0.5f, 0.14f));
            c.Disc(32, 32, 18, C(0.74f, 0.44f, 0.12f), C(0.94f, 0.68f, 0.26f));
            c.Poly(C(1f, 0.92f, 0.62f), C(1f, 0.78f, 0.36f), 32, 18, 46, 32, 32, 46, 18, 32);
        },
        ["belt"] = c =>
        {
            c.Poly(C(0.34f, 0.38f, 0.42f), C(0.16f, 0.18f, 0.22f), 4, 26, 60, 26, 60, 46, 4, 46);
            foreach (var x in new[] { 14f, 32f, 50f }) c.Disc(x, 36, 6.5f, C(0.74f, 0.78f, 0.82f), C(0.4f, 0.44f, 0.5f));
            c.Poly(C(1f, 0.76f, 0.3f), C(0.9f, 0.56f, 0.14f), 22, 6, 40, 6, 40, 12, 50, 12, 32, 24, 14, 12, 22, 12);
        },
    };

    public static void Generate(string dir)
    {
        Directory.CreateDirectory(dir);
        Parallel.ForEach(All, kv =>
        {
            var canvas = new Canvas();
            kv.Value(canvas);
            Png.Save(Path.Combine(dir, kv.Key + ".png"), Final, Final, canvas.Finish());
        });
    }
}
