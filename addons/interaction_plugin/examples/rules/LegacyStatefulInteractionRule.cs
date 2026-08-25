using Godot;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Examples.Rules;

/// <summary>
/// Reproduces the V1 lifecycle gating of <c>InteractiveComponent.Stateful</c> as an explicit rule.
/// </summary>
/// <remarks>
/// The interaction core no longer interprets any state value, so the former built-in
/// <c>Idle == interactable</c> assumption now lives here, where a scene opts into it. This rule is a
/// migration helper for the V1 example objects: the generic replacement is the state rule shipped with
/// the Stateful integration primitives, and this class is removed with the V1 compatibility layer.
/// </remarks>
[GlobalClass]
public partial class LegacyStatefulInteractionRule : InteractionRule
{
    /// <summary>Gets or sets the reason returned while a transient phase is running.</summary>
    [Export]
    public string BusyReason { get; set; } = "This is busy.";

    /// <summary>Gets or sets the reason returned once the state component has been activated.</summary>
    [Export]
    public string ActivatedReason { get; set; } = "This is already activated.";

    /// <inheritdoc />
    public override InteractionAvailability Evaluate(in InteractionContext context)
    {
        if (
            context.Interactive.Stateful is null
            || context.Interactive.Stateful.State == InteractionState.Idle
        )
        {
            return new InteractionAllowed();
        }

        return context.Interactive.Stateful.State == InteractionState.Activated
            ? new InteractionBlocked(ActivatedReason)
            : new InteractionBlocked(BusyReason);
    }
}
