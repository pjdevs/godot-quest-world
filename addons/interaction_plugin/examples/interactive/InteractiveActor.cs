using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Examples.Interactive;

[GlobalClass]
public partial class InteractiveActor : Node3D, IInteractionHandler, IInteractionStateHandler
{
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

    [Export]
    public float ActivationDuration { get; set; } = 1.5f;

    private float _activationElapsed;
    private bool _releaseRequested;

    public int StartCount { get; private set; }

    public int EndCount { get; private set; }

    private InteractiveComponent? _interactive;
    private InteractionStateful? _stateful;

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
    }

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

    public InteractionStatus EvaluateCustomInteractionStatus(in InteractionContext context)
    {
        if (Stateful is null)
        {
            return new InteractionBlocked("Interaction is not configured.");
        }

        return Stateful.State == InteractionState.Activated
            ? new InteractionBlocked("This is already activated.")
            : new InteractionAllowed();
    }

    public void OnStartInteractionInput(in InteractionContext context)
    {
        StartCount++;
        _activationElapsed = 0.0f;
        Interactive?.StartInteractionPhase(context.Interactor);
    }

    public void OnEndInteractionInput(in InteractionContext context)
    {
        EndCount++;
        _releaseRequested = true;
    }

    public void OnInteractionStateChangedAuthority(
        InteractionState oldState,
        InteractionState newState
    ) { }

    public void OnInteractionStateChangedPresentation(
        InteractionState oldState,
        InteractionState newState
    ) { }
}
