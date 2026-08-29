using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactor;
using QuestWorld.Interaction.Runtime.Rules;

namespace QuestWorld.Interaction.Runtime.Interactive;

/// <summary>Execution reserved on a target, owning one interactor and one action.</summary>
/// <remarks>
/// A target holds at most one execution per concurrency group. An instant action reserves one only
/// for the duration of its executor call; a running one keeps it until gameplay completes or cancels
/// it through <paramref name="Id"/>.
/// </remarks>
/// <param name="Id">Identifier allocated before the executor runs, unique for the whole session.</param>
/// <param name="Interactor">Interactor that reserved the execution.</param>
/// <param name="Action">Action being executed.</param>
/// <param name="ConcurrencyGroup">Group this execution is exclusive with on its target.</param>
internal readonly record struct InteractionExecution(
    ulong Id,
    InteractionInteractor Interactor,
    InteractionAction Action,
    StringName ConcurrencyGroup
);

internal sealed class InteractionExecutionPresentationSlot
{
    public InteractionExecutionPresentationSlot(ulong executionId, StringName actionId)
    {
        ExecutionId = executionId;
        ActionId = actionId;
    }

    public ulong ExecutionId { get; set; }

    public StringName ActionId { get; }

    public InteractionExecutionProgressState Progress { get; } = new();
}

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

internal readonly record struct InteractionExecutionDispatch(
    InteractionExecution Execution,
    InteractionExecutionResult Result
);

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
    public Godot.Collections.Array<InteractionAction> Actions { get; set; } = new();

    /// <summary>
    /// Gets or sets the ordered gameplay conditions shared by every action of this target.
    /// Evaluation stops at the first hidden or blocked result, before the action rules run.
    /// </summary>
    [Export]
    public Godot.Collections.Array<InteractionRule> TargetRules { get; set; } = new();

    private const string NotConfiguredReason = "Interaction is not configured.";
    private const string NotAuthoritativeReason = "The interaction is not authoritative.";
    private const string AlreadyRunningReason = "This is already in use.";
    private const string SomeoneElseReason = "Someone else is using this.";

    private static ulong _nextExecutionId = 1;

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

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private readonly HashSet<InteractionInteractor> _interactionOverlaps = new();
    private readonly HashSet<InteractionInteractor> _indicationOverlaps = new();
    private readonly List<InteractionInteractor> _overlapBuffer = new();
    private readonly List<InteractionExecution> _activeExecutions = new();
    private readonly Dictionary<
        StringName,
        InteractionExecutionPresentationSlot
    > _executionPresentations = new();
    private readonly Dictionary<
        ulong,
        InteractionExecutionPresentationSlot
    > _pendingExecutionProgress = new();
    private readonly HashSet<StringName> _warnedUnknownReplicatedActions = new();
    private Area3D? _interactionArea;

    internal bool HasActiveExecution => _activeExecutions.Count > 0;

    internal InteractionInteractor? ActiveInteractor =>
        _activeExecutions.Count > 0 ? _activeExecutions[0].Interactor : null;

    internal InteractionAction? ActiveAction =>
        _activeExecutions.Count > 0 ? _activeExecutions[0].Action : null;

    /// <summary>Gets whether this peer runs the authoritative half of an interaction.</summary>
    /// <remarks>
    /// Offline counts as authoritative: a peerless game is its own server. Asking the multiplayer API
    /// for an id it does not have only pushes an error and answers no, which would make every
    /// authoritative path refuse itself outside a session.
    /// </remarks>
    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

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
            || action.Executor is null
            || !Actions.Contains(action)
        )
        {
            return new InteractionBlocked(NotConfiguredReason);
        }

        InteractionContext context = new(interactor, this, action);
        InteractionAvailability targetAvailability = EvaluateRules(TargetRules, context);
        if (targetAvailability is not InteractionAllowed)
        {
            return targetAvailability;
        }

        InteractionAvailability actionAvailability = EvaluateRules(action.Rules, context);
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
            TryGetActionExecution(action, out InteractionExecution running)
            || TryGetGroupExecution(action, out running)
        )
        {
            return new InteractionBlocked(
                running.Interactor == interactor ? AlreadyRunningReason : SomeoneElseReason
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
            if (action?.Definition is null || action.Definition.InputActionName != inputActionName)
            {
                continue;
            }

            if (action.Definition.HoldThreshold > heldSeconds)
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
            if (action?.Definition is null || action.Definition.InputActionName != inputActionName)
            {
                continue;
            }

            if (
                action.Definition.HoldThreshold > longest
                && TryRankAction(interactor, action, out _)
            )
            {
                longest = action.Definition.HoldThreshold;
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
        float threshold = action.Definition!.HoldThreshold;
        float bestThreshold = best.Definition!.HoldThreshold;
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
            action.Automatic,
            action.Definition.HoldThreshold > 0.0f,
            progress.HoldOf(action.Definition),
            progress.HoldElapsedOf(action.Definition)
        );
        return true;
    }

    /// <summary>Gets the execution presentations visible on this peer.</summary>
    /// <remarks>
    /// The returned snapshot is ordered by <see cref="Actions"/>, not by execution start time. Progress
    /// is resolved lazily from a local source, a linear transport sample, or a published value.
    /// </remarks>
    /// <returns>A fresh action-ordered snapshot of the visible active executions.</returns>
    public IReadOnlyList<InteractionExecutionPresentation> GetExecutionPresentations()
    {
        List<InteractionExecutionPresentation> presentations = new();
        HashSet<StringName> addedActionIds = new();
        foreach (InteractionAction action in Actions)
        {
            if (
                action?.Definition is not null
                && addedActionIds.Add(action.Definition.Id)
                && _executionPresentations.TryGetValue(
                    action.Definition.Id,
                    out InteractionExecutionPresentationSlot? slot
                )
            )
            {
                presentations.Add(ResolveExecutionPresentation(slot));
            }
        }

        return presentations;
    }

    /// <summary>Looks up the visible execution presentation for one action identifier.</summary>
    /// <param name="actionId">Stable identifier of the action to look up.</param>
    /// <param name="presentation">Visible execution snapshot when one exists.</param>
    /// <returns><see langword="true"/> when this target has a matching visible execution.</returns>
    public bool TryGetExecutionPresentation(
        StringName actionId,
        out InteractionExecutionPresentation presentation
    )
    {
        presentation = default;
        if (
            actionId is null
            || actionId.IsEmpty
            || !_executionPresentations.TryGetValue(
                actionId,
                out InteractionExecutionPresentationSlot? slot
            )
        )
        {
            return false;
        }

        presentation = ResolveExecutionPresentation(slot);
        return true;
    }

    /// <summary>Runs the authoritative command of one action through its single executor.</summary>
    /// <remarks>
    /// This is the only supported way to perform an action. Called by
    /// <see cref="InteractionInteractor"/> on the server or offline host, it re-evaluates
    /// availability, reserves the execution, hands a coherent target to the executor, applies the
    /// returned outcome, and only then notifies. No signal is a command, so observers cannot make the
    /// action run twice or run it at all.
    /// </remarks>
    /// <param name="interactor">Interactor requesting the action.</param>
    /// <param name="action">Action of this target resolved from the requested identifier.</param>
    /// <returns>The outcome returned by the executor, or the refusal that stopped the command.</returns>
    public InteractionExecutionResult ExecuteAction(
        InteractionInteractor interactor,
        InteractionAction action
    )
    {
        return ExecuteAction(interactor, action, out _);
    }

    /// <summary>Runs the authoritative command of one action and reports its reservation.</summary>
    /// <remarks>
    /// Same contract as <see cref="ExecuteAction(InteractionInteractor, InteractionAction)"/>. The
    /// identifier is only meaningful while the result is <see cref="InteractionExecutionRunning"/>:
    /// any other outcome released the reservation before returning.
    /// </remarks>
    /// <param name="interactor">Interactor requesting the action.</param>
    /// <param name="action">Action of this target resolved from the requested identifier.</param>
    /// <param name="executionId">Identifier of the reservation, or zero when none was allocated.</param>
    /// <returns>The outcome returned by the executor, or the refusal that stopped the command.</returns>
    public InteractionExecutionResult ExecuteAction(
        InteractionInteractor interactor,
        InteractionAction action,
        out ulong executionId
    )
    {
        executionId = 0;
        if (!IsAuthoritative)
        {
            // No notification here: this component only reports what happened authoritatively.
            GD.PushWarning($"{GetPath()}: only the server may execute an interaction action.");
            return new InteractionExecutionRejected(NotAuthoritativeReason);
        }

        InteractionAvailability availability = EvaluateAvailability(interactor, action);
        if (availability is not InteractionAllowed)
        {
            return RefuseExecution(interactor, action, availability.DescribeRefusal());
        }

        InteractionExecution? reservation = ReserveExecutionCore(interactor, action);
        if (reservation is null)
        {
            return RefuseExecution(interactor, action, NotConfiguredReason);
        }

        executionId = reservation.Value.Id;

        // The reservation is complete and every invariant holds before arbitrary gameplay runs.
        InteractionExecutionResult result = action.Executor!.Execute(
            BuildExecutionContext(reservation.Value)
        );

        InteractionExecutionDispatch dispatch = ApplyExecutionResultCore(reservation.Value, result);
        DispatchExecutionResult(dispatch);
        return result;
    }

    private void AddExecutionPresentation(in InteractionExecution execution)
    {
        if (
            !IsAuthoritative
            || execution.Action?.Definition is not InteractionActionDefinition definition
        )
        {
            return;
        }

        StringName actionId = definition.Id;
        InteractionExecutionPresentationSlot slot;
        if (
            _pendingExecutionProgress.Remove(
                execution.Id,
                out InteractionExecutionPresentationSlot? pending
            )
        )
        {
            slot = pending;
        }
        else
        {
            slot = new InteractionExecutionPresentationSlot(execution.Id, actionId);
        }
        slot.ExecutionId = execution.Id;
        bool structuralChange =
            !_executionPresentations.TryGetValue(
                actionId,
                out InteractionExecutionPresentationSlot? previous
            )
            || previous.ExecutionId != slot.ExecutionId;
        _executionPresentations[actionId] = slot;
        if (structuralChange)
        {
            EmitSignal(SignalName.ExecutionPresentationChanged, actionId);
        }
    }

    private static double CurrentTimeSeconds() => Time.GetTicksMsec() / 1000.0;

    private InteractionExecutionPresentation ResolveExecutionPresentation(
        InteractionExecutionPresentationSlot slot
    ) =>
        new(
            slot.ExecutionId,
            slot.ActionId,
            slot.Progress.Resolve(this, slot.ActionId, CurrentTimeSeconds())
        );

    private void RemoveExecutionPresentation(in InteractionExecution execution)
    {
        if (
            execution.Action?.Definition is not InteractionActionDefinition definition
            || !_executionPresentations.TryGetValue(
                definition.Id,
                out InteractionExecutionPresentationSlot? slot
            )
            || slot.ExecutionId != execution.Id
        )
        {
            return;
        }

        _executionPresentations.Remove(definition.Id);
        EmitSignal(SignalName.ExecutionPresentationChanged, definition.Id);
    }

    /// <summary>Publishes a discrete normalized progress value for a running execution.</summary>
    /// <remarks>
    /// This is an authority-only gameplay API. An owned linear sample cannot be overwritten by a
    /// discrete producer; an execution without one may publish null or a
    /// clamped finite value. The value is sent to the requester's local presentation through the
    /// existing reliable owner channel.
    /// </remarks>
    /// <param name="executionId">Identifier of the active execution.</param>
    /// <param name="progress">Normalized value, or null to clear the published value.</param>
    /// <returns>False for stale executions or invalid values.</returns>
    public bool ReportExecutionProgress(ulong executionId, float? progress)
    {
        InteractionExecution? execution = FindExecution(executionId);
        if (execution is null || !IsAuthoritative)
        {
            return false;
        }

        if (progress.HasValue && !float.IsFinite(progress.Value))
        {
            GD.PushWarning($"{GetPath()}: execution progress must be finite.");
            return false;
        }

        bool wasVisible = IsVisibleExecutionSlot(execution.Value.Action, executionId);
        InteractionExecutionPresentationSlot slot = GetProgressSlot(execution.Value);

        if (slot.Progress.RejectPublishedOverride(this, executionId))
        {
            return false;
        }

        float normalized = progress.HasValue ? Mathf.Clamp(progress.Value, 0.0f, 1.0f) : 0.0f;
        if (slot.Progress.MatchesPublished(progress))
        {
            return false;
        }

        slot.Progress.Publish(progress.HasValue ? normalized : null);
        if (wasVisible)
        {
            EmitSignal(SignalName.ExecutionPresentationChanged, slot.ActionId);
            NotifyRequesterProgress(execution.Value, slot, progress.HasValue);
        }
        return true;
    }

    /// <summary>Registers a local callable that derives progress for an existing presentation slot.</summary>
    /// <param name="executionId">Identifier of the local execution presentation.</param>
    /// <param name="source">Callable returning null or a numeric normalized value.</param>
    /// <returns>False for the prediction sentinel or an unknown execution.</returns>
    public bool SetExecutionProgressSource(ulong executionId, Callable source)
    {
        if (
            executionId == 0ul
            || !TryGetProgressSlot(executionId, out InteractionExecutionPresentationSlot? slot)
            || slot is null
        )
        {
            return false;
        }

        slot.Progress.SetSource(source);
        if (IsVisibleExecutionSlot(slot.ActionId, slot.ExecutionId))
        {
            EmitSignal(SignalName.ExecutionPresentationChanged, slot.ActionId);
        }
        return true;
    }

    /// <summary>Clears a local callable progress source.</summary>
    /// <param name="executionId">Identifier of the local execution presentation.</param>
    /// <returns>False when no matching local slot exists.</returns>
    public bool ClearExecutionProgressSource(ulong executionId)
    {
        if (
            executionId == 0ul
            || !TryGetProgressSlot(executionId, out InteractionExecutionPresentationSlot? slot)
            || slot is null
        )
        {
            return false;
        }

        slot.Progress.ClearSource();
        if (IsVisibleExecutionSlot(slot.ActionId, slot.ExecutionId))
        {
            EmitSignal(SignalName.ExecutionPresentationChanged, slot.ActionId);
        }
        return true;
    }

    internal bool ReportExecutionLinearProgress(
        ulong executionId,
        float progressBase,
        float progressPerSecond
    )
    {
        InteractionExecution? execution = FindExecution(executionId);
        if (execution is null || !IsAuthoritative)
        {
            return false;
        }

        bool wasVisible = IsVisibleExecutionSlot(execution.Value.Action, executionId);
        InteractionExecutionPresentationSlot slot = GetProgressSlot(execution.Value);

        slot.Progress.ReportLinear(progressBase, progressPerSecond, CurrentTimeSeconds());
        if (wasVisible)
        {
            EmitSignal(SignalName.ExecutionPresentationChanged, slot.ActionId);
            NotifyRequesterProgress(execution.Value, slot, hasProgress: true);
        }
        return true;
    }

    internal bool AddPendingExecutionPresentation(
        StringName actionId,
        InteractionProgressSample sample
    )
    {
        if (actionId is null || actionId.IsEmpty || _executionPresentations.ContainsKey(actionId))
        {
            return false;
        }

        InteractionExecutionPresentationSlot slot = new(0ul, actionId);
        slot.Progress.Predict(sample, CurrentTimeSeconds());
        _executionPresentations.Add(actionId, slot);
        EmitSignal(SignalName.ExecutionPresentationChanged, actionId);
        return true;
    }

    internal bool ConfirmRequesterExecution(
        StringName actionId,
        ulong executionId,
        bool hasSample,
        InteractionProgressSample sample
    )
    {
        if (executionId == 0ul || actionId is null || actionId.IsEmpty)
        {
            return false;
        }

        InteractionExecutionPresentationSlot? slot = _executionPresentations.TryGetValue(
            actionId,
            out InteractionExecutionPresentationSlot? existing
        )
            ? existing
            : null;
        if (slot is not null && slot.ExecutionId != 0ul && slot.ExecutionId != executionId)
        {
            return false;
        }

        if (slot is not null && slot.ExecutionId == executionId)
        {
            if (hasSample && sample.Revision > slot.Progress.Revision)
            {
                ApplyRequesterProgress(actionId, executionId, true, sample);
            }
            return true;
        }

        if (slot is null)
        {
            slot = new InteractionExecutionPresentationSlot(executionId, actionId);
            _executionPresentations.Add(actionId, slot);
        }

        bool structuralChange = slot.ExecutionId != executionId;
        slot.ExecutionId = executionId;
        slot.Progress.Confirm(hasSample, sample, CurrentTimeSeconds(), this, actionId);

        if (structuralChange)
        {
            EmitSignal(SignalName.ExecutionPresentationChanged, actionId);
        }

        return true;
    }

    internal bool ApplyRequesterProgress(
        StringName actionId,
        ulong executionId,
        bool hasProgress,
        InteractionProgressSample sample
    )
    {
        if (
            !_executionPresentations.TryGetValue(
                actionId,
                out InteractionExecutionPresentationSlot? slot
            )
            || slot.ExecutionId != executionId
        )
        {
            return false;
        }

        if (
            !slot.Progress.ApplyNewerSample(
                hasProgress,
                sample,
                CurrentTimeSeconds(),
                this,
                actionId
            )
        )
        {
            return false;
        }
        EmitSignal(SignalName.ExecutionPresentationChanged, actionId);
        return true;
    }

    internal bool RemovePendingExecution(StringName actionId)
    {
        return RemoveExecutionPresentation(actionId, 0ul);
    }

    internal bool RemoveRequesterExecution(StringName actionId, ulong executionId)
    {
        return RemoveExecutionPresentation(actionId, executionId);
    }

    internal bool HasLocalExecution(InteractionAction action)
    {
        return action?.Definition is not null
            && _executionPresentations.ContainsKey(action.Definition.Id);
    }

    internal bool HasLocalExecutionInGroup(StringName group)
    {
        foreach (InteractionExecutionPresentationSlot slot in _executionPresentations.Values)
        {
            if (ResolveAction(slot.ActionId)?.GetConcurrencyGroup() == group)
            {
                return true;
            }
        }

        return false;
    }

    private bool RemoveExecutionPresentation(StringName actionId, ulong executionId)
    {
        if (
            actionId is null
            || !_executionPresentations.TryGetValue(
                actionId,
                out InteractionExecutionPresentationSlot? slot
            )
            || slot.ExecutionId != executionId
        )
        {
            return false;
        }

        _executionPresentations.Remove(actionId);
        EmitSignal(SignalName.ExecutionPresentationChanged, actionId);
        return true;
    }

    private InteractionExecution? FindExecution(ulong executionId)
    {
        int index = IndexOfExecution(executionId);
        return index < 0 ? null : _activeExecutions[index];
    }

    private bool TryGetProgressSlot(
        ulong executionId,
        out InteractionExecutionPresentationSlot? slot
    )
    {
        foreach (InteractionExecutionPresentationSlot candidate in _executionPresentations.Values)
        {
            if (candidate.ExecutionId == executionId)
            {
                slot = candidate;
                return true;
            }
        }
        if (
            _pendingExecutionProgress.TryGetValue(
                executionId,
                out InteractionExecutionPresentationSlot? pending
            )
        )
        {
            slot = pending;
            return true;
        }

        slot = null;
        return false;
    }

    private bool IsVisibleExecutionSlot(InteractionAction action, ulong executionId)
    {
        return action.Definition is InteractionActionDefinition definition
            && IsVisibleExecutionSlot(definition.Id, executionId);
    }

    private bool IsVisibleExecutionSlot(StringName actionId, ulong executionId)
    {
        return _executionPresentations.TryGetValue(
                actionId,
                out InteractionExecutionPresentationSlot? slot
            )
            && slot.ExecutionId == executionId;
    }

    private InteractionExecutionPresentationSlot GetProgressSlot(in InteractionExecution execution)
    {
        if (
            TryGetProgressSlot(execution.Id, out InteractionExecutionPresentationSlot? existing)
            && existing is not null
        )
        {
            return existing;
        }

        InteractionExecutionPresentationSlot slot = new(
            execution.Id,
            execution.Action.Definition!.Id
        );
        _pendingExecutionProgress.Add(execution.Id, slot);
        return slot;
    }

    private void NotifyRequesterProgress(
        in InteractionExecution execution,
        InteractionExecutionPresentationSlot slot,
        bool hasProgress
    )
    {
        if (
            execution.Action.ExecutionVisibility == InteractionExecutionVisibility.RequesterOnly
            && IsRequesterUsable(execution)
            && !execution.Interactor.IsLocallyControlled
        )
        {
            slot.Progress.TryGetSample(out _, out InteractionProgressSample sample);
            execution.Interactor.NotifyExecutionProgress(
                this,
                execution.Action,
                execution.Id,
                hasProgress,
                hasProgress ? sample : new InteractionProgressSample(0.0f, 0.0f, sample.Revision)
            );
        }
    }

    internal bool TryGetProgressSample(
        ulong executionId,
        out bool hasProgress,
        out InteractionProgressSample sample
    )
    {
        hasProgress = false;
        sample = default;
        if (
            !TryGetProgressSlot(executionId, out InteractionExecutionPresentationSlot? slot)
            || slot is null
        )
        {
            return false;
        }

        return slot.Progress.TryGetSample(out hasProgress, out sample);
    }

    internal Godot.Collections.Array BuildReplicatedExecutionEntries()
    {
        Godot.Collections.Array entries = new();
        foreach (InteractionAction action in Actions)
        {
            if (
                action?.Definition is not InteractionActionDefinition definition
                || action.ExecutionVisibility != InteractionExecutionVisibility.Replicated
                || !TryGetActionExecution(action, out InteractionExecution execution)
            )
            {
                continue;
            }

            InteractionExecutionPresentationSlot slot = GetProgressSlot(execution);
            slot.Progress.TryGetSample(out bool hasProgress, out InteractionProgressSample sample);
            entries.Add(
                new Godot.Collections.Dictionary
                {
                    ["action_id"] = definition.Id,
                    ["execution_id"] = checked((long)execution.Id),
                    ["progress_present"] = hasProgress,
                    ["progress_base"] = sample.ProgressBase,
                    ["progress_per_second"] = sample.ProgressPerSecond,
                    ["revision"] = sample.Revision,
                }
            );
        }

        return entries;
    }

    internal void ApplyReplicatedExecutionEntries(Godot.Collections.Array entries)
    {
        HashSet<StringName> presentActions = new();
        foreach (Variant entryValue in entries)
        {
            if (entryValue.VariantType != Variant.Type.Dictionary)
            {
                continue;
            }

            Godot.Collections.Dictionary entry = entryValue.AsGodotDictionary();
            if (!TryReadReplicatedExecutionEntry(entry, out ReplicatedExecutionEntry decoded))
            {
                continue;
            }

            InteractionAction? action = ResolveAction(decoded.ActionId);
            if (action?.Definition is null)
            {
                if (_warnedUnknownReplicatedActions.Add(decoded.ActionId))
                {
                    GD.PushWarning(
                        $"{GetPath()}: replicated execution references unknown action '{decoded.ActionId}'."
                    );
                }
                continue;
            }

            if (action.ExecutionVisibility != InteractionExecutionVisibility.Replicated)
            {
                continue;
            }

            presentActions.Add(decoded.ActionId);
            ApplyReplicatedExecution(decoded);
        }

        foreach (InteractionAction action in Actions)
        {
            if (
                action?.Definition is not InteractionActionDefinition definition
                || action.ExecutionVisibility != InteractionExecutionVisibility.Replicated
                || presentActions.Contains(definition.Id)
                || !_executionPresentations.TryGetValue(
                    definition.Id,
                    out InteractionExecutionPresentationSlot? slot
                )
                || slot.ExecutionId == 0ul
            )
            {
                continue;
            }

            _executionPresentations.Remove(definition.Id);
            EmitSignal(SignalName.ExecutionPresentationChanged, definition.Id);
        }
    }

    private void ApplyReplicatedExecution(in ReplicatedExecutionEntry entry)
    {
        if (
            _executionPresentations.TryGetValue(
                entry.ActionId,
                out InteractionExecutionPresentationSlot? existing
            )
            && existing.ExecutionId == entry.ExecutionId
        )
        {
            if (
                existing.Progress.ApplyNewerSample(
                    entry.HasProgress,
                    entry.Sample,
                    CurrentTimeSeconds(),
                    this,
                    entry.ActionId
                )
            )
            {
                EmitSignal(SignalName.ExecutionPresentationChanged, entry.ActionId);
            }
            return;
        }

        InteractionExecutionPresentationSlot slot = new(entry.ExecutionId, entry.ActionId);
        slot.Progress.Confirm(
            entry.HasProgress,
            entry.Sample,
            CurrentTimeSeconds(),
            this,
            entry.ActionId
        );
        _executionPresentations[entry.ActionId] = slot;
        EmitSignal(SignalName.ExecutionPresentationChanged, entry.ActionId);
    }

    private static bool TryReadReplicatedExecutionEntry(
        Godot.Collections.Dictionary entry,
        out ReplicatedExecutionEntry decoded
    )
    {
        decoded = default;
        if (
            !entry.TryGetValue("action_id", out Variant actionValue)
            || !entry.TryGetValue("execution_id", out Variant executionValue)
            || !entry.TryGetValue("progress_present", out Variant presentValue)
            || !entry.TryGetValue("progress_base", out Variant baseValue)
            || !entry.TryGetValue("progress_per_second", out Variant rateValue)
            || !entry.TryGetValue("revision", out Variant revisionValue)
            || actionValue.VariantType != Variant.Type.StringName
            || executionValue.VariantType != Variant.Type.Int
            || presentValue.VariantType != Variant.Type.Bool
            || baseValue.VariantType != Variant.Type.Float
            || rateValue.VariantType != Variant.Type.Float
            || revisionValue.VariantType != Variant.Type.Int
        )
        {
            return false;
        }

        long signedExecutionId = executionValue.AsInt64();
        if (signedExecutionId <= 0)
        {
            return false;
        }

        StringName actionId = actionValue.AsStringName();
        if (actionId.IsEmpty)
        {
            return false;
        }

        decoded = new ReplicatedExecutionEntry(
            actionId,
            (ulong)signedExecutionId,
            presentValue.AsBool(),
            new InteractionProgressSample(
                (float)baseValue.AsDouble(),
                (float)rateValue.AsDouble(),
                revisionValue.AsInt64()
            )
        );
        return true;
    }

    private readonly record struct ReplicatedExecutionEntry(
        StringName ActionId,
        ulong ExecutionId,
        bool HasProgress,
        InteractionProgressSample Sample
    );

    /// <summary>Gets whether one execution is still reserved on this target.</summary>
    /// <remarks>
    /// A pure query, safe to call from any peer. Identifiers are never reused, so an unknown one
    /// simply means the execution already ended.
    /// </remarks>
    /// <param name="executionId">Identifier carried by the execution context.</param>
    /// <returns><see langword="true"/> while the execution holds its reservation.</returns>
    public bool IsExecutionActive(ulong executionId)
    {
        return IndexOfExecution(executionId) >= 0;
    }

    internal InteractionExecution? ReserveExecutionCore(
        InteractionInteractor interactor,
        InteractionAction action
    )
    {
        if (
            interactor is null
            || action?.Definition is null
            || action.Executor is null
            || !Actions.Contains(action)
        )
        {
            return null;
        }

        if (TryGetActionExecution(action, out _) || TryGetGroupExecution(action, out _))
        {
            return null;
        }

        if (_nextExecutionId > (ulong)long.MaxValue)
        {
            GD.PushError($"{GetPath()}: interaction execution identifier space is exhausted.");
            return null;
        }

        // The reservation exists before arbitrary gameplay runs so the executor receives a coherent
        // target and cannot race a sibling execution.
        InteractionExecution execution = new(
            _nextExecutionId++,
            interactor,
            action,
            action.GetConcurrencyGroup()
        );
        _activeExecutions.Add(execution);
        return execution;
    }

    internal InteractionExecutionDispatch ApplyExecutionResultCore(
        in InteractionExecution execution,
        in InteractionExecutionResult result
    )
    {
        if (result is InteractionExecutionRunning)
        {
            AddExecutionPresentation(execution);
        }
        else
        {
            ReleaseExecutionCore(execution.Id);
        }

        return new InteractionExecutionDispatch(execution, result);
    }

    /// <summary>Completes the execution an executor left running.</summary>
    /// <remarks>
    /// Call from authoritative gameplay code on the server or offline host, with the identifier the
    /// executor received in its <see cref="InteractionExecutionContext"/>.
    /// </remarks>
    /// <param name="executionId">Identifier of the execution to complete.</param>
    /// <returns><see langword="true"/> when a running execution was completed.</returns>
    public bool CompleteExecution(ulong executionId)
    {
        InteractionExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        NotifyExecutorCompleted(execution.Value);
        DispatchExecutionCompletion(execution.Value);
        return true;
    }

    /// <summary>Cancels the execution an executor left running.</summary>
    /// <remarks>
    /// Call from authoritative gameplay code on the server or offline host, with the identifier the
    /// executor received in its <see cref="InteractionExecutionContext"/>.
    /// </remarks>
    /// <param name="executionId">Identifier of the execution to cancel.</param>
    /// <param name="reason">Reason carried by <see cref="InteractionActionCancelled"/>.</param>
    /// <returns><see langword="true"/> when a running execution was cancelled.</returns>
    public bool CancelExecution(ulong executionId, string reason = "")
    {
        InteractionExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        NotifyExecutorCancelled(execution.Value, reason);
        DispatchExecutionCancellation(execution.Value, reason);
        return true;
    }

    /// <summary>Fails the execution an executor left running.</summary>
    /// <remarks>
    /// Failure is distinct from cancellation and produces one failed notification after releasing the
    /// reservation. The method is authority-only and stale identifiers are ignored.
    /// </remarks>
    /// <param name="executionId">Identifier of the execution to fail.</param>
    /// <param name="reason">Reason carried by <see cref="InteractionActionFailed"/>.</param>
    /// <returns><see langword="true"/> when a running execution was failed.</returns>
    public bool FailExecution(ulong executionId, string reason)
    {
        InteractionExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        NotifyExecutorFailed(execution.Value, reason);
        DispatchExecutionFailure(execution.Value, reason);
        return true;
    }

    internal InteractionExecution? EndExecutionCore(ulong executionId)
    {
        if (!IsAuthoritative)
        {
            GD.PushWarning($"{GetPath()}: only the server may end an interaction execution.");
            return null;
        }

        return ReleaseExecutionCore(executionId);
    }

    private InteractionExecution? ReleaseExecutionCore(ulong executionId)
    {
        int index = IndexOfExecution(executionId);
        if (index < 0)
        {
            return null;
        }

        InteractionExecution execution = _activeExecutions[index];
        _activeExecutions.RemoveAt(index);
        RemoveExecutionPresentation(execution);
        _pendingExecutionProgress.Remove(execution.Id);
        return execution;
    }

    private int IndexOfExecution(ulong executionId)
    {
        for (int index = 0; index < _activeExecutions.Count; index++)
        {
            if (_activeExecutions[index].Id == executionId)
            {
                return index;
            }
        }

        return -1;
    }

    private bool TryGetGroupExecution(InteractionAction action, out InteractionExecution execution)
    {
        execution = default;
        if (action is null)
        {
            return false;
        }

        StringName group = action.GetConcurrencyGroup();
        foreach (InteractionExecution candidate in _activeExecutions)
        {
            if (candidate.ConcurrencyGroup == group)
            {
                execution = candidate;
                return true;
            }
        }

        return false;
    }

    private bool TryGetActionExecution(InteractionAction action, out InteractionExecution execution)
    {
        execution = default;
        if (
            action?.Definition is not InteractionActionDefinition definition
            || definition.Id.IsEmpty
        )
        {
            return false;
        }

        foreach (InteractionExecution candidate in _activeExecutions)
        {
            if (candidate.Action?.Definition?.Id == definition.Id)
            {
                execution = candidate;
                return true;
            }
        }

        return false;
    }

    private InteractionExecutionContext BuildExecutionContext(in InteractionExecution execution)
    {
        return new InteractionExecutionContext(
            execution.Id,
            execution.Interactor,
            this,
            execution.Action
        );
    }

    // The owner of the mutation learns that its execution ended before any observer does, and it
    // learns it by a direct call: nothing is broadcast, so no executor has to filter out the
    // executions of its siblings.
    private void NotifyExecutorCompleted(in InteractionExecution execution)
    {
        InteractionActionExecutor? executor = execution.Action?.Executor;
        if (executor is not null && IsInstanceValid(executor))
        {
            executor.OnExecutionCompleted(BuildExecutionContext(execution));
        }
    }

    private void NotifyExecutorCancelled(in InteractionExecution execution, string reason)
    {
        InteractionActionExecutor? executor = execution.Action?.Executor;
        if (executor is not null && IsInstanceValid(executor))
        {
            executor.OnExecutionCancelled(BuildExecutionContext(execution), reason);
        }
    }

    private void NotifyExecutorFailed(in InteractionExecution execution, string reason)
    {
        InteractionActionExecutor? executor = execution.Action?.Executor;
        if (executor is not null && IsInstanceValid(executor))
        {
            executor.OnExecutionFailed(BuildExecutionContext(execution), reason);
        }
    }

    private InteractionExecutionResult RefuseExecution(
        InteractionInteractor interactor,
        InteractionAction action,
        string reason
    )
    {
        // A refused action changed nothing, so no status invalidation follows the notification.
        EmitSignal(SignalName.InteractionActionRejected, interactor, action, reason);
        return new InteractionExecutionRejected(reason);
    }

    internal void DispatchExecutionResult(in InteractionExecutionDispatch dispatch)
    {
        InteractionInteractor interactor = dispatch.Execution.Interactor;
        InteractionAction action = dispatch.Execution.Action;
        switch (dispatch.Result)
        {
            case InteractionExecutionCompleted:
                EmitSignal(SignalName.InteractionActionStarted, interactor, action);
                NotifyRequesterStarted(dispatch.Execution);
                EmitSignal(SignalName.InteractionActionCompleted, interactor, action);
                NotifyRequesterCompleted(dispatch.Execution);
                break;

            case InteractionExecutionRunning:
                EmitSignal(SignalName.InteractionActionStarted, interactor, action);
                NotifyRequesterStarted(dispatch.Execution);
                break;

            case InteractionExecutionRejected rejected:
                // Nothing ran, so the refusal alone is reported and no status is invalidated. The
                // requester learns about it through the refusal path of its own interactor, which is
                // also the one reporting refusals this component never saw.
                EmitSignal(
                    SignalName.InteractionActionRejected,
                    interactor,
                    action,
                    rejected.Reason
                );
                return;

            case InteractionExecutionFailed failed:
                EmitSignal(SignalName.InteractionActionStarted, interactor, action);
                NotifyRequesterStarted(dispatch.Execution);
                EmitSignal(SignalName.InteractionActionFailed, interactor, action, failed.Reason);
                NotifyRequesterFailed(dispatch.Execution, failed.Reason);
                break;
        }

        NotifyStatusChanged();
    }

    private void DispatchExecutionFailure(in InteractionExecution execution, string reason)
    {
        EmitSignal(
            SignalName.InteractionActionFailed,
            execution.Interactor,
            execution.Action,
            reason
        );
        NotifyRequesterFailed(execution, reason);
        NotifyStatusChanged();
    }

    private void DispatchExecutionCompletion(in InteractionExecution execution)
    {
        EmitSignal(SignalName.InteractionActionCompleted, execution.Interactor, execution.Action);
        NotifyRequesterCompleted(execution);
        NotifyStatusChanged();
    }

    private void DispatchExecutionCancellation(in InteractionExecution execution, string reason)
    {
        EmitSignal(
            SignalName.InteractionActionCancelled,
            execution.Interactor,
            execution.Action,
            reason
        );
        NotifyRequesterCancelled(execution, reason);
        NotifyStatusChanged();
    }

    // The peer that asked for the action learns its authoritative lifecycle by a direct call, on the
    // same principle as the executor callbacks above: nothing is broadcast, so the acknowledgement
    // stays with its requester instead of telling every client what somebody else is doing.
    private void NotifyRequesterStarted(in InteractionExecution execution)
    {
        if (IsRequesterUsable(execution))
        {
            execution.Interactor.NotifyExecutionStarted(this, execution.Action, execution.Id);
        }
    }

    private void NotifyRequesterCompleted(in InteractionExecution execution)
    {
        if (IsRequesterUsable(execution))
        {
            execution.Interactor.NotifyExecutionCompleted(this, execution.Action, execution.Id);
        }
    }

    private void NotifyRequesterCancelled(in InteractionExecution execution, string reason)
    {
        if (IsRequesterUsable(execution))
        {
            execution.Interactor.NotifyExecutionCancelled(
                this,
                execution.Action,
                execution.Id,
                reason
            );
        }
    }

    private void NotifyRequesterFailed(in InteractionExecution execution, string reason)
    {
        if (IsRequesterUsable(execution))
        {
            execution.Interactor.NotifyExecutionFailed(
                this,
                execution.Action,
                execution.Id,
                reason
            );
        }
    }

    // An interactor that left the tree between the start and the end of its execution has nobody to
    // acknowledge to, which is an ordinary disconnection rather than an error.
    private static bool IsRequesterUsable(in InteractionExecution execution) =>
        execution.Interactor is not null
        && IsInstanceValid(execution.Interactor)
        && execution.Action is not null;

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
        _registered.Remove(this);
        ForgetArea(InteractionArea);
        ForgetArea(IndicationArea);
        _activeExecutions.Clear();
        _executionPresentations.Clear();
        _pendingExecutionProgress.Clear();
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
