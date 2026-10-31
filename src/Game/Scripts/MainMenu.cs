using Godot;
using Pluton.Networking;

namespace Pluton.Game;

public partial class MainMenu : Control
{
    private readonly Main _main;
    private readonly string _message;
    private VBoxContainer _box = null!;

    public MainMenu(Main main, string message)
    {
        _main = main;
        _message = message;
    }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = new Color(0.025f, 0.035f, 0.06f) };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Plutón de fondo: un disco sencillo y su terminador, sin recursos externos.
        var shader = new Shader { Code = PlanetShader };
        var planet = new ColorRect { Material = new ShaderMaterial { Shader = shader } };
        planet.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(planet);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        margin.OffsetLeft = 90;
        margin.OffsetRight = 640;
        AddChild(margin);
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", 10);
        margin.AddChild(col);
        col.AddChild(Ui.Text(Loc.T("game_title"), 84, Ui.Ink));
        col.AddChild(Ui.Text(Loc.T("game_subtitle"), 26, Ui.Amber));
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, 30) });
        _box = new VBoxContainer();
        _box.AddThemeConstantOverride("separation", 8);
        col.AddChild(_box);
        if (_message.Length > 0)
        {
            var label = Ui.Text(_message, 18, Ui.Bad);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(label);
        }

        var version = Ui.Text($"Alpha {Protocol.GameVersion} · protocol {Protocol.Version} · data {_main.Content.Revision[..8]}", 14, Ui.Dim);
        version.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        version.OffsetLeft = 16;
        version.OffsetTop = -30;
        AddChild(version);

        ShowRoot();
    }

    private const string PlanetShader = """
        shader_type canvas_item;
        float hash(vec2 p){ return fract(sin(dot(p, vec2(127.1,311.7))) * 43758.5453); }
        float noise(vec2 p){ vec2 i=floor(p); vec2 f=fract(p); f=f*f*(3.0-2.0*f);
          return mix(mix(hash(i),hash(i+vec2(1,0)),f.x),mix(hash(i+vec2(0,1)),hash(i+vec2(1,1)),f.x),f.y); }
        float fbm(vec2 p){ float v=0.0; float a=0.5; for(int i=0;i<5;i++){ v+=a*noise(p); p*=2.1; a*=0.5; } return v; }
        void fragment(){
          vec2 uv = UV; float aspect = 16.0/9.0; vec2 p = vec2((uv.x-0.92)*aspect, uv.y-0.62);
          float r = length(p)/0.72;
          vec3 col = vec3(0.0); float alpha = 0.0;
          float star = step(0.9975, hash(floor(uv*vec2(900.0,520.0))));
          col += vec3(star)*0.8; alpha = star;
          if (r < 1.0) {
            float z = sqrt(1.0-r*r); vec3 n = vec3(p/0.72, z);
            vec2 q = n.xy/(1.0+n.z)*3.0 + TIME*0.003;
            float t = fbm(q*3.0);
            vec3 surf = mix(vec3(0.27,0.17,0.13), vec3(0.72,0.66,0.6), smoothstep(0.35,0.72,t));
            float d = max(dot(n, normalize(vec3(-0.75,-0.35,0.55))),0.0);
            col = surf*(d*1.15+0.012); alpha = 1.0;
          } else if (r < 1.02) { col = vec3(0.28,0.42,0.68)*(1.02-r)*30.0*0.25; alpha = (1.02-r)*50.0*0.5; }
          COLOR = vec4(col, alpha);
        }
        """;

    private void Clear()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
    }

    private void ShowRoot()
    {
        Clear();
        var saves = _main.Store.List();
        if (saves.Count > 0) _box.AddChild(Ui.Btn(Loc.T("menu_continue"), () => Safe(() => _main.StartSolo(saves[0].Slot, false)), 400));
        _box.AddChild(Ui.Btn(Loc.T("menu_new"), () => _main.StartSolo(_main.NewSlotName(), true), 400));
        if (saves.Count > 0) _box.AddChild(Ui.Btn(Loc.T("menu_load"), ShowLoad));
        _box.AddChild(Ui.Btn(Loc.T("menu_host"), ShowHost));
        _box.AddChild(Ui.Btn(Loc.T("menu_join"), ShowJoin));
        _box.AddChild(Ui.Btn(Loc.T("menu_settings"), () => { Clear(); SettingsForm.Build(_box, _main.Prefs, ShowRoot); }));
        _box.AddChild(Ui.Btn(Loc.T("menu_quit"), () => GetTree().Quit()));
    }

    private void Safe(Action action)
    {
        try { action(); }
        catch (Pluton.Persistence.SaveException e)
        {
            Clear();
            var label = Ui.Text(e.Message, 18, Ui.Bad);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _box.AddChild(label);
            _box.AddChild(Ui.Btn(Loc.T("menu_back"), ShowRoot));
        }
    }

    private void ShowLoad()
    {
        Clear();
        _box.AddChild(Ui.Text(Loc.T("load_title"), 24, Ui.Amber));
        foreach (var s in _main.Store.List().Take(8))
        {
            var slot = s.Slot;
            var hours = s.Tick / 10 / 3600.0;
            var incompatible = s.FormatVersion > Pluton.Simulation.WorldCodec.FormatVersion;
            var b = Ui.Btn($"{slot} · {s.SavedUtc.ToLocalTime():dd/MM HH:mm} · {hours:0.0} h", () => Safe(() => _main.StartSolo(slot, false)));
            b.Disabled = incompatible;
            _box.AddChild(b);
        }
        _box.AddChild(Ui.Btn(Loc.T("menu_back"), ShowRoot));
    }

    private void ShowHost()
    {
        Clear();
        _box.AddChild(Ui.Text(Loc.T("host_title"), 22, Ui.Amber));
        var note = Ui.Text(Loc.T("host_note"), 15, Ui.Dim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _box.AddChild(note);
        var name = new LineEdit { Text = _main.Prefs.PlayerName, MaxLength = Protocol.MaxNameLength };
        _box.AddChild(Ui.Text(Loc.T("join_name"), 15, Ui.Dim));
        _box.AddChild(name);
        var port = new LineEdit { Text = Main.DefaultPort.ToString() };
        _box.AddChild(Ui.Text(Loc.T("join_port"), 15, Ui.Dim));
        _box.AddChild(port);
        var saves = _main.Store.List();
        void Host(string slot, bool isNew)
        {
            _main.Prefs.PlayerName = name.Text;
            _main.Prefs.Save();
            var p = int.TryParse(port.Text, out var v) ? v : Main.DefaultPort;
            Safe(() => _main.StartHost(slot, isNew, p));
        }
        if (saves.Count > 0) _box.AddChild(Ui.Btn($"{Loc.T("host_start")} · {saves[0].Slot}", () => Host(saves[0].Slot, false)));
        _box.AddChild(Ui.Btn($"{Loc.T("host_start")} · {Loc.T("menu_new")}", () => Host(_main.NewSlotName(), true)));
        _box.AddChild(Ui.Btn(Loc.T("menu_back"), ShowRoot));
    }

    private void ShowJoin()
    {
        Clear();
        _box.AddChild(Ui.Text(Loc.T("join_title"), 22, Ui.Amber));
        var name = new LineEdit { Text = _main.Prefs.PlayerName, MaxLength = Protocol.MaxNameLength };
        var address = new LineEdit { Text = "127.0.0.1" };
        var port = new LineEdit { Text = Main.DefaultPort.ToString() };
        var password = new LineEdit { Secret = true };
        _box.AddChild(Ui.Text(Loc.T("join_name"), 15, Ui.Dim));
        _box.AddChild(name);
        _box.AddChild(Ui.Text(Loc.T("join_address"), 15, Ui.Dim));
        _box.AddChild(address);
        _box.AddChild(Ui.Text(Loc.T("join_port"), 15, Ui.Dim));
        _box.AddChild(port);
        _box.AddChild(Ui.Text(Loc.T("join_password"), 15, Ui.Dim));
        _box.AddChild(password);
        _box.AddChild(Ui.Btn(Loc.T("join_connect"), () =>
        {
            _main.Prefs.PlayerName = name.Text;
            _main.Prefs.Save();
            _main.StartJoin(address.Text.Trim(), int.TryParse(port.Text, out var p) ? p : Main.DefaultPort, password.Text);
        }));
        _box.AddChild(Ui.Btn(Loc.T("menu_back"), ShowRoot));
    }
}

public static class SettingsForm
{
    public static void Build(Container box, Settings prefs, Action back)
    {
        void Slider(string key, float min, float max, float step, float value, Action<float> set)
        {
            var label = Ui.Text($"{Loc.T(key)}: {value:0.##}", 15, Ui.Dim);
            box.AddChild(label);
            var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(380, 20) };
            s.ValueChanged += v =>
            {
                set((float)v);
                label.Text = $"{Loc.T(key)}: {v:0.##}";
                prefs.Save();
            };
            box.AddChild(s);
        }
        void Toggle(string key, bool value, Action<bool> set)
        {
            var c = new CheckButton { Text = Loc.T(key), ButtonPressed = value };
            c.Toggled += v =>
            {
                set(v);
                prefs.Save();
            };
            box.AddChild(c);
        }

        box.AddChild(Ui.Text(Loc.T("settings_title"), 24, Ui.Amber));
        Slider("settings_sensitivity", 0.2f, 3f, 0.05f, prefs.Sensitivity, v => prefs.Sensitivity = v);
        Slider("settings_fov", 60, 110, 1, prefs.Fov, v => prefs.Fov = v);
        Slider("settings_master", 0, 1, 0.05f, prefs.MasterVolume, v => prefs.MasterVolume = v);
        Slider("settings_sfx", 0, 1, 0.05f, prefs.SfxVolume, v => prefs.SfxVolume = v);
        Slider("settings_machines", 0, 1, 0.05f, prefs.MachineVolume, v => prefs.MachineVolume = v);
        Slider("settings_ui_scale", 0.75f, 1.5f, 0.05f, prefs.UiScale, v =>
        {
            prefs.UiScale = v;
            ((SceneTree)Engine.GetMainLoop()).Root.ContentScaleFactor = v;
        });
        Slider("settings_event_ambience", 0, 1, 0.05f, prefs.EventAmbience, v => prefs.EventAmbience = v);
        Toggle("settings_fullscreen", prefs.Fullscreen, v => { prefs.Fullscreen = v; prefs.ApplyDisplay(); });
        Toggle("settings_bob", prefs.CameraBob, v => prefs.CameraBob = v);
        Toggle("settings_shadows", prefs.Shadows, v => prefs.Shadows = v);
        Toggle("settings_glow", prefs.Glow, v => prefs.Glow = v);
        var languages = new HBoxContainer();
        languages.AddChild(Ui.Text(Loc.T("settings_language") + ": ", 15, Ui.Dim));
        foreach (var code in Loc.Languages)
        {
            var lang = code;
            languages.AddChild(Ui.Btn(code.ToUpperInvariant(), () =>
            {
                prefs.Language = lang;
                prefs.Save();
                Loc.Load(lang);
                back();
            }));
        }
        box.AddChild(languages);
        box.AddChild(Ui.Btn(Loc.T("settings_controls"), () =>
        {
            foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
            ControlsForm(box, back);
        }));
        box.AddChild(Ui.Btn(Loc.T("menu_back"), back));
    }

    private static void ControlsForm(Container box, Action back)
    {
        box.AddChild(Ui.Text(Loc.T("settings_controls"), 24, Ui.Amber));
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(460, 520) };
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(list);
        box.AddChild(scroll);
        foreach (var action in Controls.Rebindable)
        {
            var id = action;
            Button button = null!;
            button = Ui.Btn($"{Loc.T("action_" + id)}: {Controls.KeyNames(id)}", () =>
            {
                button.Text = Loc.T("settings_press_key");
                var capture = new KeyCapture(key =>
                {
                    if (key != Key.None) Controls.Rebind(id, key);
                    foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
                    ControlsForm(box, back);
                });
                box.AddChild(capture);
            });
            button.Alignment = HorizontalAlignment.Left;
            list.AddChild(button);
        }
        box.AddChild(Ui.Btn(Loc.T("settings_reset_controls"), () =>
        {
            Controls.ResetAll();
            foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
            ControlsForm(box, back);
        }));
        box.AddChild(Ui.Btn(Loc.T("menu_back"), back));
    }
}

/// <summary>Espera una pulsación para reasignar. Esc cancela; no llega al juego.</summary>
public partial class KeyCapture : Control
{
    private readonly Action<Key> _done;
    public KeyCapture(Action<Key> done) { _done = done; }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key) return;
        GetViewport().SetInputAsHandled();
        var pressed = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        _done(pressed == Key.Escape ? Key.None : pressed);
    }
}
