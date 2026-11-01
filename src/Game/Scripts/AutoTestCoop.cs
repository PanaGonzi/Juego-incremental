using System.Text;
using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>
/// Dos clientes reales compitiendo por la misma fábrica: colocar, enlazar, sacar material, desmontar,
/// reconectar y guardar. Se sincronizan con archivos en un directorio común, así que exige un solo PC.
/// Las acciones usan los mismos comandos que la interfaz, no entradas de Windows: es una prueba de integración.
/// </summary>
public partial class AutoTest
{
    private const int CoopCrateX = 8, CoopCrateZ = -16;
    private bool CoopIsA => _args.GetValueOrDefault("role", "a") == "a";
    private int _hashMismatchesBefore;
    private int _hashChecksBefore;

    private async Task Barrier(string name)
    {
        var dir = _args["barrier"];
        var other = CoopIsA ? "b" : "a";
        File.WriteAllText(Path.Combine(dir, $"{name}.{(CoopIsA ? "a" : "b")}"), "");
        var path = Path.Combine(dir, $"{name}.{other}");
        if (!await Until(() => File.Exists(path), 120)) throw new TimeoutException($"el otro cliente no llegó a «{name}»");
    }

    private void DumpGeometry(string tag)
    {
        var text = new StringBuilder();
        foreach (var m in W.Machines.Values.OrderBy(m => m.Id)) text.AppendLine($"M {m.Id} {m.Def} {m.Planet} {m.X} {m.Z} {m.Rot}");
        foreach (var l in W.Links.Values.OrderBy(l => l.Id)) text.AppendLine($"L {l.Id} {l.From} {l.To} {string.Join(";", l.Cells)}");
        File.WriteAllText(Path.Combine(_out, $"geom_{tag}.txt"), text.ToString());
    }

    private long BagsTotal(string item) => W.Players.Values.Sum(p => p.Bag.Get(item));

    private async Task CoopFactoryScenario()
    {
        var client = _view!.Session.Client;
        var side = CoopIsA ? 1 : -1;
        Check(await Until(() => W.Players.Values.Count(p => p.Connected) >= 2, 60), "2 jugadores conectados");
        Log($"Modo: {(_view.Session.IsLocal ? "anfitrión" : "cliente remoto")}, rol {(CoopIsA ? "A" : "B")}");

        // 1. Los dos colocan el mismo almacén en la misma celda.
        var depot = W.Machines.Values.First(m => m.Def == "depot" && m.Planet == 0);
        var def = W.Content.Machines["storage_crate"];
        var site = new Vector3((CoopCrateX + def.W / 2f) * 2, 0, (CoopCrateZ + def.D / 2f) * 2);
        await WalkTo(site + new Vector3(7 * side, 0, 2));
        await Barrier("colocar_listo");
        var builtBefore = W.Stats.GetValueOrDefault("built.storage_crate");
        var creditsBefore = W.Credits;
        var recoveredBefore = W.CountEverywhere("recovered");
        client.Send(new Command { Type = CommandType.Place, Text = "storage_crate", A = CoopCrateX, B = CoopCrateZ });
        Check(await Until(() => W.Stats.GetValueOrDefault("built.storage_crate") > builtBefore, 5), "el almacén se construye");
        await Wait(1.5);
        Check(W.Stats.GetValueOrDefault("built.storage_crate") == builtBefore + 1, "colocar a la vez construye una sola pieza");
        var crates = W.Machines.Values.Where(m => m.Def == "storage_crate" && m.X == CoopCrateX && m.Z == CoopCrateZ).ToList();
        Check(crates.Count == 1, $"un solo almacén en la celda: {crates.Count}");
        Check(creditsBefore - W.Credits == def.Credits, $"los créditos se cobran una vez: {creditsBefore} → {W.Credits}");
        var recoveredCost = def.Cost.GetValueOrDefault("recovered");
        Check(recoveredBefore - W.CountEverywhere("recovered") == recoveredCost, $"el material se cobra una vez: {recoveredBefore} → {W.CountEverywhere("recovered")}");
        var crate = crates[0];
        await Barrier("colocar_fin");
        DumpGeometry("1_colocar");

        // 2. Los dos sacan más lingotes de los que hay, y después los devuelven. Aún sin cintas que vacíen el depósito.
        var c = CenterOf(depot);
        await WalkTo(new Vector3(c.X + 5 * side, 0, c.Z + 3));
        await Barrier("sacar_listo");
        var ingotTotal = W.CountEverywhere("ingot");
        var ingotDepot = W.Machines[depot.Id].In.Get("ingot");
        var bagsBefore = BagsTotal("ingot");
        var ask = ingotDepot * 7 / 10;
        client.Send(new Command { Type = CommandType.Extract, A = depot.Id, Text = "ingot", Amount = ask });
        var expected = Math.Min(ask * 2, ingotDepot);
        Check(await Until(() => BagsTotal("ingot") - bagsBefore == expected, 5), $"se saca lo que había, ni más ni menos: {BagsTotal("ingot") - bagsBefore} de {expected}");
        await Barrier("sacar_fin");
        await Wait(1);
        Check(W.CountEverywhere("ingot") == ingotTotal, $"sacar a la vez conserva los lingotes: {ingotTotal} → {W.CountEverywhere("ingot")}");
        Check(W.Machines[depot.Id].In.Get("ingot") == ingotDepot - expected, "el depósito queda con la diferencia exacta");
        var mine = Me.Bag.Get("ingot");
        await Barrier("meter_listo");
        if (mine > 0) client.Send(new Command { Type = CommandType.Insert, A = depot.Id, Text = "ingot", Amount = mine });
        Check(await Until(() => Me.Bag.Get("ingot") == 0, 5), "devolver vacía la mochila");
        await Barrier("meter_fin");
        await Wait(1);
        Check(W.CountEverywhere("ingot") == ingotTotal, $"devolver a la vez conserva los lingotes: {W.CountEverywhere("ingot")}");
        Check(W.Machines[depot.Id].In.Get("ingot") == ingotDepot, "los lingotes vuelven al depósito");

        // 3. Los dos piden la misma cinta con la misma ruta.
        var linkedBefore = W.Stats.GetValueOrDefault("linked");
        var route = W.RouteBelt(depot.Id, crate.Id);
        Check(route.Cells.Count > 0, $"hay ruta del depósito al almacén: {route.Cells.Count} celdas");
        await Barrier("cinta_lista");
        client.Send(World.LinkCommand(depot.Id, crate.Id, route.Cells));
        Check(await Until(() => W.Stats.GetValueOrDefault("linked") > linkedBefore, 5), "la cinta se construye");
        await Wait(1.5);
        var belts = W.Links.Values.Where(l => l.From == depot.Id && l.To == crate.Id).ToList();
        Check(belts.Count == 1, $"una sola cinta entre las mismas piezas: {belts.Count}");
        Check(W.Stats.GetValueOrDefault("linked") == linkedBefore + 1, "enlazar a la vez construye una sola cinta");
        await Barrier("cinta_fin");
        DumpGeometry("2_cinta");

        // Los dos ven pasar la misma carga.
        var line = belts[0];
        var lineMid = line.Cells[line.Cells.Count / 2];
        var lineAim = new Vector3((lineMid.x + 0.5f) * 2, 0.5f, (lineMid.z + 0.5f) * 2);
        await WalkTo(lineAim + new Vector3(5 * side, -0.5f, 3));
        _view.AimAt(lineAim);
        // Hasta que la carga pase del tramo enfocado: antes, la captura no probaría que se ve.
        Check(await Until(() => W.Links[line.Id].Load >= W.Links[line.Id].Slots.Length * 3 / 4, 60), "la cinta lleva carga real en este cliente");
        await Barrier("carga_lista");
        await Wait(1);
        Check(_view.Belts.DrawnItems > 0, $"objetos dibujados en este cliente: {_view.Belts.DrawnItems}, carga lógica {W.Links[line.Id].Load}");
        await Shot(CoopIsA ? "coop_cinta_a" : "coop_cinta_b");

        // 4. Los dos desmontan la cinta cargada: la devolución ocurre una vez.
        await Barrier("quitar_listo");
        var paid = W.Links[line.Id].Paid;
        var credits = W.Credits;
        var recovered = W.CountEverywhere("recovered");
        client.Send(new Command { Type = CommandType.Unlink, A = line.Id });
        Check(await Until(() => !W.Links.ContainsKey(line.Id), 5), "la cinta se retira");
        await Wait(1.5);
        Check(W.CountEverywhere("recovered") == recovered, $"retirar a la vez conserva la carga: {recovered} → {W.CountEverywhere("recovered")}");
        var refund = W.Credits - credits;
        Check(refund >= 0 && refund <= paid, $"la devolución no se duplica: {refund} de {paid} pagados");
        await Barrier("quitar_fin");
        DumpGeometry("3_quitar");

        // 5. El cliente B se va con la fábrica en marcha y vuelve.
        if (CoopIsA)
        {
            var again = W.RouteBelt(depot.Id, crate.Id);
            client.Send(World.LinkCommand(depot.Id, crate.Id, again.Cells));
        }
        Check(await Until(() => W.Links.Values.Any(l => l.From == depot.Id && l.To == crate.Id), 5), "cinta reconstruida para la reconexión");
        var rebuilt = W.Links.Values.First(l => l.From == depot.Id && l.To == crate.Id).Id;
        await Barrier("reconectar_listo");
        await CoopReconnect(rebuilt);
        client = _view.Session.Client;
        await Barrier("reconectar_fin");
        await Wait(1.5);
        DumpGeometry("4_reconectar");
        Check(W.Links.ContainsKey(rebuilt), "la cinta sigue en el estado recibido tras reconectar");
        Check(await Until(() => W.Links[rebuilt].Load > 0, 15), $"la carga se ve en el estado recibido: {W.Links[rebuilt].Load}");
        await CoopSignals(client);
        await Wait(4);
        Check(_hashMismatchesBefore + client.HashMismatches == 0, $"sin divergencias ({_hashChecksBefore + client.HashChecks} comprobaciones)");
        Check(_hashChecksBefore + client.HashChecks > 0, "ha habido comprobaciones de hash");
        await Barrier("fin_cooperativo");

        // 6. Solo con anfitrión: guardar, cerrar y volver a cargar la misma partida.
        if (_view.Session.IsLocal) await CoopSaveAndLoad();
    }

    /// <summary>Uno tiende la vía y parte con una señal, y el otro la quita y la vuelve a poner. Los dos ven lo mismo y lo pagado cuadra.</summary>
    private async Task CoopSignals(Pluton.Networking.GameClient client)
    {
        await Barrier("senales_listo");
        const int X = 0, Length = 8;
        var z = -16;
        if (CoopIsA)
        {
            while (z < 12 && W.CheckRail(0, X, z, X + Length, z, 0, out _) != null) z++;
            Check(z < 12, $"sitio libre para la vía en z={z}");
            await WalkTo(new Vector3((X + Length / 2f) * 2, 0, z * 2 + 6));
            client.Send(new Command { Type = CommandType.PlaceRail, A = X, B = z, C = X + Length, D = z });
        }
        Check(await Until(() => W.RailSegments.Count > 0, 10), "los dos ven la vía");
        var first = W.RailSegments.Values.First();
        z = W.RailNodes[first.A].Z;
        if (!CoopIsA) await WalkTo(new Vector3((X + Length / 2f) * 2, 0, z * 2 - 6));
        await Barrier("senales_via");
        var credits = W.Credits;
        RailNode? Mid() => W.RailNodes.Values.FirstOrDefault(n => n.X == X + 4 && n.Z == z);
        if (CoopIsA) client.Send(new Command { Type = CommandType.ToggleSignal, A = X + 4, B = z });
        Check(await Until(() => Mid()?.Signal == true, 10), "los dos ven la señal, con la vía partida");
        Check(W.RailSegments.Count == 2, $"tramos tras partir: {W.RailSegments.Count}");
        await Barrier("senales_puesta");
        if (!CoopIsA) client.Send(new Command { Type = CommandType.ToggleSignal, A = X + 4, B = z });
        Check(await Until(() => Mid()?.Signal == false, 10), "los dos ven que B la quita");
        await Barrier("senales_quitada");
        Check(await Until(() => W.Credits == credits, 5), $"poner y quitar cuesta cero: {credits} → {W.Credits}");
        if (CoopIsA) client.Send(new Command { Type = CommandType.ToggleSignal, A = X + 4, B = z });
        Check(await Until(() => Mid()?.Signal == true, 10), "queda puesta para el guardado");
        await Barrier("senales_fin");
    }

    private async Task CoopReconnect(int beltId)
    {
        if (CoopIsA)
        {
            Check(await Until(() => W.Players.Values.Count(p => p.Connected) == 1, 15), "A ve salir a B");
            var load = W.CountEverywhere("recovered");
            await Wait(2);
            Check(W.Links.ContainsKey(beltId) && W.CountEverywhere("recovered") == load, "la fábrica sigue y conserva la carga sin B");
            Check(await Until(() => W.Players.Values.Count(p => p.Connected) == 2, 30), "A ve volver a B");
            Check(W.Players.Count == 2, $"la reconexión no crea otro jugador: {W.Players.Count}");
            return;
        }
        var bag = Me.Bag.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
        var key = Me.Key;
        var client = _view!.Session.Client;
        _hashMismatchesBefore += client.HashMismatches;
        _hashChecksBefore += client.HashChecks;
        var old = _view;
        _main.EndSession("");
        _view = null;
        await Wait(3);
        var parts = _args["join"].Split(':');
        _main.StartJoin(parts[0], int.Parse(parts[1]), "");
        _view = _main.GetChildren().OfType<GameView>().First(v => v != old);
        Check(await Until(() => _view.Session.Client.State == Pluton.Networking.ClientState.InGame, 20), "B vuelve a entrar");
        Check(await Until(() => _view.Session.Client.LocalPlayer != null, 5), "B recupera su jugador");
        Check(Me.Key == key, "misma identidad");
        Check(bag.All(kv => Me.Bag.Get(kv.Key) == kv.Value) && Me.Bag.Items.All(kv => bag.GetValueOrDefault(kv.Key) == kv.Value), "misma mochila");
    }

    private async Task CoopSaveAndLoad()
    {
        const string Slot = "autotest-coop";
        var path = _main.Store.PathOf(Slot);
        Check(await Until(() => W.Players.Values.Count(p => p.Connected) == 1, 30), "el invitado ha terminado");
        var before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        _view!.Session.Client.RequestSave();
        Check(await Until(() => File.Exists(path) && File.GetLastWriteTimeUtc(path) > before, 15), "la partida se guarda con la orden normal");
        await Wait(1);
        var links = W.Links.Values.OrderBy(l => l.Id).Select(l => $"{l.Id} {l.From} {l.To} {string.Join(";", l.Cells)}").ToList();
        var machines = W.Machines.Keys.OrderBy(i => i).ToList();
        var recovered = W.CountEverywhere("recovered");
        var ingots = W.CountEverywhere("ingot");
        var signals = W.RailNodes.Values.Count(n => n.Signal);
        var old = _view;
        var port = int.Parse(_args["host"]);
        _main.EndSession("");
        _view = null;
        await Wait(3);
        _main.StartHost(Slot, false, port);
        _view = _main.GetChildren().OfType<GameView>().First(v => v != old);
        Check(await Until(() => _view.Session.Client.State == Pluton.Networking.ClientState.InGame, 20), "la partida guardada vuelve a abrirse");
        await Wait(1.5);
        Check(W.Links.Values.OrderBy(l => l.Id).Select(l => $"{l.Id} {l.From} {l.To} {string.Join(";", l.Cells)}").SequenceEqual(links), "las cintas conservan su trazado al cargar");
        Check(W.Machines.Keys.OrderBy(i => i).SequenceEqual(machines), "las máquinas siguen al cargar");
        Check(W.CountEverywhere("recovered") == recovered, $"la carga se conserva al cargar: {recovered} → {W.CountEverywhere("recovered")}");
        Check(W.CountEverywhere("ingot") == ingots, $"los lingotes se conservan al cargar: {ingots} → {W.CountEverywhere("ingot")}");
        Check(W.RailNodes.Values.Count(n => n.Signal) == signals && signals > 0, $"las señales se conservan al cargar: {signals}");
        DumpGeometry("5_cargada");
    }
}
