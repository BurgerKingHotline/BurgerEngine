using Burger.Core.Data;
using Burger.Core.Model;
using Burger.Core.Modding;
using Burger.Core.Script;

namespace Burger.Core.Warfare;

/// <summary>Overridable division designer rules. Subclass and assign to <c>Game.Designer.Rules</c>.</summary>
public class DesignerRules
{
    public virtual int MaxBattalions(Game game, Country c) => (int)game.Data.Defines.Get("designer_max_battalions", 25);

    /// <summary>Why a design is not allowed (null = fine).</summary>
    public virtual string? CannotSaveReason(Game game, Country c, string name, IReadOnlyList<(UnitType Type, int Count)> units)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Give the division a name";
        int total = units.Sum(u => u.Count);
        if (total <= 0) return "Add at least one battalion";
        int max = MaxBattalions(game, c);
        if (total > max) return $"At most {max} battalions";
        return null;
    }

    /// <summary>Unit types the country may use in the designer (all by default; scripts can add requirements).</summary>
    public virtual bool CanUse(Game game, Country c, UnitType u)
        => u.Definition.Get("potential") is not { } p || game.Script.EvalTrigger(p, game.Context(c));
}

/// <summary>
/// Division templates made in the game (division_designer feature). They belong to one country, are stored in the
/// save and work like templates from the game files. Changing a template updates its divisions (their share of
/// strength is kept).
/// </summary>
public sealed class DesignerSystem
{
    private readonly Game _game;
    public DesignerRules Rules = new();

    public DesignerSystem(Game game)
    {
        _game = game;
        game.SaveSections.Add(new SaveSection { Name = "templates", Early = true, Write = Save, Read = Load, Reset = Reset });
    }

    public IEnumerable<DivisionTemplate> TemplatesOf(Country c)
        => _game.Data.Templates.Values.Where(t => string.IsNullOrEmpty(t.OwnerTag) || t.OwnerTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase));

    public bool CanEdit(Country c, DivisionTemplate t) => t.Custom && t.OwnerTag.Equals(c.Tag, StringComparison.OrdinalIgnoreCase);

    public string? CannotSaveReason(Country c, string name, IReadOnlyList<(UnitType Type, int Count)> units) => Rules.CannotSaveReason(_game, c, name, units);

    /// <summary>Stats, equipment and costs a design would have (for the designer preview).</summary>
    public DivisionTemplate Preview(string name, IReadOnlyList<(UnitType Type, int Count)> units)
    {
        var t = new DivisionTemplate { Key = "preview", Name = name };
        t.Units.AddRange(units.Where(u => u.Count > 0));
        if (t.Units.Count > 0) DataLoader.AggregateStats(_game.Data, t);
        return t;
    }

    public DivisionTemplate? Create(Country c, string name, IReadOnlyList<(UnitType Type, int Count)> units)
    {
        if (CannotSaveReason(c, name, units) != null) return null;
        int n = 1;
        string key;
        do key = $"{c.Tag.ToLowerInvariant()}_design_{n++}"; while (_game.Data.Templates.ContainsKey(key));
        var t = new DivisionTemplate { Key = key, Name = name.Trim(), OwnerTag = c.Tag, Custom = true };
        t.Units.AddRange(units.Where(u => u.Count > 0));
        DataLoader.AggregateStats(_game.Data, t);
        _game.Data.Templates[key] = t;
        return t;
    }

    /// <summary>Changes a custom template. Divisions using it keep their share of strength and organisation.</summary>
    public bool Update(Country c, DivisionTemplate t, string name, IReadOnlyList<(UnitType Type, int Count)> units)
    {
        if (!CanEdit(c, t) || CannotSaveReason(c, name, units) != null) return false;
        var divisions = _game.World.Divisions.Where(d => d.Template == t).Select(d => (D: d, S: d.StrengthFraction, O: d.OrgFraction)).ToList();
        t.Name = name.Trim();
        t.Units.Clear();
        t.Units.AddRange(units.Where(u => u.Count > 0));
        DataLoader.AggregateStats(_game.Data, t);
        foreach (var (d, s, o) in divisions)
        {
            d.Strength = d.MaxStrength * s;
            d.Org = d.MaxOrg * o;
        }
        return true;
    }

    /// <summary>Deletes a custom template that no division or training order uses.</summary>
    public string? Delete(Country c, DivisionTemplate t)
    {
        if (!CanEdit(c, t)) return "Only your own designs can be deleted";
        if (_game.World.Divisions.Any(d => d.Template == t)) return "Divisions use this template";
        if (_game.World.Countries.Any(x => x.Recruitment.Any(o => o.Template == t))) return "Divisions are training with this template";
        _game.Data.Templates.Remove(t.Key);
        return null;
    }

    private void Save(Node root)
    {
        foreach (var t in _game.Data.Templates.Values.Where(t => t.Custom))
        {
            var n = root.AddBlock(t.Key);
            n.Add("name", t.Name, true);
            n.Add("owner", t.OwnerTag);
            var u = n.AddBlock("units");
            foreach (var (type, count) in t.Units) u.Add(type.Key, count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private void Reset()
    {
        foreach (var k in _game.Data.Templates.Values.Where(t => t.Custom).Select(t => t.Key).ToList()) _game.Data.Templates.Remove(k);
    }

    private void Load(Node root)
    {
        foreach (var n in root.All)
        {
            if (n.Children == null) continue;
            var t = new DivisionTemplate { Key = n.Key, Name = n.GetString("name", n.Key)!, OwnerTag = n.GetString("owner", "")!, Custom = true };
            foreach (var u in n.Get("units")?.All ?? Enumerable.Empty<Node>())
                if (_game.Data.UnitTypes.TryGetValue(u.Key, out var ut)) t.Units.Add((ut, (int)u.AsDouble(1)));
            if (t.Units.Count == 0) continue;
            DataLoader.AggregateStats(_game.Data, t);
            _game.Data.Templates[t.Key] = t;
        }
    }
}
