namespace Pluton.Simulation;

/// <summary>Cantidades enteras por objeto. Nunca baja de cero; el orden de iteración es estable.</summary>
public sealed class Inventory
{
    private readonly SortedDictionary<string, long> _items = new(StringComparer.Ordinal);

    public IEnumerable<KeyValuePair<string, long>> Items => _items;
    public int Kinds => _items.Count;
    public long Get(string item) => _items.TryGetValue(item, out var qty) ? qty : 0;

    public long Total
    {
        get
        {
            long total = 0;
            foreach (var kv in _items) total += kv.Value;
            return total;
        }
    }

    public void Add(string item, long qty)
    {
        if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (qty == 0) return;
        _items[item] = checked(Get(item) + qty);
    }

    public bool TryRemove(string item, long qty)
    {
        if (qty < 0) throw new ArgumentOutOfRangeException(nameof(qty));
        if (qty == 0) return true;
        var have = Get(item);
        if (have < qty) return false;
        if (have == qty) _items.Remove(item);
        else _items[item] = have - qty;
        return true;
    }

    public void Clear() => _items.Clear();
}

public enum MachineStatus : byte { Idle, Working, NoInput, OutputFull, NoPower, LowPower, Generating, Storing, Ready, Overheated, Cooling, Passing }

public sealed class Machine
{
    public int Id;
    public string Def = "";
    public int Planet;
    public int X, Z, Rot;
    public bool Builtin;
    public int NodeId = -1;
    public int RailA = -1, RailB = -1;
    public bool StationUnload;
    public readonly Inventory In = new();
    public readonly Inventory Out = new();
    public bool Processing;
    public long Progress;
    public MachineStatus Status;
    public bool AutoSell;
    public readonly SortedDictionary<string, long> Reserves = new(StringComparer.Ordinal);
    public int SellCooldown;
    /// <summary>Solo en el servidor: ciclos completados en la ventana móvil. Derivado, no se guarda ni entra en firmas.</summary>
    public RateWindow? Window;
}

/// <summary>Cinta abierta entre dos máquinas. Sin cuerpo físico: se puede pisar.</summary>
public sealed class Link
{
    public int Id;
    public int From;
    public int To;
    public int Planet;
    /// <summary>Ruta ordenada de origen a destino. Vacía si la cinta es elevada.</summary>
    public readonly List<(int x, int z)> Cells = new();
    /// <summary>Carga real por huecos; el índice 0 es la entrada y el último, la cabeza que llega a destino.</summary>
    public string?[] Slots = Array.Empty<string?>();
    /// <summary>Recta sobre soportes, sin ocupar celdas: enlace antiguo sin ruta posible.</summary>
    public bool Elevated;
    /// <summary>Créditos pagados al tenderla; es lo que se devuelve al retirarla.</summary>
    public long Paid;
    /// <summary>Acumulador del avance (ticks por velocidad).</summary>
    public int Accum;
    /// <summary>Último turno servido al repartir entre varias salidas de una misma máquina.</summary>
    public long Turn;
    /// <summary>Última entrega a un divisor o unificador compartido. Derivado: al cargar la partida el reparto vuelve a empezar.</summary>
    public long DeliveredTurn;
    /// <summary>Huecos ocupados. Derivado de Slots.</summary>
    public int Load;
    /// <summary>Solo en el espejo del cliente: cuántas veces ha llegado carga nueva. La vista lo usa para corregir su copia.</summary>
    public int SlotsVersion;
}

public sealed class Debris
{
    public int Id;
    public string Def = "";
    public int Planet;
    public int X, Z;
    public int Hp;
    public int CoversNode = -1;
}

public sealed class ResourceNode
{
    public int Id;
    public string Item = "";
    public int Planet;
    public int X, Z;
    public bool Revealed;
    public int OccupiedBy = -1;
    public int YieldPermille = 1000;
}

public sealed class RailNode
{
    public int Id;
    public int Planet;
    public int X, Z; // esquinas de celda
    /// <summary>Señal: divide las rutas en bloques en este punto. Vale para los dos sentidos.</summary>
    public bool Signal;
}

public sealed class RailSegment
{
    public int Id;
    public int Planet;
    public int A, B;
    public byte Shape; // 0 recta, 1 curva por (B.X, A.Z), 2 curva por (A.X, B.Z)
    public long LengthMm;
    public int OwnerStation = -1;
}

/// <summary>Carga en vuelo entre plataformas: pertenece a este registro hasta llegar, nunca a ambos planetas.</summary>
public sealed class Shipment
{
    public int Id;
    public int ToPlanet;
    public long ArriveTick;
    public readonly Inventory Cargo = new();
}

public enum TrainState : byte { Loading, Moving, Unloading, Waiting, AtSignal }

public sealed class Train
{
    public int Id;
    public int Planet;
    public int StationA, StationB;
    public bool HeadingToB = true;
    public TrainState State = TrainState.Loading;
    public string WaitReason = "";
    public readonly Inventory Cargo = new();
    public readonly List<int> Path = new();
    public int PathIndex;
    public int CurrentNode = -1;
    public bool Forward = true; // sentido A→B en el tramo actual
    public long PosMm;
    public int Timer;
    /// <summary>Tramos de la ruta ya concedidos: el tren se detiene al final del último si no obtiene el siguiente bloque.</summary>
    public int GrantEnd;
    /// <summary>Tiempo detenido ante la señal actual.</summary>
    public int HoldTicks;
}

public enum Role : byte { Visitor, Builder, Admin, Owner }

public sealed class PlayerState
{
    public string Key = "";
    public int Num;
    public string Name = "";
    public Role Role = Role.Builder;
    public bool Connected;
    public int Planet;
    public float X, Y, Z, Yaw;
    public int TransitTo = -1;
    public long TransitStartTick, TransitEndTick;
    public readonly Inventory Bag = new();
    public long CutterReadyTick;
}

public enum CommandType : byte
{
    CutDebris, Place, Remove, Move, Link, Unlink, Insert, Extract, Sell, SetAutoSell,
    FulfillContract, BuyUpgrade, SetRole, Kick,
    PlaceRail, RemoveRail, StationMode, CreateTrain, RemoveTrain, Travel, SetEvent, SetCosmetic, InsertJunction, ToggleSignal,
}

/// <summary>Intención de un jugador. Todo lo que contiene se valida antes de aplicarse.</summary>
public sealed class Command
{
    public CommandType Type;
    public int A, B, C, D;
    public long Amount;
    public string Text = "";
    /// <summary>Solo en Link: la ruta exacta previsualizada por el cliente. El primer y último elemento fijan los conectores.</summary>
    public List<(int x, int z)>? Cells;
}

public readonly record struct CommandResult(bool Ok, string Error)
{
    public static readonly CommandResult Success = new(true, "");
    public static CommandResult Fail(string error) => new(false, error);
}
