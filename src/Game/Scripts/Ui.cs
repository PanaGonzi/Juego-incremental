using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

public static class Ui
{
    public static readonly Color Amber = UiTheme.Amber;
    public static readonly Color Ink = UiTheme.Ink;
    public static readonly Color Dim = UiTheme.Dim;
    public static readonly Color Good = UiTheme.Good;
    public static readonly Color Bad = UiTheme.Bad;
    public static readonly Color Warn = UiTheme.Warn;

    public static Theme Theme => UiTheme.Main;

    public static Label Heading(string text, int size = UiTheme.Title, Color? color = null)
    {
        var l = Text(text, size, color ?? Amber);
        l.AddThemeFontOverride("font", UiTheme.Bold);
        return l;
    }

    /// <summary>Texto que se ajusta al ancho disponible: nunca se corta.</summary>
    public static Label Para(string text, int size = UiTheme.Small, Color? color = null)
    {
        var l = Text(text, size, color ?? Dim);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return l;
    }

    public static TextureRect Icon(Texture2D texture, int size = 28)
    {
        return new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    public static HBoxContainer Row(int separation = UiTheme.M, params Control[] children)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", separation);
        foreach (var c in children) row.AddChild(c);
        return row;
    }

    public static ProgressBar Bar(double value, double max, Color? fill = null, int height = 12)
    {
        var bar = new ProgressBar { MaxValue = Math.Max(max, 0.001), Value = value, ShowPercentage = false, CustomMinimumSize = new Vector2(0, height), MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        if (fill != null) bar.AddThemeStyleboxOverride("fill", UiTheme.Box(fill.Value, new Color(0, 0, 0, 0), 0, 0, 0, 2));
        return bar;
    }

    /// <summary>Icono de objeto y cantidad, con el nombre como ayuda emergente.</summary>
    public static HBoxContainer Stack(string item, string amount, int iconSize = 28, Color? color = null)
    {
        var icon = Icon(ItemVisuals.IconOf(item), iconSize);
        icon.TooltipText = Loc.T("item_" + item);
        icon.MouseFilter = Control.MouseFilterEnum.Stop;
        var row = Row(UiTheme.S, icon, Text(amount, UiTheme.Body, color));
        row.AddChild(Text(Loc.T("item_" + item), UiTheme.Small, Dim));
        return row;
    }

    public static Texture2D Glyph(string name) => ItemVisuals.IconOf(name);

    /// <summary>Forma de estado: icono además de color, y texto junto a él.</summary>
    public static string StatusGlyph(MachineStatus status) => status switch
    {
        MachineStatus.NoInput => "st_wait",
        MachineStatus.OutputFull => "st_full",
        MachineStatus.NoPower or MachineStatus.LowPower => "st_power",
        MachineStatus.Overheated => "st_heat",
        _ => "st_ok",
    };

    public static Label Text(string text, int size = 18, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    public static Button Btn(string text, Action action, int minWidth = 0)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, UiTheme.ButtonHeight) };
        b.Pressed += action;
        return b;
    }

    public static string Amounts(IEnumerable<KeyValuePair<string, long>> items)
    {
        var parts = items.Select(kv => $"{kv.Value} {Loc.T("item_" + kv.Key)}").ToList();
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    public static string Cost(long credits, Dictionary<string, long> items)
    {
        var parts = new List<string>();
        if (credits > 0) parts.Add($"{credits} ¤");
        parts.AddRange(items.Select(kv => $"{kv.Value} {Loc.T("item_" + kv.Key)}"));
        return string.Join(" · ", parts);
    }
}

/// <summary>Interfaz en partida: datos del mundo replicado, sin lógica económica propia.</summary>
public partial class Hud : Control
{
    private readonly GameView _view;
    private Label _credits = null!, _power = null!, _income = null!, _objective = null!, _objectiveStep = null!, _objectiveReward = null!, _objectiveCount = null!, _bag = null!, _prompt = null!, _status = null!, _players = null!, _chatLog = null!, _toast = null!;
    private readonly List<PanelContainer> _toolSlots = new();
    private readonly List<(Label label, string key)> _toolNames = new();
    private ProgressBar _powerBar = null!, _objectiveBar = null!;
    private readonly Dictionary<string, Queue<(double time, long total)>> _produced = new();
    private int _meterId = -1;
    private long _meterProgress;
    private double _meterTime;
    private PanelContainer _center = null!;
    private VBoxContainer _centerBody = null!;
    private LineEdit _chatInput = null!;
    private float _toastTime;
    private readonly List<string> _chatLines = new();
    private readonly Queue<(double time, long total)> _earnings = new();
    private double _elapsed;
    private double _nextEarning;
    private double _panelRefresh;
    private Action? _panelBuilder;
    public bool PanelOpen => _center.Visible;
    public bool ChatOpen => _chatInput.Visible;

    public Hud(GameView view) { _view = view; }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        var topLeft = Panel(16, 16);
        var tl = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        tl.AddThemeConstantOverride("separation", UiTheme.S);
        topLeft.AddChild(tl);
        _credits = Ui.Heading("", UiTheme.Title);
        _income = Ui.Text("", UiTheme.Small, Ui.Dim);
        _power = Ui.Text("", UiTheme.Small);
        tl.AddChild(Ui.Row(UiTheme.M, Ui.Icon(Ui.Glyph("credit"), 28), _credits));
        tl.AddChild(_income);
        tl.AddChild(Ui.Row(UiTheme.S, Ui.Icon(Ui.Glyph("energy"), 20), _power));
        _powerBar = Ui.Bar(0, 1, Ui.Good, 8);
        _powerBar.CustomMinimumSize = new Vector2(190, 8);
        tl.AddChild(_powerBar);

        var top = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        top.GrowHorizontal = GrowDirection.Both;
        top.OffsetTop = 16;
        AddChild(top);
        var topBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        topBox.AddThemeConstantOverride("separation", UiTheme.S);
        top.AddChild(topBox);
        // Objetivo: qué toca hacer ahora, cuánto falta y qué se gana.
        _objectiveStep = Ui.Text("", UiTheme.Caption, Ui.Amber);
        _objectiveStep.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _objectiveReward = Ui.Text("", UiTheme.Caption, Ui.Warn);
        topBox.AddChild(Ui.Row(UiTheme.M, _objectiveStep, _objectiveReward));
        _objective = Ui.Text("", 18);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _objective.CustomMinimumSize = new Vector2(760, 0);
        topBox.AddChild(_objective);
        _objectiveBar = Ui.Bar(0, 1, Ui.Amber, 8);
        _objectiveCount = Ui.Text("", UiTheme.Caption, Ui.Dim);
        var progressRow = Ui.Row(UiTheme.M, _objectiveBar, _objectiveCount);
        progressRow.Alignment = BoxContainer.AlignmentMode.Center;
        topBox.AddChild(progressRow);

        var right = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        right.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        right.GrowHorizontal = GrowDirection.Begin;
        right.OffsetLeft = -16; right.OffsetRight = -16; right.OffsetTop = 16;
        AddChild(right);
        _players = Ui.Text("", 15);
        right.AddChild(_players);

        var bagPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        bagPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        bagPanel.GrowHorizontal = GrowDirection.Begin;
        bagPanel.GrowVertical = GrowDirection.Begin;
        bagPanel.OffsetRight = -16; bagPanel.OffsetBottom = -16; bagPanel.OffsetLeft = -16; bagPanel.OffsetTop = -16;
        AddChild(bagPanel);
        _bag = Ui.Text("", 16);
        bagPanel.AddChild(_bag);

        var hotbar = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false }; // sustituida por Hotbar
        hotbar.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        hotbar.GrowHorizontal = GrowDirection.Both;
        hotbar.GrowVertical = GrowDirection.Begin;
        hotbar.OffsetBottom = -16;
        AddChild(hotbar);
        foreach (var (key, label) in new[] { (Controls.KeyNames("tool_cutter"), "tool_cutter"), (Controls.KeyNames("tool_build"), "tool_build"), (Controls.KeyNames("tool_link"), "tool_link"), (Controls.KeyNames("tool_remove"), "tool_remove"), (Controls.KeyNames("tool_rail"), "tool_rail") })
        {
            var slot = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
            var box = new VBoxContainer();
            slot.AddChild(box);
            box.AddChild(Ui.Text(key, 13, Ui.Dim));
            var name = Ui.Text(Loc.T(label), 17);
            box.AddChild(name);
            _toolNames.Add((name, label));
            hotbar.AddChild(slot);
            _toolSlots.Add(slot);
        }

        var cross = new ColorRect { Color = new Color(1, 1, 1, 0.85f), CustomMinimumSize = new Vector2(4, 4), MouseFilter = MouseFilterEnum.Ignore };
        cross.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        cross.OffsetLeft = -2; cross.OffsetRight = 2; cross.OffsetTop = -2; cross.OffsetBottom = 2;
        AddChild(cross);

        var promptBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        promptBox.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        promptBox.GrowHorizontal = GrowDirection.Both;
        promptBox.OffsetTop = 34;
        promptBox.OffsetBottom = 120;
        AddChild(promptBox);
        _status = Ui.Text("", 20);
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.AddThemeConstantOverride("outline_size", 5);
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        promptBox.AddChild(_status);
        _prompt = Ui.Text("", 16, Ui.Amber);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt.AddThemeConstantOverride("outline_size", 4);
        _prompt.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        promptBox.AddChild(_prompt);

        _toast = Ui.Text("", 20, Ui.Bad);
        _toast.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        _toast.GrowHorizontal = GrowDirection.Both;
        _toast.GrowVertical = GrowDirection.Begin;
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toast.OffsetBottom = -110;
        _toast.AddThemeConstantOverride("outline_size", 5);
        _toast.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        AddChild(_toast);

        var chatBox = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        chatBox.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        chatBox.GrowVertical = GrowDirection.Begin;
        chatBox.OffsetLeft = 16; chatBox.OffsetBottom = -16; chatBox.OffsetRight = 500;
        AddChild(chatBox);
        _chatLog = Ui.Text("", 15);
        _chatLog.AddThemeConstantOverride("outline_size", 4);
        _chatLog.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        chatBox.AddChild(_chatLog);
        _chatInput = new LineEdit { Visible = false, MaxLength = 200 };
        _chatInput.TextSubmitted += text =>
        {
            if (text.Trim().Length > 0) _view.Session.Client.SendChat(text);
            CloseChat();
        };
        chatBox.AddChild(_chatInput);

        _center = new PanelContainer { Visible = false };
        _center.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _center.GrowHorizontal = GrowDirection.Both;
        _center.GrowVertical = GrowDirection.Both;
        AddChild(_center);
        _centerBody = new VBoxContainer();
        _centerBody.AddThemeConstantOverride("separation", 8);
        _center.AddChild(_centerBody);
        _center.Theme = Ui.Theme;
    }

    private PanelContainer Panel(float x, float y)
    {
        var p = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Position = new Vector2(x, y) };
        AddChild(p);
        return p;
    }

    public void Toast(string text, Color? color = null)
    {
        if (text.Length == 0) return;
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", color ?? Ui.Bad);
        _toastTime = 3f;
    }

    public void AddChat(string line)
    {
        _chatLines.Add(line);
        if (_chatLines.Count > 7) _chatLines.RemoveAt(0);
        _chatLog.Text = string.Join("\n", _chatLines);
    }

    public void OpenChat()
    {
        _chatInput.Text = "";
        _chatInput.Visible = true;
        _chatInput.GrabFocus();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void CloseChat()
    {
        _chatInput.Visible = false;
        _chatInput.ReleaseFocus();
        _view.UpdateMouseMode();
    }

    public string StatusText => _status.Text;

    public void SetPrompt(string status, Color statusColor, string prompt)
    {
        _status.Text = status;
        _status.AddThemeColorOverride("font_color", statusColor);
        _prompt.Text = prompt;
    }

    /// <summary>Vuelve a aplicar el idioma a los textos fijos del HUD.</summary>
    public void RefreshStaticText()
    {
        foreach (var (label, key) in _toolNames) label.Text = Loc.T(key);
    }

    public void SetTool(int index)
    {
        for (var i = 0; i < _toolSlots.Count; i++) _toolSlots[i].Modulate = i == index ? new Color(1.25f, 1.05f, 0.65f) : new Color(1, 1, 1, 0.7f);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_toastTime > 0)
        {
            _toastTime -= (float)delta;
            if (_toastTime <= 0) _toast.Text = "";
        }
        var client = _view.Session.Client;
        var w = client.World;
        var me = client.LocalPlayer;
        if (w == null || me == null) return;

        _credits.Text = $"{w.Credits:N0} ¤";
        var total = w.Stats.GetValueOrDefault("sold_credits") + w.Stats.GetValueOrDefault("contract_credits");
        if (_elapsed >= _nextEarning)
        {
            _nextEarning = _elapsed + 1;
            _earnings.Enqueue((_elapsed, total));
            while (_earnings.Count > 1 && _earnings.Peek().time < _elapsed - 60) _earnings.Dequeue();
        }
        var (oldTime, oldTotal) = _earnings.Count > 0 ? _earnings.Peek() : (_elapsed, total);
        var span = Math.Max(10, _elapsed - oldTime);
        _income.Text = $"{Loc.T("hud_income")}: {(total - oldTotal) * 60 / span:F0} ¤";
        SampleProduction(w);

        var (supply, demand) = EstimatePower(w, me.Planet);
        _power.Text = $"{Loc.T("hud_power")}: {demand} / {supply}";
        _power.AddThemeColorOverride("font_color", demand > supply ? Ui.Bad : Ui.Good);
        _powerBar.MaxValue = Math.Max(1, supply);
        _powerBar.Value = Math.Min(demand, supply);
        _powerBar.AddThemeStyleboxOverride("fill", UiTheme.Box(demand > supply ? Ui.Bad : Ui.Good, new Color(0, 0, 0, 0), 0, 0, 0, 2));
        if (w.Content.Planets.First(pl => pl.Id == me.Planet).Thermal)
        {
            var (heat, cooling) = EstimateHeat(w, me.Planet);
            _power.Text += $"\n{Loc.T("hud_heat")}: {heat} / {cooling}";
            if (heat > cooling) _power.AddThemeColorOverride("font_color", Ui.Bad);
        }

        UpdateObjective(w);

        var bag = Ui.Amounts(me.Bag.Items);
        _bag.Text = $"{Loc.T("hud_bag")}: {(me.Bag.Kinds == 0 ? Loc.T("hud_empty") : "\n" + bag.Replace(", ", "\n"))}";

        var list = w.Players.Values.Where(p => p.Connected).ToList();
        var mode = _view.Session.IsSolo ? "" : $"  ·  {client.RoundTripMs} ms";
        _players.Text = $"{Loc.T("hud_players")} {list.Count}/12{mode}\n" + string.Join("\n", list.Select(p => $"{p.Name} ({Loc.T("role_" + p.Role)})"));

        if (PanelOpen)
        {
            // Centrado cada cuadro: el contenido cambia de tamaño al reconstruirse.
            var size = _center.GetCombinedMinimumSize();
            _center.Size = size;
            _center.Position = ((GetRect().Size - size) / 2).Max(Vector2.Zero);
        }
        if (PanelOpen && _panelBuilder != null)
        {
            _panelRefresh -= delta;
            // Reconstruir durante una pulsación haría perder el clic: el botón desaparecería antes de soltarlo.
            if (_panelRefresh <= 0 && !Input.IsMouseButtonPressed(MouseButton.Left))
            {
                _panelRefresh = 0.3;
                _panelBuilder();
            }
        }
    }

    private void UpdateObjective(World w)
    {
        var count = w.Content.Objectives.Count;
        if (w.ObjectiveIndex >= count)
        {
            _objectiveStep.Text = Loc.T("hud_objective").ToUpperInvariant();
            _objectiveReward.Text = "";
            _objective.Text = Loc.T("objective_done");
            _objectiveBar.Visible = false;
            _objectiveCount.Visible = false;
            return;
        }
        var o = w.Content.Objectives[w.ObjectiveIndex];
        var have = Math.Min(o.Qty, w.Stats.GetValueOrDefault(o.Stat));
        _objectiveStep.Text = $"{Loc.T("hud_objective").ToUpperInvariant()}  {w.ObjectiveIndex + 1}/{count}";
        _objectiveReward.Text = o.Credits > 0 ? $"{Loc.T("hud_reward")}: +{o.Credits} ¤" : "";
        _objective.Text = Loc.T("objective_" + o.Id);
        _objectiveBar.Visible = true;
        _objectiveCount.Visible = true;
        _objectiveBar.MaxValue = Math.Max(1, o.Qty);
        _objectiveBar.Value = have;
        var distance = _view.ObjectiveDistance > 7 ? $"   ·   {_view.ObjectiveDistance:0} m" : "";
        _objectiveCount.Text = $"{have}/{o.Qty}{distance}";
    }

    /// <summary>Producción acumulada del mundo cada segundo: de aquí sale la cifra real del panel de máquina.</summary>
    private void SampleProduction(World w)
    {
        if (_elapsed < _nextEarning - 1 + 0.02)
        {
            foreach (var item in w.Content.Items.Keys)
            {
                if (!_produced.TryGetValue(item, out var q)) _produced[item] = q = new Queue<(double, long)>();
                q.Enqueue((_elapsed, w.ProducedTotal(item)));
                while (q.Count > 1 && q.Peek().time < _elapsed - 60) q.Dequeue();
            }
        }
    }

    private double? MeasuredPerMinute(string item)
    {
        if (!_produced.TryGetValue(item, out var q) || q.Count < 2) return null;
        var first = q.Peek();
        var last = q.Last();
        var seconds = last.time - first.time;
        return seconds < 5 ? null : (last.total - first.total) * 60.0 / seconds;
    }

    private static (long heat, long cooling) EstimateHeat(World w, int planet)
    {
        long heat = 0, cooling = 0;
        foreach (var m in w.Machines.Values)
        {
            if (m.Planet != planet) continue;
            var d = w.Content.Machines[m.Def];
            if (m.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.NoPower or MachineStatus.Overheated) heat += d.Heat;
            if (d.Kind == MachineKind.Radiator || (d.Cooling > 0 && m.Processing)) cooling += d.Cooling;
        }
        return (heat, cooling);
    }

    private static (long supply, long demand) EstimatePower(World w, int planet)
    {
        long supply = 0, demand = 0;
        foreach (var m in w.Machines.Values)
        {
            if (m.Planet != planet) continue;
            var d = w.Content.Machines[m.Def];
            supply += d.PowerGen;
            if (m.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.NoPower or MachineStatus.Overheated) demand += d.Power;
        }
        return (supply, demand);
    }

    // ---------- Paneles centrales ----------

    private void Open(Action builder)
    {
        _panelBuilder = builder;
        _panelRefresh = 0.3;
        _center.Visible = true;
        builder();
        _view.UpdateMouseMode();
    }

    public void ClosePanel()
    {
        _center.Visible = false;
        _panelBuilder = null;
        Clear();
        _view.UpdateMouseMode();
    }

    private void Clear()
    {
        foreach (var c in _centerBody.GetChildren()) { _centerBody.RemoveChild(c); c.QueueFree(); }
        _centerBody.CustomMinimumSize = Vector2.Zero;
    }

    private void Add(Control c) => _centerBody.AddChild(c);

    public void OpenPause(GameSession session, Action resume, Action save, Action leave, Settings prefs, Action settings)
    {
        Open(() => { });
        _panelBuilder = null;
        Clear();
        Add(Ui.Text(Loc.T("paused"), 28, Ui.Amber));
        if (!session.IsSolo) Add(Ui.Text(Loc.T("menu_online_nopause"), 15, Ui.Dim));
        Add(Ui.Btn(Loc.T("menu_resume"), resume, 320));
        if (session.IsLocal || session.Client.LocalPlayer?.Role >= Role.Admin) Add(Ui.Btn(Loc.T("menu_save"), save));
        Add(Ui.Btn(Loc.T("menu_settings"), settings));
        Add(Ui.Btn(Loc.T(session.IsLocal ? "menu_to_title" : "menu_leave"), leave));
    }

    public void OpenSettings(Settings prefs, Action back)
    {
        _panelBuilder = null;
        Clear();
        SettingsForm.Build(_centerBody, prefs, back);
    }

    public void OpenBuild(Action<string> choose)
    {
        Open(() => BuildMenu(choose));
        _panelBuilder = null;
    }

    private void BuildMenu(Action<string> choose)
    {
        Clear();
        var w = _view.Session.Client.World!;
        var me = _view.Session.Client.LocalPlayer!;
        Add(Ui.Text(Loc.T("tool_build"), 26, Ui.Amber));
        foreach (var def in w.Content.Machines.Values)
        {
            if (def.Builtin && def.Credits == 0 && def.Cost.Count == 0) continue;
            if (def.Unlock != null && !w.Upgrades.Contains(def.Unlock)) continue;
            var afford = w.CanAfford(me, def.Credits, def.Cost);
            var b = Ui.Btn($"{Loc.T("machine_" + def.Id)}   —   {Ui.Cost(def.Credits, def.Cost)}{(afford ? "" : "   ·   " + Loc.T("cost_missing"))}", () => choose(def.Id), 760);
            b.Alignment = HorizontalAlignment.Left;
            if (!afford) b.Modulate = new Color(1, 0.75f, 0.7f);
            Add(b);
        }
        Add(Ui.Btn(Loc.T("panel_close"), ClosePanel));
    }

    public void OpenMachine(int id)
    {
        Open(() => MachinePanel(id));
    }

    private void MachinePanel(int id)
    {
        var client = _view.Session.Client;
        var w = client.World!;
        if (!w.Machines.TryGetValue(id, out var m)) { ClosePanel(); return; }
        Clear();
        var def = w.Content.Machines[m.Def];
        var readout = w.ReadMachine(m);
        var stopped = m.Status is MachineStatus.NoInput or MachineStatus.OutputFull or MachineStatus.NoPower or MachineStatus.LowPower or MachineStatus.Overheated;
        var color = m.Status is MachineStatus.NoPower or MachineStatus.Overheated or MachineStatus.LowPower ? Ui.Bad : stopped ? Ui.Warn : Ui.Good;
        _centerBody.CustomMinimumSize = new Vector2(620, 0);

        // Cabecera: qué máquina es y en qué estado está, con forma y texto además de color.
        var title = Ui.Heading(Loc.T("machine_" + m.Def));
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Add(Ui.Row(UiTheme.M, title, Ui.Icon(Ui.Glyph(Ui.StatusGlyph(m.Status)), 24), Ui.Text(Loc.T("status_" + m.Status), UiTheme.Body, color)));
        var cause = CauseText(readout);
        if (cause.Length > 0) Add(Ui.Para(cause, UiTheme.Body, color));
        Add(new HSeparator());

        if (def.Kind == MachineKind.Production)
        {
            var r = w.Content.Recipes[def.Recipe!];
            // Receta como flujo: entradas, flecha, salidas y duración de ciclo.
            var flow = Ui.Row(UiTheme.M);
            foreach (var (item, qty) in r.Inputs) flow.AddChild(Ui.Stack(item, $"{qty}×"));
            if (r.Inputs.Count == 0) flow.AddChild(Ui.Text(Loc.T("panel_from_ground"), UiTheme.Small, Ui.Dim));
            flow.AddChild(Ui.Text("→", UiTheme.Heading, Ui.Amber));
            foreach (var (item, qty) in r.Outputs) flow.AddChild(Ui.Stack(item, $"{qty}×"));
            Add(Ui.Row(UiTheme.M, Ui.Text(Loc.T("panel_recipe"), UiTheme.Small, Ui.Dim)));
            Add(flow);

            // Progreso: el servidor replica al cambiar de estado; entre medias se estima con la velocidad real.
            if (_meterId != id || _meterProgress != m.Progress)
            {
                _meterId = id;
                _meterProgress = m.Progress;
                _meterTime = _elapsed;
            }
            var total = r.Ticks * 1000.0;
            var estimate = m.Progress / total + (m.Status is MachineStatus.Working or MachineStatus.LowPower or MachineStatus.Overheated ? (_elapsed - _meterTime) / Math.Max(0.1, readout.CycleSeconds) : 0);
            estimate = m.Processing ? Math.Clamp(estimate, 0, 1) : 0;
            var bar = Ui.Bar(estimate, 1, stopped ? Ui.Warn : Ui.Good);
            Add(Ui.Row(UiTheme.M, Ui.Text(Loc.T("panel_progress"), UiTheme.Small, Ui.Dim), bar, Ui.Text($"{estimate * 100:0} %  ·  {readout.CycleSeconds:0.#} s", UiTheme.Small, Ui.Ink)));

            var buffers = new GridContainer { Columns = 2 };
            buffers.AddThemeConstantOverride("h_separation", UiTheme.XL);
            buffers.AddThemeConstantOverride("v_separation", UiTheme.S);
            buffers.AddChild(Ui.Text(Loc.T("panel_input"), UiTheme.Small, Ui.Dim));
            buffers.AddChild(Ui.Text(Loc.T("panel_output"), UiTheme.Small, Ui.Dim));
            var inBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var outBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var (item, _) in r.Inputs) inBox.AddChild(Ui.Stack(item, $"{m.In.Get(item)}", 24));
            if (r.Inputs.Count == 0) inBox.AddChild(Ui.Text("—", UiTheme.Body, Ui.Dim));
            foreach (var (item, _) in r.Outputs) outBox.AddChild(Ui.Stack(item, $"{m.Out.Get(item)} / {w.Content.Balance.OutputBuffer}", 24));
            buffers.AddChild(inBox);
            buffers.AddChild(outBox);
            Add(buffers);

            // Rendimiento: teórica de esta máquina frente a la real medida en el mundo.
            Add(new HSeparator());
            Add(Ui.Text(Loc.T("panel_rate_title"), UiTheme.Small, Ui.Dim));
            foreach (var (item, perMinute) in readout.Theoretical)
            {
                var producers = Math.Max(1, w.ProducersOf(item));
                var measured = MeasuredPerMinute(item);
                var real = measured == null ? Loc.T("panel_rate_measuring") : $"{measured / producers:0.#}";
                var note = producers > 1 ? Loc.T("panel_rate_shared", producers.ToString()) : "";
                var perMachine = measured / producers;
                var rateColor = perMachine == null ? Ui.Dim : perMachine >= perMinute * 0.9 ? Ui.Good : Ui.Warn;
                var row = Ui.Row(UiTheme.M, Ui.Icon(ItemVisuals.IconOf(item), 24), Ui.Text($"{Loc.T("panel_rate_real")}: {real}/min", UiTheme.Body, rateColor), Ui.Text($"{Loc.T("panel_rate_theory")}: {perMinute:0.#}/min", UiTheme.Small, Ui.Dim));
                if (note.Length > 0) row.AddChild(Ui.Text(note, UiTheme.Caption, Ui.Dim));
                Add(row);
            }
        }
        else if (def.Kind is not (MachineKind.Generator or MachineKind.Shuttle))
        {
            Add(Ui.Text(Loc.T("panel_capacity", def.Capacity.ToString()), UiTheme.Small, Ui.Dim));
            var content = new HFlowContainer();
            content.AddThemeConstantOverride("h_separation", UiTheme.L);
            foreach (var kv in m.In.Items.ToList()) content.AddChild(Ui.Stack(kv.Key, $"{kv.Value}", 28));
            if (content.GetChildCount() == 0) content.AddChild(Ui.Text(Loc.T("hud_empty"), UiTheme.Body, Ui.Dim));
            Add(content);
        }

        // Energía y calor de la máquina y de su red.
        if (def.Power > 0 || def.PowerGen > 0 || def.Heat > 0 || def.Cooling > 0)
        {
            Add(new HSeparator());
            var energy = Ui.Row(UiTheme.M, Ui.Icon(Ui.Glyph("energy"), 22));
            if (def.Power > 0) energy.AddChild(Ui.Text($"{Loc.T("panel_power_use")}: {def.Power}", UiTheme.Body));
            if (def.PowerGen > 0) energy.AddChild(Ui.Text($"{Loc.T("panel_power_gen")}: {def.PowerGen}", UiTheme.Body, Ui.Good));
            energy.AddChild(Ui.Text($"{Loc.T("panel_grid")}: {readout.PowerDemand} / {readout.PowerSupply}", UiTheme.Small, readout.PowerDemand > readout.PowerSupply ? Ui.Bad : Ui.Dim));
            if (def.Heat > 0) energy.AddChild(Ui.Text($"{Loc.T("panel_heat")}: {def.Heat}", UiTheme.Small, Ui.Dim));
            if (def.Cooling > 0) energy.AddChild(Ui.Text($"{Loc.T("panel_cooling")}: {def.Cooling}", UiTheme.Small, Ui.Dim));
            Add(energy);
        }
        Add(new HSeparator());

        if (def.Kind == MachineKind.Station)
        {
            Add(Ui.Text(Loc.T(m.StationUnload ? "station_mode_unload" : "station_mode_load"), 17, Ui.Ink));
            Add(Ui.Btn(Loc.T("station_toggle"), () => client.Send(new Command { Type = CommandType.StationMode, A = id, B = m.StationUnload ? 0 : 1 })));
            foreach (var other in w.Machines.Values.Where(o => o.Id != id && o.Planet == m.Planet && w.Content.Machines[o.Def].Kind == MachineKind.Station).ToList())
            {
                var (ox, oz) = w.MachineCenterMm(other);
                var otherId = other.Id;
                var affordTrain = w.CanAfford(client.LocalPlayer!, w.Content.Balance.Rail.TrainCredits, w.Content.Balance.Rail.TrainCost);
                Add(Ui.Btn($"{Loc.T("station_train_to")} ({ox / 1000}, {oz / 1000}) — {Ui.Cost(w.Content.Balance.Rail.TrainCredits, w.Content.Balance.Rail.TrainCost)}{(affordTrain ? "" : " · " + Loc.T("cost_missing"))}",
                    () => client.Send(new Command { Type = CommandType.CreateTrain, A = id, B = otherId })));
            }
            foreach (var t in w.Trains.Values.Where(t => t.StationA == id || t.StationB == id).ToList())
            {
                var trainId = t.Id;
                var row = new HBoxContainer();
                var reason = t.WaitReason.Length > 0 ? $": {Loc.T(t.WaitReason)}" : "";
                row.AddChild(Ui.Text($"{Loc.T("train_label")} — {Loc.T("train_state_" + t.State)}{reason} · {Ui.Amounts(t.Cargo.Items)}", 16));
                row.AddChild(Ui.Btn(Loc.T("train_remove"), () => client.Send(new Command { Type = CommandType.RemoveTrain, A = trainId })));
                Add(row);
            }
        }

        if (def.Kind == MachineKind.CargoPad)
        {
            Add(Ui.Text(Loc.T(m.StationUnload ? "pad_mode_receive" : "pad_mode_send"), 17, Ui.Ink));
            Add(Ui.Btn(Loc.T("station_toggle"), () => client.Send(new Command { Type = CommandType.StationMode, A = id, B = m.StationUnload ? 0 : 1 })));
            Add(Ui.Text($"{Loc.T("pad_in_flight")}: {w.Shipments.Count}", 16, Ui.Dim));
        }

        if (def.Kind == MachineKind.Shuttle)
        {
            if (!w.Upgrades.Contains("tech_shuttle")) Add(Ui.Text(Loc.T("shuttle_locked"), 16, Ui.Bad));
            else
                foreach (var planet in w.Content.Planets.Where(pl => pl.Id != m.Planet))
                {
                    var destination = planet.Id;
                    Add(Ui.Btn($"{Loc.T("shuttle_go")} {Loc.T("planet_" + planet.Id)}", () => client.Send(new Command { Type = CommandType.Travel, A = destination })));
                }
        }

        if (def.Kind == MachineKind.Terminal)
        {
            var row = new HBoxContainer();
            row.AddChild(Ui.Btn(Loc.T("panel_sell_all"), () =>
            {
                foreach (var kv in m.In.Items.ToList())
                    client.Send(new Command { Type = CommandType.Sell, A = id, Text = kv.Key, Amount = kv.Value });
                Sfx.Play("sell");
            }));
            row.AddChild(Ui.Btn($"{Loc.T("panel_auto_sell")}: {(m.AutoSell ? "ON" : "OFF")}", () =>
                client.Send(new Command { Type = CommandType.SetAutoSell, A = id, B = m.AutoSell ? 0 : 1 })));
            Add(row);
            foreach (var kv in m.In.Items.ToList())
            {
                var item = kv.Key;
                var reserve = m.Reserves.GetValueOrDefault(item);
                var rr = new HBoxContainer();
                rr.AddChild(Ui.Text($"{Loc.T("panel_reserve")} {Loc.T("item_" + item)}: {reserve}", 16));
                rr.AddChild(Ui.Btn("−10", () => client.Send(new Command { Type = CommandType.SetAutoSell, A = id, B = m.AutoSell ? 1 : 0, Text = item, Amount = Math.Max(0, reserve - 10) })));
                rr.AddChild(Ui.Btn("+10", () => client.Send(new Command { Type = CommandType.SetAutoSell, A = id, B = m.AutoSell ? 1 : 0, Text = item, Amount = reserve + 10 })));
                Add(rr);
            }
        }

        if (def.Kind is not (MachineKind.Generator or MachineKind.Shuttle or MachineKind.Radiator))
        {
            var source = def.Kind == MachineKind.Production ? m.Out : m.In;
            var me = client.LocalPlayer!;
            var transfer = new HFlowContainer { CustomMinimumSize = new Vector2(560, 0) };
            foreach (var kv in source.Items.ToList())
            {
                var item = kv.Key;
                transfer.AddChild(Ui.Btn($"{Loc.T("transfer_take")}: {Loc.T("item_" + item)}", () => client.Send(new Command { Type = CommandType.Extract, A = id, Text = item })));
            }
            foreach (var kv in me.Bag.Items.ToList())
            {
                var item = kv.Key;
                if (w.InputRoom(m, item) <= 0) continue;
                transfer.AddChild(Ui.Btn($"{Loc.T("transfer_put")}: {Loc.T("item_" + item)}", () => client.Send(new Command { Type = CommandType.Insert, A = id, Text = item })));
            }
            if (transfer.GetChildCount() > 0) Add(transfer);
        }

        foreach (var link in w.Links.Values.Where(l => l.From == id || l.To == id).ToList())
        {
            var otherId = link.From == id ? link.To : link.From;
            if (!w.Machines.TryGetValue(otherId, out var other)) continue;
            var arrow = link.From == id ? "→" : "←";
            var row = new HBoxContainer();
            row.AddChild(Ui.Text($"{Loc.T("tool_link")} {arrow} {Loc.T("machine_" + other.Def)}", 16));
            row.AddChild(Ui.Btn("✕", () => client.Send(new Command { Type = CommandType.Unlink, A = link.Id })));
            Add(row);
        }
        Add(Ui.Btn(Loc.T("panel_close"), ClosePanel));
    }

    /// <summary>Convierte la lectura en una frase accionable: qué falta y qué hacer.</summary>
    private static string CauseText(MachineReadout r)
    {
        var name = r.CauseItem.Length > 0 ? Loc.T("item_" + r.CauseItem) : "";
        return r.Cause switch
        {
            StopCause.MissingInput => Loc.T(r.LinksIn == 0 ? "cause_input_nolink" : "cause_input").Replace("{0}", name).Replace("{1}", $"{r.CauseHave}/{r.CauseNeed}"),
            StopCause.OutputFull => Loc.T(r.LinksOut == 0 ? "cause_output_nolink" : "cause_output").Replace("{0}", name).Replace("{1}", $"{r.CauseHave}/{r.CauseNeed}"),
            StopCause.NoPower => Loc.T("cause_nopower").Replace("{1}", $"{r.PowerDemand}/{r.PowerSupply}"),
            StopCause.LowPower => Loc.T("cause_lowpower").Replace("{1}", $"{r.PowerDemand}/{r.PowerSupply}"),
            StopCause.Overheated => Loc.T("cause_heat"),
            _ => "",
        };
    }

    public void OpenPlayers()
    {
        Open(PlayersPanel);
    }

    private void PlayersPanel()
    {
        var client = _view.Session.Client;
        var w = client.World!;
        var me = client.LocalPlayer!;
        Clear();
        _centerBody.CustomMinimumSize = new Vector2(760, 0);
        Add(Ui.Heading($"{Loc.T("hud_players")}  {w.Players.Values.Count(p => p.Connected)}/12"));
        Add(Ui.Para($"{Loc.T("player_your_role")}: {Loc.T("role_" + me.Role)}. {Loc.T("perm_" + me.Role)}", UiTheme.Small, Ui.Dim));
        Add(new HSeparator());
        foreach (var p in w.Players.Values.OrderByDescending(p => p.Connected).ThenBy(p => p.Name).ToList())
        {
            var key = p.Key;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTheme.M);
            var place = p.TransitTo >= 0 ? Loc.T("shuttle_title") + " " + Loc.T("planet_" + p.TransitTo) : Loc.T("planet_" + p.Planet);
            // Conectado se indica con icono y texto, no solo con tono.
            row.AddChild(Ui.Icon(Ui.Glyph(p.Connected ? "st_ok" : "st_wait"), 20));
            var who = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            who.AddThemeConstantOverride("separation", 0);
            who.AddChild(Ui.Text(p.Key == me.Key ? $"{p.Name}  ({Loc.T("player_you")})" : p.Name, UiTheme.Body, p.Connected ? Ui.Ink : Ui.Dim));
            who.AddChild(Ui.Text($"{Loc.T("role_" + p.Role)}  ·  {(p.Connected ? place : Loc.T("player_offline"))}", UiTheme.Small, Ui.Dim));
            row.AddChild(who);
            if (me.Role >= Role.Admin && p.Key != me.Key && p.Role < me.Role)
            {
                if (p.Role == Role.Visitor)
                    row.AddChild(Ui.Btn(Loc.T("player_allow_build"), () => client.Send(new Command { Type = CommandType.SetRole, Text = key, A = (int)Role.Builder })));
                else if (p.Role == Role.Builder)
                    row.AddChild(Ui.Btn(Loc.T("player_view_only"), () => client.Send(new Command { Type = CommandType.SetRole, Text = key, A = (int)Role.Visitor })));
                if (me.Role == Role.Owner && p.Role == Role.Builder)
                    row.AddChild(Ui.Btn(Loc.T("player_make_admin"), () => client.Send(new Command { Type = CommandType.SetRole, Text = key, A = (int)Role.Admin })));
                if (me.Role == Role.Owner && p.Role == Role.Admin)
                    row.AddChild(Ui.Btn(Loc.T("player_remove_admin"), () => client.Send(new Command { Type = CommandType.SetRole, Text = key, A = (int)Role.Builder })));
                if (p.Connected)
                    row.AddChild(Ui.Btn(Loc.T("player_kick"), () => client.Send(new Command { Type = CommandType.Kick, Text = key })));
            }
            Add(row);
        }
        if (me.Role >= Role.Admin && w.Content.Events.Count > 0)
        {
            Add(new HSeparator());
            foreach (var e in w.Content.Events.Keys)
            {
                var id = e;
                var active = w.ActiveEvents.Contains(id);
                Add(Ui.Btn($"{Loc.T("event_" + id)}: {Loc.T(active ? "event_on" : "event_off")}", () => client.Send(new Command { Type = CommandType.SetEvent, Text = id, A = active ? 0 : 1 })));
            }
        }
        if (w.Cosmetics.Count > 0) Add(new HSeparator());
        foreach (var c in w.Cosmetics)
        {
            var id = c;
            var worn = w.EquippedCosmetics.Contains(id);
            Add(Ui.Btn($"{Loc.T("cosmetic_" + id)}: {Loc.T(worn ? "cosmetic_on" : "cosmetic_off")}", () => client.Send(new Command { Type = CommandType.SetCosmetic, Text = id, A = worn ? 0 : 1 })));
        }
        Add(Ui.Btn(Loc.T("panel_close"), ClosePanel));
    }

    public void OpenTech()
    {
        Open(TechPanel);
    }

    private void TechPanel()
    {
        var client = _view.Session.Client;
        var w = client.World!;
        var me = client.LocalPlayer!;
        Clear();
        _centerBody.CustomMinimumSize = new Vector2(1160, 0);
        Add(Ui.Heading(Loc.T("tech_title")));
        var viewportHeight = GetViewportRect().Size.Y;
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(1160, Mathf.Max(320, viewportHeight - 240)), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var columns = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", UiTheme.XL);
        scroll.AddChild(columns);
        Add(scroll);
        scroll.ScrollVertical = (int)_techScroll;
        scroll.GetVScrollBar().ValueChanged += v => _techScroll = v;

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddThemeConstantOverride("separation", UiTheme.M);
        columns.AddChild(left);
        left.AddChild(Ui.Text(Loc.T("tech_contracts"), UiTheme.Heading, Ui.Ink));
        foreach (var c in w.Content.Contracts.Values)
        {
            // Un contrato de evento solo se ofrece con el evento activo, salvo que ya esté cumplido.
            if (c.Event != null && !w.ActiveEvents.Contains(c.Event) && !w.ContractsDone.Contains(c.Id)) continue;
            var done = w.ContractsDone.Contains(c.Id);
            var locked = c.Requires != null && !w.Upgrades.Contains(c.Requires);
            var id = c.Id;
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", UiTheme.S);
            var name = Ui.Text(Loc.T("contract_" + c.Id), UiTheme.Body, done ? Ui.Dim : Ui.Ink);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.AddChild(Ui.Row(UiTheme.M, name, Ui.Icon(Ui.Glyph("credit"), 18), Ui.Text($"+{c.Credits} ¤", UiTheme.Body, done ? Ui.Dim : Ui.Warn)));
            var ready = !done && !locked;
            // Progreso real: lo que ya hay disponible para entregar, objeto a objeto.
            foreach (var (item, qty) in c.Items)
            {
                var have = done ? qty : Math.Min(qty, w.ContractStock(me, item));
                if (have < qty) ready = false;
                var line = Ui.Row(UiTheme.M, Ui.Icon(ItemVisuals.IconOf(item), 24), Ui.Text(Loc.T("item_" + item), UiTheme.Small, Ui.Dim), Ui.Bar(have, qty, have >= qty ? Ui.Good : Ui.Amber, 8), Ui.Text($"{have}/{qty}", UiTheme.Small, have >= qty ? Ui.Good : Ui.Ink));
                card.AddChild(line);
            }
            foreach (var (stat, qty) in c.NeedStats)
            {
                var have = Math.Min(qty, w.Stats.GetValueOrDefault(stat));
                if (have < qty) ready = false;
                card.AddChild(Ui.Row(UiTheme.M, Ui.Text(Loc.T("stat_" + stat), UiTheme.Small, Ui.Dim), Ui.Bar(have, qty, have >= qty ? Ui.Good : Ui.Amber, 8), Ui.Text($"{have}/{qty}", UiTheme.Small, have >= qty ? Ui.Good : Ui.Ink)));
            }
            if (c.Event != null && !done) card.AddChild(Ui.Para(Loc.T("event_hint"), UiTheme.Caption, Ui.Amber));
            if (locked) card.AddChild(Ui.Para($"{Loc.T("tech_requires")}: {Loc.T("upgrade_" + c.Requires)}", UiTheme.Small, Ui.Warn));
            var b = Ui.Btn(done ? Loc.T("tech_done") : locked ? Loc.T("tech_locked") : ready ? Loc.T("tech_deliver") : Loc.T("tech_gather"),
                () => client.Send(new Command { Type = CommandType.FulfillContract, Text = id }));
            b.Disabled = done || !ready;
            card.AddChild(b);
            left.AddChild(Card(card));
        }

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", UiTheme.M);
        columns.AddChild(right);
        right.AddChild(Ui.Text(Loc.T("tech_upgrades"), UiTheme.Heading, Ui.Ink));
        foreach (var u in w.Content.Upgrades.Values)
        {
            var done = w.Upgrades.Contains(u.Id);
            var locked = u.Requires != null && !w.Upgrades.Contains(u.Requires);
            var afford = w.CanAfford(me, u.Credits, u.Cost);
            var id = u.Id;
            var card = new VBoxContainer();
            card.AddThemeConstantOverride("separation", UiTheme.S);
            card.AddChild(Ui.Text(Loc.T("upgrade_" + u.Id), UiTheme.Body, done ? Ui.Dim : Ui.Ink));
            card.AddChild(Ui.Para(Loc.T("upgrade_" + u.Id + "_d"), UiTheme.Small, Ui.Dim));
            if (locked) card.AddChild(Ui.Para($"{Loc.T("tech_requires")}: {Loc.T("upgrade_" + u.Requires)}", UiTheme.Small, Ui.Warn));
            var b = Ui.Btn(done ? Loc.T("tech_done") : locked ? Loc.T("tech_locked") : $"{Loc.T("tech_buy")} — {Ui.Cost(u.Credits, u.Cost)}{(afford ? "" : " · " + Loc.T("cost_missing"))}",
                () => client.Send(new Command { Type = CommandType.BuyUpgrade, Text = id }));
            b.Disabled = done || locked || !afford;
            card.AddChild(b);
            right.AddChild(Card(card));
        }
        Add(Ui.Btn(Loc.T("panel_close"), ClosePanel));
    }

    private double _techScroll;

    private static PanelContainer Card(Control body)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.08f, 0.1f, 0.135f, 0.8f), new Color(0.25f, 0.3f, 0.37f, 0.6f), 1, UiTheme.L, UiTheme.M));
        card.AddChild(body);
        return card;
    }
}
