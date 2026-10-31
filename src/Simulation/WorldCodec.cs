using System.Security.Cryptography;

namespace Pluton.Simulation;

/// <summary>
/// Formato binario único para guardados, instantáneas y deltas: lo que se replica es lo mismo que se guarda.
/// </summary>
public static class WorldCodec
{
    public const int FormatVersion = 1;

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

    public static Machine ReadMachine(BinaryReader r)
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
    }

    public static ResourceNode ReadNode(BinaryReader r) => new()
    {
        Id = r.ReadInt32(),
        Item = r.ReadString(),
        Planet = r.Read7BitEncodedInt(),
        X = r.ReadInt32(),
        Z = r.ReadInt32(),
        Revealed = r.ReadBoolean(),
    };

    public static void WritePlayer(BinaryWriter w, PlayerState p, bool forHash = false)
    {
        w.Write(p.Key);
        w.Write(p.Name);
        w.Write((byte)p.Role);
        w.Write7BitEncodedInt(p.Planet);
        w.Write7BitEncodedInt(p.Num);
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

    public static PlayerState ReadPlayer(BinaryReader r)
    {
        var p = new PlayerState
        {
            Key = r.ReadString(),
            Name = r.ReadString(),
            Role = (Role)r.ReadByte(),
            Planet = r.Read7BitEncodedInt(),
            Num = r.Read7BitEncodedInt(),
            Connected = r.ReadBoolean(),
            X = r.ReadSingle(),
            Y = r.ReadSingle(),
            Z = r.ReadSingle(),
            Yaw = r.ReadSingle(),
        };
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
    }

    public static void ReadGlobal(BinaryReader r, World world)
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
    }

    public static byte[] Serialize(World world)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(world.NextId);
        w.Write(world.NextPlayerNum);
        w.Write(world.OwnerKey);
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
        ReadGlobal(r, world);
        var n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var m = ReadMachine(r);
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
            var node = ReadNode(r);
            world.Nodes[node.Id] = node;
        }
        n = r.Read7BitEncodedInt();
        for (var i = 0; i < n; i++)
        {
            var p = ReadPlayer(r);
            p.Connected = false;
            world.Players[p.Key] = p;
        }
        world.Reindex();
        return world;
    }

    /// <summary>Firma del estado económico y estructural; excluye datos volátiles como posición o progreso.</summary>
    public static string Hash(World world)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        WriteGlobal(w, world, forHash: true);
        foreach (var m in world.Machines.Values) WriteMachine(w, m, forHash: true);
        foreach (var l in world.Links.Values) WriteLink(w, l);
        foreach (var d in world.DebrisById.Values) WriteDebris(w, d);
        foreach (var n in world.Nodes.Values) WriteNode(w, n);
        foreach (var p in world.Players.Values) WritePlayer(w, p, forHash: true);
        w.Flush();
        return Convert.ToHexString(SHA256.HashData(ms.GetBuffer().AsSpan(0, (int)ms.Length)))[..16];
    }
}
