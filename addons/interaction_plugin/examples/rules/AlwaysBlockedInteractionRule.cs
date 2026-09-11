using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Rules;
using Godot;

namespace InteractionPlugin.Examples.Rules;

/// <summary>Rule that always blocks interaction with a configurable reason.</summary>
[GlobalClass]
public partial class AlwaysBlockedInteractionRule : GameplayActionRule
{
    /// <summary>Gets or sets the reason returned for every evaluation.</summary>
    [Export]
    public string Reason { get; set; } = "Interaction unavailable.";

    /// <inheritdoc />
    public override GameplayActionAvailability Evaluate(in GameplayActionContext context) =>
        new GameplayActionBlocked(Reason);
}
