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
        public float Lift;                                // world height of the label plane
        public float LengthPx;
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
        foreach (var g in Enumerable.Range(0, blocks.Count).GroupBy(i => blocks[i].Owner))
        {
            var c = g.Key;
            long biggest = g.Max(i => blocks[i].Px);
            int home = c.Capital != null && c.Capital.Owner == c && block[c.Capital.Index] >= 0 ? block[c.Capital.Index] : g.OrderByDescending(i => blocks[i].Px).First();
            foreach (int i in g)
            {
                var b = blocks[i];
                bool wanted = i == home || (b.Px >= biggest * 0.15 && b.Locs.Count >= 2 && b.Px >= 400);
                if (!wanted) continue;
                var label = Fit(c, b.Locs, i, block, md);
                if (label != null) _labels.Add(label);
            }
        }
        _dirty = false;
    }

    private Label? Fit(Country c, List<Location> locs, int blockId, int[] block, Burger.Core.Map.MapData md)
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
            return li >= 0 && block[li] == blockId;
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
        const int bins = 20;
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
        var thick = new double[bins];
        double maxThick = 0;
        for (int b = 0; b < bins; b++) { thick[b] = cnt[b] > 0 ? vmax[b] - vmin[b] + step : 0; maxThick = Math.Max(maxThick, thick[b]); }
        // the longest run of bins that are reasonably thick: the text goes there
        int bestStart = 0, bestLen = 0;
        for (int b = 0; b < bins;)
        {
            if (thick[b] < maxThick * 0.35) { b++; continue; }
            int s = b;
            while (b < bins && thick[b] >= maxThick * 0.35) b++;
            if (b - s > bestLen) { bestLen = b - s; bestStart = s; }
        }
        if (bestLen == 0) { bestStart = 0; bestLen = bins; }
        double binW = (u1 - u0) / bins;
        double r0 = u0 + bestStart * binW, r1 = u0 + (bestStart + bestLen) * binW;
        // least-squares quadratic through the middles of the bins in the run
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
        double margin = (r1 - r0) * 0.07;
        Vector2[] Curve(double a, double bb, double cc)
        {
            const int N = 24;
            var pts = new Vector2[N];
            for (int i = 0; i < N; i++)
            {
                double u = r0 + margin + (r1 - r0 - 2 * margin) * i / (N - 1) - uc;
                double v = a * u * u + bb * u + cc;
                double uu = u + uc;
                pts[i] = new Vector2((float)(mx + uu * ca - v * sa), (float)(my + uu * sa + v * ca));
            }
            return pts;
        }
        double InsideShare(Vector2[] pts) => pts.Count(p => Inside((int)p.X, (int)p.Y)) / (double)pts.Length;
        var points = Curve(qa, qb, qc);
        if (InsideShare(points) < 0.75)
        {
            var straight = Curve(0, qb, qc);
            if (InsideShare(straight) > InsideShare(points)) points = straight;
        }
        float len = 0;
        for (int i = 1; i < points.Length; i++) len += Vector2.Distance(points[i - 1], points[i]);
        if (len < 1) len = 1;
        // letter height: limited by the block's thickness along the run and by the name's length
        var runThick = Enumerable.Range(bestStart, bestLen).Select(b => thick[b]).Where(t => t > 0).OrderBy(t => t).ToList();
        double medThick = runThick.Count > 0 ? runThick[runThick.Count / 2] : maxThick;
        string text = c.Name.ToUpperInvariant();
        float byThick = (float)(medThick * 0.8);
        float byLength = len / Math.Max(3, text.Length * 0.62f);
        float fontPx = Math.Max(1.5f, Math.Min(byThick, byLength));
        float lift = 0;
        foreach (var p in points) lift += _map.WorldPos(p.X, p.Y).Y;
        lift = lift / points.Length + 0.25f;
        return new Label { Country = c, Text = text, Points = points, FontPx = fontPx, Lift = lift, LengthPx = len };
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
                scr[i] = Viewport.ToScreen(new Vector3(p.X / ppu, lb.Lift, p.Y / ppu), cam);
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
            float avail = L * 0.96f;
            if (total > avail) { float k = avail / total; fs *= k; for (int i = 0; i < adv.Length; i++) adv[i] *= k; total = avail; if (fs < minScreenFont) continue; }
            float spacing = lb.Text.Length > 1 ? Math.Clamp((avail - total) / (lb.Text.Length - 1), 0, fs * 1.1f) : 0;
            float used = total + spacing * (lb.Text.Length - 1);
            float pos = (L - used) / 2;
            byte alpha = (byte)Math.Clamp(110 + fs * 6, 120, 225);
            var col = new Color((byte)250, (byte)246, (byte)230, alpha);
            var shadow = new Color((byte)0, (byte)0, (byte)0, (byte)(alpha * 0.75f));
            float so = Math.Max(1f, fs / 24f);
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
                Raylib.DrawTextPro(font, s, p, origin, rot, fs, 0, col);
            }
        }
    }
}
