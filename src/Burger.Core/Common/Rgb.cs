using System.Globalization;
using Burger.Core.Script;

namespace Burger.Core;

/// <summary>Simple RGB color used by the simulation (the renderer converts it to its own type).</summary>
public readonly struct Rgb : IEquatable<Rgb>
{
    public readonly byte R, G, B;

    public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
    public Rgb(int r, int g, int b) { R = (byte)Math.Clamp(r, 0, 255); G = (byte)Math.Clamp(g, 0, 255); B = (byte)Math.Clamp(b, 0, 255); }

    /// <summary>0xRRGGBB</summary>
    public int Packed => (R << 16) | (G << 8) | B;

    public static Rgb FromPacked(int p) => new((byte)((p >> 16) & 0xFF), (byte)((p >> 8) & 0xFF), (byte)(p & 0xFF));

    public string ToHex() => $"{R:X2}{G:X2}{B:X2}";

    public Rgb Scale(double f) => new((int)(R * f), (int)(G * f), (int)(B * f));

    public Rgb Lerp(Rgb o, double t) => new((int)(R + (o.R - R) * t), (int)(G + (o.G - G) * t), (int)(B + (o.B - B) * t));

    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Gray = new(128, 128, 128);

    /// <summary>Parses "FF0000", "#FF0000", "0xFF0000", "x4a7f1e".</summary>
    public static bool TryParseHex(string? s, out Rgb c)
    {
        c = default;
        if (s == null) return false;
        s = s.Trim();
        if (s.StartsWith("#")) s = s[1..];
        else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        else if (s.Length == 7 && (s[0] == 'x' || s[0] == 'X')) s = s[1..];
        if (s.Length != 6) return false;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        c = FromPacked(v);
        return true;
    }

    /// <summary>
    /// Reads a color from a script node. Accepts <c>{ 255 0 0 }</c>, <c>rgb { 255 0 0 }</c>,
    /// <c>{ 1.0 0 0 }</c> (0-1 floats), <c>hsv { 0.5 1 1 }</c>, <c>"FF0000"</c>.
    /// </summary>
    public static bool TryParse(Node? n, out Rgb c)
    {
        c = default;
        if (n == null) return false;
        if (n.Value != null) return TryParseHex(n.Value, out c);
        var items = n.Items();
        if (items.Count < 3) return false;
        var v = new double[3];
        for (int i = 0; i < 3; i++)
            if (!Node.TryParseDouble(items[i], out v[i])) return false;
        if (n.BlockType == "hsv" || n.BlockType == "hsv360")
        {
            double h = n.BlockType == "hsv360" ? v[0] / 360.0 : v[0];
            double s = n.BlockType == "hsv360" ? v[1] / 100.0 : v[1];
            double val = n.BlockType == "hsv360" ? v[2] / 100.0 : v[2];
            c = FromHsv(h, s, val);
            return true;
        }
        bool floats = v.All(x => x <= 1.0) && items.Any(i => i.Contains('.'));
        if (floats) c = new Rgb((int)(v[0] * 255), (int)(v[1] * 255), (int)(v[2] * 255));
        else c = new Rgb((int)v[0], (int)v[1], (int)v[2]);
        return true;
    }

    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 1.0) + 1.0) % 1.0;
        int i = (int)(h * 6);
        double f = h * 6 - i, p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        (double r, double g, double b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return new Rgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    public Node ToNode(string key)
    {
        var n = new Node(key, new List<Node>());
        n.AddItem(R.ToString(CultureInfo.InvariantCulture));
        n.AddItem(G.ToString(CultureInfo.InvariantCulture));
        n.AddItem(B.ToString(CultureInfo.InvariantCulture));
        return n;
    }

    public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;
    public override bool Equals(object? obj) => obj is Rgb o && Equals(o);
    public override int GetHashCode() => Packed;
    public static bool operator ==(Rgb a, Rgb b) => a.Equals(b);
    public static bool operator !=(Rgb a, Rgb b) => !a.Equals(b);
    public override string ToString() => ToHex();
}
