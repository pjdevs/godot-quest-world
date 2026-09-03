using Godot;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public abstract partial class GameplayActionExecutor : Node
{
    public virtual bool RequiresRequesterPresence => true;

    public abstract GameplayActionExecutionResult Execute(in GameplayActionContext context);

    internal virtual Execution.GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    ) => null;

    protected internal virtual void OnExecutionCompleted(in GameplayActionContext context) { }

    protected internal virtual void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    ) { }

    protected internal virtual void OnExecutionFailed(
        in GameplayActionContext context,
        string reason
    ) { }
}
