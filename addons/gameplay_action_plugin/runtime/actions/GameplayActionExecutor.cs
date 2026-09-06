using Godot;

namespace QuestWorld.GameplayActions.Runtime.Actions;

/// <summary>Base class for the single gameplay command executed by one action occurrence.</summary>
[GlobalClass]
public abstract partial class GameplayActionExecutor : Node
{
    /// <summary>Gets whether requester/access loss should cancel a running requested execution.</summary>
    public virtual bool RequiresRequesterPresence => true;

    /// <summary>Executes the command after authority has validated rules and reserved the action.</summary>
    /// <param name="context">Read-only context of the reserved execution.</param>
    /// <returns>The synchronous or running execution outcome.</returns>
    public abstract GameplayActionExecutionResult Execute(in GameplayActionContext context);

    internal virtual Execution.GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    ) => null;

    /// <summary>Called after this executor's execution completes and its reservation is released.</summary>
    protected internal virtual void OnExecutionCompleted(in GameplayActionContext context) { }

    /// <summary>Called after this executor's execution is cancelled and its reservation is released.</summary>
    protected internal virtual void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    ) { }

    /// <summary>Called after this executor's execution fails and its reservation is released.</summary>
    protected internal virtual void OnExecutionFailed(
        in GameplayActionContext context,
        string reason
    ) { }
}
