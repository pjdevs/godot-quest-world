using Godot;

namespace QuestWorld.Character;

/// <summary>
/// Immutable result of one character physics tick, shared by presentation systems.
/// </summary>
public readonly struct CharacterFrameState
{
    public CharacterFrameState(
        ulong frameNumber,
        CharacterSimulationInput input,
        Vector3 moveDirection,
        Vector3 velocity,
        bool wasGrounded,
        bool isGrounded,
        bool jumped,
        bool landed,
        bool isSprinting,
        float impactSpeed,
        float landingStrength)
    {
        FrameNumber = frameNumber;
        Input = input;
        MoveDirection = moveDirection;
        Velocity = velocity;
        WasGrounded = wasGrounded;
        IsGrounded = isGrounded;
        Jumped = jumped;
        Landed = landed;
        IsSprinting = isSprinting;
        ImpactSpeed = impactSpeed;
        LandingStrength = landingStrength;
    }

    public ulong FrameNumber { get; }

    public CharacterSimulationInput Input { get; }

    public Vector3 MoveDirection { get; }

    public Vector3 Velocity { get; }

    public bool WasGrounded { get; }

    public bool IsGrounded { get; }

    public bool Jumped { get; }

    public bool Landed { get; }

    public bool IsSprinting { get; }

    public float ImpactSpeed { get; }

    public float LandingStrength { get; }
}
