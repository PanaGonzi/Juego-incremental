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
        if (_measuring && _lastUsec != 0) _frames.Add((now - _lastUsec) / 1000f);
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
        var approach = _view!.PlayerPosition.DirectionTo(pos) * -4f + pos;
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
            if (_args.TryGetValue("host", out var hostPort))
            {
                _main.StartHost("autotest-host", true, int.Parse(hostPort));
            }
            else if (_args.TryGetValue("join", out var join))
            {
                var parts = join.Split(':');
                _main.StartJoin(parts[0], int.Parse(parts[1]), "");
            }
            else
            {
                _main.StartSolo("autotest-" + DateTime.Now.ToString("HHmmss"), true);
            }
            _view = _main.GetChildren().OfType<GameView>().First();
            Check(await Until(() => _view.Session.Client.State == Pluton.Networking.ClientState.InGame, 20), "entrada en el mundo");
            Log($"Renderizador {RenderingServer.GetCurrentRenderingMethod()} GPU {RenderingServer.GetVideoAdapterName()} ventana {DisplayServer.WindowGetSize()}");

            await Wait(1.5);
            await Shot("01_inicio");

            if (_args.ContainsKey("join") || _args.ContainsKey("host")) await JoinedScenario();
            else await FullLoop();
        }
        catch (Exception e)
        {
            Check(false, "excepción: " + e);
        }
        Finish();
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
        Key(Godot.Key.Q);
        await Wait(0.1);
        Check(v.CurrentTool == Tool.Cutter, "Q cancela y vuelve al cortador");

        Key(Godot.Key.Tab);
        await Wait(0.2);
        Check(v.PanelIsOpen && Input.MouseMode == Input.MouseModeEnum.Visible, "Tab abre el panel y libera el ratón");
        Key(Godot.Key.Escape);
        await Wait(0.2);
        Check(!v.PanelIsOpen && !v.IsPaused, "Esc cierra el panel sin pausar");

        Key(Godot.Key.B);
        await Wait(0.2);
        Check(v.PanelIsOpen, "B abre el menú de construcción");
        await Shot("00_menu_construir");
        Key(Godot.Key.Escape);
        await Wait(0.2);

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
        var approach = v.PlayerPosition.DirectionTo(pos) * -4f + pos;
        await WalkTo(new Vector3(approach.X, 0, approach.Z));
        v.AimAt(pos);
        await Wait(0.2);
        var hp = d.Hp;
        var id = d.Id;
        Input.ActionPress("primary");
        var cut = await Until(() => !W.DebrisById.TryGetValue(id, out var left) || left.Hp < hp, 4);
        Input.ActionRelease("primary");
        Check(cut, "mantener el clic izquierdo corta");

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
