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

public enum GameplayActionInvocationKind
{
    PlayerRequest,
    Programmatic,
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

public readonly record struct GameplayActionContext(
    Node? Instigator,
    Node? Requester,
    GameplayActionComponent Component,
    GameplayAction Action,
    GameplayActionInvocationKind InvocationKind
);

public readonly record struct GameplayActionExecutionContext(
    ulong ExecutionId,
    Node? Instigator,
    Node? Requester,
    GameplayActionComponent Component,
    GameplayAction Action,
    GameplayActionInvocationKind InvocationKind
);
