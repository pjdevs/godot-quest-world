using Godot;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Examples.Rules;

/// <summary>Rule that always blocks interaction with a configurable reason.</summary>
[GlobalClass]
public partial class AlwaysBlockedInteractionRule : InteractionRule
{
    /// <summary>Gets or sets the reason returned for every evaluation.</summary>
    [Export]
    public string Reason { get; set; } = "Interaction unavailable.";

    /// <inheritdoc />
    public override InteractionAvailability Evaluate(in InteractionContext context) =>
        new InteractionBlocked(Reason);
}
