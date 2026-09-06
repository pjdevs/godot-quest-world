using Godot;

namespace QuestWorld.GameplayActions;

/// <summary>Read-only snapshot of one gameplay action offered through an input binding.</summary>
/// <param name="ActionId">Stable gameplay and network identity of the action.</param>
/// <param name="Label">Player-facing label of the action.</param>
/// <param name="Description">Optional player-facing description of the action.</param>
/// <param name="InputActionName">Project input action requesting this action.</param>
/// <param name="Availability">Current availability of this action.</param>
/// <param name="ActivationMode">Input gesture used to select this action.</param>
/// <param name="HoldProgress">Progress towards this binding's hold threshold, or null.</param>
/// <param name="HoldElapsed">Seconds this binding has been held, or null.</param>
public readonly record struct GameplayActionPresentation(
    StringName ActionId,
    string Label,
    string Description,
    StringName InputActionName,
    GameplayActionAvailability Availability,
    GameplayActionActivationMode ActivationMode,
    float? HoldProgress = null,
    float? HoldElapsed = null
)
{
    /// <summary>Gets whether this action can currently be requested.</summary>
    public bool IsAllowed => Availability is GameplayActionAllowed;

    /// <summary>Gets whether this action is requested without player input.</summary>
    public bool IsAutomatic => ActivationMode == GameplayActionActivationMode.Automatic;

    /// <summary>Gets whether this action is selected by holding its input.</summary>
    public bool IsHoldable => ActivationMode == GameplayActionActivationMode.Hold;

    /// <summary>Gets the blocked reason, or an empty string when the action is not blocked.</summary>
    public string BlockReason =>
        Availability is GameplayActionBlocked blocked ? blocked.Reason : string.Empty;
}
