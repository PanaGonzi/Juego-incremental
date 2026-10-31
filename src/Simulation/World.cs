namespace Pluton.Simulation;

/// <summary>Estado autoritativo compartido y sus reglas. No conoce Godot, red ni disco.</summary>
public sealed partial class World
{
    public readonly ContentDb Content;
    public long Tick;
    public long Credits;
    public int NextId = 1;
    public int NextPlayerNum = 1;
    public int ObjectiveIndex;
    public string OwnerKey = "";

    public readonly SortedDictionary<int, Machine> Machines = new();
    public readonly SortedDictionary<int, Link> Links = new();
    public readonly SortedDictionary<int, Debris> DebrisById = new();
    public readonly SortedDictionary<int, ResourceNode> Nodes = new();
    public readonly SortedDictionary<string, PlayerState> Players = new(StringComparer.Ordinal);
    public readonly SortedSet<string> Upgrades = new(StringComparer.Ordinal);
    public readonly SortedSet<string> ContractsDone = new(StringComparer.Ordinal);
    public readonly SortedDictionary<string, long> Stats = new(StringComparer.Ordinal);

    // Índices derivados: se reconstruyen al cargar, nunca se guardan.
    private readonly Dictionary<(int planet, int x, int z), int> _occupied = new();
    private readonly Dictionary<(int planet, int x, int z), int> _debrisCells = new();
    private readonly Dictionary<string, int> _mods = new(StringComparer.Ordinal);
    private Machine[] _machineList = Array.Empty<Machine>();
    private Link[] _linkList = Array.Empty<Link>();

    // Cambios pendientes de replicar a los clientes.
    public readonly HashSet<int> DirtyMachines = new();
    public readonly HashSet<int> DirtyDebris = new();
    public readonly HashSet<int> DirtyNodes = new();
    public readonly List<(int planet, int id)> RemovedMachines = new();
    public readonly List<(int planet, int id)> RemovedDebris = new();
    public readonly List<(int planet, int id)> RemovedLinks = new();
    public readonly HashSet<int> DirtyLinks = new();
    public readonly HashSet<string> DirtyPlayers = new(StringComparer.Ordinal);
    public bool DirtyGlobal = true;

    public World(ContentDb content) { Content = content; }

    private BalanceDef B => Content.Balance;

    public static World CreateNew(ContentDb content)
    {
        var w = new World(content);
        foreach (var planet in content.Planets)
        {
            w.Credits += planet.StartCredits;
            foreach (var b in planet.Builtins)
            {
                var m = new Machine { Id = w.NextId++, Def = b.Def, Planet = planet.Id, X = b.X, Z = b.Z, Builtin = true };
                w.Machines[m.Id] = m;
            }
            foreach (var n in planet.Nodes)
            {
                var node = new ResourceNode { Id = w.NextId++, Item = n.Item, Planet = planet.Id, X = n.X, Z = n.Z, YieldPermille = n.YieldPermille };
                w.Nodes[node.Id] = node;
                for (var dx = 0; dx < 2; dx++)
                for (var dz = 0; dz < 2; dz++)
                {
                    var d = new Debris { Id = w.NextId++, Def = "ice_crust", Planet = planet.Id, X = n.X + dx, Z = n.Z + dz, CoversNode = node.Id };
                    d.Hp = content.Balance.Debris[d.Def].Hp;
                    w.DebrisById[d.Id] = d;
                }
            }
            w.Reindex();
            foreach (var f in planet.DebrisFields) w.ScatterDebris(planet, f);
        }
        w.Reindex();
        return w;
    }

    private void ScatterDebris(PlanetDef planet, DebrisFieldDef f)
    {
        var rng = new Lcg((uint)f.Seed);
        var placed = 0;
        for (var attempt = 0; attempt < f.Count * 20 && placed < f.Count; attempt++)
        {
            var x = f.X + rng.Range(-f.Radius, f.Radius + 1);
            var z = f.Z + rng.Range(-f.Radius, f.Radius + 1);
            if ((x - f.X) * (x - f.X) + (z - f.Z) * (z - f.Z) > f.Radius * f.Radius) continue;
            // Se mantiene libre la zona inicial para no bloquear las primeras máquinas.
            if (Math.Abs(x - planet.Spawn.X) < 4 && Math.Abs(z - planet.Spawn.Z) < 4) continue;
            if (_occupied.ContainsKey((planet.Id, x, z)) || _debrisCells.ContainsKey((planet.Id, x, z))) continue;
            if (IsOnNode(planet.Id, x, z)) continue;
            var d = new Debris { Id = NextId++, Def = f.Def, Planet = planet.Id, X = x, Z = z, Hp = B.Debris[f.Def].Hp };
            DebrisById[d.Id] = d;
            _debrisCells[(planet.Id, x, z)] = d.Id;
            placed++;
        }
    }

    private bool IsOnNode(int planet, int x, int z)
    {
        foreach (var n in Nodes.Values)
            if (n.Planet == planet && x >= n.X - 1 && x <= n.X + 2 && z >= n.Z - 1 && z <= n.Z + 2) return true;
        return false;
    }

    /// <summary>Reconstruye índices y modificadores tras crear, cargar o aplicar una instantánea.</summary>
    public void Reindex()
    {
        _occupied.Clear();
        _debrisCells.Clear();
        foreach (var m in Machines.Values)
            foreach (var cell in Footprint(m.Def, m.X, m.Z, m.Rot))
                _occupied[(m.Planet, cell.x, cell.z)] = m.Id;
        foreach (var d in DebrisById.Values) _debrisCells[(d.Planet, d.X, d.Z)] = d.Id;
        foreach (var n in Nodes.Values)
        {
            n.OccupiedBy = -1;
            foreach (var m in Machines.Values)
                if (m.NodeId == n.Id) n.OccupiedBy = m.Id;
        }
        _machineList = Machines.Values.ToArray();
        _linkList = Links.Values.ToArray();
        ReindexRail();
        RecomputeMods();
    }

    private void RecomputeMods()
    {
        _mods.Clear();
        foreach (var id in Upgrades)
            if (Content.Upgrades.TryGetValue(id, out var u))
                foreach (var (key, value) in u.Mods)
                    _mods[key] = _mods.GetValueOrDefault(key) + value;
    }

    public int Mod(string key) => _mods.GetValueOrDefault(key);

    public IEnumerable<(int x, int z)> Footprint(string def, int x, int z, int rot)
    {
        var d = Content.Machines[def];
        var (w, depth) = rot % 2 == 0 ? (d.W, d.D) : (d.D, d.W);
        for (var i = 0; i < w; i++)
        for (var j = 0; j < depth; j++)
            yield return (x + i, z + j);
    }

    public (long x, long z) MachineCenterMm(Machine m)
    {
        var d = Content.Machines[m.Def];
        var (w, depth) = m.Rot % 2 == 0 ? (d.W, d.D) : (d.D, d.W);
        return (m.X * (long)B.CellSizeMm + w * (long)B.CellSizeMm / 2, m.Z * (long)B.CellSizeMm + depth * (long)B.CellSizeMm / 2);
    }

    private bool InRange(PlayerState p, long xMm, long zMm, int rangeMm)
    {
        var dx = p.X * 1000.0 - xMm;
        var dz = p.Z * 1000.0 - zMm;
        return dx * dx + dz * dz <= (double)rangeMm * rangeMm;
    }

    public void Bump(string stat, long by = 1)
    {
        Stats[stat] = Stats.GetValueOrDefault(stat) + by;
        DirtyGlobal = true;
    }

    private void AddCredits(long amount)
    {
        Credits = Math.Min(B.MaxCredits, Credits + amount);
        DirtyGlobal = true;
    }

    // ---------- Comandos ----------

    public CommandResult Apply(PlayerState p, Command c)
    {
        var need = c.Type switch
        {
            CommandType.SetRole or CommandType.Kick => Role.Admin,
            CommandType.Sell or CommandType.SetAutoSell or CommandType.FulfillContract or CommandType.BuyUpgrade => Role.Builder,
            _ => Role.Builder,
        };
        if (p.Role < need) return CommandResult.Fail("err_permission");

        return c.Type switch
        {
            CommandType.CutDebris => Cut(p, c.A),
            CommandType.Place => Place(p, c.Text, c.A, c.B, c.C),
            CommandType.Remove => Remove(p, c.A),
            CommandType.Move => Move(p, c.A, c.B, c.C, c.D),
            CommandType.Link => AddLink(p, c.A, c.B),
            CommandType.Unlink => RemoveLink(p, c.A),
            CommandType.Insert => Insert(p, c.A),
            CommandType.Extract => Extract(p, c.A),
            CommandType.Sell => Sell(p, c.A, c.Text, c.Amount),
            CommandType.SetAutoSell => SetAutoSell(p, c.A, c.B != 0, c.Text, c.Amount),
            CommandType.FulfillContract => Fulfill(p, c.Text),
            CommandType.BuyUpgrade => Buy(p, c.Text),
            CommandType.SetRole => SetRole(p, c.Text, (Role)c.A),
            CommandType.PlaceRail => PlaceRail(p, c.A, c.B, c.C, c.D, c.Amount),
            CommandType.RemoveRail => RemoveRail(p, c.A),
            CommandType.StationMode => StationMode(p, c.A, c.B != 0),
            CommandType.CreateTrain => CreateTrain(p, c.A, c.B),
            CommandType.RemoveTrain => RemoveTrain(p, c.A),
            _ => CommandResult.Fail("err_invalid"),
        };
    }

    private CommandResult Cut(PlayerState p, int debrisId)
    {
        if (!DebrisById.TryGetValue(debrisId, out var d) || d.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (!InRange(p, (d.X * 2L + 1) * B.CellSizeMm / 2, (d.Z * 2L + 1) * B.CellSizeMm / 2, B.CutterRangeMm)) return CommandResult.Fail("err_range");
        if (Tick < p.CutterReadyTick) return CommandResult.Fail("err_cooldown");
        p.CutterReadyTick = Tick + B.CutterCooldownTicks;

        var def = B.Debris[d.Def];
        var damage = Math.Min(d.Hp, 1 + Mod("cutter_damage"));
        d.Hp -= damage;
        p.Bag.Add(def.Drop, def.PerHit * damage);
        DirtyPlayers.Add(p.Key);

        if (d.Hp > 0)
        {
            DirtyDebris.Add(d.Id);
            return CommandResult.Success;
        }

        p.Bag.Add(def.Drop, def.Final);
        AddCredits(def.Credits);
        DebrisById.Remove(d.Id);
        _debrisCells.Remove((d.Planet, d.X, d.Z));
        RemovedDebris.Add((d.Planet, d.Id));
        Bump("cleared");
        if (d.CoversNode >= 0 && Nodes.TryGetValue(d.CoversNode, out var node) && !node.Revealed)
        {
            var stillCovered = false;
            foreach (var other in DebrisById.Values)
                if (other.CoversNode == node.Id) { stillCovered = true; break; }
            if (!stillCovered)
            {
                node.Revealed = true;
                DirtyNodes.Add(node.Id);
                Bump("revealed");
            }
        }
        return CommandResult.Success;
    }

    /// <summary>Comprobación de colocación compartida por el servidor y la previsualización local.</summary>
    public string? CheckPlacement(int planet, string defId, int x, int z, int rot, int ignoreMachine, out int nodeId)
    {
        nodeId = -1;
        if (!Content.Machines.TryGetValue(defId, out var def) || def.Builtin && ignoreMachine < 0) return "err_invalid";
        if (def.Unlock != null && !Upgrades.Contains(def.Unlock)) return "err_locked";
        var half = Content.Planets.First(pl => pl.Id == planet).HalfSizeCells;
        foreach (var cell in Footprint(defId, x, z, rot))
        {
            if (Math.Abs(cell.x) >= half || Math.Abs(cell.z) >= half) return "err_bounds";
            if (_occupied.TryGetValue((planet, cell.x, cell.z), out var occupant) && occupant != ignoreMachine) return "err_blocked";
            if (_debrisCells.ContainsKey((planet, cell.x, cell.z))) return "err_debris";
            if (RailBlocksCell(planet, cell.x, cell.z) && ignoreMachine < 0) return "err_blocked";
        }
        if (def.Kind == MachineKind.Station)
        {
            var (a, b) = StationRailEnds(defId, x, z, rot);
            var error = CheckRail(planet, a.x, a.z, b.x, b.z, 0, out _);
            if (error != null) return error;
        }
        if (def.Node != null)
        {
            foreach (var n in Nodes.Values)
                if (n.Planet == planet && n.Item == def.Node && n.X == x && n.Z == z && n.Revealed && (n.OccupiedBy < 0 || n.OccupiedBy == ignoreMachine))
                {
                    nodeId = n.Id;
                    return null;
                }
            return "err_node";
        }
        return null;
    }

    /// <summary>Paga de la mochila y luego del depósito del planeta. Todo o nada.</summary>
    public bool CanAfford(PlayerState p, long credits, Dictionary<string, long> cost)
    {
        if (Credits < credits) return false;
        var depot = DepotOf(p.Planet);
        foreach (var (item, qty) in cost)
            if (p.Bag.Get(item) + (depot?.In.Get(item) ?? 0) < qty) return false;
        return true;
    }

    private bool Pay(PlayerState p, long credits, Dictionary<string, long> cost)
    {
        if (!CanAfford(p, credits, cost)) return false;
        var depot = DepotOf(p.Planet);
        Credits -= credits;
        DirtyGlobal = true;
        foreach (var (item, qty) in cost)
        {
            var fromBag = Math.Min(qty, p.Bag.Get(item));
            p.Bag.TryRemove(item, fromBag);
            if (qty > fromBag)
            {
                depot!.In.TryRemove(item, qty - fromBag);
                DirtyMachines.Add(depot.Id);
            }
        }
        DirtyPlayers.Add(p.Key);
        return true;
    }

    public Machine? DepotOf(int planet)
    {
        foreach (var m in _machineList)
            if (m.Planet == planet && m.Def == "depot") return m;
        return null;
    }

    private CommandResult Place(PlayerState p, string defId, int x, int z, int rot)
    {
        rot = ((rot % 4) + 4) % 4;
        var error = CheckPlacement(p.Planet, defId, x, z, rot, -1, out var nodeId);
        if (error != null) return CommandResult.Fail(error);
        var def = Content.Machines[defId];
        if (!InRange(p, x * (long)B.CellSizeMm, z * (long)B.CellSizeMm, B.BuildRangeMm)) return CommandResult.Fail("err_range");
        if (!Pay(p, def.Credits, def.Cost)) return CommandResult.Fail("err_cost");

        var m = new Machine { Id = NextId++, Def = defId, Planet = p.Planet, X = x, Z = z, Rot = rot, NodeId = nodeId };
        Machines[m.Id] = m;
        foreach (var cell in Footprint(defId, x, z, rot)) _occupied[(p.Planet, cell.x, cell.z)] = m.Id;
        if (nodeId >= 0)
        {
            Nodes[nodeId].OccupiedBy = m.Id;
            DirtyNodes.Add(nodeId);
        }
        if (def.Kind == MachineKind.Station) AttachStation(m);
        _machineList = Machines.Values.ToArray();
        DirtyMachines.Add(m.Id);
        Bump("built." + defId);
        return CommandResult.Success;
    }

    private CommandResult Remove(PlayerState p, int id)
    {
        if (!Machines.TryGetValue(id, out var m) || m.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (m.Builtin) return CommandResult.Fail("err_builtin");
        var (cx, cz) = MachineCenterMm(m);
        if (!InRange(p, cx, cz, B.BuildRangeMm)) return CommandResult.Fail("err_range");
        foreach (var t in Trains.Values)
            if (t.StationA == id || t.StationB == id) return CommandResult.Fail("err_rail_occupied");
        var depot = DepotOf(m.Planet);
        if (depot == null) return CommandResult.Fail("err_invalid");

        // Regla documentada: coste y contenido vuelven al depósito, aunque supere su capacidad.
        var def = Content.Machines[m.Def];
        foreach (var (item, qty) in def.Cost) depot.In.Add(item, qty * B.RefundPermille / 1000);
        AddCredits(def.Credits * B.RefundPermille / 1000);
        foreach (var kv in m.In.Items) depot.In.Add(kv.Key, kv.Value);
        foreach (var kv in m.Out.Items) depot.In.Add(kv.Key, kv.Value);
        if (m.Processing && def.Recipe != null)
            foreach (var (item, qty) in Content.Recipes[def.Recipe].Inputs) depot.In.Add(item, qty);
        DirtyMachines.Add(depot.Id);

        foreach (var link in Links.Values.Where(l => l.From == id || l.To == id).ToList())
        {
            Links.Remove(link.Id);
            RemovedLinks.Add((link.Planet, link.Id));
        }
        _linkList = Links.Values.ToArray();
        foreach (var cell in Footprint(m.Def, m.X, m.Z, m.Rot)) _occupied.Remove((m.Planet, cell.x, cell.z));
        if (m.NodeId >= 0 && Nodes.TryGetValue(m.NodeId, out var node))
        {
            node.OccupiedBy = -1;
            DirtyNodes.Add(node.Id);
        }
        foreach (var s in RailSegments.Values.Where(s => s.OwnerStation == id).ToList()) RemoveSegment(s);
        Machines.Remove(id);
        _machineList = Machines.Values.ToArray();
        DirtyMachines.Remove(id);
        RemovedMachines.Add((m.Planet, id));
        return CommandResult.Success;
    }

    private CommandResult Move(PlayerState p, int id, int x, int z, int rot)
    {
        if (!Machines.TryGetValue(id, out var m) || m.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (m.Builtin || Content.Machines[m.Def].Kind == MachineKind.Station) return CommandResult.Fail("err_builtin");
        rot = ((rot % 4) + 4) % 4;
        var error = CheckPlacement(p.Planet, m.Def, x, z, rot, id, out var nodeId);
        if (error != null) return CommandResult.Fail(error);
        if (!InRange(p, x * (long)B.CellSizeMm, z * (long)B.CellSizeMm, B.BuildRangeMm)) return CommandResult.Fail("err_range");

        foreach (var cell in Footprint(m.Def, m.X, m.Z, m.Rot)) _occupied.Remove((m.Planet, cell.x, cell.z));
        m.X = x; m.Z = z; m.Rot = rot;
        foreach (var cell in Footprint(m.Def, x, z, rot)) _occupied[(m.Planet, cell.x, cell.z)] = id;
        if (m.NodeId != nodeId && m.NodeId >= 0)
        {
            Nodes[m.NodeId].OccupiedBy = -1;
            DirtyNodes.Add(m.NodeId);
        }
        m.NodeId = nodeId;
        if (nodeId >= 0)
        {
            Nodes[nodeId].OccupiedBy = id;
            DirtyNodes.Add(nodeId);
        }
        DirtyMachines.Add(id);

        // Los enlaces que quedan demasiado largos se retiran; el jugador lo ve en el mundo.
        foreach (var link in Links.Values.Where(l => l.From == id || l.To == id).ToList())
            if (!LinkInRange(Machines[link.From], Machines[link.To]))
            {
                Links.Remove(link.Id);
                RemovedLinks.Add((link.Planet, link.Id));
            }
        _linkList = Links.Values.ToArray();
        return CommandResult.Success;
    }

    public bool LinkInRange(Machine a, Machine b)
    {
        var (ax, az) = MachineCenterMm(a);
        var (bx, bz) = MachineCenterMm(b);
        var dx = (double)(ax - bx);
        var dz = (double)(az - bz);
        return dx * dx + dz * dz <= (double)B.LinkMaxMm * B.LinkMaxMm;
    }

    private CommandResult AddLink(PlayerState p, int from, int to)
    {
        if (from == to) return CommandResult.Fail("err_invalid");
        if (!Machines.TryGetValue(from, out var a) || !Machines.TryGetValue(to, out var b)) return CommandResult.Fail("err_gone");
        if (a.Planet != p.Planet || b.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (!LinkInRange(a, b)) return CommandResult.Fail("err_link_range");
        var aDef = Content.Machines[a.Def];
        var bDef = Content.Machines[b.Def];
        if (aDef.Kind == MachineKind.Generator || bDef.Kind == MachineKind.Generator) return CommandResult.Fail("err_link_kind");
        foreach (var l in Links.Values)
            if (l.From == from && l.To == to) return CommandResult.Fail("err_link_exists");

        var link = new Link { Id = NextId++, From = from, To = to, Planet = p.Planet };
        Links[link.Id] = link;
        _linkList = Links.Values.ToArray();
        DirtyLinks.Add(link.Id);
        Bump("linked");
        return CommandResult.Success;
    }

    private CommandResult RemoveLink(PlayerState p, int id)
    {
        if (!Links.TryGetValue(id, out var link) || link.Planet != p.Planet) return CommandResult.Fail("err_gone");
        Links.Remove(id);
        _linkList = Links.Values.ToArray();
        DirtyLinks.Remove(id);
        RemovedLinks.Add((link.Planet, id));
        return CommandResult.Success;
    }

    /// <summary>Espacio libre para un objeto en la entrada de una máquina.</summary>
    public long InputRoom(Machine m, string item)
    {
        var def = Content.Machines[m.Def];
        switch (def.Kind)
        {
            case MachineKind.Storage:
            case MachineKind.Terminal:
            case MachineKind.Station:
                return Math.Max(0, def.Capacity - m.In.Get(item));
            case MachineKind.Production:
                var recipe = Content.Recipes[def.Recipe!];
                if (!recipe.Inputs.TryGetValue(item, out var need)) return 0;
                return Math.Max(0, need * B.RecipeBufferFactor - m.In.Get(item));
            default:
                return 0;
        }
    }

    private CommandResult Insert(PlayerState p, int id)
    {
        if (!Machines.TryGetValue(id, out var m) || m.Planet != p.Planet) return CommandResult.Fail("err_gone");
        var (cx, cz) = MachineCenterMm(m);
        if (!InRange(p, cx, cz, B.InteractRangeMm)) return CommandResult.Fail("err_range");
        long moved = 0;
        foreach (var kv in p.Bag.Items.ToList())
        {
            var qty = Math.Min(kv.Value, InputRoom(m, kv.Key));
            if (qty <= 0) continue;
            p.Bag.TryRemove(kv.Key, qty);
            m.In.Add(kv.Key, qty);
            moved += qty;
        }
        if (moved == 0) return CommandResult.Fail("err_nothing");
        DirtyMachines.Add(id);
        DirtyPlayers.Add(p.Key);
        return CommandResult.Success;
    }

    private CommandResult Extract(PlayerState p, int id)
    {
        if (!Machines.TryGetValue(id, out var m) || m.Planet != p.Planet) return CommandResult.Fail("err_gone");
        var (cx, cz) = MachineCenterMm(m);
        if (!InRange(p, cx, cz, B.InteractRangeMm)) return CommandResult.Fail("err_range");
        var def = Content.Machines[m.Def];
        var source = def.Kind == MachineKind.Production ? m.Out : m.In;
        long moved = 0;
        foreach (var kv in source.Items.ToList())
        {
            var room = B.PersonalStackLimit - p.Bag.Get(kv.Key);
            var qty = Math.Min(kv.Value, room);
            if (qty <= 0) continue;
            source.TryRemove(kv.Key, qty);
            p.Bag.Add(kv.Key, qty);
            moved += qty;
        }
        if (moved == 0) return CommandResult.Fail("err_nothing");
        DirtyMachines.Add(id);
        DirtyPlayers.Add(p.Key);
        return CommandResult.Success;
    }

    public long SellPrice(string item) =>
        Content.Items[item].Price * (1000 + Mod("sell_bonus")) / 1000;

    private CommandResult Sell(PlayerState p, int terminalId, string item, long qty)
    {
        if (!Machines.TryGetValue(terminalId, out var m) || m.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (Content.Machines[m.Def].Kind != MachineKind.Terminal) return CommandResult.Fail("err_invalid");
        if (!Content.Items.ContainsKey(item) || qty <= 0) return CommandResult.Fail("err_invalid");
        var (cx, cz) = MachineCenterMm(m);
        if (!InRange(p, cx, cz, B.InteractRangeMm)) return CommandResult.Fail("err_range");
        var amount = Math.Min(qty, m.In.Get(item));
        if (amount <= 0) return CommandResult.Fail("err_nothing");
        m.In.TryRemove(item, amount);
        var earned = amount * SellPrice(item);
        AddCredits(earned);
        Bump("sold_credits", earned);
        DirtyMachines.Add(m.Id);
        return CommandResult.Success;
    }

    private CommandResult SetAutoSell(PlayerState p, int terminalId, bool enabled, string item, long reserve)
    {
        if (!Machines.TryGetValue(terminalId, out var m) || m.Planet != p.Planet) return CommandResult.Fail("err_gone");
        if (Content.Machines[m.Def].Kind != MachineKind.Terminal) return CommandResult.Fail("err_invalid");
        m.AutoSell = enabled;
        if (item.Length > 0)
        {
            if (!Content.Items.ContainsKey(item) || reserve < 0) return CommandResult.Fail("err_invalid");
            m.Reserves[item] = reserve;
        }
        DirtyMachines.Add(m.Id);
        return CommandResult.Success;
    }

    private CommandResult Fulfill(PlayerState p, string id)
    {
        if (!Content.Contracts.TryGetValue(id, out var c)) return CommandResult.Fail("err_invalid");
        if (ContractsDone.Contains(id)) return CommandResult.Fail("err_done");
        if (c.Requires != null && !Upgrades.Contains(c.Requires)) return CommandResult.Fail("err_locked");

        // Reúne de terminales y depósito del planeta y de la mochila, sin tocar nada hasta confirmar.
        var sources = _machineList
            .Where(m => m.Planet == p.Planet && Content.Machines[m.Def].Kind is MachineKind.Terminal or MachineKind.Storage or MachineKind.Station)
            .ToList();
        foreach (var (item, qty) in c.Items)
        {
            var have = p.Bag.Get(item);
            foreach (var s in sources) have += s.In.Get(item);
            if (have < qty) return CommandResult.Fail("err_cost");
        }
        foreach (var (item, qty) in c.Items)
        {
            var left = qty;
            var fromBag = Math.Min(left, p.Bag.Get(item));
            p.Bag.TryRemove(item, fromBag);
            left -= fromBag;
            foreach (var s in sources)
            {
                if (left == 0) break;
                var take = Math.Min(left, s.In.Get(item));
                if (take == 0) continue;
                s.In.TryRemove(item, take);
                DirtyMachines.Add(s.Id);
                left -= take;
            }
        }
        DirtyPlayers.Add(p.Key);
        ContractsDone.Add(id);
        AddCredits(c.Credits);
        Bump("contract." + id);
        Bump("contract_credits", c.Credits);
        return CommandResult.Success;
    }

    private CommandResult Buy(PlayerState p, string id)
    {
        if (!Content.Upgrades.TryGetValue(id, out var u)) return CommandResult.Fail("err_invalid");
        if (Upgrades.Contains(id)) return CommandResult.Fail("err_done");
        if (u.Requires != null && !Upgrades.Contains(u.Requires)) return CommandResult.Fail("err_locked");
        if (!Pay(p, u.Credits, u.Cost)) return CommandResult.Fail("err_cost");
        Upgrades.Add(id);
        RecomputeMods();
        Bump("upgrade." + id);
        return CommandResult.Success;
    }

    private CommandResult SetRole(PlayerState p, string targetKey, Role role)
    {
        if (!Players.TryGetValue(targetKey, out var target)) return CommandResult.Fail("err_gone");
        if (target.Role == Role.Owner || role == Role.Owner) return CommandResult.Fail("err_permission");
        if (role > Role.Admin || role < Role.Visitor) return CommandResult.Fail("err_invalid");
        if (role == Role.Admin && p.Role != Role.Owner) return CommandResult.Fail("err_permission");
        target.Role = role;
        DirtyPlayers.Add(target.Key);
        return CommandResult.Success;
    }

    // ---------- Simulación ----------

    public void Step()
    {
        Tick++;
        StepPower();
        foreach (var m in _machineList) StepMachine(m);
        foreach (var l in _linkList) StepLink(l);
        StepTrains();
        if (Tick % B.TicksPerSecond == 0) StepObjectives();
    }

    private readonly Dictionary<int, long> _demand = new();
    private readonly Dictionary<int, long> _supply = new();

    private void StepPower()
    {
        _demand.Clear();
        _supply.Clear();
        foreach (var m in _machineList)
        {
            var def = Content.Machines[m.Def];
            if (def.PowerGen > 0) _supply[m.Planet] = _supply.GetValueOrDefault(m.Planet) + def.PowerGen;
            if (def.Power > 0 && WantsToRun(m, def)) _demand[m.Planet] = _demand.GetValueOrDefault(m.Planet) + def.Power;
        }
    }

    public long PowerSupply(int planet) => _supply.GetValueOrDefault(planet);
    public long PowerDemand(int planet) => _demand.GetValueOrDefault(planet);

    private long Satisfaction(int planet)
    {
        var demand = _demand.GetValueOrDefault(planet);
        if (demand == 0) return 1000;
        return Math.Min(1000, _supply.GetValueOrDefault(planet) * 1000 / demand);
    }

    private bool WantsToRun(Machine m, MachineDef def)
    {
        if (def.Kind != MachineKind.Production) return false;
        if (m.Processing) return true;
        var recipe = Content.Recipes[def.Recipe!];
        return HasInputs(m, recipe) && OutputHasRoom(m, recipe);
    }

    private static bool HasInputs(Machine m, RecipeDef r)
    {
        foreach (var (item, qty) in r.Inputs)
            if (m.In.Get(item) < qty) return false;
        return true;
    }

    private bool OutputHasRoom(Machine m, RecipeDef r)
    {
        foreach (var (item, qty) in r.Outputs)
            if (m.Out.Get(item) + qty > B.OutputBuffer) return false;
        return true;
    }

    private void StepMachine(Machine m)
    {
        var def = Content.Machines[m.Def];
        var previous = m.Status;
        switch (def.Kind)
        {
            case MachineKind.Generator:
                m.Status = MachineStatus.Generating;
                break;
            case MachineKind.Storage:
            case MachineKind.Station:
                m.Status = MachineStatus.Storing;
                break;
            case MachineKind.Terminal:
                m.Status = MachineStatus.Storing;
                if (m.AutoSell) StepAutoSell(m);
                break;
            case MachineKind.Production:
                StepProduction(m, def);
                break;
        }
        if (m.Status != previous) DirtyMachines.Add(m.Id);
    }

    private void StepAutoSell(Machine m)
    {
        if (--m.SellCooldown > 0) return;
        m.SellCooldown = B.SellIntervalTicks;
        var budget = (long)B.SellUnitsPerInterval;
        foreach (var kv in m.In.Items.ToList())
        {
            if (budget == 0) break;
            var above = kv.Value - m.Reserves.GetValueOrDefault(kv.Key);
            var qty = Math.Min(budget, above);
            if (qty <= 0) continue;
            m.In.TryRemove(kv.Key, qty);
            var earned = qty * SellPrice(kv.Key);
            AddCredits(earned);
            Bump("sold_credits", earned);
            budget -= qty;
            DirtyMachines.Add(m.Id);
        }
    }

    private void StepProduction(Machine m, MachineDef def)
    {
        var recipe = Content.Recipes[def.Recipe!];
        if (!m.Processing)
        {
            if (!HasInputs(m, recipe)) { m.Status = MachineStatus.NoInput; return; }
            if (!OutputHasRoom(m, recipe)) { m.Status = MachineStatus.OutputFull; return; }
            foreach (var (item, qty) in recipe.Inputs) m.In.TryRemove(item, qty);
            m.Processing = true;
            m.Progress = 0;
            DirtyMachines.Add(m.Id);
        }

        var satisfaction = def.Power > 0 ? Satisfaction(m.Planet) : 1000;
        if (satisfaction == 0) { m.Status = MachineStatus.NoPower; return; }
        m.Status = satisfaction < 1000 ? MachineStatus.LowPower : MachineStatus.Working;

        var yieldPermille = m.NodeId >= 0 && Nodes.TryGetValue(m.NodeId, out var source) ? source.YieldPermille : 1000;
        m.Progress += satisfaction * (1000 + Mod("speed." + recipe.Id)) / 1000 * yieldPermille / 1000;
        if (m.Progress < recipe.Ticks * 1000L) return;

        foreach (var (item, qty) in recipe.Outputs)
        {
            m.Out.Add(item, qty);
            Bump("produced." + item, qty);
        }
        m.Processing = false;
        m.Progress = 0;
        DirtyMachines.Add(m.Id);
    }

    private void StepLink(Link l)
    {
        if (--l.Cooldown > 0) return;
        if (!Machines.TryGetValue(l.From, out var a) || !Machines.TryGetValue(l.To, out var b)) return;
        var source = Content.Machines[a.Def].Kind == MachineKind.Production ? a.Out : a.In;
        var units = 1 + Mod("link_rate");
        foreach (var kv in source.Items)
        {
            var qty = Math.Min(Math.Min(kv.Value, units), InputRoom(b, kv.Key));
            if (qty <= 0) continue;
            var item = kv.Key;
            source.TryRemove(item, qty);
            b.In.Add(item, qty);
            DirtyMachines.Add(a.Id);
            DirtyMachines.Add(b.Id);
            l.Cooldown = B.LinkIntervalTicks;
            return;
        }
        l.Cooldown = B.LinkIntervalTicks;
    }

    private void StepObjectives()
    {
        while (ObjectiveIndex < Content.Objectives.Count)
        {
            var o = Content.Objectives[ObjectiveIndex];
            if (Stats.GetValueOrDefault(o.Stat) < o.Qty) return;
            AddCredits(o.Credits);
            ObjectiveIndex++;
            DirtyGlobal = true;
        }
    }

    // ---------- Totales para pruebas de conservación ----------

    public long CountEverywhere(string item)
    {
        long total = 0;
        foreach (var m in Machines.Values)
        {
            total += m.In.Get(item) + m.Out.Get(item);
            if (m.Processing)
            {
                var recipeId = Content.Machines[m.Def].Recipe;
                if (recipeId != null && Content.Recipes[recipeId].Inputs.TryGetValue(item, out var q)) total += q;
            }
        }
        foreach (var p in Players.Values) total += p.Bag.Get(item);
        foreach (var t in Trains.Values) total += t.Cargo.Get(item);
        return total;
    }
}

/// <summary>Generador simple y estable entre plataformas para contenido sembrado.</summary>
public struct Lcg
{
    private uint _state;
    public Lcg(uint seed) { _state = seed * 2654435761u + 1; }

    public uint Next()
    {
        _state = unchecked(_state * 1664525u + 1013904223u);
        return _state ^ (_state >> 15);
    }

    public int Range(int min, int maxExclusive) => min + (int)(Next() % (uint)(maxExclusive - min));
}
