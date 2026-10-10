using Burger.Core.Model;

namespace Burger.Core.Warfare;

/// <summary>
/// Fog of war: which locations a country can see. Replace with a subclass from C# (Game.Rules.Vision = new MyVision())
/// to change how far armies see, add spies, reveal the whole map during peace...
/// </summary>
public class VisionRules
{
    /// <summary>Countries whose eyes the viewer shares (itself, allies, overlord, subjects).</summary>
    public virtual HashSet<Country> SharedVision(Game game, Country viewer)
    {
        var set = new HashSet<Country> { viewer };
        var rel = game.World.Relations;
        foreach (var a in rel.AlliesOf(viewer)) set.Add(a);
        foreach (var s in rel.SubjectsOf(viewer)) set.Add(s);
        if (rel.OverlordOf(viewer) is { } o) set.Add(o);
        return set;
    }

    /// <summary>How many steps around a division it sees.</summary>
    public virtual int DivisionSight(Game game, Division d) => 1;

    /// <summary>How many steps around owned or controlled land is visible.</summary>
    public virtual int BorderSight(Game game, Country viewer) => 1;

    /// <summary>Visibility of every location (index = Location.Index) for a viewer.</summary>
    public virtual bool[] Compute(Game game, Country viewer)
    {
        var w = game.World;
        var vis = new bool[w.Locations.Count];
        var friends = SharedVision(game, viewer);
        var frontier = new List<(Location L, int Range)>();
        int border = BorderSight(game, viewer);
        foreach (var l in w.Locations)
        {
            bool mine = (l.Owner != null && friends.Contains(l.Owner) && (l.Controller == null || friends.Contains(l.Controller)))
                        || (l.Controller != null && friends.Contains(l.Controller));
            if (mine) frontier.Add((l, border));
        }
        foreach (var d in w.Divisions)
            if (friends.Contains(d.Owner)) frontier.Add((d.Location, DivisionSight(game, d)));
        if (game.Features.Has(Burger.Core.Modding.FeatureSet.Navy))
            foreach (var f in game.Navy.Fleets)
                if (friends.Contains(f.Owner)) frontier.Add((f.Location, 1));
        // spread outwards (breadth first, keep the largest remaining range per location)
        var best = new int[w.Locations.Count];
        Array.Fill(best, -1);
        var queue = new Queue<(Location L, int Range)>();
        foreach (var f in frontier)
            if (f.Range > best[f.L.Index]) { best[f.L.Index] = f.Range; queue.Enqueue(f); }
        while (queue.Count > 0)
        {
            var (l, r) = queue.Dequeue();
            vis[l.Index] = true;
            if (r <= 0) continue;
            foreach (var a in l.Neighbors)
                if (r - 1 > best[a.To.Index]) { best[a.To.Index] = r - 1; queue.Enqueue((a.To, r - 1)); }
        }
        return vis;
    }
}
