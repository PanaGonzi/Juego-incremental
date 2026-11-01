using Godot;
using Pluton.Simulation;

namespace Pluton.Game;

/// <summary>
/// Franja discreta en lo alto: rumbo de la vista y marcadores del objetivo y del depósito. No depende de ver la baliza;
/// lo que queda fuera de su arco se fija al borde con una flecha que indica hacia qué lado girar.
/// </summary>
public partial class Compass : Control
{
    public const float Height = 30;
    private const float Width = 520;
    private const float Span = 150;
    private static readonly string[] Cardinals = { "compass_n", "compass_ne", "compass_e", "compass_se", "compass_s", "compass_sw", "compass_w", "compass_nw" };
    private static readonly Color DepotColor = new(0.45f, 0.8f, 1f);

    private readonly GameView _view;

    public Compass(GameView view)
    {
        _view = view;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        OffsetLeft = -Width / 2;
        OffsetRight = Width / 2;
        OffsetTop = 6;
        OffsetBottom = 6 + Height;
    }

    public override void _Process(double delta) => QueueRedraw();

    private float X(float relative) => Width / 2 + relative / Span * Width;

    public override void _Draw()
    {
        if (!_view.WorldShown) return;
        DrawRect(new Rect2(0, 0, Width, Height), new Color(0.03f, 0.04f, 0.05f, 0.62f));
        var font = UiTheme.Bold;
        var heading = CompassMath.Heading(_view.Yaw);
        for (var bearing = 0; bearing < 360; bearing += 15)
        {
            var rel = CompassMath.Relative(bearing, heading);
            if (Mathf.Abs(rel) > Span / 2) continue;
            var x = X(rel);
            if (bearing % 45 == 0)
            {
                var name = Loc.T(Cardinals[bearing / 45]);
                DrawString(font, new Vector2(x - 20, 21), name, HorizontalAlignment.Center, 40, 15, bearing == 0 ? Ui.Amber : Ui.Ink);
            }
            else DrawLine(new Vector2(x, 4), new Vector2(x, 10), new Color(1, 1, 1, 0.4f), 1);
        }
        // Rumbo exacto y centro de la franja.
        DrawColoredPolygon(new[] { new Vector2(Width / 2 - 5, Height), new Vector2(Width / 2 + 5, Height), new Vector2(Width / 2, Height - 6) }, Ui.Ink);
        // Fuera de la franja: los marcadores se fijan a sus bordes y lo taparían.
        DrawString(font, new Vector2(Width + 8, 21), $"{heading:000}°", HorizontalAlignment.Left, 44, 13, Ui.Dim);

        var from = _view.PlayerPosition;
        // El objetivo manda: si los dos caen en el mismo punto (o en el mismo borde), el depósito se aparta para que ambos se lean.
        var objective = _view.ObjectivePoint;
        var depot = _view.DepotPoint;
        float? taken = objective is { } o ? PlaceX(heading, from, o) : null;
        if (depot is { } d) Marker(heading, from, d, DepotColor, false, Loc.T("compass_depot"), taken);
        if (objective is { } g) Marker(heading, from, g, Ui.Amber, true, $"{_view.ObjectiveDistance:0} m", null);
    }

    private float PlaceX(float heading, Vector3 from, Vector3 at) =>
        X(Mathf.Clamp(CompassMath.Relative(CompassMath.Bearing(from.X, from.Z, at.X, at.Z), heading), -Span / 2 + 8, Span / 2 - 8));

    private void Marker(float heading, Vector3 from, Vector3 at, Color color, bool diamond, string label, float? avoid)
    {
        var rel = CompassMath.Relative(CompassMath.Bearing(from.X, from.Z, at.X, at.Z), heading);
        var font = UiTheme.Bold;
        var outside = Mathf.Abs(rel) > Span / 2 - 4;
        var x = PlaceX(heading, from, at);
        if (avoid is { } taken && Mathf.Abs(x - taken) < 64) x = taken + (taken < Width / 2 ? 64 : -64);
        var y = Height / 2 - 2;
        var shadow = new Color(0, 0, 0, 0.9f);
        if (outside)
        {
            // Fuera del arco: flecha hacia el lado por el que se gira antes.
            var s = Mathf.Sign(rel);
            DrawColoredPolygon(new[] { new Vector2(x + s * 9, y), new Vector2(x - s * 6, y - 8), new Vector2(x - s * 6, y + 8) }, shadow);
            DrawColoredPolygon(new[] { new Vector2(x + s * 7, y), new Vector2(x - s * 4, y - 6), new Vector2(x - s * 4, y + 6) }, color);
        }
        else if (diamond)
        {
            DrawColoredPolygon(new[] { new Vector2(x, y - 9), new Vector2(x + 7, y), new Vector2(x, y + 9), new Vector2(x - 7, y) }, shadow);
            DrawColoredPolygon(new[] { new Vector2(x, y - 7), new Vector2(x + 5, y), new Vector2(x, y + 7), new Vector2(x - 5, y) }, color);
        }
        else
        {
            DrawRect(new Rect2(x - 6, y - 6, 12, 12), shadow);
            DrawRect(new Rect2(x - 4, y - 4, 8, 8), color);
        }
        // La etiqueta cuelga bajo la franja y sigue dentro de ella en los bordes.
        var labelX = Mathf.Clamp(x - 40, 0, Width - 80);
        DrawString(font, new Vector2(labelX + 1, Height + 13), label, HorizontalAlignment.Center, 80, 13, shadow);
        DrawString(font, new Vector2(labelX, Height + 12), label, HorizontalAlignment.Center, 80, 13, color);
    }
}

/// <summary>
/// Señala el destino del objetivo sobre la pantalla: un rombo si se ve en el encuadre y, si no, una flecha en el borde
/// hacia donde hay que girar, también cuando está detrás del jugador.
/// </summary>
public partial class ObjectivePointer : Control
{
    private const float MarginX = 56, MarginTop = 150, MarginBottom = 140;
    private readonly GameView _view;

    public Vector2? ArrowForTest { get; private set; }

    public ObjectivePointer(GameView view)
    {
        _view = view;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Ready() => SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        ArrowForTest = null;
        if (!_view.PointerShown || _view.ObjectivePoint is not { } point) return;
        var camera = _view.Camera;
        var size = GetViewportRect().Size;
        var center = size / 2;
        var world = point + Vector3.Up * 1.4f;
        var behind = camera.IsPositionBehind(world);
        // La cámara devuelve píxeles de ventana; la interfaz puede estar escalada.
        var window = (Vector2)GetWindow().Size;
        var screen = camera.UnprojectPosition(world) * (size / window.Max(Vector2.One));
        var inner = new Rect2(MarginX, MarginTop, size.X - 2 * MarginX, size.Y - MarginTop - MarginBottom);
        var distance = $"{_view.ObjectiveDistance:0} m";
        var font = UiTheme.Bold;
        var shadow = new Color(0, 0, 0, 0.9f);
        if (!behind && inner.HasPoint(screen))
        {
            DrawColoredPolygon(new[] { screen + new Vector2(0, -9), screen + new Vector2(7, 0), screen + new Vector2(0, 9), screen + new Vector2(-7, 0) }, shadow);
            DrawColoredPolygon(new[] { screen + new Vector2(0, -7), screen + new Vector2(5, 0), screen + new Vector2(0, 7), screen + new Vector2(-5, 0) }, Ui.Amber);
            DrawString(font, screen + new Vector2(-39, 25), distance, HorizontalAlignment.Center, 80, 14, shadow);
            DrawString(font, screen + new Vector2(-40, 24), distance, HorizontalAlignment.Center, 80, 14, Ui.Amber);
            return;
        }

        // Hacia dónde girar: por el rumbo, que sigue siendo fiable cuando el punto queda detrás de la cámara.
        var rel = Mathf.DegToRad(CompassMath.Relative(
            CompassMath.Bearing(_view.PlayerPosition.X, _view.PlayerPosition.Z, point.X, point.Z), CompassMath.Heading(_view.Yaw)));
        var dir = new Vector2(Mathf.Sin(rel), -Mathf.Cos(rel));
        if (!behind && (screen - center).LengthSquared() > 1) dir = (screen - center).Normalized();
        var innerCenter = inner.GetCenter();
        var half = inner.Size / 2;
        var t = Mathf.Min(Mathf.Abs(dir.X) < 0.001f ? float.MaxValue : half.X / Mathf.Abs(dir.X), Mathf.Abs(dir.Y) < 0.001f ? float.MaxValue : half.Y / Mathf.Abs(dir.Y));
        var at = innerCenter + dir * t;
        ArrowForTest = at;
        var side = new Vector2(-dir.Y, dir.X);
        DrawColoredPolygon(new[] { at + dir * 24, at + side * 16 - dir * 12, at - side * 16 - dir * 12 }, shadow);
        DrawColoredPolygon(new[] { at + dir * 19, at + side * 12 - dir * 9, at - side * 12 - dir * 9 }, Ui.Amber);
        var labelAt = at - dir * 34 + new Vector2(-40, 5);
        DrawString(font, labelAt + Vector2.One, distance, HorizontalAlignment.Center, 80, 15, shadow);
        DrawString(font, labelAt, distance, HorizontalAlignment.Center, 80, 15, Ui.Amber);
    }
}
