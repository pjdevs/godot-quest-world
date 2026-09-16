using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Rules;
using Godot;

namespace InteractionPlugin.Examples.Rules;

/// <summary>Allows only interactors that belong to a configured Godot node group.</summary>
[GlobalClass]
public partial class InteractorGroupInteractionRule : GameplayActionRule
{
    /// <summary>Gets or sets the required group. An empty value allows every interactor.</summary>
    [Export]
    public StringName? RequiredGroup { get; set; }

    /// <summary>Gets or sets the reason returned when the interactor is outside the required group.</summary>
    [Export]
    public string MissingGroupReason { get; set; } = "You cannot use this yet.";

    /// <inheritdoc />
    public override GameplayActionAvailability Evaluate(in GameplayActionContext context)
    {
        if (
            string.IsNullOrEmpty(RequiredGroup)
            || context.GetInstigator<Node>()?.IsInGroup(RequiredGroup) == true
        )
        {
            return new GameplayActionAllowed();
        }

        return new GameplayActionBlocked(MissingGroupReason);
    }
}
