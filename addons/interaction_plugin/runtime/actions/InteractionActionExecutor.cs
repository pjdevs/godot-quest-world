using Godot;

namespace QuestWorld.Interaction.Runtime.Actions;

/// <summary>
/// Single owner of the gameplay mutation of one action, called by the authoritative target.
/// </summary>
/// <remarks>
/// Add the node to the target scene and reference it from <see cref="InteractionAction.Executor"/>;
/// nothing is discovered in the tree and an action without executor is a configuration error. The
/// core never broadcasts a command, so exactly one executor runs per accepted action. Availability
/// belongs to rules: this call happens once the action is already allowed and reserved.
/// </remarks>
[GlobalClass]
public abstract partial class InteractionActionExecutor : Node
{
    /// <summary>Performs the gameplay mutation of one accepted action on the authoritative peer.</summary>
    /// <remarks>
    /// Called synchronously by <see cref="Interactive.InteractiveComponent.ExecuteAction"/> once the
    /// target is reserved and coherent. Returning <see cref="InteractionExecutionRunning"/> keeps the
    /// reservation until gameplay completes or cancels the execution.
    /// </remarks>
    /// <param name="context">Interactor, interactive, and action of the accepted command.</param>
    /// <returns>The outcome deciding whether the target keeps the execution reserved.</returns>
    public abstract InteractionExecutionResult Execute(in InteractionExecutionContext context);

    /// <summary>Gets how long an execution started by this executor is expected to last.</summary>
    /// <remarks>
    /// The duration belongs here rather than to the action, because only the executor knows whether
    /// it runs long at all: a duration declared next to an executor that completes instantly would be
    /// a setting that silently does nothing.
    /// <para>
    /// Zero means no deadline rather than instant. An executor that returns
    /// <see cref="InteractionExecutionRunning"/> with no duration keeps its execution reserved until
    /// something ends it: an animation finishing, a dialogue closing, a machine reporting done. The
    /// player walking out of range or releasing a sustained input also ends it, so an open-ended
    /// execution is never able to reserve a target forever.
    /// </para>
    /// <para>
    /// The authoritative target reads this when the executor returns
    /// <see cref="InteractionExecutionRunning"/> without a duration of its own, and completes the
    /// execution itself once it elapses. The owning client reads the same value straight from the
    /// scene to draw a progress bar, which is why it must be readable without running anything.
    /// </para>
    /// <para>
    /// Override it with a computed value when the length is known before the action starts. When it
    /// is only known once the action has started — the clip an executor just played — return
    /// <c>new InteractionExecutionRunning(seconds)</c> instead; the target then follows that value,
    /// and this one remains the estimate a client draws with.
    /// </para>
    /// </remarks>
    public virtual float ExpectedDuration => 0.0f;

    /// <summary>Reports that an execution this executor left running reached its end.</summary>
    /// <remarks>
    /// Called by the authoritative target on the executor that owns the execution, never broadcast:
    /// an executor learns about its own execution without subscribing to a target-level signal and
    /// without filtering out the executions of its siblings. Only an execution that outlived its
    /// <see cref="Execute"/> call is reported here, so an instant action never sees this callback.
    /// </remarks>
    /// <param name="context">Context of the execution that ended, carrying its identifier.</param>
    protected internal virtual void OnExecutionCompleted(in InteractionExecutionContext context) { }

    /// <summary>Reports that an execution this executor left running ended without completing.</summary>
    /// <remarks>
    /// This covers a released input, an interactor leaving range or the tree, and an explicit
    /// gameplay cancellation. The same "called directly, never broadcast" contract as
    /// <see cref="OnExecutionCompleted"/> applies.
    /// </remarks>
    /// <param name="context">Context of the execution that ended, carrying its identifier.</param>
    /// <param name="reason">Reason describing why the execution did not complete.</param>
    protected internal virtual void OnExecutionCancelled(
        in InteractionExecutionContext context,
        string reason
    ) { }
}
