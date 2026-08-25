using System.Collections.Generic;
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

/// <summary>Snapshot of one action currently offered by a target.</summary>
/// <remarks>
/// One entry exists per presentable action. Availability is carried per action and is never
/// summarized across the target: a prompt shows each action with its own allowed or blocked state.
/// </remarks>
/// <param name="ActionId">Stable gameplay and network identity of the action.</param>
/// <param name="Label">Player-facing label of the action.</param>
/// <param name="Description">Optional player-facing description of the action.</param>
/// <param name="InputActionName">Project input action requesting this action.</param>
/// <param name="Availability">Availability of this action, either allowed or blocked.</param>
/// <param name="IsAutomatic">Whether local focus requests this action without any player input.</param>
public readonly record struct InteractionActionPresentation(
    StringName ActionId,
    string Label,
    string Description,
    StringName InputActionName,
    InteractionAvailability Availability,
    bool IsAutomatic = false
)
{
    /// <summary>Gets whether this action can currently be requested.</summary>
    public bool IsAllowed =>
        Availability switch
        {
            InteractionAllowed => true,
            InteractionBlocked => false,
            InteractionHidden => false,
        };

    /// <summary>Gets the blocked reason of this action, or an empty string when allowed.</summary>
    public string BlockReason =>
        Availability switch
        {
            InteractionAllowed => string.Empty,
            InteractionBlocked blocked => blocked.Reason,
            InteractionHidden => string.Empty,
        };
}

/// <summary>Snapshot consumed by local prompt and indication presentation.</summary>
/// <remarks>
/// Hidden actions are absent from <paramref name="Actions"/>; blocked ones stay present so a prompt
/// can explain them. A target offering no presentable action is neither focused nor indicated.
/// </remarks>
/// <param name="Interactive">Interactive component represented by the snapshot.</param>
/// <param name="DisplayName">Name of the target shown to the player.</param>
/// <param name="Description">Optional descriptive text supplied by the interactive.</param>
/// <param name="Actions">Presentable actions, in target declaration order.</param>
/// <param name="IsFocused">Whether this interactive is the current focus target.</param>
public readonly record struct InteractionTargetPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    IReadOnlyList<InteractionActionPresentation> Actions,
    bool IsFocused
)
{
    /// <summary>Gets whether at least one presented action can currently be requested.</summary>
    /// <remarks>
    /// Reserved for the target-level indication, which is a single visual for the whole object.
    /// Prompts must read the availability of each action instead of this aggregate.
    /// </remarks>
    public bool HasAllowedAction
    {
        get
        {
            if (Actions is null)
            {
                return false;
            }

            foreach (InteractionActionPresentation action in Actions)
            {
                if (action.Availability is InteractionAllowed)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets whether at least one presented action is requested by player input.</summary>
    /// <remarks>
    /// Automatic actions stay in <see cref="Actions"/> so focus and indication keep seeing them, but
    /// a prompt showing an input the player cannot press would be misleading.
    /// </remarks>
    public bool HasPromptableAction
    {
        get
        {
            if (Actions is null)
            {
                return false;
            }

            foreach (InteractionActionPresentation action in Actions)
            {
                if (!action.IsAutomatic)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>Versioned state snapshot used by an external persistence system.</summary>
/// <param name="Version">Serialization contract version.</param>
/// <param name="State">State captured by the snapshot.</param>
public readonly record struct InteractionSavedState(int Version, InteractionState State);
