using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>
/// Single owner of the gameplay mutation of one action, called by the authoritative target.
/// </summary>
/// <remarks>
/// Add the node to the target scene and reference it from <see cref="InteractionAction.Executor"/>;
/// nothing is discovered in the tree and an action without executor is a configuration error. The
/// core never broadcasts a command, so exactly one executor runs per accepted action. Availability
/// belongs to rules: this call happens once the action is already allowed and reserved.
/// </remarks>
[GlobalClass]
public abstract partial class InteractionActionExecutor : GameplayActionExecutor
{
    /// <summary>Performs the gameplay mutation of one accepted action on the authoritative peer.</summary>
    /// <remarks>
    /// Called synchronously by <see cref="Interactive.InteractiveComponent.ExecuteAction"/> once the
    /// target is reserved and coherent. Returning <see cref="InteractionExecutionRunning"/> keeps the
    /// reservation until gameplay completes or cancels the execution.
    /// </remarks>
    /// <param name="context">Interactor, interactive, and action of the accepted command.</param>
    /// <returns>The outcome deciding whether the target keeps the execution reserved.</returns>
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

    /// <summary>Gets whether an execution ends when the interactor stops being there.</summary>
    public virtual bool RequiresInteractorPresence => true;

    public sealed override bool RequiresRequesterPresence => RequiresInteractorPresence;

    /// <summary>Reports that an execution this executor left running reached its end.</summary>
    /// <param name="context">Context of the execution that ended, carrying its identifier.</param>
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

    /// <summary>Reports that an execution this executor left running ended without completing.</summary>
    /// <param name="context">Context of the execution that ended, carrying its identifier.</param>
    /// <param name="reason">Reason describing why the execution did not complete.</param>
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

    /// <summary>Reports that an execution this executor left running failed after acceptance.</summary>
    /// <param name="context">Context of the execution that failed.</param>
    /// <param name="reason">Reason describing the failure.</param>
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

    /// <summary>Returns the optional initial progress sample a requester may predict locally.</summary>
    /// <remarks>
    /// The hook is intentionally internal and producer-agnostic to the renderer. Timed executors opt in
    /// with a linear sample; generic executors return no prediction and still use the same presentation
    /// record once the authority acknowledges them.
    /// </remarks>
    /// <param name="context">Pure query context for the action being requested.</param>
    /// <returns>An initial sample, or null when no local prediction is available.</returns>
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

    /// <summary>Keeps the execution reserved until gameplay ends it, with no deadline.</summary>
    /// <returns>A payload-free running outcome.</returns>
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
