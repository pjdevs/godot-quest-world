using Godot;

/// <summary>
/// Immutable input sampled once for a character physics tick.
/// </summary>
public readonly struct CharacterInputFrame
{
    public static CharacterInputFrame Empty => default;

    public CharacterInputFrame(Vector2 move, Vector2 lookDelta, bool jumpPressed, bool sprintHeld)
    {
        Move = move;
        LookDelta = lookDelta;
        JumpPressed = jumpPressed;
        SprintHeld = sprintHeld;
    }

    public Vector2 Move { get; }

    public Vector2 LookDelta { get; }

    public bool JumpPressed { get; }

    public bool SprintHeld { get; }
}
