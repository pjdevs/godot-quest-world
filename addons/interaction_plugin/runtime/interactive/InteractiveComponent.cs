using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Runtime.Interactive;

[GlobalClass]
public partial class InteractiveComponent : Node
{
    [Signal]
    public delegate void InteractionInputStartedEventHandler(InteractionInteractor interactor);

    [Signal]
    public delegate void InteractionInputEndedEventHandler(InteractionInteractor interactor);

    [Signal]
    public delegate void InteractiveStatusChangedEventHandler();

    [ExportGroup("Interaction")]
    [Export]
    public Area3D? InteractionArea
    {
        get => _interactionArea;
        set
        {
            if (_interactionArea == value)
            {
                return;
            }

            _interactionArea = value;
        }
    }

    [Export]
    public Area3D? IndicationArea { get; set; }

    [Export]
    public Node3D? InteractionAnchor { get; set; }

    [Export]
    public InteractionStateful? Stateful { get; set; }

    [Export]
    public Node? InteractionOwner
    {
        get => _interactionOwner;
        set
        {
            if (_interactionOwner == value)
            {
                return;
            }

            _interactionOwner = value;
        }
    }

    [Export]
    public string DisplayName { get; set; } = "Interact";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Export]
    public string BusyReason { get; set; } = "This is busy.";

    [Export]
    public string ActivatedReason { get; set; } = "This is already activated.";

    [Export]
    public StringName InteractionActionName { get; set; } = "interact";

    [Export]
    public bool AutomaticInteraction { get; set; }

    [Export]
    public PackedScene? PromptScene { get; set; }

    [Export]
    public PackedScene? IndicationScene { get; set; }

    [Export]
    public PackedScene? BlockedIndicationScene { get; set; }

    [Export]
    public Godot.Collections.Array<InteractionRule> InteractionRules { get; set; } = new();

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private Area3D? _interactionArea;
    private Node? _interactionOwner;
    private InteractionInteractor? _activeInteractor;

    internal InteractionInteractor? ActiveInteractor => _activeInteractor;

    public override void _Ready()
    {
        if (InteractionArea is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionArea.");
        }

        if (InteractionOwner is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionOwner.");
        }

        if (InteractionArea is not null)
        {
            InteractionArea.BodyEntered += OnInteractionAreaBodyEntered;
            InteractionArea.BodyExited += OnInteractionAreaBodyExited;
        }

        if (IndicationArea is not null)
        {
            IndicationArea.BodyEntered += OnIndicationAreaBodyEntered;
            IndicationArea.BodyExited += OnIndicationAreaBodyExited;
        }

        if (Stateful is not null)
        {
            Stateful.InteractionStateChanged += OnStatefulInteractionStateChanged;
        }
    }

    public InteractionStatus EvaluateStatus(InteractionInteractor interactor)
    {
        if (interactor is null || InteractionArea is null)
        {
            return new InteractionBlocked("Interaction is not configured.");
        }

        if (InteractionOwner is null)
        {
            return new InteractionBlocked("Interaction is not configured.");
        }

        if (ActiveInteractor is not null && ActiveInteractor != interactor)
        {
            return new InteractionBlocked("Someone else is using this.");
        }

        if (Stateful is not null && Stateful.State != InteractionState.Idle)
        {
            return Stateful.State == InteractionState.Activated
                ? new InteractionBlocked(ActivatedReason)
                : new InteractionBlocked(BusyReason);
        }

        InteractionContext context = new(interactor, this, InteractionOwner);
        foreach (InteractionRule rule in InteractionRules)
        {
            if (rule is null)
            {
                continue;
            }

            InteractionStatus status = rule.Evaluate(context);
            if (status is InteractionBlocked)
            {
                return status;
            }
        }

        return new InteractionAllowed();
    }

    public InteractionPresentation GetPresentation(InteractionInteractor interactor, bool isFocused)
    {
        return new InteractionPresentation(
            this,
            DisplayName,
            Description,
            InteractionActionName,
            EvaluateStatus(interactor),
            isFocused
        );
    }

    public bool StartInteraction(InteractionInteractor interactor)
    {
        if (EvaluateStatus(interactor) is not InteractionAllowed)
        {
            return false;
        }

        EmitSignal(SignalName.InteractionInputStarted, interactor);
        return true;
    }

    public bool StartInteractionPhase(InteractionInteractor interactor)
    {
        if (
            interactor is null
            || ActiveInteractor is not null
            || Stateful is null
            || Stateful.State != InteractionState.Idle
            || !Multiplayer.IsServer()
        )
        {
            return false;
        }

        _activeInteractor = interactor;
        if (Stateful.SetState(InteractionState.Activating))
        {
            return true;
        }

        _activeInteractor = null;
        return false;
    }

    public bool EndInteractionPhase(InteractionState nextState)
    {
        if (ActiveInteractor is null || Stateful is null || !Multiplayer.IsServer())
        {
            return false;
        }

        _activeInteractor = null;
        bool stateChanged = Stateful.SetState(nextState);
        if (!stateChanged)
        {
            NotifyStatusChanged();
        }

        return stateChanged;
    }

    public bool ReleaseInteractionInput(InteractionInteractor interactor)
    {
        if (ActiveInteractor != interactor)
        {
            return false;
        }

        _activeInteractor = null;
        EmitSignal(SignalName.InteractionInputEnded, interactor);
        NotifyStatusChanged();
        return true;
    }

    internal void NotifyStatusChanged()
    {
        EmitSignal(SignalName.InteractiveStatusChanged);
        PurgeInvalidInteractors();
        foreach (InteractionInteractor interactor in _presentInteractors)
        {
            interactor.NotifyInteractiveStatusChanged(this);
        }
    }

    internal void RegisterInteractor(InteractionInteractor interactor)
    {
        _presentInteractors.Add(interactor);
    }

    internal void UnregisterInteractor(InteractionInteractor interactor)
    {
        _presentInteractors.Remove(interactor);
    }

    public Vector3 GetInteractionPosition()
    {
        if (InteractionAnchor is not null)
        {
            return InteractionAnchor.GlobalPosition;
        }

        return InteractionOwner is Node3D owner3D ? owner3D.GlobalPosition : Vector3.Zero;
    }

    public override void _ExitTree()
    {
        if (Stateful is not null && IsInstanceValid(Stateful))
        {
            Stateful.InteractionStateChanged -= OnStatefulInteractionStateChanged;
        }

        _activeInteractor = null;
        PurgeInvalidInteractors();
        foreach (
            InteractionInteractor interactor in new List<InteractionInteractor>(_presentInteractors)
        )
        {
            interactor.RemoveInteractive(this);
            interactor.RemoveInteractiveIndication(this);
        }

        _presentInteractors.Clear();
    }

    private void OnStatefulInteractionStateChanged(int oldState, int newState)
    {
        NotifyStatusChanged();
    }

    private void OnInteractionAreaBodyEntered(Node3D body)
    {
        FindInteractors(
            body,
            static (interactor, component) => interactor.AddInteractive(component),
            this
        );
    }

    private void OnInteractionAreaBodyExited(Node3D body)
    {
        FindInteractors(
            body,
            static (interactor, component) => interactor.RemoveInteractive(component),
            this
        );
    }

    private void OnIndicationAreaBodyEntered(Node3D body)
    {
        FindInteractors(
            body,
            static (interactor, component) => interactor.AddInteractiveIndication(component),
            this
        );
    }

    private void OnIndicationAreaBodyExited(Node3D body)
    {
        FindInteractors(
            body,
            static (interactor, component) => interactor.RemoveInteractiveIndication(component),
            this
        );
    }

    private static void FindInteractors(
        Node node,
        System.Action<InteractionInteractor, InteractiveComponent> action,
        InteractiveComponent component
    )
    {
        if (node is InteractionInteractor direct)
        {
            action(direct, component);
        }

        foreach (Node child in node.GetChildren())
        {
            FindInteractors(child, action, component);
        }
    }

    private void PurgeInvalidInteractors()
    {
        _presentInteractors.RemoveWhere(interactor => !IsInstanceValid(interactor));
    }
}
