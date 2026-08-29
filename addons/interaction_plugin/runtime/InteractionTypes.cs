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

/// <summary>Turns an availability into the text reported to a refused interactor.</summary>
public static class InteractionAvailabilityExtensions
{
    /// <summary>Reason used when a refusal must not reveal why an action is unavailable.</summary>
    public const string UnavailableReason = "Interaction unavailable.";

    /// <summary>Describes why an action was refused, without disclosing a hidden action.</summary>
    /// <remarks>
    /// Hidden and blocked deliberately share one wording on the authoritative side: an action absent
    /// from the offered choices must not become discoverable through the refusal it produces.
    /// </remarks>
    /// <param name="availability">Availability that stopped the request.</param>
    /// <returns>The blocked reason, the neutral wording, or an empty string when allowed.</returns>
    public static string DescribeRefusal(this InteractionAvailability availability) =>
        availability switch
        {
            InteractionAllowed => string.Empty,
            InteractionBlocked blocked => blocked.Reason,
            InteractionHidden => UnavailableReason,
        };
}

/// <summary>Availability an authoring-time choice may select when a rule condition does not hold.</summary>
/// <remarks>
/// <see cref="InteractionAvailability"/> is a union and cannot be exported, so a rule exposing its
/// own refusal to the Inspector declares this enum instead. Only the two unavailable cases exist:
/// a rule that would select <c>Allowed</c> carries no condition at all.
/// </remarks>
public enum InteractionUnavailableKind
{
    /// <summary>The action leaves the offered choices, without any reason to display.</summary>
    Hidden,

    /// <summary>The action stays presentable and explains why it cannot run.</summary>
    Blocked
}

/// <summary>Turns an authored unavailable kind into the availability returned by a rule.</summary>
public static class InteractionUnavailableKindExtensions
{
    /// <summary>Builds the availability selected by an authored refusal.</summary>
    /// <param name="kind">Unavailable case chosen in the Inspector.</param>
    /// <param name="reason">Reason carried by a blocked result, ignored when hidden.</param>
    /// <returns>A hidden or blocked availability.</returns>
    public static InteractionAvailability ToAvailability(
        this InteractionUnavailableKind kind,
        string reason
    ) =>
        kind switch
        {
            InteractionUnavailableKind.Blocked => new InteractionBlocked(reason),
            _ => new InteractionHidden(),
        };
}

/// <summary>Tier one target reaches for one interactor, decided by its detector.</summary>
/// <remarks>
/// The tiers are cumulative: an interactible target is also indicated, because a widget saying "there
/// is something over there" must not disappear the moment the target becomes usable. A new tier is
/// only worth adding when the interactor itself behaves differently, which is why "close / medium /
/// far" is not one: that is visual, it belongs to the widget, and it is fed by data.
/// </remarks>
public enum InteractionDetectionKind
{
    /// <summary>The target is not detected at all and takes no part in presentation.</summary>
    None,

    /// <summary>The target is worth pointing at, but no action of it may be requested.</summary>
    Indicated,

    /// <summary>The target is eligible for focus, for a command, and for continued validation.</summary>
    Interactible
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

/// <summary>Snapshot of one execution visible on an interactive target.</summary>
/// <remarks>
/// The execution model is separate from <see cref="InteractionActionPresentation"/>: an action says
/// what may be requested, while this record says what is currently observable on the target. A null
/// progress means that the active execution has no generic presentable progress.
/// </remarks>
/// <param name="ExecutionId">Opaque identifier of the active execution.</param>
/// <param name="ActionId">Stable identifier of the action owning the execution.</param>
/// <param name="Progress">Optional normalized progress between zero and one.</param>
public readonly record struct InteractionExecutionPresentation(
    ulong ExecutionId,
    StringName ActionId,
    float? Progress = null
);

/// <summary>Linear progress sample transported between the authority and a presentation owner.</summary>
/// <remarks>
/// The sample is presentation state only. A positive rate lets a receiving peer extrapolate locally;
/// a zero rate represents a discrete published value. Revisions are monotonic for one execution so
/// ACKs and later corrections cannot rewind a slot through a different transport path.
/// </remarks>
/// <param name="ProgressBase">Normalised value at the moment the sample was received.</param>
/// <param name="ProgressPerSecond">Local extrapolation rate, or zero for a discrete value.</param>
/// <param name="Revision">Monotonic sample revision for this execution.</param>
internal readonly record struct InteractionProgressSample(
    float ProgressBase,
    float ProgressPerSecond,
    long Revision
)
{
    /// <summary>Preserves the value already rendered while adopting this sample's remaining time.</summary>
    public InteractionProgressSample PreserveVisibleProgress(float visibleProgress)
    {
        if (ProgressPerSecond <= 0.0f)
        {
            return this;
        }

        float authoritativeBase = Mathf.Clamp(ProgressBase, 0.0f, 1.0f);
        float preservedBase = Mathf.Max(
            Mathf.Clamp(visibleProgress, 0.0f, 1.0f),
            authoritativeBase
        );
        float remainingSeconds = (1.0f - authoritativeBase) / ProgressPerSecond;
        float preservedRate = remainingSeconds > 0.0f
            ? (1.0f - preservedBase) / remainingSeconds
            : 0.0f;
        return new InteractionProgressSample(preservedBase, preservedRate, Revision);
    }
}

/// <summary>Indicates that the executor finished the action synchronously.</summary>
/// <remarks>The reservation held during the call is released immediately.</remarks>
public sealed record InteractionExecutionCompleted();

/// <summary>Indicates that the executor started an action that finishes later.</summary>
/// <remarks>
/// The target keeps the execution reserved until gameplay calls
/// <c>InteractiveComponent.CompleteExecution</c>, <c>InteractiveComponent.CancelExecution</c>, or
/// <c>InteractiveComponent.FailExecution</c> with the identifier carried by
/// <see cref="InteractionExecutionContext.ExecutionId"/>.
/// </remarks>
public sealed record InteractionExecutionRunning();

/// <summary>Indicates that the executor refused the action at the execution boundary.</summary>
/// <param name="Reason">Reason reported to the requesting interactor.</param>
/// <remarks>
/// This case must stay rare: an ordinary gameplay condition belongs to a rule, where it is also
/// visible to presentation, instead of being discovered once the command is already authoritative.
/// </remarks>
public sealed record InteractionExecutionRejected(string Reason = "Interaction unavailable.");

/// <summary>Indicates that the action was accepted and then failed.</summary>
/// <param name="Reason">Reason reported to the requesting interactor.</param>
/// <remarks>
/// A failure is a gameplay or technical error discovered after acceptance, never a plain
/// "not allowed": the action did start, so it is notified as started and then failed.
/// </remarks>
public sealed record InteractionExecutionFailed(string Reason = "The interaction failed.");

/// <summary>Outcome returned by the single executor owning the gameplay mutation of an action.</summary>
public readonly union InteractionExecutionResult(
    InteractionExecutionCompleted,
    InteractionExecutionRunning,
    InteractionExecutionRejected,
    InteractionExecutionFailed
);

/// <summary>Read-only inputs supplied to the executor of an authoritative action.</summary>
/// <remarks>
/// This is deliberately distinct from <see cref="InteractionContext"/>: a rule answers "may this
/// happen", while an executor performs it. The target is fully reserved and coherent before this
/// context is built, so an executor may freely call back into gameplay.
/// </remarks>
/// <param name="ExecutionId">Identifier of the reservation, allocated before the executor runs.</param>
/// <param name="Interactor">Interactor that requested the action.</param>
/// <param name="Interactive">Interactive component owning the executed action.</param>
/// <param name="Action">Action being executed.</param>
public readonly record struct InteractionExecutionContext(
    ulong ExecutionId,
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
/// <param name="IsHoldable">Whether selecting this action requires holding its input.</param>
/// <param name="HoldProgress">
/// How far the hold selecting this action has progressed towards <b>its own</b> threshold, or null when
/// none is in progress.
/// </param>
/// <param name="HoldElapsed">
/// Seconds that hold has lasted, or null when none is in progress.
/// </param>
public readonly record struct InteractionActionPresentation(
    StringName ActionId,
    string Label,
    string Description,
    StringName InputActionName,
    InteractionAvailability Availability,
    bool IsAutomatic = false,
    bool IsHoldable = false,
    float? HoldProgress = null,
    float? HoldElapsed = null
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
/// <para>
/// Only named physical quantities are exposed here, never the raw score of the detection layer: the
/// score of an aim detector is an angle and that of a proximity detector a ratio, so a widget reading
/// it would break the day the detector changes. <paramref name="Distance"/> means the same thing
/// everywhere.
/// </para>
/// </remarks>
/// <param name="Interactive">Interactive component represented by the snapshot.</param>
/// <param name="DisplayName">Name of the target shown to the player.</param>
/// <param name="Description">Optional descriptive text supplied by the interactive.</param>
/// <param name="Actions">Presentable actions, in target declaration order.</param>
/// <param name="IsFocused">Whether this interactive is the current focus target.</param>
/// <param name="Distance">
/// World units between the interactor's interaction origin and this target's anchor.
/// </param>
public readonly record struct InteractionTargetPresentation(
    InteractiveComponent Interactive,
    string DisplayName,
    string Description,
    IReadOnlyList<InteractionActionPresentation> Actions,
    bool IsFocused,
    float Distance = 0.0f
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
