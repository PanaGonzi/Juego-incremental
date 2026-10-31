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

        Machine Add(string def, int x, int z, int node = -1)
        {
            var m = new Machine { Id = w.NextId++, Def = def, Planet = 0, X = x, Z = z, NodeId = node };
            w.Machines[m.Id] = m;
            return m;
        }

        void Link(Machine a, Machine b)
        {
            var l = new Link { Id = w.NextId++, From = a.Id, To = b.Id, Planet = 0 };
            w.Links[l.Id] = l;
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

            var feed = Add("ore_extractor", startX - 8, z - 2, Node(startX - 8, z - 2).Id);
            Link(feed, a);
            machines++;

            var train = new Train { Id = w.NextId++, Planet = 0, StationA = a.Id, StationB = b.Id, CurrentNode = a.RailA };
            w.Trains[train.Id] = train;
        }

        // Cadenas de producción en una rejilla: extractor → fundición → ensambladora → terminal con venta automática + generador.
        const int cols = 13;
        var group = 0;
        while (machines < size.Machines)
        {
            var x = -84 + group % cols * 13;
            var z = 8 + group / cols * 7;
            group++;
            var ex = Add("ore_extractor", x, z, Node(x, z).Id);
            var sm = Add("smelter", x + 3, z);
            var asm = Add("assembler", x + 6, z);
            var term = Add("export_terminal", x + 9, z);
            term.AutoSell = true;
            Add("rtg_generator", x, z + 3);
            Link(ex, sm);
            Link(sm, asm);
            Link(asm, term);
            machines += 5;
        }
        w.Reindex();
        return w;
    }
}
