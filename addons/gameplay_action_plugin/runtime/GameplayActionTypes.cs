using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;

namespace QuestWorld.GameplayActions;

public sealed record GameplayActionAllowed();

public sealed record GameplayActionBlocked(string Reason = "Action unavailable.");

public sealed record GameplayActionHidden();

public readonly union GameplayActionAvailability(
    GameplayActionAllowed,
    GameplayActionBlocked,
    GameplayActionHidden
);

public static class GameplayActionAvailabilityExtensions
{
    public const string UnavailableReason = "Action unavailable.";

    public static string DescribeRefusal(this GameplayActionAvailability availability) =>
        availability switch
        {
            GameplayActionAllowed => string.Empty,
            GameplayActionBlocked blocked => blocked.Reason,
            GameplayActionHidden => UnavailableReason,
        };
}

public enum GameplayActionExecutionVisibility
{
    RequesterOnly,
    Replicated,
    AuthorityOnly,
}

public enum GameplayActionActivationMode
{
    Press,
    Hold,
    Release,
    Automatic,
}

public enum GameplayActionInputRequirement
{
    None,
    Pressed,
}

public readonly record struct GameplayActionExecutionPresentation(
    ulong ExecutionId,
    StringName ActionId,
    float? Progress = null
);

public sealed record GameplayActionExecutionCompleted();

public sealed record GameplayActionExecutionRunning();

public sealed record GameplayActionExecutionRejected(
    string Reason = "Action unavailable."
);

public sealed record GameplayActionExecutionFailed(string Reason = "The action failed.");

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
    public T? GetInstigator<T>()
        where T : Node => Instigator as T;

    public T? GetHost<T>()
        where T : Node => Host as T;

    public T? GetWorld<T>()
        where T : Node => World as T;
}
