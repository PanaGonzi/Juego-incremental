using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pluton.Simulation;

public enum MachineKind { Production, Generator, Storage, Terminal, Station }

public sealed class ItemDef
{
    public string Id { get; set; } = "";
    public long Price { get; set; }
}

public sealed class RecipeDef
{
    public string Id { get; set; } = "";
    public int Ticks { get; set; }
    public Dictionary<string, long> Inputs { get; set; } = new();
    public Dictionary<string, long> Outputs { get; set; } = new();
}

public sealed class MachineDef
{
    public string Id { get; set; } = "";
    public MachineKind Kind { get; set; }
    public int W { get; set; } = 1;
    public int D { get; set; } = 1;
    public int Power { get; set; }
    public int PowerGen { get; set; }
    public long Capacity { get; set; }
    public long Credits { get; set; }
    public Dictionary<string, long> Cost { get; set; } = new();
    public string? Recipe { get; set; }
    public string? Node { get; set; }
    public string? Unlock { get; set; }
    public bool Builtin { get; set; }
}

public sealed class UpgradeDef
{
    public string Id { get; set; } = "";
    public long Credits { get; set; }
    public Dictionary<string, long> Cost { get; set; } = new();
    public string? Requires { get; set; }
    public Dictionary<string, int> Mods { get; set; } = new();
}

public sealed class ContractDef
{
    public string Id { get; set; } = "";
    public Dictionary<string, long> Items { get; set; } = new();
    public long Credits { get; set; }
    public string? Requires { get; set; }
}

public sealed class ObjectiveDef
{
    public string Id { get; set; } = "";
    public string Stat { get; set; } = "";
    public long Qty { get; set; }
    public long Credits { get; set; }
}

public sealed class ProgressionDef
{
    public List<UpgradeDef> Upgrades { get; set; } = new();
    public List<ContractDef> Contracts { get; set; } = new();
    public List<ObjectiveDef> Objectives { get; set; } = new();
}

public sealed class CellDef
{
    public int X { get; set; }
    public int Z { get; set; }
}

public sealed class BuiltinDef
{
    public string Def { get; set; } = "";
    public int X { get; set; }
    public int Z { get; set; }
}

public sealed class NodeDef
{
    public string Item { get; set; } = "";
    public int X { get; set; }
    public int Z { get; set; }
    public int YieldPermille { get; set; } = 1000;
}

public sealed class DebrisFieldDef
{
    public string Def { get; set; } = "";
    public int X { get; set; }
    public int Z { get; set; }
    public int Radius { get; set; }
    public int Count { get; set; }
    public int Seed { get; set; }
}

public sealed class PlanetDef
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public int HalfSizeCells { get; set; }
    public long StartCredits { get; set; }
    public CellDef Spawn { get; set; } = new();
    public List<BuiltinDef> Builtins { get; set; } = new();
    public List<NodeDef> Nodes { get; set; } = new();
    public List<DebrisFieldDef> DebrisFields { get; set; } = new();
}

public sealed class DebrisDef
{
    public int Hp { get; set; }
    public string Drop { get; set; } = "";
    public long PerHit { get; set; }
    public long Final { get; set; }
    public long Credits { get; set; }
    public int Cells { get; set; } = 1;
}

public sealed class RailDef
{
    public long CreditsPerCell { get; set; }
    public int MaxStraightCells { get; set; }
    public int MaxCurveCells { get; set; }
    public long TrainCredits { get; set; }
    public Dictionary<string, long> TrainCost { get; set; } = new();
    public int TrainSpeedMmPerTick { get; set; }
    public long TrainCapacity { get; set; }
    public long TransferUnitsPerTick { get; set; }
    public int MaxLoadWaitTicks { get; set; }
}

public sealed class BalanceDef
{
    public RailDef Rail { get; set; } = new();
    public int CellSizeMm { get; set; } = 2000;
    public int TicksPerSecond { get; set; } = 10;
    public long MaxCredits { get; set; }
    public long PersonalStackLimit { get; set; }
    public int CutterRangeMm { get; set; }
    public int InteractRangeMm { get; set; }
    public int BuildRangeMm { get; set; }
    public int CutterCooldownTicks { get; set; }
    public int LinkMaxMm { get; set; }
    public int LinkIntervalTicks { get; set; }
    public int RecipeBufferFactor { get; set; }
    public long OutputBuffer { get; set; }
    public int RefundPermille { get; set; }
    public int SellIntervalTicks { get; set; }
    public int SellUnitsPerInterval { get; set; }
    public int OfflineCapHours { get; set; }
    public Dictionary<string, DebrisDef> Debris { get; set; } = new();
}

/// <summary>Conjunto de datos validado e inmutable; el servidor lo identifica por su revisión.</summary>
public sealed class ContentDb
{
    private static readonly string[] Files =
        { "items.json", "recipes.json", "machines.json", "progression.json", "planets.json", "balance.json" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Revision { get; private set; } = "";
    public Dictionary<string, ItemDef> Items { get; private set; } = new();
    public Dictionary<string, RecipeDef> Recipes { get; private set; } = new();
    public Dictionary<string, MachineDef> Machines { get; private set; } = new();
    public Dictionary<string, UpgradeDef> Upgrades { get; private set; } = new();
    public Dictionary<string, ContractDef> Contracts { get; private set; } = new();
    public List<ObjectiveDef> Objectives { get; private set; } = new();
    public List<PlanetDef> Planets { get; private set; } = new();
    public BalanceDef Balance { get; private set; } = new();

    /// <summary>Lee y valida un directorio completo. Lanza ContentException sin efectos parciales.</summary>
    public static ContentDb Load(string dir)
    {
        var raw = new Dictionary<string, string>();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Files)
        {
            var path = Path.Combine(dir, file);
            if (!File.Exists(path)) throw new ContentException($"Falta {file} en {dir}");
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            raw[file] = text;
            sha.AppendData(Encoding.UTF8.GetBytes(file));
            sha.AppendData(Encoding.UTF8.GetBytes(text));
        }

        try
        {
            var progression = Parse<ProgressionDef>(raw["progression.json"]);
            var db = new ContentDb
            {
                Revision = Convert.ToHexString(sha.GetHashAndReset())[..16].ToLowerInvariant(),
                Items = Parse<List<ItemDef>>(raw["items.json"]).ToDictionary(x => x.Id),
                Recipes = Parse<List<RecipeDef>>(raw["recipes.json"]).ToDictionary(x => x.Id),
                Machines = Parse<List<MachineDef>>(raw["machines.json"]).ToDictionary(x => x.Id),
                Upgrades = progression.Upgrades.ToDictionary(x => x.Id),
                Contracts = progression.Contracts.ToDictionary(x => x.Id),
                Objectives = progression.Objectives,
                Planets = Parse<List<PlanetDef>>(raw["planets.json"]),
                Balance = Parse<BalanceDef>(raw["balance.json"]),
            };
            db.Validate();
            return db;
        }
        catch (JsonException e) { throw new ContentException("JSON inválido: " + e.Message); }
        catch (ArgumentException e) { throw new ContentException("ID duplicado: " + e.Message); }
    }

    private static T Parse<T>(string text) =>
        JsonSerializer.Deserialize<T>(text, Json) ?? throw new ContentException("Archivo vacío");

    private void Validate()
    {
        void Item(string id, string where)
        {
            if (!Items.ContainsKey(id)) throw new ContentException($"{where}: objeto desconocido '{id}'");
        }
        void Amounts(Dictionary<string, long> map, string where)
        {
            foreach (var (id, qty) in map)
            {
                Item(id, where);
                if (qty <= 0) throw new ContentException($"{where}: cantidad no positiva de '{id}'");
            }
        }

        foreach (var r in Recipes.Values)
        {
            if (r.Ticks <= 0) throw new ContentException($"Receta {r.Id}: ticks debe ser positivo");
            Amounts(r.Inputs, "Receta " + r.Id);
            Amounts(r.Outputs, "Receta " + r.Id);
        }
        foreach (var u in Upgrades.Values)
        {
            Amounts(u.Cost, "Mejora " + u.Id);
            if (u.Requires != null && !Upgrades.ContainsKey(u.Requires))
                throw new ContentException($"Mejora {u.Id}: requisito desconocido");
        }
        foreach (var m in Machines.Values)
        {
            Amounts(m.Cost, "Máquina " + m.Id);
            if (m.W <= 0 || m.D <= 0) throw new ContentException($"Máquina {m.Id}: tamaño inválido");
            if (m.Recipe != null && !Recipes.ContainsKey(m.Recipe))
                throw new ContentException($"Máquina {m.Id}: receta desconocida");
            if (m.Kind == MachineKind.Production && m.Recipe == null)
                throw new ContentException($"Máquina {m.Id}: producción sin receta");
            if (m.Unlock != null && !Upgrades.ContainsKey(m.Unlock))
                throw new ContentException($"Máquina {m.Id}: desbloqueo desconocido");
            if (m.Node != null) Item(m.Node, "Máquina " + m.Id);
        }
        foreach (var c in Contracts.Values)
        {
            Amounts(c.Items, "Contrato " + c.Id);
            if (c.Credits < 0) throw new ContentException($"Contrato {c.Id}: recompensa negativa");
            if (c.Requires != null && !Upgrades.ContainsKey(c.Requires))
                throw new ContentException($"Contrato {c.Id}: requisito desconocido");
        }
        foreach (var d in Balance.Debris.Values) Item(d.Drop, "Escombro");
        foreach (var p in Planets)
        {
            foreach (var b in p.Builtins)
                if (!Machines.ContainsKey(b.Def)) throw new ContentException($"Planeta {p.Key}: máquina '{b.Def}'");
            foreach (var n in p.Nodes) Item(n.Item, "Planeta " + p.Key);
            foreach (var f in p.DebrisFields)
                if (!Balance.Debris.ContainsKey(f.Def)) throw new ContentException($"Planeta {p.Key}: escombro '{f.Def}'");
        }
        if (Planets.Count == 0) throw new ContentException("No hay planetas");
        if (Balance.TicksPerSecond <= 0 || Balance.CellSizeMm <= 0) throw new ContentException("Balance inválido");
    }

    /// <summary>Busca content/ junto al ejecutable o en carpetas superiores (desarrollo).</summary>
    public static string LocateDirectory(string? startDir = null)
    {
        var dir = startDir ?? AppContext.BaseDirectory;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "content");
            if (File.Exists(Path.Combine(candidate, "items.json"))) return candidate;
        }
        throw new ContentException($"No se encontró la carpeta content desde {dir}");
    }
}

public sealed class ContentException : Exception
{
    public ContentException(string message) : base(message) { }
}
