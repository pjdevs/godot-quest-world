using Godot;

namespace QuestWorld.Character;

public partial class Character : CharacterBody3D
{
    public enum ViewMode
    {
        ThirdPerson,
        FirstPerson
    }

    [ExportGroup("View")]
    [Export]
    public ViewMode CurrentViewMode
    {
        get => _currentViewMode;
        set => SetViewMode(value);
    }

    [Export]
    public Vector3 FirstPersonCameraOffset
    {
        get => _firstPersonCameraOffset;
        set
        {
            _firstPersonCameraOffset = value;
            if (_cameraRig != null)
            {
                _cameraRig.FirstPersonCameraOffset = value;
                _cameraRig.SetViewMode(_currentViewMode);
            }
        }
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
    private Vector3 _firstPersonCameraOffset = new(0.0f, 0.0f, -0.2f);
    private Node3D _visual = null!;
    private CharacterCameraRig _cameraRig = null!;
    private CharacterCameraEffects _cameraEffects = null!;
    private CharacterAnimationController _animationController = null!;
    private CharacterInputFrame _pendingInput;
    private CharacterFrameState _latestFrame;
    private float _airborneDuration;
    private bool _hasFloorSample;
    private bool _wasGrounded;
    private bool _configurationValid;
    private bool _isPossessed;
    private CharacterPlayerController _possessingController = null!;
    private ulong _frameNumber;
    private ulong _networkPresentationFrameNumber;
    private bool _networkPresentationHasGroundSample;
    private bool _networkPresentationWasGrounded;

    public bool IsPossessed => _isPossessed;

    public CharacterPlayerController PossessingController => _possessingController;

    public bool IsTurnInPlaceActive => _animationController?.IsTurnInPlaceActive ?? false;

    public CharacterFrameState LatestFrame => _latestFrame;

    public bool IsLocalNetworkAuthority => IsMultiplayerAuthority();

    public Node3D CameraPitchNode => _cameraRig?.CameraPitch
        ?? GetNodeOrNull<Node3D>("CameraYaw/CameraPitch")!;

    public float MouseSensitivity => _cameraRig?.MouseSensitivity ?? 0.002f;

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

        _cameraRig.FirstPersonCameraOffset = _firstPersonCameraOffset;
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
        if (!_configurationValid)
        {
            return;
        }

        float frameDelta = (float)delta;
        bool groundedBeforeMove = IsOnFloor();
        bool wasGrounded = _hasFloorSample && _wasGrounded;
        Vector3 moveDirection = GetViewRelativeDirection(input.Move, input.ViewYaw);
        bool sprintRequested = groundedBeforeMove
            && input.SprintHeld
            && -input.Move.Y >= Mathf.Clamp(SprintForwardInputThreshold, 0.0f, 1.0f);
        float targetSpeed = sprintRequested ? RunSpeed : WalkSpeed;
        Vector3 targetVelocity = moveDirection * targetSpeed;
        float acceleration = groundedBeforeMove ? Acceleration : AirAcceleration;
        bool jumped = false;

        Vector3 velocity = Velocity;
        velocity.X = Mathf.MoveToward(velocity.X, targetVelocity.X, acceleration * frameDelta);
        velocity.Z = Mathf.MoveToward(velocity.Z, targetVelocity.Z, acceleration * frameDelta);
        if (groundedBeforeMove)
        {
            if (input.JumpPressed)
            {
                velocity.Y = JumpVelocity;
                jumped = true;
            }
            else if (velocity.Y < 0.0f)
            {
                velocity.Y = -0.1f;
            }
        }
        else
        {
            velocity += GetGravity() * frameDelta;
        }

        float impactSpeed = Mathf.Max(-velocity.Y, 0.0f);
        Velocity = velocity;
        MoveAndSlide();

        bool isGrounded = IsOnFloor();
        bool sprinting = isGrounded && sprintRequested;
        float sampledAirTime = _airborneDuration;
        if (!groundedBeforeMove || !isGrounded)
        {
            sampledAirTime += frameDelta;
        }

        bool landed = _hasFloorSample
            && !_wasGrounded
            && isGrounded
            && sampledAirTime >= Mathf.Max(MinimumLandingAirTime, 0.0f)
            && impactSpeed >= Mathf.Max(MinimumLandingImpactSpeed, 0.0f);
        float landingStrength = landed ? CalculateLandingStrength(impactSpeed) : 0.0f;
        _airborneDuration = isGrounded ? 0.0f : sampledAirTime;
        _wasGrounded = isGrounded;
        _hasFloorSample = true;

        _frameNumber++;
        _latestFrame = new CharacterFrameState(
            _frameNumber,
            input,
            moveDirection,
            GetRealVelocity(),
            wasGrounded,
            isGrounded,
            jumped,
            landed,
            sprinting,
            impactSpeed,
            landingStrength);
        NetworkIsGrounded = isGrounded;
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

    internal void SubmitInputFrame(CharacterPlayerController controller, CharacterInputFrame inputFrame)
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
            "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects")!;
        _animationController = GetNodeOrNull<CharacterAnimationController>("AnimationController")!;

        bool valid = true;
        valid &= RequireNode(_visual, "Visual");
        valid &= RequireNode(_cameraRig, "CameraYaw");
        valid &= RequireNode(_cameraEffects, "CameraYaw/CameraPitch/SpringArm3D/CameraAnchor/CameraEffects");
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
        float delta)
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
        Vector2 moveInput = new Vector2(localVelocity.X / runSpeed, -localVelocity.Z / runSpeed).LimitLength(1.0f);
        Vector3 moveDirection = horizontalVelocity.LengthSquared() > 0.0001f
            ? horizontalVelocity.Normalized()
            : Vector3.Zero;
        bool wasGrounded = _networkPresentationHasGroundSample && _networkPresentationWasGrounded;
        bool landed = _networkPresentationHasGroundSample
            && !_networkPresentationWasGrounded
            && NetworkIsGrounded;

        CharacterSimulationInput presentationInput = new(
            moveInput,
            _cameraRig.Rotation.Y,
            _cameraRig.CameraPitch.Rotation.X,
            false,
            false);
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
            0.0f);

        _animationController.ApplyFrame(_latestFrame, _currentViewMode, 0.0f, delta);
        _networkPresentationWasGrounded = NetworkIsGrounded;
        _networkPresentationHasGroundSample = true;
    }

    private Vector3 GetViewRelativeDirection(Vector2 input, float viewYaw)
    {
        Basis viewBasis = new(Vector3.Up, viewYaw);
        Basis viewGlobalBasis = GlobalBasis * viewBasis;
        Vector3 forward = -viewGlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        Vector3 right = viewGlobalBasis.X;
        right.Y = 0.0f;
        right = right.Normalized();
        Vector3 direction = right * input.X + forward * -input.Y;
        return direction.LengthSquared() > 1.0f ? direction.Normalized() : direction;
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

    private float CalculateLandingStrength(float impactSpeed)
    {
        float minimumImpact = Mathf.Max(MinimumLandingImpactSpeed, 0.0f);
        float fullImpact = Mathf.Max(FullLandingImpactSpeed, minimumImpact + 0.001f);
        float normalizedImpact = Mathf.Clamp(
            (impactSpeed - minimumImpact) / (fullImpact - minimumImpact),
            0.0f,
            1.0f);
        return Mathf.Lerp(Mathf.Clamp(MinimumLandingStrength, 0.0f, 1.0f), 1.0f, normalizedImpact);
    }
}
