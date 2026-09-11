using Godot;

namespace DummyCharacterPlugin;

/// <summary>
/// Immutable input sampled once for a character physics tick.
/// </summary>
public readonly record struct CharacterInputFrame(
    Vector2 Move,
    Vector2 LookDelta,
    bool JumpPressed,
    bool SprintHeld
)
{
    public static CharacterInputFrame Empty => default;
}
