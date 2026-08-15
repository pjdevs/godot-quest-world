using Godot;

namespace QuestWorld.Interaction.Runtime.Rules;

[GlobalClass]
public abstract partial class InteractionRule : Resource
{
    public abstract InteractionStatus Evaluate(in InteractionContext context);
}
