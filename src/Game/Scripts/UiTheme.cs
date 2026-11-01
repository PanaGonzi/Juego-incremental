using Godot;

namespace Pluton.Game;

/// <summary>Sistema visual de la interfaz: color, tipografía, espaciado y estados de todos los controles.</summary>
public static class UiTheme
{
    // Color. El texto sobre panel supera 7:1; las señales llevan además icono o texto.
    public static readonly Color Amber = new(1f, 0.67f, 0.25f);
    public static readonly Color Ink = new(0.93f, 0.95f, 0.98f);
    public static readonly Color Dim = new(0.68f, 0.74f, 0.81f);
    public static readonly Color Good = new(0.55f, 0.94f, 0.68f);
    public static readonly Color Bad = new(1f, 0.56f, 0.47f);
    public static readonly Color Warn = new(1f, 0.82f, 0.4f);
    public static readonly Color Surface = new(0.043f, 0.058f, 0.082f, 0.93f);
    public static readonly Color SurfaceRaised = new(0.1f, 0.13f, 0.17f);
    public static readonly Color SurfaceHover = new(0.16f, 0.2f, 0.26f);
    public static readonly Color Edge = new(0.32f, 0.39f, 0.48f, 0.75f);

    // Espaciado y tamaños.
    public const int S = 4, M = 8, L = 14, XL = 22;
    public const int Caption = 13, Small = 15, Body = 17, Heading = 20, Title = 26;
    public const int ButtonHeight = 40;

    private static Theme? _main;
    private static FontVariation? _bold, _display;

    public static Font Bold => _bold ??= new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.55f };

    public static Font Display
    {
        get
        {
            if (_display != null) return _display;
            _display = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.3f };
            _display.SetSpacing(TextServer.SpacingType.Glyph, 7);
            return _display;
        }
    }

    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, int padX = 12, int padY = 7, int radius = 3)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ContentMarginLeft = padX,
            ContentMarginRight = padX,
            ContentMarginTop = padY,
            ContentMarginBottom = padY,
        };
        s.SetBorderWidthAll(borderWidth);
        s.SetCornerRadiusAll(radius);
        return s;
    }

    /// <summary>Botón: el borde izquierdo ámbar marca hover y selección; se ve también sin distinguir tonos.</summary>
    private static StyleBoxFlat Marked(Color bg, Color border, int mark)
    {
        var s = Box(bg, border);
        s.BorderWidthLeft = mark;
        s.ContentMarginLeft = 15 - mark;
        return s;
    }

    public static Theme Main
    {
        get
        {
            if (_main != null) return _main;
            var t = new Theme { DefaultFontSize = Body };
            t.SetStylebox("panel", "PanelContainer", Box(Surface, Edge, 1, L, 10));
            t.SetColor("font_color", "Label", Ink);

            foreach (var type in new[] { "Button", "OptionButton", "CheckButton" })
            {
                t.SetStylebox("normal", type, Marked(SurfaceRaised, Edge, 1));
                t.SetStylebox("hover", type, Marked(SurfaceHover, Amber, 4));
                t.SetStylebox("pressed", type, Marked(new Color(0.36f, 0.24f, 0.09f), Amber, 4));
                t.SetStylebox("hover_pressed", type, Marked(new Color(0.42f, 0.28f, 0.1f), Amber, 4));
                t.SetStylebox("disabled", type, Box(new Color(0.07f, 0.085f, 0.11f), new Color(0.2f, 0.24f, 0.3f)));
                var focus = Box(new Color(0, 0, 0, 0), Amber, 2);
                focus.SetExpandMarginAll(1);
                t.SetStylebox("focus", type, focus);
                t.SetColor("font_color", type, Ink);
                t.SetColor("font_hover_color", type, Colors.White);
                t.SetColor("font_pressed_color", type, Colors.White);
                t.SetColor("font_focus_color", type, Colors.White);
                t.SetColor("font_disabled_color", type, new Color(0.5f, 0.55f, 0.61f));
                t.SetConstant("h_separation", type, M);
            }

            t.SetStylebox("normal", "LineEdit", Box(new Color(0.025f, 0.035f, 0.05f), Edge));
            var editFocus = Box(new Color(0.03f, 0.045f, 0.065f), Amber, 2);
            t.SetStylebox("focus", "LineEdit", editFocus);
            t.SetStylebox("read_only", "LineEdit", Box(new Color(0.06f, 0.07f, 0.09f), new Color(0.2f, 0.24f, 0.3f)));
            t.SetColor("font_color", "LineEdit", Ink);
            t.SetColor("font_placeholder_color", "LineEdit", Dim);
            t.SetColor("caret_color", "LineEdit", Amber);
            t.SetColor("selection_color", "LineEdit", new Color(1f, 0.67f, 0.25f, 0.35f));

            // Barra de progreso: sirve para objetivos, ciclos de máquina y energía.
            t.SetStylebox("background", "ProgressBar", Box(new Color(0.02f, 0.03f, 0.04f), Edge, 1, 0, 0, 2));
            t.SetStylebox("fill", "ProgressBar", Box(Amber, new Color(0, 0, 0, 0), 0, 0, 0, 2));

            // Deslizadores con el agarre grande y visible.
            var track = Box(new Color(0.02f, 0.03f, 0.04f), Edge, 1, 0, 3, 3);
            t.SetStylebox("slider", "HSlider", track);
            var filled = Box(new Color(0.7f, 0.46f, 0.16f), new Color(0, 0, 0, 0), 0, 0, 3, 3);
            t.SetStylebox("grabber_area", "HSlider", filled);
            t.SetStylebox("grabber_area_highlight", "HSlider", Box(Amber, new Color(0, 0, 0, 0), 0, 0, 3, 3));

            // Pestañas de ajustes.
            t.SetStylebox("panel", "TabContainer", Box(new Color(0.03f, 0.04f, 0.06f, 0.6f), Edge, 1, L, L));
            t.SetStylebox("tab_selected", "TabContainer", Box(SurfaceHover, Amber, 2, 16, 8));
            t.SetStylebox("tab_unselected", "TabContainer", Box(SurfaceRaised, Edge, 1, 16, 8));
            t.SetStylebox("tab_hovered", "TabContainer", Box(SurfaceHover, Edge, 1, 16, 8));
            t.SetStylebox("tab_focus", "TabContainer", Box(new Color(0, 0, 0, 0), Amber, 2, 16, 8));
            t.SetColor("font_selected_color", "TabContainer", Colors.White);
            t.SetColor("font_unselected_color", "TabContainer", Dim);
            t.SetColor("font_hovered_color", "TabContainer", Ink);

            t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.03f, 0.04f, 0.055f, 0.97f), Edge));
            t.SetColor("font_color", "TooltipLabel", Ink);
            t.SetStylebox("separator", "HSeparator", Box(Edge, new Color(0, 0, 0, 0), 0, 0, 0, 0));
            t.SetConstant("separation", "HSeparator", M);

            var grabber = Box(new Color(0.42f, 0.48f, 0.56f), new Color(0, 0, 0, 0), 0, 3, 3, 3);
            var grabberHot = Box(Amber, new Color(0, 0, 0, 0), 0, 3, 3, 3);
            var scrollTrack = Box(new Color(0.02f, 0.03f, 0.04f, 0.6f), new Color(0, 0, 0, 0), 0, 3, 3, 3);
            t.SetStylebox("scroll", "VScrollBar", scrollTrack);
            t.SetStylebox("grabber", "VScrollBar", grabber);
            t.SetStylebox("grabber_highlight", "VScrollBar", grabberHot);
            t.SetStylebox("grabber_pressed", "VScrollBar", grabberHot);
            _main = t;
            return t;
        }
    }
}
