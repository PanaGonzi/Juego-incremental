using Godot;

namespace Pluton.Game;

/// <summary>Aspecto de cada objeto: color, malla sobre la cinta e icono de interfaz. Se genera una vez y se comparte.</summary>
public static class ItemVisuals
{
    private static readonly Dictionary<string, Mesh> Meshes = new();
    private static readonly Dictionary<string, Texture2D> Icons = new();

    public static Color ColorOf(string item)
    {
        var hash = (uint)item.Aggregate(17, (h, c) => unchecked(h * 31 + c));
        return Color.FromHsv((hash % 360) / 360f, 0.55f, 0.8f);
    }

    public static Mesh MeshOf(string item)
    {
        if (Meshes.TryGetValue(item, out var mesh)) return mesh;
        mesh = new BoxMesh
        {
            Size = new Vector3(0.32f, 0.22f, 0.32f),
            Material = new StandardMaterial3D { AlbedoColor = ColorOf(item), Roughness = 0.7f },
        };
        Meshes[item] = mesh;
        return mesh;
    }

    public static Texture2D IconOf(string item)
    {
        if (Icons.TryGetValue(item, out var icon)) return icon;
        var image = Image.CreateEmpty(64, 64, false, Image.Format.Rgba8);
        image.Fill(ColorOf(item));
        icon = ImageTexture.CreateFromImage(image);
        Icons[item] = icon;
        return icon;
    }
}
