using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Rules;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Rules;

/// <summary>
/// Injects one InteractiveComponent's target-level rules ahead of the authored action rules without
/// copying either collection into the other.
/// </summary>
internal sealed partial class InteractionTargetRulesAdapter : GameplayActionRule
{
    public InteractiveComponent? Interactive { get; set; }

    public override GameplayActionAvailability Evaluate(in GameplayActionContext context)
    {
        if (Interactive is null)
        {
            return new GameplayActionBlocked(
                GameplayActionAvailabilityExtensions.UnavailableReason
            );
        }

        foreach (InteractionRule rule in Interactive.TargetRules)
        {
            if (rule is null)
            {
                continue;
            }

            GameplayActionAvailability availability = rule.Evaluate(context);
            if (availability is not GameplayActionAllowed)
            {
                return availability;
            }
        }

        return new GameplayActionAllowed();
    }
}
