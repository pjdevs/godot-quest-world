using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction;

/// <summary>Indicates that an action may be requested by the interactor.</summary>
public sealed record InteractionAllowed();

/// <summary>Indicates that an action is presentable but cannot be requested, and why.</summary>
/// <param name="Reason">Reason displayed by interaction presentation widgets.</param>
public sealed record InteractionBlocked(string Reason = "Interaction unavailable.");

/// <summary>Indicates that an action is not part of the choices currently offered.</summary>
/// <remarks>
/// A hidden action carries no reason: it is absent from presentation instead of being explained,
/// for example <c>Close</c> while a door is already closed.
/// </remarks>
public sealed record InteractionHidden();

/// <summary>Availability of one action, returned by rules and by interactive evaluation.</summary>
public readonly union InteractionAvailability(
    InteractionAllowed,
    InteractionBlocked,
    InteractionHidden
);

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
/// <param name="Interactor">Interactor requesting the availability evaluation.</param>
/// <param name="Interactive">Interactive component owning the evaluated action.</param>
/// <param name="Action">Action being evaluated, including for target-level rules.</param>
public readonly record struct InteractionContext(
    InteractionInteractor Interactor,
    InteractiveComponent Interactive,
    InteractionAction Action
);

/// <summary>Snapshot consumed by local prompt and indication presentation.</summary>
/// <param name="Interactive">Interactive component represented by the snapshot.</param>
/// <param name="DisplayName">Name shown to the player.</param>
/// <param name="Description">Optional descriptive text supplied by the interactive.</param>
/// <param name="ActionName">Input action displayed by prompt widgets.</param>
/// <param name="Availability">Current availability aggregated over the target actions.</param>
/// <param name="IsFocused">Whether this interactive is the current focus target.</param>
public readonly record struct InteractionPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    StringName ActionName,
    InteractionAvailability Availability,
    bool IsFocused
)
{
    /// <summary>Gets whether the current availability allows the interaction to start.</summary>
    public bool IsAllowed =>
        Availability switch
        {
            InteractionAllowed => true,
            InteractionBlocked => false,
            InteractionHidden => false,
        };

    /// <summary>Gets the blocked reason, or an empty string when allowed or hidden.</summary>
    public string BlockReason =>
        Availability switch
        {
            InteractionAllowed => string.Empty,
            InteractionBlocked blocked => blocked.Reason,
            InteractionHidden => string.Empty,
        };
}

/// <summary>Versioned state snapshot used by an external persistence system.</summary>
/// <param name="Version">Serialization contract version.</param>
/// <param name="State">State captured by the snapshot.</param>
public readonly record struct InteractionSavedState(int Version, InteractionState State);
