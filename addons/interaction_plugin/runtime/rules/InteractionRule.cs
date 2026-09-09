using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Rules;
using GameplayActionPlugin.Runtime.Runner;
using Godot;
using InteractionPlugin.Runtime.Actions;
using InteractionPlugin.Runtime.Interactor;

namespace InteractionPlugin.Runtime.Rules;

/// <summary>
/// Base resource for reusable target and offer conditions such as inventory, quest, or progression checks.
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
    /// <param name="context">Interactor, interactive, offer, and resolved action data used by the condition.</param>
    /// <returns>
    /// An allowed availability, or the hidden or blocked result that stops the rule pipeline.
    /// </returns>
    public abstract GameplayActionAvailability Evaluate(in InteractionContext context);

    public sealed override GameplayActionAvailability Evaluate(in GameplayActionContext context)
    {
        if (
            context.Action is not InteractionAction action
            || action.Interactive is null
            || InteractionInteractor.ResolveForInstigator(context.GetInstigator<Node>())
                is not InteractionInteractor interactor
        )
        {
            return new GameplayActionBlocked(
                GameplayActionAvailabilityExtensions.UnavailableReason
            );
        }

        return Evaluate(
            new InteractionContext(interactor, action.Interactive, action, null, action.Component)
        );
    }
}
