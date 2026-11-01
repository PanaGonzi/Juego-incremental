using static Pluton.TexGen.Noise;

namespace Pluton.TexGen;

/// <summary>Muestra de una superficie: color lineal neutro, altura, rugosidad, cavidad y máscara de desgaste.</summary>
public struct Sample
{
    public float R, G, B, Height, Rough, Cavity, Mask;
}

/// <summary>Superficies PBR propias. Los colores son neutros: el tinte de cada pieza se aplica por vértice.</summary>
public static class Surfaces
{
    public const int Size = 512;

    /// <summary>El orden es el índice de capa de Materials.cs: no reordenar sin actualizarlo.</summary>
    public static readonly (string name, Func<float, float, Sample> fn, float normalStrength)[] Layers =
    {
        ("metal_pintado", Painted, 2.2f),
        ("acero", Steel, 1.2f),
        ("goma", Rubber, 3.5f),
        ("roca", Rock, 6f),
        ("hielo", Ice, 3.5f),
        ("regolito", Regolith, 3.2f),
        ("peligro", Hazard, 2f),
        ("chapa_lagrimada", Tread, 5f),
        ("oxido", Rust, 4f),
    };

    private static float Scratches(float u, float v, int seed)
    {
        // Trazos finos en direcciones dominantes: rayaduras de uso, no ruido uniforme.
        var a = Perlin(u, v, 3, 3, seed);
        var line = MathF.Abs(Perlin(u + a * 0.08f, v, 2, 90, seed + 3) - 0.5f) * 2;
        var gate = Smooth(0.62f, 0.72f, Perlin(u, v, 6, 6, seed + 5));
        return (1 - Smooth(0f, 0.05f, line)) * gate;
    }

    public static Sample Painted(float u, float v)
    {
        var big = Fbm(u, v, 3, 4, 11);
        var mid = Fbm(u, v, 16, 3, 13);
        var grime = Smooth(0.5f, 0.78f, Fbm(u, v, 5, 5, 17));
        var scratch = Scratches(u, v, 19);
        var streak = Perlin(u, v, 22, 3, 23);
        var shade = 0.92f + (big - 0.5f) * 0.18f + (mid - 0.5f) * 0.08f - grime * 0.26f - (1 - streak) * 0.06f + scratch * 0.18f;
        return new Sample
        {
            R = shade, G = shade, B = shade * 0.985f,
            Height = big * 0.18f + mid * 0.05f - scratch * 0.05f,
            Rough = 0.42f + mid * 0.16f + grime * 0.24f - scratch * 0.1f,
            Cavity = 1 - grime * 0.25f,
            Mask = Fbm(u, v, 24, 4, 29, 0.6f) * 0.7f + big * 0.3f,
        };
    }

    public static Sample Steel(float u, float v)
    {
        var brushed = Perlin(u, v, 5, 220, 31) * 0.6f + Perlin(u, v, 11, 90, 33) * 0.4f;
        var big = Fbm(u, v, 4, 4, 35);
        var scratch = Scratches(v, u, 37);
        var shade = 0.72f + (brushed - 0.5f) * 0.16f + (big - 0.5f) * 0.2f + scratch * 0.12f;
        return new Sample
        {
            R = shade, G = shade, B = shade * 1.01f,
            Height = brushed * 0.04f + big * 0.08f,
            Rough = 0.28f + brushed * 0.1f + big * 0.2f,
            Cavity = 1,
            Mask = big,
        };
    }

    public static Sample Rubber(float u, float v)
    {
        var (edge, _) = Voronoi(u, v, 48, 41);
        var bump = Smooth(0f, 0.45f, edge);
        var worn = Fbm(u, v, 6, 3, 43);
        var shade = 0.72f + bump * 0.16f + (worn - 0.5f) * 0.3f;
        return new Sample { R = shade, G = shade, B = shade, Height = bump * 0.25f, Rough = 0.72f + worn * 0.2f, Cavity = 0.75f + bump * 0.25f, Mask = worn };
    }

    public static Sample Rock(float u, float v)
    {
        // Estratos horizontales en v, deformados: la dirección importa al proyectar en laderas.
        var warp = Fbm(u, v, 3, 3, 51) * 0.09f;
        var phase = (v + warp) * 9;
        var band = phase - MathF.Floor(phase);
        var id = MathF.Floor(phase);
        var ledge = Smooth(0f, 0.12f, band) * (1 - Smooth(0.82f, 1f, band));
        var layerTone = Perlin(0.37f, (((id % 9) + 9) % 9 + 0.5f) / 9f, 1, 9, 53);
        var coarse = Fbm(u, v, 8, 5, 55);
        var (crack, _) = Voronoi(u, v, 7, 57);
        var cracks = 1 - Smooth(0f, 0.05f, crack);
        var shade = 0.42f + layerTone * 0.42f + (coarse - 0.5f) * 0.5f - cracks * 0.25f - (1 - ledge) * 0.14f;
        return new Sample
        {
            R = shade * 1.02f, G = shade * 0.99f, B = shade * 0.96f,
            Height = ledge * 0.32f + coarse * 0.5f - cracks * 0.3f,
            Rough = 0.8f + coarse * 0.18f,
            Cavity = 1 - cracks * 0.55f - (1 - ledge) * 0.3f,
            Mask = coarse,
        };
    }

    public static Sample Ice(float u, float v)
    {
        var (big, cell) = Voronoi(u, v, 6, 61);
        var (small, _) = Voronoi(u + Perlin(u, v, 9, 9, 63) * 0.02f, v, 22, 65);
        var cracks = (1 - Smooth(0f, 0.035f, big)) * 0.9f + (1 - Smooth(0f, 0.03f, small)) * 0.1f * Smooth(0.4f, 0.7f, Perlin(u, v, 5, 5, 67));
        var cloud = Fbm(u, v, 5, 5, 69);
        var fine = Fbm(u, v, 40, 3, 71);
        var milky = 0.72f + cloud * 0.3f + (cell - 0.5f) * 0.1f;
        var dark = cracks * 0.22f;
        return new Sample
        {
            R = (milky - dark) * 0.86f, G = (milky - dark) * 0.94f, B = (milky - dark) * 1.02f,
            Height = cloud * 0.3f + fine * 0.05f - cracks * 0.14f,
            Rough = 0.14f + cloud * 0.2f + fine * 0.18f + cracks * 0.3f,
            Cavity = 1 - cracks * 0.5f,
            Mask = cloud,
        };
    }

    public static Sample Regolith(float u, float v)
    {
        var big = Fbm(u, v, 4, 5, 81);
        var grains = Fbm(u, v, 48, 3, 83, 0.65f);
        var (stone, id) = Voronoi(u, v, 26, 85);
        var clods = Smooth(0.12f, 0.42f, stone) * Smooth(0.68f, 0.92f, id);
        var shade = 0.6f + (big - 0.5f) * 0.6f + (grains - 0.5f) * 0.6f + clods * 0.04f;
        return new Sample
        {
            R = shade * 1.01f, G = shade, B = shade * 0.98f,
            Height = big * 0.5f + grains * 0.26f + clods * 0.05f,
            Rough = 0.84f + grains * 0.14f,
            Cavity = 0.78f + grains * 0.22f,
            Mask = big,
        };
    }

    public static Sample Hazard(float u, float v)
    {
        var t = (u + v) * 4;
        t -= MathF.Floor(t);
        var chip = Smooth(0.6f, 0.68f, Fbm(u, v, 20, 4, 91, 0.6f));
        var yellow = Smooth(0.48f, 0.5f, t) * (1 - Smooth(0.98f, 1f, t)) + (1 - Smooth(0f, 0.02f, t));
        var dirt = 0.8f + Fbm(u, v, 6, 4, 93) * 0.25f;
        var r = (0.012f + yellow * 0.8f) * dirt;
        var g = (0.012f + yellow * 0.46f) * dirt;
        var b = (0.014f + yellow * 0.02f) * dirt;
        return new Sample
        {
            R = r * (1 - chip) + 0.3f * chip, G = g * (1 - chip) + 0.3f * chip, B = b * (1 - chip) + 0.31f * chip,
            Height = (1 - chip) * 0.1f, Rough = 0.5f + chip * 0.2f, Cavity = 1, Mask = chip,
        };
    }

    public static Sample Tread(float u, float v)
    {
        // Chapa lagrimada: lágrimas alternas a 45 grados en rejilla regular.
        const int n = 10;
        var x = u * n;
        var y = v * n;
        var cx = MathF.Floor(x);
        var cy = MathF.Floor(y);
        var fx = x - cx - 0.5f;
        var fy = y - cy - 0.5f;
        var flip = ((int)cx + (int)cy) % 2 == 0 ? 1f : -1f;
        var a = (fx + flip * fy) * 0.7071f;
        var b = (fx - flip * fy) * 0.7071f;
        var d = MathF.Sqrt(a * a * 16 + b * b * 1.5f);
        var lug = 1 - Smooth(0.3f, 0.5f, d);
        var big = Fbm(u, v, 5, 4, 101);
        var shade = 0.6f + lug * 0.14f + (big - 0.5f) * 0.3f;
        return new Sample { R = shade, G = shade, B = shade, Height = lug * 0.5f + big * 0.02f, Rough = 0.36f + big * 0.25f - lug * 0.08f, Cavity = 0.82f + lug * 0.18f, Mask = big };
    }

    public static Sample Rust(float u, float v)
    {
        var big = Fbm(u, v, 5, 5, 111);
        var pits = Fbm(u, v, 36, 3, 113, 0.7f);
        var flake = Smooth(0.5f, 0.56f, Fbm(u, v, 12, 4, 115));
        var t = big * 0.7f + pits * 0.3f;
        return new Sample
        {
            R = 0.5f + t * 0.45f - flake * 0.1f,
            G = 0.48f + t * 0.45f - flake * 0.12f,
            B = 0.44f + t * 0.45f - flake * 0.12f,
            Height = flake * 0.1f + pits * 0.3f,
            Rough = 0.72f + pits * 0.26f,
            Cavity = 0.72f + pits * 0.28f,
            Mask = big,
        };
    }

    /// <summary>Ruido a gran escala para variar terreno sin repetición visible.</summary>
    public static (float r, float g, float b, float a) Macro(float u, float v)
    {
        var (edge, _) = Voronoi(u + Perlin(u, v, 6, 6, 121) * 0.012f, v, 14, 123);
        return (Fbm(u, v, 4, 6, 125), Fbm(u, v, 16, 5, 127), Fbm(u, v, 64, 3, 129), Smooth(0f, 0.09f, edge));
    }
}
