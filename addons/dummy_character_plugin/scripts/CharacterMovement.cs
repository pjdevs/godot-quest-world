using Godot;

namespace QuestWorld.Character;

/// <summary>
/// Serialized movement values owned by the Character composition root.
/// This is a plain value snapshot, not a Godot Resource.
/// </summary>
public readonly struct CharacterMovementSettings
{
    public CharacterMovementSettings(
        float walkSpeed,
        float runSpeed,
        float acceleration,
        float airAcceleration,
        float jumpVelocity,
        float sprintForwardInputThreshold,
        float minimumLandingAirTime,
        float minimumLandingImpactSpeed,
        float fullLandingImpactSpeed,
        float minimumLandingStrength)
    {
        WalkSpeed = walkSpeed;
        RunSpeed = runSpeed;
        Acceleration = acceleration;
        AirAcceleration = airAcceleration;
        JumpVelocity = jumpVelocity;
        SprintForwardInputThreshold = sprintForwardInputThreshold;
        MinimumLandingAirTime = minimumLandingAirTime;
        MinimumLandingImpactSpeed = minimumLandingImpactSpeed;
        FullLandingImpactSpeed = fullLandingImpactSpeed;
        MinimumLandingStrength = minimumLandingStrength;
    }

    public float WalkSpeed { get; }

    public float RunSpeed { get; }

    public float Acceleration { get; }

    public float AirAcceleration { get; }

    public float JumpVelocity { get; }

    public float SprintForwardInputThreshold { get; }

    public float MinimumLandingAirTime { get; }

    public float MinimumLandingImpactSpeed { get; }

    public float FullLandingImpactSpeed { get; }

    public float MinimumLandingStrength { get; }
}

/// <summary>
/// Camera-independent character motor. It owns simulation state and talks to
/// the CharacterBody3D supplied by the composition root for physics movement.
/// </summary>
public class CharacterMovement
{
    private readonly CharacterBody3D _body;
    private float _airborneDuration;
    private bool _hasFloorSample;
    private bool _wasGrounded;
    private ulong _frameNumber;

    public CharacterMovement(CharacterBody3D body)
    {
        _body = body;
    }

    public CharacterFrameState LatestFrame { get; private set; }

    public CharacterFrameState Simulate(
        CharacterSimulationInput input,
        double delta,
        CharacterMovementSettings settings)
    {
        float frameDelta = (float)delta;
        bool groundedBeforeMove = _body.IsOnFloor();
        bool wasGrounded = _hasFloorSample && _wasGrounded;
        Vector3 moveDirection = GetViewRelativeDirection(input.Move, input.ViewYaw);
        bool sprintRequested = groundedBeforeMove
            && input.SprintHeld
            && -input.Move.Y >= Mathf.Clamp(settings.SprintForwardInputThreshold, 0.0f, 1.0f);
        float targetSpeed = sprintRequested ? settings.RunSpeed : settings.WalkSpeed;
        Vector3 targetVelocity = moveDirection * targetSpeed;
        float acceleration = groundedBeforeMove ? settings.Acceleration : settings.AirAcceleration;
        bool jumped = false;

        Vector3 velocity = _body.Velocity;
        velocity.X = Mathf.MoveToward(velocity.X, targetVelocity.X, acceleration * frameDelta);
        velocity.Z = Mathf.MoveToward(velocity.Z, targetVelocity.Z, acceleration * frameDelta);
        if (groundedBeforeMove)
        {
            if (input.JumpPressed)
            {
                velocity.Y = settings.JumpVelocity;
                jumped = true;
            }
            else if (velocity.Y < 0.0f)
            {
                velocity.Y = -0.1f;
            }
        }
        else
        {
            velocity += _body.GetGravity() * frameDelta;
        }

        float impactSpeed = Mathf.Max(-velocity.Y, 0.0f);
        _body.Velocity = velocity;
        _body.MoveAndSlide();

        bool isGrounded = _body.IsOnFloor();
        bool sprinting = isGrounded && sprintRequested;
        float sampledAirTime = _airborneDuration;
        if (!groundedBeforeMove || !isGrounded)
        {
            sampledAirTime += frameDelta;
        }

        bool landed = _hasFloorSample
            && !_wasGrounded
            && isGrounded
            && sampledAirTime >= Mathf.Max(settings.MinimumLandingAirTime, 0.0f)
            && impactSpeed >= Mathf.Max(settings.MinimumLandingImpactSpeed, 0.0f);
        float landingStrength = landed
            ? CalculateLandingStrength(impactSpeed, settings)
            : 0.0f;
        _airborneDuration = isGrounded ? 0.0f : sampledAirTime;
        _wasGrounded = isGrounded;
        _hasFloorSample = true;

        _frameNumber++;
        LatestFrame = new CharacterFrameState(
            _frameNumber,
            input,
            moveDirection,
            _body.GetRealVelocity(),
            wasGrounded,
            isGrounded,
            jumped,
            landed,
            sprinting,
            impactSpeed,
            landingStrength);
        return LatestFrame;
    }

    private Vector3 GetViewRelativeDirection(Vector2 input, float viewYaw)
    {
        Basis viewBasis = new(Vector3.Up, viewYaw);
        Basis viewGlobalBasis = _body.GlobalBasis * viewBasis;
        Vector3 forward = -viewGlobalBasis.Z;
        forward.Y = 0.0f;
        forward = forward.Normalized();
        Vector3 right = viewGlobalBasis.X;
        right.Y = 0.0f;
        right = right.Normalized();
        Vector3 direction = right * input.X + forward * -input.Y;
        return direction.LengthSquared() > 1.0f ? direction.Normalized() : direction;
    }

    private float CalculateLandingStrength(float impactSpeed, CharacterMovementSettings settings)
    {
        float minimumImpact = Mathf.Max(settings.MinimumLandingImpactSpeed, 0.0f);
        float fullImpact = Mathf.Max(settings.FullLandingImpactSpeed, minimumImpact + 0.001f);
        float normalizedImpact = Mathf.Clamp(
            (impactSpeed - minimumImpact) / (fullImpact - minimumImpact),
            0.0f,
            1.0f);
        return Mathf.Lerp(
            Mathf.Clamp(settings.MinimumLandingStrength, 0.0f, 1.0f),
            1.0f,
            normalizedImpact);
    }
}
