using Godot;
using QuestWorld.Interaction.Runtime.Interactive;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Examples.Interactive;

[GlobalClass]
public partial class InteractiveActor : Node3D
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

        Interactive.InteractionInputStarted += OnInteractionInputStarted;
        Interactive.InteractionInputEnded += OnInteractionInputEnded;
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

    public override void _ExitTree()
    {
        if (Interactive is not null && IsInstanceValid(Interactive))
        {
            Interactive.InteractionInputStarted -= OnInteractionInputStarted;
            Interactive.InteractionInputEnded -= OnInteractionInputEnded;
        }
    }

    private void OnInteractionInputStarted(InteractionInteractor interactor)
    {
        StartCount++;
        _activationElapsed = 0.0f;
        Interactive?.StartInteractionPhase(interactor);
    }

    private void OnInteractionInputEnded(InteractionInteractor interactor)
    {
        EndCount++;
        _releaseRequested = true;
    }
}
