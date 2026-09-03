using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Rules;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Runtime.Interactive;

/// <summary>The hold progression the interactor reports, read once per presentation snapshot.</summary>
/// <remarks>
/// The hold is attributed per action rather than per target so a widget never has to filter by action
/// identifier. It answers a different question from execution presentation: it is a local
/// <b>selection</b> between actions sharing an input, while execution presentation belongs to the
/// interactive target.
/// </remarks>
/// <param name="HeldInput">Input being held, or null when no hold is in progress.</param>
/// <param name="HoldElapsed">Seconds that input has been held.</param>
internal readonly record struct InteractionProgress(StringName? HeldInput, float HoldElapsed)
{
    /// <summary>Gets the hold progress one action should show, or null when it shows none.</summary>
    /// <remarks>
    /// Normalised on the threshold of <b>that</b> action and not on the longest one of the input, so a
    /// bar drawn around the key reaches one when the action it belongs to becomes selectable — the
    /// shorter of two actions sharing an input would otherwise never fill.
    /// <para>
    /// An action that asks for no hold never reports progress, whatever the player is holding: it is
    /// selected by the press or by the release, and a bar filling towards a threshold it does not have
    /// would promise something else.
    /// </para>
    /// </remarks>
    public float? HoldOf(InteractionActionDefinition definition) =>
        Holds(definition) ? Mathf.Clamp(HoldElapsed / definition.HoldThreshold, 0.0f, 1.0f) : null;

    /// <summary>Gets the seconds one action has been held for, or null when it is not being held.</summary>
    /// <remarks>
    /// The raw duration next to the normalised one, because a widget showing a countdown cannot rebuild
    /// it: the threshold it would divide by is not part of the presentation.
    /// </remarks>
    public float? HoldElapsedOf(InteractionActionDefinition definition) =>
        Holds(definition) ? HoldElapsed : null;

    private bool Holds(InteractionActionDefinition definition) =>
        HeldInput is not null
        && definition.HoldThreshold > 0.0f
        && definition.InputActionName == HeldInput;
}

/// <summary>
/// Defines an interactable target, evaluates its rules, and owns the execution of its actions.
/// </summary>
/// <remarks>
/// Add this node beside its gameplay node and assign explicit scene references in the Inspector.
/// Availability evaluation is pure and runs anywhere; reserving, executing, completing, and
/// cancelling run on the server or offline host.
/// </remarks>
[GlobalClass]
public partial class InteractiveComponent : Node
{
    /// <summary>Emitted on the authoritative instance once an executor has accepted an action.</summary>
    /// <remarks>
    /// Every notification of this component reports something that already happened. None of them is
    /// a command: the gameplay mutation belongs to <see cref="InteractionAction.Executor"/>, so
    /// connecting any number of observers never runs the action more than once. A started action is
    /// always followed by exactly one completion, cancellation, or failure.
    /// </remarks>
    /// <param name="interactor">Interactor that requested the action.</param>
    /// <param name="action">Action whose executor accepted the command.</param>
    [Signal]
    public delegate void InteractionActionStartedEventHandler(
        InteractionInteractor interactor,
        InteractionAction action
    );

    /// <summary>Emitted on the authoritative instance when a started action reaches its end.</summary>
    /// <param name="interactor">Interactor that requested the action.</param>
    /// <param name="action">Action that completed.</param>
    [Signal]
    public delegate void InteractionActionCompletedEventHandler(
        InteractionInteractor interactor,
        InteractionAction action
    );

    /// <summary>Emitted on the authoritative instance when a started action ends without completing.</summary>
    /// <remarks>
    /// This covers a released input, an interactor leaving range, and an explicit gameplay cancellation.
    /// </remarks>
    /// <param name="interactor">Interactor that requested the action.</param>
    /// <param name="action">Action that was cancelled.</param>
    /// <param name="reason">Reason describing why the action did not complete.</param>
    [Signal]
    public delegate void InteractionActionCancelledEventHandler(
        InteractionInteractor interactor,
        InteractionAction action,
        string reason
    );

    /// <summary>Emitted on the authoritative instance when a started action fails.</summary>
    /// <param name="interactor">Interactor that requested the action.</param>
    /// <param name="action">Action that failed after it was accepted.</param>
    /// <param name="reason">Reason describing the failure.</param>
    [Signal]
    public delegate void InteractionActionFailedEventHandler(
        InteractionInteractor interactor,
        InteractionAction action,
        string reason
    );

    /// <summary>Emitted on the authoritative instance when an action was refused before starting.</summary>
    /// <remarks>
    /// A refused action never runs its executor, so this notification is never preceded by
    /// <see cref="InteractionActionStarted"/>.
    /// </remarks>
    /// <param name="interactor">Interactor that requested the action.</param>
    /// <param name="action">Action that was refused.</param>
    /// <param name="reason">Reason describing the refusal.</param>
    [Signal]
    public delegate void InteractionActionRejectedEventHandler(
        InteractionInteractor interactor,
        InteractionAction action,
        string reason
    );

    /// <summary>
    /// Emitted on any peer whose visible interaction status may have changed.
    /// </summary>
    [Signal]
    public delegate void InteractiveStatusChangedEventHandler();

    /// <summary>Emitted when this peer's visible execution presentation changes.</summary>
    /// <remarks>
    /// It covers slot creation/removal and published or synchronized progress changes. Derived local
    /// progress is pulled by consumers and does not emit once per frame.
    /// </remarks>
    /// <param name="actionId">Action whose execution presentation changed.</param>
    [Signal]
    public delegate void ExecutionPresentationChangedEventHandler(StringName actionId);

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

    /// <summary>Gets or sets the distance at which this target may be interacted with, or zero.</summary>
    /// <remarks>
    /// Only a detector that decides range per target reads this — the proximity one. Zero means "use
    /// the detector's default", so an object that has no opinion authors nothing. A target that wants a
    /// <b>shape</b> rather than a radius does not fiddle with this: it uses the area detector, which is
    /// made for that. The choice is made per scene and even per interactor.
    /// </remarks>
    [Export]
    public float InteractionRadius { get; set; }

    /// <summary>Gets or sets the distance at which this target is worth indicating, or zero.</summary>
    /// <remarks>Same contract as <see cref="InteractionRadius"/>, for the wider tier.</remarks>
    [Export]
    public float IndicationRadius { get; set; }

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

    /// <summary>
    /// Gets or sets the explicit actions offered by this target, evaluated in declaration order.
    /// </summary>
    /// <remarks>
    /// Add each <see cref="InteractionAction"/> to the target scene and reference it here. Nothing is
    /// discovered from the tree, and a target without action offers no interaction at all.
    /// </remarks>
    [ExportGroup("Actions")]
    [Export]
    public GameplayActionComponent? ActionComponent { get; set; }

    [Export]
    public Godot.Collections.Array<InteractionAction> Actions { get; set; } = new();

    /// <summary>
    /// Gets or sets the ordered gameplay conditions shared by every action of this target.
    /// Evaluation stops at the first hidden or blocked result, before the action rules run.
    /// </summary>
    [Export]
    public Godot.Collections.Array<InteractionRule> TargetRules { get; set; } = new();

    private const string NotConfiguredReason = "Interaction is not configured.";
    private const string AlreadyRunningReason = "This is already in use.";
    private const string SomeoneElseReason = "Someone else is using this.";

    // Every target currently in the tree, for the detectors whose source is not an overlap event. A
    // plain list rather than a Godot group: GetNodesInGroup allocates on every call, and a detector
    // walks this once per frame. It stays retranscriptible in GDExtension, which a static of the
    // plugin trivially is.
    private static readonly List<InteractiveComponent> _registered = new();

    // The reverse of the two area properties, because a physics query answers with a collider and a
    // cast detector resolves every hit it reports, every frame. Keyed by instance id rather than by
    // the object so a freed area still resolves to a removable entry, and filled at registration
    // like the signals the target connects to those very areas: swapping one at runtime is already
    // outside the contract.
    private static readonly Dictionary<ulong, InteractiveComponent> _areaOwners = new();
    private static readonly Dictionary<ulong, InteractiveComponent> _actionComponentOwners = new();

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private readonly HashSet<InteractionInteractor> _interactionOverlaps = new();
    private readonly HashSet<InteractionInteractor> _indicationOverlaps = new();
    private readonly List<InteractionInteractor> _overlapBuffer = new();
    private Area3D? _interactionArea;

    internal bool HasActiveExecution =>
        ActionComponent?.TryGetFirstActiveExecution(out _, out _, out _) == true;

    internal InteractionInteractor? ActiveInteractor
    {
        get
        {
            if (
                ActionComponent?.TryGetFirstActiveExecution(
                    out _,
                    out Node? instigator,
                    out Node? requester
                ) != true
            )
            {
                return null;
            }

            return ResolveInteractor(requester) ?? instigator as InteractionInteractor;
        }
    }

    internal InteractionAction? ActiveAction =>
        ActionComponent?.TryGetFirstActiveExecution(out GameplayAction? action, out _, out _)
        == true
            ? action as InteractionAction
            : null;

    /// <summary>Gets whether this peer runs the authoritative half of an interaction.</summary>
    /// <remarks>
    /// Offline counts as authoritative: a peerless game is its own server. Asking the multiplayer API
    /// for an id it does not have only pushes an error and answers no, which would make every
    /// authoritative path refuse itself outside a session.
    /// </remarks>
    /// <summary>Gets every target currently in the tree, in registration order.</summary>
    internal static IReadOnlyList<InteractiveComponent> Registered => _registered;

    /// <summary>Finds the target owning one of the areas a physics query returned.</summary>
    /// <param name="area">Collider reported by a cast or an overlap query.</param>
    /// <returns>The owning target, or null when the area belongs to something else.</returns>
    internal static InteractiveComponent? FindByArea(GodotObject? area)
    {
        return
            area is not null
            && _areaOwners.TryGetValue(area.GetInstanceId(), out InteractiveComponent? owner)
            && IsInstanceValid(owner)
            ? owner
            : null;
    }

    internal static InteractiveComponent? FindByActionComponent(GameplayActionComponent? component)
    {
        return
            component is not null
            && _actionComponentOwners.TryGetValue(
                component.GetInstanceId(),
                out InteractiveComponent? owner
            )
            && IsInstanceValid(owner)
            ? owner
            : null;
    }

    /// <summary>Godot callback that joins the registry the sourceless detectors read.</summary>
    public override void _EnterTree()
    {
        if (!_registered.Contains(this))
        {
            _registered.Add(this);
            IndexArea(InteractionArea);
            IndexArea(IndicationArea);
        }
    }

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

        if (ActionComponent is null)
        {
            GD.PushError($"{GetPath()}: InteractiveComponent requires a GameplayActionComponent.");
        }

        foreach (InteractionAction action in Actions)
        {
            PrepareAction(action);
        }

        ConnectActionComponent();

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

    private void PrepareAction(InteractionAction? action)
    {
        if (action is not null)
        {
            action.PrepareForInteractive(this, TargetRules);
        }
    }

    private void ConnectActionComponent()
    {
        if (ActionComponent is null)
        {
            return;
        }

        _actionComponentOwners[ActionComponent.GetInstanceId()] = this;
        ActionComponent.GameplayActionStarted += OnGameplayActionStarted;
        ActionComponent.GameplayActionCompleted += OnGameplayActionCompleted;
        ActionComponent.GameplayActionCancelled += OnGameplayActionCancelled;
        ActionComponent.GameplayActionFailed += OnGameplayActionFailed;
        ActionComponent.GameplayActionRejected += OnGameplayActionRejected;
        ActionComponent.ExecutionPresentationChanged += OnExecutionPresentationChanged;
    }

    private static InteractionInteractor? ResolveInteractor(Node? requester) =>
        InteractionInteractor.FindByRunner(requester as GameplayActionRunner);

    private void OnGameplayActionStarted(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester
    )
    {
        if (
            action is InteractionAction interactionAction
            && ResolveInteractor(requester) is { } interactor
        )
        {
            EmitSignal(SignalName.InteractionActionStarted, interactor, interactionAction);
        }
    }

    private void OnGameplayActionCompleted(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester
    )
    {
        if (
            action is InteractionAction interactionAction
            && ResolveInteractor(requester) is { } interactor
        )
        {
            EmitSignal(SignalName.InteractionActionCompleted, interactor, interactionAction);
        }
    }

    private void OnGameplayActionCancelled(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester,
        string reason
    )
    {
        if (
            action is InteractionAction interactionAction
            && ResolveInteractor(requester) is { } interactor
        )
        {
            EmitSignal(
                SignalName.InteractionActionCancelled,
                interactor,
                interactionAction,
                reason
            );
        }
    }

    private void OnGameplayActionFailed(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester,
        string reason
    )
    {
        if (
            action is InteractionAction interactionAction
            && ResolveInteractor(requester) is { } interactor
        )
        {
            EmitSignal(SignalName.InteractionActionFailed, interactor, interactionAction, reason);
        }
    }

    private void OnGameplayActionRejected(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester,
        string reason
    )
    {
        if (
            action is InteractionAction interactionAction
            && ResolveInteractor(requester) is { } interactor
        )
        {
            EmitSignal(
                SignalName.InteractionActionRejected,
                interactor,
                interactionAction,
                AdaptRejectionReason(interactor, interactionAction, reason)
            );
        }
    }

    internal string AdaptRejectionReason(
        InteractionInteractor interactor,
        InteractionAction action,
        string reason
    )
    {
        if (reason == GameplayActionAvailabilityExtensions.UnavailableReason)
        {
            return InteractionAvailabilityExtensions.UnavailableReason;
        }

        if (reason != GameplayActionComponent.AlreadyRunningReason || ActionComponent is null)
        {
            return reason;
        }

        bool requestedByInteractor =
            interactor.Runner is not null
            && ActionComponent.IsConcurrencyGroupExecutingByRequester(
                action.GetHostConcurrencyGroup(),
                interactor.Runner
            );
        return requestedByInteractor ? AlreadyRunningReason : SomeoneElseReason;
    }

    private void OnExecutionPresentationChanged(StringName actionId) =>
        EmitSignal(SignalName.ExecutionPresentationChanged, actionId);

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
            || action.InteractionDefinition is null
            || action.Executor is null
            || !Actions.Contains(action)
            || ActionComponent is null
            || action.Component != ActionComponent
        )
        {
            return new InteractionBlocked(NotConfiguredReason);
        }

        PrepareAction(action);
        InteractionAvailability actionAvailability = ActionComponent
            .EvaluateAction(
                action.InteractionDefinition.Id,
                interactor,
                interactor.Runner,
                GameplayActionInvocationKind.PlayerRequest
            )
            .ToInteractionAvailability();
        if (actionAvailability is not InteractionAllowed)
        {
            return actionAvailability;
        }

        // Concurrency is evaluated last, after the rules. An action the rules already hid stays
        // hidden instead of surfacing as blocked just because a sibling is running, and an action
        // the rules already explained keeps its own reason.
        //
        // A reserved group blocks the action for everybody, its own interactor included. Staying
        // allowed for the owner would make a prompt claim an action the target would immediately
        // refuse; blocked keeps the action presented, with the reason, which is what a prompt needs.
        if (
            ActionComponent.IsActionExecuting(action.InteractionDefinition.Id)
            || ActionComponent.IsConcurrencyGroupExecuting(action.GetHostConcurrencyGroup())
        )
        {
            bool requestedByInteractor =
                interactor.Runner is not null
                && ActionComponent.IsConcurrencyGroupExecutingByRequester(
                    action.GetHostConcurrencyGroup(),
                    interactor.Runner
                );
            return new InteractionBlocked(
                requestedByInteractor ? AlreadyRunningReason : SomeoneElseReason
            );
        }

        return new InteractionAllowed();
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
            if (
                action?.InteractionDefinition is not null
                && action.InteractionDefinition.Id == actionId
            )
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
    /// <param name="heldSeconds">
    /// How long the input has been held, which excludes the actions asking for a longer hold. The
    /// default considers every action, since a target whose actions declare no threshold resolves
    /// the same way whatever the gesture.
    /// </param>
    /// <returns>The preferred allowed or blocked action, or null when the input offers none.</returns>
    public InteractionAction? ResolveActionForInput(
        InteractionInteractor interactor,
        StringName inputActionName,
        float heldSeconds = float.MaxValue
    )
    {
        InteractionAction? best = null;
        int bestRank = 0;
        foreach (InteractionAction action in Actions)
        {
            if (
                action?.InteractionDefinition is null
                || action.InteractionDefinition.InputActionName != inputActionName
            )
            {
                continue;
            }

            if (action.InteractionDefinition.HoldThreshold > heldSeconds)
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

    /// <summary>Gets the longest hold this target asks for on one input.</summary>
    /// <remarks>
    /// A pure query used by the local gesture layer to decide whether pressing the input selects an
    /// action immediately or starts a hold. Hidden actions are ignored, so a threshold that cannot
    /// currently be reached never makes the player wait for nothing.
    /// </remarks>
    /// <param name="interactor">Interactor for which availability is evaluated.</param>
    /// <param name="inputActionName">Project input action pressed by the player.</param>
    /// <returns>The highest threshold in seconds, or zero when no action asks for a hold.</returns>
    public float GetLongestHoldThreshold(
        InteractionInteractor interactor,
        StringName inputActionName
    )
    {
        float longest = 0.0f;
        foreach (InteractionAction action in Actions)
        {
            if (
                action?.InteractionDefinition is null
                || action.InteractionDefinition.InputActionName != inputActionName
            )
            {
                continue;
            }

            if (
                action.InteractionDefinition.HoldThreshold > longest
                && TryRankAction(interactor, action, out _)
            )
            {
                longest = action.InteractionDefinition.HoldThreshold;
            }
        }

        return longest;
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
        // A hold is a deliberate selection, so the action the player held for wins over one asking
        // for no hold. Without this, holding could never reach the action the threshold exists for.
        float threshold = action.InteractionDefinition!.HoldThreshold;
        float bestThreshold = best.InteractionDefinition!.HoldThreshold;
        if (threshold != bestThreshold)
        {
            return threshold > bestThreshold;
        }

        if (rank != bestRank)
        {
            return rank < bestRank;
        }

        if (action.Priority != best.Priority)
        {
            return action.Priority > best.Priority;
        }

        return string.CompareOrdinal(
                action.InteractionDefinition!.Id.ToString(),
                best.InteractionDefinition!.Id.ToString()
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
        // Read once for the whole snapshot: the interactor holds one input at a time, and every action
        // of this target reads the same hold state.
        InteractionProgress progress = new(
            interactor.TryGetGestureElapsed(out StringName heldInput, out float holdElapsed)
                ? heldInput
                : null,
            holdElapsed
        );

        List<InteractionActionPresentation> presentedActions = new();
        foreach (InteractionAction action in Actions)
        {
            if (
                TryGetActionPresentation(
                    interactor,
                    action,
                    out InteractionActionPresentation presentation,
                    progress
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
            isFocused,
            interactor.Detector?.GetInteractionDistance(this) ?? 0.0f
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
        out InteractionActionPresentation presentation,
        in InteractionProgress progress = default
    )
    {
        presentation = default;
        if (action?.InteractionDefinition is null)
        {
            return false;
        }

        InteractionAvailability availability = EvaluateAvailability(interactor, action);
        if (availability is InteractionHidden)
        {
            return false;
        }

        presentation = new InteractionActionPresentation(
            action.InteractionDefinition.Id,
            action.InteractionDefinition.Label,
            action.InteractionDefinition.Description,
            action.InteractionDefinition.InputActionName,
            availability,
            action.Automatic,
            action.InteractionDefinition.HoldThreshold > 0.0f,
            progress.HoldOf(action.InteractionDefinition),
            progress.HoldElapsedOf(action.InteractionDefinition)
        );
        return true;
    }

    /// <summary>Gets the execution presentations visible on this peer.</summary>
    /// <remarks>
    /// The returned snapshot is ordered by <see cref="Actions"/>, not by execution start time. Progress
    /// is resolved lazily from a local source, a linear transport sample, or a published value.
    /// </remarks>
    /// <returns>A fresh action-ordered snapshot of the visible active executions.</returns>
    public IReadOnlyList<GameplayActionExecutionPresentation> GetExecutionPresentations()
    {
        return ActionComponent?.GetExecutionPresentations()
            ?? System.Array.Empty<GameplayActionExecutionPresentation>();
    }

    /// <summary>Looks up the visible execution presentation for one action identifier.</summary>
    /// <param name="actionId">Stable identifier of the action to look up.</param>
    /// <param name="presentation">Visible execution snapshot when one exists.</param>
    /// <returns><see langword="true"/> when this target has a matching visible execution.</returns>
    public bool TryGetExecutionPresentation(
        StringName actionId,
        out GameplayActionExecutionPresentation presentation
    )
    {
        presentation = default;
        if (
            ActionComponent is null
            || !ActionComponent.TryGetExecutionPresentation(
                actionId,
                out GameplayActionExecutionPresentation current
            )
        )
        {
            return false;
        }

        presentation = current;
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

    /// <summary>Gets the world-space point used by focus scoring, range validation, and UI projection.</summary>
    /// <returns>The configured anchor position, or <see cref="Vector3.Zero"/> before configuration.</returns>
    public Vector3 GetInteractionPosition()
    {
        return InteractionAnchor?.GlobalPosition ?? Vector3.Zero;
    }

    /// <summary>Tells whether one collider reported by a query belongs to this target itself.</summary>
    /// <remarks>
    /// This is what "excluding the target's colliders" means for the line of sight ray: a target does
    /// not occlude itself, and an anchor authored inside the mesh that carries it must still be
    /// visible. The whole scene of the target counts, because the geometry and the anchor are siblings
    /// under it.
    /// </remarks>
    /// <param name="collider">Collider a physics query reported.</param>
    /// <returns>Whether the collider is part of this target.</returns>
    internal bool OwnsCollider(GodotObject? collider)
    {
        if (collider is not Node node)
        {
            return false;
        }

        Node root = GetParent() ?? this;
        return root == node || root.IsAncestorOf(node);
    }

    // Two targets sharing one area is a configuration error rather than a model, so the first
    // registration wins, exactly like the walk this replaces did.
    private void IndexArea(Area3D? area)
    {
        if (area is not null)
        {
            _areaOwners.TryAdd(area.GetInstanceId(), this);
        }
    }

    private void ForgetArea(Area3D? area)
    {
        if (
            area is not null
            && _areaOwners.TryGetValue(area.GetInstanceId(), out InteractiveComponent? owner)
            && owner == this
        )
        {
            _areaOwners.Remove(area.GetInstanceId());
        }
    }

    /// <summary>Godot callback that disconnects state and interactor registrations.</summary>
    public override void _ExitTree()
    {
        if (ActionComponent is not null && IsInstanceValid(ActionComponent))
        {
            ActionComponent.GameplayActionStarted -= OnGameplayActionStarted;
            ActionComponent.GameplayActionCompleted -= OnGameplayActionCompleted;
            ActionComponent.GameplayActionCancelled -= OnGameplayActionCancelled;
            ActionComponent.GameplayActionFailed -= OnGameplayActionFailed;
            ActionComponent.GameplayActionRejected -= OnGameplayActionRejected;
            ActionComponent.ExecutionPresentationChanged -= OnExecutionPresentationChanged;
            _actionComponentOwners.Remove(ActionComponent.GetInstanceId());
        }

        _registered.Remove(this);
        ForgetArea(InteractionArea);
        ForgetArea(IndicationArea);
        PurgeInvalidInteractors();

        // An area cannot report the overlap it loses by being freed, so every interactor that holds
        // this target — through its detector or through a registration — is told explicitly.
        HashSet<InteractionInteractor> holders = new(_presentInteractors);
        holders.UnionWith(_interactionOverlaps);
        holders.UnionWith(_indicationOverlaps);
        foreach (InteractionInteractor interactor in holders)
        {
            interactor.NotifyInteractiveRemoved(this);
        }

        _presentInteractors.Clear();
        _interactionOverlaps.Clear();
        _indicationOverlaps.Clear();
    }

    private void OnInteractionAreaBodyEntered(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Interactible, entered: true);

    private void OnInteractionAreaBodyExited(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Interactible, entered: false);

    private void OnIndicationAreaBodyEntered(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Indicated, entered: true);

    private void OnIndicationAreaBodyExited(Node3D body) =>
        NotifyOverlapChanged(body, InteractionDetectionKind.Indicated, entered: false);

    /// <summary>Pushes one overlap change of an area this target owns to the interactors involved.</summary>
    /// <remarks>
    /// The areas belong to the target, so the target is the only one able to report them, and it does
    /// so on every peer: an authoritative validation reads the very same overlap the owning client
    /// detected with. A detector that has a source of its own simply ignores the call.
    /// </remarks>
    private void NotifyOverlapChanged(Node3D body, InteractionDetectionKind kind, bool entered)
    {
        HashSet<InteractionInteractor> overlaps =
            kind == InteractionDetectionKind.Interactible
                ? _interactionOverlaps
                : _indicationOverlaps;

        _overlapBuffer.Clear();
        CollectInteractors(body, _overlapBuffer);
        foreach (InteractionInteractor interactor in _overlapBuffer)
        {
            if (entered ? !overlaps.Add(interactor) : !overlaps.Remove(interactor))
            {
                continue;
            }

            if (interactor.Detector is not InteractionDetector detector)
            {
                continue;
            }

            if (entered)
            {
                detector.OnEnteredTargetArea(this, kind);
            }
            else
            {
                detector.OnExitedTargetArea(this, kind);
            }
        }
    }

    private static void CollectInteractors(Node node, List<InteractionInteractor> interactors)
    {
        if (node is InteractionInteractor interactor)
        {
            interactors.Add(interactor);
        }

        foreach (Node child in node.GetChildren())
        {
            CollectInteractors(child, interactors);
        }
    }

    private void PurgeInvalidInteractors()
    {
        _presentInteractors.RemoveWhere(interactor => !IsInstanceValid(interactor));
        _interactionOverlaps.RemoveWhere(interactor => !IsInstanceValid(interactor));
        _indicationOverlaps.RemoveWhere(interactor => !IsInstanceValid(interactor));
    }
}
