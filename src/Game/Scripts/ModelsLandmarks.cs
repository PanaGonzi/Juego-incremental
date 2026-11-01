using Godot;

namespace Pluton.Game;

/// <summary>
/// Hitos de la zona inicial de Plutón. Se levantan fuera de los muros (±188 m): no ocupan celdas ni tienen colisión.
/// Cada uno es una sola malla y se distingue por silueta y color: orientan sin mirar la interfaz.
/// </summary>
public static partial class Models
{
    /// <summary>Altura de la baliza sobre la base de la torre, en metros de malla.</summary>
    public const float BeaconHeight = 66.5f;

    /// <summary>Celosía de cuatro cordones con anillos y diagonales, de a hasta c, estrechándose de w0 a w1.</summary>
    private static void Lattice(Builder b, string chord, string brace, Vector3 a, Vector3 c, float w0, float w1, int sections, float thick)
    {
        var axis = (c - a).Normalized();
        var side = (Mathf.Abs(axis.Y) > 0.9f ? Vector3.Right : Vector3.Up).Cross(axis).Normalized();
        var up = axis.Cross(side).Normalized();
        Vector3 Corner(int i, float t)
        {
            var half = Mathf.Lerp(w0, w1, t) / 2;
            var s = i == 0 || i == 3 ? -1f : 1f;
            var u = i < 2 ? -1f : 1f;
            return a.Lerp(c, t) + side * (s * half) + up * (u * half);
        }
        for (var n = 0; n < sections; n++)
        {
            var t0 = n / (float)sections;
            var t1 = (n + 1) / (float)sections;
            var band = n % 2 == 0 ? chord : brace;
            for (var i = 0; i < 4; i++)
            {
                var j = (i + 1) % 4;
                b.Beam(chord, Corner(i, t0), Corner(i, t1), thick, thick);
                b.Beam(band, Corner(i, t1), Corner(j, t1), thick * 0.7f, thick * 0.7f);
                b.Beam(brace, n % 2 == 0 ? Corner(i, t0) : Corner(j, t0), n % 2 == 0 ? Corner(j, t1) : Corner(i, t1), thick * 0.5f, thick * 0.5f);
            }
        }
    }

    /// <summary>Torre de antena de 60 m con parábola y bandas rojas y blancas. La baliza se añade aparte porque parpadea.</summary>
    public static ArrayMesh LandmarkTower()
    {
        const string key = "lm_tower";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        Lattice(b, "signal", "cream", new Vector3(0, 0, 0), new Vector3(0, 58, 0), 13f, 2.2f, 9, 0.5f);
        // Zapata, caseta y riostras clavadas al suelo.
        b.Box("dark", 0, 0.3f, 0, 17f, 2.2f, 17f, bevel: 0.2f);
        b.Box("hull", 5f, 3.4f, -4f, 6f, 4.4f, 4.6f, bevel: 0.1f);
        b.Box("amber", 5f, 3.8f, -1.68f, 2.6f, 0.7f, 0.05f, bevel: 0);
        foreach (var (x, z) in new[] { (-34f, -22f), (31f, -28f), (-5f, 38f) })
        {
            b.Beam("steel_dark", new Vector3(0, 46, 0), new Vector3(x, -1, z), 0.16f, 0.16f);
            b.Box("dark", x, 0, z, 3f, 1.6f, 3f, bevel: 0.1f);
        }
        // Plataforma y parábola grande a media altura, inclinada hacia la base.
        b.Box("steel_dark", 0, 40.5f, 0, 8.6f, 0.5f, 8.6f, bevel: 0.05f);
        b.Beam("steel_dark", new Vector3(0, 40.5f, 3.4f), new Vector3(0, 44f, 6.2f), 0.5f, 0.5f);
        b.Lathe("steel", 0, 44.4f, 6.8f, new[] { (0.3f, -1.1f), (3.2f, -0.72f), (6.2f, 0.45f), (6.0f, 0.62f), (3f, -0.5f), (0.2f, -0.88f) }, 20, pitch: 66);
        // Remate y mástil de la baliza.
        b.Box("steel_dark", 0, 58.4f, 0, 3f, 0.6f, 3f);
        b.Cyl("steel", 0, 62.4f, 0, 0.32f, 8f, sides: 8, topRadius: 0.12f);
        b.Box("steel_dark", 0, BeaconHeight - 0.9f, 0, 1.3f, 0.2f, 1.3f);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Grúa pórtico derribada: torre tumbada a medias sobre su pluma amarilla, con la cabina colgando.</summary>
    public static ArrayMesh LandmarkCrane()
    {
        const string key = "lm_crane";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        var foot = new Vector3(0, -1, 0);
        var head = new Vector3(14f, 38f, -6f);
        // Torre vencida y ya sin arriostrar: es lo que se ve de lejos, una diagonal contra el cielo.
        Lattice(b, "brass", "stripe", foot, head, 6.5f, 3.6f, 5, 0.55f);
        // Pluma caída: arranca arriba de la torre y clava la punta en el suelo.
        var tip = new Vector3(84f, -2.5f, 26f);
        Lattice(b, "brass", "steel_dark", head, tip, 4.2f, 1.8f, 8, 0.42f);
        // Contrapluma rota y lastre desparramado.
        var back = head + new Vector3(-18f, 7f, -6f);
        Lattice(b, "steel_dark", "rust", head, back, 3.6f, 2.8f, 3, 0.4f);
        b.Box("dark", back.X - 1f, back.Y - 2.2f, back.Z, 6f, 4.4f, 5f, yaw: 20, roll: 12, bevel: 0.15f);
        b.Tube("steel_dark", 0.12f, 6, back + Vector3.Down * 2f, new Vector3(back.X - 3f, 10f, back.Z + 3f), new Vector3(back.X + 2f, 0f, back.Z + 7f));
        // Cabina amarilla enganchada a la cabeza de la torre.
        b.Box("signal", head.X + 4.4f, head.Y - 3.2f, head.Z + 2.4f, 4.6f, 3.8f, 3.6f, yaw: 18, roll: -24, bevel: 0.12f);
        b.Box("glass", head.X + 6.5f, head.Y - 3.6f, head.Z + 3.1f, 0.2f, 2f, 2.8f, yaw: 18, roll: -24, bevel: 0);
        b.Box("amber", head.X + 2.8f, head.Y + 0.1f, head.Z + 1.6f, 1.1f, 0.5f, 1.1f, bevel: 0);
        // Cable colgando de la pluma, con su gancho sobre el suelo.
        var along = head.Lerp(tip, 0.32f);
        var hook = along + new Vector3(0.6f, -along.Y + 3.4f, 0.4f);
        b.Tube("steel_dark", 0.1f, 6, along, hook);
        b.Box("stripe", hook.X, hook.Y - 0.8f, hook.Z, 1.8f, 1.6f, 1.2f, bevel: 0.1f);
        // Raíles de rodadura sueltos: dan escala a la máquina.
        foreach (var (x, z, yaw) in new[] { (-10f, 12f, 12f), (-3f, -13f, 4f), (28f, 7f, 74f), (46f, 8f, 61f) })
            b.Box("rust", x, 0.1f, z, 1.1f, 0.9f, 24f, yaw: yaw, bevel: 0.08f);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Cúpula presurizada de la antigua base: casco claro, costillas, ventanas ámbar y módulos a los lados.</summary>
    public static ArrayMesh LandmarkDome()
    {
        const string key = "lm_dome";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        const float radius = 27f;
        const float rise = 21f;
        const int ribs = 16;
        var profile = new (float r, float y)[10];
        for (var i = 0; i < profile.Length; i++)
        {
            var a = i / (float)(profile.Length - 1) * Mathf.Pi / 2;
            profile[i] = (Mathf.Cos(a) * radius, Mathf.Sin(a) * rise + 2.4f);
        }
        b.Lathe("cream", 0, 0, 0, profile, 32);
        b.Lathe("hull", 0, 0, 0, new[] { (radius + 1.7f, -2f), (radius + 1.7f, 2.4f), (radius - 1f, 3.4f) }, 32);
        for (var k = 0; k < ribs; k++)
        {
            var yaw = k * Mathf.Tau / ribs;
            var dir = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
            var path = new Vector3[6];
            for (var i = 0; i < path.Length; i++)
            {
                var a = i / 5f * Mathf.Pi * 0.48f;
                path[i] = dir * (Mathf.Cos(a) * (radius + 0.25f)) + Vector3.Up * (Mathf.Sin(a) * (rise + 0.25f) + 2.4f);
            }
            for (var i = 0; i < path.Length - 1; i++) b.Beam("steel_dark", path[i], path[i + 1], 0.5f, 0.5f);
            // Ventanal entre costillas: faja encendida que se lee de noche a 200 m.
            var w = yaw + Mathf.Pi / ribs;
            var at = new Vector3(Mathf.Cos(w), 0, Mathf.Sin(w)) * (radius - 0.5f) + Vector3.Up * 7.4f;
            b.Box("amber", at.X, at.Y, at.Z, 0.25f, 1.25f, 5.2f, yaw: -Mathf.RadToDeg(w), bevel: 0);
        }
        // Cima con anillo de servicio y mástil.
        b.Lathe("steel_dark", 0, rise + 1.6f, 0, new[] { (5.4f, 0f), (5.4f, 1.1f), (3.8f, 1.8f), (0f, 2f) }, 16);
        b.Cyl("steel", 0, rise + 7f, 0, 0.2f, 8f, sides: 6);
        // Dos módulos y un túnel presurizado.
        foreach (var (x, z, r) in new[] { (38f, 14f, 8f), (31f, -28f, 6f) })
        {
            b.Lathe("panel", x, 0, z, new[] { (r, -1f), (r, 2.5f), (r * 0.8f, r * 0.75f), (r * 0.42f, r * 1.02f), (0f, r * 1.08f) }, 16);
            b.Box("amber", x - r * 0.97f, 2.1f, z, 0.2f, 0.8f, r * 0.6f, bevel: 0);
            var tube = new Vector3(x, 2.5f, z);
            b.Tube("steel_dark", 1.5f, 8, tube, tube.Lerp(Vector3.Up * 2.5f, 0.8f));
        }
        // Depósitos en batería.
        for (var i = 0; i < 3; i++) b.Cyl("hull", -34f, 3.5f, -14f + i * 7f, 2.6f, 9f, sides: 12);
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Pecio de un carguero: morro hundido, sección central sin chapa con las cuadernas al aire y cola suelta con su deriva.</summary>
    public static ArrayMesh LandmarkWreck()
    {
        const string key = "lm_wreck";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        const float roll = -74f;
        var axis = Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(roll))) * Vector3.Up;
        // Morro y mitad delantera.
        b.Lathe("navy", 0, 3, 0, new[] { (1f, 0f), (5f, 3f), (8f, 9f), (9f, 18f), (9f, 34f) }, 20, roll: roll);
        var band = axis * 22f + new Vector3(0, 3, 0);
        b.Lathe("signal", band.X, band.Y, band.Z, new[] { (9.15f, 0f), (9.15f, 5.5f) }, 20, roll: roll);
        // Costillar al aire: cuadernas y largueros sin forro.
        var start = axis * 34f + new Vector3(0, 3, 0);
        for (var i = 0; i < 6; i++)
        {
            var at = start + axis * (i * 3.2f);
            b.Lathe("steel_dark", at.X, at.Y, at.Z, new[] { (8.9f - i * 0.1f, 0f), (9.2f - i * 0.1f, 0f), (9.2f - i * 0.1f, 0.7f), (8.9f - i * 0.1f, 0.7f) }, 16, roll: roll);
        }
        for (var i = 0; i < 7; i++)
        {
            var a = i * Mathf.Tau / 7 + 0.3f;
            var off = Basis.FromEuler(new Vector3(0, 0, Mathf.DegToRad(roll))) * new Vector3(Mathf.Cos(a) * 8.8f, 0, Mathf.Sin(a) * 8.8f);
            b.Beam(i % 3 == 0 ? "rust" : "steel_dark", start + off, start + off + axis * (i % 2 == 0 ? 17f : 11.5f), 0.6f, 0.6f);
        }
        // Cola suelta, algo girada, con deriva alta: es lo que se ve por encima del relieve.
        var tail = new Vector3(66f, 3.2f, 13f);
        b.Lathe("navy", tail.X, tail.Y, tail.Z, new[] { (8.6f, 0f), (9f, 6f), (7.6f, 16f), (4f, 24f) }, 18, roll: -84f, yaw: -18f);
        b.Box("signal", tail.X + 12f, 18f, tail.Z - 3.6f, 15f, 26f, 1.1f, yaw: -18f, bevel: 0.15f);
        b.Box("cream", tail.X + 14f, 26f, tail.Z - 4.2f, 9f, 5f, 1.25f, yaw: -18f, bevel: 0.1f);
        foreach (var z in new[] { -4f, 4f }) b.Cyl("steel_dark", tail.X + 24f, 4f, tail.Z + z - 3f, 3.1f, 6f, roll: -84f, sides: 12);
        // Chapas del forro y tierra removida por el impacto.
        var r = new Rng(917);
        for (var i = 0; i < 9; i++)
        {
            var x = r.Range(-14f, 70f);
            var z = r.Range(-22f, 26f);
            b.Box(i % 3 == 0 ? "navy" : "rust", x, 0.9f, z, r.Range(4f, 10f), 0.25f, r.Range(3f, 8f), yaw: r.Range(0, 180), pitch: r.Range(-16, 16), roll: r.Range(-14, 14), bevel: 0.05f);
        }
        for (var i = 0; i < 7; i++)
        {
            var a = i * 0.7f - 2.2f;
            b.Rock("regolith", -2f + Mathf.Cos(a) * -10f, 0.4f, Mathf.Sin(a) * 12f, r.Range(7f, 11f), r.Range(2.5f, 4.5f), r.Range(6f, 10f), 90 + i, 1);
        }
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }

    /// <summary>Carga a granel del vagón. Se escala en altura con el llenado real, como la caja a la que sustituye.</summary>
    public static ArrayMesh WagonLoad()
    {
        const string key = "wagon_load";
        if (DebrisCache.TryGetValue(key, out var mesh)) return mesh;
        var b = new Builder();
        var r = new Rng(433);
        b.Box("rust", 0, -0.2f, 0, 2f, 0.6f, 5.7f, bevel: 0);
        for (var i = 0; i < 11; i++)
        {
            var z = -2.4f + i * 0.48f;
            b.Rock(i % 3 == 0 ? "rustlight" : "rust", r.Range(-0.45f, 0.45f), 0.14f, z, r.Range(0.42f, 0.62f), r.Range(0.26f, 0.42f), r.Range(0.4f, 0.56f), 200 + i, 0);
        }
        mesh = b.Commit();
        DebrisCache[key] = mesh;
        return mesh;
    }
}
