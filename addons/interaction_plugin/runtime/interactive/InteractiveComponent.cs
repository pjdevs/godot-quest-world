using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
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
/// <param name="Duration">Seconds the target holds the execution, or zero when its executor decides.</param>
/// <param name="Elapsed">Seconds already spent, advanced by the authoritative target.</param>
internal readonly record struct InteractionExecution(
    ulong Id,
    InteractionInteractor Interactor,
    InteractionAction Action,
    StringName ConcurrencyGroup,
    float Duration,
    float Elapsed
)
{
    /// <summary>Gets how far this execution has progressed, or zero when it has no duration.</summary>
    public float Progress => Duration > 0.0f ? Mathf.Clamp(Elapsed / Duration, 0.0f, 1.0f) : 0.0f;
}

/// <summary>Notification payload built once an execution result has been applied.</summary>
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
    /// always followed by exactly one completion or cancellation.
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
    /// This covers a released input, an interactor leaving range, an explicit gameplay cancellation,
    /// and an executor failing after acceptance.
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

    private const string NotConfiguredReason = "Interaction is not configured.";
    private const string NotAuthoritativeReason = "The interaction is not authoritative.";
    private const string AlreadyRunningReason = "This is already in use.";

    private static ulong _nextExecutionId = 1;

    private readonly HashSet<InteractionInteractor> _presentInteractors = new();
    private readonly List<InteractionExecution> _activeExecutions = new();
    private Area3D? _interactionArea;

    internal bool HasActiveExecution => _activeExecutions.Count > 0;

    internal InteractionInteractor? ActiveInteractor =>
        _activeExecutions.Count > 0 ? _activeExecutions[0].Interactor : null;

    internal InteractionAction? ActiveAction =>
        _activeExecutions.Count > 0 ? _activeExecutions[0].Action : null;

    /// <summary>Godot callback that validates configuration and connects area and state signals.</summary>
    public override void _Ready()
    {
        SetProcess(false);

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

        if (
            TryGetGroupExecution(action, out InteractionExecution running)
            && running.Interactor != interactor
        )
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
        if (!Multiplayer.IsServer())
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

        // Availability lets an interactor keep requesting a target it already reserved, so the
        // running execution is what refuses here. Only the concurrency group of this action is
        // considered: an unrelated group stays free, which is the whole point of naming one.
        if (TryGetGroupExecution(action, out _))
        {
            return RefuseExecution(interactor, action, AlreadyRunningReason);
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

    /// <summary>Godot callback that advances the timed executions this target owns.</summary>
    /// <remarks>
    /// The authoritative peer owns the clock of a running action, so the progress a player watches
    /// cannot be forged by holding an input longer. Processing stays disabled while no execution
    /// declares a duration.
    /// </remarks>
    public override void _Process(double delta)
    {
        if (!Multiplayer.IsServer())
        {
            return;
        }

        for (int index = _activeExecutions.Count - 1; index >= 0; index--)
        {
            if (index >= _activeExecutions.Count)
            {
                continue;
            }

            InteractionExecution execution = _activeExecutions[index];
            if (execution.Duration <= 0.0f)
            {
                continue;
            }

            float elapsed = execution.Elapsed + (float)delta;
            if (elapsed < execution.Duration)
            {
                _activeExecutions[index] = execution with { Elapsed = elapsed };
                continue;
            }

            // Completing runs the same path as a gameplay completion, callbacks and notifications
            // included: nothing about the end differs because the clock happened to own it.
            CompleteExecution(execution.Id);
        }
    }

    /// <summary>Reads how far one running execution has progressed on the authoritative peer.</summary>
    /// <remarks>
    /// A pure query. An execution without duration always reports zero: its end is owned by gameplay,
    /// so the target has nothing to measure it against.
    /// </remarks>
    /// <param name="executionId">Identifier carried by the execution context.</param>
    /// <param name="progress">Progress between zero and one, or zero when unknown.</param>
    /// <returns><see langword="true"/> while the execution holds its reservation.</returns>
    public bool TryGetExecutionProgress(ulong executionId, out float progress)
    {
        int index = IndexOfExecution(executionId);
        progress = index < 0 ? 0.0f : _activeExecutions[index].Progress;
        return index >= 0;
    }

    private void ApplyRunningDurationCore(ulong executionId, float duration)
    {
        int index = duration > 0.0f ? IndexOfExecution(executionId) : -1;
        if (index < 0)
        {
            return;
        }

        _activeExecutions[index] = _activeExecutions[index] with { Duration = duration };
        UpdateExecutionProcessing();
    }

    private void UpdateExecutionProcessing()
    {
        foreach (InteractionExecution execution in _activeExecutions)
        {
            if (execution.Duration > 0.0f)
            {
                SetProcess(true);
                return;
            }
        }

        SetProcess(false);
    }

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
        if (interactor is null || action?.Executor is null || !Actions.Contains(action))
        {
            return null;
        }

        if (TryGetGroupExecution(action, out _))
        {
            return null;
        }

        InteractionExecution execution = new(
            _nextExecutionId++,
            interactor,
            action,
            action.GetConcurrencyGroup(),
            Mathf.Max(action.Executor.ExpectedDuration, 0.0f),
            0.0f
        );
        _activeExecutions.Add(execution);
        UpdateExecutionProcessing();
        return execution;
    }

    internal InteractionExecutionDispatch ApplyExecutionResultCore(
        in InteractionExecution execution,
        in InteractionExecutionResult result
    )
    {
        if (result is InteractionExecutionRunning running)
        {
            ApplyRunningDurationCore(execution.Id, running.Duration);
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

    internal InteractionExecution? EndExecutionCore(ulong executionId)
    {
        if (!Multiplayer.IsServer())
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
        UpdateExecutionProcessing();
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
                EmitSignal(SignalName.InteractionActionCompleted, interactor, action);
                break;

            case InteractionExecutionRunning:
                EmitSignal(SignalName.InteractionActionStarted, interactor, action);
                break;

            case InteractionExecutionRejected rejected:
                // Nothing ran, so the refusal alone is reported and no status is invalidated.
                EmitSignal(
                    SignalName.InteractionActionRejected,
                    interactor,
                    action,
                    rejected.Reason
                );
                return;

            case InteractionExecutionFailed failed:
                EmitSignal(SignalName.InteractionActionStarted, interactor, action);
                EmitSignal(
                    SignalName.InteractionActionCancelled,
                    interactor,
                    action,
                    failed.Reason
                );
                break;
        }

        NotifyStatusChanged();
    }

    private void DispatchExecutionCompletion(in InteractionExecution execution)
    {
        EmitSignal(SignalName.InteractionActionCompleted, execution.Interactor, execution.Action);
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
        _activeExecutions.Clear();
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
