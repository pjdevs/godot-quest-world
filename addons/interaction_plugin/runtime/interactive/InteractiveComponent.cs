using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Runtime.Interactive;

[Tool]
[GlobalClass]
public partial class InteractiveComponent : Node
{
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
            UpdateConfigurationWarnings();
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
            UpdateConfigurationWarnings();
        }
    }

    [Export]
    public string DisplayName { get; set; } = "Interact";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

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

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        List<string> warnings = [];
        if (InteractionArea is null)
        {
            warnings.Add("InteractionArea must be assigned.");
        }

        if (InteractionOwner is null)
        {
            warnings.Add("InteractionOwner must be assigned.");
        }
        else if (InteractionOwner is not IInteractionHandler)
        {
            warnings.Add("InteractionOwner must implement IInteractionHandler.");
        }

        return [.. warnings];
    }
#endif

    public override void _Ready()
    {
#if TOOLS
        if (Engine.IsEditorHint())
        {
            return;
        }
#endif

        if (InteractionArea is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionArea.");
        }

        if (InteractionOwner is not IInteractionHandler)
        {
            GD.PushError(
                $"{GetPath()}: InteractiveComponent owner must implement IInteractionHandler."
            );
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
    }

    public InteractionStatus EvaluateStatus(InteractionInteractor interactor)
    {
        if (interactor is null || InteractionArea is null)
        {
            return new InteractionBlocked("Interaction is not configured.");
        }

        if (InteractionOwner is not IInteractionHandler handler)
        {
            return new InteractionBlocked("Interaction has no valid handler.");
        }

        if (
            Stateful is not null
            && Stateful.ActiveInteractor is not null
            && Stateful.ActiveInteractor != interactor
        )
        {
            return new InteractionBlocked("Someone else is using this.");
        }

        if (Stateful is not null && Stateful.State != InteractionState.Idle)
        {
            return Stateful.State == InteractionState.Activated
                ? new InteractionBlocked("This is already activated.")
                : new InteractionBlocked("This is busy.");
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
            isFocused
        );
    }

    public bool StartInteraction(InteractionInteractor interactor)
    {
        if (
            EvaluateStatus(interactor) is not InteractionAllowed
            || InteractionOwner is not IInteractionHandler handler
        )
        {
            return false;
        }

        InteractionContext context = new(interactor, this, InteractionOwner);
        handler.OnStartInteractionInput(context);
        return true;
    }

    public bool EndInteraction(InteractionInteractor interactor, InteractionState nextState)
    {
        return Stateful?.ActiveInteractor == interactor && Stateful.EndInteractionPhase(nextState);
    }

    public bool ReleaseInteractionInput(InteractionInteractor interactor)
    {
        return Stateful?.ReleaseInteractionInput(interactor) ?? false;
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
#if TOOLS
        if (Engine.IsEditorHint())
        {
            return;
        }
#endif

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
