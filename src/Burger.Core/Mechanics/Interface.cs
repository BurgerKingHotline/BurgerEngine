using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Script;
using Burger.Core.Scripting;

namespace Burger.Core.Mechanics;

public enum PanelElementKind { Text, Header, Separator, Button, Bar, List }

/// <summary>One line/button/bar of a custom panel. Either data (Node) or C# callbacks can drive it.</summary>
public sealed class PanelElement
{
    public PanelElementKind Kind;
    /// <summary>Text with [placeholders], e.g. "Prestige: [ROOT.prestige]".</summary>
    public string Text = "";
    public string? Tooltip;
    public Node? Potential;
    public Node? Allow;
    public Node? Effect;
    public Node? Value;
    public double Min, Max = 100;
    public Rgb Color = new(220, 190, 90);
    /// <summary>For lists: iterator name such as owned_state, character, ally.</summary>
    public string Iterator = "";
    public Node? Limit;
    public int MaxRows = 50;
    public List<PanelElement> Children = new();

    // C# alternatives (set from scripts)
    public Func<ScriptContext, string>? TextFn;
    public Func<ScriptContext, double>? ValueFn;
    public Func<ScriptContext, bool>? EnabledFn;
    public Action<ScriptContext>? OnClick;
}

/// <summary>A custom window opened from a top bar button.</summary>
public sealed class PanelDef
{
    public string Id = "";
    public string Title = "";
    public string ButtonText = "";
    public string? Hotkey;
    public int Width = 420;
    /// <summary>Optional icon for the button in the left menu bar (path relative to the game folder).</summary>
    public string? Icon;
    public Node? Potential;
    public List<PanelElement> Elements = new();
    public Func<ScriptContext, bool>? PotentialFn;
}

/// <summary>A theme color with alpha.</summary>
public readonly record struct ThemeColor(byte R, byte G, byte B, byte A = 255);

/// <summary>
/// The look of the in-game interface, from <c>theme = { ... }</c> in <c>interface/*.txt</c>.
/// Every value is optional; anything not given keeps the engine default. C# scripts can change it at runtime
/// (<c>Game.Interface.Theme.SetColor("panel", 20, 20, 30, 240)</c>) and the interface updates immediately.
/// </summary>
public sealed class ThemeDef
{
    /// <summary>Body text font (ttf/otf, relative to the game folder).</summary>
    public string? Font;
    /// <summary>Font for buttons and numbers.</summary>
    public string? BoldFont;
    /// <summary>Font for panel titles and country names on the map.</summary>
    public string? HeaderFont;
    public float FontScale = 1f;
    /// <summary>Multiplies the automatic interface scale (which depends on the screen height).</summary>
    public float UiScale = 1f;
    /// <summary>Optional 9-slice images (stretched with <see cref="TextureBorder"/> pixels kept at the edges).</summary>
    public string? PanelTexture, ButtonTexture, TopBarTexture;
    public int TextureBorder = 12;
    /// <summary>Draw the decorated (gradient, double border, corner) style. Off = flat panels.</summary>
    public bool Ornate = true;
    /// <summary>Colors by name: panel, panel_border, panel_inner_border, panel_header, top_bar, text, text_dim, title, good, bad,
    /// button, button_hover, button_selected, button_disabled, button_border, tooltip, menu_bar.</summary>
    public readonly Dictionary<string, ThemeColor> Colors = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Increases on every change so the interface knows to reload.</summary>
    public int Version { get; private set; }

    public void SetColor(string name, int r, int g, int b, int a = 255)
    {
        Colors[name] = new ThemeColor((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255), (byte)Math.Clamp(a, 0, 255));
        Version++;
    }

    /// <summary>Call after changing fonts, textures or scales from C#.</summary>
    public void Changed() => Version++;

    public static readonly string[] KnownColors =
    {
        "panel", "panel_border", "panel_inner_border", "panel_header", "top_bar", "text", "text_dim", "title", "good", "bad",
        "button", "button_hover", "button_selected", "button_disabled", "button_border", "tooltip", "menu_bar",
    };

    internal void Load(Node n)
    {
        Font = n.GetString("font", Font);
        BoldFont = n.GetString("bold_font", BoldFont);
        HeaderFont = n.GetString("header_font", HeaderFont);
        FontScale = (float)Math.Clamp(n.GetDouble("font_scale", FontScale), 0.5, 3);
        UiScale = (float)Math.Clamp(n.GetDouble("ui_scale", UiScale), 0.5, 3);
        PanelTexture = n.GetString("panel_texture", PanelTexture);
        ButtonTexture = n.GetString("button_texture", ButtonTexture);
        TopBarTexture = n.GetString("top_bar_texture", TopBarTexture);
        TextureBorder = n.GetInt("texture_border", TextureBorder);
        if (n.Get("ornate") is { } o) Ornate = o.Value is "yes" or "true" or "1";
        if (n.Get("colors") is { } colors)
            foreach (var c in colors.All)
            {
                var items = c.Items();
                if (!KnownColors.Contains(c.Key, StringComparer.OrdinalIgnoreCase))
                    Log.Warn($"Unknown theme color '{c.Key}' (known: {string.Join(", ", KnownColors)})", c.Location);
                if (Rgb.TryParse(c, out var rgb))
                {
                    int a = 255;
                    if (items.Count >= 4 && Node.TryParseDouble(items[3], out var av)) a = av <= 1.0 && items[3].Contains('.') ? (int)(av * 255) : (int)av;
                    SetColor(c.Key, rgb.R, rgb.G, rgb.B, a);
                }
                else Log.Error($"Theme color '{c.Key}' needs a color like {{ 30 30 40 }} or {{ 30 30 40 230 }}", c.Location);
            }
        Version++;
    }
}

/// <summary>A custom map mode: colors every location by a script value (location scope).</summary>
public sealed class MapModeDef
{
    public string Id = "";
    public string Name = "";
    public Node? Value;
    public double Min, Max = 1;
    public Rgb LowColor = new(200, 60, 50);
    public Rgb HighColor = new(60, 190, 80);
    /// <summary>C# alternative: return a color or null (= no color) for a location.</summary>
    public Func<Location, Rgb?>? ColorFn;
}

/// <summary>
/// Data-driven user interface for new mechanics, from <c>interface/*.txt</c>:
/// <code>
/// panel = {
///     id = court
///     title = "Royal Court"
///     button_text = "Court"          # top bar button
///     hotkey = F7
///     header = "Prestige"
///     bar = { text = "Prestige: [ROOT.prestige]" value = prestige min = 0 max = 100 }
///     text = "Ruler: [ROOT.ruler]"
///     button = { text = "Hold a feast" tooltip = "Costs 50 gold" allow = { gold >= 50 } effect = { add_gold = -50 add_prestige = 5 } }
///     separator = yes
///     list = { iterator = owned_state  text = "[THIS]: tax [THIS.tax]"  button = { text = "Invest" effect = { add_tax = 1 } } }
/// }
/// map_mode = { id = wealth name = "Wealth" value = { value = state.tax } min = 0 max = 8 }
/// </code>
/// C# scripts can add panels and map modes too: <c>Game.Interface.AddPanel(...)</c>.
/// </summary>
public sealed class InterfaceRegistry
{
    public readonly List<PanelDef> Panels = new();
    public readonly List<MapModeDef> MapModes = new();
    /// <summary>Fonts, colors and decoration of the in-game interface.</summary>
    public readonly ThemeDef Theme = new();

    public void AddPanel(PanelDef p)
    {
        Panels.RemoveAll(x => x.Id == p.Id);
        Panels.Add(p);
    }

    public void AddMapMode(MapModeDef m)
    {
        MapModes.RemoveAll(x => x.Id == m.Id);
        MapModes.Add(m);
    }

    public void Load(GameData data)
    {
        foreach (var root in DataLoader.ParseFileOrFolder(data, "interface"))
            foreach (var n in root.All)
            {
                if (n.Children == null) continue;
                switch (n.Key.ToLowerInvariant())
                {
                    case "panel": AddPanel(ParsePanel(n)); break;
                    case "map_mode": AddMapMode(ParseMapMode(n)); break;
                    case "theme": Theme.Load(n); break;
                    default: Log.Error($"Unknown interface entry '{n.Key}' (use panel, map_mode or theme)", n.Location); break;
                }
            }
    }

    private static PanelDef ParsePanel(Node n)
    {
        var p = new PanelDef
        {
            Id = n.GetString("id") ?? "panel_" + n.Line,
            Title = n.GetString("title") ?? n.GetString("name") ?? "Panel",
            Hotkey = n.GetString("hotkey"),
            Width = n.GetInt("width", 420),
            Icon = n.GetString("icon"),
            Potential = n.Get("potential"),
        };
        p.ButtonText = n.GetString("button_text") ?? p.Title;
        p.Elements = ParseElements(n);
        return p;
    }

    private static readonly HashSet<string> PanelKeys = new(StringComparer.OrdinalIgnoreCase)
        { "id", "title", "name", "hotkey", "width", "icon", "potential", "button_text", "iterator", "limit", "max_rows" };

    private static List<PanelElement> ParseElements(Node block)
    {
        var list = new List<PanelElement>();
        foreach (var c in block.All)
        {
            if (PanelKeys.Contains(c.Key)) continue;
            switch (c.Key.ToLowerInvariant())
            {
                case "text":
                    if (c.Value != null) list.Add(new PanelElement { Kind = PanelElementKind.Text, Text = c.Value });
                    else list.Add(new PanelElement { Kind = PanelElementKind.Text, Text = c.GetString("text", "")!, Tooltip = c.GetString("tooltip"), Potential = c.Get("potential") });
                    break;
                case "header":
                    list.Add(new PanelElement { Kind = PanelElementKind.Header, Text = c.Value ?? c.GetString("text", "")! });
                    break;
                case "separator":
                    list.Add(new PanelElement { Kind = PanelElementKind.Separator });
                    break;
                case "button":
                    list.Add(new PanelElement
                    {
                        Kind = PanelElementKind.Button,
                        Text = c.GetString("text", "Button")!,
                        Tooltip = c.GetString("tooltip"),
                        Potential = c.Get("potential"),
                        Allow = c.Get("allow"),
                        Effect = c.Get("effect"),
                    });
                    break;
                case "bar":
                    {
                        var e = new PanelElement
                        {
                            Kind = PanelElementKind.Bar,
                            Text = c.GetString("text", "")!,
                            Tooltip = c.GetString("tooltip"),
                            Potential = c.Get("potential"),
                            Value = c.Get("value"),
                            Min = c.GetDouble("min", 0),
                            Max = c.GetDouble("max", 100),
                        };
                        if (Rgb.TryParse(c.Get("color"), out var col)) e.Color = col;
                        list.Add(e);
                        break;
                    }
                case "list":
                    list.Add(new PanelElement
                    {
                        Kind = PanelElementKind.List,
                        Iterator = c.GetString("iterator", "")!,
                        Limit = c.Get("limit"),
                        MaxRows = c.GetInt("max_rows", 50),
                        Potential = c.Get("potential"),
                        Children = ParseElements(c),
                    });
                    break;
                default:
                    Log.Error($"Unknown panel element '{c.Key}' (text, header, separator, button, bar, list)", c.Location);
                    break;
            }
        }
        return list;
    }

    private static MapModeDef ParseMapMode(Node n)
    {
        var m = new MapModeDef
        {
            Id = n.GetString("id") ?? "mode_" + n.Line,
            Value = n.Get("value"),
            Min = n.GetDouble("min", 0),
            Max = n.GetDouble("max", 1),
        };
        m.Name = n.GetString("name") ?? Localisation.GetOrPretty(m.Id);
        if (Rgb.TryParse(n.Get("low_color"), out var lo)) m.LowColor = lo;
        if (Rgb.TryParse(n.Get("high_color"), out var hi)) m.HighColor = hi;
        return m;
    }

    /// <summary>Color of a location in a custom map mode (null = leave uncolored).</summary>
    public Rgb? MapModeColor(Game game, MapModeDef m, Location l)
    {
        if (m.ColorFn != null) return m.ColorFn(l);
        if (m.Value == null || l.IsWater) return null;
        double v = game.ScriptValues.Evaluate(m.Value, game.Context(l));
        double t = m.Max - m.Min == 0 ? 0 : Math.Clamp((v - m.Min) / (m.Max - m.Min), 0, 1);
        return m.LowColor.Lerp(m.HighColor, t);
    }
}
