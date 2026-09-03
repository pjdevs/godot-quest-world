using Godot;
using QuestWorld.GameplayActions.Runtime.Execution;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public abstract partial class TimedGameplayActionExecutor : GameplayActionExecutor
{
    [Export]
    public float Duration { get; set; }

    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    private readonly TimedExecution _timedExecution = new();

    protected bool IsTimerActive => _timedExecution.IsActive;

    public virtual float ComputeTimedDuration(in GameplayActionContext context) => Duration;

    internal override GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    ) => TimedExecution.BuildPredictionSample(ComputeTimedDuration(context));

    protected GameplayActionExecutionResult RunningTimed(in GameplayActionContext context)
    {
        TimedExecutionStartResult startResult = _timedExecution.Start(
            context.Component,
            context.ExecutionId,
            ComputeTimedDuration(context),
            CorrectionInterval
        );
        return startResult == TimedExecutionStartResult.Started
            ? new GameplayActionExecutionRunning()
            : new GameplayActionExecutionFailed(StartFailureReason(startResult));
    }

    protected internal override void OnExecutionCompleted(in GameplayActionContext context)
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionCompleted(context);
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    )
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionCancelled(context, reason);
    }

    protected internal override void OnExecutionFailed(
        in GameplayActionContext context,
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
