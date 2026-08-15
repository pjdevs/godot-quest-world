using Godot;

namespace QuestWorld.Character;

public partial class CharacterAnimationController : Node
{
    private const string LocomotionState = "Locomotion";
    private const string JumpState = "Jump";
    private const string FallState = "Fall";
    private const string TurnLeftState = "TurnLeft";
    private const string TurnRightState = "TurnRight";
    private const string PlaybackPath = "parameters/StateMachine/playback";
    private const string BlendPositionPath = "parameters/StateMachine/Locomotion/blend_position";
    private const string AnimationTimeScalePath = "parameters/TimeScale/scale";
    private const string LandingOneShotRequestPath = "parameters/LandOneShot/request";

    private static readonly string[] BaseAnimations =
    {
        "Idle",
        "Jog_Fwd",
        "Jog_Bwd",
        "Jog_Left",
        "Jog_Right",
        "Sprint",
        "Jump_Start",
        "Jump",
    };

    [ExportGroup("Playback")]
    [Export]
    public float PlaybackSpeed { get; set; } = 1.5f;

    [Export]
    public bool LandingEnabled { get; set; } = true;

    [Export]
    public float LandingBlendOutDelay { get; set; } = 0.15f;

    [ExportGroup("Turn In Place")]
    [Export]
    public bool TurnInPlaceEnabled { get; set; } = true;

    [Export]
    public float TurnThresholdDegrees { get; set; } = 20.0f;

    [Export]
    public float TurnRetriggerThresholdDegrees { get; set; } = 25.0f;

    [Export]
    public float TurnSpeedThreshold { get; set; } = 0.5f;

    [Export]
    public float TurnMaxPlaybackSpeed { get; set; } = 2.5f;

    [Export]
    public float TurnSpeedRampDegrees { get; set; } = 90.0f;

    private Character _character = null!;
    private Node3D _visual = null!;
    private AnimationPlayer _animationPlayer = null!;
    private AnimationTree _animationTree = null!;
    private AnimationNodeStateMachinePlayback _playback = null!;
    private CharacterFrameState _frame;
    private float _turnYawAccumulator;
    private string _overrideState = string.Empty;
    private float _overrideRemaining;
    private float _landingBlendOutRemaining;
    private bool _landingBlendOutPending;
    private string _lastRequestedState = string.Empty;
    private bool _initialized;

    public bool Initialize(Character character, Node3D visual)
    {
        _character = character;
        _visual = visual;
        _animationPlayer = character.GetNodeOrNull<AnimationPlayer>(
            "Visual/UALCharacter/AnimationPlayer"
        )!;
        _animationTree = character.GetNodeOrNull<AnimationTree>("AnimationTree")!;
        if (_animationPlayer == null)
        {
            GD.PushError(
                $"{Name}: expected UAL AnimationPlayer at 'Visual/UALCharacter/AnimationPlayer'."
            );
            return false;
        }

        if (_animationTree == null)
        {
            GD.PushError($"{Name}: expected AnimationTree at 'AnimationTree'.");
            return false;
        }

        if (!ValidateAnimations())
        {
            return false;
        }

        _animationTree.Active = true;
        SetAnimationPlaybackSpeed(PlaybackSpeed);
        _playback = (AnimationNodeStateMachinePlayback)_animationTree.Get(PlaybackPath);
        if (_playback == null)
        {
            GD.PushError($"{Name}: AnimationTree is missing its state-machine playback parameter.");
            return false;
        }

        _initialized = true;
        RequestState(LocomotionState);
        return true;
    }

    public void ApplyFrame(
        CharacterFrameState frame,
        Character.ViewMode viewMode,
        float yawDelta,
        float delta
    )
    {
        if (!_initialized)
        {
            return;
        }

        _frame = frame;
        if (
            IsTurnInPlaceActive
            && (!TurnInPlaceEnabled || viewMode != Character.ViewMode.FirstPerson)
        )
        {
            CancelTurnInPlace();
        }

        if (viewMode == Character.ViewMode.FirstPerson)
        {
            _turnYawAccumulator += yawDelta;
        }

        UpdateBlendPosition(frame.Velocity);
        if (!frame.IsGrounded)
        {
            CancelTurnInPlace();
            SetAnimationPlaybackSpeed(PlaybackSpeed);
            RequestState(frame.Velocity.Y > 0.0f ? JumpState : FallState);
            AdvanceLanding(delta);
            return;
        }

        if (frame.Landed && LandingEnabled)
        {
            BeginLanding();
        }

        if (IsTurnInPlaceActive && frame.Input.Move.LengthSquared() > 0.0001f)
        {
            CancelTurnInPlace();
        }

        TryStartTurnInPlace(viewMode);
        UpdatePlaybackSpeed();
        RequestState(string.IsNullOrEmpty(_overrideState) ? LocomotionState : _overrideState);
        AdvanceOverride(delta);
        AdvanceLanding(delta);
    }

    public void OnViewModeChanged(Character.ViewMode mode)
    {
        _turnYawAccumulator = 0.0f;
        if (mode != Character.ViewMode.FirstPerson)
        {
            CancelTurnInPlace();
        }
    }

    public void CancelIncompatibleState()
    {
        _turnYawAccumulator = 0.0f;
        CancelTurnInPlace();
    }

    public bool IsTurnInPlaceActive =>
        _overrideState == TurnLeftState || _overrideState == TurnRightState;

    private bool ValidateAnimations()
    {
        bool valid = true;
        foreach (string animationName in BaseAnimations)
        {
            valid &= RequireAnimation(animationName);
        }

        if (LandingEnabled)
        {
            valid &= RequireAnimation("Jump_Land");
        }

        if (TurnInPlaceEnabled)
        {
            valid &= RequireAnimation("Turn90_L");
            valid &= RequireAnimation("Turn90_R");
        }

        return valid;
    }

    private bool RequireAnimation(string animationName)
    {
        if (_animationPlayer.HasAnimation(animationName))
        {
            return true;
        }

        GD.PushError(
            $"{Name}: UAL AnimationPlayer is missing required animation '{animationName}'."
        );
        return false;
    }

    private void UpdateBlendPosition(Vector3 velocity)
    {
        Vector3 horizontalVelocity = new(velocity.X, 0.0f, velocity.Z);
        Vector3 localVelocity = _visual.GlobalBasis.Inverse() * horizontalVelocity;
        float speedRadius = Mathf.Max(_character.RunSpeed, 0.001f);
        Vector2 blendPosition = new(localVelocity.X / speedRadius, -localVelocity.Z / speedRadius);
        _animationTree.Set(BlendPositionPath, blendPosition.LimitLength(1.0f));
    }

    private void TryStartTurnInPlace(Character.ViewMode viewMode)
    {
        if (!TurnInPlaceEnabled || viewMode != Character.ViewMode.FirstPerson || !_frame.IsGrounded)
        {
            _turnYawAccumulator = 0.0f;
            return;
        }

        if (!string.IsNullOrEmpty(_overrideState))
        {
            return;
        }

        float horizontalSpeed = new Vector2(_frame.Velocity.X, _frame.Velocity.Z).Length();
        if (
            _frame.Input.Move.LengthSquared() > 0.0001f
            || horizontalSpeed > Mathf.Max(TurnSpeedThreshold, 0.0f)
        )
        {
            _turnYawAccumulator = 0.0f;
            return;
        }

        float threshold = Mathf.DegToRad(Mathf.Max(TurnThresholdDegrees, 0.0f));
        if (Mathf.Abs(_turnYawAccumulator) < threshold)
        {
            return;
        }

        string state = _turnYawAccumulator > 0.0f ? TurnLeftState : TurnRightState;
        _turnYawAccumulator = 0.0f;
        BeginOverride(state);
    }

    private void BeginOverride(string state)
    {
        _overrideState = state;
        float speed = Mathf.Max(PlaybackSpeed, 0.01f);
        _overrideRemaining = Mathf.Max(GetAnimationLength(GetAnimationName(state)) / speed, 0.01f);
        RequestState(state, true);
    }

    private void AdvanceOverride(float delta)
    {
        if (string.IsNullOrEmpty(_overrideState))
        {
            return;
        }

        float baseSpeed = Mathf.Max(PlaybackSpeed, 0.01f);
        float currentSpeed = GetTurnPlaybackSpeed();
        _overrideRemaining -= delta * currentSpeed / baseSpeed;
        if (_overrideRemaining > 0.0f)
        {
            return;
        }

        if (TryGetQueuedTurnState(out string queuedState))
        {
            _turnYawAccumulator = 0.0f;
            BeginOverride(queuedState);
            return;
        }

        CancelTurnInPlace();
    }

    private bool TryGetQueuedTurnState(out string state)
    {
        state = string.Empty;
        if (!TurnInPlaceEnabled || !_frame.IsGrounded)
        {
            return false;
        }

        float horizontalSpeed = new Vector2(_frame.Velocity.X, _frame.Velocity.Z).Length();
        if (
            _frame.Input.Move.LengthSquared() > 0.0001f
            || horizontalSpeed > Mathf.Max(TurnSpeedThreshold, 0.0f)
        )
        {
            return false;
        }

        float threshold = Mathf.DegToRad(Mathf.Max(TurnRetriggerThresholdDegrees, 0.0f));
        if (Mathf.Abs(_turnYawAccumulator) < threshold)
        {
            return false;
        }

        state = _turnYawAccumulator > 0.0f ? TurnLeftState : TurnRightState;
        return true;
    }

    private void CancelTurnInPlace()
    {
        _overrideState = string.Empty;
        _overrideRemaining = 0.0f;
        _turnYawAccumulator = 0.0f;
        SetAnimationPlaybackSpeed(PlaybackSpeed);
    }

    private void BeginLanding()
    {
        _animationTree.Set(
            LandingOneShotRequestPath,
            (int)AnimationNodeOneShot.OneShotRequest.Fire
        );
        _landingBlendOutRemaining = Mathf.Max(LandingBlendOutDelay, 0.0f);
        _landingBlendOutPending = true;
    }

    private void AdvanceLanding(float delta)
    {
        if (!_landingBlendOutPending)
        {
            return;
        }

        _landingBlendOutRemaining -= delta;
        if (_landingBlendOutRemaining <= 0.0f)
        {
            _animationTree.Set(
                LandingOneShotRequestPath,
                (int)AnimationNodeOneShot.OneShotRequest.FadeOut
            );
            _landingBlendOutPending = false;
        }
    }

    private void UpdatePlaybackSpeed()
    {
        SetAnimationPlaybackSpeed(IsTurnInPlaceActive ? GetTurnPlaybackSpeed() : PlaybackSpeed);
    }

    private float GetTurnPlaybackSpeed()
    {
        float baseSpeed = Mathf.Max(PlaybackSpeed, 0.01f);
        float maxSpeed = Mathf.Max(TurnMaxPlaybackSpeed, baseSpeed);
        float rampDegrees = Mathf.Max(TurnSpeedRampDegrees, 0.01f);
        float pendingYawDegrees = Mathf.RadToDeg(Mathf.Abs(_turnYawAccumulator));
        return Mathf.Lerp(
            baseSpeed,
            maxSpeed,
            Mathf.Clamp(pendingYawDegrees / rampDegrees, 0.0f, 1.0f)
        );
    }

    private void SetAnimationPlaybackSpeed(float speed)
    {
        if (_animationTree != null)
        {
            _animationTree.Set(AnimationTimeScalePath, Mathf.Max(speed, 0.01f));
        }
    }

    private float GetAnimationLength(string animationName)
    {
        Animation animation = _animationPlayer.GetAnimation(animationName);
        return animation == null ? 0.0f : (float)animation.Length;
    }

    private static string GetAnimationName(string state)
    {
        return state switch
        {
            TurnLeftState => "Turn90_L",
            TurnRightState => "Turn90_R",
            _ => state,
        };
    }

    private void RequestState(string state, bool restart = false)
    {
        if (_lastRequestedState == state && !restart)
        {
            return;
        }

        if (_lastRequestedState == state && restart)
        {
            _playback.Start(state, true);
        }
        else
        {
            _playback.Travel(state);
        }

        _lastRequestedState = state;
    }
}
