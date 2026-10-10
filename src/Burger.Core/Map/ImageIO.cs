using StbImageSharp;

namespace Burger.Core.Map;

/// <summary>An image decoded to 8-bit channels.</summary>
public sealed class RawImage
{
    public int Width;
    public int Height;
    /// <summary>Bytes per pixel (1 = grey, 4 = RGBA).</summary>
    public int Channels;
    public byte[] Data = Array.Empty<byte>();

    public int RgbAt(int x, int y)
    {
        int i = (y * Width + x) * Channels;
        if (Channels == 1) { int g = Data[i]; return (g << 16) | (g << 8) | g; }
        return (Data[i] << 16) | (Data[i + 1] << 8) | Data[i + 2];
    }
}

/// <summary>Loads PNG, BMP, TGA and JPG images (8-bit or 16-bit, paletted or not) with StbImageSharp.</summary>
public static class ImageIO
{
    public static RawImage? Load(string path, bool grey, string displayName)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var bytes = File.ReadAllBytes(path);
            var img = ImageResult.FromMemory(bytes, grey ? ColorComponents.Grey : ColorComponents.RedGreenBlueAlpha);
            return new RawImage { Width = img.Width, Height = img.Height, Channels = grey ? 1 : 4, Data = img.Data };
        }
        catch (Exception ex)
        {
            Log.Error($"Could not decode image ({ex.Message}). Use PNG or BMP.", displayName);
            return null;
        }
    }

    /// <summary>Nearest-neighbor resample of a single-channel image to a new size.</summary>
    public static byte[] ResampleGrey(RawImage img, int w, int h)
    {
        var result = new byte[w * h];
        if (img.Width == w && img.Height == h && img.Channels == 1)
        {
            Buffer.BlockCopy(img.Data, 0, result, 0, result.Length);
            return result;
        }
        for (int y = 0; y < h; y++)
        {
            int sy = Math.Min(img.Height - 1, (int)((y + 0.5) * img.Height / h));
            for (int x = 0; x < w; x++)
            {
                int sx = Math.Min(img.Width - 1, (int)((x + 0.5) * img.Width / w));
                result[y * w + x] = img.Data[(sy * img.Width + sx) * img.Channels];
            }
        }
        return result;
    }
}
