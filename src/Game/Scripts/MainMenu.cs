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

        // Fondo: la base inicial viva, con los mismos modelos y entorno que la partida.
        var scene = new MenuScene(_main.Prefs);
        AddChild(scene);

        // Velo lateral: el texto se lee con cualquier encuadre, sin tapar la escena.
        var veil = new TextureRect
        {
            Texture = VeilTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        veil.OffsetRight = 880;
        AddChild(veil);

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        margin.OffsetLeft = 96;
        margin.OffsetRight = 720;
        margin.OffsetTop = 48;
        margin.OffsetBottom = -56;
        AddChild(margin);
        var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        col.AddThemeConstantOverride("separation", UiTheme.M);
        margin.AddChild(col);
        var title = Ui.Text(Loc.T("game_title"), 92, Ui.Ink);
        title.AddThemeFontOverride("font", UiTheme.Display);
        col.AddChild(title);
        var sub = Ui.Text(Loc.T("game_subtitle").ToUpperInvariant(), UiTheme.Heading, Ui.Amber);
        col.AddChild(sub);
        var rule = new ColorRect { Color = Ui.Amber, CustomMinimumSize = new Vector2(64, 3), SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, UiTheme.S) });
        col.AddChild(rule);
        col.AddChild(new Control { CustomMinimumSize = new Vector2(0, UiTheme.XL) });
        _box = new VBoxContainer();
        _box.AddThemeConstantOverride("separation", UiTheme.M);
        col.AddChild(_box);
        if (_message.Length > 0)
        {
            var label = Ui.Text(_message, UiTheme.Body, Ui.Bad);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            col.AddChild(label);
        }

        var version = Ui.Text($"Alpha {Protocol.GameVersion} · protocol {Protocol.Version} · data {_main.Content.Revision[..8]}", UiTheme.Caption, Ui.Dim);
        version.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        version.OffsetLeft = 96;
        version.OffsetTop = -36;
        AddChild(version);

        ShowRoot();
    }

    /// <summary>Degradado de izquierda a derecha en memoria; no depende de un recurso.</summary>
    private static Texture2D VeilTexture()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0.012f, 0.018f, 0.03f, 0.94f));
        gradient.SetColor(1, new Color(0.012f, 0.018f, 0.03f, 0f));
        gradient.AddPoint(0.55f, new Color(0.012f, 0.018f, 0.03f, 0.72f));
        return new GradientTexture2D { Gradient = gradient, Width = 256, Height = 4, FillFrom = Vector2.Zero, FillTo = Vector2.Right };
    }

    /// <summary>Botón principal: texto a la izquierda y altura cómoda.</summary>
    private static Button MainButton(string text, Action action)
    {
        var b = Ui.Btn(text, action, 420);
        b.Alignment = HorizontalAlignment.Left;
        b.AddThemeFontSizeOverride("font_size", UiTheme.Heading);
        b.CustomMinimumSize = new Vector2(420, 46);
        b.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        return b;
    }

    private void Clear()
    {
        foreach (var c in _box.GetChildren()) { _box.RemoveChild(c); c.QueueFree(); }
    }

    private void ShowRoot()
    {
        Clear();
        var saves = _main.Store.List();
        Button? first = null;
        Button Add(Button b)
        {
            _box.AddChild(b);
            first ??= b;
            return b;
        }
        if (saves.Count > 0) Add(MainButton($"{Loc.T("menu_continue")}  ·  {saves[0].SavedUtc.ToLocalTime():dd/MM HH:mm}", () => Safe(() => _main.StartSolo(saves[0].Slot, false))));
        Add(MainButton(Loc.T("menu_new"), () => _main.StartSolo(_main.NewSlotName(), true)));
        if (saves.Count > 0) Add(MainButton(Loc.T("menu_load"), ShowLoad));
        Add(MainButton(Loc.T("menu_host"), ShowHost));
        Add(MainButton(Loc.T("menu_join"), ShowJoin));
        Add(MainButton(Loc.T("menu_settings"), () => { Clear(); SettingsForm.Build(_box, _main.Prefs, ShowRoot); }));
        Add(MainButton(Loc.T("menu_quit"), () => GetTree().Quit()));
        // Navegación por teclado desde el primer momento.
        first?.CallDeferred(Control.MethodName.GrabFocus);
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
        _box.AddChild(Ui.Heading(Loc.T("load_title"), UiTheme.Heading));
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
        _box.AddChild(Ui.Heading(Loc.T("host_title"), UiTheme.Heading));
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
        _box.AddChild(Ui.Heading(Loc.T("join_title"), UiTheme.Heading));
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
        box.AddChild(Ui.Heading(Loc.T("settings_title")));
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(600, 430), TabAlignment = TabBar.AlignmentMode.Left };
        box.AddChild(tabs);

        VBoxContainer Section(string key)
        {
            var section = new VBoxContainer { Name = Loc.T(key) };
            section.AddThemeConstantOverride("separation", UiTheme.M);
            tabs.AddChild(section);
            return section;
        }

        void Slider(VBoxContainer to, string key, float min, float max, float step, float value, Func<float, string> show, Action<float> set)
        {
            var label = Ui.Text(Loc.T(key), UiTheme.Small, Ui.Ink);
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var amount = Ui.Text(show(value), UiTheme.Small, Ui.Amber);
            to.AddChild(Ui.Row(UiTheme.M, label, amount));
            var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(380, 22), FocusMode = Control.FocusModeEnum.All };
            s.ValueChanged += v =>
            {
                set((float)v);
                amount.Text = show((float)v);
                prefs.Save();
            };
            to.AddChild(s);
        }

        void Toggle(VBoxContainer to, string key, bool value, Action<bool> set)
        {
            var c = new CheckButton { Text = Loc.T(key), ButtonPressed = value };
            c.Toggled += v =>
            {
                set(v);
                prefs.Save();
                Scenery.Current?.ApplyPrefs(prefs);
            };
            to.AddChild(c);
        }

        // Elección entre valores cerrados: lo activo se marca con relleno y borde, y aplica al momento.
        void Segments(VBoxContainer to, string key, string hintKey, (string label, Func<bool> active, Action choose)[] options)
        {
            to.AddChild(Ui.Text(Loc.T(key), UiTheme.Small, Ui.Ink));
            var row = Ui.Row(UiTheme.S);
            var group = new ButtonGroup();
            foreach (var (label, active, choose) in options)
            {
                var b = new Button { Text = label, ToggleMode = true, ButtonGroup = group, ButtonPressed = active(), CustomMinimumSize = new Vector2(92, UiTheme.ButtonHeight), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                b.Pressed += () =>
                {
                    choose();
                    prefs.Save();
                };
                row.AddChild(b);
            }
            to.AddChild(row);
            if (hintKey.Length > 0) to.AddChild(Ui.Para(Loc.T(hintKey), UiTheme.Caption, Ui.Dim));
        }

        var percent = (float v) => $"{v * 100:0} %";

        var display = Section("settings_tab_display");
        Segments(display, "settings_quality", "settings_quality_hint", new (string, Func<bool>, Action)[]
        {
            (Loc.T("quality_low"), () => prefs.Quality == 0, () => { prefs.Quality = 0; Scenery.Current?.ApplyPrefs(prefs); }),
            (Loc.T("quality_medium"), () => prefs.Quality == 1, () => { prefs.Quality = 1; Scenery.Current?.ApplyPrefs(prefs); }),
            (Loc.T("quality_high"), () => prefs.Quality == 2, () => { prefs.Quality = 2; Scenery.Current?.ApplyPrefs(prefs); }),
        });
        Toggle(display, "settings_shadows", prefs.Shadows, v => prefs.Shadows = v);
        Toggle(display, "settings_glow", prefs.Glow, v => prefs.Glow = v);
        Toggle(display, "settings_fullscreen", prefs.Fullscreen, v => { prefs.Fullscreen = v; prefs.ApplyDisplay(); });
        Slider(display, "settings_fov", 60, 110, 1, prefs.Fov, v => $"{v:0}°", v => prefs.Fov = v);

        var ui = Section("settings_tab_interface");
        var scales = new[] { 0.75f, 0.9f, 1f, 1.1f, 1.25f, 1.5f };
        Segments(ui, "settings_ui_scale", "settings_ui_scale_hint", scales.Select(sc => (percent(sc), (Func<bool>)(() => Mathf.Abs(prefs.UiScale - sc) < 0.01f), (Action)(() =>
        {
            prefs.UiScale = sc;
            ((SceneTree)Engine.GetMainLoop()).Root.ContentScaleFactor = sc;
        }))).ToArray());
        ui.AddChild(Ui.Text(Loc.T("settings_language"), UiTheme.Small, Ui.Ink));
        var languages = Ui.Row(UiTheme.S);
        foreach (var code in Loc.Languages)
        {
            var lang = code;
            var b = Ui.Btn(code.ToUpperInvariant(), () =>
            {
                prefs.Language = lang;
                prefs.Save();
                Loc.Load(lang);
                back();
            }, 92);
            b.Disabled = lang == prefs.Language;
            languages.AddChild(b);
        }
        ui.AddChild(languages);

        var sound = Section("settings_tab_audio");
        Slider(sound, "settings_master", 0, 1, 0.05f, prefs.MasterVolume, percent, v => prefs.MasterVolume = v);
        Slider(sound, "settings_sfx", 0, 1, 0.05f, prefs.SfxVolume, percent, v => prefs.SfxVolume = v);
        Slider(sound, "settings_machines", 0, 1, 0.05f, prefs.MachineVolume, percent, v => prefs.MachineVolume = v);
        Slider(sound, "settings_event_ambience", 0, 1, 0.05f, prefs.EventAmbience, percent, v => prefs.EventAmbience = v);

        var input = Section("settings_tab_controls");
        Slider(input, "settings_sensitivity", 0.2f, 3f, 0.05f, prefs.Sensitivity, v => $"{v:0.00}", v => prefs.Sensitivity = v);
        Toggle(input, "settings_bob", prefs.CameraBob, v => prefs.CameraBob = v);
        input.AddChild(Ui.Btn(Loc.T("settings_controls"), () =>
        {
            foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
            ControlsForm(box, back);
        }));

        var close = Ui.Btn(Loc.T("menu_back"), back);
        box.AddChild(close);
        close.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static void ControlsForm(Container box, Action back)
    {
        box.AddChild(Ui.Heading(Loc.T("settings_controls")));
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
