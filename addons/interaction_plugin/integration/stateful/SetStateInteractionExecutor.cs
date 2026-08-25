using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>
/// Generic executor whose whole gameplay mutation is setting one <see cref="StatefulComponent"/> state.
/// </summary>
/// <remarks>
/// This node replaces most small scripts that used to observe an interaction signal and change the
/// state of an object themselves. The target is an explicit node reference and may live in another
/// scene, so a button switching a distant machine needs no script at all.
/// <para>
/// It deliberately performs one mutation and nothing else: animation, delay, audio, quest, inventory,
/// or several combined effects belong to a specialized executor of the game, not here.
/// </para>
/// </remarks>
[GlobalClass]
public partial class SetStateInteractionExecutor : InteractionActionExecutor
{
    /// <summary>Gets or sets the required component whose state this action applies.</summary>
    [Export]
    public StatefulComponent? Stateful { get; set; }

    /// <summary>Gets or sets the state applied to the target, for example <c>open</c>.</summary>
    [Export]
    public StringName TargetState { get; set; } = new(string.Empty);

    /// <summary>Godot callback that reports a missing target or an undeclared state.</summary>
    public override void _Ready()
    {
        if (Stateful is null)
        {
            GD.PushError($"{GetPath()}: SetStateInteractionExecutor requires a Stateful.");
            return;
        }

        if (TargetState.IsEmpty)
        {
            GD.PushError($"{GetPath()}: SetStateInteractionExecutor requires a TargetState.");
            return;
        }

        if (!Stateful.IsStateDeclared(TargetState))
        {
            GD.PushError(
                $"{GetPath()}: TargetState '{TargetState}' is not declared by the Schema of {Stateful.GetPath()}."
            );
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reaching a state the target already holds is reported as a failure rather than as a silent
    /// success: preventing that case is the job of the rules of the action, where the player also
    /// sees it.
    /// </remarks>
    public override InteractionExecutionResult Execute(in InteractionExecutionContext context)
    {
        if (Stateful is null || TargetState.IsEmpty)
        {
            return new InteractionExecutionFailed("This is not connected to anything.");
        }

        return Stateful.SetState(TargetState)
            ? new InteractionExecutionCompleted()
            : new InteractionExecutionFailed("Nothing happens.");
    }
}
