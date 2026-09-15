using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Rules;
using Godot;

[GlobalClass]
public partial class GroundedActionRule : GameplayActionRule
{
    [Export]
    public string BlockedReason { get; set; } = "Must be grounded";

    [Export]
    public bool ShouldHide { get; set; } = true;

    public override GameplayActionAvailability Evaluate(in GameplayActionContext context)
    {
        if (
            context.GetHost<QuestWorldCharacter>() is QuestWorldCharacter character
            && character.NetworkIsGrounded
        )
        {
            return new GameplayActionAllowed();
        }

        return ShouldHide ? new GameplayActionHidden() : new GameplayActionBlocked(BlockedReason);
    }
}
