using Godot;

namespace QuestWorld.Interaction;

public sealed record InteractionAllowed
{
    public static InteractionAllowed Instance { get; } = new();

    private InteractionAllowed()
    {
    }
}

public sealed record InteractionBlocked(string Reason)
{
    public string Reason { get; } = string.IsNullOrWhiteSpace(Reason) ? "Interaction unavailable." : Reason;
}

public union InteractionStatus(InteractionAllowed, InteractionBlocked);

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
    Node InteractionOwner);

public readonly record struct InteractionPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    StringName ActionName,
    InteractionStatus Status,
    bool IsFocused)
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
