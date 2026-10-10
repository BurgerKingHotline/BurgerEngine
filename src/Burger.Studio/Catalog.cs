using Burger.Core.Script;

namespace Burger.Studio;

/// <summary>One script block of the project (a country, a decision, an event...).</summary>
public sealed class BlockRef
{
    public BlockSpec Spec = null!;
    public string File = "";
    /// <summary>ScriptPatch path (#index based, so it also works for repeated keys like events).</summary>
    public string[] Path = Array.Empty<string>();
    public string Id = "";
    /// <summary>Statement key (country_event, panel...).</summary>
    public string Type = "";
    public string Display = "";
    public int Line;

    public string Label => Display.Length > 0 && !Display.Equals(Id, StringComparison.OrdinalIgnoreCase) ? $"{Id}  -  {Display}" : Id;
    public bool Same(BlockRef? o) => o != null && o.Spec == Spec && o.File == File && o.Id == Id;
}

/// <summary>Lists the blocks of every kind (cached until a file changes).</summary>
public sealed class Catalog
{
    private readonly ProjectStore _store;
    private readonly ProjectIndex _index;
    private readonly Dictionary<BlockSpec, List<BlockRef>> _cache = new();

    /// <summary>Every kind of block the editor knows, in outliner order.</summary>
    public static readonly BlockSpec[] Kinds =
    {
        Specs.Countries, Specs.Characters, Specs.Resources, Specs.Modifiers, Specs.Decisions, Specs.ScriptValues, Specs.Interface,
        Specs.Events, Specs.DiplomaticActions, Specs.CharacterInteractions, Specs.CasusBelli, Specs.SubjectTypes, Specs.Traits,
        Specs.Terrain, Specs.UnitTypes, Specs.DivisionTemplates, Specs.Equipment, Specs.ShipTypes, Specs.PlaneTypes, Specs.Buildings, Specs.OnActions,
        Specs.Ideologies, Specs.Governments, Specs.LawGroups, Specs.CabinetPosts,
    };

    public Catalog(ProjectStore store, ProjectIndex index)
    {
        _store = store;
        _index = index;
        store.Changed += (rel, _) =>
        {
            if (rel == "*") { _cache.Clear(); return; }
            foreach (var k in _cache.Keys.ToList())
                if (rel.StartsWith(k.Source, StringComparison.OrdinalIgnoreCase)) _cache.Remove(k);
        };
    }

    public void Invalidate() => _cache.Clear();

    public List<BlockRef> Of(BlockSpec spec)
    {
        if (_cache.TryGetValue(spec, out var list)) return list;
        list = new List<BlockRef>();
        foreach (var file in _store.FilesOf(spec.Source))
        {
            var root = _store.Parse(file);
            var parents = new List<(Node Parent, string[] Prefix)>();
            if (spec.Container != null)
            {
                for (int i = 0; i < root.Children!.Count; i++)
                {
                    var c = root.Children[i];
                    if (c.Children != null && c.Key.Equals(spec.Container, StringComparison.OrdinalIgnoreCase)) parents.Add((c, new[] { "#" + i }));
                }
            }
            else parents.Add((root, Array.Empty<string>()));
            foreach (var (parent, prefix) in parents)
            {
                for (int i = 0; i < parent.Children!.Count; i++)
                {
                    var n = parent.Children[i];
                    if (n.Children == null || n.Op == Op.None) continue;
                    if (spec.StatementKeys != null && !spec.StatementKeys.Contains(n.Key, StringComparer.OrdinalIgnoreCase)) continue;
                    string id = spec.IdKey != null ? n.GetString(spec.IdKey) ?? $"(no {spec.IdKey}, line {n.Line})" : n.Key;
                    string name = n.GetString(spec.NameKey) ?? "";
                    if (spec == Specs.Characters) name = (name + " " + (n.GetString("family") ?? "")).Trim();
                    list.Add(new BlockRef
                    {
                        Spec = spec,
                        File = file,
                        Path = prefix.Append("#" + i).ToArray(),
                        Id = id,
                        Type = n.Key,
                        Display = _index.Text(name) ?? name,
                        Line = n.Line,
                    });
                }
            }
        }
        _cache[spec] = list;
        return list;
    }

    public BlockRef? Find(BlockSpec spec, string id) => Of(spec).FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds the block again after an edit (paths are positional).</summary>
    public BlockRef? Refresh(BlockRef b) =>
        Of(b.Spec).FirstOrDefault(x => x.File == b.File && x.Id == b.Id) ?? Of(b.Spec).FirstOrDefault(x => x.File == b.File && x.Path.SequenceEqual(b.Path));

    /// <summary>The node of a block in the current text.</summary>
    public Node? NodeOf(BlockRef b) => ScriptPatch.Find(_store.Parse(b.File), b.Path);

    /// <summary>Which kind of block a file holds, if any (for the content browser).</summary>
    public static BlockSpec? KindOfFile(string rel)
    {
        rel = rel.Replace('\\', '/');
        BlockSpec? best = null;
        foreach (var k in Kinds)
        {
            if (rel.StartsWith(k.Source + "/", StringComparison.OrdinalIgnoreCase) || rel.Equals(k.Source + ".txt", StringComparison.OrdinalIgnoreCase))
                if (best == null || k.Source.Length > best.Source.Length) best = k;
        }
        // unit types and division templates share common/units
        return best;
    }
}
