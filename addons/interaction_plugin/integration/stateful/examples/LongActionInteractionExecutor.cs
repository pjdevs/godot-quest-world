using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.State;

namespace QuestWorld.Interaction.Integration.Stateful.Examples;

/// <summary>Example executor demonstrating one long, cancellable action owned end to end.</summary>
/// <remarks>
/// This is the long counterpart of <see cref="SetStateInteractionExecutor"/>: instead of applying one
/// state and completing, it applies a running state, keeps the execution reserved for a duration, then
/// applies an end state and completes. A cancellation restores the initial state.
/// <para>
/// Everything the action does lives here, in the single owner of its gameplay mutation. The demo scene
/// root carries no script at all: an executor that only forwards to another script would put the
/// behaviour back outside the one node the core actually calls.
/// </para>
/// <para>
/// The duration is deliberately a plain timer. A real game usually drives the end of a long action from
/// something it already owns — an animation, a machine, a dialogue — and calls
/// <see cref="InteractiveComponent.CompleteExecution"/> from there.
/// </para>
/// </remarks>
[GlobalClass]
public partial class LongActionInteractionExecutor : InteractionActionExecutor
{
    /// <summary>Gets or sets the required component holding the state of the acting object.</summary>
    [Export]
    public StatefulComponent? Stateful { get; set; }

    /// <summary>Gets or sets the state applied while the action runs.</summary>
    [Export]
    public StringName RunningState { get; set; } = new("activating");

    /// <summary>Gets or sets the state applied once the action completed.</summary>
    [Export]
    public StringName CompletedState { get; set; } = new("activated");

    /// <summary>Gets or sets the state restored when the action is cancelled.</summary>
    [Export]
    public StringName CancelledState { get; set; } = new("idle");

    /// <summary>Gets or sets the number of seconds the action stays running.</summary>
    [Export]
    public float Duration { get; set; } = 1.5f;

    /// <summary>Gets the number of executions this node started.</summary>
    public int StartCount { get; private set; }

    /// <summary>Gets the number of executions cancelled before completion.</summary>
    public int CancelCount { get; private set; }

    private InteractiveComponent? _interactive;
    private float _elapsed;

    /// <summary>Godot callback that reports a missing target or an undeclared state.</summary>
    public override void _Ready()
    {
        SetProcess(false);

        if (Stateful is null)
        {
            GD.PushError($"{GetPath()}: LongActionInteractionExecutor requires a Stateful.");
            return;
        }

        foreach (StringName state in new[] { RunningState, CompletedState, CancelledState })
        {
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
    /// The execution stays reserved until the duration elapses on the authoritative peer. The
    /// interactive component is learned from the context rather than looked up in the tree.
    /// </remarks>
    public override InteractionExecutionResult Execute(in InteractionExecutionContext context)
    {
        if (Stateful is null)
        {
            return new InteractionExecutionFailed("This is not connected to anything.");
        }

        if (!Stateful.SetState(RunningState))
        {
            return new InteractionExecutionFailed("This cannot start.");
        }

        Observe(context.Interactive);
        StartCount++;
        _elapsed = 0.0f;
        SetProcess(true);
        return new InteractionExecutionRunning();
    }

    /// <summary>Godot callback that completes the running execution once its duration elapsed.</summary>
    public override void _Process(double delta)
    {
        if (Stateful is null || !Multiplayer.IsServer())
        {
            return;
        }

        _elapsed += (float)delta;
        if (_elapsed < Mathf.Max(Duration, 0.0f))
        {
            return;
        }

        SetProcess(false);
        Stateful.SetState(CompletedState);
        _interactive?.CompleteExecution();
    }

    /// <summary>Godot callback that stops observing the interactive component.</summary>
    public override void _ExitTree()
    {
        Observe(null);
    }

    private void Observe(InteractiveComponent? interactive)
    {
        if (_interactive == interactive)
        {
            return;
        }

        if (_interactive is not null && IsInstanceValid(_interactive))
        {
            _interactive.InteractionActionCancelled -= OnInteractionActionCancelled;
        }

        _interactive = interactive;

        if (_interactive is not null)
        {
            _interactive.InteractionActionCancelled += OnInteractionActionCancelled;
        }
    }

    private void OnInteractionActionCancelled(
        InteractionInteractor interactor,
        InteractionAction action,
        string reason
    )
    {
        // The component notifies every cancellation of the target, including other actions.
        if (action?.Executor != this)
        {
            return;
        }

        SetProcess(false);
        CancelCount++;
        Stateful?.SetState(CancelledState);
    }
}
