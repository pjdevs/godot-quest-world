using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Execution;

namespace QuestWorld.GameplayActions.Integration.Stateful;

/// <summary>Timed variant of the generic three-state transition executor.</summary>
[GlobalClass]
public partial class TimedTransitionStateGameplayActionExecutor
    : TransitionStateGameplayActionExecutor
{
    private readonly TimedExecution _timedExecution = new();

    [Export]
    public float Duration { get; set; }

    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    public virtual float ComputeTimedDuration(in GameplayActionContext context) => Duration;

    protected override GameplayActionExecutionResult StartRunning(
        in GameplayActionExecutionContext context
    )
    {
        GameplayActionContext query = new(
            context.Instigator,
            context.Requester,
            context.Component,
            context.Action,
            context.InvocationKind
        );
        TimedExecutionStartResult startResult = _timedExecution.Start(
            context.Component,
            context.ExecutionId,
            ComputeTimedDuration(query),
            CorrectionInterval
        );
        if (startResult == TimedExecutionStartResult.Started)
        {
            return base.StartRunning(context);
        }

        Stateful?.SetState(CancelledState);
        return new GameplayActionExecutionFailed(StartFailureReason(startResult));
    }

    internal override GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    ) => TimedExecution.BuildPredictionSample(ComputeTimedDuration(context));

    protected internal override void OnExecutionCompleted(in GameplayActionExecutionContext context)
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionCompleted(context);
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionExecutionContext context,
        string reason
    )
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionCancelled(context, reason);
    }

    protected internal override void OnExecutionFailed(
        in GameplayActionExecutionContext context,
        string reason
    )
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionFailed(context, reason);
    }

    private static string StartFailureReason(TimedExecutionStartResult result) =>
        result switch
        {
            TimedExecutionStartResult.AlreadyActive => "The timed executor is already active.",
            TimedExecutionStartResult.InvalidDuration =>
                "Timed execution duration must be finite and greater than zero.",
            TimedExecutionStartResult.InvalidExecution =>
                "The timed execution is no longer active.",
            TimedExecutionStartResult.MissingSceneTree =>
                "The timed execution requires an active scene tree.",
            _ => "The timed execution could not start.",
        };
}
