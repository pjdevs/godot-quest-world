using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions;

/// <summary>Availability result indicating that an action may be requested.</summary>
public sealed record GameplayActionAllowed();

/// <summary>Availability result keeping an action presentable with a refusal reason.</summary>
/// <param name="Reason">Player-facing or diagnostic reason the action cannot currently run.</param>
public sealed record GameplayActionBlocked(string Reason = "Action unavailable.");

/// <summary>Availability result removing an action from the offered choices.</summary>
public sealed record GameplayActionHidden();

/// <summary>Current request availability of one gameplay action.</summary>
public readonly union GameplayActionAvailability(
    GameplayActionAllowed,
    GameplayActionBlocked,
    GameplayActionHidden
);

/// <summary>Helpers for consuming gameplay action availability results.</summary>
public static class GameplayActionAvailabilityExtensions
{
    /// <summary>Fallback refusal reason for an unavailable action with no explainable reason.</summary>
    public const string UnavailableReason = "Action unavailable.";

    /// <summary>Returns an explainable refusal reason, or an empty string when allowed.</summary>
    public static string DescribeRefusal(this GameplayActionAvailability availability) =>
        availability switch
        {
            GameplayActionAllowed => string.Empty,
            GameplayActionBlocked blocked => blocked.Reason,
            GameplayActionHidden => UnavailableReason,
        };
}

/// <summary>Availability an authoring-time rule may select when its condition does not hold.</summary>
/// <remarks>
/// <see cref="GameplayActionAvailability"/> is a union and cannot be exported, so authored rules
/// expose this enum instead. An allowed result carries no unavailable kind at all.
/// </remarks>
public enum GameplayActionUnavailableKind
{
    /// <summary>The action leaves the offered choices, without any reason to display.</summary>
    Hidden,

    /// <summary>The action stays presentable and explains why it cannot run.</summary>
    Blocked,
}

/// <summary>Turns an authored unavailable kind into a generic gameplay action availability.</summary>
public static class GameplayActionUnavailableKindExtensions
{
    /// <summary>Builds the availability selected by an authored refusal.</summary>
    /// <param name="kind">Unavailable case chosen in the Inspector.</param>
    /// <param name="reason">Reason carried by a blocked result, ignored when hidden.</param>
    public static GameplayActionAvailability ToAvailability(
        this GameplayActionUnavailableKind kind,
        string reason
    ) =>
        kind switch
        {
            GameplayActionUnavailableKind.Blocked => new GameplayActionBlocked(reason),
            _ => new GameplayActionHidden(),
        };
}

/// <summary>Controls which remote peers receive transient presentation of a running execution.</summary>
public enum GameplayActionExecutionVisibility
{
    /// <summary>Only the requesting runner receives lifecycle acknowledgements/presentation.</summary>
    RequesterOnly,

    /// <summary>Observers may receive the execution through an execution synchronizer.</summary>
    Replicated,

    /// <summary>The execution remains presentation-local to authority.</summary>
    AuthorityOnly,
}

/// <summary>Describes this peer's local relationship to a visible execution.</summary>
public enum GameplayActionExecutionRelation
{
    /// <summary>The execution is visible here but was not requested locally.</summary>
    Observed,

    /// <summary>The local runner requested this execution.</summary>
    RequestedLocally,
}

/// <summary>Input gesture that selects a gameplay action binding.</summary>
public enum GameplayActionActivationMode
{
    /// <summary>Select on the input press edge.</summary>
    Press,

    /// <summary>Select after the authored hold threshold is reached.</summary>
    Hold,

    /// <summary>Select on the input release edge.</summary>
    Release,

    /// <summary>Select automatically when the binding becomes eligible.</summary>
    Automatic,
}

/// <summary>Input state that must remain true while an accepted request is sustained.</summary>
public enum GameplayActionInputRequirement
{
    /// <summary>The input does not sustain the accepted execution.</summary>
    None,

    /// <summary>Releasing the originating input requests cancellation.</summary>
    Pressed,
}

/// <summary>Read-only transient presentation of one running gameplay action execution.</summary>
/// <param name="ExecutionId">Stable execution identifier assigned by authority.</param>
/// <param name="ActionId">Stable action identifier.</param>
/// <param name="Progress">Optional normalized execution progress.</param>
/// <param name="Relation">This peer's local relationship to the execution.</param>
public readonly record struct GameplayActionExecutionPresentation(
    ulong ExecutionId,
    StringName ActionId,
    float? Progress = null,
    GameplayActionExecutionRelation Relation = GameplayActionExecutionRelation.Observed
);

/// <summary>Execution result indicating synchronous successful completion.</summary>
public sealed record GameplayActionExecutionCompleted();

/// <summary>Execution result reserving the action until a later terminal call.</summary>
public sealed record GameplayActionExecutionRunning();

/// <summary>Execution result refusing an accepted dispatch without starting lifecycle notifications.</summary>
/// <param name="Reason">Reason the executor refused to start.</param>
public sealed record GameplayActionExecutionRejected(string Reason = "Action unavailable.");

/// <summary>Execution result indicating that accepted execution failed.</summary>
/// <param name="Reason">Failure reason.</param>
public sealed record GameplayActionExecutionFailed(string Reason = "The action failed.");

/// <summary>Result returned by a gameplay action executor.</summary>
public readonly union GameplayActionExecutionResult(
    GameplayActionExecutionCompleted,
    GameplayActionExecutionRunning,
    GameplayActionExecutionRejected,
    GameplayActionExecutionFailed
);

/// <summary>Read-only inputs supplied to every rule and executor of one gameplay action.</summary>
/// <remarks>
/// One shape answers both questions an action asks, because an evaluation and the execution it
/// authorises read the same facts. <paramref name="ExecutionId"/> is the only thing an evaluation
/// does not have yet, and zero says so.
/// <para>
/// Where the action came from is not one of those facts. What an execution owes its caller is
/// carried by <paramref name="Requester"/>: a requester is present exactly when a runner asked for
/// this action and is therefore waiting to be acknowledged, and only the request transport can put
/// one there.
/// </para>
/// </remarks>
/// <param name="ExecutionId">Identifier of the reservation, or zero while none is reserved.</param>
/// <param name="Instigator">Node the action is attributed to, or null when nothing claims it.</param>
/// <param name="Requester">Runner awaiting acknowledgement, or null outside a request.</param>
/// <param name="Component">Generic action component owning the action occurrence.</param>
/// <param name="Action">Action being evaluated or executed.</param>
/// <param name="Host">Gameplay object hosting the action, or null when it has no host.</param>
/// <param name="World">Gameplay world containing the execution, or null when unavailable.</param>
public readonly record struct GameplayActionContext(
    ulong ExecutionId,
    Node? Instigator,
    Node? Requester,
    GameplayActionComponent Component,
    GameplayAction Action,
    Node? Host = null,
    Node? World = null
)
{
    /// <summary>Returns the instigator as the requested type, or null when incompatible.</summary>
    public T? GetInstigator<T>()
        where T : class => Instigator as T;

    /// <summary>Returns the gameplay host as the requested type, or null when incompatible.</summary>
    public T? GetHost<T>()
        where T : class => Host as T;

    /// <summary>Returns the gameplay world as the requested type, or null when incompatible.</summary>
    public T? GetWorld<T>()
        where T : class => World as T;
}
