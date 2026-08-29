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

    /// <summary>Gets whether an execution ends when the interactor stops being there.</summary>
    public virtual bool RequiresInteractorPresence => true;

    /// <summary>Reports that an execution this executor left running reached its end.</summary>
    /// <param name="context">Context of the execution that ended, carrying its identifier.</param>
    protected internal virtual void OnExecutionCompleted(in InteractionExecutionContext context) { }

    /// <summary>Reports that an execution this executor left running ended without completing.</summary>
    /// <param name="context">Context of the execution that ended, carrying its identifier.</param>
    /// <param name="reason">Reason describing why the execution did not complete.</param>
    protected internal virtual void OnExecutionCancelled(
        in InteractionExecutionContext context,
        string reason
    ) { }

    /// <summary>Reports that an execution this executor left running failed after acceptance.</summary>
    /// <param name="context">Context of the execution that failed.</param>
    /// <param name="reason">Reason describing the failure.</param>
    protected internal virtual void OnExecutionFailed(
        in InteractionExecutionContext context,
        string reason
    ) { }

    /// <summary>Returns the optional initial progress sample a requester may predict locally.</summary>
    /// <remarks>
    /// The hook is intentionally internal and producer-agnostic to the renderer. Timed executors opt in
    /// with a linear sample; generic executors return no prediction and still use the same presentation
    /// record once the authority acknowledges them.
    /// </remarks>
    /// <param name="context">Pure query context for the action being requested.</param>
    /// <returns>An initial sample, or null when no local prediction is available.</returns>
    internal virtual InteractionProgressSample? GetPredictionSample(
        in InteractionContext context
    ) => null;

    /// <summary>Keeps the execution reserved until gameplay ends it, with no deadline.</summary>
    /// <returns>A payload-free running outcome.</returns>
    protected static InteractionExecutionResult Running() => new InteractionExecutionRunning();
}
