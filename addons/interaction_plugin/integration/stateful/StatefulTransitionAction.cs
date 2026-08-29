using Godot;
using QuestWorld.Interaction.Runtime.Actions;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Small instant action that composes a Stateful availability rule and state setter.</summary>
[GlobalClass]
public partial class StatefulTransitionAction : StatefulTransitionActionBase
{
    /// <summary>Gets or sets the state applied when this action executes.</summary>
    [Export]
    public StringName To { get; set; } = new(string.Empty);

    /// <inheritdoc />
    protected override InteractionActionExecutor CreateComposedExecutor() =>
        new SetStateInteractionExecutor();

    /// <inheritdoc />
    protected override void ConfigureComposedExecutor(InteractionActionExecutor executor)
    {
        SetStateInteractionExecutor setState = (SetStateInteractionExecutor)executor;
        setState.Stateful = Stateful;
        setState.TargetState = To;
    }
}
