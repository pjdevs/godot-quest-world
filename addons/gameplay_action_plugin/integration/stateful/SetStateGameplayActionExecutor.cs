using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.State;

namespace QuestWorld.GameplayActions.Integration.Stateful;

/// <summary>Sets one Stateful component state as an instant gameplay action.</summary>
[GlobalClass]
public partial class SetStateGameplayActionExecutor : GameplayActionExecutor
{
    [Export]
    public StatefulComponent? Stateful { get; set; }

    [Export]
    public StringName TargetState { get; set; } = new(string.Empty);

    public override void _Ready()
    {
        if (Stateful is null)
        {
            GD.PushError($"{GetPath()}: SetStateGameplayActionExecutor requires a Stateful.");
            return;
        }

        if (TargetState.IsEmpty)
        {
            GD.PushError($"{GetPath()}: SetStateGameplayActionExecutor requires a TargetState.");
            return;
        }

        if (!Stateful.IsStateDeclared(TargetState))
        {
            GD.PushError(
                $"{GetPath()}: TargetState '{TargetState}' is not declared by the Schema of {Stateful.GetPath()}."
            );
        }
    }

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        if (Stateful is null || TargetState.IsEmpty)
        {
            return new GameplayActionExecutionFailed("This is not connected to anything.");
        }

        return Stateful.SetState(TargetState)
            ? new GameplayActionExecutionCompleted()
            : new GameplayActionExecutionFailed("Nothing happens.");
    }
}
