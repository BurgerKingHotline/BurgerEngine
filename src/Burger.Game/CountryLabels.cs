using System.Numerics;
using Burger.Core;
using Burger.Core.Model;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Paradox-style country names: one label per contiguous block of a country's land, written along a gentle curve
/// that follows the block's shape and stays inside it. Every country gets a label on the block with its capital,
/// however small (it simply becomes readable when zoomed in); other blocks (colonies, exclaves) get one if big enough.
/// </summary>
public sealed class CountryLabels
{
    private sealed class Label
    {
        public Country Country = null!;
        public string Text = "";
        public Vector2[] Points = Array.Empty<Vector2>(); // curve in map pixels, left to right
        public float FontPx;                              // letter height in map pixels
        public float[] Heights = Array.Empty<float>();    // smoothed terrain height under each point (the name lies on the ground)
        public float LengthPx;
        public float Coverage = 1;                        // share of the land's length the name runs along
    }

    private readonly Game _game;
    private readonly MapRenderer _map;
    private readonly List<Label> _labels = new();
    private bool _dirty = true;
    private double _lastBuild = -10;

    public CountryLabels(Game game, MapRenderer map)
    {
        _game = game;
        _map = map;
        game.MapChanged += () => _dirty = true;
    }

    public void MarkDirty() => _dirty = true;

    // ------------------------------------------------------------------------------------- building
    private void Rebuild()
    {
        _labels.Clear();
        var w = _game.World;
        var md = w.Map;
        int n = w.Locations.Count;
        var block = new int[n];
        Array.Fill(block, -1);
        var blocks = new List<(Country Owner, List<Location> Locs, long Px)>();
        foreach (var start in w.Locations)
        {
            var o = start.Owner;
            if (o == null || start.IsWater || block[start.Index] >= 0) continue;
            int id = blocks.Count;
            var list = new List<Location>();
            long px = 0;
            var q = new Queue<Location>();
            block[start.Index] = id;
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var l = q.Dequeue();
                list.Add(l);
                px += l.PixelCount;
                foreach (var a in l.Neighbors)
                    if (a.Kind != AdjacencyKind.Sea && a.To.IsLand && a.To.Owner == o && block[a.To.Index] < 0) { block[a.To.Index] = id; q.Enqueue(a.To); }
            }
            blocks.Add((o, list, px));
        }
        // blocks of one country separated only by a narrow stretch of sea (Danish islands, Sicily, Japan...) share one name
        var groupOf = new int[blocks.Count];
        for (int i = 0; i < groupOf.Length; i++) groupOf[i] = i;
        int Find(int i) { while (groupOf[i] != i) i = groupOf[i] = groupOf[groupOf[i]]; return i; }
        // two blocks join when some of their locations come within a narrow strait of each other (bounding boxes)
        float gap = Math.Max(6, md.Width * 0.0025f);
        static float BoxGap(int ax0, int ay0, int ax1, int ay1, int bx0, int by0, int bx1, int by1)
            => Math.Max(Math.Max(0, Math.Max(ax0 - bx1, bx0 - ax1)), Math.Max(0, Math.Max(ay0 - by1, by0 - ay1)));
        var bbox = blocks.Select(b => (b.Locs.Min(l => l.MinX), b.Locs.Min(l => l.MinY), b.Locs.Max(l => l.MaxX), b.Locs.Max(l => l.MaxY))).ToArray();
        foreach (var g in Enumerable.Range(0, blocks.Count).GroupBy(i => blocks[i].Owner))
        {
            var ids = g.ToList();
            for (int x = 0; x < ids.Count; x++)
                for (int y = x + 1; y < ids.Count; y++)
                {
                    int a = ids[x], b = ids[y];
                    var (ax0, ay0, ax1, ay1) = bbox[a];
                    var (bx0, by0, bx1, by1) = bbox[b];
                    if (BoxGap(ax0, ay0, ax1, ay1, bx0, by0, bx1, by1) > gap || Find(a) == Find(b)) continue;
                    bool near = false;
                    foreach (var la in blocks[a].Locs)
                    {
                        foreach (var lb in blocks[b].Locs)
                            if (BoxGap(la.MinX, la.MinY, la.MaxX, la.MaxY, lb.MinX, lb.MinY, lb.MaxX, lb.MaxY) <= gap) { near = true; break; }
                        if (near) break;
                    }
                    if (near) groupOf[Find(a)] = Find(b);
                }
        }
        for (int i = 0; i < groupOf.Length; i++) groupOf[i] = Find(i);
        var blockGroup = groupOf;
        foreach (var g in Enumerable.Range(0, blocks.Count).GroupBy(i => blocks[i].Owner))
        {
            var c = g.Key;
            int homeBlock = c.Capital != null && c.Capital.Owner == c && block[c.Capital.Index] >= 0 ? block[c.Capital.Index] : g.OrderByDescending(i => blocks[i].Px).First();
            int home = blockGroup[homeBlock];
            // every group of the country that is not tiny gets its name (colonies and exclaves too)
            foreach (var grp in g.GroupBy(i => blockGroup[i]))
            {
                var locs = grp.SelectMany(i => blocks[i].Locs).ToList();
                long px = grp.Sum(i => blocks[i].Px);
                bool wanted = grp.Key == home || (locs.Count >= 2 && px >= 400);
                if (!wanted) continue;
                var label = Fit(c, locs, grp.Key, block, blockGroup, md);
                if (label != null) _labels.Add(label);
            }
        }
        _dirty = false;
    }

    private Label? Fit(Country c, List<Location> locs, int groupId, int[] block, int[] blockGroup, Burger.Core.Map.MapData md)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        foreach (var l in locs) { minX = Math.Min(minX, l.MinX); minY = Math.Min(minY, l.MinY); maxX = Math.Max(maxX, l.MaxX); maxY = Math.Max(maxY, l.MaxY); }
        long area = (long)(maxX - minX + 1) * (maxY - minY + 1);
        int step = Math.Max(1, (int)Math.Sqrt(area / 25000.0));
        var xs = new List<float>();
        var ys = new List<float>();
        bool Inside(int x, int y)
        {
            if (x < 0 || y < 0 || x >= md.Width || y >= md.Height) return false;
            int li = md.PixelLocation[y * md.Width + x];
            return li >= 0 && block[li] >= 0 && blockGroup[block[li]] == groupId;
        }
        for (int y = minY; y <= maxY; y += step)
            for (int x = minX; x <= maxX; x += step)
                if (Inside(x, y)) { xs.Add(x); ys.Add(y); }
        if (xs.Count < 3)
        {
            xs.Clear(); ys.Clear();
            foreach (var l in locs) { xs.Add(l.CenterX); ys.Add(l.CenterY); xs.Add(l.MinX); ys.Add(l.CenterY); xs.Add(l.MaxX); ys.Add(l.CenterY); }
        }
        int m = xs.Count;
        double mx = xs.Average(), my = ys.Average();
        double cxx = 0, cyy = 0, cxy = 0;
        for (int i = 0; i < m; i++) { double dx = xs[i] - mx, dy = ys[i] - my; cxx += dx * dx; cyy += dy * dy; cxy += dx * dy; }
        double ang = 0.5 * Math.Atan2(2 * cxy, cxx - cyy);
        // how elongated the block is (ratio of the two axes); roundish blocks get (nearly) horizontal names
        double tr = cxx + cyy, dt = Math.Sqrt(Math.Max(0, (cxx - cyy) * (cxx - cyy) / 4 + cxy * cxy));
        double l1 = tr / 2 + dt, l2 = Math.Max(1e-6, tr / 2 - dt);
        double elong = Math.Sqrt(l1 / l2);
        if (ang > Math.PI / 2) ang -= Math.PI;
        if (ang < -Math.PI / 2) ang += Math.PI;
        if (elong < 2.2) ang *= Math.Clamp((elong - 1.2) / 1.0, 0, 1);
        // keep text readable: never steeper than 60 degrees, always left to right
        double lim = 60 * Math.PI / 180;
        ang = Math.Clamp(ang, -lim, lim);
        // try the main axis and a fan of other directions; the one that fits the biggest letters wins
        // (flatter names are preferred a little, the main axis a little more)
        Label? best = null;
        double bestScore = 0;
        var angles = new List<double> { ang };
        for (int deg = -60; deg <= 60; deg += 15) angles.Add(deg * Math.PI / 180);
        Label? fallback = null;
        foreach (var a in angles)
            foreach (var cand in FitAngle(a))
            {
                fallback ??= cand;
                // names stay on their land: lines that run over the sea or other countries lose
                double on = cand.Points.Count(p => Inside((int)p.X, (int)p.Y)) / (double)cand.Points.Length;
                if (on < 0.6) continue;
                // steep names read worse: they must fit clearly bigger letters to win (less so along the land's own long axis)
                var chord = cand.Points[^1] - cand.Points[0];
                double tilt = Math.Min(Math.Abs(Math.Atan2(chord.Y, chord.X)), Math.Abs(Math.Atan2(-chord.Y, -chord.X)));
                double steep = Math.Pow(Math.Min(1.3, tilt / lim), 1.3) * (a == ang && elong > 2.2 ? 0.35 : 1);
                // and a name that runs along more of the land is worth more than slightly bigger letters on a part of it
                // bent names are a little harder to read than straight ones
                double bend = 0;
                for (int k = 2; k < cand.Points.Length; k++)
                {
                    var d0 = cand.Points[k - 1] - cand.Points[k - 2];
                    var d1 = cand.Points[k] - cand.Points[k - 1];
                    bend += Math.Abs(Math.Atan2(d0.X * d1.Y - d0.Y * d1.X, d0.X * d1.X + d0.Y * d1.Y));
                }
                double score = cand.FontPx * (1 - 0.5 * Math.Min(1, steep)) * (a == ang ? 1.06 : 1) * Math.Pow(on, 1.5) * Math.Pow(cand.Coverage, 0.8)
                             * (1 - 0.18 * Math.Min(1.5, bend));
                if (score > bestScore) { bestScore = score; best = cand; }
            }
        return best ?? fallback;

        List<Label> FitAngle(double ang)
        {
        var result = new List<Label>();
        double ca = Math.Cos(ang), sa = Math.Sin(ang);
        var us = new double[m];
        var vs = new double[m];
        for (int i = 0; i < m; i++)
        {
            double dx = xs[i] - mx, dy = ys[i] - my;
            us[i] = dx * ca + dy * sa;
            vs[i] = -dx * sa + dy * ca;
        }
        var sortedU = us.OrderBy(v => v).ToArray();
        double u0 = sortedU[(int)(m * 0.03)], u1 = sortedU[Math.Min(m - 1, (int)(m * 0.97))];
        if (u1 - u0 < 1) { u0 -= 2; u1 += 2; }
        const int bins = 24;
        var vmin = Enumerable.Repeat(double.MaxValue, bins).ToArray();
        var vmax = Enumerable.Repeat(double.MinValue, bins).ToArray();
        var cnt = new int[bins];
        for (int i = 0; i < m; i++)
        {
            int b = (int)((us[i] - u0) / (u1 - u0) * bins);
            if (b < 0 || b >= bins) continue;
            cnt[b]++;
            vmin[b] = Math.Min(vmin[b], vs[i]);
            vmax[b] = Math.Max(vmax[b], vs[i]);
        }
        // the land in each bin as separate stretches across the line (a bay or gulf splits a bin in two)
        var segs = new List<(double Lo, double Hi)>[bins];
        {
            var binVs = new List<double>[bins];
            for (int b = 0; b < bins; b++) binVs[b] = new List<double>();
            for (int i = 0; i < m; i++)
            {
                int b = (int)((us[i] - u0) / (u1 - u0) * bins);
                if (b >= 0 && b < bins) binVs[b].Add(vs[i]);
            }
            for (int b = 0; b < bins; b++)
            {
                segs[b] = new List<(double, double)>();
                var vl = binVs[b];
                if (vl.Count == 0) continue;
                vl.Sort();
                double lo = vl[0], prev = vl[0];
                foreach (var v in vl)
                {
                    if (v - prev > step * 3.5) { segs[b].Add((lo, prev + step)); lo = v; }
                    prev = v;
                }
                segs[b].Add((lo, prev + step));
            }
        }
        var thick = new double[bins];
        double maxThick = 0;
        for (int b = 0; b < bins; b++) { thick[b] = cnt[b] > 0 ? vmax[b] - vmin[b] + step : 0; maxThick = Math.Max(maxThick, thick[b]); }
        double binW = (u1 - u0) / bins;
        string text = c.Name.ToUpperInvariant();
        var across = new Vector2((float)-sa, (float)ca);
        float limit = (float)Math.Max(8, maxThick * 1.2);
        float stepPx = Math.Max(1f, step * 0.75f);
        float gapMax = (float)Math.Max(2 * step, maxThick * 0.06);
        double InsideShare(Vector2[] pts) => pts.Count(p => Inside((int)p.X, (int)p.Y)) / (double)pts.Length;
        Vector2 ToMap(double u, double v) => new((float)(mx + u * ca - v * sa), (float)(my + u * sa + v * ca));
        // how far the country goes from p in a direction; small lakes and inlets (short gaps) do not stop it
        float Room(Vector2 p, Vector2 dir)
        {
            float t = 0, lastInside = 0, gap = 0;
            while (t < limit)
            {
                t += stepPx;
                if (Inside((int)(p.X + dir.X * t), (int)(p.Y + dir.Y * t))) { lastInside = t; gap = 0; }
                else if ((gap += stepPx) > gapMax) break;
            }
            return lastInside;
        }

        // two ways to lay the name, each on a thick part (35% of the widest) or also thinner parts (18%):
        // a gentle arc, and a line through the middle of the land that bends with it (coasts, peninsulas)
        foreach (double thr in new[] { 0.35, 0.18 })
        {
            int bestStart = 0, bestLen = 0;
            for (int b = 0; b < bins;)
            {
                if (thick[b] < maxThick * thr) { b++; continue; }
                int s0_ = b;
                while (b < bins && thick[b] >= maxThick * thr) b++;
                if (b - s0_ > bestLen) { bestLen = b - s0_; bestStart = s0_; }
            }
            if (bestLen == 0) { bestStart = 0; bestLen = bins; }
            double r0 = u0 + bestStart * binW, r1 = u0 + (bestStart + bestLen) * binW;
            double extent = u1 - u0;
            var arc = Arc(bestStart, bestLen, r0, r1);
            if (arc != null) { var l = Finish(arc, rigid: true, extent); if (l != null) result.Add(l); }
            var spine = Spine(bestStart, bestLen);
            if (spine != null) { var l = Finish(spine, rigid: false, extent); if (l != null) result.Add(l); }
            var ridge = Ridge(thr);
            if (ridge != null) { var l = Finish(ridge, rigid: false, extent); if (l != null) result.Add(l); }
        }
        // and down the whole length of long, thin land (Norway's coast, Chile), however narrow it gets
        {
            var ridge = Ridge(0.06);
            if (ridge != null) { var l = Finish(ridge, rigid: false, u1 - u0); if (l != null) result.Add(l); }
        }
        return result;

        // the line down the middle of the land itself: one stretch of land per bin, chosen so the line runs on
        // without jumps (a peninsula with a gulf in it - Italy's boot - is followed down one side, not through the sea)
        Vector2[]? Ridge(double thr)
        {
            // best chain of stretches (Viterbi): long stretches are good, jumping sideways between bins is bad
            var score = new double[bins][];
            var from = new int[bins][];
            for (int b = 0; b < bins; b++)
            {
                score[b] = new double[segs[b].Count];
                from[b] = new int[segs[b].Count];
                for (int k = 0; k < segs[b].Count; k++)
                {
                    var (lo, hi) = segs[b][k];
                    double mid = (lo + hi) / 2;
                    double bestPrev = 0; int arg = -1;
                    if (b > 0)
                        for (int j = 0; j < segs[b - 1].Count; j++)
                        {
                            var (plo, phi) = segs[b - 1][j];
                            // the stretches must touch across the bin border to be one piece of land
                            if (plo > hi + step * 2 || phi < lo - step * 2) continue;
                            double v = score[b - 1][j] - Math.Abs((plo + phi) / 2 - mid) * 0.8;
                            if (v > bestPrev) { bestPrev = v; arg = j; }
                        }
                    score[b][k] = (hi - lo) + bestPrev;
                    from[b][k] = arg;
                }
            }
            int eb = -1, ek = -1; double es = 0;
            for (int b = 0; b < bins; b++)
                for (int k = 0; k < segs[b].Count; k++)
                    if (score[b][k] > es) { es = score[b][k]; eb = b; ek = k; }
            if (eb < 0) return null;
            var chain = new List<(int B, double Lo, double Hi)>();
            for (int b = eb, k = ek; b >= 0 && k >= 0; k = from[b][k], b--) chain.Add((b, segs[b][k].Lo, segs[b][k].Hi));
            chain.Reverse();
            // keep the longest run of stretches that are not too thin compared to the widest one in the chain
            double widest = chain.Max(x => x.Hi - x.Lo);
            int rs = 0, rl = 0;
            for (int i = 0; i < chain.Count;)
            {
                if (chain[i].Hi - chain[i].Lo < widest * thr) { i++; continue; }
                int st = i;
                while (i < chain.Count && chain[i].Hi - chain[i].Lo >= widest * thr) i++;
                if (i - st > rl) { rl = i - st; rs = st; }
            }
            if (rl < 4) return null;
            var mids = new List<Vector2>();
            for (int i = rs; i < rs + rl; i++) mids.Add(ToMap(u0 + (chain[i].B + 0.5) * binW, (chain[i].Lo + chain[i].Hi) / 2));
            return Smooth(mids);
        }

        // least-squares quadratic through the middles of the bins in the run
        Vector2[]? Arc(int bestStart, int bestLen, double r0, double r1)
        {
            double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0, t0 = 0, t1 = 0, t2 = 0;
            double uc = (r0 + r1) / 2;
            for (int b = bestStart; b < bestStart + bestLen; b++)
            {
                if (cnt[b] == 0) continue;
                double u = u0 + (b + 0.5) * binW - uc, v = (vmin[b] + vmax[b]) / 2, wgt = Math.Sqrt(cnt[b]);
                s0 += wgt; s1 += wgt * u; s2 += wgt * u * u; s3 += wgt * u * u * u; s4 += wgt * u * u * u * u;
                t0 += wgt * v; t1 += wgt * u * v; t2 += wgt * u * u * v;
            }
            double qa = 0, qb = 0, qc = s0 > 0 ? t0 / s0 : 0;
            double det = s0 * (s2 * s4 - s3 * s3) - s1 * (s1 * s4 - s3 * s2) + s2 * (s1 * s3 - s2 * s2);
            if (bestLen >= 3 && Math.Abs(det) > 1e-9)
            {
                qc = (t0 * (s2 * s4 - s3 * s3) - s1 * (t1 * s4 - s3 * t2) + s2 * (t1 * s3 - s2 * t2)) / det;
                qb = (s0 * (t1 * s4 - t2 * s3) - t0 * (s1 * s4 - s3 * s2) + s2 * (s1 * t2 - t1 * s2)) / det;
                qa = (s0 * (s2 * t2 - s3 * t1) - s1 * (s1 * t2 - s2 * t1) + t0 * (s1 * s3 - s2 * s2)) / det;
            }
            double half = (r1 - r0) / 2;
            // limit the bend so the name stays readable
            double maxA = half > 0 ? 0.22 * half / (half * half) : 0;
            qa = Math.Clamp(qa, -maxA, maxA);
            qb = Math.Clamp(qb, -0.6, 0.6);
            // the tilt of the curve adds to the label's angle: together they stay within the readable limit
            qb = Math.Clamp(qb, Math.Tan(Math.Max(-lim - ang, -1.2)), Math.Tan(Math.Min(lim - ang, 1.2)));
            double endSlope = Math.Max(Math.Abs(ang + Math.Atan(qb + 2 * qa * half)), Math.Abs(ang + Math.Atan(qb - 2 * qa * half)));
            if (endSlope > lim + 0.17) qa = 0;
            double margin = (r1 - r0) * 0.04;
            Vector2[] Curve(double a, double bb, double cc)
            {
                const int N = 24;
                var pts = new Vector2[N];
                for (int i = 0; i < N; i++)
                {
                    double u = r0 + margin + (r1 - r0 - 2 * margin) * i / (N - 1) - uc;
                    pts[i] = ToMap(u + uc, a * u * u + bb * u + cc);
                }
                return pts;
            }
            var points = Curve(qa, qb, qc);
            if (InsideShare(points) < 0.75)
            {
                var straight = Curve(0, qb, qc);
                if (InsideShare(straight) > InsideShare(points)) points = straight;
            }
            return points;
        }

        // the middles of the bins, smoothed, as a bending line (resampled evenly)
        Vector2[]? Spine(int bestStart, int bestLen)
        {
            var mids = new List<Vector2>();
            for (int b = bestStart; b < bestStart + bestLen; b++)
                if (cnt[b] > 0) mids.Add(ToMap(u0 + (b + 0.5) * binW, (vmin[b] + vmax[b]) / 2));
            if (mids.Count < 4) return null;
            return Smooth(mids);
        }

        // smooth until it reads well: no part steeper than 80 degrees and no sharp turns
        static Vector2[]? Smooth(List<Vector2> mids)
        {
            for (int pass = 0; pass < 14; pass++)
            {
                var pts = Resample(mids);
                if (pts == null) return null;
                if (Readable(pts)) return pts;
                var sm = new List<Vector2>(mids);
                for (int i = 1; i < mids.Count - 1; i++) sm[i] = (mids[i - 1] + mids[i] * 2 + mids[i + 1]) / 4;
                mids = sm;
            }
            return null;
        }

        // readable: no part steeper than 80 degrees, gentle turns, and not more than about 70 degrees of bending in all
        static bool Readable(Vector2[] pts)
        {
            double sum = 0;
            for (int k = 1; k < pts.Length; k++)
            {
                var dd = pts[k] - pts[k - 1];
                if (Math.Abs(dd.Y) > Math.Abs(dd.X) * 5.7f) return false;
                if (k >= 2)
                {
                    var d0 = pts[k - 1] - pts[k - 2];
                    double turn = Math.Atan2(d0.X * dd.Y - d0.Y * dd.X, d0.X * dd.X + d0.Y * dd.Y);
                    if (Math.Abs(turn) > 0.14) return false;
                    sum += turn;
                    if (Math.Abs(sum) > 1.25) return false;
                }
            }
            return true;
        }

        // evenly spaced points along a line, a little in from both ends
        static Vector2[]? Resample(List<Vector2> mids)
        {
            var cum = new float[mids.Count];
            for (int i = 1; i < mids.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(mids[i - 1], mids[i]);
            float total = cum[^1];
            if (total < 2) return null;
            const int N = 24;
            var pts = new Vector2[N];
            for (int k = 0; k < N; k++)
            {
                float d = total * (0.03f + 0.94f * k / (N - 1));
                int j = 1;
                while (j < mids.Count - 1 && cum[j] < d) j++;
                float t = (d - cum[j - 1]) / Math.Max(1e-3f, cum[j] - cum[j - 1]);
                pts[k] = Vector2.Lerp(mids[j - 1], mids[j], Math.Clamp(t, 0, 1));
            }
            return pts;
        }

        // room for the letters, centring (arcs only), letter height, terrain heights
        Label? Finish(Vector2[] points, bool rigid, double extent)
        {
            int np = points.Length;
            var up = new float[np];
            var down = new float[np];
            var normals = new Vector2[np];
            for (int i = 0; i < np; i++)
            {
                if (rigid) normals[i] = across;
                else
                {
                    var d = points[Math.Min(np - 1, i + 1)] - points[Math.Max(0, i - 1)];
                    normals[i] = d.LengthSquared() < 1e-6f ? across : Vector2.Normalize(new Vector2(-d.Y, d.X));
                }
                up[i] = Room(points[i], -normals[i]);
                down[i] = Room(points[i], normals[i]);
            }
            var inner = Enumerable.Range(np / 8, np - 2 * (np / 8)).ToList();
            float shift = 0;
            if (rigid)
            {
                // move the whole arc (without bending it) to the middle of the room
                shift = inner.Select(i => (down[i] - up[i]) / 2).OrderBy(v => v).ElementAt(inner.Count / 2);
                for (int i = 0; i < np; i++) points[i] += across * shift;
            }
            var room = inner.Select(i => 2 * Math.Min(up[i] + shift, down[i] - shift)).Select(v => Math.Max(0f, v)).OrderBy(v => v).ToList();
            float len = 0;
            for (int i = 1; i < np; i++) len += Vector2.Distance(points[i - 1], points[i]);
            if (len < 1) len = 1;
            // letter height: as tall as the country allows along the line (a few narrow spots may be crossed) and the name's length
            float byThick = room.Count > 0 ? room[room.Count / 2] * 0.9f : (float)(maxThick * 0.5);
            float byLength = len / Math.Max(3, text.Length * 0.62f);
            float fontPx = Math.Max(1.5f, Math.Min(byThick, byLength));
            // terrain height under the line, smoothed so the name follows the land without wobbling
            var raw = points.Select(p => _map.WorldPos(p.X, p.Y).Y).ToArray();
            var heights = new float[np];
            for (int i = 0; i < np; i++)
            {
                float sum = 0, wsum = 0;
                for (int j = Math.Max(0, i - 6); j <= Math.Min(np - 1, i + 6); j++) { float wgt = 7 - Math.Abs(i - j); sum += raw[j] * wgt; wsum += wgt; }
                heights[i] = sum / wsum + 0.02f;
            }
            return new Label { Country = c, Text = text, Points = points, FontPx = fontPx, Heights = heights, LengthPx = len, Coverage = (float)Math.Min(1, Math.Min(len, fontPx * (text.Length * 0.62f + 0.8f * (text.Length - 1))) / Math.Max(1, extent)) };
        }
        }
    }

    // ------------------------------------------------------------------------------------- drawing
    public void Draw(Camera3D cam, float minScreenFont = 3f)
    {
        if (_dirty && Raylib.GetTime() - _lastBuild > 1.5) { Rebuild(); _lastBuild = Raylib.GetTime(); }
        float ppu = _game.World.Map.Settings.PixelsPerUnit;
        var font = Assets.HeaderFont;
        var scr = new Vector2[24];
        var cum = new float[24];
        foreach (var lb in _labels)
        {
            int n = lb.Points.Length;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var p = lb.Points[i];
                scr[i] = Viewport.ToScreen(new Vector3(p.X / ppu, lb.Heights[i], p.Y / ppu), cam);
                minX = Math.Min(minX, scr[i].X); maxX = Math.Max(maxX, scr[i].X); minY = Math.Min(minY, scr[i].Y); maxY = Math.Max(maxY, scr[i].Y);
            }
            if (maxX < -100 || minX > Viewport.W + 100 || maxY < -100 || minY > Viewport.H + 100) continue;
            // left to right on screen
            if (scr[0].X > scr[n - 1].X) Array.Reverse(scr, 0, n);
            cum[0] = 0;
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector2.Distance(scr[i - 1], scr[i]);
            float L = cum[n - 1];
            if (L < 2) continue;
            float fs = lb.FontPx * (L / lb.LengthPx);
            if (fs < minScreenFont) continue;
            fs = Math.Min(fs, 140);
            // glyph advances
            float total = 0;
            var adv = new float[lb.Text.Length];
            for (int i = 0; i < lb.Text.Length; i++) { adv[i] = Raylib.MeasureTextEx(font, lb.Text[i].ToString(), fs, 0).X; total += adv[i]; }
            float avail = L * 0.9f;   // a little room at both ends, so neighbouring names do not touch
            if (total > avail) { float k = avail / total; fs *= k; for (int i = 0; i < adv.Length; i++) adv[i] *= k; total = avail; if (fs < minScreenFont) continue; }
            float spacing = lb.Text.Length > 1 ? Math.Clamp((avail - total) / (lb.Text.Length - 1), 0, fs * 0.8f) : 0;
            float used = total + spacing * (lb.Text.Length - 1);
            float pos = (L - used) / 2;
            // solid letters with a dark outline, so names read on any map color (fading in only while tiny)
            byte alpha = (byte)Math.Clamp(150 + (fs - minScreenFont) * 30, 150, 245);
            var col = new Color((byte)250, (byte)246, (byte)230, alpha);
            var shadow = new Color((byte)0, (byte)0, (byte)0, (byte)(alpha * 0.8f));
            float so = Math.Max(1f, fs / 22f);
            for (int i = 0; i < lb.Text.Length; i++)
            {
                float center = pos + adv[i] / 2;
                pos += adv[i] + spacing;
                char ch = lb.Text[i];
                if (ch == ' ') continue;
                int k = 1;
                while (k < n - 1 && cum[k] < center) k++;
                float t = (center - cum[k - 1]) / Math.Max(0.001f, cum[k] - cum[k - 1]);
                var p = Vector2.Lerp(scr[k - 1], scr[k], Math.Clamp(t, 0, 1));
                var d = scr[k] - scr[k - 1];
                float rot = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
                var origin = new Vector2(adv[i] / 2, fs / 2);
                string s = ch.ToString();
                Raylib.DrawTextPro(font, s, p + new Vector2(so, so), origin, rot, fs, 0, shadow);
                Raylib.DrawTextPro(font, s, p + new Vector2(-so * 0.6f, so * 0.6f), origin, rot, fs, 0, shadow);
                Raylib.DrawTextPro(font, s, p + new Vector2(so * 0.6f, -so * 0.6f), origin, rot, fs, 0, shadow);
                Raylib.DrawTextPro(font, s, p, origin, rot, fs, 0, col);
            }
        }
    }
}
