using Godot;

namespace QuestWorld.Interaction.Runtime.Rules;

/// <summary>
/// Base resource for reusable gameplay conditions such as inventory, quest, or progression checks.
/// </summary>
/// <remarks>
/// Rules run during local client prevalidation and authoritative server validation. Implementations
/// must remain synchronous, side-effect free, and free of mutable runtime state.
/// </remarks>
[GlobalClass]
public abstract partial class InteractionRule : Resource
{
    /// <summary>
    /// Evaluates one synchronous, side-effect-free gameplay condition for the interaction.
    /// Runtime state belongs to nodes or services reached through the context, not to this resource.
    /// </summary>
    /// <param name="context">Interaction and owner data used by the condition.</param>
    /// <returns>An allowed status, or the blocked reason that stops the rule pipeline.</returns>
    public abstract InteractionStatus Evaluate(in InteractionContext context);
}
