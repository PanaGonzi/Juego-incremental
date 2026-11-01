using Godot;
using Pluton.Networking;
using Pluton.Persistence;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>Raíz de la aplicación: ajustes, menú y ciclo de sesión. Solo existe una sesión a la vez.</summary>
public partial class Main : Node
{
    public const int DefaultPort = 27777;

    public ContentDb Content = null!;
    public SaveStore Store = null!;
    public Settings Prefs = null!;
    private MainMenu? _menu;
    private GameView? _view;
    private GameSession? _session;
    private string _pendingMessage = "";
    public AutoTest? Auto;

    public override void _Ready()
    {
        var args = CmdArgs.Parse();
        Content = ContentDb.Load(ContentDb.LocateDirectory(ProjectDir()));
        var profile = args.GetValueOrDefault("profile", OS.HasFeature("editor") ? "dev" : "user");
        Store = new SaveStore(profile);
        Prefs = Settings.Load(profile);
        if (args.TryGetValue("language", out var language)) Prefs.Language = language;
        Controls.Setup(Prefs);
        Loc.Load(Prefs.Language);
        Prefs.ApplyDisplay();
        Sfx.Setup(this, Prefs);

        if (args.ContainsKey("autotest"))
        {
            Auto = new AutoTest(this, args);
            AddChild(Auto);
            return;
        }
        ShowMenu();
    }

    private static string ProjectDir() =>
        OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://") : Path.GetDirectoryName(OS.GetExecutablePath())!;

    public void ShowMenu()
    {
        _menu = new MainMenu(this, _pendingMessage);
        _pendingMessage = "";
        AddChild(_menu);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    private void HideMenu()
    {
        _menu?.QueueFree();
        _menu = null;
    }

    public void StartSession(GameSession session)
    {
        HideMenu();
        _session = session;
        _view = new GameView(this, session);
        AddChild(_view);
    }

    public void StartSolo(string slot, bool isNew)
    {
        var world = isNew ? World.CreateNew(Content) : Store.Load(slot, Content);
        StartSession(GameSession.Local(this, world, slot, hostPort: null, Prefs, PrepareWorld(world, isNew)));
    }

    public void StartHost(string slot, bool isNew, int port)
    {
        isNew = isNew || !File.Exists(Store.PathOf(slot));
        var world = isNew ? World.CreateNew(Content) : Store.Load(slot, Content);
        StartSession(GameSession.Local(this, world, slot, port, Prefs, PrepareWorld(world, isNew)));
    }

    /// <summary>Progreso de ausencia y eventos de temporada. Devuelve el resumen que se avisa al jugador.</summary>
    private string PrepareWorld(World world, bool isNew)
    {
        foreach (var e in Content.Events.Values)
            if (e.InSeason(DateTime.Now) && !world.EventsSeeded.Contains(e.Id)) world.SetEvent(e.Id, true);
        if (isNew) return "";
        var report = OfflineProgress.Apply(world, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (report == null || report.SecondsApplied < 60) return "";
        return $"{report.SecondsApplied / 3600.0:0.#} h, +{report.CreditsEarned:N0} ¤";
    }

    public void StartJoin(string host, int port, string password)
    {
        StartSession(GameSession.Remote(this, host, port, password, Prefs));
    }

    /// <summary>Termina la sesión activa y vuelve al título, con un motivo si lo hay.</summary>
    public void EndSession(string message)
    {
        _view?.QueueFree();
        _view = null;
        _session?.Shutdown();
        _session = null;
        _pendingMessage = message;
        if (Auto == null) ShowMenu();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            _session?.Shutdown();
            _session = null;
            GetTree().Quit();
        }
    }

    public string NewSlotName() => "partida-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
}

public static class CmdArgs
{
    public static Dictionary<string, string> Parse()
    {
        var map = new Dictionary<string, string>();
        var argv = OS.GetCmdlineUserArgs();
        for (var i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = argv[i][2..];
            map[key] = i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal) ? argv[++i] : "true";
        }
        return map;
    }
}

/// <summary>Una partida en marcha desde el punto de vista del jugador: cliente y, si es local, también servidor.</summary>
public sealed class GameSession
{
    public readonly GameClient Client;
    public readonly ServerRunner? Runner;
    public readonly bool IsSolo;
    public readonly bool IsHost;
    public readonly string Slot;
    public bool IsLocal => Runner != null;

    private GameSession(GameClient client, ServerRunner? runner, bool solo, bool host, string slot)
    {
        Client = client;
        Runner = runner;
        IsSolo = solo;
        IsHost = host;
        Slot = slot;
    }

    public static GameSession Local(Main main, World world, string slot, int? hostPort, Settings prefs, string noticeArg = "")
    {
        // Modo individual: servidor en un hilo y conexión en memoria; no toca la red.
        var hub = new LoopbackHub();
        ITransport serverTransport = hub.ServerEnd;
        if (hostPort != null) serverTransport = new CompositeTransport(hub.ServerEnd, LiteNetTransport.Listen(hostPort.Value));
        var server = new GameServer(world, serverTransport, new ServerOptions
        {
            HostKey = prefs.PlayerKey,
            JoinNoticeKey = noticeArg.Length > 0 ? "notice_offline" : "",
            JoinNoticeArg = noticeArg,
            Store = main.Store,
            SaveSlot = slot,
            Log = message => GD.Print("[servidor] " + message),
        });
        var runner = new ServerRunner(server);
        var client = new GameClient(world.Content, hub.ClientEnd, prefs.PlayerKey, prefs.PlayerName);
        hub.Open();
        runner.Start();
        return new GameSession(client, runner, hostPort == null, hostPort != null, slot);
    }

    public static GameSession Remote(Main main, string host, int port, string password, Settings prefs)
    {
        var client = new GameClient(main.Content, LiteNetTransport.Dial(host, port), prefs.PlayerKey, prefs.PlayerName, password);
        return new GameSession(client, null, false, false, "");
    }

    public void Shutdown()
    {
        if (Runner != null)
        {
            Runner.Stop(save: true);
            Runner.Dispose();
        }
        Client.Disconnect();
        Client.Dispose();
    }
}

/// <summary>Preferencias locales del jugador; no forman parte del mundo.</summary>
public sealed class Settings
{
    public string PlayerKey { get; set; } = "";
    public string PlayerName { get; set; } = "Ingeniero";
    public float Sensitivity { get; set; } = 1f;
    public float Fov { get; set; } = 75f;
    public float MasterVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 0.8f;
    public float MachineVolume { get; set; } = 0.6f;
    public float UiScale { get; set; } = 1f;
    public string Language { get; set; } = "es";
    public bool Fullscreen { get; set; }
    public bool CameraBob { get; set; } = true;
    public float BobIntensity { get; set; } = 1f;
    public List<string> Hotbar { get; set; } = new();
    public int ControlsVersion { get; set; }
    public bool Shadows { get; set; } = true;
    public bool Glow { get; set; } = true;
    public float EventAmbience { get; set; } = 0.7f;
    public Dictionary<string, long> Bindings { get; set; } = new();

    private string _path = "";

    public static Settings Load(string profile)
    {
        var dir = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "PlutonNovenoPlaneta", profile);
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "settings.json");
        Settings s;
        try
        {
            s = File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings() : new Settings();
        }
        catch (System.Text.Json.JsonException)
        {
            s = new Settings();
        }
        s._path = path;
        if (s.PlayerKey.Length < 16) s.PlayerKey = Guid.NewGuid().ToString("N");
        s.Save();
        return s;
    }

    public void Save() =>
        File.WriteAllText(_path, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    public void ApplyDisplay()
    {
        DisplayServer.WindowSetMode(Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        ((SceneTree)Engine.GetMainLoop()).Root.ContentScaleFactor = UiScale;
    }
}

public static class Loc
{
    private static Dictionary<string, string> _table = new();
    private static readonly System.Text.RegularExpressions.Regex KeyToken = new(@"\{([a-z_]+)\}", System.Text.RegularExpressions.RegexOptions.Compiled);
    public static readonly string[] Languages = { "es", "en" };

    public static void Load(string language)
    {
        var root = OS.HasFeature("editor") ? ProjectSettings.GlobalizePath("res://") : System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!;
        var dir = System.IO.Path.Combine(ContentDb.LocateDirectory(root), "loc");
        var path = System.IO.Path.Combine(dir, language + ".json");
        if (!File.Exists(path)) path = System.IO.Path.Combine(dir, "es.json");
        _table = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new();
    }

    /// <summary>Los marcadores {accion} muestran la tecla asignada: la ayuda no miente tras reasignar controles.</summary>
    public static string T(string key)
    {
        if (!_table.TryGetValue(key, out var v)) return key;
        return v.Contains('{') ? KeyToken.Replace(v, m => Controls.KeyName(m.Groups[1].Value)) : v;
    }

    public static string T(string key, string arg) => T(key).Replace("{0}", arg);
}