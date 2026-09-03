using Godot;
using QuestWorld.Interaction.Runtime.Actions;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Timed authoring variant of the generic three-state transition executor.</summary>
/// <remarks>
/// State mutation remains implemented by <see cref="TransitionStateInteractionExecutor"/>. This
/// specialization composes a <see cref="TimedExecution"/> solely as its completion and progress
/// policy, so the interaction core still receives a payload-free running result.
/// </remarks>
[GlobalClass]
public partial class TimedTransitionStateInteractionExecutor : TransitionStateInteractionExecutor
{
    private readonly TimedExecution _timedExecution = new();

    /// <summary>Gets or sets the default duration in seconds.</summary>
    [Export]
    public float Duration { get; set; }

    /// <summary>Gets or sets the interval between sparse authoritative timing samples.</summary>
    [Export]
    public float CorrectionInterval { get; set; } = 0.5f;

    /// <summary>Computes the duration without mutating gameplay.</summary>
    public virtual float ComputeTimedDuration(in InteractionContext context) => Duration;

    /// <inheritdoc />
    protected override InteractionExecutionResult StartRunning(
        in InteractionExecutionContext context
    )
    {
        InteractionContext query = new(context.Interactor, context.Interactive, context.Action);
        float duration = ComputeTimedDuration(query);
        TimedExecutionStartResult startResult = _timedExecution.Start(
            context.Interactive,
            context.ExecutionId,
            duration,
            CorrectionInterval
        );
        if (startResult == TimedExecutionStartResult.Started)
        {
            return base.StartRunning(context);
        }

        Stateful?.SetState(CancelledState);
        return new InteractionExecutionFailed(
            startResult switch
            {
                TimedExecutionStartResult.AlreadyActive => "The timed executor is already active.",
                TimedExecutionStartResult.InvalidDuration =>
                    "Timed execution duration must be finite and greater than zero.",
                TimedExecutionStartResult.InvalidExecution =>
                    "The timed execution is no longer active.",
                TimedExecutionStartResult.MissingSceneTree =>
                    "The timed execution requires an active scene tree.",
                _ => "The timed execution could not start.",
            }
        );
    }

    /// <inheritdoc />
    internal override InteractionProgressSample? GetInteractionPredictionSample(
        in InteractionContext context
    ) => TimedExecution.BuildPredictionSample(ComputeTimedDuration(context));

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
}
