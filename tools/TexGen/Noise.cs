namespace Pluton.TexGen;

/// <summary>Ruido periódico: las texturas repiten sin costura.</summary>
public static class Noise
{
    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647 + 0x51ED27);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }
    }

    private static int Wrap(int v, int p) => ((v % p) + p) % p;
    private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    /// <summary>Ruido de gradiente en [0,1]. u y v en [0,1); px y py celdas por repetición.</summary>
    public static float Perlin(float u, float v, int px, int py, int seed)
    {
        var x = u * px;
        var y = v * py;
        var xi = (int)MathF.Floor(x);
        var yi = (int)MathF.Floor(y);
        var fx = x - xi;
        var fy = y - yi;
        float G(int ix, int iy, float dx, float dy)
        {
            var a = Hash(Wrap(ix, px), Wrap(iy, py), seed) * MathF.Tau;
            return MathF.Cos(a) * dx + MathF.Sin(a) * dy;
        }
        var ux = Fade(fx);
        var uy = Fade(fy);
        var a0 = G(xi, yi, fx, fy) + (G(xi + 1, yi, fx - 1, fy) - G(xi, yi, fx, fy)) * ux;
        var a1 = G(xi, yi + 1, fx, fy - 1) + (G(xi + 1, yi + 1, fx - 1, fy - 1) - G(xi, yi + 1, fx, fy - 1)) * ux;
        return Math.Clamp((a0 + (a1 - a0) * uy) * 0.71f + 0.5f, 0, 1);
    }

    public static float Fbm(float u, float v, int freq, int octaves, int seed, float gain = 0.5f)
    {
        float sum = 0, amp = 1, total = 0;
        for (var i = 0; i < octaves; i++)
        {
            sum += Perlin(u, v, freq, freq, seed + i * 17) * amp;
            total += amp;
            amp *= gain;
            freq *= 2;
        }
        return sum / total;
    }

    /// <summary>Voronoi periódico: distancia al borde de celda (grietas) y valor de celda.</summary>
    public static (float edge, float cell) Voronoi(float u, float v, int cells, int seed)
    {
        var x = u * cells;
        var y = v * cells;
        var xi = (int)MathF.Floor(x);
        var yi = (int)MathF.Floor(y);
        float d1 = 9, d2 = 9, id = 0;
        for (var j = -2; j <= 2; j++)
        for (var i = -2; i <= 2; i++)
        {
            var cx = Wrap(xi + i, cells);
            var cy = Wrap(yi + j, cells);
            var px = xi + i + Hash(cx, cy, seed);
            var py = yi + j + Hash(cx, cy, seed + 101);
            var d = MathF.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
            if (d < d1) { d2 = d1; d1 = d; id = Hash(cx, cy, seed + 7); }
            else if (d < d2) d2 = d;
        }
        return (d2 - d1, id);
    }

    public static float Smooth(float a, float b, float x)
    {
        var t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
