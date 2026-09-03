using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.State;

namespace QuestWorld.GameplayActions.Integration.Stateful;

/// <summary>Drives a Stateful component through running, completed, and cancelled states.</summary>
[GlobalClass]
public partial class TransitionStateGameplayActionExecutor : GameplayActionExecutor
{
    [Export]
    public StatefulComponent? Stateful { get; set; }

    [Export]
    public StringName RunningState { get; set; } = new(string.Empty);

    [Export]
    public StringName CompletedState { get; set; } = new(string.Empty);

    [Export]
    public StringName CancelledState { get; set; } = new(string.Empty);

    [Export]
    public bool RequiresPresence { get; set; } = true;

    public override bool RequiresRequesterPresence => RequiresPresence;

    public override void _Ready()
    {
        if (Stateful is null)
        {
            GD.PushError(
                $"{GetPath()}: TransitionStateGameplayActionExecutor requires a Stateful."
            );
            return;
        }

        foreach (StringName state in new[] { RunningState, CompletedState, CancelledState })
        {
            if (state.IsEmpty)
            {
                GD.PushError(
                    $"{GetPath()}: TransitionStateGameplayActionExecutor requires its three states."
                );
                continue;
            }

            if (!Stateful.IsStateDeclared(state))
            {
                GD.PushError(
                    $"{GetPath()}: state '{state}' is not declared by the Schema of {Stateful.GetPath()}."
                );
            }
        }
    }

    public override GameplayActionExecutionResult Execute(in GameplayActionContext context)
    {
        if (Stateful is null)
        {
            return new GameplayActionExecutionFailed("This is not connected to anything.");
        }

        return Stateful.SetState(RunningState)
            ? StartRunning(context)
            : new GameplayActionExecutionFailed("This cannot start.");
    }

    protected virtual GameplayActionExecutionResult StartRunning(
        in GameplayActionContext context
    ) => new GameplayActionExecutionRunning();

    protected internal override void OnExecutionCompleted(in GameplayActionContext context)
    {
        base.OnExecutionCompleted(context);
        Stateful?.SetState(CompletedState);
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    )
    {
        base.OnExecutionCancelled(context, reason);
        Stateful?.SetState(CancelledState);
    }

    protected internal override void OnExecutionFailed(
        in GameplayActionContext context,
        string reason
    )
    {
        base.OnExecutionFailed(context, reason);
        Stateful?.SetState(CancelledState);
    }
}
