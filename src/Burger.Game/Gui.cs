using System.Numerics;
using Burger.Core.Mechanics;
using Raylib_cs;

namespace Burger.Runtime;

public enum FontKind { Body, Bold, Header }

/// <summary>
/// A tiny immediate-mode GUI on top of raylib: panels, labels, buttons, tooltips, scroll lists.
/// Everything is drawn and handled in the same call, so UI code reads top-to-bottom.
/// Colors, fonts and optional panel/button images come from the theme (<c>interface/theme.txt</c>).
/// </summary>
public static class Gui
{
    // Theme colors (defaults below; interface/theme.txt or C# can change them)
    public static Color PanelBg, PanelBorder, PanelInner, PanelHeader, TopBar, Text, TextDim, Gold, Good, Bad;
    public static Color ButtonBg, ButtonHover, ButtonDisabled, ButtonBorder, Accent, TooltipBg, MenuBar;
    public static bool Ornate = true;
    private static Texture2D? _panelTex, _buttonTex, _topBarTex;
    private static int _texBorder = 12;
    // interface images by element name (theme textures = { ... }), with their 9-slice borders
    private static readonly Dictionary<string, (Texture2D Tex, int Border)> _skins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> DefaultBorders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["panel"] = 12, ["menu_bar"] = 12, ["panel_header"] = 6, ["top_bar"] = 6, ["tooltip"] = 6, ["button"] = 6, ["button_hover"] = 6,
        ["button_selected"] = 6, ["button_disabled"] = 6, ["close_button"] = 0, ["close_button_hover"] = 0, ["bar"] = 3, ["bar_fill"] = 3,
        ["text_field"] = 4, ["text_field_focused"] = 4, ["scrollbar"] = 2, ["scroll_thumb"] = 2, ["card"] = 4, ["card_selected"] = 4, ["section_line"] = 0,
    };

    /// <summary>True if the theme gives an image for this element.</summary>
    public static bool HasSkin(string name) => _skins.ContainsKey(name);

    /// <summary>Draws the element's image (9-slice) if the theme has one; false = draw it with colors instead.</summary>
    public static bool Skin(string name, Rectangle r, Color? tint = null)
    {
        if (!_skins.TryGetValue(name, out var sk)) return false;
        NinePatch(sk.Tex, r, tint ?? Color.White, sk.Border);
        return true;
    }
    private static float _fontScale = 1f;
    private static float _uiScale = 1f;
    private static int _themeVersion = -1;

    public static float Scale = 1f;
    public static Vector2 Mouse;
    private static bool _leftPressed, _rightPressed;
    private static readonly List<Rectangle> _blockers = new();
    private static readonly List<Rectangle> _prevBlockers = new();
    private static string? _tooltip;
    private static readonly Dictionary<string, float> _scroll = new();
    private static bool _wheelUsed;

    static Gui() => ResetTheme();

    /// <summary>True if the mouse was over any UI element last frame (map should ignore it).</summary>
    public static bool MouseOverUi { get; private set; }
    public static bool LeftClickConsumed { get; private set; }

    // -------------------------------------------------------------------------------------------- theme
    private static void ResetTheme()
    {
        PanelBg = new(26, 28, 32, 246);
        PanelBorder = new(168, 136, 72, 255);
        PanelInner = new(92, 78, 50, 255);
        PanelHeader = new(58, 46, 30, 255);
        TopBar = new(40, 34, 26, 250);
        Text = new(236, 230, 214, 255);
        TextDim = new(168, 162, 150, 255);
        Gold = new(236, 200, 100, 255);
        Good = new(126, 214, 120, 255);
        Bad = new(236, 104, 92, 255);
        ButtonBg = new(56, 58, 64, 255);
        ButtonHover = new(84, 74, 52, 255);
        ButtonDisabled = new(40, 41, 45, 255);
        ButtonBorder = new(96, 86, 66, 255);
        Accent = new(150, 112, 46, 255);
        TooltipBg = new(16, 16, 20, 248);
        MenuBar = new(22, 22, 26, 236);
        Ornate = true;
        _fontScale = 1f;
        _uiScale = 1f;
    }

    /// <summary>Applies a theme (call again whenever <see cref="ThemeDef.Version"/> changes).</summary>
    public static void ApplyTheme(ThemeDef theme)
    {
        _themeVersion = theme.Version;
        ResetTheme();
        Color Get(string name, Color def) => theme.Colors.TryGetValue(name, out var c) ? new Color(c.R, c.G, c.B, c.A) : def;
        PanelBg = Get("panel", PanelBg);
        PanelBorder = Get("panel_border", PanelBorder);
        PanelInner = Get("panel_inner_border", PanelInner);
        PanelHeader = Get("panel_header", PanelHeader);
        TopBar = Get("top_bar", TopBar);
        Text = Get("text", Text);
        TextDim = Get("text_dim", TextDim);
        Gold = Get("title", Gold);
        Good = Get("good", Good);
        Bad = Get("bad", Bad);
        ButtonBg = Get("button", ButtonBg);
        ButtonHover = Get("button_hover", ButtonHover);
        Accent = Get("button_selected", Accent);
        ButtonDisabled = Get("button_disabled", ButtonDisabled);
        ButtonBorder = Get("button_border", ButtonBorder);
        TooltipBg = Get("tooltip", TooltipBg);
        MenuBar = Get("menu_bar", MenuBar);
        Ornate = theme.Ornate;
        _fontScale = theme.FontScale;
        _uiScale = theme.UiScale;
        _texBorder = Math.Max(1, theme.TextureBorder);
        _skins.Clear();
        foreach (var name in ThemeDef.KnownTextures.Concat(theme.Textures.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = theme.TextureOf(name);
            if (path == null) continue;
            var tex = Assets.Texture(path);
            if (tex == null) { Burger.Core.Log.Warn($"Interface image for '{name}' not found: {path}"); continue; }
            bool legacy = name is "panel" or "button" or "top_bar" && !theme.Textures.ContainsKey(name);
            int border = theme.TextureBorders.TryGetValue(name, out var b) ? b : legacy ? _texBorder : DefaultBorders.GetValueOrDefault(name, 4);
            _skins[name] = (tex.Value, border);
        }
        _panelTex = _skins.TryGetValue("panel", out var pt) ? pt.Tex : null;
        _buttonTex = _skins.TryGetValue("button", out var bt) ? bt.Tex : null;
        _topBarTex = _skins.TryGetValue("top_bar", out var tt) ? tt.Tex : null;
        Assets.LoadFonts(theme);
    }

    /// <summary>Re-applies the theme if a script changed it.</summary>
    public static void CheckTheme(ThemeDef theme)
    {
        if (theme.Version != _themeVersion) ApplyTheme(theme);
    }

    /// <summary>Interface scale for a screen height, including the theme's ui_scale.</summary>
    public static float ScaleFor(int screenHeight) => Math.Clamp(screenHeight / 960f, 0.95f, 1.7f) * _uiScale;

    public static Color Lighter(Color c, float f) => new((byte)Math.Min(255, c.R + (255 - c.R) * f), (byte)Math.Min(255, c.G + (255 - c.G) * f), (byte)Math.Min(255, c.B + (255 - c.B) * f), c.A);
    public static Color Darker(Color c, float f) => new((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)), c.A);

    // -------------------------------------------------------------------------------------------- frame
    public static void BeginFrame()
    {
        Mouse = Raylib.GetMousePosition();
        _leftPressed = Raylib.IsMouseButtonPressed(MouseButton.Left);
        _rightPressed = Raylib.IsMouseButtonPressed(MouseButton.Right);
        _prevBlockers.Clear();
        _prevBlockers.AddRange(_blockers);
        _blockers.Clear();
        MouseOverUi = _prevBlockers.Any(r => Raylib.CheckCollisionPointRec(Mouse, r));
        _clips.Clear();
        _tooltip = null;
        LeftClickConsumed = false;
        _wheelUsed = false;
    }

    public static void EndFrame()
    {
        // a field that was not drawn this frame (its panel closed) loses focus
        if (!_fieldSeenThisFrame) _focusedField = null;
        _fieldSeenThisFrame = false;
        if (_tooltip != null) DrawTooltip(_tooltip);
    }

    /// <summary>Registers a rectangle as UI so map clicks below it are ignored.</summary>
    public static void Block(Rectangle r) => _blockers.Add(r);

    /// <summary>True if the mouse is over the rectangle (and inside the scroll area being drawn, so rows scrolled out of view cannot be clicked).</summary>
    public static bool Hover(Rectangle r) => Raylib.CheckCollisionPointRec(Mouse, r) && (_clips.Count == 0 || Raylib.CheckCollisionPointRec(Mouse, _clips.Peek()));

    private static readonly Stack<Rectangle> _clips = new();

    public static bool Clicked(Rectangle r)
    {
        if (_leftPressed && !LeftClickConsumed && Hover(r))
        {
            LeftClickConsumed = true;
            return true;
        }
        return false;
    }

    public static bool RightClicked(Rectangle r) => _rightPressed && Hover(r);

    public static void Tooltip(string text) => _tooltip = text;

    // -------------------------------------------------------------------------------------------- text
    /// <summary>Text is drawn a bit larger than the layout units so it stays readable on 1080p screens.</summary>
    public const float BaseTextBoost = 1.12f;

    public static float FontSize(float size) => size * Scale * _fontScale * BaseTextBoost;

    private static Font FontOf(FontKind k) => k switch { FontKind.Bold => Assets.BoldFont, FontKind.Header => Assets.HeaderFont, _ => Assets.Font };
    private static float Spacing(FontKind k, float size) => Assets.CustomFont ? (k == FontKind.Header ? size * 0.04f : 0.3f) : 1f;

    public static Vector2 Measure(string text, float size, FontKind font = FontKind.Body)
        => Raylib.MeasureTextEx(FontOf(font), text, FontSize(size), Spacing(font, FontSize(size)));

    public static void Label(string text, float x, float y, float size = 18, Color? color = null, FontKind font = FontKind.Body)
        => Raylib.DrawTextEx(FontOf(font), text, new Vector2(MathF.Round(x), MathF.Round(y)), FontSize(size), Spacing(font, FontSize(size)), color ?? Text);

    public static void LabelShadow(string text, float x, float y, float size, Color color, FontKind font = FontKind.Body)
    {
        var shadow = new Color(0, 0, 0, (int)Math.Min(190, (int)color.A));
        float o = Math.Max(1f, FontSize(size) / 22f);
        Label(text, x + o, y + o, size, shadow, font);
        Label(text, x, y, size, color, font);
    }

    public static void LabelCentered(string text, Rectangle r, float size = 18, Color? color = null, FontKind font = FontKind.Body)
    {
        var m = Measure(text, size, font);
        Label(text, r.X + (r.Width - m.X) / 2, r.Y + (r.Height - m.Y) / 2, size, color, font);
    }

    /// <summary>Shortens text with "..." so it fits a width.</summary>
    public static string Fit(string text, float width, float size, FontKind font = FontKind.Body)
    {
        if (Measure(text, size, font).X <= width) return text;
        int n = text.Length;
        while (n > 1 && Measure(text[..n] + "...", size, font).X > width) n--;
        return text[..n] + "...";
    }

    /// <summary>Word-wraps text into a width. Returns the height used.</summary>
    public static float Wrapped(string text, float x, float y, float width, float size = 18, Color? color = null)
    {
        float lineH = FontSize(size) * 1.2f;
        float cy = y;
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var words = para.Split(' ');
            string line = "";
            foreach (var w in words)
            {
                string test = line.Length == 0 ? w : line + " " + w;
                if (Measure(test, size).X > width && line.Length > 0)
                {
                    Label(line, x, cy, size, color);
                    cy += lineH;
                    line = w;
                }
                else line = test;
            }
            Label(line, x, cy, size, color);
            cy += lineH;
        }
        return cy - y;
    }

    public static float WrappedHeight(string text, float width, float size = 18)
    {
        float lineH = FontSize(size) * 1.2f;
        int lines = 0;
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var words = para.Split(' ');
            string line = "";
            lines++;
            foreach (var w in words)
            {
                string test = line.Length == 0 ? w : line + " " + w;
                if (Measure(test, size).X > width && line.Length > 0) { lines++; line = w; }
                else line = test;
            }
        }
        return lines * lineH;
    }

    // -------------------------------------------------------------------------------------------- drawing helpers
    public static void Gradient(Rectangle r, Color top, Color bottom)
        => Raylib.DrawRectangleGradientV((int)MathF.Round(r.X), (int)MathF.Round(r.Y), (int)MathF.Round(r.Width), (int)MathF.Round(r.Height), top, bottom);

    private static void NinePatch(Texture2D tex, Rectangle r, Color tint, int border = -1)
    {
        if (border < 0) border = _texBorder;
        if (border == 0) { Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), r, Vector2.Zero, 0, tint); return; }
        int b = Math.Max(1, Math.Min(border, Math.Min(tex.Width, tex.Height) / 2 - 1));
        var info = new NPatchInfo { Source = new Rectangle(0, 0, tex.Width, tex.Height), Left = b, Top = b, Right = b, Bottom = b, Layout = NPatchLayout.NinePatch };
        Raylib.DrawTextureNPatch(tex, info, r, Vector2.Zero, 0, tint);
    }

    private static void Diamond(Vector2 c, float radius, Color col) => Raylib.DrawPoly(c, 4, radius, 45, col);

    /// <summary>The decorated frame used by panels: gradient, dark outer line, gold inner line and corner studs.</summary>
    public static void Frame(Rectangle r, Color bg, bool shadow = true, string skin = "panel")
    {
        if (shadow) Raylib.DrawRectangleRec(new Rectangle(r.X + 4, r.Y + 5, r.Width, r.Height), new Color(0, 0, 0, 80));
        if (Skin(skin, r)) return;
        if (skin != "panel" && Skin("panel", r)) return;
        if (!Ornate)
        {
            Raylib.DrawRectangleRec(r, bg);
            Raylib.DrawRectangleLinesEx(r, 1.5f, PanelBorder);
            return;
        }
        Gradient(r, Lighter(bg, 0.05f), Darker(bg, 0.18f));
        Raylib.DrawRectangleLinesEx(r, 1, new Color(0, 0, 0, 210));
        float i = 3 * Scale;
        var inner = new Rectangle(r.X + i, r.Y + i, r.Width - 2 * i, r.Height - 2 * i);
        Raylib.DrawRectangleLinesEx(inner, 1, PanelInner);
        Raylib.DrawRectangleLinesEx(new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), 1, Darker(PanelBorder, 0.35f));
        float d = 3.2f * Scale;
        Diamond(new Vector2(inner.X, inner.Y), d, PanelBorder);
        Diamond(new Vector2(inner.X + inner.Width, inner.Y), d, PanelBorder);
        Diamond(new Vector2(inner.X, inner.Y + inner.Height), d, PanelBorder);
        Diamond(new Vector2(inner.X + inner.Width, inner.Y + inner.Height), d, PanelBorder);
    }

    /// <summary>The top bar background.</summary>
    public static void TopBarBackground(Rectangle r)
    {
        if (Skin("top_bar", r)) return;
        Gradient(r, Lighter(TopBar, 0.08f), Darker(TopBar, 0.45f));
        Raylib.DrawLineEx(new Vector2(r.X, r.Y + r.Height - 1), new Vector2(r.X + r.Width, r.Y + r.Height - 1), 2, PanelBorder);
        Raylib.DrawLineEx(new Vector2(r.X, r.Y + r.Height + 1), new Vector2(r.X + r.Width, r.Y + r.Height + 1), 1, new Color(0, 0, 0, 160));
    }

    /// <summary>A section title inside a panel: header font with a thin line under it.</summary>
    public static void Section(string text, float x, float y, float width)
    {
        Label(text, x, y, 17, Gold, FontKind.Header);
        float ly = y + FontSize(17) * 1.12f;
        if (Skin("section_line", new Rectangle(x, ly - 1.5f * Scale, width, 3 * Scale))) return;
        Raylib.DrawLineEx(new Vector2(x, ly), new Vector2(x + width, ly), 1, new Color(PanelInner.R, PanelInner.G, PanelInner.B, (byte)200));
    }

    // -------------------------------------------------------------------------------------------- widgets
    public static float HeaderHeight => 30 * Scale;

    public static void Panel(Rectangle r, string? title = null)
    {
        Block(r);
        Frame(r, PanelBg);
        if (title != null)
        {
            float i = 4 * Scale;
            var hr = new Rectangle(r.X + i, r.Y + i, r.Width - 2 * i, HeaderHeight - i);
            if (!Skin("panel_header", hr))
            {
                if (_panelTex == null) Gradient(hr, Lighter(PanelHeader, 0.10f), Darker(PanelHeader, 0.25f));
                Raylib.DrawLineEx(new Vector2(hr.X, hr.Y + hr.Height), new Vector2(hr.X + hr.Width, hr.Y + hr.Height), 1.5f, PanelBorder);
            }
            string t = Fit(title, hr.Width - 44 * Scale, 19, FontKind.Header);
            var m = Measure(t, 19, FontKind.Header);
            LabelShadow(t, hr.X + 9 * Scale, hr.Y + (hr.Height - m.Y) / 2, 19, Gold, FontKind.Header);
        }
    }

    /// <summary>A small × button in the top-right of a panel; returns true when clicked.</summary>
    public static bool CloseButton(Rectangle panel)
    {
        float s = 20 * Scale;
        var r = new Rectangle(panel.X + panel.Width - s - 8 * Scale, panel.Y + 4 * Scale + (HeaderHeight - 4 * Scale - s) / 2, s, s);
        bool hover = Hover(r);
        if (!(hover && Skin("close_button_hover", r)) && !Skin("close_button", r, hover ? new Color(255, 200, 190, 255) : null))
        {
            Gradient(r, hover ? Lighter(Bad, 0.1f) : Lighter(ButtonBg, 0.1f), hover ? Darker(Bad, 0.3f) : Darker(ButtonBg, 0.2f));
            Raylib.DrawRectangleLinesEx(r, 1, hover ? Gold : ButtonBorder);
            float p = 6 * Scale;
            Raylib.DrawLineEx(new Vector2(r.X + p, r.Y + p), new Vector2(r.X + r.Width - p, r.Y + r.Height - p), 2 * Scale, Text);
            Raylib.DrawLineEx(new Vector2(r.X + r.Width - p, r.Y + p), new Vector2(r.X + p, r.Y + r.Height - p), 2 * Scale, Text);
        }
        if (hover) Tooltip("Close (Esc)");
        return Clicked(r);
    }

    public static void ButtonFrame(Rectangle r, bool enabled, bool hover, bool selected)
    {
        var bg = !enabled ? ButtonDisabled : selected ? Accent : hover ? ButtonHover : ButtonBg;
        string state = !enabled ? "button_disabled" : selected ? "button_selected" : hover ? "button_hover" : "button";
        if (state != "button" && Skin(state, r)) return;
        if (_buttonTex != null)
        {
            NinePatch(_buttonTex.Value, r, !enabled ? new Color(140, 140, 140, 255) : selected ? Lighter(Accent, 0.4f) : hover ? new Color(255, 240, 210, 255) : Color.White);
            return;
        }
        if (Ornate)
        {
            Gradient(r, Lighter(bg, 0.10f), Darker(bg, 0.28f));
            Raylib.DrawLineEx(new Vector2(r.X + 1, r.Y + 1.5f), new Vector2(r.X + r.Width - 1, r.Y + 1.5f), 1, new Color(255, 255, 255, enabled ? 34 : 12));
        }
        else Raylib.DrawRectangleRec(r, bg);
        Raylib.DrawRectangleLinesEx(r, 1, hover && enabled ? Gold : selected ? PanelBorder : ButtonBorder);
    }

    public static bool Button(Rectangle r, string text, bool enabled = true, string? tooltip = null, bool selected = false, float size = 17)
    {
        bool hover = Hover(r);
        ButtonFrame(r, enabled, hover, selected);
        string t = Fit(text, r.Width - 8 * Scale, size, FontKind.Bold);
        LabelCentered(t, r, size, enabled ? (selected ? Color.White : Text) : TextDim, FontKind.Bold);
        if (hover && tooltip != null) Tooltip(tooltip);
        else if (hover && t != text) Tooltip(text);
        Block(r);
        return enabled && Clicked(r);
    }

    // -------------------------------------------------------------------------------------------- text fields
    private static string? _focusedField;
    private static bool _fieldSeenThisFrame;

    /// <summary>True while a text field has keyboard focus (shortcuts and camera keys should be ignored).</summary>
    public static bool TextFocused => _focusedField != null;

    /// <summary>A one-line text field. Click to type, Enter/Esc or clicking elsewhere to finish. Returns true while focused.</summary>
    public static bool TextField(string id, Rectangle r, ref string value, int maxLength = 60, string hint = "")
    {
        bool hover = Hover(r);
        if (_leftPressed && hover && !LeftClickConsumed) { _focusedField = id; LeftClickConsumed = true; }
        else if (_leftPressed && !hover && _focusedField == id) _focusedField = null;
        bool focused = _focusedField == id;
        if (focused)
        {
            _fieldSeenThisFrame = true;
            int ch;
            while ((ch = Raylib.GetCharPressed()) > 0)
                if (value.Length < maxLength && ch >= 32) value += char.ConvertFromUtf32(ch);
            if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && value.Length > 0) value = value[..^1];
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter) || Raylib.IsKeyPressed(KeyboardKey.Escape)) _focusedField = null;
        }
        if (!(focused && Skin("text_field_focused", r)) && !Skin("text_field", r, focused ? new Color(255, 236, 190, 255) : null))
        {
            Raylib.DrawRectangleRec(r, new Color(12, 12, 14, 230));
            Raylib.DrawRectangleLinesEx(r, 1, focused ? Gold : hover ? Lighter(ButtonBorder, 0.3f) : ButtonBorder);
        }
        string shown = value.Length == 0 && !focused ? hint : value;
        var m = Measure(shown, 16);
        float tx = r.X + 6 * Scale, ty = r.Y + (r.Height - m.Y) / 2;
        Raylib.BeginScissorMode((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
        Label(shown, tx, ty, 16, value.Length == 0 && !focused ? TextDim : Text);
        if (focused && (int)(Raylib.GetTime() * 2) % 2 == 0)
            Raylib.DrawRectangle((int)(tx + Measure(value, 16).X + 1), (int)(ty + 2), 2, (int)(m.Y > 0 ? m.Y - 4 : FontSize(16)), Gold);
        Raylib.EndScissorMode();
        Block(r);
        return _focusedField == id;
    }

    private static string? _dragSlider;

    /// <summary>A horizontal slider from <paramref name="min"/> to <paramref name="max"/>. Returns true while the value changes.</summary>
    public static bool Slider(string id, Rectangle r, ref double value, double min, double max)
    {
        bool hover = Hover(r);
        if (_leftPressed && hover && !LeftClickConsumed) { _dragSlider = id; LeftClickConsumed = true; }
        if (!Raylib.IsMouseButtonDown(MouseButton.Left)) _dragSlider = null;
        bool changed = false;
        if (_dragSlider == id)
        {
            double t = Math.Clamp((Mouse.X - r.X) / Math.Max(1, r.Width), 0, 1);
            double v = min + (max - min) * t;
            changed = Math.Abs(v - value) > 1e-9;
            value = v;
        }
        float f = (float)Math.Clamp((value - min) / Math.Max(1e-9, max - min), 0, 1);
        var track = new Rectangle(r.X, r.Y + r.Height / 2 - 3 * Scale, r.Width, 6 * Scale);
        Raylib.DrawRectangleRec(track, new Color(12, 12, 14, 230));
        Raylib.DrawRectangleRec(new Rectangle(track.X, track.Y, track.Width * f, track.Height), Gold);
        Raylib.DrawRectangleLinesEx(track, 1, ButtonBorder);
        var knob = new Rectangle(r.X + r.Width * f - 5 * Scale, r.Y + 2 * Scale, 10 * Scale, r.Height - 4 * Scale);
        Raylib.DrawRectangleRec(knob, hover || _dragSlider == id ? Lighter(Gold, 0.3f) : Gold);
        Raylib.DrawRectangleLinesEx(knob, 1, ButtonBorder);
        Block(r);
        return changed;
    }

    public static void Bar(Rectangle r, float fraction, Color fill, Color? back = null)
    {
        bool skinned = back == null && Skin("bar", r);
        if (!skinned) Raylib.DrawRectangleRec(r, back ?? new Color(14, 14, 16, 230));
        var f = new Rectangle(r.X, r.Y, r.Width * Math.Clamp(fraction, 0, 1), r.Height);
        if (f.Width >= 1 && !Skin("bar_fill", f, fill)) Gradient(f, Lighter(fill, 0.25f), Darker(fill, 0.2f));
        if (!skinned) Raylib.DrawRectangleLinesEx(r, 1, new Color(0, 0, 0, 160));
    }

    /// <summary>Card background for list rows inside panels (theme images card / card_selected, tinted with the row color).</summary>
    public static void Card(Rectangle r, Color bg, bool highlight = false)
    {
        if (highlight && Skin("card_selected", r)) return;
        if (Skin("card", r, new Color((byte)Math.Min(255, bg.R * 2.2f + 30), (byte)Math.Min(255, bg.G * 2.2f + 30), (byte)Math.Min(255, bg.B * 2.2f + 30), (byte)255))) return;
        Gradient(r, Lighter(bg, 0.06f), Darker(bg, 0.15f));
        Raylib.DrawRectangleLinesEx(r, 1, highlight ? PanelBorder : new Color(0, 0, 0, 150));
    }

    public static void Image(Texture2D tex, Rectangle dst, Color? tint = null)
        => Raylib.DrawTexturePro(tex, new Rectangle(0, 0, tex.Width, tex.Height), dst, Vector2.Zero, 0, tint ?? Color.White);

    public static void Flag(Burger.Core.Model.Country c, Rectangle dst)
    {
        Image(Assets.Flag(c), dst);
        // a little shine and a dark frame make flags look like cloth on a plaque
        Gradient(new Rectangle(dst.X, dst.Y, dst.Width, dst.Height * 0.5f), new Color(255, 255, 255, 40), new Color(255, 255, 255, 0));
        Raylib.DrawRectangleLinesEx(dst, 1, new Color(0, 0, 0, 200));
    }

    /// <summary>
    /// A vertical scroll area. Call <see cref="BeginScroll"/>, draw rows at the returned y offset, then <see cref="EndScroll"/>.
    /// </summary>
    public static float BeginScroll(string id, Rectangle area, float contentHeight)
    {
        Block(area);
        float off = _scroll.TryGetValue(id, out var o) ? o : 0;
        float max = Math.Max(0, contentHeight - area.Height);
        if (!_wheelUsed && Hover(area))
        {
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0) { off -= wheel * 40 * Scale; _wheelUsed = true; }
        }
        off = Math.Clamp(off, 0, max);
        _scroll[id] = off;
        Raylib.BeginScissorMode((int)area.X, (int)area.Y, (int)area.Width, (int)area.Height);
        _clips.Push(area);
        if (max > 0)
        {
            float h = area.Height * area.Height / contentHeight;
            float y = area.Y + (area.Height - h) * (off / max);
            var track = new Rectangle(area.X + area.Width - 5 * Scale, area.Y, 4 * Scale, area.Height);
            var thumb = new Rectangle(track.X, y, track.Width, h);
            if (!Skin("scrollbar", track)) Raylib.DrawRectangleRec(track, new Color(0, 0, 0, 90));
            if (!Skin("scroll_thumb", thumb)) Raylib.DrawRectangleRec(thumb, new Color(PanelBorder.R, PanelBorder.G, PanelBorder.B, (byte)190));
        }
        return area.Y - off;
    }

    public static void EndScroll()
    {
        Raylib.EndScissorMode();
        if (_clips.Count > 0) _clips.Pop();
    }

    public static void ResetScroll(string id) => _scroll.Remove(id);

    /// <summary>True if the scroll wheel was used by a UI scroll area this frame (map zoom should ignore it).</summary>
    public static bool WheelUsed => _wheelUsed;

    private static void DrawTooltip(string text)
    {
        float maxW = 420 * Scale;
        float h = WrappedHeight(text, maxW - 20 * Scale, 16) + 12 * Scale;
        float w = Math.Min(maxW, text.Split('\n').Max(l => Measure(l, 16).X) + 22 * Scale);
        float x = Mouse.X + 18, y = Mouse.Y + 18;
        if (x + w > Raylib.GetScreenWidth()) x = Raylib.GetScreenWidth() - w - 4;
        if (y + h > Raylib.GetScreenHeight()) y = Mouse.Y - h - 6;
        var r = new Rectangle(x, y, w, h);
        Raylib.DrawRectangleRec(new Rectangle(x + 3, y + 4, w, h), new Color(0, 0, 0, 90));
        if (!Skin("tooltip", r))
        {
            Raylib.DrawRectangleRec(r, TooltipBg);
            Raylib.DrawRectangleLinesEx(r, 1, PanelBorder);
        }
        Wrapped(text, x + 10 * Scale, y + 6 * Scale, w - 20 * Scale, 16);
    }
}
