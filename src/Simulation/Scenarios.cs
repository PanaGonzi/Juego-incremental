namespace Pluton.Simulation;

/// <summary>
/// Mundos de medición reproducibles. Se construyen directamente sobre el estado, sin coste de jugador,
/// y solo los usan las pruebas y los ensayos: no son accesibles desde una partida normal.
/// </summary>
public static class Scenarios
{
    public sealed record Size(int Machines, int Trains, int RailSegments);

    public static readonly Dictionary<string, Size> Sizes = new()
    {
        ["small"] = new Size(50, 5, 100),
        ["medium"] = new Size(500, 25, 2000),
        ["stress"] = new Size(2000, 100, 10000),
        ["long"] = new Size(0, 0, 0),
        ["saturated"] = new Size(0, 0, 0),
    };

    public static World Build(ContentDb content, string name)
    {
        var size = Sizes[name];
        var w = World.CreateNew(content);
        foreach (var d in w.DebrisById.Values.Where(d => d.Planet == 0).ToList()) w.DebrisById.Remove(d.Id);
        foreach (var n in w.Nodes.Values) n.Revealed = true;
        foreach (var id in content.Upgrades.Keys) w.Upgrades.Add(id);
        w.Credits = 1000000;
        w.ObjectiveIndex = content.Objectives.Count;
        // Reservas para que los clientes de carga ejerzan la economía: recoger, vender y construir.
        var depot = w.DepotOf(0) ?? w.Machines.Values.First(m => m.Def == "depot" && m.Planet == 0);
        depot.In.Add("scrap", 100000);
        depot.In.Add("recovered", 100000);

        // Rot 3 mira a +X: las salidas dan al este y las entradas al oeste, y las cintas van rectas entre máquinas.
        Machine Add(string def, int x, int z, int node = -1, int rot = 0)
        {
            var m = new Machine { Id = w.NextId++, Def = def, Planet = 0, X = x, Z = z, NodeId = node, Rot = rot };
            w.Machines[m.Id] = m;
            return m;
        }

        // Tramo recto de x0 a x1 en la fila z; se siembra carga para que el mundo mida cintas en uso desde el primer tick.
        Link Belt(Machine a, Machine b, int x0, int x1, int z, string seed)
        {
            var cells = new List<(int, int)>();
            for (var x = x0; x <= x1; x++) cells.Add((x, z));
            var l = w.AddBeltDirect(a, b, cells);
            w.SeedBeltSlot(l, 1, seed);
            return l;
        }

        ResourceNode Node(int x, int z)
        {
            var n = new ResourceNode { Id = w.NextId++, Item = "ore", Planet = 0, X = x, Z = z, Revealed = true };
            w.Nodes[n.Id] = n;
            return n;
        }

        // Líneas ferroviarias en filas al sur de la base; cada una alimenta a su tren con un extractor.
        var perLine = size.RailSegments / Math.Max(1, size.Trains);
        var machines = 0;
        for (var line = 0; line < size.Trains; line++)
        {
            var z = -14 - line * 3;
            var startX = -82;
            var a = Add("station", startX - 4, z - 2);
            var endX = startX + perLine * 2;
            var b = Add("station", endX, z - 2);
            b.StationUnload = true;
            machines += 2;

            RailNode NodeAtCell(int x)
            {
                var n = new RailNode { Id = w.NextId++, Planet = 0, X = x, Z = z };
                w.RailNodes[n.Id] = n;
                return n;
            }
            RailSegment Segment(RailNode p, RailNode q, int owner)
            {
                var s = new RailSegment { Id = w.NextId++, Planet = 0, A = p.Id, B = q.Id, LengthMm = RailMath.Length(p, q, 0, content.Balance.CellSizeMm), OwnerStation = owner };
                w.RailSegments[s.Id] = s;
                return s;
            }

            var first = NodeAtCell(startX - 4);
            var previous = NodeAtCell(startX);
            Segment(first, previous, a.Id);
            a.RailA = first.Id;
            a.RailB = previous.Id;
            for (var i = 1; i <= perLine; i++)
            {
                var next = NodeAtCell(startX + i * 2);
                Segment(previous, next, -1);
                previous = next;
            }
            var far = NodeAtCell(endX + 4);
            Segment(previous, far, b.Id);
            b.RailA = previous.Id;
            b.RailB = far.Id;

            var feed = Add("ore_extractor", startX - 8, z - 2, Node(startX - 8, z - 2).Id, rot: 3);
            Belt(feed, a, startX - 6, startX - 5, z - 2, "ore");
            machines++;

            var train = new Train { Id = w.NextId++, Planet = 0, StationA = a.Id, StationB = b.Id, CurrentNode = a.RailA };
            w.Trains[train.Id] = train;
        }

        // Cadenas de producción en una rejilla: extractor → fundición → ensambladora → terminal con venta automática + generador.
        // Una de cada tres añade además una línea saturada: almacén con reservas → cinta llena → terminal.
        const int cols = 9;
        var group = 0;
        var fixedCells = new HashSet<(int, int)>();
        foreach (var m in w.Machines.Values.Where(m => m.Planet == 0))
            foreach (var c in w.Footprint(m.Def, m.X, m.Z, m.Rot)) fixedCells.Add(c);
        while (machines < size.Machines)
        {
            var x = -84 + group % cols * 19;
            var z = 8 + group / cols * 7;
            var index = group++;
            var clash = false;
            for (var cx = x - 1; cx <= x + 18 && !clash; cx++)
                for (var cz = z - 1; cz <= z + 5 && !clash; cz++)
                    clash = fixedCells.Contains((cx, cz));
            if (clash) continue;
            var ex = Add("ore_extractor", x, z, Node(x, z).Id, rot: 3);
            var sm = Add("smelter", x + 4, z, rot: 3);
            var asm = Add("assembler", x + 9, z, rot: 3);
            var term = Add("export_terminal", x + 14, z);
            term.AutoSell = true;
            Add("rtg_generator", x, z + 3);
            Belt(ex, sm, x + 2, x + 3, z, "ore");
            Belt(sm, asm, x + 7, x + 8, z, "ingot");
            Belt(asm, term, x + 12, x + 13, z, "parts");
            machines += 5;
            if (index % 3 != 0) continue;
            var stock = Add("storage_crate", x + 3, z + 3);
            stock.In.Add("scrap", 1000000);
            var sink = Add("export_terminal", x + 14, z + 3);
            sink.AutoSell = true;
            var line = Belt(stock, sink, x + 5, x + 13, z + 3, "scrap");
            for (var s = 0; s < line.Slots.Length; s++) w.SeedBeltSlot(line, s, "scrap");
            machines += 2;
        }
        // 30 cintas de 168 celdas con carga escasa y regular: mide huecos largos en simulación y red.
        if (name == "saturated")
            for (var row = 0; row < 30; row++)
            {
                // Las mismas cintas, llenas desde el principio: el peor caso de carga dibujada.
                var z = -88 + row * 3;
                var stock = Add("storage_crate", -86, z);
                stock.In.Add("scrap", 1000000);
                var term = Add("export_terminal", 84, z);
                term.AutoSell = true;
                var line = Belt(stock, term, -84, 83, z, "scrap");
                for (var s = 0; s < line.Slots.Length; s++) w.SeedBeltSlot(line, s, "scrap");
            }
        if (name == "long")
            for (var row = 0; row < 30; row++)
            {
                var z = -88 + row * 3;
                var ex = Add("ore_extractor", -86, z, Node(-86, z).Id, rot: 3);
                var term = Add("export_terminal", 84, z);
                term.AutoSell = true;
                if (row % 4 == 0) Add("rtg_generator", -89, z);
                Belt(ex, term, -84, 83, z, "ore");
            }
        w.Reindex();
        return w;
    }
}
