using Godot;
using ZombieApocalypse.Core.Autoloads;
using ZombieApocalypse.Core.Utilities;

namespace ZombieApocalypse.Entities.Player;

/// <summary>
/// Player character controller for 2.5D isometric movement.
/// 
/// Architecture:
/// - CharacterBody3D root node (never rotates — camera is parented here)
/// - CollisionShape3D child (capsule at Y=0.9 so bottom touches Y=0)
/// - Mesh child (rotates to face movement direction)
/// - CameraPivot child (rotated -45° Y for isometric diamond grid)
///   └── Camera3D (rotated -35.264° X for true isometric pitch, orthographic)
/// 
/// Input is transformed from screen-space to world-space using the camera pivot's
/// Y rotation, so pressing W always moves "up" on screen regardless of world axes.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    // ── Movement Tuning ────────────────────────────────────────────
    [ExportGroup("Movement")]
    [Export] public float MoveSpeed = 5.0f;
    [Export] public float SprintMultiplier = 1.6f;
    [Export] public float Acceleration = 25.0f;
    [Export] public float Friction = 20.0f;
    [Export] public float RotationSmoothing = 0.15f;
    [Export] public float Gravity = 30.0f;

    // ── Camera Tuning ──────────────────────────────────────────────
    [ExportGroup("Camera")]
    [Export] public float CameraDistance = 25.0f;
    [Export] public float CameraSize = 15.0f;
    [Export] public float CameraNearClip = 0.1f;
    [Export] public float CameraFarClip = 100.0f;

    // ── Node References (cached in _Ready) ─────────────────────────
    private Node3D _cameraPivot = null!;
    private Camera3D _camera = null!;
    private Node3D _mesh = null!;
    private ZombieApocalypse.Core.Components.AudioEmitterComponent? _audioEmitter;
    private PlayerStats? _playerStats;
    private ZombieApocalypse.Core.Components.InventoryComponent? _inventory;
    private ZombieApocalypse.Core.Components.HealthComponent? _health;

    // ── Runtime State ──────────────────────────────────────────────
    private Vector3 _moveDirection;
    private Vector3 _lastFacingDirection = Vector3.Forward;
    private bool _isSprinting;
    private float _footstepTimer = 0f;

    // ── Public Accessors ───────────────────────────────────────────
    /// <summary>Whether the player is currently providing movement input.</summary>
    public bool IsMoving => _moveDirection.LengthSquared() > 0.001f;
    /// <summary>Whether the player is sprinting.</summary>
    public bool IsSprinting => _isSprinting && IsMoving;
    /// <summary>Current horizontal speed in m/s.</summary>
    public float CurrentSpeed => new Vector3(Velocity.X, 0, Velocity.Z).Length();
    /// <summary>The isometric camera instance (for raycasting, FoW, etc.).</summary>
    public Camera3D Camera => _camera;

    // ════════════════════════════════════════════════════════════════
    // Lifecycle
    // ════════════════════════════════════════════════════════════════

    public override void _Ready()
    {
        AddToGroup("player");

        _cameraPivot = GetNode<Node3D>("CameraPivot");
        _camera = _cameraPivot.GetNode<Camera3D>("Camera3D");
        _mesh = GetNode<Node3D>("Mesh");
        _audioEmitter = GetNodeOrNull<ZombieApocalypse.Core.Components.AudioEmitterComponent>("AudioEmitterComponent");
        _playerStats = GetNodeOrNull<PlayerStats>("PlayerStats");
        _inventory = GetNodeOrNull<ZombieApocalypse.Core.Components.InventoryComponent>("InventoryComponent");

        var health = GetNodeOrNull<ZombieApocalypse.Core.Components.HealthComponent>("HealthComponent");
        if (health != null)
        {
            _health = health;
            health.Died += OnPlayerDied;
        }

        SetupIsometricCamera();

        GD.Print("[PlayerController] Ready. Isometric camera configured.");
        GD.Print($"  Camera pivot Y: {_cameraPivot.RotationDegrees.Y}°");
        GD.Print($"  Camera pitch:   {_camera.RotationDegrees.X}°");
        GD.Print($"  Ortho size:     {_camera.Size}");
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Dead players collapse in place until restart ([P]).
        if (_health != null && !_health.IsAlive)
        {
            var v = Velocity;
            v.X = 0f;
            v.Z = 0f;
            Velocity = v;
            ApplyGravity(dt);
            MoveAndSlide();
            return;
        }

        ReadMovementInput();
        ApplyGravity(dt);
        ApplyMovement(dt);
        UpdateMeshFacing();
        UpdateFootsteps(dt);

        MoveAndSlide();

        // Broadcast position for systems that track the player (FoW, acoustic, etc.)
        EventBus.Instance?.EmitPlayerMoved(GlobalPosition);
    }

    private void OnPlayerDied()
    {
        _isSprinting = false;
        _playerStats?.SetSprinting(false);
        GameManager.Instance?.HandlePlayerDeath();
        GD.Print("[PlayerController] You died. Press [P] to restart.");
    }

    private void UpdateFootsteps(float dt)
    {
        if (IsMoving && IsOnFloor())
        {
            _footstepTimer += dt;
            float stepInterval = IsSprinting ? 0.28f : 0.45f;
            if (_footstepTimer >= stepInterval)
            {
                _footstepTimer = 0f;
                _audioEmitter?.EmitFootstep(IsSprinting);
            }
        }
        else
        {
            _footstepTimer = 0f;
        }
    }

    // ════════════════════════════════════════════════════════════════
    // Camera Setup
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Configure the camera pivot and camera node for true isometric projection.
    /// All transform setup happens in code for clarity and easy tuning via exports.
    /// Focuses the camera center directly on the ground plane (Y=0) beneath the player.
    /// </summary>
    private void SetupIsometricCamera()
    {
        // Pivot is centered at ground level (Y=0)
        _cameraPivot.Position = Vector3.Zero;
        // Pivot rotates on Y to create the isometric diamond grid orientation (-45°)
        _cameraPivot.RotationDegrees = new Vector3(0f, MathUtils.IsometricYawDeg, 0f);

        // Calculate elevation and setback so the optical axis points directly at the ground center (0, 0, 0)
        float pitchRad = Mathf.DegToRad(Mathf.Abs(MathUtils.IsometricPitchDeg));
        float elevation = CameraDistance * Mathf.Sin(pitchRad);
        float setback = CameraDistance * Mathf.Cos(pitchRad);

        // Position camera elevated and pulled back along local axes
        _camera.Position = new Vector3(0f, elevation, setback);
        // Tilt camera down at true isometric angle (-35.264°)
        _camera.RotationDegrees = new Vector3(MathUtils.IsometricPitchDeg, 0f, 0f);

        // Orthographic projection removes perspective foreshortening, giving
        // the classic isometric look where parallel lines stay parallel.
        _camera.Projection = Camera3D.ProjectionType.Orthogonal;
        _camera.Size = CameraSize;
        _camera.Near = CameraNearClip; // 0.1f
        _camera.Far = CameraFarClip;   // 100.0f
        _camera.Current = true;
        _camera.CullMask = 0xFFFFF; // Ensure all layers (including Layer 1 for ground/player/obstacles) are culled and drawn
    }

    // ════════════════════════════════════════════════════════════════
    // Movement
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read WASD/arrow keys and convert the 2D input into an isometric-aligned
    /// 3D movement direction using the camera pivot's Y rotation.
    /// Supports InputMap actions ("move_forward"/"move_backward", "move_up"/"move_down")
    /// with direct physical key checking fallback.
    /// </summary>
    private void ReadMovementInput()
    {
        // 1. Primary action map
        var inputDir = Input.GetVector(
            "move_left", "move_right",
            "move_forward", "move_backward"
        );

        // 2. Action alias fallback
        if (inputDir.LengthSquared() < 0.001f)
        {
            inputDir = Input.GetVector(
                "move_left", "move_right",
                "move_up", "move_down"
            );
        }

        // 3. Raw key checking fallback (guarantees WASD and Arrow key responsiveness)
        if (inputDir.LengthSquared() < 0.001f)
        {
            float x = 0f;
            float y = 0f;

            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) || Input.IsPhysicalKeyPressed(Key.A))
                x -= 1f;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) || Input.IsPhysicalKeyPressed(Key.D))
                x += 1f;
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) || Input.IsPhysicalKeyPressed(Key.W))
                y -= 1f;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) || Input.IsPhysicalKeyPressed(Key.S))
                y += 1f;

            if (x != 0f || y != 0f)
            {
                inputDir = new Vector2(x, y).Normalized();
            }
        }

        bool wantsSprint = Input.IsActionPressed("sprint") || Input.IsKeyPressed(Key.Shift) || Input.IsPhysicalKeyPressed(Key.Shift);
        if (_playerStats != null)
        {
            _isSprinting = wantsSprint && IsMoving && _playerStats.CanSprint();
            _playerStats.SetSprinting(_isSprinting);
        }
        else
        {
            _isSprinting = wantsSprint && IsMoving;
        }

        _moveDirection = MathUtils.InputToIsometricDirection(
            inputDir,
            _cameraPivot.Rotation.Y
        );
    }

    /// <summary>Apply downward acceleration when airborne with ground adhesion.</summary>
    private void ApplyGravity(float dt)
    {
        var v = Velocity;
        if (!IsOnFloor())
        {
            v.Y -= Gravity * dt;
        }
        else if (v.Y < 0f)
        {
            v.Y = -0.1f;
        }
        Velocity = v;
    }

    /// <summary>
    /// Acceleration/friction-based horizontal movement.
    /// Produces smooth starts and stops instead of instant velocity snapping.
    /// </summary>
    private void ApplyMovement(float dt)
    {
        float speedMod = (_inventory != null && _inventory.IsEncumbered) ? 0.75f : 1.0f;
        float targetSpeed = IsMoving
            ? MoveSpeed * (_isSprinting ? SprintMultiplier : 1.0f) * speedMod
            : 0f;

        var v = Velocity;
        var horizontal = new Vector3(v.X, 0f, v.Z);

        if (IsMoving)
        {
            // Accelerate toward the desired direction
            horizontal = horizontal.MoveToward(_moveDirection * targetSpeed, Acceleration * dt);
        }
        else
        {
            // Decelerate via friction
            horizontal = horizontal.MoveToward(Vector3.Zero, Friction * dt);
        }

        v.X = horizontal.X;
        v.Z = horizontal.Z;
        Velocity = v;
    }

    /// <summary>
    /// Rotate the mesh to face the movement direction.
    /// Only the mesh rotates — the CharacterBody3D root stays axis-aligned
    /// so the parented camera maintains its fixed isometric orientation.
    /// </summary>
    private void UpdateMeshFacing()
    {
        var horizontal = new Vector3(Velocity.X, 0f, Velocity.Z);
        if (horizontal.LengthSquared() < 0.1f)
            return;

        _lastFacingDirection = horizontal.Normalized();
        float targetAngle = Mathf.Atan2(_lastFacingDirection.X, _lastFacingDirection.Z);
        float currentAngle = _mesh.Rotation.Y;
        _mesh.Rotation = new Vector3(0f, Mathf.LerpAngle(currentAngle, targetAngle, RotationSmoothing), 0f);
    }

    // ════════════════════════════════════════════════════════════════
    // Public API (for combat, interaction, and UI systems)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Height of the plane the player aims on: the weapon's line of fire, not
    /// the world floor. Aiming at the floor makes the shot climb toward the
    /// camera-height cursor point and drift as the player moves.
    /// </summary>
    [Export] public float AimPlaneHeight = 1.2f;

    /// <summary>
    /// The world point the player is aiming at: the mouse cursor projected onto
    /// the weapon-height plane. This is the target bullets are fired at.
    /// </summary>
    public Vector3 GetAimTargetPoint()
    {
        return MathUtils.ScreenToWorldOnPlane(
            _camera, GetViewport().GetMousePosition(), GlobalPosition.Y + AimPlaneHeight);
    }

    /// <summary>
    /// Get the world position on the aim plane under the mouse cursor.
    /// Used for aiming, placement previews, and click-to-move.
    /// </summary>
    public Vector3 GetMouseWorldPosition() => GetAimTargetPoint();

    /// <summary>
    /// Get the normalized direction from the player to the mouse cursor, flattened
    /// onto the XZ plane. Bullets travel horizontally, so the Y component of the
    /// target/muzzle height difference is discarded rather than tilting the shot.
    /// </summary>
    public Vector3 GetAimDirection()
    {
        Vector3 dir = MathUtils.HorizontalDirectionTo(GlobalPosition, GetAimTargetPoint());
        return dir.LengthSquared() > 0.001f ? dir : _lastFacingDirection;
    }

    /// <summary>
    /// Get the direction the player is currently facing (based on last movement).
    /// </summary>
    public Vector3 GetFacingDirection() => _lastFacingDirection;
}
