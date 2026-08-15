using Godot;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Examples.Rules;

[GlobalClass]
public partial class AlwaysBlockedInteractionRule : InteractionRule
{
    [Export]
    public string Reason { get; set; } = "Interaction unavailable.";

    public override InteractionStatus Evaluate(in InteractionContext context) =>
        new InteractionBlocked(Reason);
}
