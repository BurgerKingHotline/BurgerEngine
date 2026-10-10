using System.Numerics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// The screen area the map is drawn into. The game uses the whole window; the editor (Burger Studio) draws the map
/// into a panel, so picking, labels and camera input must use the panel's size and mouse position.
/// </summary>
public static class Viewport
{
    /// <summary>Top-left corner of the map area on screen (mouse offset). 0,0 for the game.</summary>
    public static Vector2 Origin;
    /// <summary>Size of the map area; 0 = the whole window.</summary>
    public static int Width, Height;

    public static int W => Width > 0 ? Width : Raylib.GetScreenWidth();
    public static int H => Height > 0 ? Height : Raylib.GetScreenHeight();

    /// <summary>Mouse position relative to the map area.</summary>
    public static Vector2 Mouse => Raylib.GetMousePosition() - Origin;

    public static Vector2 ToScreen(Vector3 world, Camera3D cam) => Raylib.GetWorldToScreenEx(world, cam, W, H);

    /// <summary>
    /// Projects a world point to screen space and snaps X/Y to whole pixels. Use for text/UI overlays:
    /// sub-pixel positions make glyphs shimmer while the camera moves.
    /// </summary>
    public static Vector2 SnapToScreen(Vector3 world, Camera3D cam)
    {
        var s = ToScreen(world, cam);
        return new Vector2(MathF.Round(s.X), MathF.Round(s.Y));
    }

    public static Ray Ray(Vector2 pos, Camera3D cam) => Raylib.GetScreenToWorldRayEx(pos, cam, W, H);

    /// <summary>Back to full-window mode.</summary>
    public static void Reset() { Origin = Vector2.Zero; Width = Height = 0; }
}
