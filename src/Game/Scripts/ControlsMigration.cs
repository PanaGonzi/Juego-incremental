namespace Pluton.Game;

/// <summary>Migración de ajustes locales. No depende de Godot para poder probarse sin escena.</summary>
public static class ControlsMigration
{
    public const int Current = 1;

    // Valores de Godot.Key: el formato de los ajustes guarda el código numérico.
    public const long KeyTab = 4194306;
    public const long KeyE = 69;
    public const long KeyQ = 81;

    /// <summary>
    /// Pasa a la distribución convencional (Tab inventario, E interactuar, Q catálogo) sin perder lo que el jugador reasignó.
    /// Solo se retira lo que equivale a la asignación antigua por omisión, porque dejarlo bloquearía la tecla nueva.
    /// </summary>
    public static void Migrate(IDictionary<string, long> bindings, int from)
    {
        if (from >= 1) return;
        RemoveIf(bindings, "tech", KeyTab);
        RemoveIf(bindings, "cancel", KeyQ);
        RemoveIf(bindings, "interact_insert", KeyE);
    }

    private static void RemoveIf(IDictionary<string, long> bindings, string action, long key)
    {
        if (bindings.TryGetValue(action, out var bound) && bound == key) bindings.Remove(action);
    }
}

/// <summary>Contenido de la barra rápida: una lista fija de casillas con herramientas o construcciones.</summary>
public static class HotbarLayout
{
    public const int Slots = 9;
    public const string Cutter = "tool:cutter";

    public static readonly string[] Default =
    {
        Cutter, "tool:link", "tool:remove", "tool:rail",
        "build:recycler", "build:ore_extractor", "build:smelter", "build:storage_crate", "build:rtg_generator",
    };

    /// <summary>Corrige lo guardado: nueve casillas, la herramienta en la primera y sin repetidos.</summary>
    public static List<string> Normalize(IEnumerable<string>? saved)
    {
        var source = saved?.ToList();
        if (source == null || source.Count == 0) return Default.ToList();
        var seen = new HashSet<string> { Cutter };
        var result = new List<string> { Cutter };
        foreach (var entry in source.Skip(1))
        {
            if (result.Count == Slots) break;
            result.Add(IsValid(entry) && seen.Add(entry) ? entry : "");
        }
        while (result.Count < Slots) result.Add("");
        return result;
    }

    /// <summary>Coloca una entrada en una casilla. Si ya estaba en otra, intercambia: nunca hay dos iguales ni se pierde la herramienta.</summary>
    public static void Assign(IList<string> slots, int index, string entry)
    {
        if (index <= 0 || index >= slots.Count || !IsValid(entry) || entry == Cutter) return;
        var previous = slots.IndexOf(entry);
        if (previous >= 0) slots[previous] = slots[index];
        slots[index] = entry;
    }

    public static void Clear(IList<string> slots, int index)
    {
        if (index > 0 && index < slots.Count) slots[index] = "";
    }

    public static int FirstFree(IList<string> slots)
    {
        for (var i = 1; i < slots.Count; i++)
            if (slots[i].Length == 0) return i;
        return -1;
    }

    /// <summary>Siguiente casilla con contenido en la dirección de la ruedecilla; da la vuelta.</summary>
    public static int Step(IList<string> slots, int from, int direction)
    {
        var count = slots.Count;
        var at = from < 0 ? (direction > 0 ? -1 : 0) : from;
        for (var i = 0; i < count; i++)
        {
            at = ((at + direction) % count + count) % count;
            if (slots[at].Length > 0) return at;
        }
        return from;
    }

    public static bool IsValid(string entry) =>
        entry.StartsWith("tool:", StringComparison.Ordinal) && entry.Length > 5 ||
        entry.StartsWith("build:", StringComparison.Ordinal) && entry.Length > 6;
}
