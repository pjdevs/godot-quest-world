using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction;

/// <summary>Indicates that an interaction may start.</summary>
public sealed record InteractionAllowed();

/// <summary>Indicates that an interaction cannot start and provides a user-facing reason.</summary>
/// <param name="Reason">Reason displayed by interaction presentation widgets.</param>
public sealed record InteractionBlocked(string Reason = "Interaction unavailable.");

/// <summary>Result returned by interaction status checks and rules.</summary>
public readonly union InteractionStatus(InteractionAllowed, InteractionBlocked);

/// <summary>Persistent lifecycle state of a stateful interaction.</summary>
public enum InteractionState
{
    /// <summary>The object is available for interaction.</summary>
    Idle,

    /// <summary>An activation phase is currently running.</summary>
    Activating,

    /// <summary>The object has completed activation and is no longer available.</summary>
    Activated,

    /// <summary>A deactivation phase is currently running.</summary>
    Deactivating
}

/// <summary>Read-only inputs supplied to every gameplay interaction rule.</summary>
/// <param name="Interactor">Interactor requesting the status evaluation.</param>
/// <param name="Interactive">Interactive component being evaluated.</param>
/// <param name="InteractionOwner">Gameplay node configured as the interaction owner.</param>
public readonly record struct InteractionContext(
    InteractionInteractor Interactor,
    InteractiveComponent Interactive,
    Node InteractionOwner
);

/// <summary>Snapshot consumed by local prompt and indication presentation.</summary>
/// <param name="Interactive">Interactive component represented by the snapshot.</param>
/// <param name="DisplayName">Name shown to the player.</param>
/// <param name="Description">Optional descriptive text supplied by the interactive.</param>
/// <param name="ActionName">Input action displayed by prompt widgets.</param>
/// <param name="Status">Current allowed or blocked status.</param>
/// <param name="IsFocused">Whether this interactive is the current focus target.</param>
public readonly record struct InteractionPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    StringName ActionName,
    InteractionStatus Status,
    bool IsFocused
)
{
    /// <summary>Gets whether the current status allows the interaction to start.</summary>
    public bool IsAllowed => Status switch
    {
        InteractionAllowed => true,
        InteractionBlocked => false,
    };

    /// <summary>Gets the blocked reason, or an empty string when interaction is allowed.</summary>
    public string BlockReason => Status switch
    {
        InteractionAllowed => string.Empty,
        InteractionBlocked blocked => blocked.Reason,
    };
}

/// <summary>Versioned state snapshot used by an external persistence system.</summary>
/// <param name="Version">Serialization contract version.</param>
/// <param name="State">State captured by the snapshot.</param>
public readonly record struct InteractionSavedState(int Version, InteractionState State);
