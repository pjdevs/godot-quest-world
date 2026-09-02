using Godot;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public abstract partial class GameplayActionExecutor : Node
{
    public abstract GameplayActionExecutionResult Execute(
        in GameplayActionExecutionContext context
    );

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
