namespace Pluton.Simulation;

/// <summary>
/// Qué conectores conviene señalar. Una pieza grande tiene decenas de ellos y dibujarlos todos tapa justo lo que hay que mirar (INC-20).
/// Solo decide qué se dibuja: los conectores reales, y lo que se puede conectar, no cambian.
/// </summary>
public static class PortIndicators
{
    /// <summary>Hasta este número se señalan todos: en piezas pequeñas ver cada conector no estorba.</summary>
    public const int ToolShowAllLimit = 6;
    private const int SelectedFaceLimit = 4;

    /// <summary>Al colocar una pieza importa dónde puede entrar y salir: una marca por cara y sentido.</summary>
    public static List<BeltPort> ForPlacement(IEnumerable<BeltPort> ports) =>
        ports.GroupBy(p => (p.Dir, p.Output)).OrderBy(g => g.Key.Dir).ThenBy(g => g.Key.Output).Select(g => Representative(g.ToList())).ToList();

    /// <summary>
    /// Al tender una cinta: el conector elegido siempre; en su cara, sus vecinos libres para poder cambiar de uno a otro;
    /// y en las demás caras, una sola marca.
    /// </summary>
    public static List<BeltPort> ForTool(IEnumerable<BeltPort> ports, (int x, int z)? selected)
    {
        var all = ports.ToList();
        if (all.Count <= ToolShowAllLimit) return all;
        var chosen = selected == null ? null : all.Where(p => (p.X, p.Z) == selected).Cast<BeltPort?>().FirstOrDefault();
        var result = new List<BeltPort>();
        foreach (var face in all.GroupBy(p => (p.Dir, p.Output)).OrderBy(g => g.Key.Dir).ThenBy(g => g.Key.Output))
        {
            var list = face.ToList();
            if (chosen is { } c && c.Dir == face.Key.Dir && c.Output == face.Key.Output)
            {
                result.Add(c);
                result.AddRange(list.Where(p => p.Free && p != c).OrderBy(p => Math.Abs(p.X - c.X) + Math.Abs(p.Z - c.Z)).Take(SelectedFaceLimit - 1));
            }
            else result.Add(Representative(list));
        }
        return result;
    }

    /// <summary>El conector libre más centrado de la cara; si todos están ocupados, el más centrado, que se verá en gris.</summary>
    private static BeltPort Representative(List<BeltPort> face)
    {
        var candidates = face.Any(p => p.Free) ? face.Where(p => p.Free).ToList() : face;
        var centerX = face.Average(p => p.X);
        var centerZ = face.Average(p => p.Z);
        return candidates.OrderBy(p => Math.Abs(p.X - centerX) + Math.Abs(p.Z - centerZ)).ThenBy(p => p.X).ThenBy(p => p.Z).First();
    }
}
