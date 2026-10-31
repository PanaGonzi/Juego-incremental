using System.Security.Cryptography;

namespace Pluton.Simulation;

/// <summary>
/// Formato binario único para guardados, instantáneas y deltas: lo que se replica es lo mismo que se guarda.
/// </summary>
public static class WorldCodec
{
    public const int FormatVersion = 3;

    private static void WriteInv(BinaryWriter w, Inventory inv)
    {
        w.Write7BitEncodedInt(inv.Kinds);
        foreach (var kv in inv.Items)
        {
            w.Write(kv.Key);
            w.Write7BitEncodedInt64(kv.Value);
        }
    }

    private static void ReadInv(BinaryReader r, Inventory inv)
    {
        inv.Clear();
        var n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var item = r.ReadString();
            var qty = r.Read7BitEncodedInt64();
            if (qty < 0) throw new InvalidDataException("Cantidad negativa");
            inv.Add(item, qty);
        }
    }

    public static void WriteMachine(BinaryWriter w, Machine m, bool forHash = false)
    {
        w.Write(m.Id);
        w.Write(m.Def);
        w.Write7BitEncodedInt(m.Planet);
        w.Write(m.X);
        w.Write(m.Z);
        w.Write((byte)m.Rot);
        w.Write(m.Builtin);
        w.Write(m.NodeId);
        w.Write(m.RailA);
        w.Write(m.RailB);
        w.Write(m.StationUnload);
        WriteInv(w, m.In);
        WriteInv(w, m.Out);
        w.Write(m.Processing);
        if (!forHash)
        {
            w.Write7BitEncodedInt64(m.Progress);
            w.Write((byte)m.Status);
        }
        w.Write(m.AutoSell);
        w.Write7BitEncodedInt(m.Reserves.Count);
        foreach (var kv in m.Reserves)
        {
            w.Write(kv.Key);
            w.Write7BitEncodedInt64(kv.Value);
        }
    }

    public static Machine ReadMachine(BinaryReader r, int version = FormatVersion)
    {
        var m = new Machine
        {
            Id = r.ReadInt32(),
            Def = r.ReadString(),
            Planet = r.Read7BitEncodedInt(),
            X = r.ReadInt32(),
            Z = r.ReadInt32(),
            Rot = r.ReadByte(),
            Builtin = r.ReadBoolean(),
            NodeId = r.ReadInt32(),
        };
        if (version >= 2)
        {
            m.RailA = r.ReadInt32();
            m.RailB = r.ReadInt32();
            m.StationUnload = r.ReadBoolean();
        }
        ReadInv(r, m.In);
        ReadInv(r, m.Out);
        m.Processing = r.ReadBoolean();
        m.Progress = r.Read7BitEncodedInt64();
        m.Status = (MachineStatus)r.ReadByte();
        m.AutoSell = r.ReadBoolean();
        var reserves = r.Read7BitEncodedInt();
        for (var i = 0; i < reserves; i++)
        {
            var key = r.ReadString();
            m.Reserves[key] = r.Read7BitEncodedInt64();
        }
        return m;
    }

    public static void WriteLink(BinaryWriter w, Link l)
    {
        w.Write(l.Id);
        w.Write(l.From);
        w.Write(l.To);
        w.Write7BitEncodedInt(l.Planet);
    }

    public static Link ReadLink(BinaryReader r) => new()
    {
        Id = r.ReadInt32(),
        From = r.ReadInt32(),
        To = r.ReadInt32(),
        Planet = r.Read7BitEncodedInt(),
    };

    public static void WriteDebris(BinaryWriter w, Debris d)
    {
        w.Write(d.Id);
        w.Write(d.Def);
        w.Write7BitEncodedInt(d.Planet);
        w.Write(d.X);
        w.Write(d.Z);
        w.Write7BitEncodedInt(d.Hp);
        w.Write(d.CoversNode);
    }

    public static Debris ReadDebris(BinaryReader r) => new()
    {
        Id = r.ReadInt32(),
        Def = r.ReadString(),
        Planet = r.Read7BitEncodedInt(),
        X = r.ReadInt32(),
        Z = r.ReadInt32(),
        Hp = r.Read7BitEncodedInt(),
        CoversNode = r.ReadInt32(),
    };

    public static void WriteNode(BinaryWriter w, ResourceNode n)
    {
        w.Write(n.Id);
        w.Write(n.Item);
        w.Write7BitEncodedInt(n.Planet);
        w.Write(n.X);
        w.Write(n.Z);
        w.Write(n.Revealed);
        w.Write7BitEncodedInt(n.YieldPermille);
    }

    public static ResourceNode ReadNode(BinaryReader r, int version = FormatVersion)
    {
        var n = new ResourceNode
        {
            Id = r.ReadInt32(),
            Item = r.ReadString(),
            Planet = r.Read7BitEncodedInt(),
            X = r.ReadInt32(),
            Z = r.ReadInt32(),
            Revealed = r.ReadBoolean(),
        };
        if (version >= 2) n.YieldPermille = r.Read7BitEncodedInt();
        return n;
    }

    public static void WriteRailNode(BinaryWriter w, RailNode n)
    {
        w.Write(n.Id);
        w.Write7BitEncodedInt(n.Planet);
        w.Write(n.X);
        w.Write(n.Z);
    }

    public static RailNode ReadRailNode(BinaryReader r) => new()
    {
        Id = r.ReadInt32(),
        Planet = r.Read7BitEncodedInt(),
        X = r.ReadInt32(),
        Z = r.ReadInt32(),
    };

    public static void WriteRailSegment(BinaryWriter w, RailSegment s)
    {
        w.Write(s.Id);
        w.Write7BitEncodedInt(s.Planet);
        w.Write(s.A);
        w.Write(s.B);
        w.Write(s.Shape);
        w.Write7BitEncodedInt64(s.LengthMm);
        w.Write(s.OwnerStation);
    }

    public static RailSegment ReadRailSegment(BinaryReader r) => new()
    {
        Id = r.ReadInt32(),
        Planet = r.Read7BitEncodedInt(),
        A = r.ReadInt32(),
        B = r.ReadInt32(),
        Shape = r.ReadByte(),
        LengthMm = r.Read7BitEncodedInt64(),
        OwnerStation = r.ReadInt32(),
    };

    public static void WriteTrain(BinaryWriter w, Train t, bool forHash = false, bool withPath = true)
    {
        w.Write(t.Id);
        w.Write7BitEncodedInt(t.Planet);
        w.Write(t.StationA);
        w.Write(t.StationB);
        w.Write(t.HeadingToB);
        w.Write((byte)t.State);
        w.Write(t.WaitReason);
        WriteInv(w, t.Cargo);
        if (forHash) return;
        w.Write(withPath);
        if (withPath)
        {
            w.Write7BitEncodedInt(t.Path.Count);
            foreach (var id in t.Path) w.Write(id);
        }
        w.Write7BitEncodedInt(t.PathIndex);
        w.Write(t.CurrentNode);
        w.Write(t.Forward);
        w.Write7BitEncodedInt64(t.PosMm);
        w.Write7BitEncodedInt(t.Timer);
    }

    /// <param name="previous">Tren ya conocido, del que se conserva la ruta si no viene en el mensaje.</param>
    public static Train ReadTrain(BinaryReader r, Func<int, Train?>? previous = null)
    {
        var t = new Train
        {
            Id = r.ReadInt32(),
            Planet = r.Read7BitEncodedInt(),
            StationA = r.ReadInt32(),
            StationB = r.ReadInt32(),
            HeadingToB = r.ReadBoolean(),
            State = (TrainState)r.ReadByte(),
            WaitReason = r.ReadString(),
        };
        ReadInv(r, t.Cargo);
        if (r.ReadBoolean())
        {
            var n = r.Read7BitEncodedInt();
            for (var i = 0; i < n; i++) t.Path.Add(r.ReadInt32());
        }
        else if (previous?.Invoke(t.Id) is { } known)
        {
            t.Path.AddRange(known.Path);
        }
        t.PathIndex = r.Read7BitEncodedInt();
        t.CurrentNode = r.ReadInt32();
        t.Forward = r.ReadBoolean();
        t.PosMm = r.Read7BitEncodedInt64();
        t.Timer = r.Read7BitEncodedInt();
        return t;
    }

    public static void WritePlayer(BinaryWriter w, PlayerState p, bool forHash = false)
    {
        w.Write(p.Key);
        w.Write(p.Name);
        w.Write((byte)p.Role);
        w.Write7BitEncodedInt(p.Planet);
        w.Write7BitEncodedInt(p.Num);
        w.Write7BitEncodedInt(p.TransitTo + 1);
        w.Write7BitEncodedInt64(p.TransitStartTick);
        w.Write7BitEncodedInt64(p.TransitEndTick);
        if (!forHash)
        {
            w.Write(p.Connected);
            w.Write(p.X);
            w.Write(p.Y);
            w.Write(p.Z);
            w.Write(p.Yaw);
        }
        WriteInv(w, p.Bag);
    }

    public static PlayerState ReadPlayer(BinaryReader r, int version = FormatVersion)
    {
        var p = new PlayerState
        {
            Key = r.ReadString(),
            Name = r.ReadString(),
            Role = (Role)r.ReadByte(),
            Planet = r.Read7BitEncodedInt(),
            Num = r.Read7BitEncodedInt(),
        };
        if (version >= 2)
        {
            p.TransitTo = r.Read7BitEncodedInt() - 1;
            p.TransitStartTick = r.Read7BitEncodedInt64();
            p.TransitEndTick = r.Read7BitEncodedInt64();
        }
        p.Connected = r.ReadBoolean();
        p.X = r.ReadSingle();
        p.Y = r.ReadSingle();
        p.Z = r.ReadSingle();
        p.Yaw = r.ReadSingle();
        ReadInv(r, p.Bag);
        return p;
    }

    public static void WriteGlobal(BinaryWriter w, World world, bool forHash = false)
    {
        if (!forHash) w.Write7BitEncodedInt64(world.Tick);
        w.Write7BitEncodedInt64(world.Credits);
        w.Write7BitEncodedInt(world.ObjectiveIndex);
        w.Write7BitEncodedInt(world.Upgrades.Count);
        foreach (var u in world.Upgrades) w.Write(u);
        w.Write7BitEncodedInt(world.ContractsDone.Count);
        foreach (var c in world.ContractsDone) w.Write(c);
        w.Write7BitEncodedInt(world.Stats.Count);
        foreach (var kv in world.Stats)
        {
            w.Write(kv.Key);
            w.Write7BitEncodedInt64(kv.Value);
        }
        WriteSet(w, world.ActiveEvents);
        WriteSet(w, world.EventsSeeded);
        WriteSet(w, world.Cosmetics);
        WriteSet(w, world.EquippedCosmetics);
    }

    private static void WriteSet(BinaryWriter w, SortedSet<string> set)
    {
        w.Write7BitEncodedInt(set.Count);
        foreach (var s in set) w.Write(s);
    }

    private static void ReadSet(BinaryReader r, SortedSet<string> set)
    {
        set.Clear();
        var n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++) set.Add(r.ReadString());
    }

    public static void ReadGlobal(BinaryReader r, World world, int version = FormatVersion)
    {
        world.Tick = r.Read7BitEncodedInt64();
        world.Credits = r.Read7BitEncodedInt64();
        world.ObjectiveIndex = r.Read7BitEncodedInt();
        world.Upgrades.Clear();
        var n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++) world.Upgrades.Add(r.ReadString());
        world.ContractsDone.Clear();
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++) world.ContractsDone.Add(r.ReadString());
        world.Stats.Clear();
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var key = r.ReadString();
            world.Stats[key] = r.Read7BitEncodedInt64();
        }
        if (version >= 3)
        {
            ReadSet(r, world.ActiveEvents);
            ReadSet(r, world.EventsSeeded);
            ReadSet(r, world.Cosmetics);
            ReadSet(r, world.EquippedCosmetics);
        }
    }

    public static byte[] Serialize(World world)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(world.NextId);
        w.Write(world.NextPlayerNum);
        w.Write(world.OwnerKey);
        w.Write(world.SavedUnix);
        WriteGlobal(w, world);
        w.Write7BitEncodedInt(world.Machines.Count);
        foreach (var m in world.Machines.Values) WriteMachine(w, m);
        w.Write7BitEncodedInt(world.Links.Count);
        foreach (var l in world.Links.Values) WriteLink(w, l);
        w.Write7BitEncodedInt(world.DebrisById.Count);
        foreach (var d in world.DebrisById.Values) WriteDebris(w, d);
        w.Write7BitEncodedInt(world.Nodes.Count);
        foreach (var n in world.Nodes.Values) WriteNode(w, n);
        w.Write7BitEncodedInt(world.Players.Count);
        foreach (var p in world.Players.Values) WritePlayer(w, p);
        w.Write7BitEncodedInt(world.RailNodes.Count);
        foreach (var n in world.RailNodes.Values) WriteRailNode(w, n);
        w.Write7BitEncodedInt(world.RailSegments.Count);
        foreach (var s in world.RailSegments.Values) WriteRailSegment(w, s);
        w.Write7BitEncodedInt(world.Trains.Count);
        foreach (var t in world.Trains.Values) WriteTrain(w, t);
        w.Flush();
        return ms.ToArray();
    }

    public static World Deserialize(ContentDb content, byte[] data, int formatVersion = FormatVersion)
    {
        if (formatVersion > FormatVersion) throw new InvalidDataException($"Formato {formatVersion} más nuevo que {FormatVersion}");
        using var r = new BinaryReader(new MemoryStream(data));
        var world = new World(content)
        {
            NextId = r.ReadInt32(),
            NextPlayerNum = r.ReadInt32(),
            OwnerKey = r.ReadString(),
        };
        if (formatVersion >= 3) world.SavedUnix = r.ReadInt64();
        ReadGlobal(r, world, formatVersion);
        var n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var m = ReadMachine(r, formatVersion);
            if (!content.Machines.ContainsKey(m.Def)) throw new InvalidDataException($"Máquina desconocida: {m.Def}");
            world.Machines[m.Id] = m;
        }
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var l = ReadLink(r);
            world.Links[l.Id] = l;
        }
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var d = ReadDebris(r);
            if (!content.Balance.Debris.ContainsKey(d.Def)) throw new InvalidDataException($"Escombro desconocido: {d.Def}");
            world.DebrisById[d.Id] = d;
        }
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var node = ReadNode(r, formatVersion);
            world.Nodes[node.Id] = node;
        }
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var p = ReadPlayer(r, formatVersion);
            p.Connected = false;
            world.Players[p.Key] = p;
        }
        if (formatVersion >= 2)
        {
            n = r.Read7BitEncodedInt();
            for (var i = 0; i < n; i++)
            {
                var node = ReadRailNode(r);
                world.RailNodes[node.Id] = node;
            }
            n = r.Read7BitEncodedInt();
            for (var i = 0; i < n; i++)
            {
                var s = ReadRailSegment(r);
                world.RailSegments[s.Id] = s;
            }
            n = r.Read7BitEncodedInt();
            for (var i = 0; i < n; i++)
            {
                var t = ReadTrain(r);
                world.Trains[t.Id] = t;
            }
        }
        else
        {
            MigrateFromV1(world);
        }
        world.Reindex();
        return world;
    }

    /// <summary>
    /// v1 → v2: tres objetivos nuevos tras «o_parts» y rendimiento de yacimientos según el contenido actual.
    /// </summary>
    private static void MigrateFromV1(World world)
    {
        const int insertedAfter = 9;
        if (world.ObjectiveIndex > insertedAfter) world.ObjectiveIndex += 3;
        foreach (var node in world.Nodes.Values)
            foreach (var planet in world.Content.Planets)
                foreach (var def in planet.Nodes)
                    if (planet.Id == node.Planet && def.X == node.X && def.Z == node.Z)
                        node.YieldPermille = def.YieldPermille;
        foreach (var planet in world.Content.Planets)
        {
            if (world.Machines.Values.Any(m => m.Planet == planet.Id))
                foreach (var b in planet.Builtins) world.EnsureBuiltin(planet, b);
            else
                world.GeneratePlanet(planet);
        }
    }

    public static string Hash(World world) => HashOf(HashableBytes(world));

    public static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes))[..16];

    /// <summary>Estado económico y estructural; excluye datos volátiles como posición o progreso.</summary>
    public static byte[] HashableBytes(World world)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteGlobal(w, world, forHash: true);
        foreach (var m in world.Machines.Values) WriteMachine(w, m, forHash: true);
        foreach (var l in world.Links.Values) WriteLink(w, l);
        foreach (var d in world.DebrisById.Values) WriteDebris(w, d);
        foreach (var n in world.Nodes.Values) WriteNode(w, n);
        foreach (var p in world.Players.Values) WritePlayer(w, p, forHash: true);
        foreach (var n in world.RailNodes.Values) WriteRailNode(w, n);
        foreach (var s in world.RailSegments.Values) WriteRailSegment(w, s);
        foreach (var t in world.Trains.Values) WriteTrain(w, t, forHash: true);
        w.Flush();
        return ms.ToArray();
    }
}
