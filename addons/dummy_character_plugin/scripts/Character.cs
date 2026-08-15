using Godot;

namespace QuestWorld.Character;

public partial class Character : CharacterBody3D
{
    public enum ViewMode
    {
        ThirdPerson,
        FirstPerson,
    }

    [ExportGroup("View")]
    [Export]
    public ViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set => SetViewMode(value);
    }

    [Export]
    public float ThirdPersonForwardAlignmentThreshold { get; set; } = 0.75f;

    [ExportGroup("Movement")]
    [Export]
    public float WalkSpeed { get; set; } = 3.0f;

    [Export]
    public float RunSpeed { get; set; } = 6.0f;

    [Export]
    public float Acceleration { get; set; } = 15.0f;

    [Export]
    public float AirAcceleration { get; set; } = 5.0f;

    [Export]
    public float JumpVelocity { get; set; } = 5.0f;

    [Export]
    public float RotationSpeed { get; set; } = 10.0f;

    [Export]
    public float SprintForwardInputThreshold { get; set; } = 0.5f;

    [ExportGroup("Landing")]
    [Export]
    public float MinimumLandingAirTime { get; set; } = 0.1f;

    [Export]
    public float MinimumLandingImpactSpeed { get; set; } = 2.0f;

    [Export]
    public float FullLandingImpactSpeed { get; set; } = 10.0f;

    [Export]
    public float MinimumLandingStrength { get; set; } = 0.35f;

    [ExportGroup("Networking")]
    [Export]
    public int OwnerPeerId { get; private set; } = 1;

    [Export]
    public bool NetworkIsGrounded { get; set; } = true;

    private ViewMode _currentViewMode = ViewMode.ThirdPerson;
    private Node3D _visual = null!;
    private CharacterCameraRig _cameraRig = null!;
    private CharacterCameraEffects _cameraEffects = null!;
    private CharacterAnimationController _animationController = null!;
    private CharacterMovement _movement = null!;
    private CharacterInputFrame _pendingInput;
    private CharacterFrameState _latestFrame;
    private bool _configurationValid;
    private bool _isPossessed;
    private CharacterPlayerController _possessingController = null!;
    private ulong _networkPresentationFrameNumber;
    private bool _networkPresentationHasGroundSample;
    private bool _networkPresentationWasGrounded;

    public bool IsPossessed => _isPossessed;

    public CharacterPlayerController PossessingController => _possessingController;

    public bool IsTurnInPlaceActive => _animationController?.IsTurnInPlaceActive ?? false;

    public CharacterFrameState LatestFrame => _latestFrame;

    public CharacterMovement Movement => _movement;

    public bool IsLocalNetworkAuthority => IsMultiplayerAuthority();

    public Node3D CameraPitchNode =>
        _cameraRig?.CameraPitch ?? GetNodeOrNull<Node3D>("CameraYaw/CameraPitch")!;

    public CharacterCameraRig CameraRig => _cameraRig;

    public override void _EnterTree()
    {
        if (NetworkPlayerIdentity.TryGetPeerId(Name, out int peerId))
        {
            OwnerPeerId = peerId;
            SetMultiplayerAuthority(peerId);
        }
    }

    public override void _Ready()
    {
        _movement = new CharacterMovement(this);
        _configurationValid = ResolveNodes();
        if (!_configurationValid)
        {
            SetPhysicsProcess(false);
            return;
        }

        _configurationValid = _animationController.Initialize(this, _visual);
        if (!_configurationValid)
        {
            SetPhysicsProcess(false);
            return;
        }

        _cameraEffects.Initialize(this);
        ApplyViewMode();
        _cameraRig.SetActive(_isPossessed);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_configurationValid || !IsMultiplayerAuthority())
        {
            return;
        }

        CharacterInputFrame input = _pendingInput;
        _pendingInput = CharacterInputFrame.Empty;
        float yawDelta = _cameraRig.ApplyLook(input);
        CharacterSimulationInput simulationInput = new(
            input.Move,
            _cameraRig.Rotation.Y,
            _cameraRig.CameraPitch.Rotation.X,
            input.JumpPressed,
            input.SprintHeld
        );
        Simulate(simulationInput, delta);
        ApplyLocalPresentation(simulationInput, input.LookDelta, yawDelta, (float)delta);
    }

    public override void _Process(double delta)
    {
        if (!_configurationValid || IsMultiplayerAuthority())
        {
            return;
        }

        ApplyRemotePresentation((float)delta);
    }

    /// <summary>
    /// Advances the authoritative character motor without reading local camera state or applying presentation.
    /// </summary>
    public void Simulate(CharacterSimulationInput input, double delta)
    {
        if (!_configurationValid || _movement == null)
        {
            return;
        }

        _latestFrame = _movement.Simulate(input, delta, GetMovementSettings());
        NetworkIsGrounded = _latestFrame.IsGrounded;
    }

    public void SubmitInputFrame(CharacterInputFrame inputFrame)
    {
        _pendingInput = inputFrame;
    }

    internal void TakePossession(CharacterPlayerController controller)
    {
        _possessingController = controller;
        _isPossessed = true;
        _pendingInput = CharacterInputFrame.Empty;
        if (_cameraRig != null)
        {
            _cameraRig.SetActive(true);
        }
    }

    internal void ReleasePossession(CharacterPlayerController controller)
    {
        if (_possessingController != controller)
        {
            return;
        }

        _possessingController = null!;
        _isPossessed = false;
        _pendingInput = CharacterInputFrame.Empty;
        _cameraRig?.SetActive(false);
        _animationController?.CancelIncompatibleState();
    }

    internal void SubmitInputFrame(
        CharacterPlayerController controller,
        CharacterInputFrame inputFrame
    )
    {
        if (_possessingController == controller)
        {
            _pendingInput = inputFrame;
        }
    }

    public void SetViewMode(ViewMode mode)
    {
        _currentViewMode = mode;
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        if (_cameraRig == null)
        {
            return;
        }

        _cameraRig.SetViewMode(_currentViewMode);
        _cameraEffects?.ResetPose();
        _animationController?.OnViewModeChanged(_currentViewMode);
    }

    private bool ResolveNodes()
    {
        _visual = GetNodeOrNull<Node3D>("Visual")!;
        _cameraRig = GetNodeOrNull<CharacterCameraRig>("CameraYaw")!;
        _cameraEffects = GetNodeOrNull<CharacterCameraEffects>(
            "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects"
        )!;
        _animationController = GetNodeOrNull<CharacterAnimationController>("AnimationController")!;

        bool valid = true;
        valid &= RequireNode(_visual, "Visual");
        valid &= RequireNode(_cameraRig, "CameraYaw");
        valid &= RequireNode(
            _cameraEffects,
            "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects"
        );
        valid &= RequireNode(_animationController, "AnimationController");
        return valid;
    }

    private bool RequireNode(Node node, string path)
    {
        if (node != null)
        {
            return true;
        }

        GD.PushError($"{Name}: character scene is missing required node '{path}'.");
        return false;
    }

    private void ApplyLocalPresentation(
        CharacterSimulationInput input,
        Vector2 lookDelta,
        float yawDelta,
        float delta
    )
    {
        UpdateVisualOrientation(input.ViewYaw, _latestFrame.MoveDirection, delta);
        _animationController.ApplyFrame(_latestFrame, _currentViewMode, yawDelta, delta);
        _cameraEffects.PushFrame(_latestFrame, lookDelta);
    }

    private void ApplyRemotePresentation(float delta)
    {
        Vector3 replicatedVelocity = Velocity;
        Vector3 horizontalVelocity = new(replicatedVelocity.X, 0.0f, replicatedVelocity.Z);
        float runSpeed = Mathf.Max(RunSpeed, 0.001f);
        Vector3 localVelocity = GlobalBasis.Inverse() * horizontalVelocity;
        Vector2 moveInput = new Vector2(
            localVelocity.X / runSpeed,
            -localVelocity.Z / runSpeed
        ).LimitLength(1.0f);
        Vector3 moveDirection =
            horizontalVelocity.LengthSquared() > 0.0001f
                ? horizontalVelocity.Normalized()
                : Vector3.Zero;
        bool wasGrounded = _networkPresentationHasGroundSample && _networkPresentationWasGrounded;
        bool landed =
            _networkPresentationHasGroundSample
            && !_networkPresentationWasGrounded
            && NetworkIsGrounded;

        CharacterSimulationInput presentationInput = new(
            moveInput,
            _cameraRig.Rotation.Y,
            _cameraRig.CameraPitch.Rotation.X,
            false,
            false
        );
        _networkPresentationFrameNumber++;
        _latestFrame = new CharacterFrameState(
            _networkPresentationFrameNumber,
            presentationInput,
            moveDirection,
            replicatedVelocity,
            wasGrounded,
            NetworkIsGrounded,
            false,
            landed,
            NetworkIsGrounded && horizontalVelocity.Length() >= runSpeed * 0.9f,
            Mathf.Max(-replicatedVelocity.Y, 0.0f),
            0.0f
        );

        _animationController.ApplyFrame(_latestFrame, _currentViewMode, 0.0f, delta);
        _networkPresentationWasGrounded = NetworkIsGrounded;
        _networkPresentationHasGroundSample = true;
    }

    private void UpdateVisualOrientation(float viewYaw, Vector3 moveDirection, float delta)
    {
        float targetLocalYaw = _visual.Rotation.Y;
        if (_currentViewMode == ViewMode.FirstPerson)
        {
            targetLocalYaw = viewYaw;
        }
        else if (moveDirection.LengthSquared() > 0.0001f)
        {
            Basis viewBasis = new(Vector3.Up, viewYaw);
            Vector3 cameraForward = -(GlobalBasis * viewBasis).Z;
            cameraForward.Y = 0.0f;
            cameraForward = cameraForward.Normalized();
            float forwardAlignment = moveDirection.Dot(cameraForward);
            if (forwardAlignment >= ThirdPersonForwardAlignmentThreshold)
            {
                Vector3 localDirection = GlobalBasis.Inverse() * moveDirection;
                targetLocalYaw = Mathf.Atan2(-localDirection.X, -localDirection.Z);
            }
            else
            {
                targetLocalYaw = _cameraRig.Rotation.Y;
            }
        }

        float turnWeight = 1.0f - Mathf.Exp(-Mathf.Max(RotationSpeed, 0.0f) * delta);
        Vector3 visualRotation = _visual.Rotation;
        visualRotation.Y = Mathf.LerpAngle(visualRotation.Y, targetLocalYaw, turnWeight);
        _visual.Rotation = visualRotation;
    }

    private CharacterMovementSettings GetMovementSettings()
    {
        return new CharacterMovementSettings(
            WalkSpeed,
            RunSpeed,
            Acceleration,
            AirAcceleration,
            JumpVelocity,
            SprintForwardInputThreshold,
            MinimumLandingAirTime,
            MinimumLandingImpactSpeed,
            FullLandingImpactSpeed,
            MinimumLandingStrength
        );
    }
}
