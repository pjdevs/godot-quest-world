using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Examples.Rules;

/// <summary>Allows only interactors that belong to a configured Godot node group.</summary>
[GlobalClass]
public partial class InteractorGroupInteractionRule : InteractionRule
{
    /// <summary>Gets or sets the required group. An empty value allows every interactor.</summary>
    [Export]
    public StringName? RequiredGroup { get; set; }

    /// <summary>Gets or sets the reason returned when the interactor is outside the required group.</summary>
    [Export]
    public string MissingGroupReason { get; set; } = "You cannot use this yet.";

    /// <inheritdoc />
    public override GameplayActionAvailability Evaluate(in InteractionContext context)
    {
        if (string.IsNullOrEmpty(RequiredGroup) || context.Interactor.IsInGroup(RequiredGroup))
        {
            return new GameplayActionAllowed();
        }

        return new GameplayActionBlocked(MissingGroupReason);
    }
}
