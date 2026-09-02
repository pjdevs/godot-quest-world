using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>
/// Interaction adapter over one generic gameplay-action executor.
/// </summary>
[GlobalClass]
public abstract partial class InteractionActionExecutor : GameplayActionExecutor
{
    internal InteractionAction? InteractionAction { get; set; }

    public abstract InteractionExecutionResult Execute(in InteractionExecutionContext context);

    public sealed override GameplayActionExecutionResult Execute(
        in GameplayActionExecutionContext context
    )
    {
        if (!TryAdapt(context, out InteractionExecutionContext interactionContext))
        {
            return new GameplayActionExecutionFailed("The interaction execution context is invalid.");
        }

        return Execute(interactionContext).ToGameplayActionExecutionResult();
    }

    public virtual bool RequiresInteractorPresence => true;

    public sealed override bool RequiresRequesterPresence =>
        RequiresInteractorPresence
        || InteractionAction?.InteractionDefinition?.CancelOnInputReleased == true;

    protected internal virtual void OnExecutionCompleted(in InteractionExecutionContext context) { }

    protected internal sealed override void OnExecutionCompleted(
        in GameplayActionExecutionContext context
    )
    {
        if (TryAdapt(context, out InteractionExecutionContext interactionContext))
        {
            OnExecutionCompleted(interactionContext);
        }
    }

    protected internal virtual void OnExecutionCancelled(
        in InteractionExecutionContext context,
        string reason
    ) { }

    protected internal sealed override void OnExecutionCancelled(
        in GameplayActionExecutionContext context,
        string reason
    )
    {
        if (TryAdapt(context, out InteractionExecutionContext interactionContext))
        {
            OnExecutionCancelled(interactionContext, reason);
        }
    }

    protected internal virtual void OnExecutionFailed(
        in InteractionExecutionContext context,
        string reason
    ) { }

    protected internal sealed override void OnExecutionFailed(
        in GameplayActionExecutionContext context,
        string reason
    )
    {
        if (TryAdapt(context, out InteractionExecutionContext interactionContext))
        {
            OnExecutionFailed(interactionContext, reason);
        }
    }

    internal virtual InteractionProgressSample? GetInteractionPredictionSample(
        in InteractionContext context
    ) => null;

    internal sealed override GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    )
    {
        if (
            context.Action is not InteractionAction action
            || action.Interactive is null
            || context.Requester is not GameplayActionRunner runner
            || InteractionInteractor.FindByRunner(runner) is not InteractionInteractor interactor
        )
        {
            return null;
        }

        InteractionProgressSample? sample = GetInteractionPredictionSample(
            new InteractionContext(interactor, action.Interactive, action)
        );
        return sample is InteractionProgressSample value
            ? new GameplayActionProgressSample(
                value.ProgressBase,
                value.ProgressPerSecond,
                value.Revision
            )
            : null;
    }

    protected static InteractionExecutionResult Running() => new InteractionExecutionRunning();

    private static bool TryAdapt(
        in GameplayActionExecutionContext context,
        out InteractionExecutionContext interactionContext
    )
    {
        interactionContext = default;
        if (
            context.Action is not InteractionAction action
            || action.Interactive is null
            || context.Requester is not GameplayActionRunner runner
            || InteractionInteractor.FindByRunner(runner) is not InteractionInteractor interactor
        )
        {
            return false;
        }

        interactionContext = new InteractionExecutionContext(
            context.ExecutionId,
            interactor,
            action.Interactive,
            action
        );
        return true;
    }
}
