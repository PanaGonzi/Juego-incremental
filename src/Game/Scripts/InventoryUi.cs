using Godot;
using Pluton.Networking;
using Pluton.Simulation;

namespace Pluton.Game;

public enum InventoryTab { Items, Catalog }

/// <summary>Una pila en la mochila o en una máquina. Se actualiza en su sitio para no perder foco ni arrastre al llegar datos del servidor.</summary>
public partial class ItemSlot : PanelContainer
{
    private readonly InventoryUi _ui;
    public readonly string Item;
    public readonly bool InBag;
    public long Quantity;
    private readonly Label _qty;
    private readonly Label _name;
    private bool _usable = true, _hover;

    public ItemSlot(InventoryUi ui, string item, bool inBag)
    {
        _ui = ui;
        Item = item;
        InBag = inBag;
        CustomMinimumSize = new Vector2(102, 118);
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        AddChild(box);
        var holder = new Control { CustomMinimumSize = new Vector2(0, 56), MouseFilter = MouseFilterEnum.Ignore };
        box.AddChild(holder);
        var icon = new TextureRect
        {
            Texture = ItemVisuals.IconOf(item),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.SetAnchorsPreset(LayoutPreset.FullRect);
        holder.AddChild(icon);
        _qty = Ui.Text("", 16, Ui.Ink);
        _qty.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        _qty.GrowHorizontal = GrowDirection.Begin;
        _qty.GrowVertical = GrowDirection.Begin;
        _qty.AddThemeConstantOverride("outline_size", 5);
        _qty.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        holder.AddChild(_qty);
        _name = Ui.Text(Loc.T("item_" + item), 13, Ui.Ink);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.VerticalAlignment = VerticalAlignment.Center;
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _name.CustomMinimumSize = new Vector2(90, 36);
        _name.AddThemeConstantOverride("line_spacing", -2);
        box.AddChild(_name);
        Paint();
        MouseEntered += () => { _hover = true; Paint(); _ui.Hovered(this); };
        MouseExited += () => { _hover = false; Paint(); };
        FocusEntered += () => { Paint(); _ui.Hovered(this); };
        FocusExited += Paint;
    }

    public void Set(long quantity, bool usable, string tooltip)
    {
        if (quantity != Quantity)
        {
            Quantity = quantity;
            _qty.Text = quantity.ToString("N0");
        }
        if (usable != _usable)
        {
            _usable = usable;
            Modulate = usable ? Colors.White : new Color(1, 1, 1, 0.55f);
        }
        TooltipText = tooltip;
    }

    private void Paint()
    {
        var hot = _hover || HasFocus();
        AddThemeStyleboxOverride("panel", SlotStyle.Box(
            hot ? new Color(0.14f, 0.18f, 0.24f, 0.96f) : SlotStyle.Slot,
            HasFocus() ? Ui.Amber : hot ? Ui.Ink : SlotStyle.Line,
            HasFocus() ? 2 : 1));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton button && !button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.Left)
            {
                GrabFocus();
                _ui.Quick(this, button.ShiftPressed, button.CtrlPressed);
                AcceptEvent();
            }
            else if (button.ButtonIndex == MouseButton.Right)
            {
                GrabFocus();
                _ui.OpenSplit(this);
                AcceptEvent();
            }
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            if (key.PhysicalKeycode is Key.Enter or Key.KpEnter or Key.Space)
            {
                _ui.Quick(this, key.ShiftPressed, key.CtrlPressed);
                AcceptEvent();
            }
            else if (key.PhysicalKeycode == Key.Menu)
            {
                _ui.OpenSplit(this);
                AcceptEvent();
            }
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        var preview = new TextureRect { Texture = ItemVisuals.IconOf(Item), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, CustomMinimumSize = new Vector2(52, 52), Size = new Vector2(52, 52) };
        SetDragPreview(preview);
        return new Godot.Collections.Dictionary { { "item", Item }, { "bag", InBag }, { "amount", Quantity } };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => _ui.CanDrop(InBag, data);
    public override void _DropData(Vector2 atPosition, Variant data) => _ui.Drop(InBag, data);
}

/// <summary>Zona que recibe pilas arrastradas: toda la columna, no solo sus casillas.</summary>
public partial class DropColumn : PanelContainer
{
    private readonly InventoryUi _ui;
    private readonly bool _bag;

    public DropColumn(InventoryUi ui, bool bag)
    {
        _ui = ui;
        _bag = bag;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => _ui.CanDrop(_bag, data);
    public override void _DropData(Vector2 atPosition, Variant data) => _ui.Drop(_bag, data);
}

/// <summary>Herramienta o construcción seleccionable y asignable a la barra rápida.</summary>
public partial class EntryCard : PanelContainer
{
    private readonly InventoryUi _ui;
    public readonly string Entry;
    private readonly Label _cost;
    private bool _hover;

    public EntryCard(InventoryUi ui, string entry, Vector2 size)
    {
        _ui = ui;
        Entry = entry;
        CustomMinimumSize = size;
        FocusMode = FocusModeEnum.All;
        MouseFilter = MouseFilterEnum.Stop;
        var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        h.AddThemeConstantOverride("separation", 8);
        AddChild(h);
        h.AddChild(EntryInfo.Icon(entry, 56));
        var v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        v.AddThemeConstantOverride("separation", 0);
        h.AddChild(v);
        var name = Ui.Text(EntryInfo.Name(entry), 15, Ui.Ink);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        name.CustomMinimumSize = new Vector2(size.X - 86, 0);
        v.AddChild(name);
        _cost = Ui.Text("", 13, Ui.Dim);
        _cost.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _cost.CustomMinimumSize = new Vector2(size.X - 86, 0);
        v.AddChild(_cost);
        Paint();
        MouseEntered += () => { _hover = true; Paint(); _ui.HoveredEntry(this); };
        MouseExited += () => { _hover = false; Paint(); _ui.LeftEntry(this); };
        FocusEntered += () => { Paint(); _ui.HoveredEntry(this); };
        FocusExited += Paint;
    }

    public void SetCost(string text, Color color)
    {
        if (_cost.Text != text) _cost.Text = text;
        _cost.AddThemeColorOverride("font_color", color);
    }

    private void Paint()
    {
        var hot = _hover || HasFocus();
        AddThemeStyleboxOverride("panel", SlotStyle.Box(
            hot ? new Color(0.14f, 0.18f, 0.24f, 0.96f) : SlotStyle.Slot,
            HasFocus() ? Ui.Amber : hot ? Ui.Ink : SlotStyle.Line,
            HasFocus() ? 2 : 1, 4, 7));
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { Pressed: false } button)
        {
            if (button.ButtonIndex == MouseButton.Left) { _ui.Choose(Entry); AcceptEvent(); }
            else if (button.ButtonIndex == MouseButton.Right) { _ui.PinToFree(Entry); AcceptEvent(); }
        }
        else if (e is InputEventKey { Pressed: true, Echo: false } key && key.PhysicalKeycode is Key.Enter or Key.KpEnter or Key.Space)
        {
            _ui.Choose(Entry);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        SetDragPreview(EntryInfo.Icon(Entry, 56));
        return new Godot.Collections.Dictionary { { "entry", Entry } };
    }
}

/// <summary>
/// Inventario, equipo y catálogo. Cada transferencia es un comando con cantidad exacta que el servidor valida;
/// la interfaz solo refleja lo replicado. El mundo sigue en marcha: abrirlo no pausa nada.
/// </summary>
public partial class InventoryUi : Control
{
    private readonly GameView _view;
    private PanelContainer _panel = null!;
    private Button _tabItems = null!, _tabCatalog = null!;
    private Label _credits = null!;
    private Control _itemsPage = null!, _catalogPage = null!;

    private GridContainer _bagGrid = null!, _machineGrid = null!;
    private Label _bagEmpty = null!, _machineEmpty = null!, _machineTitle = null!, _machineStatus = null!, _machineInputs = null!;
    private HBoxContainer _machineNav = null!;
    private Button _machinePanelButton = null!;
    private readonly Dictionary<string, ItemSlot> _bagSlots = new();
    private readonly Dictionary<string, ItemSlot> _machineSlots = new();

    private Label _detailTitle = null!, _detailBody = null!, _help = null!;
    private HBoxContainer _splitRow = null!;
    private HSlider _splitSlider = null!;
    private Label _splitLabel = null!;
    private ItemSlot? _splitSlot;

    private GridContainer _catalogGrid = null!, _equipGrid = null!;
    private readonly List<EntryCard> _catalogCards = new();
    private readonly List<EntryCard> _equipCards = new();
    private Label _catalogTitle = null!, _catalogDetail = null!;
    private VBoxContainer _costList = null!;
    private string _catalogSignature = "";
    private EntryCard? _hoverEntry;

    private InventoryTab _tab = InventoryTab.Items;
    private int _machineId = -1;
    private readonly List<int> _nearby = new();
    private double _refresh;

    public bool IsOpen => Visible;
    public InventoryTab CurrentTab => _tab;
    public int MachineId => _machineId;
    public int BagSlotCount => _bagSlots.Count;
    public int MachineSlotCount => _machineSlots.Count;

    public InventoryUi(GameView view) { _view = view; }

    public override void _Ready()
    {
        Theme = Ui.Theme;
        Visible = false;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0.01f, 0.02f, 0.03f, 0.38f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        _panel = new PanelContainer();
        _panel.AddThemeStyleboxOverride("panel", SlotStyle.Box(new Color(0.045f, 0.06f, 0.09f, 0.97f), new Color(0.33f, 0.4f, 0.5f, 0.9f), 1, 6, 14));
        AddChild(_panel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(root);

        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        root.AddChild(tabs);
        _tabItems = Ui.Btn("", () => ShowTab(InventoryTab.Items));
        _tabCatalog = Ui.Btn("", () => ShowTab(InventoryTab.Catalog));
        tabs.AddChild(_tabItems);
        tabs.AddChild(_tabCatalog);
        tabs.AddChild(Ui.Btn($"{Loc.T("tech_title")} [{Controls.KeyName("tech")}]", _view.OpenTechFromInventory));
        tabs.AddChild(Ui.Btn($"{Loc.T("hud_players")} [{Controls.KeyName("players")}]", _view.OpenPlayersFromInventory));
        tabs.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _credits = Ui.Text("", 22, Ui.Amber);
        tabs.AddChild(_credits);
        tabs.AddChild(Ui.Btn($"{Loc.T("panel_close")} [{Controls.KeyName("inventory")}]", Close));

        var pages = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(pages);
        _itemsPage = BuildItemsPage();
        _itemsPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pages.AddChild(_itemsPage);
        _catalogPage = BuildCatalogPage();
        _catalogPage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pages.AddChild(_catalogPage);

        _help = Ui.Text("", 14, Ui.Dim);
        _help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_help);
    }

    private Control BuildItemsPage()
    {
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 10);
        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 10);
        page.AddChild(columns);

        // Equipo: herramientas reales, que se eligen con clic y se fijan a la barra.
        var equip = Column(Loc.T("inv_equipment"), out var equipBody, new DropColumnFactory(null));
        equip.SizeFlagsHorizontal = SizeFlags.Fill;
        equip.CustomMinimumSize = new Vector2(286, 0);
        columns.AddChild(equip);
        _equipGrid = new GridContainer { Columns = 1 };
        _equipGrid.AddThemeConstantOverride("v_separation", 6);
        equipBody.AddChild(_equipGrid);
        foreach (var tool in new[] { "cutter", "link", "remove", "rail" })
        {
            var card = new EntryCard(this, "tool:" + tool, new Vector2(254, 72));
            _equipCards.Add(card);
            _equipGrid.AddChild(card);
        }

        var bag = Column(Loc.T("inv_bag"), out var bagBody, new DropColumnFactory(true));
        columns.AddChild(bag);
        _bagEmpty = Ui.Text(Loc.T("inv_empty"), 16, Ui.Dim);
        _bagEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _bagEmpty.CustomMinimumSize = new Vector2(320, 0);
        bagBody.AddChild(_bagEmpty);
        _bagGrid = ScrollGrid(bagBody, 5);

        var machine = Column("", out var machineBody, new DropColumnFactory(false));
        columns.AddChild(machine);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 6);
        machineBody.AddChild(head);
        _machineNav = new HBoxContainer();
        _machineNav.AddChild(Ui.Btn("◀", () => CycleMachine(-1)));
        _machineNav.AddChild(Ui.Btn("▶", () => CycleMachine(1)));
        head.AddChild(_machineNav);
        _machineTitle = Ui.Text("", 20, Ui.Amber);
        _machineTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _machineTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        head.AddChild(_machineTitle);
        _machinePanelButton = Ui.Btn(Loc.T("inv_open_panel"), () => { if (_machineId >= 0) _view.OpenMachineFromInventory(_machineId); });
        head.AddChild(_machinePanelButton);
        _machineStatus = Ui.Text("", 15, Ui.Dim);
        _machineStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        machineBody.AddChild(_machineStatus);
        _machineInputs = Ui.Text("", 15, Ui.Ink);
        _machineInputs.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        machineBody.AddChild(_machineInputs);
        _machineEmpty = Ui.Text("", 16, Ui.Dim);
        _machineEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _machineEmpty.CustomMinimumSize = new Vector2(320, 0);
        machineBody.AddChild(_machineEmpty);
        _machineGrid = ScrollGrid(machineBody, 5);

        // Detalle y división de pila: siempre en el mismo lugar para que nada salte al pasar el cursor.
        var detail = new PanelContainer { CustomMinimumSize = new Vector2(0, 136) };
        detail.AddThemeStyleboxOverride("panel", SlotStyle.Box(new Color(0.06f, 0.08f, 0.115f, 0.95f), SlotStyle.Line, 1, 4, 10));
        page.AddChild(detail);
        var detailBox = new HBoxContainer();
        detailBox.AddThemeConstantOverride("separation", 16);
        detail.AddChild(detailBox);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        detailBox.AddChild(text);
        _detailTitle = Ui.Text(Loc.T("inv_detail_hint"), 18, Ui.Ink);
        text.AddChild(_detailTitle);
        _detailBody = Ui.Text("", 15, Ui.Dim);
        _detailBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _detailBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.AddChild(_detailBody);
        _splitRow = new HBoxContainer { Visible = false, CustomMinimumSize = new Vector2(520, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _splitRow.AddThemeConstantOverride("separation", 10);
        detailBox.AddChild(_splitRow);
        _splitSlider = new HSlider { MinValue = 1, Step = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 24) };
        _splitSlider.ValueChanged += _ => UpdateSplitLabel();
        _splitRow.AddChild(_splitSlider);
        _splitLabel = Ui.Text("", 18, Ui.Amber);
        _splitLabel.CustomMinimumSize = new Vector2(66, 0);
        _splitLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _splitRow.AddChild(_splitLabel);
        _splitRow.AddChild(Ui.Btn(Loc.T("inv_split"), ConfirmSplit));
        return page;
    }

    private sealed record DropColumnFactory(bool? Bag);

    private Control Column(string title, out VBoxContainer body, DropColumnFactory factory)
    {
        PanelContainer panel = factory.Bag is { } bag ? new DropColumn(this, bag) : new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", SlotStyle.Box(new Color(0.06f, 0.08f, 0.115f, 0.95f), SlotStyle.Line, 1, 4, 10));
        body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);
        if (title.Length > 0) body.AddChild(Ui.Text(title, 20, Ui.Amber));
        return panel;
    }

    private static GridContainer ScrollGrid(Control parent, int columns)
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        parent.AddChild(scroll);
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);
        return grid;
    }

    private Control BuildCatalogPage()
    {
        var page = new HBoxContainer();
        page.AddThemeConstantOverride("separation", 10);
        var list = Column(Loc.T("inv_catalog"), out var listBody, new DropColumnFactory(null));
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        page.AddChild(list);
        _catalogGrid = ScrollGrid(listBody, 3);

        var side = Column("", out var sideBody, new DropColumnFactory(null));
        side.CustomMinimumSize = new Vector2(430, 0);
        page.AddChild(side);
        _catalogTitle = Ui.Text(Loc.T("inv_catalog_hint"), 20, Ui.Amber);
        _catalogTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sideBody.AddChild(_catalogTitle);
        _catalogDetail = Ui.Text("", 15, Ui.Dim);
        _catalogDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sideBody.AddChild(_catalogDetail);
        _costList = new VBoxContainer();
        _costList.AddThemeConstantOverride("separation", 4);
        sideBody.AddChild(_costList);
        return page;
    }

    // ---------- Abrir y cerrar ----------

    public void Open(InventoryTab tab, int machineId = -1)
    {
        Visible = true;
        _machineId = machineId;
        _splitSlot = null;
        _splitRow.Visible = false;
        ResizePanel();
        ShowTab(tab);
        _refresh = 0;
        Refresh();
        _view.UpdateMouseMode();
        _view.RefreshHotbar();
    }

    public void Close()
    {
        if (!Visible) return;
        Visible = false;
        GetViewport().GuiReleaseFocus();
        _view.OnInventoryClosed();
    }

    private void ResizePanel()
    {
        var size = GetViewportRect().Size;
        var w = Mathf.Min(size.X - 40, 1640);
        var h = Mathf.Min(size.Y - 130, 930);
        _panel.Position = new Vector2((size.X - w) / 2, Mathf.Max(10, (size.Y - 110 - h) / 2));
        _panel.Size = new Vector2(w, h);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _panel != null && Visible) ResizePanel();
    }

    private void ShowTab(InventoryTab tab)
    {
        _tab = tab;
        _itemsPage.Visible = tab == InventoryTab.Items;
        _catalogPage.Visible = tab == InventoryTab.Catalog;
        _tabItems.Text = $"{Loc.T("inv_title")} [{Controls.KeyName("inventory")}]";
        _tabCatalog.Text = $"{Loc.T("inv_catalog")} [{Controls.KeyName("tool_build")}]";
        _tabItems.Modulate = tab == InventoryTab.Items ? new Color(1.3f, 1.1f, 0.7f) : Colors.White;
        _tabCatalog.Modulate = tab == InventoryTab.Catalog ? new Color(1.3f, 1.1f, 0.7f) : Colors.White;
        _help.Text = Loc.T(tab == InventoryTab.Items ? "inv_help_items" : "inv_help_catalog");
        _catalogSignature = "";
    }

    /// <summary>Tab, E, Q y Esc deben funcionar aunque una casilla tenga el foco: se atienden antes que la navegación de la interfaz.</summary>
    public override void _Input(InputEvent e)
    {
        if (!Visible || e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (e.IsActionPressed("inventory") || e.IsActionPressed("pause"))
        {
            if (_tab != InventoryTab.Items && e.IsActionPressed("inventory")) ShowTab(InventoryTab.Items);
            else Close();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e.IsActionPressed("interact"))
        {
            Close();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e.IsActionPressed("tool_build"))
        {
            if (_tab == InventoryTab.Catalog) Close();
            else ShowTab(InventoryTab.Catalog);
            GetViewport().SetInputAsHandled();
            return;
        }
        // Con el cursor sobre una herramienta o construcción, un número la fija a esa casilla.
        for (var i = 1; i < HotbarLayout.Slots; i++)
        {
            if (_hoverEntry == null || !e.IsActionPressed("hotbar_" + (i + 1))) continue;
            _view.AssignHotbar(i, _hoverEntry.Entry);
            GetViewport().SetInputAsHandled();
            return;
        }
        _ = key;
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _refresh -= delta;
        if (_refresh > 0 || Input.IsMouseButtonPressed(MouseButton.Left)) return;
        _refresh = 0.12;
        Refresh();
    }

    // ---------- Datos ----------

    private static bool Transferable(MachineDef def) => def.Kind is not (MachineKind.Generator or MachineKind.Shuttle or MachineKind.Radiator);

    private void FindNearby(World w, PlayerState me)
    {
        _nearby.Clear();
        var range = (double)w.Content.Balance.InteractRangeMm;
        var list = new List<(double d, int id)>();
        foreach (var m in w.Machines.Values)
        {
            if (m.Planet != me.Planet || !Transferable(w.Content.Machines[m.Def])) continue;
            var (cx, cz) = w.MachineCenterMm(m);
            var dx = me.X * 1000.0 - cx;
            var dz = me.Z * 1000.0 - cz;
            var d = dx * dx + dz * dz;
            if (d <= range * range) list.Add((d, m.Id));
        }
        foreach (var (_, id) in list.OrderBy(x => x.d)) _nearby.Add(id);
    }

    private void CycleMachine(int direction)
    {
        if (_nearby.Count == 0) return;
        var index = Math.Max(0, _nearby.IndexOf(_machineId));
        _machineId = _nearby[((index + direction) % _nearby.Count + _nearby.Count) % _nearby.Count];
        foreach (var slot in _machineSlots.Values) slot.QueueFree();
        _machineSlots.Clear();
        Refresh();
    }

    private void Refresh()
    {
        var client = _view.Session.Client;
        var w = client.World;
        var me = client.LocalPlayer;
        if (w == null || me == null) return;
        _credits.Text = $"{w.Credits:N0} ¤";
        foreach (var card in _equipCards) card.SetCost(Loc.T("tool_" + EntryInfo.ToolOf(card.Entry) + "_d"), Ui.Dim);
        if (_tab == InventoryTab.Items) RefreshItems(w, me);
        else RefreshCatalog(w, me);
    }

    private void RefreshItems(World w, PlayerState me)
    {
        FindNearby(w, me);
        if (_machineId >= 0 && !_nearby.Contains(_machineId)) _machineId = -1;
        if (_machineId < 0 && _nearby.Count > 0) _machineId = _nearby[0];
        _machineNav.Visible = _nearby.Count > 1;
        w.Machines.TryGetValue(_machineId, out var machine);

        var bagTip = new Dictionary<string, (long qty, bool usable, string tip)>();
        foreach (var (item, qty) in me.Bag.Items)
        {
            var usable = machine == null || w.InputRoom(machine, item) > 0;
            bagTip[item] = (qty, usable, Tooltip(w, item, machine != null && !usable));
        }
        Sync(_bagGrid, _bagSlots, bagTip, true);
        _bagEmpty.Visible = _bagSlots.Count == 0;
        _bagGrid.Visible = _bagSlots.Count > 0;

        if (machine == null)
        {
            _machineTitle.Text = Loc.T("inv_no_machine_title");
            _machineStatus.Text = "";
            _machineInputs.Text = "";
            _machineEmpty.Text = Loc.T("inv_no_machine");
            _machineEmpty.Visible = true;
            _machinePanelButton.Visible = false;
            Sync(_machineGrid, _machineSlots, new(), false);
            return;
        }
        var def = w.Content.Machines[machine.Def];
        _machinePanelButton.Visible = true;
        _machineTitle.Text = Loc.T("machine_" + machine.Def);
        _machineStatus.Text = $"{Loc.T("panel_status")}: {Loc.T("status_" + machine.Status)}" + (def.Capacity > 0 ? $"   ·   {Loc.T("panel_capacity", def.Capacity.ToString())}" : "");
        var source = def.Kind == MachineKind.Production ? machine.Out : machine.In;
        _machineInputs.Text = def.Kind == MachineKind.Production ? $"{Loc.T("panel_input")}: {Ui.Amounts(machine.In.Items)}" : "";
        var data = new Dictionary<string, (long qty, bool usable, string tip)>();
        foreach (var (item, qty) in source.Items) data[item] = (qty, true, Tooltip(w, item, false));
        Sync(_machineGrid, _machineSlots, data, false);
        _machineEmpty.Text = Loc.T(def.Kind == MachineKind.Production ? "inv_machine_no_output" : "inv_machine_empty");
        _machineEmpty.Visible = _machineSlots.Count == 0;
        _machineGrid.Visible = _machineSlots.Count > 0;

        // Si la pila que se divide ya no existe, se retira el control.
        if (_splitSlot != null && !GodotObject.IsInstanceValid(_splitSlot))
        {
            _splitSlot = null;
            _splitRow.Visible = false;
        }
        else if (_splitSlot != null)
        {
            _splitSlider.MaxValue = Math.Max(1, _splitSlot.Quantity);
            UpdateSplitLabel();
        }
    }

    private void Sync(GridContainer grid, Dictionary<string, ItemSlot> slots, Dictionary<string, (long qty, bool usable, string tip)> data, bool bag)
    {
        foreach (var item in slots.Keys.Where(k => !data.ContainsKey(k)).ToList())
        {
            var gone = slots[item];
            if (_splitSlot == gone)
            {
                _splitSlot = null;
                _splitRow.Visible = false;
            }
            grid.RemoveChild(gone);
            gone.QueueFree();
            slots.Remove(item);
        }
        var added = false;
        foreach (var (item, value) in data)
        {
            if (!slots.TryGetValue(item, out var slot))
            {
                slot = new ItemSlot(this, item, bag);
                slots[item] = slot;
                grid.AddChild(slot);
                added = true;
            }
            slot.Set(value.qty, value.usable, value.tip);
        }
        if (!added) return;
        // Orden estable por nombre: una pila nueva no reordena de forma imprevisible.
        var ordered = slots.Values.OrderBy(s => Loc.T("item_" + s.Item), StringComparer.CurrentCulture).ToList();
        for (var i = 0; i < ordered.Count; i++) grid.MoveChild(ordered[i], i);
    }

    private static string Tooltip(World w, string item, bool refused)
    {
        var lines = new List<string> { Loc.T("item_" + item), Loc.T("item_" + item + "_d") };
        var (uses, sources) = Uses(w, item);
        if (uses.Length > 0) lines.Add($"{Loc.T("item_tip_use")}: {uses}");
        if (sources.Length > 0) lines.Add($"{Loc.T("item_tip_made")}: {sources}");
        lines.Add(Loc.T("item_tip_price", w.SellPrice(item).ToString()));
        if (refused) lines.Add(Loc.T("inv_refused"));
        return string.Join("\n", lines);
    }

    private static (string uses, string sources) Uses(World w, string item)
    {
        var uses = new List<string>();
        var sources = new List<string>();
        foreach (var def in w.Content.Machines.Values)
        {
            if (def.Recipe == null || !w.Content.Recipes.TryGetValue(def.Recipe, out var r)) continue;
            if (r.Inputs.ContainsKey(item)) uses.Add(Loc.T("machine_" + def.Id));
            if (r.Outputs.ContainsKey(item)) sources.Add(Loc.T("machine_" + def.Id));
        }
        return (string.Join(", ", uses), string.Join(", ", sources));
    }

    // ---------- Transferencias ----------

    public void Hovered(ItemSlot slot)
    {
        var w = _view.Session.Client.World;
        if (w == null) return;
        _detailTitle.Text = $"{Loc.T("item_" + slot.Item)}   ·   {slot.Quantity:N0}";
        var (uses, sources) = Uses(w, slot.Item);
        var lines = new List<string> { Loc.T("item_" + slot.Item + "_d") };
        if (uses.Length > 0) lines.Add($"{Loc.T("item_tip_use")}: {uses}");
        if (sources.Length > 0) lines.Add($"{Loc.T("item_tip_made")}: {sources}");
        lines.Add(Loc.T("item_tip_price", w.SellPrice(slot.Item).ToString()));
        _detailBody.Text = string.Join("\n", lines);
    }

    /// <summary>Clic o tecla: pila entera; con Mayús, la mitad; con Ctrl, una unidad.</summary>
    public void Quick(ItemSlot slot, bool half, bool one)
    {
        var amount = one ? 1 : half ? Math.Max(1, slot.Quantity / 2) : slot.Quantity;
        Transfer(slot.Item, slot.InBag, amount);
    }

    public void OpenSplit(ItemSlot slot)
    {
        if (_machineId < 0 || slot.Quantity < 2) return;
        _splitSlot = slot;
        _splitRow.Visible = true;
        _splitSlider.MaxValue = slot.Quantity;
        _splitSlider.Value = Math.Max(1, slot.Quantity / 2);
        _splitSlider.GrabFocus();
        UpdateSplitLabel();
    }

    private void UpdateSplitLabel() => _splitLabel.Text = ((long)_splitSlider.Value).ToString("N0");

    private void ConfirmSplit()
    {
        if (_splitSlot == null || !GodotObject.IsInstanceValid(_splitSlot)) return;
        Transfer(_splitSlot.Item, _splitSlot.InBag, (long)_splitSlider.Value);
        _splitRow.Visible = false;
        _splitSlot = null;
    }

    public bool CanDrop(bool toBag, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary || _machineId < 0) return false;
        var d = data.AsGodotDictionary();
        return d.ContainsKey("item") && d["bag"].AsBool() != toBag;
    }

    public void Drop(bool toBag, Variant data)
    {
        var d = data.AsGodotDictionary();
        Transfer(d["item"].AsString(), !toBag, d["amount"].AsInt64());
    }

    private void Transfer(string item, bool fromBag, long amount)
    {
        var client = _view.Session.Client;
        if (client.World == null) return;
        if (_machineId < 0 || !client.World.Machines.ContainsKey(_machineId))
        {
            _view.Notify(Loc.T("inv_no_machine"));
            return;
        }
        if (amount <= 0) return;
        client.Send(new Command { Type = fromBag ? CommandType.Insert : CommandType.Extract, A = _machineId, Text = item, Amount = amount });
        Sfx.Play(fromBag ? "deposit" : "take");
        _view.RigGesture();
    }

    // ---------- Catálogo ----------

    private void RefreshCatalog(World w, PlayerState me)
    {
        var defs = w.Content.Machines.Values
            .Where(d => !(d.Builtin && d.Credits == 0 && d.Cost.Count == 0) && (d.Unlock == null || w.Upgrades.Contains(d.Unlock)))
            .ToList();
        var signature = string.Join(",", defs.Select(d => d.Id));
        if (signature != _catalogSignature)
        {
            _catalogSignature = signature;
            foreach (var c in _catalogCards)
            {
                _catalogGrid.RemoveChild(c);
                c.QueueFree();
            }
            _catalogCards.Clear();
            _hoverEntry = null;
            foreach (var d in defs)
            {
                var card = new EntryCard(this, "build:" + d.Id, new Vector2(340, 86));
                _catalogCards.Add(card);
                _catalogGrid.AddChild(card);
            }
        }
        foreach (var card in _catalogCards)
        {
            var def = w.Content.Machines[EntryInfo.DefOf(card.Entry)];
            var afford = w.CanAfford(me, def.Credits, def.Cost);
            card.SetCost(Ui.Cost(def.Credits, def.Cost), afford ? Ui.Good : Ui.Bad);
        }
        var focus = _hoverEntry;
        if (focus != null && EntryInfo.DefOf(focus.Entry).Length > 0 && w.Content.Machines.TryGetValue(EntryInfo.DefOf(focus.Entry), out var shown))
            ShowCost(w, me, shown);
    }

    private void ShowCost(World w, PlayerState me, MachineDef def)
    {
        _catalogTitle.Text = Loc.T("machine_" + def.Id);
        var lines = new List<string> { $"{Loc.T("cat_size")}: {def.W * 2} × {def.D * 2} m" };
        if (def.Power > 0) lines.Add($"{Loc.T("panel_power_use")}: {def.Power}");
        if (def.PowerGen > 0) lines.Add($"{Loc.T("panel_power_gen")}: {def.PowerGen}");
        if (def.Capacity > 0) lines.Add(Loc.T("panel_capacity", def.Capacity.ToString()));
        if (def.Recipe != null && w.Content.Recipes.TryGetValue(def.Recipe, out var r))
            lines.Add($"{Loc.T("panel_recipe")}: {Ui.Amounts(r.Inputs)} → {Ui.Amounts(r.Outputs)}");
        _catalogDetail.Text = string.Join("\n", lines);

        foreach (var c in _costList.GetChildren()) { _costList.RemoveChild(c); c.QueueFree(); }
        if (def.Credits > 0) _costList.AddChild(CostRow(null, def.Credits, w.Credits));
        foreach (var (item, qty) in def.Cost) _costList.AddChild(CostRow(item, qty, EntryInfo.HaveOf(w, me, item)));
        var verdict = Ui.Text(EntryInfo.MissingText(w, me, def), 16, w.CanAfford(me, def.Credits, def.Cost) ? Ui.Good : Ui.Bad);
        verdict.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _costList.AddChild(verdict);
        _costList.AddChild(Ui.Text(Loc.T("inv_catalog_pin"), 14, Ui.Dim));
    }

    private static Control CostRow(string? item, long need, long have)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        if (item != null)
            row.AddChild(new TextureRect { Texture = ItemVisuals.IconOf(item), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(26, 26) });
        var name = Ui.Text(item == null ? "¤" : Loc.T("item_" + item), 16, Ui.Ink);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(name);
        row.AddChild(Ui.Text($"{have:N0} / {need:N0}", 16, have >= need ? Ui.Good : Ui.Bad));
        return row;
    }

    public void HoveredEntry(EntryCard card)
    {
        _hoverEntry = card;
        var client = _view.Session.Client;
        if (client.World == null || client.LocalPlayer == null) return;
        var def = EntryInfo.DefOf(card.Entry);
        if (def.Length > 0 && client.World.Content.Machines.TryGetValue(def, out var d)) ShowCost(client.World, client.LocalPlayer, d);
        else if (_tab == InventoryTab.Items)
        {
            _detailTitle.Text = EntryInfo.Name(card.Entry);
            _detailBody.Text = $"{Loc.T("tool_" + EntryInfo.ToolOf(card.Entry) + "_d")}\n{Loc.T("inv_catalog_pin")}";
        }
    }

    public void LeftEntry(EntryCard card)
    {
        if (_hoverEntry == card) _hoverEntry = null;
    }

    /// <summary>Elegir una herramienta o construcción la equipa y devuelve el control al jugador.</summary>
    public void Choose(string entry)
    {
        _view.ActivateEntry(entry);
        Close();
    }

    public void PinToFree(string entry)
    {
        var free = HotbarLayout.FirstFree(_view.HotbarEntries);
        if (_view.HotbarEntries.Contains(entry)) return;
        if (free < 0) _view.Notify(Loc.T("hotbar_full"));
        else _view.AssignHotbar(free, entry);
    }
}
