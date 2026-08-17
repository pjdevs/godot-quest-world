using Godot;

namespace QuestWorld.Interaction.Runtime.Rules;

[GlobalClass]
public abstract partial class InteractionRule : Resource
{
    /// <summary>
    /// Evaluates one synchronous, side-effect-free gameplay condition for the interaction.
    /// Runtime state belongs to nodes or services reached through the context, not to this resource.
    /// </summary>
    public abstract InteractionStatus Evaluate(in InteractionContext context);
}
