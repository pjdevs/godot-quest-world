using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;
using QuestWorld.Interaction.Runtime.State;

namespace QuestWorld.Interaction.Runtime.Interactive;

internal readonly record struct InteractionStartResult(
    InteractionInteractor Interactor,
    InteractionAction Action
);

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

internal readonly record struct InteractionReleaseResult(
    InteractionInteractor Interactor,
    InteractionAction Action
);

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
    /// <param name="action">Action of this target that was requested and validated.</param>
    [Signal]
    public delegate void InteractionInputStartedEventHandler(
        InteractionInteractor interactor,
        InteractionAction action
    );

    /// <summary>
    /// Emitted on the authoritative instance when the active interactor releases input or is removed.
    /// </summary>
    /// <param name="interactor">Interactor whose active input was released.</param>
    /// <param name="action">Action that owned the released phase.</param>
    [Signal]
    public delegate void InteractionInputEndedEventHandler(
        InteractionInteractor interactor,
        InteractionAction action
    );

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

    /// <summary>Gets or sets the optional prompt scene instantiated once per presented action.</summary>
    /// <remarks>
    /// The focused target shows one prompt widget per presented action. Stacking and the
    /// target-level frame around them belong to <c>InteractionPresenter</c>.
    /// </remarks>
    [Export]
    public PackedScene? ActionPromptScene { get; set; }

    /// <summary>Gets or sets the optional indication scene shown when this target is allowed.</summary>
    [Export]
    public PackedScene? IndicationScene { get; set; }

    /// <summary>Gets or sets the optional indication scene shown when this target is blocked.</summary>
    [Export]
    public PackedScene? BlockedIndicationScene { get; set; }

    /// <summary>
    /// Gets or sets the explicit actions offered by this target, evaluated in declaration order.
    /// </summary>
    /// <remarks>
    /// Add each <see cref="InteractionAction"/> to the target scene and reference it here. Nothing is
    /// discovered from the tree, and a target without action offers no interaction at all.
    /// </remarks>
    [ExportGroup("Actions")]
    [Export]
    public Godot.Collections.Array<InteractionAction> Actions { get; set; } = new();

    /// <summary>
    /// Gets or sets the ordered gameplay conditions shared by every action of this target.
    /// Evaluation stops at the first hidden or blocked result, before the action rules run.
    /// </summary>
    [Export]
    public Godot.Collections.Array<InteractionRule> TargetRules { get; set; } = new();

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private Area3D? _interactionArea;
    private InteractionInteractor? _activeInteractor;
    private InteractionAction? _activeAction;

    internal InteractionInteractor? ActiveInteractor => _activeInteractor;

    internal InteractionAction? ActiveAction => _activeAction;

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

        if (Actions.Count == 0)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires at least one Action.");
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
    /// Evaluates configuration, reservation, target rules, and action rules for one action.
    /// </summary>
    /// <remarks>
    /// Called repeatedly during local client presentation and again on the server before dispatch.
    /// The evaluation and every rule must be side-effect free. World state influences the result only
    /// through an explicit rule: this component never interprets a state value itself.
    /// </remarks>
    /// <param name="interactor">Interactor for which availability is evaluated.</param>
    /// <param name="action">Action of this target being evaluated.</param>
    /// <returns>The first hidden or blocked result, or allowed when every check succeeds.</returns>
    public InteractionAvailability EvaluateAvailability(
        InteractionInteractor interactor,
        InteractionAction action
    )
    {
        if (
            interactor is null
            || InteractionArea is null
            || InteractionAnchor is null
            || action is null
            || action.Definition is null
            || !Actions.Contains(action)
        )
        {
            return new InteractionBlocked("Interaction is not configured.");
        }

        if (ActiveInteractor is not null && ActiveInteractor != interactor)
        {
            return new InteractionBlocked("Someone else is using this.");
        }

        InteractionContext context = new(interactor, this, action);
        InteractionAvailability targetAvailability = EvaluateRules(TargetRules, context);

        return targetAvailability is InteractionAllowed
            ? EvaluateRules(action.Rules, context)
            : targetAvailability;
    }

    /// <summary>Aggregates the availability of every action into one target-level result.</summary>
    /// <remarks>
    /// Allowed wins over blocked, and blocked over hidden, so a target is presentable as long as one
    /// action is requestable or explained. A target without any action is hidden.
    /// </remarks>
    /// <param name="interactor">Interactor for which availability is evaluated.</param>
    /// <returns>Allowed when one action is allowed, the first blocked result, or hidden.</returns>
    public InteractionAvailability EvaluateAvailability(InteractionInteractor interactor)
    {
        InteractionAvailability aggregate = new InteractionHidden();
        foreach (InteractionAction action in Actions)
        {
            if (action is null)
            {
                continue;
            }

            InteractionAvailability availability = EvaluateAvailability(interactor, action);
            if (availability is InteractionAllowed)
            {
                return availability;
            }

            if (availability is InteractionBlocked && aggregate is InteractionHidden)
            {
                aggregate = availability;
            }
        }

        return aggregate;
    }

    private static InteractionAvailability EvaluateRules(
        Godot.Collections.Array<InteractionRule> rules,
        in InteractionContext context
    )
    {
        foreach (InteractionRule rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            InteractionAvailability availability = rule.Evaluate(context);
            if (availability is not InteractionAllowed)
            {
                return availability;
            }
        }

        return new InteractionAllowed();
    }

    /// <summary>Resolves one action of this target from its stable identifier.</summary>
    /// <remarks>
    /// This is the only supported way for the authoritative peer to turn a requested identifier into
    /// an action. The scene owns the mapping, so a client can never designate an executor or an
    /// action this target does not declare.
    /// </remarks>
    /// <param name="actionId">Stable identifier carried by the interaction command.</param>
    /// <returns>The matching action, or null when this target declares no such action.</returns>
    public InteractionAction? ResolveAction(StringName actionId)
    {
        if (actionId is null || actionId.IsEmpty)
        {
            return null;
        }

        foreach (InteractionAction action in Actions)
        {
            if (action?.Definition is not null && action.Definition.Id == actionId)
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>Resolves the action this target offers for one project input action.</summary>
    /// <remarks>
    /// Hidden actions are ignored, so an input can be reused by mutually exclusive actions such as
    /// <c>open</c> and <c>close</c>. A blocked action is still resolved so the refusal can be
    /// explained instead of the input silently doing nothing.
    /// </remarks>
    /// <param name="interactor">Interactor for which availability is evaluated.</param>
    /// <param name="inputActionName">Project input action pressed by the player.</param>
    /// <returns>The preferred allowed or blocked action, or null when the input offers none.</returns>
    public InteractionAction? ResolveActionForInput(
        InteractionInteractor interactor,
        StringName inputActionName
    )
    {
        InteractionAction? best = null;
        int bestRank = 0;
        foreach (InteractionAction action in Actions)
        {
            if (action?.Definition is null || action.Definition.InputActionName != inputActionName)
            {
                continue;
            }

            if (!TryRankAction(interactor, action, out int rank))
            {
                continue;
            }

            if (best is null || IsBetterCandidate(action, rank, best, bestRank))
            {
                best = action;
                bestRank = rank;
            }
        }

        return best;
    }

    /// <summary>Resolves the action this target offers to a focusing interactor without input.</summary>
    /// <remarks>
    /// A blocked automatic action is still resolved so focusing a target that cannot run it reports
    /// the reason, exactly like a pressed input would.
    /// </remarks>
    /// <param name="interactor">Interactor for which availability is evaluated.</param>
    /// <returns>The preferred automatic action, or null when this target declares none.</returns>
    public InteractionAction? ResolveAutomaticAction(InteractionInteractor interactor)
    {
        InteractionAction? best = null;
        int bestRank = 0;
        foreach (InteractionAction action in Actions)
        {
            if (action?.Definition is null || !action.Automatic)
            {
                continue;
            }

            if (!TryRankAction(interactor, action, out int rank))
            {
                continue;
            }

            if (best is null || IsBetterCandidate(action, rank, best, bestRank))
            {
                best = action;
                bestRank = rank;
            }
        }

        return best;
    }

    private bool TryRankAction(
        InteractionInteractor interactor,
        InteractionAction action,
        out int rank
    )
    {
        rank = EvaluateAvailability(interactor, action) switch
        {
            InteractionAllowed => 0,
            InteractionBlocked => 1,
            InteractionHidden => -1,
        };
        return rank >= 0;
    }

    private static bool IsBetterCandidate(
        InteractionAction action,
        int rank,
        InteractionAction best,
        int bestRank
    )
    {
        if (rank != bestRank)
        {
            return rank < bestRank;
        }

        if (action.Priority != best.Priority)
        {
            return action.Priority > best.Priority;
        }

        return string.CompareOrdinal(
                action.Definition!.Id.ToString(),
                best.Definition!.Id.ToString()
            ) < 0;
    }

    /// <summary>Builds the local presentation snapshot for prompt or indication widgets.</summary>
    /// <remarks>
    /// One entry is produced per presentable action, in declaration order. Hidden actions are
    /// omitted; blocked ones are kept so a prompt can explain them.
    /// </remarks>
    /// <param name="interactor">Interactor viewing this target.</param>
    /// <param name="isFocused">Whether this target currently owns focus.</param>
    /// <returns>A fresh snapshot including the currently evaluated actions.</returns>
    public InteractionTargetPresentation GetPresentation(
        InteractionInteractor interactor,
        bool isFocused
    )
    {
        List<InteractionActionPresentation> presentedActions = new();
        foreach (InteractionAction action in Actions)
        {
            if (
                TryGetActionPresentation(
                    interactor,
                    action,
                    out InteractionActionPresentation presentation
                )
            )
            {
                presentedActions.Add(presentation);
            }
        }

        return new InteractionTargetPresentation(
            this,
            DisplayName,
            Description,
            presentedActions,
            isFocused
        );
    }

    /// <summary>Gets whether this target currently offers at least one presentable action.</summary>
    /// <remarks>
    /// Focus and indication use this instead of a target-wide availability: a target whose actions
    /// are all hidden is ignored entirely rather than presented as unavailable.
    /// </remarks>
    /// <param name="interactor">Interactor viewing this target.</param>
    /// <returns><see langword="true"/> when one action is allowed or blocked.</returns>
    public bool HasVisibleAction(InteractionInteractor interactor)
    {
        foreach (InteractionAction action in Actions)
        {
            if (TryGetActionPresentation(interactor, action, out _))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetActionPresentation(
        InteractionInteractor interactor,
        InteractionAction? action,
        out InteractionActionPresentation presentation
    )
    {
        presentation = default;
        if (action?.Definition is null)
        {
            return false;
        }

        InteractionAvailability availability = EvaluateAvailability(interactor, action);
        if (availability is InteractionHidden)
        {
            return false;
        }

        presentation = new InteractionActionPresentation(
            action.Definition.Id,
            action.Definition.Label,
            action.Definition.Description,
            action.Definition.InputActionName,
            availability,
            action.Automatic
        );
        return true;
    }

    /// <summary>
    /// Revalidates and emits <see cref="InteractionInputStarted"/> for an authoritative request.
    /// </summary>
    /// <remarks>Called by <see cref="InteractionInteractor"/> on the server or offline host.</remarks>
    /// <param name="interactor">Interactor starting the gameplay action.</param>
    /// <param name="action">Action of this target resolved from the requested identifier.</param>
    /// <returns><see langword="true"/> when validation succeeded and the signal was emitted.</returns>
    public bool StartInteraction(InteractionInteractor interactor, InteractionAction action)
    {
        InteractionStartResult? result = StartInteractionCore(interactor, action);
        if (result is null)
        {
            return false;
        }

        DispatchInteractionStart(result.Value);
        return true;
    }

    internal InteractionStartResult? StartInteractionCore(
        InteractionInteractor interactor,
        InteractionAction action
    )
    {
        if (action is null || EvaluateAvailability(interactor, action) is not InteractionAllowed)
        {
            return null;
        }

        return new InteractionStartResult(interactor, action);
    }

    private void DispatchInteractionStart(in InteractionStartResult result)
    {
        EmitSignal(SignalName.InteractionInputStarted, result.Interactor, result.Action);
    }

    /// <summary>Reserves this stateful interaction and moves it to <see cref="InteractionState.Activating"/>.</summary>
    /// <remarks>
    /// Call synchronously from an authoritative <see cref="InteractionInputStarted"/> subscriber for
    /// long-running interactions. Returns false on clients or stateless targets.
    /// </remarks>
    /// <param name="interactor">Interactor to reserve until the phase ends or input is released.</param>
    /// <param name="action">Action received with <see cref="InteractionInputStarted"/>.</param>
    /// <returns><see langword="true"/> when the server started and reserved the phase.</returns>
    public bool StartInteractionPhase(InteractionInteractor interactor, InteractionAction action)
    {
        InteractionPhaseStartResult? result = StartInteractionPhaseCore(interactor, action);
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
            _activeAction = null;
        }

        return false;
    }

    internal InteractionPhaseStartResult? StartInteractionPhaseCore(
        InteractionInteractor interactor,
        InteractionAction action
    )
    {
        if (
            interactor is null
            || action is null
            || !Actions.Contains(action)
            || ActiveInteractor is not null
            || Stateful is null
            || Stateful.State != InteractionState.Idle
            || !Multiplayer.IsServer()
        )
        {
            return null;
        }

        _activeInteractor = interactor;
        _activeAction = action;
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
        _activeAction = null;
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
        if (ActiveInteractor != interactor || _activeAction is null)
        {
            return null;
        }

        InteractionAction action = _activeAction;
        _activeInteractor = null;
        _activeAction = null;
        return new InteractionReleaseResult(interactor, action);
    }

    private void DispatchInteractionRelease(in InteractionReleaseResult result)
    {
        EmitSignal(SignalName.InteractionInputEnded, result.Interactor, result.Action);
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
        _activeAction = null;
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
