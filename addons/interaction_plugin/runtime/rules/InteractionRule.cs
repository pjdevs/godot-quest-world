using Godot;

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
public abstract partial class InteractionRule : Resource
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
}
