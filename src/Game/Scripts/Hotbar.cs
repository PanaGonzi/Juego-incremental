using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>Iconos de herramientas dibujados por código, para no depender de texturas externas.</summary>
public partial class ToolGlyph : Control
{
    private readonly string _tool;

    public ToolGlyph(string tool)
    {
        _tool = tool;
        MouseFilter = MouseFilterEnum.Ignore;
        CustomMinimumSize = new Vector2(44, 44);
    }

    public override void _Draw()
    {
        var s = Mathf.Min(Size.X, Size.Y);
        var o = (Size - new Vector2(s, s)) / 2;
        Vector2 P(float x, float y) => o + new Vector2(x, y) * s;
        var ink = Ui.Ink;
        var amber = Ui.Amber;
        switch (_tool)
        {
            case "cutter":
                // Disco dentado, cuerpo y empuñadura.
                for (var i = 0; i < 12; i++)
                {
                    var a = i / 12f * Mathf.Tau;
                    DrawLine(P(0.3f, 0.32f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.16f, P(0.3f, 0.32f) + new Vector2(Mathf.Cos(a + 0.2f), Mathf.Sin(a + 0.2f)) * s * 0.25f, ink, s * 0.035f, true);
                }
                DrawCircle(P(0.3f, 0.32f), s * 0.17f, ink);
                DrawCircle(P(0.3f, 0.32f), s * 0.06f, new Color(0.08f, 0.1f, 0.14f));
                DrawLine(P(0.42f, 0.38f), P(0.86f, 0.38f), amber, s * 0.15f);
                DrawLine(P(0.72f, 0.42f), P(0.68f, 0.82f), ink, s * 0.11f);
                break;
            case "link":
                // Tramo de cinta con objetos.
                DrawLine(P(0.1f, 0.34f), P(0.9f, 0.34f), ink, s * 0.05f);
                DrawLine(P(0.1f, 0.68f), P(0.9f, 0.68f), ink, s * 0.05f);
                for (var i = 0; i < 4; i++) DrawCircle(P(0.18f + i * 0.21f, 0.68f), s * 0.05f, ink);
                DrawRect(new Rect2(P(0.2f, 0.42f), new Vector2(s * 0.18f, s * 0.2f)), amber);
                DrawRect(new Rect2(P(0.58f, 0.42f), new Vector2(s * 0.18f, s * 0.2f)), amber);
                break;
            case "remove":
                // Llave cruzada con una señal de retirada.
                DrawLine(P(0.2f, 0.2f), P(0.8f, 0.8f), Ui.Bad, s * 0.11f, true);
                DrawLine(P(0.8f, 0.2f), P(0.2f, 0.8f), Ui.Bad, s * 0.11f, true);
                DrawArc(P(0.5f, 0.5f), s * 0.42f, 0, Mathf.Tau, 32, ink, s * 0.045f, true);
                break;
            case "rail":
                // Dos carriles y traviesas en perspectiva.
                for (var i = 0; i < 5; i++)
                {
                    var t = i / 4f;
                    var y = Mathf.Lerp(0.14f, 0.86f, t);
                    var half = Mathf.Lerp(0.2f, 0.42f, t);
                    DrawLine(P(0.5f - half, y), P(0.5f + half, y), amber, s * 0.05f);
                }
                DrawLine(P(0.36f, 0.08f), P(0.2f, 0.92f), ink, s * 0.06f, true);
                DrawLine(P(0.64f, 0.08f), P(0.8f, 0.92f), ink, s * 0.06f, true);
                break;
        }
    }
}

/// <summary>Miniaturas de máquinas renderizadas una vez a partir de su modelo real. Se generan de una en una, sin frenar la partida.</summary>
public partial class MachineThumbnails : Node
{
    private static MachineThumbnails? _instance;
    private static readonly Dictionary<string, ImageTexture> Cache = new();
    private static readonly Queue<string> Pending = new();
    private SubViewport? _viewport;
    private string _current = "";
    private int _wait;

    public override void _EnterTree() => _instance = this;

    public override void _ExitTree()
    {
        if (_instance == this) _instance = null;
    }

    public static Texture2D Get(string def)
    {
        if (Cache.TryGetValue(def, out var texture)) return texture;
        var empty = Image.CreateEmpty(96, 96, false, Image.Format.Rgba8);
        texture = ImageTexture.CreateFromImage(empty);
        Cache[def] = texture;
        Pending.Enqueue(def);
        return texture;
    }

    public override void _Process(double delta)
    {
        if (_viewport != null)
        {
            if (--_wait > 0) return;
            var image = _viewport.GetTexture().GetImage();
            image.Resize(96, 96, Image.Interpolation.Lanczos);
            Cache[_current].SetImage(image);
            _viewport.QueueFree();
            _viewport = null;
            return;
        }
        if (Pending.Count == 0) return;
        _current = Pending.Dequeue();
        _viewport = Render(_current);
        AddChild(_viewport);
        _wait = 3;
    }

    private static SubViewport Render(string def)
    {
        var vp = new SubViewport
        {
            Size = new Vector2I(192, 192),
            OwnWorld3D = true,
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        var model = Models.Machine(def);
        var root = new Node3D();
        vp.AddChild(root);
        root.AddChild(new MeshInstance3D { Mesh = model.Body });
        if (model.Rotor != null) root.AddChild(new MeshInstance3D { Mesh = model.Rotor, Position = model.RotorPos });
        if (model.Piston != null) root.AddChild(new MeshInstance3D { Mesh = model.Piston, Position = model.PistonPos });
        var box = model.Body.GetAabb();
        var center = box.GetCenter();
        var radius = box.Size.Length() * 0.5f;
        var camera = new Camera3D
        {
            Fov = 28,
            Current = true,
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0, 0, 0, 0),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.72f, 0.78f, 0.9f),
                AmbientLightEnergy = 0.65f,
                TonemapMode = Godot.Environment.ToneMapper.Agx,
                TonemapExposure = 1.35f,
            },
        };
        vp.AddChild(camera);
        camera.Transform = new Transform3D(Basis.Identity, center + new Vector3(0.62f, 0.55f, 1f).Normalized() * (radius * 4.35f)).LookingAt(center, Vector3.Up);
        var sun = new DirectionalLight3D { LightEnergy = 1.5f, LightColor = new Color(1f, 0.95f, 0.88f) };
        vp.AddChild(sun);
        sun.Transform = new Transform3D(Basis.Identity, new Vector3(3, 6, 5)).LookingAt(Vector3.Zero, Vector3.Up);
        return vp;
    }
}

/// <summary>Aspecto y textos de lo que puede ir en una casilla de la barra.</summary>
public static class EntryInfo
{
    public static string ToolOf(string entry) => entry.StartsWith("tool:", StringComparison.Ordinal) ? entry[5..] : "";
    public static string DefOf(string entry) => entry.StartsWith("build:", StringComparison.Ordinal) ? entry[6..] : "";

    public static string Name(string entry)
    {
        var tool = ToolOf(entry);
        return tool.Length > 0 ? Loc.T("tool_" + tool) : Loc.T("machine_" + DefOf(entry));
    }

    public static string Describe(string entry, World w, PlayerState me)
    {
        var tool = ToolOf(entry);
        if (tool.Length > 0) return $"{Loc.T("tool_" + tool)}\n{Loc.T("tool_" + tool + "_d")}";
        var def = DefOf(entry);
        if (!w.Content.Machines.TryGetValue(def, out var d)) return "";
        return $"{Loc.T("machine_" + def)}\n{Ui.Cost(d.Credits, d.Cost)}\n{MissingText(w, me, d)}";
    }

    public static string MissingText(World w, PlayerState me, MachineDef d)
    {
        var parts = new List<string>();
        if (w.Credits < d.Credits) parts.Add($"{d.Credits - w.Credits} ¤");
        foreach (var (item, qty) in d.Cost)
        {
            var have = HaveOf(w, me, item);
            if (have < qty) parts.Add($"{qty - have} {Loc.T("item_" + item)}");
        }
        return parts.Count == 0 ? Loc.T("cost_ready") : Loc.T("cost_lacking", string.Join(", ", parts));
    }

    /// <summary>Lo disponible para construir: exactamente lo que el servidor puede cobrar.</summary>
    public static long HaveOf(World w, PlayerState me, string item) => w.PlanetStock(me, item);

    public static Control Icon(string entry, float size)
    {
        var tool = ToolOf(entry);
        if (tool.Length > 0)
            return new ToolGlyph(tool) { CustomMinimumSize = new Vector2(size, size) };
        return new TextureRect
        {
            Texture = MachineThumbnails.Get(DefOf(entry)),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(size, size),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
    }
}

/// <summary>Estilos compartidos por barra e inventario.</summary>
public static class SlotStyle
{
    public static StyleBoxFlat Box(Color bg, Color border, int width = 1, int radius = 4, int margin = 5)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        s.SetBorderWidthAll(width);
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(margin);
        return s;
    }

    public static readonly Color Slot = new(0.07f, 0.09f, 0.125f, 0.92f);
    public static readonly Color Line = new(0.27f, 0.33f, 0.42f, 0.85f);
}

public partial class HotbarSlot : PanelContainer
{
    private readonly GameView _view;
    public readonly int Index;
    private readonly Label _key, _name;
    private readonly Control _iconHolder;
    private string _entry = "\0";
    private bool _selected, _lacking;

    public HotbarSlot(GameView view, int index)
    {
        _view = view;
        Index = index;
        CustomMinimumSize = new Vector2(92, 92);
        MouseFilter = MouseFilterEnum.Ignore;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 1);
        AddChild(box);
        var top = new Control { CustomMinimumSize = new Vector2(0, 46), MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(top);
        _iconHolder = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _iconHolder.SetAnchorsPreset(LayoutPreset.FullRect);
        top.AddChild(_iconHolder);
        _key = Ui.Text("", 13, Ui.Amber);
        _key.Position = new Vector2(0, -2);
        _key.AddThemeConstantOverride("outline_size", 3);
        _key.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.8f));
        top.AddChild(_key);
        _name = Ui.Text("", 12);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _name.CustomMinimumSize = new Vector2(82, 32);
        _name.AddThemeConstantOverride("line_spacing", -2);
        box.AddChild(_name);
    }

    public void Display(string entry, bool selected, bool lacking, bool editing, string keyName)
    {
        MouseFilter = editing ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        Visible = entry.Length > 0 || editing;
        _key.Text = keyName;
        if (entry != _entry)
        {
            _entry = entry;
            foreach (var c in _iconHolder.GetChildren()) { _iconHolder.RemoveChild(c); c.QueueFree(); }
            if (entry.Length > 0) _iconHolder.AddChild(EntryInfo.Icon(entry, 44));
            _selected = !selected; // obliga a repintar
        }
        _name.Text = entry == HotbarLayout.Cutter ? Loc.T("tool_cutter_short") : entry.Length > 0 ? EntryInfo.Name(entry) : Loc.T("hotbar_empty");
        _name.Modulate = entry.Length > 0 ? Colors.White : new Color(1, 1, 1, 0.45f);
        if (selected != _selected || lacking != _lacking)
        {
            _selected = selected;
            _lacking = lacking;
            AddThemeStyleboxOverride("panel", SlotStyle.Box(
                selected ? new Color(0.22f, 0.16f, 0.07f, 0.95f) : SlotStyle.Slot,
                selected ? Ui.Amber : lacking ? new Color(0.7f, 0.33f, 0.28f, 0.9f) : SlotStyle.Line,
                selected ? 2 : 1));
        }
        TooltipText = entry.Length > 0 ? $"{EntryInfo.Name(entry)}\n{Loc.T("hotbar_tip")}" : Loc.T("hotbar_empty_tip");
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton { Pressed: false } button) return;
        if (button.ButtonIndex == MouseButton.Left && _entry.Length > 0)
        {
            _view.SelectHotbar(Index);
            AcceptEvent();
        }
        else if (button.ButtonIndex == MouseButton.Right)
        {
            _view.ClearHotbar(Index);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_entry.Length == 0 || Index == 0) return default;
        SetDragPreview(EntryInfo.Icon(_entry, 56));
        return new Godot.Collections.Dictionary { { "entry", _entry } };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Index > 0 && data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary().ContainsKey("entry");

    public override void _DropData(Vector2 atPosition, Variant data) =>
        _view.AssignHotbar(Index, data.AsGodotDictionary()["entry"].AsString());
}

/// <summary>Barra rápida inferior. Las casillas muestran funciones reales; las vacías solo aparecen al editar con el inventario abierto.</summary>
public partial class Hotbar : Control
{
    private readonly GameView _view;
    private readonly List<HotbarSlot> _slots = new();
    private double _refresh;
    private HBoxContainer _row = null!;

    /// <summary>Altura a la que empieza la barra, medida: los paneles centrales terminan antes.</summary>
    public float TopY => _row == null ? GetViewportRect().Size.Y - 104 : _row.GetGlobalRect().Position.Y;

    /// <summary>Borde derecho de la barra, medido, para que lo que va a su lado no la pise.</summary>
    public float RightX => _row == null ? GetViewportRect().Size.X / 2 : _row.GetGlobalRect().End.X;

    public Hotbar(GameView view)
    {
        _view = view;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        _row = row;
        row.AddThemeConstantOverride("separation", 6);
        AddChild(row);
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        row.GrowHorizontal = GrowDirection.Both;
        row.GrowVertical = GrowDirection.Begin;
        row.OffsetBottom = -12;
        for (var i = 0; i < HotbarLayout.Slots; i++)
        {
            var slot = new HotbarSlot(_view, i);
            _slots.Add(slot);
            row.AddChild(slot);
        }
        Refresh();
    }

    public override void _Process(double delta)
    {
        _refresh -= delta;
        if (_refresh > 0) return;
        _refresh = 0.12;
        Refresh();
    }

    public void Refresh()
    {
        var client = _view.Session.Client;
        var w = client.World;
        var me = client.LocalPlayer;
        var active = _view.ActiveEntry;
        var editing = _view.InventoryOpen;
        for (var i = 0; i < _slots.Count; i++)
        {
            var entry = _view.HotbarEntries[i];
            var lacking = false;
            if (w != null && me != null)
            {
                var def = EntryInfo.DefOf(entry);
                if (def.Length > 0 && w.Content.Machines.TryGetValue(def, out var d))
                    lacking = !w.CanAfford(me, d.Credits, d.Cost) || d.Unlock != null && !w.Upgrades.Contains(d.Unlock);
            }
            _slots[i].Display(entry, entry.Length > 0 && entry == active, lacking, editing, Controls.KeyName("hotbar_" + (i + 1)));
        }
    }
}

/// <summary>Objetos obtenidos: reacciona a lo que confirma el servidor en la mochila. Agrupa por objeto y nunca llena la pantalla.</summary>
public partial class PickupFeed : VBoxContainer
{
    private sealed class Row
    {
        public PanelContainer Root = null!;
        public Label Label = null!;
        public long Amount;
        public double Life;
        public float Punch;
    }

    private const int MaxRows = 5;
    private const double RowLife = 2.6;
    private readonly GameView _view;
    private readonly Dictionary<string, long> _last = new();
    private readonly Dictionary<string, Row> _rows = new();
    private readonly List<string> _order = new();
    private bool _primed;

    public PickupFeed(GameView view)
    {
        _view = view;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterRight);
        GrowHorizontal = GrowDirection.Begin;
        GrowVertical = GrowDirection.Both;
        OffsetRight = -18;
        Alignment = AlignmentMode.End;
        AddThemeConstantOverride("separation", 4);
    }

    public override void _Process(double delta)
    {
        var me = _view.Session.Client.LocalPlayer;
        if (me == null) return;
        foreach (var (item, qty) in me.Bag.Items)
        {
            var before = _last.GetValueOrDefault(item);
            if (_primed && qty > before) Gain(item, qty - before);
            _last[item] = qty;
        }
        foreach (var item in _last.Keys.Where(k => me.Bag.Get(k) == 0).ToList()) _last[item] = 0;
        _primed = true;

        foreach (var item in _order.ToList())
        {
            var row = _rows[item];
            row.Life -= delta;
            row.Punch = Mathf.MoveToward(row.Punch, 0f, (float)delta * 5f);
            var fadeIn = Mathf.Clamp((float)(RowLife - row.Life) * 8f, 0f, 1f);
            var fadeOut = Mathf.Clamp((float)row.Life * 2.5f, 0f, 1f);
            row.Root.Modulate = new Color(1 + row.Punch * 0.4f, 1 + row.Punch * 0.3f, 1, Mathf.Min(fadeIn, fadeOut));
            row.Root.Position = row.Root.Position with { X = (1f - fadeIn) * 40f };
            if (row.Life > 0) continue;
            row.Root.QueueFree();
            _rows.Remove(item);
            _order.Remove(item);
        }
    }

    private void Gain(string item, long amount)
    {
        if (_rows.TryGetValue(item, out var row))
        {
            row.Amount += amount;
            row.Life = RowLife;
            row.Punch = 1f;
            row.Label.Text = Text(item, row.Amount);
            return;
        }
        if (_order.Count >= MaxRows)
        {
            var oldest = _order[0];
            _rows[oldest].Root.QueueFree();
            _rows.Remove(oldest);
            _order.RemoveAt(0);
        }
        var panel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
        panel.AddThemeStyleboxOverride("panel", SlotStyle.Box(new Color(0.05f, 0.07f, 0.1f, 0.78f), new Color(0.3f, 0.36f, 0.45f, 0.6f)));
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 8);
        panel.AddChild(h);
        h.AddChild(new TextureRect { Texture = ItemVisuals.IconOf(item), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(26, 26), MouseFilter = MouseFilterEnum.Ignore });
        var label = Ui.Text(Text(item, amount), 16, Ui.Good);
        h.AddChild(label);
        AddChild(panel);
        _rows[item] = new Row { Root = panel, Label = label, Amount = amount, Life = RowLife, Punch = 1f };
        _order.Add(item);
    }

    private static string Text(string item, long amount) => $"+{amount}  {Loc.T("item_" + item)}";
}

/// <summary>Progreso del objetivo bajo la mira: vida del escombro o estado de alcance.</summary>
public partial class TargetBar : Control
{
    private readonly ColorRect _back, _fill;
    private float _shown;

    public TargetBar()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _back = new ColorRect { Color = new Color(0, 0, 0, 0.65f), MouseFilter = MouseFilterEnum.Ignore };
        _fill = new ColorRect { Color = Ui.Amber, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_back);
        AddChild(_fill);
        Visible = false;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        OffsetLeft = -90;
        OffsetRight = 90;
        OffsetTop = 96;
        OffsetBottom = 103;
    }

    public void Set(float? fraction, Color color)
    {
        Visible = fraction != null;
        if (fraction is not { } f) return;
        _shown = Mathf.Lerp(_shown, f, 0.35f);
        _back.Size = Size;
        _fill.Position = new Vector2(1, 1);
        _fill.Size = new Vector2(Mathf.Max(0, (Size.X - 2) * _shown), Size.Y - 2);
        _fill.Color = color;
    }
}
