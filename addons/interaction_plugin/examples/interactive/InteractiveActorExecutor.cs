using Godot;
using QuestWorld.Interaction.Runtime.Actions;

namespace QuestWorld.Interaction.Examples.Interactive;

/// <summary>Example executor binding one action to the long activation of an <see cref="InteractiveActor"/>.</summary>
/// <remarks>
/// This is the shape a game usually gives an executor: a small node placed under the action it
/// performs, holding an explicit reference to the gameplay script that owns the behaviour.
/// </remarks>
[GlobalClass]
public partial class InteractiveActorExecutor : InteractionActionExecutor
{
    /// <summary>Gets or sets the required example actor performing the activation.</summary>
    [Export]
    public InteractiveActor? Actor { get; set; }

    /// <summary>Godot callback that reports a missing actor reference.</summary>
    public override void _Ready()
    {
        if (Actor is null)
        {
            GD.PushError($"{GetPath()}: InteractiveActorExecutor requires an Actor.");
        }
    }

    /// <inheritdoc />
    public override InteractionExecutionResult Execute(in InteractionExecutionContext context) =>
        Actor is null
            ? new InteractionExecutionFailed("This example is not connected to its actor.")
            : Actor.BeginActivation();
}
