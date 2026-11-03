namespace Pluton.Simulation;

/// <summary>
/// Nueva expedición (prestigio, B-7). Opcional y posterior a la campaña: reinicia el progreso operativo a cambio
/// de ventajas permanentes y acumulables. Solo el propietario la inicia, y con confirmación del número de
/// expedición que cree estar cerrando, para que una orden repetida o desfasada no reinicie dos veces.
/// </summary>
public sealed partial class World
{
    /// <summary>Expediciones completadas en este mundo. Cada una suma las ventajas de `balance.expedition`, hasta su tope.</summary>
    public int Expeditions;

    /// <summary>Expediciones que cuentan para las ventajas.</summary>
    public int ExpeditionStacks => Math.Min(Expeditions, B.Expedition.MaxStacks);

    /// <summary>Motivo por el que no se puede iniciar ahora, o null. La interfaz lo muestra tal cual.</summary>
    public string? ExpeditionError(PlayerState p)
    {
        var e = B.Expedition;
        if (e.MaxStacks <= 0) return "err_invalid";
        if (p.Role < Role.Owner) return "err_permission";
        if (!ContractsDone.Contains(e.RequiresContract)) return "err_expedition_locked";
        if (Players.Values.Any(x => x.TransitTo >= 0)) return "err_expedition_transit";
        return null;
    }

    /// <summary>Créditos con los que empezaría la siguiente expedición: los de los planetas más la ventaja acumulada.</summary>
    public long NextExpeditionCredits() =>
        Content.Planets.Sum(pl => pl.StartCredits) + Math.Min(Expeditions + 1, B.Expedition.MaxStacks) * B.Expedition.StartCreditsPerStack;

    private CommandResult StartExpedition(PlayerState p, int expected)
    {
        var error = ExpeditionError(p);
        if (error != null) return CommandResult.Fail(error);
        if (expected != Expeditions) return CommandResult.Fail("err_expedition_confirm");

        foreach (var m in Machines.Values) RemovedMachines.Add((m.Planet, m.Id));
        foreach (var l in Links.Values) RemovedLinks.Add((l.Planet, l.Id));
        foreach (var d in DebrisById.Values) RemovedDebris.Add((d.Planet, d.Id));
        RemovedTrains.AddRange(Trains.Keys);
        RemovedRailSegments.AddRange(RailSegments.Keys);
        RemovedRailNodes.AddRange(RailNodes.Keys);
        Machines.Clear();
        Links.Clear();
        DebrisById.Clear();
        Trains.Clear();
        RailSegments.Clear();
        RailNodes.Clear();
        MeasuredRates.Clear();
        DirtyMachines.Clear();
        DirtySimMachines.Clear();
        DirtyLinks.Clear();
        DirtySlots.Clear();
        DirtySimSlots.Clear();
        DirtyDebris.Clear();
        DirtyTrains.Clear();
        DirtySimTrains.Clear();
        DirtyTrainPaths.Clear();
        DirtyRailNodes.Clear();
        DirtyRailSegments.Clear();
        if (Shipments.Count > 0)
        {
            Shipments.Clear();
            DirtyShipments = true;
        }

        // Lo de la campaña vuelve a empezar; lo ganado en eventos (contratos y cosméticos) se conserva, igual que su material, que no reaparece.
        Upgrades.Clear();
        foreach (var id in ContractsDone.Where(id => Content.Contracts.GetValueOrDefault(id)?.Event == null).ToList()) ContractsDone.Remove(id);
        Stats.Clear();
        ObjectiveIndex = 0;
        Credits = 0;
        Expeditions++;

        // Los yacimientos conservan su identidad (no hay retirada de yacimientos en el protocolo) y vuelven a quedar tapados.
        foreach (var n in Nodes.Values)
        {
            n.Revealed = false;
            n.OccupiedBy = -1;
            DirtyNodes.Add(n.Id);
        }
        Reindex();
        foreach (var planet in Content.Planets)
        {
            Credits += planet.StartCredits;
            foreach (var b in planet.Builtins)
            {
                var m = new Machine { Id = NextId++, Def = b.Def, Planet = planet.Id, X = b.X, Z = b.Z, Builtin = true };
                Machines[m.Id] = m;
                DirtyMachines.Add(m.Id);
            }
        }
        Reindex();
        foreach (var planet in Content.Planets)
        {
            foreach (var n in Nodes.Values.Where(n => n.Planet == planet.Id)) CoverNode(n);
            foreach (var f in planet.DebrisFields) ScatterDebris(planet, f);
        }
        foreach (var d in DebrisById.Values) DirtyDebris.Add(d.Id);
        Credits += ExpeditionStacks * B.Expedition.StartCreditsPerStack;

        var home = Content.Planets[0];
        var cell = B.CellSizeMm / 1000f;
        foreach (var player in Players.Values)
        {
            player.Bag.Clear();
            player.Planet = home.Id;
            player.TransitTo = -1;
            player.X = home.Spawn.X * cell;
            player.Y = 0.5f;
            player.Z = home.Spawn.Z * cell;
            player.CutterReadyTick = 0;
            DirtyPlayers.Add(player.Key);
        }
        Reindex();
        MarkGlobal();
        return CommandResult.Success;
    }

    /// <summary>Costra de hielo sobre un yacimiento ya existente, como en un planeta recién generado.</summary>
    private void CoverNode(ResourceNode node)
    {
        for (var dx = 0; dx < 2; dx++)
        for (var dz = 0; dz < 2; dz++)
        {
            if (_debrisCells.ContainsKey((node.Planet, node.X + dx, node.Z + dz))) continue;
            var d = new Debris { Id = NextId++, Def = "ice_crust", Planet = node.Planet, X = node.X + dx, Z = node.Z + dz, CoversNode = node.Id };
            d.Hp = B.Debris[d.Def].Hp;
            DebrisById[d.Id] = d;
            _debrisCells[(node.Planet, d.X, d.Z)] = d.Id;
        }
    }
}
