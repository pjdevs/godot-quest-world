using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Rules;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactor;

namespace QuestWorld.Interaction.Runtime.Rules;

/// <summary>
/// Base resource for reusable gameplay conditions such as inventory, quest, or progression checks.
/// </summary>
/// <remarks>
/// Rules run during local client prevalidation and authoritative server validation, once per
/// evaluated action. Implementations must remain synchronous, side-effect free, and free of mutable
/// runtime state.
/// </remarks>
[GlobalClass]
public abstract partial class InteractionRule : GameplayActionRule
{
    /// <summary>
    /// Evaluates one synchronous, side-effect-free gameplay condition for one action.
    /// Runtime state belongs to nodes or services reached through the context, not to this resource.
    /// </summary>
    /// <param name="context">Interactor, interactive, and action data used by the condition.</param>
    /// <returns>
    /// An allowed availability, or the hidden or blocked result that stops the rule pipeline.
    /// </returns>
    public abstract InteractionAvailability Evaluate(in InteractionContext context);

    public sealed override GameplayActionAvailability Evaluate(in GameplayActionContext context)
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
            return new GameplayActionBlocked(InteractionAvailabilityExtensions.UnavailableReason);
        }

        return Evaluate(new InteractionContext(interactor, action.Interactive, action))
            .ToGameplayActionAvailability();
    }
}
