using Pluton.TexGen;

// Genera las texturas e iconos del juego: resultado reproducible, sin recursos externos.
// Uso: dotnet run --project tools/TexGen -c Release -- <raíz del repositorio>
var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var dir = Path.Combine(root, "src", "Game", "Assets", "Textures");
var n = Surfaces.Size;
var count = Surfaces.Layers.Length;
var albedo = new byte[n * n * count * 4];
var detail = new byte[n * n * count * 4];

static byte Srgb8(float linear)
{
    linear = Math.Clamp(linear, 0, 1);
    var s = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;
    return (byte)MathF.Round(s * 255);
}

static byte B8(float v) => (byte)MathF.Round(Math.Clamp(v, 0, 1) * 255);

for (var layer = 0; layer < count; layer++)
{
    var (_, fn, strength) = Surfaces.Layers[layer];
    var samples = new Sample[n * n];
    Parallel.For(0, n, y =>
    {
        for (var x = 0; x < n; x++) samples[y * n + x] = fn((x + 0.5f) / n, (y + 0.5f) / n);
    });
    var offset = layer * n * n * 4;
    for (var y = 0; y < n; y++)
    for (var x = 0; x < n; x++)
    {
        var s = samples[y * n + x];
        // Pendiente con envoltura para que el mapa de normales también repita.
        var dx = samples[y * n + (x + 1) % n].Height - samples[y * n + (x + n - 1) % n].Height;
        var dy = samples[(y + 1) % n * n + x].Height - samples[(y + n - 1) % n * n + x].Height;
        var nx = -dx * strength * n / 256f;
        var ny = dy * strength * n / 256f;
        var len = MathF.Sqrt(nx * nx + ny * ny + 1);
        var i = offset + (y * n + x) * 4;
        // El albedo se guarda en sRGB; los valores calculados se tratan como lineales.
        albedo[i] = Srgb8(s.R);
        albedo[i + 1] = Srgb8(s.G);
        albedo[i + 2] = Srgb8(s.B);
        albedo[i + 3] = B8(s.Mask);
        detail[i] = B8(nx / len * 0.5f + 0.5f);
        detail[i + 1] = B8(ny / len * 0.5f + 0.5f);
        detail[i + 2] = B8(s.Rough);
        detail[i + 3] = B8(s.Cavity);
    }
}

Png.Save(Path.Combine(dir, "superficies_albedo.png"), n, n * count, albedo);
Png.Save(Path.Combine(dir, "superficies_detalle.png"), n, n * count, detail);

var macro = new byte[n * n * 4];
Parallel.For(0, n, y =>
{
    for (var x = 0; x < n; x++)
    {
        var (r, g, b, a) = Surfaces.Macro((x + 0.5f) / n, (y + 0.5f) / n);
        var i = (y * n + x) * 4;
        macro[i] = B8(r);
        macro[i + 1] = B8(g);
        macro[i + 2] = B8(b);
        macro[i + 3] = B8(a);
    }
});
Png.Save(Path.Combine(dir, "macro.png"), n, n, macro);

// Parámetros de importación: compresión de vídeo y mipmaps sin depender del editor.
void ArrayImport(string file, bool highQuality) => File.WriteAllText(Path.Combine(dir, file + ".import"),
    "[remap]\n\nimporter=\"2d_array_texture\"\ntype=\"CompressedTexture2DArray\"\n\n[params]\n\n" +
    $"compress/mode=2\ncompress/high_quality={(highQuality ? "true" : "false")}\ncompress/channel_pack=1\n" +
    $"mipmaps/generate=true\nmipmaps/limit=-1\nslices/horizontal=1\nslices/vertical={count}\n");
ArrayImport("superficies_albedo.png", false);
ArrayImport("superficies_detalle.png", true);
File.WriteAllText(Path.Combine(dir, "macro.png.import"),
    "[remap]\n\nimporter=\"texture\"\ntype=\"CompressedTexture2D\"\n\n[params]\n\n" +
    "compress/mode=2\ncompress/high_quality=false\ncompress/channel_pack=1\nmipmaps/generate=true\nprocess/fix_alpha_border=false\ndetect_3d/compress_to=0\n");

Icons.Generate(Path.Combine(root, "src", "Game", "Assets", "Icons"));
Console.WriteLine($"Generadas {count} superficies de {n}x{n} e iconos en {dir}");
