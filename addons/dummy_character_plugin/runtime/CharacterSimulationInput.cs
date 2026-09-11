using Godot;

namespace DummyCharacterPlugin;

/// <summary>
/// Camera-independent input consumed by the character motor for one physics tick.
/// View angles are absolute character-local angles so the same input can be replayed remotely.
/// </summary>
public readonly record struct CharacterSimulationInput(
    Vector2 Move,
    float ViewYaw,
    float ViewPitch,
    bool JumpPressed,
    bool SprintHeld
)
{
    public static CharacterSimulationInput Empty => default;
}
