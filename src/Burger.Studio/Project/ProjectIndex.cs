using Burger.Core;
using Burger.Core.Model;
using Burger.Core.Script;

namespace Burger.Studio;

/// <summary>Names used by drop-down lists (tags, locations, characters...), read from the cached project files.</summary>
public sealed class ProjectIndex
{
    private readonly ProjectStore _store;
    private bool _valid;
    private List<string> _tags = new(), _locations = new(), _characters = new(), _traits = new(), _stats = new(),
        _terrains = new(), _templates = new(), _modifiers = new(), _events = new(), _states = new();
    private readonly Dictionary<string, (string Text, string File)> _loc = new(StringComparer.OrdinalIgnoreCase);

    public ProjectIndex(ProjectStore store)
    {
        _store = store;
        store.Changed += (_, _) => _valid = false;
    }

    public IReadOnlyList<string> Tags { get { Ensure(); return _tags; } }
    public IReadOnlyList<string> Locations { get { Ensure(); return _locations; } }
    public IReadOnlyList<string> Characters { get { Ensure(); return _characters; } }
    public IReadOnlyList<string> Traits { get { Ensure(); return _traits; } }
    public IReadOnlyList<string> CharacterStats { get { Ensure(); return _stats; } }
    public IReadOnlyList<string> Terrains { get { Ensure(); return _terrains; } }
    public IReadOnlyList<string> DivisionTemplates { get { Ensure(); return _templates; } }
    public IReadOnlyList<string> Modifiers { get { Ensure(); return _modifiers; } }
    public IReadOnlyList<string> Events { get { Ensure(); return _events; } }
    public IReadOnlyList<string> States { get { Ensure(); return _states; } }

    public string LocationsFile
    {
        get
        {
            var map = _store.Parse("map/default.map");
            return "map/" + (map.GetString("locations") ?? "locations.txt");
        }
    }

    /// <summary>Localised text of a key, or null.</summary>
    public string? Text(string key) { Ensure(); return _loc.TryGetValue(key, out var t) ? t.Text : null; }
    /// <summary>The localisation file that defines a key, or null.</summary>
    public string? TextFile(string key) { Ensure(); return _loc.TryGetValue(key, out var t) ? t.File : null; }

    public string Pretty(string key) => Text(key) ?? Localisation.Prettify(key);

    /// <summary>Sets a localisation text (in the file that already has it, or localisation/editor.txt).</summary>
    public void SetText(string key, string text, object? source = null)
    {
        string file = TextFile(key) ?? "localisation/editor.txt";
        if (!_store.Exists(file)) _store.Set(file, "# Texts added by BurgerEditor. key = \"text\"\n", source);
        _store.Edit(file, t => ScriptPatch.Set(t, file, new[] { key }, new Node(key, text) { ValueQuoted = true }), source);
    }

    private void Ensure()
    {
        if (_valid) return;
        _valid = true;
        _tags = Keys("common/countries").Where(Country.IsValidTag).ToList();
        _characters = Keys("history/characters");
        _traits = Keys("common/traits");
        _terrains = Keys("common/terrain");
        _modifiers = Keys("common/modifiers");
        _states = Keys("map/states");
        _templates = new List<string>();
        foreach (var f in _store.FilesOf("common/units"))
            foreach (var n in _store.Parse(f).GetAll("division_templates"))
                _templates.AddRange(n.All.Where(c => c.Children != null).Select(c => c.Key));
        _events = new List<string>();
        foreach (var f in _store.FilesOf("events"))
            foreach (var n in _store.Parse(f).All)
                if (n.GetString("id") is { } id) _events.Add(id);
        _stats = new List<string>();
        foreach (var f in _store.FilesOf("common/defines"))
            _stats.AddRange(_store.Parse(f).GetList("character_stats"));
        if (_stats.Count == 0) _stats.AddRange(new[] { "martial", "finesse", "charisma", "zeal" });

        _locations = new List<string>();
        foreach (var n in _store.Parse(LocationsFile).All)
        {
            if (Rgb.TryParseHex(n.Key, out _)) { var nm = n.Value ?? n.GetString("name"); if (nm != null) _locations.Add(nm); }
            else if (n.Value != null && Rgb.TryParseHex(n.Value, out _)) _locations.Add(n.Key);
        }
        _locations.Sort(StringComparer.OrdinalIgnoreCase);

        _loc.Clear();
        foreach (var f in _store.FilesOf("localisation"))
            foreach (var n in _store.Parse(f).All)
            {
                if (n.Value != null) _loc[n.Key] = (n.Value, f);
                else if (n.Children != null)
                    foreach (var c in n.All) if (c.Value != null) _loc[c.Key] = (c.Value, f);
            }
    }

    private List<string> Keys(string source)
    {
        var list = new List<string>();
        foreach (var f in _store.FilesOf(source))
            foreach (var n in _store.Parse(f).All)
                if (n.Children != null && n.Op != Op.None && !n.Key.StartsWith('@')) list.Add(n.Key);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
