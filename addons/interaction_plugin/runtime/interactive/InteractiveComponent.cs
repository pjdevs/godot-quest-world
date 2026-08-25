using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Runtime.Interactive;

internal readonly record struct InteractionStartResult(InteractionInteractor Interactor);

internal readonly record struct InteractionPhaseStartResult(
    InteractionInteractor Interactor,
    InteractionStateful Stateful,
    InteractionState NextState
);

internal readonly record struct InteractionPhaseEndResult(
    InteractionInteractor Interactor,
    InteractionStateful Stateful,
    InteractionState NextState
);

internal readonly record struct InteractionReleaseResult(InteractionInteractor Interactor);

/// <summary>
/// Defines an interactable target, evaluates its rules, and owns any active interaction phase.
/// </summary>
/// <remarks>
/// Add this node beside its gameplay node and assign explicit scene references in the Inspector.
/// Authoritative start, end, and phase mutations run on the server or offline host.
/// </remarks>
[GlobalClass]
public partial class InteractiveComponent : Node
{
    /// <summary>
    /// Emitted on the authoritative instance after start validation succeeds. Gameplay subscribers
    /// start the concrete action here and may synchronously call <see cref="StartInteractionPhase"/>.
    /// </summary>
    /// <param name="interactor">Interactor that requested the interaction.</param>
    [Signal]
    public delegate void InteractionInputStartedEventHandler(InteractionInteractor interactor);

    /// <summary>
    /// Emitted on the authoritative instance when the active interactor releases input or is removed.
    /// </summary>
    /// <param name="interactor">Interactor whose active input was released.</param>
    [Signal]
    public delegate void InteractionInputEndedEventHandler(InteractionInteractor interactor);

    /// <summary>
    /// Emitted on any peer whose visible interaction status may have changed.
    /// </summary>
    [Signal]
    public delegate void InteractiveStatusChangedEventHandler();

    /// <summary>Gets or sets the required area that registers interactors in interaction range.</summary>
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

    /// <summary>Gets or sets the optional wider area used to show interaction indications.</summary>
    [Export]
    public Area3D? IndicationArea { get; set; }

    /// <summary>Gets or sets the required world-space point used for range, focus, and projection.</summary>
    [Export]
    public Node3D? InteractionAnchor { get; set; }

    /// <summary>Gets or sets the optional replicated and persistent state component.</summary>
    [Export]
    public InteractionStateful? Stateful { get; set; }

    /// <summary>Gets or sets the player-facing name used by presentation widgets.</summary>
    [Export]
    public string DisplayName { get; set; } = "Interact";

    /// <summary>Gets or sets optional descriptive text included in presentation snapshots.</summary>
    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Gets or sets the reason shown while the stateful interaction is in a transient state.</summary>
    [Export]
    public string BusyReason { get; set; } = "This is busy.";

    /// <summary>Gets or sets the reason shown after the stateful interaction has been activated.</summary>
    [Export]
    public string ActivatedReason { get; set; } = "This is already activated.";

    /// <summary>Gets or sets the input action displayed by the prompt.</summary>
    [Export]
    public StringName InteractionActionName { get; set; } = "interact";

    /// <summary>Gets or sets whether local focus immediately requests interaction without player input.</summary>
    [Export]
    public bool AutomaticInteraction { get; set; }

    /// <summary>Gets or sets the optional prompt scene shown for the focused target.</summary>
    [Export]
    public PackedScene? PromptScene { get; set; }

    /// <summary>Gets or sets the optional indication scene shown when this target is allowed.</summary>
    [Export]
    public PackedScene? IndicationScene { get; set; }

    /// <summary>Gets or sets the optional indication scene shown when this target is blocked.</summary>
    [Export]
    public PackedScene? BlockedIndicationScene { get; set; }

    /// <summary>
    /// Gets or sets the ordered gameplay conditions. Evaluation stops at the first blocked result.
    /// </summary>
    [Export]
    public Godot.Collections.Array<InteractionRule> InteractionRules { get; set; } = new();

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private Area3D? _interactionArea;
    private InteractionInteractor? _activeInteractor;

    internal InteractionInteractor? ActiveInteractor => _activeInteractor;

    /// <summary>Godot callback that validates configuration and connects area and state signals.</summary>
    public override void _Ready()
    {
        if (InteractionArea is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionArea.");
        }

        if (InteractionAnchor is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires an InteractionAnchor.");
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

    /// <summary>
    /// Evaluates configuration, reservation, state, and gameplay rules for one interactor.
    /// </summary>
    /// <remarks>
    /// Called repeatedly during local client presentation and again on the server before dispatch.
    /// The evaluation and every rule must be side-effect free.
    /// </remarks>
    /// <param name="interactor">Interactor for which availability is evaluated.</param>
    /// <returns>The first blocked status, or an allowed status when every check succeeds.</returns>
    public InteractionStatus EvaluateStatus(InteractionInteractor interactor)
    {
        if (interactor is null || InteractionArea is null || InteractionAnchor is null)
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

        InteractionContext context = new(interactor, this);
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

    /// <summary>Builds the local presentation snapshot for a prompt or indication widget.</summary>
    /// <param name="interactor">Interactor viewing this target.</param>
    /// <param name="isFocused">Whether this target currently owns focus.</param>
    /// <returns>A fresh snapshot including the current evaluated status.</returns>
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

    /// <summary>
    /// Revalidates and emits <see cref="InteractionInputStarted"/> for an authoritative request.
    /// </summary>
    /// <remarks>Called by <see cref="InteractionInteractor"/> on the server or offline host.</remarks>
    /// <param name="interactor">Interactor starting the gameplay action.</param>
    /// <returns><see langword="true"/> when validation succeeded and the signal was emitted.</returns>
    public bool StartInteraction(InteractionInteractor interactor)
    {
        InteractionStartResult? result = StartInteractionCore(interactor);
        if (result is null)
        {
            return false;
        }

        DispatchInteractionStart(result.Value);
        return true;
    }

    internal InteractionStartResult? StartInteractionCore(InteractionInteractor interactor)
    {
        if (EvaluateStatus(interactor) is not InteractionAllowed)
        {
            return null;
        }

        return new InteractionStartResult(interactor);
    }

    private void DispatchInteractionStart(in InteractionStartResult result)
    {
        EmitSignal(SignalName.InteractionInputStarted, result.Interactor);
    }

    /// <summary>Reserves this stateful interaction and moves it to <see cref="InteractionState.Activating"/>.</summary>
    /// <remarks>
    /// Call synchronously from an authoritative <see cref="InteractionInputStarted"/> subscriber for
    /// long-running interactions. Returns false on clients or stateless targets.
    /// </remarks>
    /// <param name="interactor">Interactor to reserve until the phase ends or input is released.</param>
    /// <returns><see langword="true"/> when the server started and reserved the phase.</returns>
    public bool StartInteractionPhase(InteractionInteractor interactor)
    {
        InteractionPhaseStartResult? result = StartInteractionPhaseCore(interactor);
        if (result is null)
        {
            return false;
        }

        if (result.Value.Stateful.SetState(result.Value.NextState))
        {
            return true;
        }

        if (ActiveInteractor == result.Value.Interactor)
        {
            _activeInteractor = null;
        }

        return false;
    }

    internal InteractionPhaseStartResult? StartInteractionPhaseCore(
        InteractionInteractor interactor
    )
    {
        if (
            interactor is null
            || ActiveInteractor is not null
            || Stateful is null
            || Stateful.State != InteractionState.Idle
            || !Multiplayer.IsServer()
        )
        {
            return null;
        }

        _activeInteractor = interactor;
        return new InteractionPhaseStartResult(interactor, Stateful, InteractionState.Activating);
    }

    /// <summary>Completes the active phase, releases its interactor, and applies the next state.</summary>
    /// <remarks>Call from authoritative gameplay code on the server or offline host.</remarks>
    /// <param name="nextState">State to apply after the phase completes.</param>
    /// <returns><see langword="true"/> when the state changed successfully.</returns>
    public bool EndInteractionPhase(InteractionState nextState)
    {
        InteractionPhaseEndResult? result = EndInteractionPhaseCore(nextState);
        if (result is null)
        {
            return false;
        }

        bool stateChanged = result.Value.Stateful.SetState(result.Value.NextState);
        if (!stateChanged)
        {
            NotifyStatusChanged();
        }

        return stateChanged;
    }

    internal InteractionPhaseEndResult? EndInteractionPhaseCore(InteractionState nextState)
    {
        if (ActiveInteractor is null || Stateful is null || !Multiplayer.IsServer())
        {
            return null;
        }

        InteractionInteractor interactor = ActiveInteractor;
        _activeInteractor = null;
        return new InteractionPhaseEndResult(interactor, Stateful, nextState);
    }

    /// <summary>Releases matching active input and emits <see cref="InteractionInputEnded"/>.</summary>
    /// <remarks>
    /// Called by the authoritative interactor when input ends, range is lost, or the interactor exits.
    /// This is distinct from completing a phase with <see cref="EndInteractionPhase"/>.
    /// </remarks>
    /// <param name="interactor">Interactor expected to own the active phase.</param>
    /// <returns><see langword="true"/> when matching active input was released.</returns>
    public bool ReleaseInteractionInput(InteractionInteractor interactor)
    {
        InteractionReleaseResult? result = ReleaseInteractionInputCore(interactor);
        if (result is null)
        {
            return false;
        }

        DispatchInteractionRelease(result.Value);
        return true;
    }

    internal InteractionReleaseResult? ReleaseInteractionInputCore(InteractionInteractor interactor)
    {
        if (ActiveInteractor != interactor)
        {
            return null;
        }

        _activeInteractor = null;
        return new InteractionReleaseResult(interactor);
    }

    private void DispatchInteractionRelease(in InteractionReleaseResult result)
    {
        EmitSignal(SignalName.InteractionInputEnded, result.Interactor);
        NotifyStatusChanged();
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

    /// <summary>Gets the world-space point used by focus scoring, range validation, and UI projection.</summary>
    /// <returns>The configured anchor position, or <see cref="Vector3.Zero"/> before configuration.</returns>
    public Vector3 GetInteractionPosition()
    {
        return InteractionAnchor?.GlobalPosition ?? Vector3.Zero;
    }

    /// <summary>Godot callback that disconnects state and interactor registrations.</summary>
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
