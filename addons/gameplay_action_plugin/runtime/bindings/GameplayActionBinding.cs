using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions.Runtime.Bindings;

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
