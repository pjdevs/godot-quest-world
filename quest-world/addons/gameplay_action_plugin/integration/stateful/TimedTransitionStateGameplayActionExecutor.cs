using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Execution;
using Godot;

namespace GameplayActionPlugin.Integration.Stateful;

/// <summary>Timed variant of the generic three-state transition executor.</summary>
[GlobalClass]
public partial class TimedTransitionStateGameplayActionExecutor
    : TransitionStateGameplayActionExecutor
{
    private readonly TimedExecution _timedExecution = new();
    private GameplayActionContext? _timedContext;

    /// <summary>Gets or sets the default authoritative running duration in seconds.</summary>
    [Export]
    public float Duration { get; set; }

    /// <summary>Gets or sets how often authoritative linear progress corrections are published.</summary>
    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    /// <summary>Computes the authoritative duration for one execution.</summary>
    public virtual float ComputeTimedDuration(in GameplayActionContext context) => Duration;

    protected override GameplayActionExecutionResult StartRunning(in GameplayActionContext context)
    {
        TimedExecutionStartResult startResult = _timedExecution.Start(
            context.Component,
            context.ExecutionId,
            ComputeTimedDuration(context),
            CorrectionInterval
        );
        if (startResult == TimedExecutionStartResult.Started)
        {
            _timedContext = context;
            _timedExecution.Expired -= OnTimedExecutionExpired;
            _timedExecution.Expired += OnTimedExecutionExpired;
            return base.StartRunning(context);
        }

        Stateful?.SetState(CancelledState);
        return new GameplayActionExecutionFailed(StartFailureReason(startResult));
    }

    internal override GameplayActionProgressSample? GetPredictionSample(
        in GameplayActionContext context
    ) => TimedExecution.BuildPredictionSample(ComputeTimedDuration(context));

    protected internal override void OnExecutionCompleted(in GameplayActionContext context)
    {
        StopTimer(context.ExecutionId);
        base.OnExecutionCompleted(context);
    }

    protected internal override void OnExecutionCancelled(
        in GameplayActionContext context,
        string reason
    )
    {
        StopTimer(context.ExecutionId);
        base.OnExecutionCancelled(context, reason);
    }

    protected internal override void OnExecutionFailed(
        in GameplayActionContext context,
        string reason
    )
    {
        StopTimer(context.ExecutionId);
        base.OnExecutionFailed(context, reason);
    }

    private void OnTimedExecutionExpired()
    {
        GameplayActionContext? context = _timedContext;
        _timedContext = null;
        _timedExecution.Expired -= OnTimedExecutionExpired;
        context?.CompleteExecution();
    }

    private void StopTimer(ulong executionId)
    {
        if (_timedContext?.ExecutionId == executionId)
        {
            _timedContext = null;
            _timedExecution.Expired -= OnTimedExecutionExpired;
        }

        _timedExecution.Stop(executionId);
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
