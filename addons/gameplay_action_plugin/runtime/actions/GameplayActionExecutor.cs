using Godot;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public abstract partial class GameplayActionExecutor : Node
{
    public virtual bool RequiresRequesterPresence => true;

    public abstract GameplayActionExecutionResult Execute(
        in GameplayActionExecutionContext context
    );

    internal virtual Execution.GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    ) => null;

    protected internal virtual void OnExecutionCompleted(
        in GameplayActionExecutionContext context
    ) { }

    protected internal virtual void OnExecutionCancelled(
        in GameplayActionExecutionContext context,
        string reason
    ) { }

    protected internal virtual void OnExecutionFailed(
        in GameplayActionExecutionContext context,
        string reason
    ) { }
}
