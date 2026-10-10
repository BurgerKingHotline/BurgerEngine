using Burger.Core.Script;

namespace Burger.Core.Modding;

/// <summary>
/// A block in the save file owned by one system or script (production, navy, air, your own C# mechanic...).
/// Register it once (e.g. in a GameScript's OnLoad): <c>Game.SaveSections.Add(new SaveSection { Name = "my_mechanic", Write = ..., Read = ... })</c>.
/// </summary>
public sealed class SaveSection
{
    /// <summary>Unique name; stored as a block named <c>x_&lt;name&gt;</c> in the save.</summary>
    public string Name = "";
    /// <summary>Read before characters, divisions and wars (needed by things they refer to, e.g. division templates).</summary>
    public bool Early;
    /// <summary>Clears the system's runtime data before a save is loaded.</summary>
    public Action? Reset;
    public Action<Node> Write = _ => { };
    public Action<Node> Read = _ => { };
}

/// <summary>All registered save sections.</summary>
public sealed class SaveSectionRegistry
{
    private readonly List<SaveSection> _sections = new();
    public IReadOnlyList<SaveSection> All => _sections;

    public void Add(SaveSection s)
    {
        _sections.RemoveAll(x => x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase));
        _sections.Add(s);
    }
}
