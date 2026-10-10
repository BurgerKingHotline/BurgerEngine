using System.Numerics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>Paradox-style map camera: WASD/arrows/middle-drag to pan, mouse wheel to zoom towards the cursor.</summary>
public sealed class CameraController
{
    private readonly MapRenderer _map;
    public Vector3 Target;
    public float Distance;
    public float MinDistance = 5f;
    public float MaxDistance;
    private Vector3? _dragAnchor;

    public Camera3D Camera;

    public CameraController(MapRenderer map)
    {
        _map = map;
        MaxDistance = Math.Max(map.WorldWidth, map.WorldHeight) * 1.05f;
        Target = new Vector3(map.WorldWidth / 2, 0, map.WorldHeight / 2);
        Distance = MaxDistance * 0.6f;
        Camera = new Camera3D(Vector3.Zero, Vector3.Zero, Vector3.UnitY, 40f, CameraProjection.Perspective);
        Apply();
    }

    public void LookAt(Vector3 p, float? distance = null)
    {
        Target = new Vector3(p.X, 0, p.Z);
        if (distance != null) Distance = Math.Clamp(distance.Value, MinDistance, MaxDistance);
        Apply();
    }

    public void Update(float dt, bool mouseOverUi, bool wheelUsed, bool keyboardFree)
    {
        float speed = Distance * 1.2f * dt;
        if (keyboardFree)
        {
            if (Raylib.IsKeyDown(KeyboardKey.LeftShift)) speed *= 2.5f;
            if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) Target.Z -= speed;
            if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) Target.Z += speed;
            if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) Target.X -= speed;
            if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) Target.X += speed;
            if (Raylib.IsKeyDown(KeyboardKey.PageUp) || Raylib.IsKeyDown(KeyboardKey.Q)) Distance *= 1 - dt * 1.5f;
            if (Raylib.IsKeyDown(KeyboardKey.PageDown) || Raylib.IsKeyDown(KeyboardKey.E)) Distance *= 1 + dt * 1.5f;
        }

        var mouse = Viewport.Mouse;
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && !mouseOverUi && !wheelUsed)
        {
            var before = _map.GroundPoint(mouse, Camera);
            Distance = Math.Clamp(Distance * MathF.Pow(0.85f, wheel), MinDistance, MaxDistance);
            Apply();
            var after = _map.GroundPoint(mouse, Camera);
            if (before != null && after != null) Target += before.Value - after.Value; // zoom towards cursor
        }

        // middle mouse drag
        if (Raylib.IsMouseButtonPressed(MouseButton.Middle) && !mouseOverUi) _dragAnchor = _map.GroundPoint(mouse, Camera);
        if (Raylib.IsMouseButtonReleased(MouseButton.Middle)) _dragAnchor = null;
        if (_dragAnchor != null && Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            Apply();
            var now = _map.GroundPoint(mouse, Camera);
            if (now != null) Target += _dragAnchor.Value - now.Value;
        }

        Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
        Target.X = Math.Clamp(Target.X, 0, _map.WorldWidth);
        Target.Z = Math.Clamp(Target.Z, 0, _map.WorldHeight);
        Apply();
    }

    private void Apply()
    {
        float ground = _map.HeightAt(Target.X, Target.Z);
        // tilt more when zoomed in (like Paradox games), look straight down when far out
        float t = Math.Clamp((Distance - MinDistance) / (MaxDistance - MinDistance), 0, 1);
        float back = Distance * (0.75f - 0.55f * t);
        // when zoomed far out, drift towards the middle of the map so the whole map is visible
        float pull = t * t;
        var center = new Vector3(_map.WorldWidth / 2, 0, _map.WorldHeight / 2);
        var target = new Vector3(Target.X + (center.X - Target.X) * pull, ground, Target.Z + (center.Z - Target.Z) * pull);
        Camera.Target = target;
        Camera.Position = target + new Vector3(0, Distance, back);
    }
}
