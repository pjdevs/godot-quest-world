using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions.Runtime.Bindings;

/// <summary>Local runtime binding referencing an action still owned by its original component.</summary>
/// <param name="Id">Runner-local stable binding identifier.</param>
/// <param name="Component">Component owning the referenced action.</param>
/// <param name="ActionId">Stable action identity resolved through the component.</param>
/// <param name="Source">Object whose lifecycle/invalidation owns this binding.</param>
/// <param name="InputActionName">Input Map action used by the binding.</param>
/// <param name="ActivationMode">Gesture that selects the binding.</param>
/// <param name="HoldDuration">Local hold threshold used by hold selection.</param>
/// <param name="InputRequirement">Input state required while sustaining the request.</param>
/// <param name="Priority">Authored arbitration priority.</param>
/// <param name="PresentationContext">Opaque integration-specific local presentation data.</param>
public sealed record GameplayActionBinding(
    ulong Id,
    GameplayActionComponent Component,
    StringName ActionId,
    GodotObject Source,
    StringName InputActionName,
    GameplayActionActivationMode ActivationMode,
    float HoldDuration,
    GameplayActionInputRequirement InputRequirement,
    int Priority,
    Variant PresentationContext
);
