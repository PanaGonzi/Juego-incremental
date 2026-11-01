using Godot;

namespace Pluton.Game;

/// <summary>Aspecto de cada objeto: color, malla sobre la cinta e icono de interfaz. Se genera una vez y se comparte.</summary>
public static class ItemVisuals
{
    private static readonly Dictionary<string, Mesh> Meshes = new();
    private static readonly Dictionary<string, Texture2D> Icons = new();

    /// <summary>Color de referencia de cada objeto, igual en malla e icono. Nunca es la única pista: cada uno tiene forma propia.</summary>
    private static readonly Dictionary<string, Color> Known = new()
    {
        ["scrap"] = new Color(0.62f, 0.38f, 0.22f),
        ["recovered"] = new Color(0.72f, 0.8f, 0.86f),
        ["ore"] = new Color(0.72f, 0.42f, 0.7f),
        ["ingot"] = new Color(0.98f, 0.66f, 0.3f),
        ["parts"] = new Color(0.7f, 0.74f, 0.78f),
        ["ice"] = new Color(0.72f, 0.9f, 1f),
        ["coolant"] = new Color(0.3f, 0.9f, 0.94f),
        ["cryo_module"] = new Color(0.28f, 0.66f, 1f),
        ["silicon_ore"] = new Color(0.42f, 0.56f, 0.8f),
        ["wafer"] = new Color(0.6f, 0.7f, 0.9f),
        ["nav_core"] = new Color(1f, 0.62f, 0.2f),
    };

    public static Color ColorOf(string item)
    {
        if (Known.TryGetValue(item, out var known)) return known;
        var hash = (uint)item.Aggregate(17, (h, c) => unchecked(h * 31 + c));
        return Color.FromHsv((hash % 360) / 360f, 0.55f, 0.8f);
    }

    /// <summary>Malla barata (pocos cientos de triángulos) reconocible por silueta. Cabe en un hueco de cinta de 0,5 m.</summary>
    public static Mesh MeshOf(string item)
    {
        if (Meshes.TryGetValue(item, out var mesh)) return mesh;
        mesh = Build(item);
        Meshes[item] = mesh;
        return mesh;
    }

    private static Mesh Build(string item)
    {
        var b = new Models.Builder();
        switch (item)
        {
            case "scrap":
                // Trozo de chapa doblada con una barra saliente.
                b.Box("rust", 0, 0.05f, 0, 0.3f, 0.03f, 0.22f, yaw: 18, roll: 14, bevel: 0.006f);
                b.Box("rustlight", 0.07f, 0.11f, 0.02f, 0.2f, 0.03f, 0.2f, yaw: -22, roll: -32, bevel: 0.006f);
                b.Beam("steel_dark", new Vector3(-0.15f, 0.05f, 0.09f), new Vector3(0.14f, 0.15f, -0.08f), 0.03f, 0.03f);
                break;
            case "recovered":
                // Pila de chapas limpias.
                for (var i = 0; i < 3; i++) b.Box("steel", 0.006f * i, 0.025f + i * 0.04f, 0, 0.3f, 0.03f, 0.22f, yaw: i * 4 - 4, bevel: 0.005f);
                break;
            case "ore":
                b.Rock("ore", -0.04f, 0.08f, 0, 0.13f, 0.1f, 0.12f, 3);
                b.Rock("ore", 0.1f, 0.06f, 0.05f, 0.08f, 0.07f, 0.08f, 5);
                break;
            case "ingot":
                // Lingote trapezoidal.
                b.Taper("copper", 0, 0.065f, 0, 0.34f, 0.17f, 0.27f, 0.12f, 0.12f);
                break;
            case "parts":
                // Engranaje plano sobre su eje: el contorno dentado se reconoce desde arriba.
                b.Cyl("steel", 0, 0.05f, 0, 0.11f, 0.05f, sides: 10);
                for (var i = 0; i < 6; i++)
                {
                    var a = i * 60f;
                    var r = Mathf.DegToRad(a);
                    b.Box("steel", Mathf.Cos(r) * 0.125f, 0.05f, Mathf.Sin(r) * 0.125f, 0.06f, 0.05f, 0.05f, yaw: -a, bevel: 0.005f);
                }
                b.Cyl("steel_dark", 0, 0.08f, 0, 0.035f, 0.05f, sides: 8);
                break;
            case "ice":
                b.Rock("ice", 0, 0.09f, 0, 0.14f, 0.12f, 0.13f, 8);
                break;
            case "coolant":
                // Bidón con banda.
                b.Cyl("panel", 0, 0.12f, 0, 0.09f, 0.22f, sides: 10);
                b.Cyl("cyan", 0, 0.12f, 0, 0.094f, 0.07f, sides: 10);
                b.Cyl("steel_dark", 0, 0.25f, 0, 0.035f, 0.04f, sides: 6);
                break;
            case "cryo_module":
                // Cartucho tumbado con bandas y núcleo azul.
                b.Cyl("chrome", 0, 0.1f, 0, 0.1f, 0.34f, roll: 90, sides: 10);
                b.Cyl("cyan", 0, 0.1f, 0, 0.104f, 0.12f, roll: 90, sides: 10);
                foreach (var x in new[] { -0.15f, 0.15f }) b.Cyl("signal", x, 0.1f, 0, 0.106f, 0.025f, roll: 90, sides: 10);
                break;
            case "silicon_ore":
                // Cristales en punta.
                b.Cyl("silicon", 0, 0.13f, 0, 0.06f, 0.26f, sides: 5, topRadius: 0.012f);
                b.Cyl("silicon", 0.08f, 0.08f, 0.03f, 0.045f, 0.17f, -12, -16, 5, 0.01f);
                b.Cyl("silicon", -0.07f, 0.08f, -0.03f, 0.045f, 0.16f, 14, 18, 5, 0.01f);
                break;
            case "wafer":
                // Disco fino y brillante con muesca.
                b.Cyl("silicon", 0, 0.02f, 0, 0.15f, 0.025f, sides: 16);
                b.Box("dark", 0.14f, 0.033f, 0, 0.04f, 0.006f, 0.06f, bevel: 0);
                break;
            case "nav_core":
                // Esfera incandescente en armazón.
                b.Ball("amber", 0, 0.13f, 0, 0.07f, 10, 5);
                foreach (var (x, z) in new[] { (-0.09f, -0.09f), (0.09f, -0.09f), (0.09f, 0.09f), (-0.09f, 0.09f) })
                    b.Box("steel_dark", x, 0.13f, z, 0.025f, 0.22f, 0.025f, bevel: 0.004f);
                b.Box("steel", 0, 0.025f, 0, 0.24f, 0.03f, 0.24f, bevel: 0.006f);
                b.Box("steel", 0, 0.24f, 0, 0.24f, 0.02f, 0.24f, bevel: 0.005f);
                break;
            default:
                b.Box("panel", 0, 0.08f, 0, 0.28f, 0.16f, 0.28f, bevel: 0.01f);
                break;
        }
        return b.Commit();
    }

    public static Texture2D IconOf(string item)
    {
        if (Icons.TryGetValue(item, out var icon)) return icon;
        var path = $"res://Assets/Icons/{item}.png";
        if (ResourceLoader.Exists(path)) icon = GD.Load<Texture2D>(path);
        else
        {
            // Objeto futuro sin icono propio: rombo con su color, distinto de los iconos de un cuadrado.
            var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
            var color = ColorOf(item);
            for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
                if (Math.Abs(x - 31.5f) + Math.Abs(y - 31.5f) < 28) image.SetPixel(x, y, color);
            icon = ImageTexture.CreateFromImage(image);
        }
        Icons[item] = icon;
        return icon;
    }
}
