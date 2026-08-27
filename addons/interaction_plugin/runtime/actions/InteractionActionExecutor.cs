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

    /// <summary>Gets whether an execution of this executor ends when the interactor stops being there.</summary>
    /// <remarks>
    /// Two independent axes were confused until now. An execution can be sustained by the input —
    /// <see cref="InteractionActionDefinition.CancelOnInputReleased"/>, which implies this one — and it
    /// can be sustained by presence: leaving the detection window ends it. The reverse does not hold,
    /// because "stay near the terminal while it downloads" is a channel nobody holds a key for.
    /// <para>
    /// Only the executor knows which of the two it is: a hack is a channel bound to the player, while
    /// a machine that was switched on is a process bound to the world. Clearing the flag hands the
    /// execution to the world at its very start, so the player may walk away, disconnect, or leave the
    /// tree without the door stopping halfway.
    /// </para>
    /// <para>
    /// The default keeps the interactor present, which preserves the behavior of every existing
    /// executor and fails in the direction that is visible: a hack that forgot the flag pins the player
    /// in front of its target, while the opposite default would silently let them wander off mid-hack.
    /// </para>
    /// </remarks>
    public virtual bool RequiresInteractorPresence => true;

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

    /// <summary>Computes how long an execution this executor starts should last.</summary>
    /// <remarks>
    /// This is the single source of every deadline in the system, and it is a query rather than a
    /// declared value so that nothing can author one number and run another. Zero means no deadline:
    /// the reservation is then held until gameplay ends it.
    /// <para>
    /// It is called on the authority through <see cref="RunningForDuration"/>, and on the owning client
    /// alone to predict its progress bar without waiting for the acknowledgement. Both peers therefore
    /// run the same code on the same target, which is why it must stay a pure read: no mutation, no
    /// side effect, no dependency on the outcome of an execution that has not started.
    /// </para>
    /// <para>
    /// Reading state a client does not have — a server-only roll, an unreplicated inventory — is not
    /// forbidden, it is simply a deliberate incoherence: the bar is then wrong for one round trip until
    /// the acknowledgement recalibrates it. A duration important enough to change is usually built from
    /// data every peer already has, so this is a note for the implementer, not a rule of the core.
    /// </para>
    /// </remarks>
    /// <param name="context">Interactor, interactive, and action the duration is computed for.</param>
    /// <returns>Seconds the execution should last, or zero for no deadline.</returns>
    public virtual float ComputeInteractionDuration(in InteractionContext context) => 0.0f;

    // Two ways to stay reserved, and neither carries a number of its own: an executor that wants a
    // deadline puts it in ComputeInteractionDuration, where the owning client can read it too. A
    // duration passed straight through here would be one no peer could predict — and one that could
    // silently disagree with what the same executor answers when asked.

    /// <summary>Keeps the execution reserved for the duration this executor computes.</summary>
    /// <remarks>
    /// The target owns the clock and completes the execution itself once the duration elapses, by the
    /// same path a gameplay completion takes, so the progress a player watches cannot be forged by
    /// holding an input longer. A computed zero is an execution with no deadline, exactly like
    /// <see cref="RunningUntilCompleted"/>.
    /// </remarks>
    /// <param name="context">Context of the execution being started.</param>
    /// <returns>A running outcome carrying the computed deadline.</returns>
    protected InteractionExecutionResult RunningForDuration(in InteractionExecutionContext context)
    {
        InteractionContext query = new(context.Interactor, context.Interactive, context.Action);
        return new InteractionExecutionRunning(Mathf.Max(ComputeInteractionDuration(query), 0.0f));
    }

    /// <summary>Keeps the execution reserved until gameplay ends it, with no deadline.</summary>
    /// <remarks>
    /// Nothing but <c>InteractiveComponent.CompleteExecution</c> or <c>CancelExecution</c> — or the
    /// player leaving, for an execution that requires presence — ends this one. An animation reporting
    /// its own end, a dialogue closing, a machine that finished: the executor holds the identifier it
    /// received and calls back when the world says so. This is also where a length known only once the
    /// action started belongs, being a length no peer could have predicted.
    /// </remarks>
    /// <returns>A running outcome with no deadline.</returns>
    protected static InteractionExecutionResult RunningUntilCompleted()
    {
        return new InteractionExecutionRunning();
    }
}
