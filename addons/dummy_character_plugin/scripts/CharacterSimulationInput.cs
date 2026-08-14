using Godot;

/// <summary>
/// Camera-independent input consumed by the character motor for one physics tick.
/// View angles are absolute character-local angles so the same input can be replayed remotely.
/// </summary>
public readonly struct CharacterSimulationInput
{
    public static CharacterSimulationInput Empty => default;

    public CharacterSimulationInput(
        Vector2 move,
        float viewYaw,
        float viewPitch,
        bool jumpPressed,
        bool sprintHeld)
    {
        Move = move;
        ViewYaw = viewYaw;
        ViewPitch = viewPitch;
        JumpPressed = jumpPressed;
        SprintHeld = sprintHeld;
    }

    public Vector2 Move { get; }

    public float ViewYaw { get; }

    public float ViewPitch { get; }

    public bool JumpPressed { get; }

    public bool SprintHeld { get; }
}
