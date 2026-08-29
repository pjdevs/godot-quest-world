using Godot;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>Executor base for actions whose presentation progresses linearly over time.</summary>
/// <remarks>
/// The timed feature owns its clock here rather than in <see cref="InteractiveComponent"/>. The
/// interaction core only sees a payload-free running result and the normal completion lifecycle. A
/// positive duration emits an initial sample and sparse corrections; a zero duration remains a generic
/// running execution completed by gameplay.
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

    private readonly TimedExecution _timedExecution = new();

    /// <summary>Computes the duration for one action without changing gameplay.</summary>
    /// <param name="context">Pure context of the action being queried.</param>
    /// <returns>Seconds to run, or zero to wait for an external completion.</returns>
    public virtual float ComputeTimedDuration(in InteractionContext context) => Duration;

    /// <summary>Starts a timed or externally completed execution.</summary>
    /// <param name="context">Reserved execution context supplied by the target.</param>
    /// <returns>A generic running result.</returns>
    protected InteractionExecutionResult RunningTimed(in InteractionExecutionContext context)
    {
        InteractionContext query = new(context.Interactor, context.Interactive, context.Action);
        float duration = ComputeTimedDuration(query);
        bool started = _timedExecution.Start(
            context.Interactive,
            context.ExecutionId,
            duration,
            CorrectionInterval
        );
        return started || !float.IsFinite(duration) || duration <= 0.0f
            ? Running()
            : new InteractionExecutionFailed("The timed executor is already active.");
    }

    /// <inheritdoc />
    internal override InteractionProgressSample? GetPredictionSample(in InteractionContext context)
    {
        return TimedExecution.BuildPredictionSample(ComputeTimedDuration(context));
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
}
