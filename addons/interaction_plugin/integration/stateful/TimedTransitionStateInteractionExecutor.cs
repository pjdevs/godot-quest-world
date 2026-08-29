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
        bool started = _timedExecution.Start(
            context.Interactive,
            context.ExecutionId,
            duration,
            CorrectionInterval
        );
        if (started || !float.IsFinite(duration) || duration <= 0.0f)
        {
            return base.StartRunning(context);
        }

        Stateful?.SetState(CancelledState);
        return new InteractionExecutionFailed("The timed executor is already active.");
    }

    /// <inheritdoc />
    internal override InteractionProgressSample? GetPredictionSample(
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
