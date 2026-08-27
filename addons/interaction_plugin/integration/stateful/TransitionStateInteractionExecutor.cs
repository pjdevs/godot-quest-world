using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful;

/// <summary>Drives a world state through one interaction that lasts, without any gameplay script.</summary>
/// <remarks>
/// This is the long counterpart of <see cref="SetStateInteractionExecutor"/>. It applies a running
/// state when the action starts, the target keeps the execution reserved, and it applies the end
/// state once the execution completes. A cancellation restores the state instead, so an interaction
/// the player abandons leaves the world exactly as it found it.
/// <para>
/// Nothing here measures time. <see cref="Duration"/> is only declared; the authoritative target
/// owns the clock, which is what makes a progress bar impossible to forge by holding an input
/// longer. Leave it at zero when the end comes from something the game already owns — an animation,
/// a machine, a dialogue — and complete the execution from there with the identifier carried by the
/// execution context.
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
    /// <summary>Gets or sets the required component holding the state this action drives.</summary>
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

    /// <summary>Gets or sets how many seconds the running state lasts, or zero for no deadline.</summary>
    /// <remarks>
    /// Left at zero, the running state is held until something else completes the execution, which
    /// is what an object driving its own animation wants.
    /// <para>
    /// The export lives on this executor and not on the core, which reads no declared duration at all:
    /// the value is handed to <c>RunningFor</c> by the code below, so what the Inspector says and what
    /// the target counts down cannot drift apart.
    /// </para>
    /// </remarks>
    [Export]
    public float Duration { get; set; }

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

    /// <summary>Godot callback that reports a missing target or a state outside the schema.</summary>
    public override void _Ready()
    {
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
            ? RunningFor(Duration)
            : new InteractionExecutionFailed("This cannot start.");
    }

    /// <inheritdoc />
    protected internal override void OnExecutionCompleted(in InteractionExecutionContext context)
    {
        Stateful?.SetState(CompletedState);
    }

    /// <inheritdoc />
    protected internal override void OnExecutionCancelled(
        in InteractionExecutionContext context,
        string reason
    )
    {
        Stateful?.SetState(CancelledState);
    }
}
