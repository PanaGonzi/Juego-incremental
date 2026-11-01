using Pluton.Networking;
using Pluton.Persistence;
using Pluton.Simulation;
using Xunit;

namespace Pluton.Tests;

/// <summary>Divisor, unificador, producción medida por máquina y seguimiento de cadenas sobre mundos reales.</summary>
public class FactoryManagementTests
{
    private const int Bx = 8, Bz = 8;

    /// <summary>Mundo limpio: sin escombros en la zona de pruebas, con todo desbloqueado y créditos de sobra.</summary>
    private static (World w, PlayerState p, Machine depot) Stage()
    {
        var (w, p) = Fixture.NewWorld();
        foreach (var d in w.DebrisById.Values.Where(d => d.Planet == 0 && Math.Abs(d.X - Bx) < 30 && Math.Abs(d.Z - Bz) < 30).ToList()) w.DebrisById.Remove(d.Id);
        foreach (var u in w.Content.Upgrades.Keys) w.Upgrades.Add(u);
        w.Credits = 1_000_000;
        var depot = Fixture.Find(w, "depot");
        foreach (var item in new[] { "recovered", "ingot", "parts", "coolant", "wafer" }) depot.In.Add(item, 2000);
        w.Reindex();
        p.Role = Role.Owner;
        return (w, p, depot);
    }

    private static Machine Place(World w, PlayerState p, string def, int x, int z, int rot = 0)
    {
        p.X = x * 2f;
        p.Z = z * 2f;
        var result = w.Apply(p, new Command { Type = CommandType.Place, Text = def, A = x, B = z, C = rot });
        Assert.True(result.Ok, $"{def} en {x},{z}: {result.Error}");
        return w.Machines.Values.Last(m => m.Def == def);
    }

    private static Link Connect(World w, PlayerState p, Machine a, Machine b)
    {
        var result = w.Apply(p, Fixture.LinkCmd(w, a, b));
        Assert.True(result.Ok, $"{a.Def} → {b.Def}: {result.Error}");
        return w.Links.Values.Last();
    }

    private static void Run(World w, int ticks)
    {
        for (var i = 0; i < ticks; i++) w.Step();
    }

    // ---------- Puertos ----------

    [Fact]
    public void Divisor_una_entrada_detras_y_tres_salidas()
    {
        var (w, p, _) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var outs = w.BeltPorts(s, true).ToList();
        var ins = w.BeltPorts(s, false).ToList();
        Assert.Equal(3, outs.Count);
        Assert.Single(ins);
        var front = World.FrontDir(s.Rot);
        Assert.Equal((front + 2) % 4, ins[0].Dir);
        Assert.Contains(outs, o => o.Dir == front && !o.Side);
        Assert.Equal(2, outs.Count(o => o.Side));
    }

    [Fact]
    public void Unificador_tres_entradas_y_una_salida_y_no_admite_mas_cintas()
    {
        var (w, p, _) = Stage();
        var m = Place(w, p, "merger", Bx, Bz);
        Assert.Single(w.BeltPorts(m, true));
        Assert.Equal(3, w.BeltPorts(m, false).Count());

        var sources = new[] { Place(w, p, "storage_crate", Bx - 8, Bz + 4), Place(w, p, "storage_crate", Bx + 8, Bz + 4), Place(w, p, "storage_crate", Bx, Bz + 10) };
        foreach (var source in sources) Connect(w, p, source, m);
        var fourth = Place(w, p, "storage_crate", Bx + 8, Bz + 12);
        Assert.NotNull(w.RouteBelt(fourth.Id, m.Id, 200000).Error);
    }

    // ---------- Reparto ----------

    /// <summary>Depósito → divisor → tres contenedores: reparto rotatorio, sin crear ni perder nada.</summary>
    [Fact]
    public void Divisor_reparte_por_turnos_y_conserva()
    {
        var (w, p, _) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var north = Place(w, p, "storage_crate", Bx, Bz - 6);
        var east = Place(w, p, "storage_crate", Bx + 6, Bz);
        var west = Place(w, p, "storage_crate", Bx - 7, Bz);
        var feed = Place(w, p, "storage_crate", Bx, Bz + 6);
        feed.In.Add("scrap", 90);
        Connect(w, p, feed, s);
        Connect(w, p, s, north);
        Connect(w, p, s, east);
        Connect(w, p, s, west);

        var total = w.CountEverywhere("scrap");
        Run(w, 1500);
        Assert.Equal(total, w.CountEverywhere("scrap"));
        Assert.Equal(0, feed.In.Get("scrap"));
        Assert.Equal(30, north.In.Get("scrap"));
        Assert.Equal(30, east.In.Get("scrap"));
        Assert.Equal(30, west.In.Get("scrap"));
    }

    /// <summary>Si una salida no acepta el objeto, el reparto prueba otra y nada se queda atascado.</summary>
    [Fact]
    public void Divisor_salta_la_salida_que_no_acepta_el_tipo()
    {
        var (w, p, _) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var smelter = Place(w, p, "smelter", Bx, Bz - 8); // solo acepta mineral
        var recycler = Place(w, p, "recycler", Bx + 6, Bz - 1); // solo acepta chatarra
        var feed = Place(w, p, "storage_crate", Bx, Bz + 6);
        feed.In.Add("ore", 40);
        feed.In.Add("scrap", 40);
        Connect(w, p, feed, s);
        Connect(w, p, s, smelter);
        Connect(w, p, s, recycler);

        var ore = w.CountEverywhere("ore");
        Run(w, 400);
        Assert.Equal(0, smelter.In.Get("scrap"));
        Assert.Equal(0, recycler.In.Get("ore"));
        Assert.True(w.ProducedTotal("recovered") > 0, "la chatarra pasa");
        Assert.True(w.ProducedTotal("ingot") > 0, "el mineral pasa");
        Assert.True(ore > w.CountEverywhere("ore"), "se consume mineral");
    }

    /// <summary>Con una salida llena, el resto sigue recibiendo; al vaciarse, vuelve a entrar en el reparto.</summary>
    [Fact]
    public void Salida_llena_no_detiene_a_las_demas()
    {
        var (w, p, _) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var full = Place(w, p, "storage_crate", Bx, Bz - 6);
        var free = Place(w, p, "storage_crate", Bx + 6, Bz);
        var feed = Place(w, p, "storage_crate", Bx, Bz + 6);
        var capacity = w.Content.Machines["storage_crate"].Capacity;
        full.In.Add("scrap", capacity);
        feed.In.Add("scrap", 60);
        Connect(w, p, feed, s);
        var toFull = Connect(w, p, s, full);
        Connect(w, p, s, free);

        var total = w.CountEverywhere("scrap");
        Run(w, 800);
        Assert.Equal(total, w.CountEverywhere("scrap"));
        Assert.True(free.In.Get("scrap") + toFull.Load + s.In.Get("scrap") == 60, "todo está en el reparto o en la salida libre");
        Assert.True(free.In.Get("scrap") >= 60 - toFull.Slots.Length - 2, $"la salida libre recibe: {free.In.Get("scrap")}");

        full.In.TryRemove("scrap", 20);
        var taken = total - 20;
        Run(w, 400);
        Assert.Equal(taken, w.CountEverywhere("scrap"));
        Assert.Equal(0, toFull.Load);
    }

    /// <summary>Tres cintas hacia un unificador con una salida más lenta que la suma: todas pasan a partes iguales.</summary>
    [Fact]
    public void Unificador_da_paso_por_turnos_y_conserva()
    {
        var (w, p, _) = Stage();
        var m = Place(w, p, "merger", Bx, Bz);
        var a = Place(w, p, "storage_crate", Bx - 7, Bz);
        var b = Place(w, p, "storage_crate", Bx + 6, Bz);
        var c = Place(w, p, "storage_crate", Bx, Bz + 6);
        var sink = Place(w, p, "storage_crate", Bx, Bz - 6);
        foreach (var source in new[] { a, b, c }) source.In.Add("scrap", 100);
        var la = Connect(w, p, a, m);
        var lb = Connect(w, p, b, m);
        var lc = Connect(w, p, c, m);
        Connect(w, p, m, sink);

        var total = w.CountEverywhere("scrap");
        Run(w, 200);
        Assert.Equal(total, w.CountEverywhere("scrap"));
        Assert.True(sink.In.Get("scrap") > 20, $"llegan objetos: {sink.In.Get("scrap")}");
        // Lo entregado es lo que ha salido de cada origen menos lo que aún lleva su cinta: debe ir parejo.
        var delivered = new[] { 100 - a.In.Get("scrap") - la.Load, 100 - b.In.Get("scrap") - lb.Load, 100 - c.In.Get("scrap") - lc.Load };
        Assert.True(delivered.Max() - delivered.Min() <= 2, $"reparto parejo: {string.Join(",", delivered)}");
    }

    // ---------- Retirada, reconexión y guardado ----------

    [Fact]
    public void Retirar_divisor_con_carga_devuelve_todo_al_deposito()
    {
        var (w, p, depot) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var sink = Place(w, p, "storage_crate", Bx, Bz - 6);
        var feed = Place(w, p, "storage_crate", Bx, Bz + 6);
        feed.In.Add("scrap", 50);
        Connect(w, p, feed, s);
        Connect(w, p, s, sink);
        Run(w, 30);
        var total = w.CountEverywhere("scrap");
        var recoveredBefore = w.CountEverywhere("recovered");
        Assert.True(w.ItemsOnBelts() > 0, "hay carga en las cintas");

        Fixture.StandAt(w, p, s);
        Assert.True(w.Apply(p, new Command { Type = CommandType.Remove, A = s.Id }).Ok);
        Assert.Equal(total, w.CountEverywhere("scrap"));
        Assert.Equal(recoveredBefore + w.Content.Machines["splitter"].Cost["recovered"], w.CountEverywhere("recovered"));
        Assert.Empty(w.Links.Values.Where(l => l.From == s.Id || l.To == s.Id));
        Assert.True(depot.In.Get("scrap") > 0);
    }

    /// <summary>Desconectar deja esperar; la carga no se pierde y al reconectar sigue su camino.</summary>
    [Fact]
    public void Reconexion_de_una_salida_conserva_y_reanuda()
    {
        var (w, p, _) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var sink = Place(w, p, "storage_crate", Bx, Bz - 6);
        var feed = Place(w, p, "storage_crate", Bx, Bz + 6);
        feed.In.Add("scrap", 80);
        Connect(w, p, feed, s);
        var output = Connect(w, p, s, sink);
        Run(w, 40);
        var total = w.CountEverywhere("scrap");

        Assert.True(w.Apply(p, new Command { Type = CommandType.Unlink, A = output.Id }).Ok);
        Assert.Equal(total, w.CountEverywhere("scrap"));
        Run(w, 100); // sin salidas: el búfer se llena y la entrada espera
        Assert.Equal(total, w.CountEverywhere("scrap"));
        Assert.Equal(w.Content.Machines["splitter"].Capacity, s.In.Get("scrap"));

        Connect(w, p, s, sink);
        Run(w, 600);
        Assert.Equal(total, w.CountEverywhere("scrap"));
        Assert.Equal(0, s.In.Get("scrap"));
        Assert.Equal(0, feed.In.Get("scrap"));
    }

    /// <summary>Si la salida que aceptaba un tipo desaparece, lo que quedaba de ese tipo no bloquea al otro: vuelve al depósito.</summary>
    [Fact]
    public void Tipo_sin_salida_tras_reconectar_vuelve_al_deposito_y_no_atasca()
    {
        var (w, p, depot) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var smelter = Place(w, p, "smelter", Bx, Bz - 8);
        var recycler = Place(w, p, "recycler", Bx + 6, Bz - 1);
        var feed = Place(w, p, "storage_crate", Bx, Bz + 6);
        feed.In.Add("ore", 60);
        feed.In.Add("scrap", 60);
        Connect(w, p, feed, s);
        Connect(w, p, s, smelter);
        var toRecycler = Connect(w, p, s, recycler);
        Run(w, 60);

        var scrap = w.CountEverywhere("scrap");
        Assert.True(w.Apply(p, new Command { Type = CommandType.Unlink, A = toRecycler.Id }).Ok);
        Run(w, 100);
        // La chatarra que venía de camino sale del divisor hacia el depósito y el mineral sigue pasando.
        Assert.Equal(0, s.In.Get("scrap"));
        Assert.Equal(scrap, w.CountEverywhere("scrap"));
        Assert.True(depot.In.Get("scrap") > 0, "la chatarra retenida llega al depósito");
        var ingots = w.ProducedTotal("ingot");
        Run(w, 400);
        Assert.True(w.ProducedTotal("ingot") > ingots, "el mineral sigue pasando");
        Assert.Equal(scrap, w.CountEverywhere("scrap"));
    }

    [Fact]
    public void Guardado_conserva_buffer_y_carga_de_divisor_y_unificador()
    {
        var (w, p, _) = Stage();
        var s = Place(w, p, "splitter", Bx, Bz);
        var m = Place(w, p, "merger", Bx, Bz - 5);
        var sink = Place(w, p, "storage_crate", Bx, Bz - 11);
        var feed = Place(w, p, "storage_crate", Bx, Bz + 6);
        feed.In.Add("scrap", 60);
        Connect(w, p, feed, s);
        Connect(w, p, s, m);
        Connect(w, p, m, sink);
        Run(w, 40);

        var loaded = WorldCodec.Deserialize(w.Content, WorldCodec.Serialize(w));
        Assert.Equal(WorldCodec.Hash(w), WorldCodec.Hash(loaded));
        var total = loaded.CountEverywhere("scrap");
        Assert.Equal(w.CountEverywhere("scrap"), total);
        Run(loaded, 800);
        Assert.Equal(total, loaded.CountEverywhere("scrap"));
        Assert.Equal(60, loaded.Machines[sink.Id].In.Get("scrap"));
    }

    // ---------- Producción medida ----------

    [Fact]
    public void Contadores_miden_cada_maquina_por_separado_y_coinciden_con_lo_producido()
    {
        var (w, p, _) = Stage();
        var free = Fixture.Find(w, "recycler");
        var limited = Place(w, p, "recycler", Bx, Bz);
        free.In.Add("scrap", 100000);
        limited.In.Add("scrap", 10); // da para cinco ciclos y se queda sin material
        var changes = new List<(int id, int cycles, int seconds)>();
        var ticks = w.Content.Balance.TicksPerSecond * 30;
        for (var i = 0; i < ticks; i++)
        {
            w.Step();
            free.Out.Clear();
            if (w.Tick % w.Content.Balance.TicksPerSecond == 0) w.CollectRateChanges(changes);
        }

        var theory = w.ReadMachine(free).Theoretical.Single(t => t.item == "recovered").perMinute;
        Assert.InRange(w.MeasuredPerMinute(free, "recovered")!.Value, theory * 0.9, theory * 1.1);
        // Cifra por máquina, no media global: dos recicladores iguales con rendimientos distintos.
        Assert.Equal(5, w.MeasuredRates[limited.Id].cycles);
        Assert.InRange(w.MeasuredPerMinute(limited, "recovered")!.Value, theory * 0.45, theory * 0.55);
        Assert.Equal(w.ProducedTotal("recovered"), w.MeasuredRates[free.Id].cycles + w.MeasuredRates[limited.Id].cycles);
    }

    [Fact]
    public void La_ventana_olvida_lo_que_tiene_mas_de_un_minuto()
    {
        var window = new RateWindow(0);
        window.Add(1);
        window.Add(30);
        window.Add(30);
        Assert.Equal((3, 30), window.Read(30));
        Assert.Equal((3, 60), window.Read(60));
        Assert.Equal((2, 60), window.Read(61));
        Assert.Equal((0, 60), window.Read(1000));
    }

    [Fact]
    public void Medicion_solo_informa_de_cambios_y_no_altera_la_firma()
    {
        var (w, _) = Fixture.NewWorld();
        var m = Fixture.Find(w, "recycler");
        var changes = new List<(int id, int cycles, int seconds)>();
        for (var i = 0; i < 80 * w.Content.Balance.TicksPerSecond; i++)
        {
            w.Step();
            if (w.Tick % w.Content.Balance.TicksPerSecond == 0) w.CollectRateChanges(changes);
        }
        Assert.Empty(changes); // ventana llena y sin producción: nada que enviar
        Assert.Equal((0, 60), w.MeasuredRates[m.Id]);

        var before = WorldCodec.Hash(w);
        var saved = WorldCodec.Serialize(w);
        w.MeasuredRates[m.Id] = (5, 60);
        Assert.Equal(before, WorldCodec.Hash(w));
        Assert.Equal(saved, WorldCodec.Serialize(w));
        Assert.Empty(WorldCodec.Deserialize(w.Content, saved).MeasuredRates);
    }

    // ---------- Cadena ----------

    private static (World w, PlayerState p, Machine ex, Machine sm, Machine crate, Link first, Link second) Line()
    {
        var (w, p, _) = Stage();
        var sm = Place(w, p, "smelter", Bx, Bz);
        var crate = Place(w, p, "storage_crate", Bx, Bz - 7);
        var source = Place(w, p, "storage_crate", Bx, Bz + 7);
        source.In.Add("ore", 500);
        var (first, second) = (Connect(w, p, source, sm), Connect(w, p, sm, crate));
        return (w, p, source, sm, crate, first, second);
    }

    [Fact]
    public void Cadena_incluye_ambos_sentidos_y_termina_en_los_almacenes()
    {
        var (w, _, source, sm, crate, first, second) = Line();
        var t = w.TraceChain(sm.Id)!;
        Assert.Contains(source.Id, t.Machines);
        Assert.Contains(crate.Id, t.Machines);
        Assert.Contains(first.Id, t.Links);
        Assert.Contains(second.Id, t.Links);
        Assert.Equal(3, t.Machines.Count);
        Assert.Null(w.TraceChain(987654));
    }

    [Fact]
    public void Cadena_almacen_vacio_es_falta_de_material_en_el_origen()
    {
        var (w, _, source, sm, _, _, _) = Line();
        source.In.Clear();
        Run(w, 3);
        Assert.Equal(MachineStatus.NoInput, sm.Status);
        var limit = w.TraceChain(sm.Id)!.Limit;
        Assert.Equal(ChainCause.NoMaterial, limit.Cause);
        Assert.Equal(source.Id, limit.Machine);
        Assert.Equal("ore", limit.Item);
    }

    [Fact]
    public void Cadena_maquina_sin_cintas_es_falta_de_material_en_ella_misma()
    {
        var (w, _) = Fixture.NewWorld();
        var m = Fixture.Find(w, "recycler");
        w.Step();
        var limit = w.TraceChain(m.Id)!.Limit;
        Assert.Equal(ChainCause.NoMaterial, limit.Cause);
        Assert.Equal(m.Id, limit.Machine);
    }

    [Fact]
    public void Cadena_salida_llena_termina_en_el_almacen_lleno()
    {
        var (w, _, _, sm, crate, _, second) = Line();
        crate.In.Add("ingot", w.Content.Machines["storage_crate"].Capacity);
        Run(w, 1800);
        Assert.Equal(MachineStatus.OutputFull, sm.Status);
        Assert.True(second.Load > 0);
        var limit = w.TraceChain(sm.Id)!.Limit;
        Assert.Equal(ChainCause.OutputFull, limit.Cause);
        Assert.Equal(crate.Id, limit.Machine);
        Assert.Equal("ingot", limit.Item);
    }

    [Fact]
    public void Cadena_sin_energia_en_el_origen_se_ve_desde_el_consumidor()
    {
        var (w, p, _) = Stage();
        var downstreamAssembler = Place(w, p, "assembler", Bx, Bz);
        var feeder = Place(w, p, "smelter", Bx, Bz + 7);
        feeder.In.Add("ore", 400);
        Connect(w, p, feeder, downstreamAssembler);
        // Sin generación en el planeta.
        foreach (var g in w.Machines.Values.Where(m => w.Content.Machines[m.Def].PowerGen > 0 && m.Planet == 0).ToList())
            w.Machines.Remove(g.Id);
        w.Reindex();
        Run(w, 5);
        Assert.Equal(MachineStatus.NoPower, feeder.Status);
        var limit = w.TraceChain(downstreamAssembler.Id)!.Limit;
        Assert.Equal(ChainCause.NoPower, limit.Cause);
        Assert.Equal(feeder.Id, limit.Machine);
    }

    /// <summary>Productor rápido con consumidor lento: el rápido se llena y el lento, que trabaja sin parar, es el cuello.</summary>
    [Fact]
    public void Cadena_consumidor_lento_es_la_menor_capacidad()
    {
        var (w, p, _) = Stage();
        // Con todas las mejoras, fundición y ensambladora quedan empatadas a 30/min: un empate no es un cuello de botella.
        w.Upgrades.Remove("smelt_speed");
        w.Reindex();
        var smelter = Place(w, p, "smelter", Bx, Bz); // produce lingotes
        var assembler = Place(w, p, "assembler", Bx, Bz - 8); // los consume
        var crate = Place(w, p, "storage_crate", Bx, Bz - 15);
        var source = Place(w, p, "storage_crate", Bx, Bz + 7);
        source.In.Add("ore", 2000);
        Connect(w, p, source, smelter);
        Connect(w, p, smelter, assembler);
        Connect(w, p, assembler, crate);
        Run(w, 3000);

        var trace = w.TraceChain(smelter.Id)!;
        var machines = new[] { smelter, assembler };
        var limiting = machines.Single(m => m.Id == trace.Limit.Machine);
        Assert.Equal(ChainCause.LowestCapacity, trace.Limit.Cause);
        // Es la que está en marcha mientras la otra espera.
        Assert.Equal(MachineStatus.Working, limiting.Status);
        Assert.Contains(machines.Single(m => m != limiting).Status, new[] { MachineStatus.NoInput, MachineStatus.OutputFull, MachineStatus.Working });
    }

    /// <summary>Todo trabajando: sin paradas que seguir, decide la capacidad teórica en proporción a la receta.</summary>
    [Fact]
    public void Cadena_todo_en_marcha_compara_capacidades()
    {
        var (w, p, _) = Stage();
        // Con todas las mejoras, fundición y ensambladora quedan empatadas a 30/min: un empate no es un cuello de botella.
        w.Upgrades.Remove("smelt_speed");
        w.Reindex();
        var smelter = Place(w, p, "smelter", Bx, Bz);
        var assembler = Place(w, p, "assembler", Bx, Bz - 8);
        Connect(w, p, smelter, assembler);
        Run(w, 1);
        // Estado replicado equivalente a un instante en que ambas trabajan.
        smelter.Status = MachineStatus.Working;
        assembler.Status = MachineStatus.Working;
        var limit = w.TraceChain(smelter.Id)!.Limit;
        Assert.Equal(ChainCause.LowestCapacity, limit.Cause);

        var ingot = w.Content.Recipes[w.Content.Machines["smelter"].Recipe!];
        var parts = w.Content.Recipes[w.Content.Machines["assembler"].Recipe!];
        var smelterIngotsPerTick = ingot.Outputs["ingot"] / (double)ingot.Ticks;
        var assemblerIngotsPerTick = parts.Inputs["ingot"] / (double)parts.Ticks;
        Assert.Equal(smelterIngotsPerTick < assemblerIngotsPerTick ? smelter.Id : assembler.Id, limit.Machine);
    }

    [Fact]
    public void Cadena_cinta_saturada_entre_origen_lleno_y_destino_con_hueco()
    {
        var (w, p, _) = Stage();
        var producer = Fixture.Find(w, "recycler");
        var crate = Place(w, p, "storage_crate", Bx, Bz);
        var belt = Connect(w, p, producer, crate);
        Run(w, 1);
        // Cinta llena de punta a punta con el destino libre: va a todo lo que da.
        for (var i = 0; i < belt.Slots.Length; i++) w.SeedBeltSlot(belt, i, "recovered");
        producer.In.Add("scrap", 100);
        producer.Out.Add("recovered", w.Content.Balance.OutputBuffer);
        producer.Status = MachineStatus.OutputFull;
        var limit = w.TraceChain(producer.Id)!.Limit;
        Assert.Equal(ChainCause.BeltSaturated, limit.Cause);
        Assert.Equal(belt.Id, limit.Link);
    }

    /// <summary>La cadena atraviesa divisores y unificadores, y la rama ajena de un divisor no entra.</summary>
    [Fact]
    public void Cadena_atraviesa_divisor_y_no_incluye_ramas_ajenas()
    {
        var (w, p, _) = Stage();
        var source = Place(w, p, "storage_crate", Bx, Bz + 6);
        var s = Place(w, p, "splitter", Bx, Bz);
        var smelterA = Place(w, p, "smelter", Bx, Bz - 8);
        var smelterB = Place(w, p, "smelter", Bx + 6, Bz - 1);
        Connect(w, p, source, s);
        Connect(w, p, s, smelterA);
        Connect(w, p, s, smelterB);
        Run(w, 3);

        var t = w.TraceChain(smelterA.Id)!;
        Assert.Contains(s.Id, t.Machines);
        Assert.Contains(source.Id, t.Machines);
        Assert.DoesNotContain(smelterB.Id, t.Machines);
        Assert.Equal(ChainCause.NoMaterial, t.Limit.Cause);
        Assert.Equal(source.Id, t.Limit.Machine);
    }

    // ---------- Red ----------

    /// <summary>Por UDP real: la medición llega al cliente, también al que entra después, sin romper la firma ni pasar por los guardados.</summary>
    [Fact]
    public void Medicion_replicada_a_clientes_incluso_a_quien_entra_despues()
    {
        var world = World.CreateNew(Fixture.Content);
        var recycler = Fixture.Find(world, "recycler");
        recycler.In.Add("scrap", 1000);
        using var net = new NetHarness(new ServerOptions { CheckEveryTicks = 10 }, world);
        var first = net.Join("client-identity-rate1");
        net.Pump(() => first.World!.MeasuredRates.TryGetValue(recycler.Id, out var r) && r.cycles >= 1, 60000);

        var late = net.Join("client-identity-rate2");
        net.Settle(100);
        Assert.Equal(world.MeasuredRates[recycler.Id].cycles, late.World!.MeasuredRates[recycler.Id].cycles);
        Assert.Equal(0, first.HashMismatches);
        Assert.Equal(0, late.HashMismatches);
        Assert.Empty(WorldCodec.Deserialize(world.Content, WorldCodec.Serialize(world)).MeasuredRates);
    }
}
