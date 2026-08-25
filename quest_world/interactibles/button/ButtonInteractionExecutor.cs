using Godot;
using QuestWorld.Interaction;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.State;

/// <summary>Executor switching the state of another object when the button action runs.</summary>
/// <remarks>
/// The whole button behaviour lives here: the scene root carries no script and nothing subscribes to
/// an interaction signal. The target is a concrete Godot node rather than a C# interface, so the
/// same wiring stays expressible from the editor and from another language.
/// </remarks>
public partial class ButtonInteractionExecutor : InteractionActionExecutor
{
    /// <summary>Gets or sets the required state component switched by this button.</summary>
    [Export]
    public InteractionStateful? TargetStateful { get; set; } = null;

    /// <summary>Gets or sets the state applied to the target.</summary>
    [Export]
    public InteractionState TargetState { get; set; } = InteractionState.Activating;

    /// <summary>Godot callback that reports a missing target.</summary>
    public override void _Ready()
    {
        if (TargetStateful is null)
        {
            GD.PushError($"{GetPath()}: ButtonInteractionExecutor requires a TargetStateful.");
        }
    }

    /// <inheritdoc />
    public override InteractionExecutionResult Execute(in InteractionExecutionContext context)
    {
        if (TargetStateful is null)
        {
            return new InteractionExecutionFailed("This button is not connected to anything.");
        }

        return TargetStateful.SetState(TargetState)
            ? new InteractionExecutionCompleted()
            : new InteractionExecutionFailed("Nothing happens.");
    }
}
