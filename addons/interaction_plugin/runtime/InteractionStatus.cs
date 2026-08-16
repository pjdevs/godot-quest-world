using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction;

public sealed record InteractionAllowed();

public sealed record InteractionBlocked(string Reason = "Interaction unavailable.");

public readonly union InteractionStatus(InteractionAllowed, InteractionBlocked);

public enum InteractionState
{
    Idle,
    Activating,
    Activated,
    Deactivating
}

public readonly record struct InteractionContext(
    InteractionInteractor Interactor,
    InteractiveComponent Interactive,
    Node InteractionOwner
);

public readonly record struct InteractionPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    StringName ActionName,
    InteractionStatus Status,
    bool IsFocused
)
{
    public bool IsAllowed => Status switch
    {
        InteractionAllowed => true,
        InteractionBlocked => false,
    };

    public string BlockReason => Status switch
    {
        InteractionAllowed => string.Empty,
        InteractionBlocked blocked => blocked.Reason,
    };
}

public readonly record struct InteractionSavedState(int Version, InteractionState State);
