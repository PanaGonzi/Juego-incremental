using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

public static class Ui
{
    public static readonly Color Amber = new(1f, 0.67f, 0.25f);
    public static readonly Color Ink = new(0.9f, 0.93f, 0.97f);
    public static readonly Color Dim = new(0.6f, 0.68f, 0.76f);
    public static readonly Color Good = new(0.5f, 0.92f, 0.65f);
    public static readonly Color Bad = new(1f, 0.5f, 0.42f);

    private static Theme? _theme;

    public static Theme Theme
    {
        get
        {
            if (_theme != null) return _theme;
            var t = new Theme { DefaultFontSize = 18 };
            t.SetStylebox("panel", "PanelContainer", Box(new Color(0.05f, 0.07f, 0.11f, 0.88f), new Color(0.3f, 0.38f, 0.48f, 0.7f)));
            t.SetStylebox("normal", "Button", Box(new Color(0.12f, 0.16f, 0.22f), new Color(0.32f, 0.4f, 0.5f)));
            t.SetStylebox("hover", "Button", Box(new Color(0.2f, 0.25f, 0.33f), Amber));
            t.SetStylebox("pressed", "Button", Box(new Color(0.34f, 0.24f, 0.1f), Amber));
            t.SetStylebox("disabled", "Button", Box(new Color(0.08f, 0.1f, 0.13f), new Color(0.2f, 0.24f, 0.3f)));
            t.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Amber));
            t.SetColor("font_color", "Button", Ink);
            t.SetColor("font_disabled_color", "Button", new Color(0.45f, 0.5f, 0.56f));
            t.SetColor("font_color", "Label", Ink);
            t.SetStylebox("normal", "LineEdit", Box(new Color(0.03f, 0.05f, 0.08f), new Color(0.32f, 0.4f, 0.5f)));
            _theme = t;
            return t;
        }
    }

    private static StyleBoxFlat Box(Color bg, Color border)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 7,
            ContentMarginBottom = 7,
        };
        s.SetBorderWidthAll(1);
        s.SetCornerRadiusAll(3);
        return s;
    }

    public static Label Text(string text, int size = 18, Color? color = null)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color != null) l.AddThemeColorOverride("font_color", color.Value);
        return l;
    }

    public static Button Btn(string text, Action action, int minWidth = 0)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 0) };
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
    private Label _credits = null!, _power = null!, _income = null!, _objective = null!, _bag = null!, _prompt = null!, _status = null!, _players = null!, _chatLog = null!, _toast = null!;
    private readonly List<PanelContainer> _toolSlots = new();
    private readonly List<(Label label, string key)> _toolNames = new();
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
        var tl = new VBoxContainer();
        topLeft.AddChild(tl);
        _credits = Ui.Text("", 26, Ui.Amber);
        _income = Ui.Text("", 16, Ui.Dim);
        _power = Ui.Text("", 16);
        tl.AddChild(_credits);
        tl.AddChild(_income);
        tl.AddChild(_power);

        var top = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        top.GrowHorizontal = GrowDirection.Both;
        top.OffsetTop = 16;
        AddChild(top);
        var topBox = new VBoxContainer();
        top.AddChild(topBox);
        topBox.AddChild(Ui.Text(Loc.T("hud_objective").ToUpperInvariant(), 13, Ui.Amber));
        _objective = Ui.Text("", 19);
        _objective.AutowrapMode = TextServer.AutowrapMode.Off;
        topBox.AddChild(_objective);

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

        var (supply, demand) = EstimatePower(w, me.Planet);
        _power.Text = $"{Loc.T("hud_power")}: {demand} / {supply}";
        _power.AddThemeColorOverride("font_color", demand > supply ? Ui.Bad : Ui.Good);
        if (w.Content.Planets.First(pl => pl.Id == me.Planet).Thermal)
        {
            var (heat, cooling) = EstimateHeat(w, me.Planet);
            _power.Text += $"\n{Loc.T("hud_heat")}: {heat} / {cooling}";
            if (heat > cooling) _power.AddThemeColorOverride("font_color", Ui.Bad);
        }

        _objective.Text = w.ObjectiveIndex < w.Content.Objectives.Count
            ? Loc.T("objective_" + w.Content.Objectives[w.ObjectiveIndex].Id) + (_view.ObjectiveDistance > 7 ? $"   ·   {_view.ObjectiveDistance:0} m" : "")
            : Loc.T("objective_done");

        var bag = Ui.Amounts(me.Bag.Items);
        _bag.Text = $"{Loc.T("hud_bag")}: {(me.Bag.Kinds == 0 ? Loc.T("hud_empty") : "\n" + bag.Replace(", ", "\n"))}";

        var list = w.Players.Values.Where(p => p.Connected).ToList();
        var mode = _view.Session.IsSolo ? "" : $"  ·  {client.RoundTripMs} ms";
        _players.Text = $"{Loc.T("hud_players")} {list.Count}/12{mode}\n" + string.Join("\n", list.Select(p => $"{p.Name} ({Loc.T("role_" + p.Role)})"));

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
        Add(Ui.Text(Loc.T("machine_" + m.Def), 26, Ui.Amber));
        var color = m.Status is MachineStatus.NoInput or MachineStatus.OutputFull or MachineStatus.NoPower or MachineStatus.LowPower or MachineStatus.Overheated ? Ui.Bad : Ui.Good;
        if (def.Heat > 0) Add(Ui.Text($"{Loc.T("panel_heat")}: {def.Heat}", 16, Ui.Dim));
        if (def.Cooling > 0) Add(Ui.Text($"{Loc.T("panel_cooling")}: {def.Cooling}", 16, Ui.Dim));
        Add(Ui.Text($"{Loc.T("panel_status")}: {Loc.T("status_" + m.Status)}", 18, color));
        if (def.Power > 0) Add(Ui.Text($"{Loc.T("panel_power_use")}: {def.Power}", 16, Ui.Dim));
        if (def.PowerGen > 0) Add(Ui.Text($"{Loc.T("panel_power_gen")}: {def.PowerGen}", 16, Ui.Dim));

        if (def.Kind == MachineKind.Production)
        {
            var r = w.Content.Recipes[def.Recipe!];
            var rate = r.Ticks / (float)w.Content.Balance.TicksPerSecond;
            Add(Ui.Text($"{Loc.T("panel_recipe")}: {Ui.Amounts(r.Inputs)} → {Ui.Amounts(r.Outputs)}  ({rate:0.#} s)", 16, Ui.Dim));
            Add(Ui.Text($"{Loc.T("panel_input")}: {Ui.Amounts(m.In.Items)}", 17));
            Add(Ui.Text($"{Loc.T("panel_output")}: {Ui.Amounts(m.Out.Items)}", 17));
        }
        else if (def.Kind is not (MachineKind.Generator or MachineKind.Shuttle))
        {
            Add(Ui.Text(Loc.T("panel_capacity", def.Capacity.ToString()), 17));
            Add(Ui.Text(Ui.Amounts(m.In.Items), 17));
        }

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
        Add(Ui.Text(Loc.T("hud_players"), 26, Ui.Amber));
        foreach (var p in w.Players.Values.OrderByDescending(p => p.Connected).ThenBy(p => p.Name).ToList())
        {
            var key = p.Key;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var place = p.TransitTo >= 0 ? Loc.T("shuttle_title") + " " + Loc.T("planet_" + p.TransitTo) : Loc.T("planet_" + p.Planet);
            var state = p.Connected ? place : Loc.T("player_offline");
            var label = Ui.Text($"{p.Name} — {Loc.T("role_" + p.Role)} — {state}", 17, p.Connected ? Ui.Ink : Ui.Dim);
            label.CustomMinimumSize = new Vector2(520, 0);
            row.AddChild(label);
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
        if (me.Role >= Role.Admin)
        {
            foreach (var e in w.Content.Events.Keys)
            {
                var id = e;
                var active = w.ActiveEvents.Contains(id);
                Add(Ui.Btn($"{Loc.T("event_" + id)}: {Loc.T(active ? "event_on" : "event_off")}", () => client.Send(new Command { Type = CommandType.SetEvent, Text = id, A = active ? 0 : 1 })));
            }
        }
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
        Add(Ui.Text(Loc.T("tech_title"), 26, Ui.Amber));
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        Add(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(500, 0) };
        columns.AddChild(left);
        left.AddChild(Ui.Text(Loc.T("tech_contracts"), 20, Ui.Ink));
        foreach (var c in w.Content.Contracts.Values)
        {
            // Un contrato de evento solo se ofrece con el evento activo, salvo que ya esté cumplido.
            if (c.Event != null && !w.ActiveEvents.Contains(c.Event) && !w.ContractsDone.Contains(c.Id)) continue;
            var done = w.ContractsDone.Contains(c.Id);
            var locked = c.Requires != null && !w.Upgrades.Contains(c.Requires);
            var id = c.Id;
            left.AddChild(Ui.Text($"{Loc.T("contract_" + c.Id)}  (+{c.Credits} ¤)", 17, done ? Ui.Dim : Ui.Ink));
            left.AddChild(Ui.Text(Ui.Amounts(c.Items), 15, Ui.Dim));
            foreach (var (stat, qty) in c.NeedStats)
                left.AddChild(Ui.Text($"{Loc.T("stat_" + stat)}: {Math.Min(qty, w.Stats.GetValueOrDefault(stat))}/{qty}", 15, w.Stats.GetValueOrDefault(stat) >= qty ? Ui.Good : Ui.Dim));
            if (c.Event != null && !done) left.AddChild(Ui.Text(Loc.T("event_hint"), 14, Ui.Amber));
            var b = Ui.Btn(done ? Loc.T("tech_done") : locked ? Loc.T("tech_locked") : Loc.T("tech_deliver"),
                () => client.Send(new Command { Type = CommandType.FulfillContract, Text = id }));
            b.Disabled = done || locked;
            left.AddChild(b);
        }

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(540, 0) };
        columns.AddChild(right);
        right.AddChild(Ui.Text(Loc.T("tech_upgrades"), 20, Ui.Ink));
        foreach (var u in w.Content.Upgrades.Values)
        {
            var done = w.Upgrades.Contains(u.Id);
            var locked = u.Requires != null && !w.Upgrades.Contains(u.Requires);
            var afford = w.CanAfford(me, u.Credits, u.Cost);
            var id = u.Id;
            right.AddChild(Ui.Text(Loc.T("upgrade_" + u.Id), 17, done ? Ui.Dim : Ui.Ink));
            right.AddChild(Ui.Text(Loc.T("upgrade_" + u.Id + "_d"), 14, Ui.Dim));
            var b = Ui.Btn(done ? Loc.T("tech_done") : locked ? Loc.T("tech_locked") : $"{Loc.T("tech_buy")} — {Ui.Cost(u.Credits, u.Cost)}{(afford ? "" : " · " + Loc.T("cost_missing"))}",
                () => client.Send(new Command { Type = CommandType.BuyUpgrade, Text = id }));
            b.Disabled = done || locked;
            if (!done && !locked && !afford) b.Modulate = new Color(1, 0.75f, 0.7f);
            right.AddChild(b);
        }
        Add(Ui.Btn(Loc.T("panel_close"), ClosePanel));
    }
}
