using Godot;

namespace QuestWorld.Character;

public partial class CharacterCameraEffects : Node3D
{
    [ExportGroup("Camera Effects")]
    [Export]
    public bool EffectsEnabled { get; set; } = true;

    [Export]
    public bool HeadBobEnabled { get; set; } = true;

    [Export]
    public float HeadBobWalkAmplitude { get; set; } = 0.025f;

    [Export]
    public float HeadBobSprintAmplitude { get; set; } = 0.045f;

    [Export]
    public float HeadBobFrequency { get; set; } = 8.0f;

    [Export]
    public float ThirdPersonScale { get; set; } = 0.35f;

    [Export]
    public float SwayStrengthDegrees { get; set; } = 1.0f;

    [Export]
    public float SwaySmoothSpeed { get; set; } = 10.0f;

    [Export]
    public float DefaultFov { get; set; } = 75.0f;

    [Export]
    public float SprintFov { get; set; } = 82.0f;

    [Export]
    public float FovTransitionSpeed { get; set; } = 8.0f;

    [Export]
    public float JumpOffset { get; set; } = 0.025f;

    [Export]
    public float LandingOffset { get; set; } = 0.05f;

    [Export]
    public float JumpPitchDegrees { get; set; } = 1.0f;

    [Export]
    public float LandingPitchDegrees { get; set; } = -1.25f;

    [Export]
    public float ImpulseResponseSpeed { get; set; } = 14.0f;

    [Export]
    public float ImpulseRecoverySpeed { get; set; } = 7.0f;

    private Character _character = null!;
    private Camera3D _camera = null!;
    private CharacterFrameState _frame;
    private Vector3 _headBobOffset;
    private Vector3 _impulseOffset;
    private Vector3 _impulseTargetOffset;
    private float _headBobTime;
    private float _impulsePitch;
    private float _impulseTargetPitch;
    private float _swayRoll;
    private float _pendingLookX;
    private ulong _lastLookFrame;
    private bool _initialized;

    public void Initialize(Character character)
    {
        _character = character;
        _camera = GetNodeOrNull<Camera3D>("Camera3D")!;
        if (_camera == null)
        {
            GD.PushError($"{Name}: camera effects are missing child 'Camera3D'.");
            return;
        }

        _camera.Fov = DefaultFov;
        _initialized = true;
    }

    public void PushFrame(CharacterFrameState frame, Vector2 lookDelta)
    {
        _frame = frame;
        if (frame.FrameNumber != _lastLookFrame)
        {
            _pendingLookX += lookDelta.X;
            _lastLookFrame = frame.FrameNumber;
        }

        if (frame.Jumped)
        {
            TriggerImpulse(JumpOffset, JumpPitchDegrees);
        }
        else if (frame.Landed)
        {
            TriggerImpulse(
                -LandingOffset * frame.LandingStrength,
                LandingPitchDegrees * frame.LandingStrength);
        }
    }

    public void ResetPose()
    {
        _headBobOffset = Vector3.Zero;
        _impulseOffset = Vector3.Zero;
        _impulseTargetOffset = Vector3.Zero;
        _impulsePitch = 0.0f;
        _impulseTargetPitch = 0.0f;
        _swayRoll = 0.0f;
        _pendingLookX = 0.0f;
        Position = Vector3.Zero;
        Rotation = Vector3.Zero;
        if (_camera != null)
        {
            _camera.Fov = DefaultFov;
        }
    }

    public override void _Process(double delta)
    {
        if (!_initialized)
        {
            return;
        }

        float frameDelta = (float)delta;
        if (!EffectsEnabled)
        {
            ResetPose();
            return;
        }

        float effectScale = _character.CurrentViewMode == Character.ViewMode.FirstPerson
            ? 1.0f
            : ThirdPersonScale;
        float horizontalSpeed = new Vector2(_frame.Velocity.X, _frame.Velocity.Z).Length();
        float speedReference = Mathf.Max(_frame.IsSprinting ? _character.RunSpeed : _character.WalkSpeed, 0.001f);
        float speedFactor = Mathf.Clamp(horizontalSpeed / speedReference, 0.0f, 1.0f);
        float smoothing = 1.0f - Mathf.Exp(-12.0f * frameDelta);

        Vector3 targetHeadBob = Vector3.Zero;
        if (HeadBobEnabled
            && _frame.IsGrounded
            && _frame.Input.Move.LengthSquared() > 0.0001f
            && horizontalSpeed > 0.1f)
        {
            float amplitude = (_frame.IsSprinting ? HeadBobSprintAmplitude : HeadBobWalkAmplitude)
                * effectScale
                * speedFactor;
            float frequency = HeadBobFrequency * (_frame.IsSprinting ? 1.15f : 1.0f);
            _headBobTime += frameDelta * frequency * Mathf.Lerp(0.65f, 1.0f, speedFactor);
            targetHeadBob = new Vector3(
                Mathf.Sin(_headBobTime * 0.5f) * amplitude * 0.5f,
                Mathf.Sin(_headBobTime) * amplitude,
                0.0f);
        }

        _headBobOffset = _headBobOffset.Lerp(targetHeadBob, smoothing);
        float swayLimit = Mathf.DegToRad(Mathf.Max(SwayStrengthDegrees, 0.0f)) * effectScale;
        float targetSway = Mathf.Clamp(
            -_pendingLookX * _character.CameraRig.MouseSensitivity,
            -swayLimit,
            swayLimit);
        float swayWeight = 1.0f - Mathf.Exp(-Mathf.Max(SwaySmoothSpeed, 0.0f) * frameDelta);
        _swayRoll = Mathf.Lerp(_swayRoll, targetSway, swayWeight);
        _pendingLookX = 0.0f;

        float responseWeight = 1.0f - Mathf.Exp(-Mathf.Max(ImpulseResponseSpeed, 0.0f) * frameDelta);
        _impulseOffset = _impulseOffset.Lerp(_impulseTargetOffset, responseWeight);
        _impulsePitch = Mathf.Lerp(_impulsePitch, _impulseTargetPitch, responseWeight);
        float recoveryWeight = 1.0f - Mathf.Exp(-Mathf.Max(ImpulseRecoverySpeed, 0.0f) * frameDelta);
        _impulseTargetOffset = _impulseTargetOffset.Lerp(Vector3.Zero, recoveryWeight);
        _impulseTargetPitch = Mathf.Lerp(_impulseTargetPitch, 0.0f, recoveryWeight);

        Position = _headBobOffset + _impulseOffset;
        Rotation = new Vector3(_impulsePitch, 0.0f, _swayRoll);

        float targetFov = _frame.IsSprinting ? SprintFov : DefaultFov;
        float fovWeight = 1.0f - Mathf.Exp(-Mathf.Max(FovTransitionSpeed, 0.0f) * frameDelta);
        _camera.Fov = Mathf.Lerp(_camera.Fov, targetFov, fovWeight);
    }

    private void TriggerImpulse(float verticalOffset, float pitchDegrees)
    {
        _impulseTargetOffset.Y += verticalOffset;
        _impulseTargetPitch += Mathf.DegToRad(pitchDegrees);
    }
}
