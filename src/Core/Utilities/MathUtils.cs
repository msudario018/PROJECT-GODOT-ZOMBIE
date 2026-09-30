using Godot;

namespace ZombieApocalypse.Core.Utilities;

/// <summary>
/// Mathematical utility methods for isometric coordinate conversions,
/// raycasting, and spatial calculations.
/// </summary>
public static class MathUtils
{
    /// <summary>True isometric camera pitch angle in degrees: -arctan(1/√2) ≈ -35.264°.</summary>
    public const float IsometricPitchDeg = -35.264f;

    /// <summary>Isometric camera yaw angle in degrees (rotates world into diamond grid).</summary>
    public const float IsometricYawDeg = -45f;

    /// <summary>
    /// Project screen coordinates onto a horizontal world plane at a given height.
    /// This is the single source of truth for mouse aiming: the plane is the one
    /// the weapon actually fires along, not the world origin plane.
    ///
    /// Never returns <see cref="Vector3.Zero"/> as a failure signal — a miss
    /// (ray parallel to, or pointing away from, the plane) falls back to a point
    /// in front of the camera instead, because callers feed the result straight
    /// into an aim direction and an origin fallback aims at the world origin.
    /// </summary>
    /// <param name="camera">The Camera3D doing the projection.</param>
    /// <param name="screenPos">Screen-space coordinates (e.g., mouse position).</param>
    /// <param name="planeHeight">World Y of the target plane (weapon height).</param>
    public static Vector3 ScreenToWorldOnPlane(Camera3D camera, Vector2 screenPos, float planeHeight)
    {
        if (camera == null || !GodotObject.IsInstanceValid(camera))
            return Vector3.Zero;

        Vector3 rayOrigin = camera.ProjectRayOrigin(screenPos);
        Vector3 rayDir = camera.ProjectRayNormal(screenPos);

        var plane = new Plane(Vector3.Up, planeHeight);

        // IntersectsRay returns null when the ray is parallel to the plane.
        var hit = plane.IntersectsRay(rayOrigin, rayDir);
        if (hit.HasValue)
            return hit.Value;

        // Degenerate ray: keep a stable point in front of the camera along the
        // same screen ray, projected onto the plane by its dominant axis.
        Vector3 flat = rayDir;
        flat.Y = 0f;
        if (flat.LengthSquared() < 0.0001f) flat = -camera.GlobalTransform.Basis.Z;
        flat = flat.Normalized();
        return rayOrigin + flat * 10f;
    }

    /// <summary>
    /// Project screen coordinates onto the world ground plane (Y=0).
    /// Prefer <see cref="ScreenToWorldOnPlane"/> with the weapon's height.
    /// </summary>
    public static Vector3 ScreenToWorldIso(Camera3D camera, Vector2 screenPos)
        => ScreenToWorldOnPlane(camera, screenPos, 0f);

    /// <summary>
    /// Aim direction from a muzzle to a world target, constrained to the XZ
    /// plane: bullets travel horizontally toward the cursor, so the vertical
    /// component of the muzzle/target height difference is discarded.
    /// </summary>
    public static Vector3 HorizontalDirectionTo(Vector3 muzzle, Vector3 target)
    {
        Vector3 dir = target - muzzle;
        dir.Y = 0f;
        return dir.LengthSquared() > 0.0001f ? dir.Normalized() : Vector3.Zero;
    }

    /// <summary>
    /// Cone spread around a horizontal forward vector, computed entirely in the
    /// XZ plane. Rotating a 3D vector around Y tilts it off the plane, which is
    /// what makes shotgun pellets drift vertically; building the direction from
    /// an explicit forward/right basis keeps the cone flat.
    /// </summary>
    /// <param name="forwardXZ">Normalized forward direction on the XZ plane.</param>
    /// <param name="angleRadians">Signed angle from forward, in radians.</param>
    public static Vector3 SpreadHorizontal(Vector3 forwardXZ, float angleRadians)
    {
        Vector3 forward = new Vector3(forwardXZ.X, 0f, forwardXZ.Z);
        if (forward.LengthSquared() < 0.0001f) forward = Vector3.Forward;
        forward = forward.Normalized();

        Vector3 right = forward.Cross(Vector3.Up).Normalized();
        return (forward * Mathf.Cos(angleRadians) + right * Mathf.Sin(angleRadians)).Normalized();
    }

    /// <summary>
    /// Convert a 2D input vector (WASD / analog stick) into a 3D world-space
    /// movement direction aligned with the isometric camera orientation.
    /// </summary>
    /// <remarks>
    /// The camera pivot is rotated -45° around Y, so pressing "W" (screen-up)
    /// needs to move the character along the camera's forward-projected direction
    /// rather than world -Z. This method handles that rotation.
    /// </remarks>
    /// <param name="inputDir">Raw 2D input: X = left/right, Y = forward/backward.</param>
    /// <param name="cameraYRotation">Camera pivot's Y rotation in radians.</param>
    /// <returns>Normalized 3D direction on the XZ plane, or Vector3.Zero if no input.</returns>
    public static Vector3 InputToIsometricDirection(Vector2 inputDir, float cameraYRotation)
    {
        if (inputDir.LengthSquared() < 0.001f)
            return Vector3.Zero;

        // Map 2D input to 3D: X stays X, Y becomes Z (forward/backward → world Z)
        var direction = new Vector3(inputDir.X, 0f, inputDir.Y);

        // Rotate by the camera's Y rotation so movement aligns with the isometric grid
        direction = direction.Rotated(Vector3.Up, cameraYRotation);

        return direction.Normalized();
    }

    /// <summary>
    /// Squared distance between two points on the XZ plane (ignoring height).
    /// Use this instead of full distance when you only need comparison (avoids sqrt).
    /// </summary>
    public static float DistanceSquaredXZ(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    /// <summary>
    /// Distance between two points projected onto the XZ plane (ignoring height).
    /// </summary>
    public static float DistanceXZ(Vector3 a, Vector3 b)
    {
        return Mathf.Sqrt(DistanceSquaredXZ(a, b));
    }

    /// <summary>
    /// Snap a world position to the nearest point on a uniform grid.
    /// Y coordinate is preserved.
    /// </summary>
    /// <param name="worldPos">Position to snap.</param>
    /// <param name="gridSize">Grid cell size in world units.</param>
    /// <returns>Snapped position with original Y.</returns>
    public static Vector3 SnapToGrid(Vector3 worldPos, float gridSize)
    {
        return new Vector3(
            Mathf.Round(worldPos.X / gridSize) * gridSize,
            worldPos.Y,
            Mathf.Round(worldPos.Z / gridSize) * gridSize
        );
    }

    /// <summary>
    /// Check whether a point lies within a 2D angle range (for vision cones, etc.).
    /// All angles in radians.
    /// </summary>
    /// <param name="sourcePos">Origin of the cone (XZ only).</param>
    /// <param name="facingAngle">Center direction of the cone in radians.</param>
    /// <param name="halfAngle">Half-width of the cone in radians.</param>
    /// <param name="targetPos">Point to test (XZ only).</param>
    /// <param name="maxRange">Maximum distance of the cone.</param>
    /// <returns>True if the target is inside the cone.</returns>
    public static bool IsInCone(Vector3 sourcePos, float facingAngle, float halfAngle,
                                 Vector3 targetPos, float maxRange)
    {
        float dx = targetPos.X - sourcePos.X;
        float dz = targetPos.Z - sourcePos.Z;
        float distSq = dx * dx + dz * dz;

        if (distSq > maxRange * maxRange)
            return false;

        float angleToTarget = Mathf.Atan2(dx, dz);
        float angleDiff = Mathf.Abs(Mathf.AngleDifference(facingAngle, angleToTarget));

        return angleDiff <= halfAngle;
    }
}
