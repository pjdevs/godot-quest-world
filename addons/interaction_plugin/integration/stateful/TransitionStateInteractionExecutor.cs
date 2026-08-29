using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Drives state through one generic running interaction without a gameplay script.</summary>
/// <remarks>
/// This is the long counterpart of <see cref="SetStateInteractionExecutor"/>. It applies a running
/// state when the action starts, the target keeps the execution reserved, and it applies the end
/// state once the execution completes. A cancellation restores the state instead, so an interaction
/// the player abandons leaves the world exactly as it found it.
/// <para>
/// This executor deliberately owns no timing. The animation, machine, dialogue, or other gameplay
/// system that owns the work ends the execution with the identifier carried by its context. Use
/// <see cref="TimedTransitionStateInteractionExecutor"/> when elapsed time is the completion policy.
/// </para>
/// <para>
/// Covering the common shape is the point; it is not meant to grow. An action that also plays a
/// sound, grants an item, or advances a quest deserves its own executor, which stays free to reuse
/// the very same three-state pattern.
/// </para>
/// </remarks>
[GlobalClass]
public partial class TransitionStateInteractionExecutor : InteractionActionExecutor
{
    /// <summary>Gets or sets the optional component override holding the state this action drives.</summary>
    [ExportGroup("Overrides")]
    [Export]
    public StatefulComponent? Stateful { get; set; }

    /// <summary>Gets or sets the state applied while the action runs.</summary>
    [Export]
    public StringName RunningState { get; set; } = new(string.Empty);

    /// <summary>Gets or sets the state applied once the action completed.</summary>
    [Export]
    public StringName CompletedState { get; set; } = new(string.Empty);

    /// <summary>Gets or sets the state restored when the action is cancelled.</summary>
    [Export]
    public StringName CancelledState { get; set; } = new(string.Empty);

    /// <summary>Gets or sets whether the interactor must stay in range for the transition to finish.</summary>
    /// <remarks>
    /// This executor serves both usages, which is exactly why it exposes the choice: keep it set for a
    /// channel the player holds — a hack, a search — and clear it for a state the world owns once it
    /// started, the machine that was switched on or the door that finishes opening alone. An action
    /// whose definition declares <c>CancelOnInputReleased</c> stays presence-bound regardless.
    /// </remarks>
    [Export]
    public bool RequiresPresence { get; set; } = true;

    /// <inheritdoc />
    public override bool RequiresInteractorPresence => RequiresPresence;

    /// <inheritdoc />
    /// <summary>Godot callback that reports a missing target or a state outside the schema.</summary>
    public override void _Ready()
    {
        Stateful ??= StatefulComposition.ResolveLocalFrom(this);
        if (Stateful is null)
        {
            GD.PushError($"{GetPath()}: TransitionStateInteractionExecutor requires a Stateful.");
            return;
        }

        foreach (StringName state in new[] { RunningState, CompletedState, CancelledState })
        {
            if (state.IsEmpty)
            {
                GD.PushError(
                    $"{GetPath()}: TransitionStateInteractionExecutor requires its three states."
                );
                continue;
            }

            if (!Stateful.IsStateDeclared(state))
            {
                GD.PushError(
                    $"{GetPath()}: state '{state}' is not declared by the Schema of {Stateful.GetPath()}."
                );
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A running state that cannot be applied is a failure rather than a silent no-op: the rules of
    /// the action are what decide whether it may run, so reaching here and changing nothing means the
    /// target is misconfigured.
    /// </remarks>
    public override InteractionExecutionResult Execute(in InteractionExecutionContext context)
    {
        if (Stateful is null)
        {
            return new InteractionExecutionFailed("This is not connected to anything.");
        }

        return Stateful.SetState(RunningState)
            ? StartRunning(context)
            : new InteractionExecutionFailed("This cannot start.");
    }

    /// <summary>Starts the reserved execution after the running state was applied.</summary>
    /// <remarks>The generic implementation has no deadline; specialized policies may compose here.</remarks>
    protected virtual InteractionExecutionResult StartRunning(
        in InteractionExecutionContext context
    ) => Running();

    /// <inheritdoc />
    protected internal override void OnExecutionCompleted(in InteractionExecutionContext context)
    {
        base.OnExecutionCompleted(context);
        Stateful?.SetState(CompletedState);
    }

    /// <inheritdoc />
    protected internal override void OnExecutionCancelled(
        in InteractionExecutionContext context,
        string reason
    )
    {
        base.OnExecutionCancelled(context, reason);
        Stateful?.SetState(CancelledState);
    }

    /// <inheritdoc />
    protected internal override void OnExecutionFailed(
        in InteractionExecutionContext context,
        string reason
    )
    {
        base.OnExecutionFailed(context, reason);
        Stateful?.SetState(CancelledState);
    }
}
