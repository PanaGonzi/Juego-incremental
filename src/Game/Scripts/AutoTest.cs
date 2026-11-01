using System.Text;
using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>
/// Recorrido automático del cliente real: mueve al jugador, apunta con la cámara y usa las herramientas.
/// Comprueba resultados en el mundo replicado y guarda capturas y tiempos de frame.
/// Uso: Pluton.exe -- --autotest --profile test --outdir RUTA [--join host:puerto] [--seconds N] [--width W --height H]
/// </summary>
public partial class AutoTest : Node
{
    private readonly Main _main;
    private readonly Dictionary<string, string> _args;
    private readonly string _out;
    private readonly StringBuilder _log = new();
    private readonly List<float> _frames = new();
    private GameView? _view;
    private double _clock;
    private bool _measuring;
    private int _failures;
    private ulong _lastUsec;
    private bool _skipFrame;
    private long _offlineCredits;
    private readonly List<string> _spikes = new();

    public AutoTest(Main main, Dictionary<string, string> args)
    {
        _main = main;
        _args = args;
        _out = args.GetValueOrDefault("outdir", ProjectSettings.GlobalizePath("user://autotest"));
        Directory.CreateDirectory(_out);
    }

    public override void _Ready()
    {
        if (_args.TryGetValue("width", out var w) && _args.TryGetValue("height", out var h))
            DisplayServer.WindowSetSize(new Vector2I(int.Parse(w), int.Parse(h)));
        _ = Run();
    }

    public override void _Process(double delta)
    {
        _clock += delta;
        var now = Time.GetTicksUsec();
        if (_skipFrame) _skipFrame = false;
        else if (_measuring && _lastUsec != 0)
        {
            var frameMs = (now - _lastUsec) / 1000f;
            _frames.Add(frameMs);
            if (frameMs > 33.3f) _spikes.Add($"{_clock:F2}s:{frameMs:F0}ms");
        }
        _lastUsec = now;
    }

    private void Log(string message)
    {
        var line = $"[{_clock,7:F1}] {message}";
        GD.Print(line);
        _log.AppendLine(line);
        File.WriteAllText(Path.Combine(_out, "autotest.log"), _log.ToString(), new UTF8Encoding(true));
    }

    private void Check(bool ok, string what)
    {
        Log((ok ? "OK    " : "FALLO ") + what);
        if (!ok) _failures++;
    }

    private async Task Wait(double seconds)
    {
        var until = _clock + seconds;
        while (_clock < until) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task<bool> Until(Func<bool> condition, double timeout)
    {
        var until = _clock + timeout;
        while (_clock < until)
        {
            if (condition()) return true;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        return condition();
    }

    private async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        image.SavePng(Path.Combine(_out, name + ".png"));
        // Guardar la imagen bloquea el hilo: ese frame no representa al juego.
        _skipFrame = true;
    }

    private World W => _view!.Session.Client.World!;
    private PlayerState Me => _view!.Session.Client.LocalPlayer!;

    private async Task WalkTo(Vector3 target, double timeout = 25)
    {
        _view!.AutoWalkTarget = target;
        await Until(() => _view.AutoWalkTarget == null, timeout);
        _view.AutoWalkTarget = null;
        await Wait(0.3);
    }

    private static Vector3 DebrisPos(Debris d) => new((d.X + 0.5f) * 2, 0.6f, (d.Z + 0.5f) * 2);

    private Vector3 CenterOf(Machine m)
    {
        var (x, z) = W.MachineCenterMm(m);
        return new Vector3(x / 1000f, 1.2f, z / 1000f);
    }

    /// <summary>Se acerca y corta con la herramienta mientras el escombro exista.</summary>
    private async Task CutDebris(Debris d)
    {
        var pos = DebrisPos(d);
        var approach = _view!.PlayerPosition.DirectionTo(pos) * -3f + pos;
        approach.Y = 0;
        await WalkTo(approach);
        _view.SetTool(Tool.Cutter);
        var id = d.Id;
        _view.AimAt(pos);
        await Wait(0.15);
        _view.AutoHoldPrimary = true;
        await Until(() => !W.DebrisById.ContainsKey(id), 12);
        _view.AutoHoldPrimary = false;
    }

    private async Task<bool> UseOn(Machine m, Action action)
    {
        var c = CenterOf(m);
        var approach = _view!.PlayerPosition.DirectionTo(c) * -6f + c;
        approach.Y = 0;
        await WalkTo(approach);
        _view.AimAt(c);
        await Wait(0.2);
        var ok = _view.AimTarget == ("machine", m.Id);
        action();
        await Wait(0.5);
        return ok;
    }

    private async Task<bool> Build(string def, int x, int z)
    {
        var d = W.Content.Machines[def];
        var center = new Vector3((x + d.W / 2f) * 2, 0, (z + d.D / 2f) * 2);
        var before = W.Stats.GetValueOrDefault("built." + def);
        var offset = _view!.PlayerPosition.DirectionTo(center) * -9f;
        await WalkTo(center + new Vector3(offset.X, 0, offset.Z));
        _view.SelectBuild(def);
        _view.AimAt(center);
        await Wait(0.3);
        await Shot("ghost_" + def);
        _view.Primary();
        var ok = await Until(() => W.Stats.GetValueOrDefault("built." + def) > before, 3);
        _view.SetTool(Tool.Cutter);
        await Wait(0.2);
        return ok;
    }

    private async Task Run()
    {
        try
        {
            await Wait(0.2);
            if (_args.TryGetValue("measure", out var measured))
            {
                int? measurePort = _args.TryGetValue("host", out var mp) ? int.Parse(mp) : null;
                _main.StartSession(GameSession.Local(_main, Scenarios.Build(_main.Content, measured), "autotest-" + measured, measurePort, _main.Prefs));
            }
            else if (_args.TryGetValue("host", out var hostPort))
            {
                _main.StartHost("autotest-host", true, int.Parse(hostPort));
            }
            else if (_args.TryGetValue("join", out var join))
            {
                var parts = join.Split(':');
                _main.StartJoin(parts[0], int.Parse(parts[1]), "");
            }
            else if (_args.GetValueOrDefault("scenario") == "halloween")
            {
                var world = RailWorld();
                foreach (var e in _main.Content.Events.Keys) world.EventsSeeded.Add(e);
                world.EventsSeeded.Remove("halloween");
                _main.StartSession(GameSession.Local(_main, world, "autotest-halloween", null, _main.Prefs));
            }
            else if (_args.GetValueOrDefault("scenario") == "offline")
            {
                // Mundo con una ausencia simulada en sus datos, cargado por la ruta normal del juego.
                var world = World.CreateNew(_main.Content);
                var terminal = world.Machines.Values.First(m => m.Def == "export_terminal");
                terminal.In.Add("ingot", 200);
                terminal.AutoSell = true;
                _offlineCredits = world.Credits;
                _main.Store.Save("autotest-offline", world);
                var bytes = WorldCodec.Serialize(world);
                world.SavedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3 * 3600;
                _main.Store.Write("autotest-offline", Pluton.Persistence.SaveStore.Encode(WorldCodec.Serialize(world), world.Content.Revision, world.Tick, DateTime.UtcNow));
                _main.StartSolo("autotest-offline", false);
            }
            else if (_args.GetValueOrDefault("scenario") == "mars")
            {
                var world = World.CreateNew(_main.Content);
                foreach (var id in new[] { "tech_assembly", "tech_rail", "tech_cryo", "tech_shuttle" }) world.Upgrades.Add(id);
                world.Credits = 50000;
                var marsDepot = world.Machines.Values.First(m => m.Def == "depot" && m.Planet == 1);
                marsDepot.In.Add("ingot", 300);
                marsDepot.In.Add("parts", 200);
                marsDepot.In.Add("recovered", 100);
                var plutoDepot = world.Machines.Values.First(m => m.Def == "depot" && m.Planet == 0);
                plutoDepot.In.Add("ingot", 100);
                plutoDepot.In.Add("parts", 60);
                plutoDepot.In.Add("cryo_module", 40);
                world.Reindex();
                _main.StartSession(GameSession.Local(_main, world, "autotest-mars", null, _main.Prefs));
            }
            else if (_args.GetValueOrDefault("scenario") == "travel")
            {
                var world = World.CreateNew(_main.Content);
                world.Upgrades.Add("tech_shuttle");
                world.Reindex();
                _main.StartSession(GameSession.Local(_main, world, "autotest-travel", null, _main.Prefs));
            }
            else if (_args.GetValueOrDefault("scenario") == "rail")
            {
                _main.StartSession(GameSession.Local(_main, RailWorld(), "autotest-rail", null, _main.Prefs));
            }
            else if (_args.GetValueOrDefault("scenario") == "belts")
            {
                _main.StartSession(GameSession.Local(_main, BeltWorld(), "autotest-belts", null, _main.Prefs));
            }
            else if (_args.GetValueOrDefault("scenario") == "beltslong")
            {
                _main.StartSession(GameSession.Local(_main, BeltStage(), "autotest-long", null, _main.Prefs));
            }
            else if (_args.GetValueOrDefault("scenario") == "beltstage")
            {
                // Solo prepara una partida para jugarla con entradas reales.
                _main.Store.Save("belts-real", BeltStage());
                Log("RESULTADO: CORRECTO");
                GetTree().Quit(0);
                return;
            }
            else if (_args.GetValueOrDefault("scenario") == "build")
            {
                var world = BeltWorld();
                world.Machines.Values.First(m => m.Def == "depot" && m.Planet == 0).In.TryRemove("ingot", 95); // no alcanza para la ensambladora
                _main.StartSession(GameSession.Local(_main, world, "autotest-build", null, _main.Prefs));
            }
            else if (_args.GetValueOrDefault("scenario") == "beltsv4")
            {
                // Guardado real de la 0.4.1 cargado por la ruta normal: migra a cintas con copia .v4.bak.
                System.IO.File.Copy(_args["fixture"], _main.Store.PathOf("autotest-v4"), true);
                _main.StartSolo("autotest-v4", false);
            }
            else
            {
                _main.StartSolo("autotest-" + DateTime.Now.ToString("HHmmss"), true);
            }
            _view = _main.GetChildren().OfType<GameView>().First();
            Check(await Until(() => _view.Session.Client.State == Pluton.Networking.ClientState.InGame, 20), "entrada en el mundo");
            Log($"Renderizador {RenderingServer.GetCurrentRenderingMethod()} GPU {RenderingServer.GetVideoAdapterName()} ventana {DisplayServer.WindowGetSize()}");

            await Wait(1.5);
            if (_args.Count <= 10 && !_args.ContainsKey("scenario") && !_args.ContainsKey("measure") && !_args.ContainsKey("join") && !_args.ContainsKey("host"))
            {
                Check(_view.ObjectiveMarkerVisible && _view.ObjectiveDistance > 0, $"marcador del primer objetivo a {_view.ObjectiveDistance:0} m");
                var firstTarget = W.DebrisById.Values.Where(d => d.Def == "scrap_pile").OrderBy(d => DebrisPos(d).DistanceSquaredTo(_view.PlayerPosition)).First();
                _view.AimAt(DebrisPos(firstTarget) + Vector3.Up);
                await Wait(0.2);
            }
            await Shot("01_inicio");

            if (_args.ContainsKey("measure")) await MeasureScenario();
            else if (_args.GetValueOrDefault("scenario") == "rail") await RailScenario();
            else if (_args.GetValueOrDefault("scenario") == "belts") await BeltScenario();
            else if (_args.GetValueOrDefault("scenario") == "build") await BuildScenario();
            else if (_args.GetValueOrDefault("scenario") == "beltsv4") await BeltMigrationScenario();
            else if (_args.GetValueOrDefault("scenario") == "beltslong") await BeltLongScenario();
            else if (_args.GetValueOrDefault("scenario") == "fp") await FirstPersonScenario();
            else if (_args.GetValueOrDefault("scenario") == "travel") await TravelScenario();
            else if (_args.GetValueOrDefault("scenario") == "halloween") await HalloweenScenario();
            else if (_args.GetValueOrDefault("scenario") == "mars") await MarsScenario();
            else if (_args.GetValueOrDefault("scenario") == "offline")
            {
                await Wait(1);
                Check(W.Credits > _offlineCredits, $"progreso de ausencia aplicado: {_offlineCredits} → {W.Credits}");
                await Shot("offline_aviso");
            }
            else if (_args.ContainsKey("join") || _args.ContainsKey("host")) await JoinedScenario();
            else await FullLoop();
        }
        catch (Exception e)
        {
            Check(false, "excepción: " + e);
        }
        Finish();
    }

    /// <summary>Primera persona: entrada real, trabajo, consola, inventario doble y el modelo externo de otro jugador.</summary>
    private async Task FirstPersonScenario()
    {
        var v = _view!;
        await InputPaths();
        await Shot("fp_reposo");

        // Inventario doble junto al depósito, con una transferencia exacta por el mismo comando de la interfaz.
        var depot = W.Machines.Values.First(m => m.Def == "depot");
        var c = CenterOf(depot);
        await WalkTo(new Vector3(c.X, 0, c.Z + 6.5f));
        v.AimAt(c);
        await Wait(0.2);
        var client = v.Session.Client;
        var before = client.LocalPlayer!.Bag.Get("scrap");
        Check(before >= 2, $"hay chatarra en la mochila: {before}");
        v.OpenInventory(InventoryTab.Items, depot.Id);
        await Wait(0.3);
        Check(v.Inventory.BagSlotCount > 0 && v.Inventory.MachineId == depot.Id, "inventario doble con la mochila y el depósito");
        var inside = depot.In.Get("scrap");
        client.Send(new Command { Type = CommandType.Insert, A = depot.Id, Text = "scrap", Amount = 2 });
        var moved = await Until(() => client.LocalPlayer!.Bag.Get("scrap") == before - 2, 3);
        await Wait(0.2);
        Check(moved && W.Machines[depot.Id].In.Get("scrap") == inside + 2, "transferencia parcial exacta confirmada por el servidor");
        await Shot("fp_inventario_doble");
        v.ClosePanelForTest();
        await Wait(0.2);
        Check(!v.PanelIsOpen, "el inventario se cierra");

        // Otro jugador, inyectado por la misma entrada que usa la red: herramienta en cada estado.
        var at = v.PlayerPosition + new Vector3(0, 0, 3.4f);
        v.AimAt(at + Vector3.Up);
        foreach (var (name, action) in new[]
        {
            ("fp_remoto_trabajando", Pluton.Networking.PlayerAction.Pack(Pluton.Networking.PlayerAction.Cutter, true)),
            ("fp_remoto_consola", Pluton.Networking.PlayerAction.Device),
            ("fp_remoto_guardada", Pluton.Networking.PlayerAction.Stowed),
        })
        {
            v.InjectRemoteForTest(at, 0, action);
            await Wait(0.4);
            v.InjectRemoteForTest(at, 0, action);
            await Wait(0.2);
            await Shot(name);
        }
    }

    private async Task JoinedScenario()
    {
        var seconds = double.Parse(_args.GetValueOrDefault("seconds", "20"));
        var expected = int.Parse(_args.GetValueOrDefault("expect-players", "2"));
        Check(await Until(() => W.Players.Values.Count(p => p.Connected) >= expected, 60), $"{expected} jugadores conectados");
        await Shot("join_inicio");
        var target = W.DebrisById.Values.OrderBy(d => DebrisPos(d).DistanceSquaredTo(_view!.PlayerPosition)).First();
        var creditsBefore = W.Credits;
        await CutDebris(target);
        Check(!W.DebrisById.ContainsKey(target.Id), "cliente remoto limpia un escombro validado por el servidor");
        Check(W.Credits > creditsBefore, "créditos compartidos actualizados");
        if (_args.ContainsKey("host")) await PlayersPanelCheck();
        var remote = await Until(() => _view!.FirstRemotePlayer != null, 15);
        Check(remote, "posición de otro jugador recibida e interpolada");
        if (_view!.FirstRemotePlayer is { } other)
        {
            var offset = _view.PlayerPosition.DirectionTo(other) * -7f + other;
            await WalkTo(new Vector3(offset.X, 0, offset.Z), 8);
            if (_view.FirstRemotePlayer is { } now) _view.AimAt(now + Vector3.Up);
            await Wait(0.3);
            await Shot("join_ve_jugador");
        }
        await Wait(seconds);
        await Shot("join_fin");
        Check(_view!.Session.Client.HashMismatches == 0, $"sin divergencias de hash ({_view.Session.Client.HashChecks} comprobaciones)");
    }

    private static void Key(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = false });
    }

    /// <summary>Atraviesa la misma ruta de entrada que un jugador: eventos de teclado y ratón.</summary>
    private async Task InputPaths()
    {
        var v = _view!;
        v.TestCapture = true;
        v.UpdateMouseMode();
        await Wait(0.2);
        Check(Input.MouseMode == Input.MouseModeEnum.Captured, "ratón capturado en partida");

        var start = v.PlayerPosition;
        Input.ActionPress("move_forward");
        await Wait(1);
        Input.ActionRelease("move_forward");
        Check(v.PlayerPosition.DistanceTo(start) > 3, $"W desplaza al jugador {v.PlayerPosition.DistanceTo(start):F1} m");

        var yaw = v.Yaw;
        Input.ParseInputEvent(new InputEventMouseMotion { Relative = new Vector2(120, 0) });
        await Wait(0.1);
        Check(!Mathf.IsEqualApprox(v.Yaw, yaw), "el ratón gira la cámara");

        Key(Godot.Key.L);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Link, "L selecciona el enlazador");
        Key(Godot.Key.Z);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Cutter, "Z cancela y vuelve a la multiherramienta");

        Controls.Rebind("tool_link", Godot.Key.K);
        Key(Godot.Key.L);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Cutter, "tras reasignar, L deja de seleccionar el enlazador");
        Key(Godot.Key.K);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Link, "K selecciona el enlazador reasignado");
        Check(Loc.T("objective_o_link").Contains("K"), "la ayuda muestra la tecla reasignada");
        Controls.Rebind("rotate", Godot.Key.K);
        Check(Controls.KeyNames("tool_link") == "—" || !Controls.KeyNames("tool_link").Contains("K"), "una tecla no queda asignada a dos acciones");
        Controls.ResetAll();
        Key(Godot.Key.Z);
        await Wait(0.1);
        Key(Godot.Key.L);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Link, "restaurar controles recupera L");
        Key(Godot.Key.Z);
        await Wait(0.1);

        Key(Godot.Key.Tab);
        await Wait(0.2);
        Check(v.InventoryOpen && Input.MouseMode == Input.MouseModeEnum.Visible, "Tab abre el inventario y libera el ratón");
        await Shot("00_inventario");
        Key(Godot.Key.Tab);
        await Wait(0.2);
        Check(!v.InventoryOpen && Input.MouseMode == Input.MouseModeEnum.Captured, "otra pulsación de Tab lo cierra y recupera el ratón");
        Key(Godot.Key.Tab);
        await Wait(0.2);
        Key(Godot.Key.Escape);
        await Wait(0.2);
        Check(!v.PanelIsOpen && !v.IsPaused, "Esc cierra el inventario sin pausar");

        Key(Godot.Key.Q);
        await Wait(0.2);
        Check(v.InventoryOpen && v.Inventory.CurrentTab == InventoryTab.Catalog, "Q abre el catálogo de construcción");
        await Wait(0.4);
        await Shot("00_menu_construir");
        Key(Godot.Key.Escape);
        await Wait(0.2);

        Key(Godot.Key.Key2);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Link, "la casilla 2 de la barra selecciona la cinta");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.WheelDown, Pressed = true });
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Remove, "la ruedecilla pasa a la casilla siguiente");
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Cutter && !v.IsStowed, "la casilla 1 saca la multiherramienta");
        Key(Godot.Key.Key1);
        await Wait(0.1);
        Check(v.IsStowed, "repetir la casilla 1 la guarda");
        Key(Godot.Key.Key1);
        await Wait(0.1);

        var serverWorld = v.Session.Runner!.Server.World;
        var running = serverWorld.Tick;
        await Wait(0.5);
        Check(serverWorld.Tick > running, "el mundo avanza antes de pausar");
        Key(Godot.Key.Escape);
        await Wait(0.3);
        Check(v.IsPaused, "Esc pausa");
        await Shot("00_pausa");
        var pausedAt = serverWorld.Tick;
        await Wait(1.2);
        Check(serverWorld.Tick == pausedAt, $"en modo individual la pausa detiene el mundo (tick {pausedAt})");
        Key(Godot.Key.Escape);
        await Wait(0.7);
        Check(!v.IsPaused && serverWorld.Tick > pausedAt, $"Esc reanuda (tick {serverWorld.Tick})");

        // Corte con el botón real sobre el escombro más cercano
        var d = W.DebrisById.Values.Where(x => x.Def == "scrap_pile").OrderBy(x => DebrisPos(x).DistanceSquaredTo(v.PlayerPosition)).First();
        var pos = DebrisPos(d);
        var approach = v.PlayerPosition.DirectionTo(pos) * -3f + pos;
        await WalkTo(new Vector3(approach.X, 0, approach.Z));
        v.AimAt(pos);
        await Wait(0.2);
        var hp = d.Hp;
        var id = d.Id;
        Input.ActionPress("primary");
        var cut = await Until(() => !W.DebrisById.TryGetValue(id, out var left) || left.Hp < hp, 4);
        await Wait(0.3);
        Check(v.IsWorking, "el cabezal trabaja mientras se mantiene el clic");
        await Shot("00_trabajando");
        Input.ActionRelease("primary");
        await Wait(0.1);
        Check(!v.IsWorking, "al soltar se interrumpe el trabajo");
        Check(cut, "mantener el clic izquierdo corta");

        // Pose de construcción con la consola.
        Key(Godot.Key.Key5);
        await Wait(0.9);
        await Shot("00_construccion");
        Key(Godot.Key.Key1);
        await Wait(0.2);

        v.TestCapture = false;
        v.UpdateMouseMode();
    }

    /// <summary>Bucle jugable de A0.1 con las herramientas reales del jugador.</summary>
    private async Task FullLoop()
    {
        await InputPaths();
        var w = W;
        var spawn = _view!.PlayerPosition;

        // 1. Limpieza
        var nearest = w.DebrisById.Values.Where(d => d.Def == "scrap_pile").OrderBy(d => DebrisPos(d).DistanceSquaredTo(spawn)).Take(10).ToList();
        for (var i = 0; i < nearest.Count; i++)
        {
            await CutDebris(nearest[i]);
            if (i == 1)
            {
                _view.AimAt(DebrisPos(nearest[2]));
                await Wait(0.2);
                await Shot("02_limpieza");
            }
        }
        Check(Me.Bag.Get("scrap") >= 50, $"chatarra en mochila: {Me.Bag.Get("scrap")}");

        // 2. Reciclar
        var recycler = w.Machines.Values.First(m => m.Def == "recycler");
        Check(await UseOn(recycler, () => _view.Session.Client.Send(new Command { Type = CommandType.Insert, A = recycler.Id })), "apuntar a la recicladora");
        Check(W.Machines[recycler.Id].In.Get("scrap") > 0 || W.Machines[recycler.Id].Processing, "chatarra depositada");

        // 3. Enlazar reciclador -> depósito con la herramienta
        var depot = w.Machines.Values.First(m => m.Def == "depot");
        var linkOk = await LinkMachines(recycler, depot);
        Check(linkOk, "enlace recicladora → depósito");
        await Shot("03_enlace");

        // Sigue alimentando la recicladora hasta reunir material para construir.
        var ok = await Until(() => W.Stats.GetValueOrDefault("produced.recovered") >= 4, 40);
        Check(ok, "producción de material recuperado");
        while (W.Machines[depot.Id].In.Get("recovered") + Me.Bag.Get("recovered") < 40 && Me.Bag.Get("scrap") > 0)
        {
            await UseOn(recycler, () => _view.Session.Client.Send(new Command { Type = CommandType.Insert, A = recycler.Id }));
            await Wait(8);
        }

        // 4. Yacimiento
        var node = w.Nodes.Values.Where(n => n.Item == "ore").OrderBy(n => n.X * n.X + n.Z * n.Z).First();
        foreach (var d in w.DebrisById.Values.Where(d => d.CoversNode == node.Id).ToList()) await CutDebris(d);
        Check(await Until(() => W.Nodes[node.Id].Revealed, 3), "yacimiento descubierto");
        await Shot("04_yacimiento");

        Check(await Build("ore_extractor", node.X, node.Z), "extractor colocado sobre el yacimiento");
        Check(await Build("smelter", node.X, node.Z - 5), "fundición colocada");
        var extractor = W.Machines.Values.First(m => m.Def == "ore_extractor");
        var smelter = W.Machines.Values.First(m => m.Def == "smelter");
        Check(await LinkMachines(extractor, smelter), "enlace extractor → fundición");
        await Shot("05_fabrica");

        Check(await Until(() => W.Stats.GetValueOrDefault("produced.ingot") >= 1, 45), "primer lingote producido");
        await Shot("06_produccion");

        // 5. Panel, venta y mejora
        _view.OpenMachinePanel(smelter.Id);
        await Wait(0.5);
        await Shot("07_panel_maquina");
        _view.ClosePanelForTest();
        _view.OpenTechPanel();
        await Wait(0.5);
        await Shot("08_panel_mejoras");
        _view.ClosePanelForTest();

        var creditsBefore = W.Credits;
        Check(await UseOn(smelter, () => _view.Session.Client.Send(new Command { Type = CommandType.Extract, A = smelter.Id })), "recoger lingotes");
        var terminal = W.Machines.Values.First(m => m.Def == "export_terminal");
        await UseOn(terminal, () => _view.Session.Client.Send(new Command { Type = CommandType.Insert, A = terminal.Id }));
        await Wait(0.4);
        foreach (var kv in W.Machines[terminal.Id].In.Items.ToList())
            _view.Session.Client.Send(new Command { Type = CommandType.Sell, A = terminal.Id, Text = kv.Key, Amount = kv.Value });
        Check(await Until(() => W.Credits > creditsBefore, 3), $"venta en terminal: {creditsBefore} → {W.Credits}");
        await Shot("09_venta");

        // 6. Guardar y comprobar archivo
        _view.Session.Client.RequestSave();
        await Wait(1);
        Check(File.Exists(_main.Store.PathOf(_view.Session.Slot)), "archivo de guardado creado");

        // 7. Rendimiento en esta escena
        _measuring = true;
        await WalkTo(new Vector3(10, 0, 20));
        await Wait(double.Parse(_args.GetValueOrDefault("seconds", "10")));
        _measuring = false;
        await Shot("10_final");

        var hash = W.Credits;
        var slot = _view.Session.Slot;
        _main.EndSession("");
        await Wait(0.5);
        _main.StartSolo(slot, false);
        _view = _main.GetChildren().OfType<GameView>().First(v => !v.IsQueuedForDeletion());
        Check(await Until(() => _view.Session.Client.State == Pluton.Networking.ClientState.InGame, 20), "reanudar desde guardado");
        Check(W.Credits >= hash, $"créditos conservados al reanudar: {hash} → {W.Credits}");
        Check(W.Stats.GetValueOrDefault("built.smelter") == 1, "fábrica conservada");
        await Wait(1.5);
        await Shot("11_reanudado");
    }

    /// <summary>Mide render y simulación local en un mundo grande, cerca de la fábrica y mirando a lo lejos.</summary>
    private async Task MeasureScenario()
    {
        var v = _view!;
        var seconds = double.Parse(_args.GetValueOrDefault("seconds", "20"));
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
        await Wait(4);
        if (_args.TryGetValue("expect-players", out var expect))
        {
            var want = int.Parse(expect);
            Check(await Until(() => W.Players.Values.Count(p => p.Connected) >= want, 60), $"{want} jugadores conectados");
            await Wait(3);
        }
        Log($"Mundo: {W.Machines.Count} máquinas, {W.Trains.Count} trenes, {W.RailSegments.Count} tramos; representadas {v.RenderedMachines} máquinas; VSync desactivada");
        foreach (var (name, walk, aim) in new[]
        {
            ("denso", new Vector3(0, 0, 40), new Vector3(10, 1, 70)),
            ("vias", new Vector3(0, 0, -20), new Vector3(0, 1, -60)),
            ("panoramica", new Vector3(-60, 0, 0), new Vector3(60, 1, 60)),
        })
        {
            await WalkTo(walk, 25);
            v.AimAt(aim);
            await Wait(1);
            _frames.Clear();
            v.MaxSyncMs = 0;
            v.MaxPollMs = 0;
            var structuralBefore = v.Session.Client.StructuralDeltas;
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            var pause = GC.GetTotalPauseDuration();
            _measuring = true;
            await Wait(seconds);
            _measuring = false;
            Summarize(_frames, out var text);
            Log($"{name} GC: gen0={GC.CollectionCount(0) - gen0} gen1={GC.CollectionCount(1) - gen1} gen2={GC.CollectionCount(2) - gen2} pausa total={(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F1} ms, última pausa={GC.GetGCMemoryInfo().PauseDurations[0].TotalMilliseconds:F1} ms; SyncWorld máx={v.MaxSyncMs:F1} ms; Poll máx={v.MaxPollMs:F1} ms; deltas estructurales={v.Session.Client.StructuralDeltas - structuralBefore}");
            var info = $"objetos={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame)} dibujos={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame)} vídeo={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576} MB memoria={WorkingSetMb()} MB";
            Log($"{name}: {text} {info}");
            Log($"{name} picos: {string.Join(" ", _spikes)}");
            _spikes.Clear();
            await Shot("measure_" + name);
        }
        var ticks = v.Session.Runner!.DrainTickTimes();
        if (ticks.Length > 0)
        {
            Array.Sort(ticks);
            var transport = v.Session.Runner.Server.Transport;
            Log($"Servidor local: tick medio {ticks.Average():F2} ms, p99 {ticks[(int)(ticks.Length * 0.99)]:F2}, máx {ticks[^1]:F2}, tardíos {v.Session.Runner.TicksLate}; {v.Session.Runner.Server.ConnectedCount} conectados; enviado {transport.BytesSent / 1048576.0:F1} MiB, recibido {transport.BytesReceived / 1048576.0:F1} MiB en {_clock:F0} s");
        }
        Check(v.Session.Client.HashMismatches == 0, $"sin divergencias ({v.Session.Client.HashChecks} comprobaciones)");
        _frames.Clear();
    }

    /// <summary>Panel de jugadores en cooperativo real: un anfitrión cambia el rol de un invitado desde la interfaz.</summary>
    private async Task PlayersPanelCheck()
    {
        var v = _view!;
        var client = v.Session.Client;
        Key(Godot.Key.P);
        await Wait(0.3);
        Check(v.PanelIsOpen, "P abre el panel de jugadores");
        await Shot("players_panel");
        Key(Godot.Key.Escape);
        await Wait(0.2);
        var guest = W.Players.Values.FirstOrDefault(p => p.Key != Me.Key && p.Connected);
        if (guest == null) return;
        client.Send(new Command { Type = CommandType.SetRole, Text = guest.Key, A = (int)Role.Visitor });
        Check(await Until(() => W.Players[guest.Key].Role == Role.Visitor, 3), "el anfitrión limita a un invitado a mirar");
        await Wait(0.5);
        client.Send(new Command { Type = CommandType.SetRole, Text = guest.Key, A = (int)Role.Builder });
        Check(await Until(() => W.Players[guest.Key].Role == Role.Builder, 3), "y le devuelve el permiso");
    }

    /// <summary>Marte con las herramientas: yacimiento, calor visible, radiador y carga real desde Plutón.</summary>
    private async Task MarsScenario()
    {
        var v = _view!;
        var client = v.Session.Client;
        var server = v.Session.Runner!.Server.World;
        var padPluto = W.Machines.Values.First(m => m.Def == "shuttle_terminal" && m.Planet == 0);

        Check(await Build("cargo_pad", -8, 12), "plataforma de carga en Plutón");
        var send = W.Machines.Values.Last(m => m.Def == "cargo_pad");
        var depot = W.Machines.Values.First(m => m.Def == "depot" && m.Planet == 0);
        await UseOn(depot, () => client.Send(new Command { Type = CommandType.Extract, A = depot.Id, Text = "cryo_module" }));
        Check(await Until(() => Me.Bag.Get("cryo_module") == 40, 3), "coger solo módulos del depósito");
        Check(Me.Bag.Get("ingot") == 0, "los demás objetos quedan en el depósito");
        await UseOn(W.Machines[send.Id], () => client.Send(new Command { Type = CommandType.Insert, A = send.Id }));
        client.Send(new Command { Type = CommandType.StationMode, A = send.Id, B = 0 });

        var c = CenterOf(padPluto);
        await WalkTo(c + new Vector3(0, -1.2f, 7));
        client.Send(new Command { Type = CommandType.Travel, A = 1 });
        Check(await Until(() => v.InFlight, 3), "despega hacia Marte");
        Check(await Until(() => !v.InFlight && Me.Planet == 1, 40), "llega a Marte");
        await Wait(1);
        await Shot("mars_01_llegada");

        var node = W.Nodes.Values.Where(n => n.Item == "silicon_ore").OrderBy(n => n.X * n.X + n.Z * n.Z).First();
        foreach (var d in W.DebrisById.Values.Where(d => d.CoversNode == node.Id).ToList()) await CutDebris(d);
        Check(await Until(() => W.Nodes[node.Id].Revealed, 3), "yacimiento de silicio descubierto");
        foreach (var d in W.DebrisById.Values.Where(d => d.Planet == 1 && d.X >= node.X - 2 && d.X <= node.X + 9 && d.Z >= node.Z - 2 && d.Z <= node.Z + 8).ToList()) await CutDebris(d);

        Check(await Build("silicon_extractor", node.X, node.Z), "extractor de silicio");
        var extractor = W.Machines.Values.Last(m => m.Def == "silicon_extractor");
        Check(await Build("wafer_refinery", node.X + 3, node.Z), "refinería");
        var refinery = W.Machines.Values.Last(m => m.Def == "wafer_refinery");
        Check(await LinkMachines(extractor, refinery), "enlace extractor → refinería");
        Check(await Until(() => W.Machines[refinery.Id].Status == MachineStatus.Overheated, 40), "la refinería se sobrecalienta sin radiadores");
        var cc = CenterOf(W.Machines[refinery.Id]);
        await WalkTo(cc + new Vector3(0, -1.2f, 10));
        v.AimAt(cc);
        await Wait(0.4);
        await Shot("mars_02_sobrecalentada");

        Check(await Build("radiator", node.X + 3, node.Z + 4), "radiador 1");
        Check(await Build("radiator", node.X + 6, node.Z + 4), "radiador 2");
        Check(await Until(() => W.Machines[refinery.Id].Status != MachineStatus.Overheated, 15), "los radiadores eliminan el sobrecalentamiento");
        await WalkTo(cc + new Vector3(0, -1.2f, 12));
        v.AimAt(cc);
        await Wait(0.4);
        await Shot("mars_03_refrigerada");

        Check(await Build("cargo_pad", -2, -14), "plataforma en Marte");
        var receive = W.Machines.Values.Last(m => m.Def == "cargo_pad" && m.Planet == 1);
        var arrived = await Until(() => W.Machines[receive.Id].In.Get("cryo_module") == 40, 110);
        Check(arrived, $"los módulos enviados desde Plutón llegan: destino {W.Machines[receive.Id].In.Get("cryo_module")}, origen {W.Machines[send.Id].In.Get("cryo_module")}, en vuelo {W.Shipments.Count}, modo origen recibe={W.Machines[send.Id].StationUnload}");
        Check(server.CountEverywhere("cryo_module") == 40, "sin duplicar ni perder carga");
        v.OpenMachinePanel(receive.Id);
        await Wait(0.4);
        await Shot("mars_04_plataforma");
        v.ClosePanelForTest();
        Check(client.HashMismatches == 0, $"sin divergencias ({client.HashChecks} comprobaciones)");
    }

    /// <summary>El evento se activa sin tocar el reloj, se juega con las herramientas y su recompensa permanece.</summary>
    private async Task HalloweenScenario()
    {
        var v = _view!;
        var client = v.Session.Client;
        var server = v.Session.Runner!.Server.World;
        client.Send(new Command { Type = CommandType.SetEvent, Text = "halloween", A = 1 });
        Check(await Until(() => W.ActiveEvents.Contains("halloween") && v.EventDecorVisible, 3), "evento activado desde el panel, sin cambiar la fecha");
        var decor = _main.Content.Events["halloween"].Decor;
        var center = new Vector3(decor.X * 2f, 0, decor.Z * 2f);
        await WalkTo(center + new Vector3(0, 0, -14), 30);
        v.AimAt(center + new Vector3(0, 1, 0));
        await Wait(0.8);
        await Shot("halloween_01_ramal");
        var shadow = W.DebrisById.Values.Where(d => d.Def == "shadow_scrap").ToList();
        Check(shadow.Count >= 8, $"chatarra sombría: {shadow.Count}");
        foreach (var d in shadow.Take(8)) await CutDebris(d);
        Check(W.Stats.GetValueOrDefault("cleared.shadow_scrap") >= 8, "ramal limpiado con el cortador");
        await Shot("halloween_02_limpio");

        // Las entregas por tren ya se prueban aparte; aquí se fija la estadística en el servidor de prueba.
        client.Send(new Command { Type = CommandType.FulfillContract, Text = "c_shadow_express" });
        await Wait(0.4);
        Check(!W.ContractsDone.Contains("c_shadow_express"), "sin entregas por tren el contrato se rechaza");
        server.Stats["train_delivered"] = 120;
        var depot = W.Machines.Values.First(m => m.Def == "depot");
        var credits = W.Credits;
        client.Send(new Command { Type = CommandType.FulfillContract, Text = "c_shadow_express" });
        Check(await Until(() => W.ContractsDone.Contains("c_shadow_express"), 3), "contrato completado");
        Check(await Until(() => W.Cosmetics.Contains("spectral_train"), 3), "recompensa cosmética obtenida");
        await Wait(0.3);
        var paid = W.Credits - credits;
        client.Send(new Command { Type = CommandType.FulfillContract, Text = "c_shadow_express" });
        await Wait(0.4);
        Check(W.Credits - credits == paid, $"el contrato paga una sola vez: {paid}");

        // Tren espectral en una ruta real
        Check(await Build("station", 2, -14), "estación A");
        var a = W.Machines.Values.Last(m => m.Def == "station");
        Check(await Build("station", 14, -14), "estación B");
        var b = W.Machines.Values.Last(m => m.Def == "station");
        Check(await PlaceRailAt(6, -12, 14, -12, 0, ""), "vía");
        client.Send(new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id });
        Check(await Until(() => W.Trains.Count == 1, 3), "tren creado");
        await WalkTo(new Vector3(18, 0, -14), 10);
        v.AimAt(new Vector3(11, 1.2f, -24));
        await Wait(1);
        await Shot("halloween_03_tren_espectral");

        client.Send(new Command { Type = CommandType.SetEvent, Text = "halloween", A = 0 });
        Check(await Until(() => !W.ActiveEvents.Contains("halloween") && !v.EventDecorVisible, 3), "evento desactivado");
        Check(W.Cosmetics.Contains("spectral_train") && W.EquippedCosmetics.Contains("spectral_train"), "la recompensa permanece al desactivarlo");
        var cash = W.Credits;
        client.Send(new Command { Type = CommandType.SetEvent, Text = "halloween", A = 1 });
        await Wait(0.5);
        Check(W.DebrisById.Values.Count(d => d.Def == "shadow_scrap") == shadow.Count - 8, "reactivarlo no regenera material");
        Check(W.Credits == cash, "reactivarlo no paga de nuevo");
    }

    private static void Summarize(List<float> frames, out string text)
    {
        var sorted = frames.OrderBy(f => f).ToList();
        var over33 = sorted.Count(f => f > 33.3f);
        var over100 = sorted.Count(f => f > 100f);
        text = sorted.Count == 0 ? "sin frames" : $"frames={sorted.Count} media={sorted.Average():F2} p99={sorted[(int)(sorted.Count * 0.99)]:F2} máx={sorted[^1]:F2} ms >33ms={over33} >100ms={over100}";
    }

    private static long WorkingSetMb() => System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576;

    /// <summary>Viajes de ida y vuelta con evidencia de continuidad, tirones y memoria.</summary>
    private async Task TravelScenario()
    {
        var v = _view!;
        var legs = int.Parse(_args.GetValueOrDefault("legs", "8"));
        var pad = W.Machines.Values.First(m => m.Def == "shuttle_terminal" && m.Planet == 0);
        var scrapWas = 0L;
        await UseOn(pad, () => { });
        v.OpenMachinePanel(pad.Id);
        await Wait(0.4);
        await Shot("travel_01_panel");
        v.ClosePanelForTest();
        var recycler = W.Machines.Values.First(m => m.Def == "recycler");
        v.Session.Runner!.Server.World.Machines[recycler.Id].In.Add("scrap", 20);
        scrapWas = v.Session.Runner.Server.World.Stats.GetValueOrDefault("produced.recovered");

        for (var leg = 0; leg < legs; leg++)
        {
            GC.Collect();
            var memBefore = WorkingSetMb();
            var from = Me.Planet;
            var to = from == 0 ? 1 : 0;
            var here = W.Machines.Values.First(m => m.Def == "shuttle_terminal" && m.Planet == from);
            var c = CenterOf(here);
            await WalkTo(c + new Vector3(0, -1.2f, 7));
            _frames.Clear();
            _measuring = true;
            v.Session.Client.Send(new Command { Type = CommandType.Travel, A = to });
            Check(await Until(() => v.InFlight, 3), $"tramo {leg}: despega {from}→{to}");
            if (leg < 2 && !_args.ContainsKey("no-shots"))
            {
                v.AimAt(c + new Vector3(0, 1000, -900));
                await Until(() => v.FlightProgress > 0.15, 10);
                v.AimAt(_view!.PlayerPosition + new Vector3(0, -2f, -6));
                await Shot($"travel_l{leg}_a_despegue");
                await Until(() => v.FlightProgress > 0.45, 15);
                await Shot($"travel_l{leg}_b_crucero");
                await Until(() => v.FlightProgress > 0.62, 15);
                await Shot($"travel_l{leg}_c_destino");
                await Until(() => v.FlightProgress > 0.85, 15);
                await Shot($"travel_l{leg}_d_descenso");
            }
            var landed = await Until(() => !v.InFlight, 40);
            _measuring = false;
            Check(landed && Me.Planet == to && v.ViewPlanet == to, $"tramo {leg}: llega al planeta {to}");
            Summarize(_frames, out var stats);
            Check(v.RenderedMachines > 0 && v.RenderedDebris > 0, $"tramo {leg}: entorno activo con {v.RenderedMachines} máquinas y {v.RenderedDebris} escombros");
            await Wait(0.5);
            Check(v.PlayerPosition.Y > -0.1f && v.PlayerPosition.Y < 4f, $"tramo {leg}: en el suelo, y={v.PlayerPosition.Y:F2}");
            Log($"tramo {leg}: {stats}; memoria {memBefore} → {WorkingSetMb()} MB, vídeo {RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576} MB");
            if (leg < 2 && !_args.ContainsKey("no-shots")) await Shot($"travel_l{leg}_e_llegada");
        }
        var produced = v.Session.Runner!.Server.World.Stats.GetValueOrDefault("produced.recovered") - scrapWas;
        Check(produced > 0, $"Plutón siguió produciendo con el jugador fuera: {produced}");
        Check(v.Session.Client.HashMismatches == 0, $"sin divergencias ({v.Session.Client.HashChecks} comprobaciones)");
        _frames.Clear();
    }

    /// <summary>Mundo preparado solo para esta prueba: evita repetir toda la progresión hasta el ferrocarril.</summary>
    private World RailWorld()
    {
        var w = World.CreateNew(_main.Content);
        w.Upgrades.Add("tech_assembly");
        w.Upgrades.Add("tech_rail");
        w.Credits = 20000;
        var depot = w.Machines.Values.First(m => m.Def == "depot");
        depot.In.Add("recovered", 200);
        depot.In.Add("ingot", 100);
        depot.In.Add("parts", 30);
        foreach (var d in w.DebrisById.Values.Where(d => d.Z is > -30 and < 2 && d.X is > -5 and < 45).ToList()) w.DebrisById.Remove(d.Id);
        w.Reindex();
        return w;
    }

    private async Task<bool> PlaceRailAt(int x1, int z1, int x2, int z2, byte shape, string shot)
    {
        var v = _view!;
        var before = W.Stats.GetValueOrDefault("built.rail");
        var a = new Vector3(x1 * 2f, 0, z1 * 2f);
        var b = new Vector3(x2 * 2f, 0, z2 * 2f);
        var dir = (b - a).Normalized();
        await WalkTo(a - dir * 6 + new Vector3(dir.Z, 0, -dir.X) * 5);
        v.SetTool(Tool.Rail);
        v.SetRailShape(shape);
        v.AimAt(a);
        await Wait(0.2);
        v.Primary();
        await WalkTo((a + b) / 2 - dir * 6 + new Vector3(dir.Z, 0, -dir.X) * 7);
        v.AimAt(b);
        await Wait(0.25);
        if (shot.Length > 0) await Shot(shot);
        v.Primary();
        var ok = await Until(() => W.Stats.GetValueOrDefault("built.rail") > before, 3);
        v.SetTool(Tool.Cutter);
        return ok;
    }

    private async Task RailScenario()
    {
        var v = _view!;
        Check(await Build("station", 2, -14), "estación A colocada con la herramienta");
        var a = W.Machines.Values.Last(m => m.Def == "station");
        Check(await Build("station", 28, -14), "estación B colocada con la herramienta");
        var b = W.Machines.Values.Last(m => m.Def == "station");
        await Shot("rail_01_estaciones");
        Check(await PlaceRailAt(6, -12, 16, -12, 0, "rail_02_previa"), "tramo recto colocado");
        Check(await PlaceRailAt(16, -12, 22, -18, 1, "rail_03_curva"), "tramo curvo colocado");
        Check(await PlaceRailAt(22, -18, 28, -12, 2, ""), "segundo tramo curvo colocado");
        await Wait(0.5);
        await Shot("rail_04_trazado");

        var client = v.Session.Client;
        client.Send(new Command { Type = CommandType.StationMode, A = b.Id, B = 1 });
        client.Send(new Command { Type = CommandType.CreateTrain, A = a.Id, B = b.Id });
        Check(await Until(() => W.Trains.Count == 1, 3), "tren creado desde el depósito");
        var trainId = W.Trains.Keys.First();
        await Wait(0.5);
        Check(W.Trains[trainId].WaitReason == "wait_no_cargo", "el tren explica que espera carga");
        // Carga real: el jugador deposita en la estación de origen desde su mochila.
        var depot = W.Machines.Values.First(m => m.Def == "depot");
        await UseOn(depot, () => client.Send(new Command { Type = CommandType.Extract, A = depot.Id }));
        await Wait(0.5);
        await UseOn(a, () => client.Send(new Command { Type = CommandType.Insert, A = a.Id }));
        await Wait(0.5);
        var loaded = W.Machines[a.Id].In.Total;
        Check(loaded > 0, $"carga depositada en origen: {loaded}");

        v.OpenMachinePanel(a.Id);
        await Wait(0.5);
        await Shot("rail_05_panel_estacion");
        v.ClosePanelForTest();
        await Wait(1.5);
        await Shot("rail_06_cargando");
        Check(await Until(() => W.Trains[trainId].State == TrainState.Moving, 25), "el tren sale de la estación");
        await Wait(4);
        if (W.Trains[trainId].Path.Count > 1)
        {
            var mid = new Vector3(19 * 2f, 1.5f, -14 * 2f);
            await WalkTo(mid + new Vector3(-10, -1.5f, 10), 6);
            v.AimAt(mid);
        }
        await Wait(0.3);
        await Shot("rail_07_en_ruta");
        var delivered = await Until(() => W.Stats.GetValueOrDefault("train_delivered") > 0, 40);
        Check(delivered, $"entrega por tren: {W.Stats.GetValueOrDefault("train_delivered")}");
        await Shot("rail_08_descarga");
        _measuring = true;
        var ok = await Until(() => W.Machines[b.Id].In.Total >= Math.Min(loaded, W.Content.Balance.Rail.TrainCapacity), 60);
        _measuring = false;
        Check(ok, $"carga recibida en destino: {W.Machines[b.Id].In.Total}");
        Check(v.Session.Client.HashMismatches == 0, $"sin divergencias ({v.Session.Client.HashChecks} comprobaciones)");

        _view.Session.Client.RequestSave();
        await Wait(1);
        var slot = v.Session.Slot;
        var trains = W.Trains.Count;
        _main.EndSession("");
        await Wait(0.5);
        _main.StartSolo(slot, false);
        _view = _main.GetChildren().OfType<GameView>().First(x => !x.IsQueuedForDeletion());
        Check(await Until(() => _view.Session.Client.State == Pluton.Networking.ClientState.InGame, 20), "reanudar con vías y tren");
        Check(W.Trains.Count == trains && W.RailSegments.Count >= 5, $"vías {W.RailSegments.Count} y trenes {W.Trains.Count} conservados");
        await Wait(1.5);
        await Shot("rail_09_reanudado");
    }

    /// <summary>Almacén junto al inicio y otro a unos 150 m, con escombros y máquinas en medio.</summary>
    private World BeltStage()
    {
        var world = BeltWorld();
        var near = new Machine { Id = world.NextId++, Def = "storage_crate", Planet = 0, X = 6, Z = 8 };
        near.In.Add("ore", 150);
        near.In.Add("ingot", 150);
        world.Machines[near.Id] = near;
        var far = new Machine { Id = world.NextId++, Def = "storage_crate", Planet = 0, X = -34, Z = -24 };
        world.Machines[far.Id] = far;
        foreach (var d in world.DebrisById.Values.Where(d => d.CoversNode < 0 && (Math.Abs(d.X - near.X) < 5 && Math.Abs(d.Z - near.Z) < 5 || Math.Abs(d.X - far.X) < 5 && Math.Abs(d.Z - far.Z) < 5)).ToList()) world.DebrisById.Remove(d.Id);
        world.Reindex();
        return world;
    }

    /// <summary>Base limpia y con material: el recorrido se monta después con comandos y herramientas normales.</summary>
    private World BeltWorld()
    {
        var world = World.CreateNew(_main.Content);
        foreach (var d in world.DebrisById.Values.Where(d => d.Planet == 0 && d.CoversNode < 0 && d.X > -12 && d.X < 34 && d.Z > -18 && d.Z < 12).ToList()) world.DebrisById.Remove(d.Id);
        foreach (var d in world.DebrisById.Values.Where(d => d.CoversNode >= 0 && world.Nodes[d.CoversNode].X == 18 && world.Nodes[d.CoversNode].Z == -6).ToList())
        {
            world.DebrisById.Remove(d.Id);
            world.Nodes[d.CoversNode].Revealed = true;
        }
        world.Credits = 5000;
        var depot = world.Machines.Values.First(m => m.Def == "depot" && m.Planet == 0);
        depot.In.Add("recovered", 600); // más que el almacén y la cinta juntos: el atasco debe llenar la cinta entera
        depot.In.Add("ingot", 100);
        world.Upgrades.Add("tech_assembly");
        world.Reindex();
        return world;
    }

    /// <summary>Regresión visual del recorrido exigible: extractor → cinta → fundición → cinta → almacén, cruce, atasco e inspección.</summary>
    private async Task BeltScenario()
    {
        var v = _view!;
        var client = v.Session.Client;
        Check(await Build("ore_extractor", 18, -6), "extractor sobre el yacimiento");
        Check(await Build("smelter", 18, -11), "fundición al frente del extractor");
        var ex = W.Machines.Values.Last(m => m.Def == "ore_extractor");
        var sm = W.Machines.Values.Last(m => m.Def == "smelter");

        // Previsualización con la herramienta: origen elegido y destino apuntado.
        v.SetTool(Tool.Link);
        await UseOn(ex, v.Primary);
        var c = CenterOf(sm);
        await WalkTo(new Vector3(c.X + 7, 0, c.Z + 5));
        v.AimAt(c);
        await Wait(0.5);
        await Shot("belt_01_previsualizacion");
        v.Primary();
        Check(await Until(() => W.Stats.GetValueOrDefault("linked") >= 1, 3), "cinta extractor → fundición");
        v.SetTool(Tool.Cutter);

        Check(await Build("storage_crate", 26, -16), "almacén de lingotes");
        var crate = W.Machines.Values.Last(m => m.Def == "storage_crate");
        Check(await LinkMachines(sm, crate), "cinta fundición → almacén");
        var toCrate = W.Links.Values.Last();

        var along = new Vector3(c.X + 9, 0, c.Z + 7);
        await WalkTo(along);
        v.AimAt(new Vector3(c.X + 2, 0.5f, c.Z + 2));
        Check(await Until(() => W.Links.Values.Sum(l => l.Load) > 0, 15), "las cintas llevan carga real");
        Check(await Until(() => crate.In.Get("ingot") + W.Machines[crate.Id].In.Get("ingot") > 0, 60), "lingotes en el almacén");
        await Shot("belt_02_flujo");

        // Inspección apuntando a la cinta.
        var cell = toCrate.Cells[toCrate.Cells.Count / 2];
        var mid = new Vector3((cell.x + 0.5f) * 2, 0.5f, (cell.z + 0.5f) * 2);
        await WalkTo(mid + new Vector3(4, -0.5f, 4));
        v.AimAt(mid);
        await Wait(0.4);
        Check(v.AimTarget == ("belt", toCrate.Id), $"se apunta a la cinta: {v.AimTarget}");
        await Shot("belt_03_inspeccion");

        // Cruce perpendicular.
        var server = v.Session.Runner!.Server.World;
        client.Send(new Command { Type = CommandType.Place, Text = "storage_crate", A = cell.x, B = cell.z + 5 });
        client.Send(new Command { Type = CommandType.Place, Text = "storage_crate", A = cell.x, B = cell.z - 5 });
        await Wait(0.5);
        var south = W.Machines.Values.Last(m => m.Def == "storage_crate" && m.Z == cell.z - 5);
        var north = W.Machines.Values.Last(m => m.Def == "storage_crate" && m.Z == cell.z + 5);
        client.Send(World.LinkCommand(north.Id, south.Id, W.RouteBelt(north.Id, south.Id).Cells));
        Check(await Until(() => W.Links.Values.Any(l => l.From == north.Id), 3), "cinta que cruza");
        var crossing = W.Links.Values.Last(l => l.From == north.Id);
        Check(crossing.Cells.Intersect(toCrate.Cells).Count() == 1, "cruza por un puente en una sola celda");
        await WalkTo(mid + new Vector3(6, -0.5f, 3));
        v.AimAt(mid);
        await Wait(0.6);
        await Shot("belt_04_cruce");
        _ = server;

        // Carga densa y atasco reales: depósito → cinta larga → almacén hasta llenarlo (200 por tipo).
        Check(await Build("storage_crate", 8, -16), "almacén que se llenará");
        var full = W.Machines.Values.Last(m => m.Def == "storage_crate" && m.X == 8);
        var depot = W.Machines.Values.First(m => m.Def == "depot" && m.Planet == 0);
        client.Send(World.LinkCommand(depot.Id, full.Id, W.RouteBelt(depot.Id, full.Id).Cells));
        Check(await Until(() => W.Links.Values.Any(l => l.From == depot.Id && l.To == full.Id), 3), "cinta depósito → almacén");
        var line = W.Links.Values.Last(l => l.From == depot.Id);
        var lineMid = line.Cells[line.Cells.Count / 2];
        var lineAim = new Vector3((lineMid.x + 0.5f) * 2, 0.5f, (lineMid.z + 0.5f) * 2);
        await WalkTo(lineAim + new Vector3(5, -0.5f, 3));
        v.AimAt(lineAim);
        Check(await Until(() => W.Links[line.Id].Load >= W.Links[line.Id].Slots.Length - 1, 60), "cinta casi saturada");
        await Wait(1.2);
        await Shot("belt_05_carga_densa");
        Check(await Until(() => W.BeltStatusOf(W.Links[line.Id]) == BeltStatus.BlockedFull, 110), $"la cinta se atasca con el destino lleno: {W.Machines[full.Id].In.Get("recovered")}");
        await Wait(1.5);
        v.AimAt(lineAim);
        await Wait(0.3);
        await Shot("belt_06_atasco");
        var ids = W.Links[line.Id].Load;
        Check(ids == W.Links[line.Id].Slots.Length, $"cinta llena: {ids}");
        var totalBefore = W.CountEverywhere("recovered");
        client.Send(new Command { Type = CommandType.Unlink, A = line.Id });
        Check(await Until(() => !W.Links.ContainsKey(line.Id), 3), "retirar devuelve la carga");
        await Wait(1.2);
        Check(W.CountEverywhere("recovered") == totalBefore, $"sin pérdida al retirar: {totalBefore} → {W.CountEverywhere("recovered")}");

        Check(client.HashMismatches == 0, $"sin divergencias ({client.HashChecks} comprobaciones)");
        Log($"Cintas: {W.Links.Count}, objetos en cinta {W.Links.Values.Sum(l => l.Load)}, dibujados {v.Belts.DrawnItems}");
    }

    /// <summary>
    /// Cinta larga con las teclas del juego (eventos de Godot, no de Windows): punto fijado, cancelar, otra ruta, retirar punto,
    /// construir y comprobar que lo construido es lo previsualizado, que los objetos llegan y que se conserva al recargar.
    /// </summary>
    private async Task BeltLongScenario()
    {
        var v = _view!;
        var near = W.Machines.Values.First(m => m.Def == "storage_crate" && m.X == 6 && m.Z == 8);
        var far = W.Machines.Values.First(m => m.Def == "storage_crate" && m.X == -34);
        var nearId = near.Id;
        var farId = far.Id;
        var credits = W.Credits;
        var plan = () => v.Belts.Plan!;

        Key(Godot.Key.L);
        await Wait(0.2);
        Check(v.CurrentTool == Tool.Link, "L activa la herramienta de cinta");
        await UseOn(near, () => { });
        v.AimAt(CenterOf(near));
        await Wait(0.3);
        await Shot("long_00_conector_origen");
        v.Primary();
        await Wait(0.2);
        Check(plan().HasOrigin && plan().FromPort != null, $"origen con conector elegido: {plan().FromPort}");

        // Cancelar sale sin coste y sin cambiar de herramienta.
        var ground = v.PlayerPosition + new Vector3(-10, -v.PlayerPosition.Y, -6);
        v.AimAt(ground);
        await Wait(0.4);
        v.Primary();
        await Wait(0.3);
        Check(plan().Waypoints.Count == 1, $"clic en el suelo fija un punto: {plan().Waypoints.Count}");
        Key(Godot.Key.Z);
        await Wait(0.3);
        Check(!plan().HasOrigin && plan().Waypoints.Count == 0, "Z cancela el trazado");
        Check(W.Credits == credits && W.Links.Count == 0, "cancelar no cuesta nada");
        Check(v.CurrentTool == Tool.Link, "tras cancelar sigue la herramienta");

        // De nuevo: origen, punto, destino lejano.
        v.AimAt(CenterOf(near));
        await Wait(0.3);
        v.Primary();
        await Wait(0.2);
        ground = v.PlayerPosition + new Vector3(-16, -v.PlayerPosition.Y, 4);
        v.AimAt(ground);
        await Wait(0.4);
        v.Primary();
        await Wait(0.3);
        var waypoint = plan().Waypoints[0];
        await Shot("long_01_punto_fijado");

        var cf = CenterOf(far);
        await WalkTo(new Vector3(cf.X + 9, 0, cf.Z + 11), 35);
        v.AimAt(cf);
        Check(await Until(() => plan().State == BeltPlanState.Ready && plan().To == farId, 8), $"destino y ruta listos: {plan().State} {plan().Error}");
        var first = plan().Route!.ToList();
        Check(first.Count > 32, $"ruta larga, sin límite: {first.Count} celdas");
        Check(first.Contains(waypoint), "la ruta respeta el punto fijado");
        await Wait(0.3);
        await Shot("long_02_previsualizacion");

        Key(Godot.Key.R);
        await Wait(1);
        var alternative = plan().Route!.ToList();
        Check(!alternative.SequenceEqual(first) && alternative.Contains(waypoint), $"R ofrece otra ruta válida ({plan().AlternativeIndex + 1}/{plan().AlternativeCount})");
        await Shot("long_03_otra_ruta");

        Key(Godot.Key.Backspace);
        await Wait(0.2);
        Check(plan().Waypoints.Count == 0, "Retroceso retira el último punto");
        Check(await Until(() => plan().State == BeltPlanState.Ready, 8), "recalcula al retirar el punto");
        var preview = plan().Route!.ToList();
        Check(!preview.Contains(waypoint) || !first.SequenceEqual(preview), "la ruta ya no depende del punto");
        var toPort = plan().ToPort;
        await Wait(0.5);
        await Shot("long_04_antes_de_construir");

        // Construir: exactamente lo mostrado, sin mover nada del encuadre.
        var before = W.Credits;
        v.Primary();
        Check(await Until(() => W.Links.Count == 1, 3), "la cinta se construye");
        var built = W.Links.Values.Single();
        Check(built.Cells.SequenceEqual(preview), "lo construido coincide celda a celda con lo previsualizado");
        Check(built.Cells[^1] == toPort, "el conector de destino no cambió al hacer clic");
        await Wait(0.3);
        Check(W.Credits <= before - preview.Count, $"coste cobrado: {before} → {W.Credits}");
        Check(v.CurrentTool == Tool.Link && !plan().HasOrigin, "la herramienta sigue activa para otra cinta");
        await Shot("long_05_despues_de_construir");

        // Objetos a lo largo de toda la cinta.
        var transit = built.Slots.Length * 0.3;
        Check(await Until(() => W.Machines[farId].In.Total > 0, transit + 15), $"los objetos recorren las {built.Cells.Count} celdas en unos {transit:0} s");
        await Wait(1.5);
        await Shot("long_06_llegan_objetos");
        Check(W.Links.Values.Single().Load > 20, $"carga a lo largo de la cinta: {W.Links.Values.Single().Load}");

        // Desnivel simulado: solo la vista, mediante el único punto de enlace con el terreno.
        BeltView.GroundHeight = (x, z) => 2.2f * Mathf.Sin(x * 0.12f) * Mathf.Cos(z * 0.09f) + 2.2f;
        v.Belts.Clear();
        await Wait(1.5);
        await Shot("long_07_desnivel_simulado");
        BeltView.GroundHeight = (_, _) => 0f;
        v.Belts.Clear();
        await Wait(1);

        // Guardar, cargar y conservar el trazado.
        var slot = "autotest-long";
        var keep = W.Links.Values.Single().Cells.ToList();
        _main.EndSession("");
        await Wait(0.5);
        _main.StartSolo(slot, false);
        _view = _main.GetChildren().OfType<GameView>().First(x => !x.IsQueuedForDeletion());
        Check(await Until(() => _view.Session.Client.State == Pluton.Networking.ClientState.InGame, 20), "reanudar la partida");
        await Wait(1);
        Check(W.Links.Count == 1 && W.Links.Values.Single().Cells.SequenceEqual(keep), "el trazado se conserva al guardar y cargar");
        Check(W.Links.Values.Single().Load > 0, "la carga se conserva");
        Check(_view.Session.Client.HashMismatches == 0, "sin divergencias");
    }

    /// <summary>
    /// Regresión de construcción con entrada inyectada (no equivale a jugar con ratón y teclado reales): catálogo, fantasma,
    /// rotación, colocar seguidas, faltas de material, cancelar y desmontar con devolución medida.
    /// </summary>
    private async Task BuildScenario()
    {
        var v = _view!;
        var client = v.Session.Client;
        v.TestCapture = true;
        v.UpdateMouseMode();
        var smelter = W.Content.Machines["smelter"];

        for (var rot = 0; rot < 4; rot++)
        {
            var (dx, dz) = World.BeltDirs[W.BeltPorts(0, "smelter", 0, -30, rot, true).First().Dir];
            Check(GameView.ModelFront(smelter, rot).DistanceTo(new Vector3(dx, 0, dz)) < 0.01f, $"la boca de salida del modelo coincide con el puerto, giro {rot}");
        }

        // Catálogo.
        Key(Godot.Key.Q);
        await Wait(0.3);
        v.Inventory.FocusCatalogEntry("build:smelter");
        await Wait(0.3);
        await Shot("build_01_catalogo");
        v.Inventory.SetCatalogFilter(MachineCategory.Extraction);
        await Wait(0.2);
        Check(v.Inventory.CatalogCardCount == W.Content.Machines.Values.Count(d => d.Node != null), $"el filtro de extracción deja solo extractores: {v.Inventory.CatalogCardCount}");
        v.Inventory.FocusCatalogEntry("build:ice_extractor");
        await Wait(0.3);
        await Shot("build_02_catalogo_bloqueada");
        v.Inventory.Choose("build:ice_extractor");
        Check(v.InventoryOpen && v.CurrentTool != Tool.Build, "elegir una construcción bloqueada no la equipa ni cierra el catálogo");
        v.Inventory.SetCatalogFilter(null);
        v.Inventory.Choose("build:smelter");
        Check(!v.InventoryOpen && v.ActiveEntry == "build:smelter", "elegir en el catálogo equipa la pieza");
        await Wait(0.2);

        // Fantasma válido, rotación y colocar seguidas.
        await WalkTo(new Vector3(14, 0, -6));
        Vector3 At(int x, int z) => new((x + smelter.W / 2f) * 2, 0, (z + smelter.D / 2f) * 2);
        v.AimAt(At(4, -12));
        await Wait(0.3);
        Check(!v.StatusText.Contains('\n'), $"lugar válido sin avisos: {v.StatusText}");
        Check(v.StatusText.Contains(Ui.Cost(smelter.Credits, smelter.Cost)), "el fantasma muestra el coste");
        await Shot("build_03_fantasma_valido");
        Key(Godot.Key.R);
        await Wait(0.3);
        Check(v.BuildRotation == 1, "R gira la pieza un paso");
        await Shot("build_04_fantasma_girado");
        Check(W.BeltPorts(0, "smelter", 4, -12, 1, true).All(p => p.Dir == World.FrontDir(1)), "los puertos del fantasma giran con la pieza");
        for (var i = 0; i < 3; i++) Key(Godot.Key.R);
        await Wait(0.2);
        Check(v.BuildRotation == 0, "cuatro pasos vuelven al giro inicial");
        // De cerca y de costado: se comparan las flechas con la boca del modelo. La salida con giro 0 va hacia −Z.
        await WalkTo(At(4, -12) + new Vector3(9, 0, 0));
        v.AimAt(At(4, -12));
        await Wait(0.4);
        await Shot("build_04b_puertos_de_costado");
        await WalkTo(new Vector3(14, 0, -6));

        var built = new List<Machine>();
        var machinesBefore = W.Stats.GetValueOrDefault("built.smelter");
        foreach (var x in new[] { 4, 6, 8 })
        {
            v.AimAt(At(x, -12));
            await Wait(0.3);
            v.Primary();
            var count = machinesBefore + built.Count + 1;
            Check(await Until(() => W.Stats.GetValueOrDefault("built.smelter") >= count, 3), $"colocada la fundición en x={x}");
            Check(v.ActiveEntry == "build:smelter", "la pieza sigue activa tras construir");
            built.Add(W.Machines.Values.Last(m => m.Def == "smelter"));
        }

        // Inválido con motivo, sin salir del modo.
        v.AimAt(At(4, -12));
        await Wait(0.4);
        Check(v.StatusText.Contains(Loc.T("err_blocked")), $"motivo a la vista: {v.StatusText}");
        await Shot("build_05_fantasma_invalido");
        v.Primary();
        await Wait(0.4);
        Check(W.Stats.GetValueOrDefault("built.smelter") == machinesBefore + 3 && v.ActiveEntry == "build:smelter", "colocar en lugar ocupado no construye ni sale del modo");

        // Faltan materiales, también desde la barra rápida.
        v.ActivateEntry("build:assembler");
        var assemblerBuilt = W.Stats.GetValueOrDefault("built.assembler");
        v.AimAt(At(12, -12));
        await Wait(0.4);
        Check(v.StatusText.Contains(Loc.T("item_ingot")) && v.StatusText.Contains(Loc.T("cost_lacking", "")), $"el fantasma indica qué falta: {v.StatusText}");
        await Shot("build_06_faltan_materiales");
        v.Primary();
        await Wait(0.4);
        Check(W.Stats.GetValueOrDefault("built.assembler") == assemblerBuilt && v.ActiveEntry == "build:assembler", "sin material no construye y sigue en el modo");

        // Cancelar.
        var credits = W.Credits;
        Key(Godot.Key.Z);
        await Wait(0.2);
        Check(v.CurrentTool == Tool.Cutter && v.ActiveEntry == HotbarLayout.Cutter && W.Credits == credits, "Z cancela sin efectos");
        v.SelectBuild("smelter");
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false });
        await Wait(0.2);
        Check(v.CurrentTool == Tool.Cutter && W.Stats.GetValueOrDefault("built.smelter") == machinesBefore + 3, "el clic derecho cancela sin efectos");
        v.ActivateEntry(HotbarLayout.Cutter);
        Check(v.ActiveEntry == "", "repetir la casilla guarda la multiherramienta");
        v.SelectBuild("smelter");
        Key(Godot.Key.Z);
        await Wait(0.2);
        Check(v.ActiveEntry == "", "cancelar devuelve las manos como estaban");
        v.ActivateEntry(HotbarLayout.Cutter);

        // Desmontar: sin apuntar a nada no hay objetivo.
        var server = v.Session.Runner!.Server.World;
        v.SetTool(Tool.Remove);
        v.AimAt(At(12, -12));
        await Wait(0.3);
        var machineCount = W.Machines.Count;
        Check(v.StatusText.Length == 0, $"sin objeto apuntado no se ofrece nada: {v.StatusText}");
        v.Primary();
        await Wait(0.4);
        Check(W.Machines.Count == machineCount, "apuntar cerca, sin dar en la máquina, no desmonta nada");

        // La máquina central de tres pegadas, con contenido.
        var target = built[1];
        server.Machines[target.Id].Out.Add("ingot", 4);
        server.DirtyMachines.Add(target.Id);
        await Until(() => W.Machines[target.Id].Out.Get("ingot") == 4, 2);
        v.AimAt(CenterOf(target) + Vector3.Up);
        await Wait(0.4);
        Check(v.AimTarget == ("machine", target.Id), $"apunta solo a la central: {v.AimTarget}");
        Check(v.StatusText.Contains(Loc.T("remove_target", Loc.T("machine_smelter"))) && v.StatusText.Contains(Loc.T("machine_depot")), $"objetivo, recuperación y destino: {v.StatusText}");
        await Shot("build_07_desmontar");
        var preview = W.PreviewRemoveMachine(Me, target.Id);
        var stock = W.DepotOf(0)!.In.Items.ToDictionary(kv => kv.Key, kv => kv.Value);
        credits = W.Credits;
        v.Primary();
        Check(await Until(() => !W.Machines.ContainsKey(target.Id), 3), "se desmonta la apuntada");
        await Wait(0.8);
        Check(W.Machines.ContainsKey(built[0].Id) && W.Machines.ContainsKey(built[2].Id), "las vecinas siguen en pie");
        Check(W.Credits - credits == preview.Credits, $"créditos devueltos como se mostró: {W.Credits - credits} / {preview.Credits}");
        Check(preview.Items.All(kv => W.DepotOf(0)!.In.Get(kv.Key) - stock.GetValueOrDefault(kv.Key) == kv.Value), $"materiales devueltos como se mostró: {Ui.Cost(0, preview.Items)}");

        // Lo instalado en la base lo explica y no se toca.
        var baseDepot = W.DepotOf(0)!;
        v.AimAt(CenterOf(baseDepot) + Vector3.Up);
        await Wait(0.4);
        Check(v.AimTarget == ("machine", baseDepot.Id) && v.StatusText.Contains(Loc.T("err_builtin")), $"el depósito explica por qué no: {v.StatusText}");
        await Shot("build_08_desmontar_base");
        v.Primary();
        await Wait(0.4);
        Check(W.Machines.ContainsKey(baseDepot.Id), "lo instalado no se desmonta");

        // Una cinta: se retira ella y no sus extremos.
        client.Send(new Command { Type = CommandType.Link, A = baseDepot.Id, B = built[2].Id });
        Check(await Until(() => W.Links.Values.Any(l => l.From == baseDepot.Id && l.To == built[2].Id), 3), "cinta tendida para retirarla");
        var belt = W.Links.Values.Last(l => l.From == baseDepot.Id);
        var cell = belt.Cells[belt.Cells.Count / 2];
        v.AimAt(new Vector3((cell.x + 0.5f) * 2, 0.5f, (cell.z + 0.5f) * 2));
        await Wait(0.4);
        Check(v.AimTarget == ("belt", belt.Id) && v.StatusText.Contains(Loc.T("belt_title")), $"se apunta a la cinta: {v.AimTarget}");
        await Shot("build_09_desmontar_cinta");
        v.Primary();
        Check(await Until(() => !W.Links.ContainsKey(belt.Id), 3), "se retira la cinta");
        Check(W.Machines.ContainsKey(built[2].Id), "su destino sigue en pie");

        v.SetTool(Tool.Cutter);
        v.TestCapture = false;
        v.UpdateMouseMode();
        Check(client.HashMismatches == 0, $"sin divergencias ({client.HashChecks} comprobaciones)");
    }

    /// <summary>Partida 0.4.1 migrada: comprueba las cintas enrutadas, la elevada y que siguen transportando.</summary>
    private async Task BeltMigrationScenario()
    {
        var v = _view!;
        Check(System.IO.File.Exists(_main.Store.PathOf("autotest-v4") + ".v4.bak"), "copia .v4.bak creada");
        Check(W.Links.Count == 6, $"seis conexiones conservadas: {W.Links.Count}");
        var elevated = W.Links.Values.FirstOrDefault(l => l.Elevated);
        Check(elevated != null, "enlace sin ruta posible queda elevado");
        var smelter = W.Machines.Values.First(m => m.Def == "smelter");
        var c = CenterOf(smelter);
        await WalkTo(new Vector3(c.X - 3, 0, c.Z + 13));
        v.AimAt(c);
        Check(await Until(() => W.Links.Values.Sum(l => l.Load) > 0, 20), "las cintas migradas transportan carga");
        await Wait(1);
        await Shot("beltv4_01_fabrica");
        if (elevated != null)
        {
            var a = CenterOf(W.Machines[elevated.From]);
            var b = CenterOf(W.Machines[elevated.To]);
            // Desde lejos y de lado: el rayo llega a la cinta por encima de las máquinas que une.
            var lateral = new Vector3(a.Z - b.Z, 0, b.X - a.X).Normalized(); // en este guardado, el lado sin máquinas
            var ground = (a + b) / 2;
            ground.Y = 0;
            var stand = ground + lateral * 24f;
            await WalkTo(stand, 15);
            var mid = v.Belts.CenterOfBelt(elevated.Id);
            v.AimAt(mid);
            await Wait(0.5);
            Check(v.AimTarget == ("belt", elevated.Id), $"la elevada se puede apuntar: {v.AimTarget}");
            await Shot("beltv4_02_elevada");
        }
        Check(v.Session.Client.HashMismatches == 0, $"sin divergencias ({v.Session.Client.HashChecks} comprobaciones)");
    }

    private async Task<bool> LinkMachines(Machine a, Machine b)
    {
        _view!.SetTool(Tool.Link);
        var before = W.Stats.GetValueOrDefault("linked");
        await UseOn(a, _view.Primary);
        await UseOn(b, _view.Primary);
        var ok = await Until(() => W.Stats.GetValueOrDefault("linked") > before, 3);
        _view.SetTool(Tool.Cutter);
        return ok;
    }

    private void Finish()
    {
        if (_frames.Count > 0)
        {
            _frames.Sort();
            var avg = _frames.Average();
            Log($"Frames={_frames.Count} media={avg:F2} ms ({1000 / avg:F0} FPS) p95={_frames[(int)(_frames.Count * 0.95)]:F2} p99={_frames[(int)(_frames.Count * 0.99)]:F2} max={_frames[^1]:F2} ms memoria={System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / 1048576.0:F0} MB vídeo={RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0:F0} MB");
        }
        Log(_failures == 0 ? "RESULTADO: CORRECTO" : $"RESULTADO: {_failures} FALLOS");
        _main.EndSession("");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }
}
