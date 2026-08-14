using System.Collections.Generic;
using Godot;

namespace QuestWorld.Interaction;

public partial class InteractiveComponent : Node
{
    [Signal]
    public delegate void InteractiveStatusChangedEventHandler();

    [ExportGroup("Interaction")]
    [Export]
    public NodePath InteractionAreaPath { get; set; } = new();

    [Export]
    public NodePath IndicationAreaPath { get; set; } = new();

    [Export]
    public NodePath InteractionAnchorPath { get; set; } = new();

    [Export]
    public NodePath StatefulPath { get; set; } = new();

    [Export]
    public string DisplayName { get; set; } = "Interact";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    [Export]
    public StringName InteractionActionName { get; set; } = "interact";

    [Export]
    public bool AutomaticInteraction { get; set; }

    [Export]
    public PackedScene PromptScene { get; set; }

    [Export]
    public PackedScene IndicationScene { get; set; }

    [Export]
    public PackedScene BlockedIndicationScene { get; set; }

    [Export]
    public Godot.Collections.Array<InteractionRule> InteractionRules { get; set; } = new();

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private Area3D _interactionArea = null!;
    private Area3D _indicationArea = null!;
    private Node3D _interactionAnchor = null!;
    private InteractionStateful _stateful = null!;
    private Node _interactionOwner = null!;
    private bool _configurationValid;

    public Area3D InteractionArea => _interactionArea;

    public Area3D IndicationArea => _indicationArea;

    public Node3D InteractionAnchor => _interactionAnchor;

    public InteractionStateful Stateful => _stateful;

    public Node InteractionOwner => _interactionOwner;

    public bool IsConfigurationValid => _configurationValid;

    public override void _Ready()
    {
        _interactionArea = ResolveNode<Area3D>(InteractionAreaPath);
        _indicationArea = ResolveNode<Area3D>(IndicationAreaPath);
        _interactionAnchor = ResolveNode<Node3D>(InteractionAnchorPath);
        _stateful = ResolveNode<InteractionStateful>(StatefulPath);
        _interactionArea ??= GetParent()?.GetNodeOrNull<Area3D>("InteractionArea");
        _indicationArea ??= GetParent()?.GetNodeOrNull<Area3D>("IndicationArea");
        _interactionAnchor ??= GetParent()?.GetNodeOrNull<Node3D>("InteractionAnchor");
        _stateful ??= GetParent()?.GetNodeOrNull<InteractionStateful>("Stateful");
        _interactionOwner = FindInteractionOwner();
        _configurationValid = ValidateConfiguration();
        if (!_configurationValid)
        {
            return;
        }

        _interactionArea.BodyEntered += OnInteractionAreaBodyEntered;
        _interactionArea.BodyExited += OnInteractionAreaBodyExited;
        if (_indicationArea != null)
        {
            _indicationArea.BodyEntered += OnIndicationAreaBodyEntered;
            _indicationArea.BodyExited += OnIndicationAreaBodyExited;
        }
    }

    public InteractionStatus EvaluateStatus(InteractionInteractor interactor)
    {
        if (!_configurationValid || interactor == null)
        {
            return new InteractionBlocked("Interaction is not configured.");
        }

        if (_stateful != null && _stateful.ActiveInteractor != null && _stateful.ActiveInteractor != interactor)
        {
            return new InteractionBlocked("Someone else is using this.");
        }

        if (_stateful != null && _stateful.State is InteractionState.Activating or InteractionState.Deactivating)
        {
            return new InteractionBlocked("This is busy.");
        }

        InteractionContext context = new(interactor, this, _interactionOwner);
        foreach (InteractionRule rule in InteractionRules)
        {
            if (rule == null)
            {
                continue;
            }

            InteractionStatus status = rule.Evaluate(context);
            if (status is InteractionBlocked)
            {
                return status;
            }
        }

        if (_interactionOwner is not IInteractionHandler handler)
        {
            return new InteractionBlocked("Interaction has no handler.");
        }

        return handler.EvaluateCustomInteractionStatus(context);
    }

    public InteractionPresentation GetPresentation(InteractionInteractor interactor, bool isFocused)
    {
        return new InteractionPresentation(
            this,
            DisplayName,
            Description,
            InteractionActionName,
            EvaluateStatus(interactor),
            isFocused);
    }

    public bool StartInteraction(InteractionInteractor interactor)
    {
        if (EvaluateStatus(interactor) is not InteractionAllowed || _interactionOwner is not IInteractionHandler handler)
        {
            return false;
        }

        InteractionContext context = new(interactor, this, _interactionOwner);
        handler.OnStartInteractionInput(context);
        return true;
    }

    public bool EndInteraction(InteractionInteractor interactor, InteractionState nextState)
    {
        return _stateful?.ActiveInteractor == interactor && _stateful.EndInteractionPhase(nextState);
    }

    public bool ReleaseInteractionInput(InteractionInteractor interactor)
    {
        return _stateful?.ReleaseInteractionInput(interactor) ?? false;
    }

    public void NotifyStatusChanged()
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
        if (interactor != null)
        {
            _presentInteractors.Add(interactor);
        }
    }

    internal void UnregisterInteractor(InteractionInteractor interactor)
    {
        _presentInteractors.Remove(interactor);
    }

    public Vector3 GetInteractionPosition()
    {
        if (_interactionAnchor != null)
        {
            return _interactionAnchor.GlobalPosition;
        }

        return _interactionOwner is Node3D owner3D ? owner3D.GlobalPosition : Vector3.Zero;
    }

    private void OnInteractionAreaBodyEntered(Node3D body)
    {
        FindInteractors(body, static (interactor, component) => interactor.AddInteractive(component), this);
    }

    private void OnInteractionAreaBodyExited(Node3D body)
    {
        FindInteractors(body, static (interactor, component) => interactor.RemoveInteractive(component), this);
    }

    private void OnIndicationAreaBodyEntered(Node3D body)
    {
        FindInteractors(body, static (interactor, component) => interactor.AddInteractiveIndication(component), this);
    }

    private void OnIndicationAreaBodyExited(Node3D body)
    {
        FindInteractors(body, static (interactor, component) => interactor.RemoveInteractiveIndication(component), this);
    }

    private bool ValidateConfiguration()
    {
        bool valid = true;
        if (_interactionArea == null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionArea.");
            valid = false;
        }

        if (_interactionOwner is not IInteractionHandler)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent owner must implement IInteractionHandler.");
            valid = false;
        }

        return valid;
    }

    private T ResolveNode<T>(NodePath path) where T : Node
    {
        if (path == null || path.IsEmpty)
        {
            return null!;
        }

        T resolved = GetNodeOrNull<T>(path);
        if (resolved != null)
        {
            return resolved;
        }

        return GetParent()?.GetNodeOrNull<T>(path)!;
    }

    private Node FindInteractionOwner()
    {
        Node current = GetParent();
        while (current != null)
        {
            if (current is IInteractionHandler)
            {
                return current;
            }

            current = current.GetParent();
        }

        return null!;
    }

    private static void FindInteractors(
        Node node,
        System.Action<InteractionInteractor, InteractiveComponent> action,
        InteractiveComponent component)
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
        _presentInteractors.RemoveWhere(interactor => !GodotObject.IsInstanceValid(interactor));
    }
}
