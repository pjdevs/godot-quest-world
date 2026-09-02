using Godot;
using GameplayActionExecution = QuestWorld.GameplayActions.Runtime.Execution;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>Executor base for actions whose presentation progresses linearly over time.</summary>
/// <remarks>
/// The timed feature owns its clock here rather than in <see cref="InteractiveComponent"/>. The
/// interaction core only sees a payload-free running result and the normal completion lifecycle. A
/// positive finite duration emits an initial sample and sparse corrections. Invalid durations fail;
/// open-ended actions must use or compose a generic executor explicitly.
/// </remarks>
[GlobalClass]
public abstract partial class TimedInteractionExecutor : InteractionActionExecutor
{
    /// <summary>Gets or sets the default duration used by this timed executor.</summary>
    [Export]
    public float Duration { get; set; }

    /// <summary>Gets or sets the interval between authoritative progress corrections.</summary>
    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    private readonly GameplayActionExecution.TimedExecution _timedExecution = new();

    /// <summary>Computes the duration for one action without changing gameplay.</summary>
    /// <param name="context">Pure context of the action being queried.</param>
    /// <returns>A strictly positive finite number of seconds.</returns>
    public virtual float ComputeTimedDuration(in InteractionContext context) => Duration;

    /// <summary>Starts a timed or externally completed execution.</summary>
    /// <param name="context">Reserved execution context supplied by the target.</param>
    /// <returns>A generic running result.</returns>
    protected InteractionExecutionResult RunningTimed(in InteractionExecutionContext context)
    {
        InteractionContext query = new(context.Interactor, context.Interactive, context.Action);
        float duration = ComputeTimedDuration(query);
        GameplayActionExecution.TimedExecutionStartResult startResult =
            _timedExecution.Start(
            context.Interactive.ActionComponent!,
            context.ExecutionId,
            duration,
            CorrectionInterval
        );
        return startResult == GameplayActionExecution.TimedExecutionStartResult.Started
            ? Running()
            : new InteractionExecutionFailed(StartFailureReason(startResult));
    }

    /// <inheritdoc />
    internal override InteractionProgressSample? GetInteractionPredictionSample(
        in InteractionContext context
    )
    {
        GameplayActionExecution.GameplayActionProgressSample? sample =
            GameplayActionExecution.TimedExecution.BuildPredictionSample(
                ComputeTimedDuration(context)
            );
        return sample is GameplayActionExecution.GameplayActionProgressSample value
            ? new InteractionProgressSample(
                value.ProgressBase,
                value.ProgressPerSecond,
                value.Revision
            )
            : null;
    }

    /// <inheritdoc />
    protected internal override void OnExecutionCompleted(in InteractionExecutionContext context)
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionCompleted(context);
    }

    /// <inheritdoc />
    protected internal override void OnExecutionCancelled(
        in InteractionExecutionContext context,
        string reason
    )
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionCancelled(context, reason);
    }

    /// <inheritdoc />
    protected internal override void OnExecutionFailed(
        in InteractionExecutionContext context,
        string reason
    )
    {
        _timedExecution.Stop(context.ExecutionId);
        base.OnExecutionFailed(context, reason);
    }

    private static string StartFailureReason(
        GameplayActionExecution.TimedExecutionStartResult result
    ) =>
        result switch
        {
            GameplayActionExecution.TimedExecutionStartResult.AlreadyActive =>
                "The timed executor is already active.",
            GameplayActionExecution.TimedExecutionStartResult.InvalidDuration =>
                "Timed execution duration must be finite and greater than zero.",
            GameplayActionExecution.TimedExecutionStartResult.InvalidExecution =>
                "The timed execution is no longer active.",
            GameplayActionExecution.TimedExecutionStartResult.MissingSceneTree =>
                "The timed execution requires an active scene tree.",
            _ => "The timed execution could not start.",
        };
}
