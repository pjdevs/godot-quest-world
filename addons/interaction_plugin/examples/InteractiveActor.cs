using Godot;

namespace QuestWorld.Interaction;

public partial class InteractiveActor : Node3D, IInteractionHandler, IInteractionStateHandler
{
    [Export]
    public NodePath InteractivePath { get; set; } = new("Interactive");

    [Export]
    public NodePath StatefulPath { get; set; } = new("Stateful");

    [Export]
    public float ActivationDuration { get; set; } = 1.5f;

    private InteractiveComponent _interactive = null!;
    private InteractionStateful _stateful = null!;
    private float _activationElapsed;
    private bool _releaseRequested;

    public int StartCount { get; private set; }

    public int EndCount { get; private set; }

    public override void _Ready()
    {
        _interactive = GetNodeOrNull<InteractiveComponent>(InteractivePath)!;
        _stateful = GetNodeOrNull<InteractionStateful>(StatefulPath)!;
        _interactive ??= GetNodeOrNull<InteractiveComponent>("Interactive");
        _stateful ??= GetNodeOrNull<InteractionStateful>("Stateful");
        if (_interactive == null || _stateful == null)
        {
            GD.PushError(
                $"{GetPath()}: InteractiveActor example requires Interactive and Stateful children."
            );
            SetProcess(false);
            return;
        }

        _interactive.PromptScene ??= GD.Load<PackedScene>(
            "res://addons/interaction_plugin/scenes/InteractionPrompt.tscn"
        );
        _interactive.IndicationScene ??= GD.Load<PackedScene>(
            "res://addons/interaction_plugin/scenes/InteractionIndicator.tscn"
        );
    }

    public override void _Process(double delta)
    {
        if (_stateful == null || _stateful.State != InteractionState.Activating)
        {
            return;
        }

        if (_releaseRequested)
        {
            _releaseRequested = false;
            _stateful.SetState(InteractionState.Idle);
            return;
        }

        _activationElapsed += (float)delta;
        if (_activationElapsed >= Mathf.Max(ActivationDuration, 0.0f))
        {
            _stateful.EndInteractionPhase(InteractionState.Activated);
        }
    }

    public InteractionStatus EvaluateCustomInteractionStatus(in InteractionContext context)
    {
        return _stateful.State == InteractionState.Activated
            ? new InteractionBlocked("This is already activated.")
            : new InteractionAllowed();
    }

    public void OnStartInteractionInput(in InteractionContext context)
    {
        StartCount++;
        _activationElapsed = 0.0f;
        _stateful.StartInteractionPhase(context.Interactor);
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
