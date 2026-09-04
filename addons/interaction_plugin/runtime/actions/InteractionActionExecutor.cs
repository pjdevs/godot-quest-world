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

    public sealed override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        if (!TryAdapt(context, out InteractionExecutionContext interactionContext))
        {
            return new GameplayActionExecutionFailed(DescribeUnadaptable(context));
        }

        return Execute(interactionContext);
    }

    public virtual bool RequiresInteractorPresence => true;

    public sealed override bool RequiresRequesterPresence =>
        RequiresInteractorPresence
        || InteractionAction?.DefaultBindingConfig?.InputRequirement
            == GameplayActionInputRequirement.Pressed;

    protected internal virtual void OnExecutionCompleted(in InteractionExecutionContext context) { }

    protected internal sealed override void OnExecutionCompleted(in GameplayActionContext context)
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
        in GameplayActionContext context,
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
        in GameplayActionContext context,
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
        if (
            context.Action is not InteractionAction action
            || action.Interactive is null
            || context.Instigator is not InteractionInteractor interactor
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

    /// <summary>Says which part of a generic execution keeps it from being an interaction.</summary>
    /// <remarks>
    /// An interaction executor is written against an interactor and a target; an execution missing
    /// either is not an interaction and cannot be adapted into one. That is a deliberate boundary
    /// rather than a gap: what the failure owes its reader is which half is absent, so a wrongly
    /// hosted action and a world-driven execution are not both reported as an invalid context.
    /// </remarks>
    private static string DescribeUnadaptable(in GameplayActionContext context)
    {
        if (context.Action is not InteractionAction action)
        {
            return "This action is not an interaction action.";
        }

        if (action.Interactive is null)
        {
            return "This interaction action is not hosted by an interactive target.";
        }

        return "An interaction action only runs for an interactor, and this execution has none.";
    }

    private static bool TryAdapt(
        in GameplayActionContext context,
        out InteractionExecutionContext interactionContext
    )
    {
        interactionContext = default;
        if (
            context.Action is not InteractionAction action
            || action.Interactive is null
            || context.Instigator is not InteractionInteractor interactor
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
