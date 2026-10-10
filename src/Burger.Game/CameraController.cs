using System.Numerics;
using Raylib_cs;

namespace Burger.Runtime;

/// <summary>
/// Paradox-style map camera: WASD/arrows/middle-drag to pan, mouse wheel to zoom towards the cursor.
/// Input only moves the logical target (Target/Distance); the rendered camera position and rotation are
/// linearly interpolated (damped) towards it every frame in Update(), which must be called once per frame
/// BEFORE BeginDrawing(). This decoupling removes the jitter/shakiness caused by moving the camera while
/// the frame is being drawn.
/// </summary>
public sealed class CameraController
{
    private readonly MapRenderer _map;
    /// <summary>Logical focus point the camera damps towards (input writes this; CurrentPos follows via Lerp).</summary>
    public Vector3 Target;
    /// <summary>Logical zoom distance the camera damps towards (input writes this; CurrentDistance follows via Lerp).</summary>
    public float Distance;
    public float MinDistance = 5f;
    public float MaxDistance;
    /// <summary>Damping strength per frame: currentPos += (targetPos - currentPos) * SmoothFactor. Default 0.15f.</summary>
    public float SmoothFactor = 0.15f;
    private Vector3? _dragAnchor;

    // Internal smoothed state (the "current" side of the Lerp). Read-only from outside.
    private Vector3 _currentPos;
    private Vector3 _currentTarget;
    private float _currentDistance;
    private bool _smoothed;

    public Camera3D Camera;

    public CameraController(MapRenderer map)
    {
        _map = map;
        MaxDistance = Math.Max(map.WorldWidth, map.WorldHeight) * 1.05f;
        Target = new Vector3(map.WorldWidth / 2, 0, map.WorldHeight / 2);
        Distance = MaxDistance * 0.6f;
        Camera = new Camera3D(Vector3.Zero, Vector3.Zero, Vector3.UnitY, 40f, CameraProjection.Perspective);
        ApplyImmediate(); // snap so the first frame is already at the requested view
    }

    public void LookAt(Vector3 p, float? distance = null)
    {
        Target = new Vector3(p.X, 0, p.Z);
        if (distance != null) Distance = Math.Clamp(distance.Value, MinDistance, MaxDistance);
        ClampLogical();
        ApplyImmediate(); // programmatic jumps (selection, saves) move instantly, no damping
    }

    /// <summary>
    /// Advances input handling and damped camera motion. Call once per frame before Raylib.BeginDrawing();
    /// never mutate the camera from inside the draw loop.
    /// </summary>
    public void Update(float dt, bool mouseOverUi, bool wheelUsed, bool keyboardFree)
    {
        // ------------------------------------------------ input modifies only the logical target/distance
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
            // keep the ground point under the cursor stable around the smoothed camera, then adjust the logical zoom
            var before = _map.GroundPoint(mouse, Camera);
            Distance = Math.Clamp(Distance * MathF.Pow(0.85f, wheel), MinDistance, MaxDistance);
            if (before != null)
            {
                var after = _map.GroundPoint(mouse, BuildDesiredCamera(out _)); // camera at the new logical zoom
                if (after != null) Target += before.Value - after.Value; // zoom towards cursor
            }
        }

        // middle mouse drag
        if (Raylib.IsMouseButtonPressed(MouseButton.Middle) && !mouseOverUi) _dragAnchor = _map.GroundPoint(mouse, Camera);
        if (Raylib.IsMouseButtonReleased(MouseButton.Middle)) _dragAnchor = null;
        if (_dragAnchor != null && Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            var now = _map.GroundPoint(mouse, Camera);
            if (now != null) Target += _dragAnchor.Value - now.Value;
        }

        ClampLogical();

        // ------------------------------------------------ damped follow: Lerp position + rotation (view target)
        // frame-rate independent smoothing derived from the per-frame SmoothFactor
        float f = 1f - MathF.Pow(1f - Math.Clamp(SmoothFactor, 0.001f, 1f), Math.Clamp(dt * 60f, 0f, 240f));
        var desired = BuildDesiredCamera(out float desiredDist);
        if (!_smoothed)
        {
            _currentPos = desired.Position;
            _currentTarget = desired.Target;
            _currentDistance = desiredDist;
            _smoothed = true;
        }
        else
        {
            _currentPos += (desired.Position - _currentPos) * f;   // currentPos += (targetPos - currentPos) * smoothFactor
            _currentTarget += (desired.Target - _currentTarget) * f; // rotation (up-vector direction) follows the target
            _currentDistance += (desiredDist - _currentDistance) * f;
        }
        Camera = BuildCamera(_currentPos, _currentTarget, _currentDistance);
    }

    private void ClampLogical()
    {
        Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
        Target.X = Math.Clamp(Target.X, 0, _map.WorldWidth);
        Target.Z = Math.Clamp(Target.Z, 0, _map.WorldHeight);
    }

    /// <summary>Builds the desired camera pose from the logical Target/Distance (no smoothing applied).</summary>
    private Camera3D BuildDesiredCamera(out float distance)
    {
        float ground = _map.HeightAt(Target.X, Target.Z);
        // tilt more when zoomed in (like Paradox games), look straight down when far out
        float t = Math.Clamp((Distance - MinDistance) / (MaxDistance - MinDistance), 0, 1);
        float back = Distance * (0.75f - 0.55f * t);
        // when zoomed far out, drift towards the middle of the map so the whole map is visible
        float pull = t * t;
        var center = new Vector3(_map.WorldWidth / 2, 0, _map.WorldHeight / 2);
        var target = new Vector3(Target.X + (center.X - Target.X) * pull, ground, Target.Z + (center.Z - Target.Z) * pull);
        distance = Distance;
        return new Camera3D(target + new Vector3(0, Distance, back), target, Vector3.UnitY, 40f, CameraProjection.Perspective);
    }

    private Camera3D BuildCamera(Vector3 pos, Vector3 look, float distance) =>
        new(pos, look, Vector3.UnitY, 40f, CameraProjection.Perspective);

    /// <summary>Snap the smoothed state to the logical pose (used for instant, non-damped placement).</summary>
    private void ApplyImmediate()
    {
        var cam = BuildDesiredCamera(out float dist);
        _currentPos = cam.Position;
        _currentTarget = cam.Target;
        _currentDistance = dist;
        _smoothed = true;
        Camera = cam;
    }
}
