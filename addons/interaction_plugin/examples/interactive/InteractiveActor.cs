using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Examples.Interactive;

/// <summary>Example interactive owner that demonstrates a cancellable long activation phase.</summary>
[GlobalClass]
public partial class InteractiveActor : Node3D
{
    /// <summary>Gets or sets the interactive component that dispatches input and owns the phase.</summary>
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

    /// <summary>Gets or sets the state component changed by the example phase.</summary>
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
    private bool _releaseRequested;

    /// <summary>Gets the number of authoritative start signals observed by this example.</summary>
    public int StartCount { get; private set; }

    /// <summary>Gets the number of authoritative end-input signals observed by this example.</summary>
    public int EndCount { get; private set; }

    private InteractiveComponent? _interactive;
    private InteractionStateful? _stateful;

    /// <summary>Godot callback that validates references and subscribes to authoritative input signals.</summary>
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

        Interactive.InteractionInputStarted += OnInteractionInputStarted;
        Interactive.InteractionInputEnded += OnInteractionInputEnded;
    }

    /// <summary>Godot callback that advances or cancels the active server-side example phase.</summary>
    public override void _Process(double delta)
    {
        if (Stateful is null || Stateful.State != InteractionState.Activating)
        {
            return;
        }

        if (_releaseRequested)
        {
            _releaseRequested = false;
            Stateful.SetState(InteractionState.Idle);
            return;
        }

        _activationElapsed += (float)delta;
        if (_activationElapsed >= Mathf.Max(ActivationDuration, 0.0f))
        {
            Interactive?.EndInteractionPhase(InteractionState.Activated);
        }
    }

    /// <summary>Godot callback that disconnects authoritative input signals.</summary>
    public override void _ExitTree()
    {
        if (Interactive is not null && IsInstanceValid(Interactive))
        {
            Interactive.InteractionInputStarted -= OnInteractionInputStarted;
            Interactive.InteractionInputEnded -= OnInteractionInputEnded;
        }
    }

    private void OnInteractionInputStarted(
        InteractionInteractor interactor,
        InteractionAction action
    )
    {
        StartCount++;
        _activationElapsed = 0.0f;
        Interactive?.StartInteractionPhase(interactor, action);
    }

    private void OnInteractionInputEnded(InteractionInteractor interactor, InteractionAction action)
    {
        EndCount++;
        _releaseRequested = true;
    }
}
