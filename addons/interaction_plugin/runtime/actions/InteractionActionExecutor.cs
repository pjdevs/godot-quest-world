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

    public abstract GameplayActionExecutionResult Execute(in InteractionExecutionContext context);

    public sealed override GameplayActionExecutionResult Execute(
        in GameplayActionExecutionContext context
    )
    {
        if (!TryAdapt(context, out InteractionExecutionContext interactionContext))
        {
            return new GameplayActionExecutionFailed(
                "The interaction execution context is invalid."
            );
        }

        return Execute(interactionContext);
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

    internal virtual GameplayActionProgressSample? GetInteractionPredictionSample(
        in InteractionContext context
    ) => null;

    internal sealed override GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    )
    {
        InteractionInteractor? interactor = context.Requester is GameplayActionRunner runner
            ? InteractionInteractor.FindByRunner(runner)
            : null;
        interactor ??= context.Instigator as InteractionInteractor;
        if (
            context.Action is not InteractionAction action
            || action.Interactive is null
            || interactor is null
        )
        {
            return null;
        }

        return GetInteractionPredictionSample(
            new InteractionContext(interactor, action.Interactive, action)
        );
    }

    protected static GameplayActionExecutionResult Running() =>
        new GameplayActionExecutionRunning();

    private static bool TryAdapt(
        in GameplayActionExecutionContext context,
        out InteractionExecutionContext interactionContext
    )
    {
        interactionContext = default;
        InteractionInteractor? interactor = context.Requester is GameplayActionRunner runner
            ? InteractionInteractor.FindByRunner(runner)
            : null;
        interactor ??= context.Instigator as InteractionInteractor;
        if (
            context.Action is not InteractionAction action
            || action.Interactive is null
            || interactor is null
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
