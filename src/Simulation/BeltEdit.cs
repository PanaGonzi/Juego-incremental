namespace Pluton.Simulation;

/// <summary>Correcciones sobre una fábrica ya tendida: insertar un divisor o unificador en una cinta sin retirarla.</summary>
public sealed partial class World
{
    /// <summary>
    /// Motivo por el que no se puede insertar la pieza en la cinta de esa celda, o null. Lo comparten la previsualización y el servidor.
    /// El giro se deduce de la cinta: quien inserta no tiene que adivinarlo.
    /// </summary>
    public string? JunctionOnBeltError(PlayerState p, string defId, int x, int z, out int linkId, out int rot)
    {
        linkId = -1;
        rot = 0;
        if (!Content.Machines.TryGetValue(defId, out var def) || !IsJunction(def.Kind)) return "err_invalid";
        if (def.Unlock is { Length: > 0 } unlock && !Upgrades.Contains(unlock)) return "err_locked";
        linkId = BeltAt(p.Planet, x, z);
        if (linkId < 0 || !Links.TryGetValue(linkId, out var link) || link.Elevated) return "err_gone";
        var index = link.Cells.IndexOf((x, z));
        if (index <= 0 || index >= link.Cells.Count - 1) return "err_junction_end";
        // Un cruce exige un tramo recto con vecinos por ambos lados: partir la cinta junto a él lo dejaría en un extremo.
        for (var i = index - 1; i <= index + 1; i++)
            if (BridgeAt(p.Planet, link.Cells[i].x, link.Cells[i].z) >= 0) return "err_junction_cross";
        var enterDir = DirBetween(link.Cells[index - 1], (x, z));
        var leaveDir = DirBetween((x, z), link.Cells[index + 1]);
        // Divisor: entra por detrás, así que mira hacia donde venía la cinta. Unificador: sale por delante, hacia donde sigue.
        var front = def.Kind == MachineKind.Splitter ? enterDir : leaveDir;
        rot = (4 - front) % 4;
        return InRange(p, x * (long)B.CellSizeMm, z * (long)B.CellSizeMm, B.BuildRangeMm) ? null : "err_range";
    }

    private static int DirBetween((int x, int z) a, (int x, int z) b) =>
        b.z < a.z ? 0 : b.x > a.x ? 1 : b.z > a.z ? 2 : 3;

    /// <summary>
    /// La cinta se parte en dos sin retirarla: la carga sigue donde estaba y lo pagado se reparte entre los dos tramos.
    /// Lo que ocupaba la celda de la pieza pasa al primer hueco libre por delante, luego por detrás y, solo si no cabe, al depósito.
    /// </summary>
    private CommandResult InsertJunction(PlayerState p, string defId, int x, int z)
    {
        var error = JunctionOnBeltError(p, defId, x, z, out var linkId, out var rot);
        if (error != null) return CommandResult.Fail(error);
        var def = Content.Machines[defId];
        if (!Pay(p, def.Credits, def.Cost)) return CommandResult.Fail("err_cost");

        var link = Links[linkId];
        var index = link.Cells.IndexOf((x, z));
        var cells = link.Cells.ToList();
        var slots = link.Slots;
        var perCell = B.BeltSlotsPerCell;
        var machine = new Machine { Id = NextId++, Def = defId, Planet = p.Planet, X = x, Z = z, Rot = rot, NodeId = -1 };
        var tail = new Link { Id = NextId++, From = machine.Id, To = link.To, Planet = link.Planet };

        UnregisterBelt(link);
        var before = cells.Take(index).ToList();
        var after = cells.Skip(index + 1).ToList();
        var head = link.Paid * before.Count / (before.Count + after.Count);
        tail.Paid = link.Paid - head;
        link.Paid = head;
        link.To = machine.Id;
        ApplyRoute(link, before);
        ApplyRoute(tail, after);
        link.Accum = 0;

        var firstSlot = index * perCell;
        var afterSlot = firstSlot + perCell;
        var displaced = new List<string>();
        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } item) continue;
            if (i < firstSlot) PutSlot(link, i, item);
            else if (i >= afterSlot) PutSlot(tail, i - afterSlot, item);
            else displaced.Add(item);
        }
        foreach (var item in displaced)
        {
            if (TryFirstFree(tail, item) || TryLastFree(link, item)) continue;
            var depot = DepotOf(link.Planet);
            if (depot == null) continue;
            depot.In.Add(item, 1);
            DirtyMachines.Add(depot.Id);
        }

        Machines[machine.Id] = machine;
        _occupied[(p.Planet, x, z)] = machine.Id;
        _machineList = Machines.Values.ToArray();
        Links[tail.Id] = tail;
        RegisterBelt(link);
        RegisterBelt(tail);
        RefreshBelts();
        DirtyMachines.Add(machine.Id);
        DirtyLinks.Add(link.Id);
        DirtyLinks.Add(tail.Id);
        Bump("built." + defId);
        return CommandResult.Success;
    }

    private static void PutSlot(Link l, int slot, string item)
    {
        l.Slots[slot] = item;
        l.Load++;
    }

    private static bool TryFirstFree(Link l, string item)
    {
        for (var i = 0; i < l.Slots.Length; i++)
            if (l.Slots[i] == null) { PutSlot(l, i, item); return true; }
        return false;
    }

    private static bool TryLastFree(Link l, string item)
    {
        for (var i = l.Slots.Length - 1; i >= 0; i--)
            if (l.Slots[i] == null) { PutSlot(l, i, item); return true; }
        return false;
    }
}
