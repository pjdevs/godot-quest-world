using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Examples.Interactive;

/// <summary>Example gameplay owner running a cancellable long activation as a single execution.</summary>
/// <remarks>
/// The action is performed by <see cref="InteractiveActorExecutor"/> and never by a signal
/// subscriber: this node owns the world state and the duration, and reports the end of the execution
/// to its interactive component. Cancellation arrives as a notification, once the core has already
/// released the execution.
/// </remarks>
[GlobalClass]
public partial class InteractiveActor : Node3D
{
    /// <summary>Gets or sets the interactive component owning the execution of the example action.</summary>
    [Export]
    public InteractiveComponent? Interactive
    {
        get => _interactive;
        set
        {
            if (_interactive == value)
            {
                return;
            }

            _interactive = value;
        }
    }

    /// <summary>Gets or sets the state component changed by the example activation.</summary>
    [Export]
    public InteractionStateful? Stateful
    {
        get => _stateful;
        set
        {
            if (_stateful == value)
            {
                return;
            }

            _stateful = value;
        }
    }

    /// <summary>Gets or sets the number of seconds required to complete activation.</summary>
    [Export]
    public float ActivationDuration { get; set; } = 1.5f;

    private float _activationElapsed;

    /// <summary>Gets the number of activations started by the example executor.</summary>
    public int StartCount { get; private set; }

    /// <summary>Gets the number of activations cancelled before completion.</summary>
    public int EndCount { get; private set; }

    private InteractiveComponent? _interactive;
    private InteractionStateful? _stateful;

    /// <summary>Godot callback that validates references and observes execution cancellation.</summary>
    public override void _Ready()
    {
        if (Interactive is null || Stateful is null)
        {
            GD.PushError(
                $"{GetPath()}: InteractiveActor example requires explicit Interactive and Stateful references."
            );
            SetProcess(false);
            return;
        }

        Interactive.InteractionActionCancelled += OnInteractionActionCancelled;
    }

    /// <summary>Starts the long activation and keeps the execution running until it completes.</summary>
    /// <remarks>Called by <see cref="InteractiveActorExecutor"/> on the authoritative peer only.</remarks>
    /// <returns>A running execution, or a failure when the world state refused the activation.</returns>
    public InteractionExecutionResult BeginActivation()
    {
        if (Stateful is null || !Stateful.SetState(InteractionState.Activating))
        {
            return new InteractionExecutionFailed("This cannot start activating.");
        }

        StartCount++;
        _activationElapsed = 0.0f;
        return new InteractionExecutionRunning();
    }

    /// <summary>Godot callback that advances the active server-side example activation.</summary>
    public override void _Process(double delta)
    {
        if (Stateful is null || Stateful.State != InteractionState.Activating)
        {
            return;
        }

        _activationElapsed += (float)delta;
        if (_activationElapsed < Mathf.Max(ActivationDuration, 0.0f))
        {
            return;
        }

        Stateful.SetState(InteractionState.Activated);
        Interactive?.CompleteExecution();
    }

    /// <summary>Godot callback that stops observing execution cancellation.</summary>
    public override void _ExitTree()
    {
        if (Interactive is not null && IsInstanceValid(Interactive))
        {
            Interactive.InteractionActionCancelled -= OnInteractionActionCancelled;
        }
    }

    private void OnInteractionActionCancelled(
        InteractionInteractor interactor,
        InteractionAction action,
        string reason
    )
    {
        EndCount++;
        Stateful?.SetState(InteractionState.Idle);
    }
}
