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
    /// Project screen coordinates onto the world ground plane (Y=0) through an isometric camera.
    /// Used for mouse-based aiming, placement previews, and click-to-move targets.
    /// </summary>
    /// <param name="camera">The orthographic isometric Camera3D.</param>
    /// <param name="screenPos">Screen-space coordinates (e.g., mouse position).</param>
    /// <returns>World position on the Y=0 plane, or Vector3.Zero if the ray is parallel to the plane.</returns>
    public static Vector3 ScreenToWorldIso(Camera3D camera, Vector2 screenPos)
    {
        var rayOrigin = camera.ProjectRayOrigin(screenPos);
        var rayDir = camera.ProjectRayNormal(screenPos);

        // Intersect with the Y=0 horizontal ground plane
        if (Mathf.Abs(rayDir.Y) > 0.0001f)
        {
            float t = -rayOrigin.Y / rayDir.Y;
            if (t >= 0f)
            {
                return rayOrigin + rayDir * t;
            }
        }

        return Vector3.Zero;
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
