using Godot;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Examples.Rules;

[GlobalClass]
public partial class InteractorGroupInteractionRule : InteractionRule
{
    [Export]
    public StringName? RequiredGroup { get; set; }

    [Export]
    public string MissingGroupReason { get; set; } = "You cannot use this yet.";

    public override InteractionStatus Evaluate(in InteractionContext context)
    {
        if (string.IsNullOrEmpty(RequiredGroup) || context.Interactor.IsInGroup(RequiredGroup))
        {
            return new InteractionAllowed();
        }

        return new InteractionBlocked(MissingGroupReason);
    }
}
