using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Rules;
using QuestWorld.GameplayActions.Runtime.Runner;

namespace QuestWorld.GameplayActions.Runtime.Actions;

/// <summary>Authoritative host owning action occurrences, reservations and execution lifecycle.</summary>
[GlobalClass]
public partial class GameplayActionComponent : Node
{
    /// <summary>Emitted after an accepted execution has entered its started lifecycle.</summary>
    [Signal]
    public delegate void GameplayActionStartedEventHandler(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester
    );

    /// <summary>Emitted after an execution completes and its reservation has been released.</summary>
    [Signal]
    public delegate void GameplayActionCompletedEventHandler(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester
    );

    /// <summary>Emitted after a running execution is cancelled and released.</summary>
    [Signal]
    public delegate void GameplayActionCancelledEventHandler(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester,
        string reason
    );

    /// <summary>Emitted after an accepted execution fails and is released.</summary>
    [Signal]
    public delegate void GameplayActionFailedEventHandler(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester,
        string reason
    );

    /// <summary>Emitted when an action request is refused before a started lifecycle exists.</summary>
    [Signal]
    public delegate void GameplayActionRejectedEventHandler(
        long executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester,
        string reason
    );

    /// <summary>Emitted when transient execution presentation for one action changes.</summary>
    [Signal]
    public delegate void ExecutionPresentationChangedEventHandler(StringName actionId);

    /// <summary>Emitted after an action occurrence joins this host's declared action set.</summary>
    [Signal]
    public delegate void GameplayActionAddedEventHandler(GameplayAction action);

    /// <summary>Emitted at logical removal, before any running retirement window finishes.</summary>
    [Signal]
    public delegate void GameplayActionRemovedEventHandler(GameplayAction action);

    private const string NotConfiguredReason = "Action is not configured.";
    internal const string AlreadyRunningReason = "Action is already running.";
    private readonly Dictionary<StringName, GameplayAction> _actionsById = new();
    private readonly Dictionary<ulong, ActiveExecution> _executionsById = new();
    private readonly GameplayActionExecutionPresentationStore _presentation;
    private readonly HashSet<GameplayAction> _retiringActions = new();
    private ulong _nextExecutionId = 1;

    /// <summary>Gets or sets the ordered action occurrences explicitly owned by this host.</summary>
    [Export]
    public Godot.Collections.Array<GameplayAction> Actions { get; set; } = new();

    [ExportGroup("Context")]
    /// <summary>Gets or sets the gameplay host exposed through execution contexts.</summary>
    /// <remarks>The component parent is used when null.</remarks>
    [Export]
    public Node? Host { get; set; }

    /// <summary>Gets or sets the gameplay world exposed through execution contexts.</summary>
    /// <remarks>The current scene is used when null and the component is inside a scene tree.</remarks>
    [Export]
    public Node? World { get; set; }

    public GameplayActionComponent()
    {
        _presentation = new GameplayActionExecutionPresentationStore(
            this,
            ResolveAction,
            actionId => EmitSignal(SignalName.ExecutionPresentationChanged, actionId)
        );
    }

    internal bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    /// <summary>Registers the authored direct-child action set when the component enters the tree.</summary>
    public override void _Ready()
    {
        foreach (GameplayAction action in Actions)
        {
            RegisterAction(action, false);
        }
    }

    /// <summary>Registers one action at runtime and appends it to the declared action set.</summary>
    /// <remarks>
    /// An action added here joins <see cref="Actions"/>, which <see cref="RemoveAction"/> already
    /// takes it out of: the declared set is the ordered action set of this host, not only what the
    /// scene authored, so a consumer reading it never has to guess which additions it can see.
    /// </remarks>
    public bool AddAction(GameplayAction action)
    {
        if (!RegisterAction(action, true))
        {
            return false;
        }

        if (action.GetParent() is null)
        {
            AddChild(action);
        }

        if (!Actions.Contains(action))
        {
            Actions.Add(action);
        }

        EmitSignal(SignalName.GameplayActionAdded, action);

        return true;
    }

    /// <summary>Resolves a currently requestable action occurrence by its stable host-local ID.</summary>
    public GameplayAction? ResolveAction(StringName actionId) =>
        actionId is not null && _actionsById.TryGetValue(actionId, out GameplayAction? action)
            ? action
            : null;

    /// <summary>Logically removes an action, retaining a running occurrence until its execution ends.</summary>
    public bool RemoveAction(StringName actionId)
    {
        GameplayAction? action = ResolveAction(actionId);
        if (action is null)
        {
            return false;
        }

        _actionsById.Remove(actionId);
        Actions.Remove(action);
        EmitSignal(SignalName.GameplayActionRemoved, action);
        bool isExecuting = IsActionExecuting(actionId);
        if (isExecuting)
        {
            _retiringActions.Add(action);
        }
        else
        {
            _presentation.RemoveAction(actionId);
            FinalizeRemoval(action);
        }

        return true;
    }

    /// <summary>Evaluates the action's ordered gameplay rules without reserving or executing it.</summary>
    public GameplayActionAvailability EvaluateAction(
        StringName actionId,
        Node? instigator = null,
        Node? requester = null
    )
    {
        GameplayAction? action = ResolveAction(actionId);
        if (action?.Definition is null || action.Executor is null)
        {
            return new GameplayActionBlocked(NotConfiguredReason);
        }

        GameplayActionContext context = CreateContext(0ul, instigator, requester, action);
        return EvaluateRules(action.Rules, context);
    }

    /// <summary>Runs one action of this host on the authority, attributed to an instigator.</summary>
    /// <remarks>
    /// There is one way to run an action, and where the call comes from does not change it: rules and
    /// reservations apply to a quest, a script, a timer, and a player alike. What separates a player
    /// request is not a different operation but a requester waiting to be acknowledged, which only
    /// <see cref="ExecuteRequestedAction"/> attaches — so an execution started here never sends an
    /// acknowledgement to a peer that asked for nothing.
    /// </remarks>
    public GameplayActionExecutionResult ExecuteAction(
        StringName actionId,
        out ulong executionId,
        Node? instigator = null
    )
    {
        return ExecuteCore(actionId, out executionId, instigator, null);
    }

    /// <summary>Runs one action on behalf of the runner that requested it.</summary>
    /// <remarks>
    /// Reserved for the request transport: the runner passed here is answered with the started,
    /// progress, completed, cancelled, and failed acknowledgements of the resulting execution.
    /// </remarks>
    internal GameplayActionExecutionResult ExecuteRequestedAction(
        StringName actionId,
        out ulong executionId,
        Node? instigator,
        GameplayActionRunner requester
    )
    {
        return ExecuteCore(actionId, out executionId, instigator, requester);
    }

    private GameplayActionExecutionResult ExecuteCore(
        StringName actionId,
        out ulong executionId,
        Node? instigator,
        Node? requester
    )
    {
        executionId = 0;
        if (!IsAuthoritative)
        {
            return new GameplayActionExecutionRejected(
                "Only the authority may execute an action programmatically."
            );
        }

        GameplayAction? action = ResolveAction(actionId);
        if (action is null)
        {
            return new GameplayActionExecutionRejected(NotConfiguredReason);
        }

        GameplayActionAvailability availability = EvaluateAction(actionId, instigator, requester);
        if (availability is not GameplayActionAllowed)
        {
            string reason = availability.DescribeRefusal();
            DispatchRejected(0ul, action, instigator, requester, reason);
            return new GameplayActionExecutionRejected(reason);
        }

        ActiveExecution? reservation = ReserveExecutionCore(action, instigator, requester);
        if (reservation is null)
        {
            DispatchRejected(0ul, action, instigator, requester, AlreadyRunningReason);
            return new GameplayActionExecutionRejected(AlreadyRunningReason);
        }

        executionId = reservation.Value.Id;
        GameplayActionExecutionResult result;
        try
        {
            result = action.Executor!.Execute(BuildExecutionContext(reservation.Value));
        }
        catch (System.Exception exception)
        {
            GD.PushError(
                $"{GetPath()}: executor for action '{actionId}' threw an exception: {exception}"
            );
            result = new GameplayActionExecutionFailed(exception.Message);
        }

        ApplyExecutionResultCore(reservation.Value, result);
        return result;
    }

    /// <summary>Gets whether this host currently has an active execution for the supplied action ID.</summary>
    public bool IsActionExecuting(StringName actionId)
    {
        foreach (ActiveExecution execution in _executionsById.Values)
        {
            if (execution.Action.Definition?.Id == actionId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets whether any active execution currently reserves the supplied host-local group.</summary>
    public bool IsConcurrencyGroupExecuting(StringName concurrencyGroup)
    {
        foreach (ActiveExecution execution in _executionsById.Values)
        {
            if (execution.Action.GetHostConcurrencyGroup() == concurrencyGroup)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets whether the supplied execution ID is still actively reserved.</summary>
    public bool IsExecutionActive(ulong executionId) => _executionsById.ContainsKey(executionId);

    internal bool TryGetFirstActiveExecution(
        out GameplayAction? action,
        out Node? instigator,
        out Node? requester
    )
    {
        foreach (ActiveExecution execution in _executionsById.Values)
        {
            action = execution.Action;
            instigator = execution.Instigator;
            requester = execution.Requester;
            return true;
        }

        action = null;
        instigator = null;
        requester = null;
        return false;
    }

    /// <summary>Gets whether one instigator is the reason a concurrency group is busy.</summary>
    /// <remarks>
    /// Asked of the instigator rather than the requester, because that is what attributes an
    /// execution whatever started it: the same answer covers an action a player asked for and one a
    /// script ran on their behalf.
    /// </remarks>
    internal bool IsConcurrencyGroupExecutingFor(StringName concurrencyGroup, Node instigator)
    {
        foreach (ActiveExecution execution in _executionsById.Values)
        {
            if (
                execution.Action.GetHostConcurrencyGroup() == concurrencyGroup
                && execution.Instigator == instigator
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns a snapshot of all current and retiring execution presentation slots.</summary>
    public IReadOnlyList<GameplayActionExecutionPresentation> GetExecutionPresentations()
    {
        List<GameplayAction> actions = new(_actionsById.Values);
        actions.AddRange(_retiringActions);
        return _presentation.GetPresentations(actions);
    }

    /// <summary>Looks up transient execution presentation for one action ID.</summary>
    public bool TryGetExecutionPresentation(
        StringName actionId,
        out GameplayActionExecutionPresentation presentation
    ) => _presentation.TryGet(actionId, out presentation);

    internal bool AddPendingExecutionPresentation(
        StringName actionId,
        GameplayActionProgressSample sample
    ) => _presentation.AddPrediction(actionId, sample);

    internal bool ConfirmRequesterExecution(
        StringName actionId,
        ulong executionId,
        bool hasSample,
        GameplayActionProgressSample sample
    ) => _presentation.ConfirmRequesterExecution(actionId, executionId, hasSample, sample);

    internal bool ApplyRequesterProgress(
        StringName actionId,
        ulong executionId,
        bool hasProgress,
        GameplayActionProgressSample sample
    ) => _presentation.ApplyRequesterProgress(actionId, executionId, hasProgress, sample);

    internal bool RemovePendingExecution(StringName actionId) =>
        _presentation.RemovePending(actionId);

    internal bool RemoveRequesterExecution(StringName actionId, ulong executionId) =>
        _presentation.RemoveRequesterExecution(actionId, executionId);

    internal bool HasLocalExecution(GameplayAction action) =>
        _presentation.HasLocalExecution(action);

    internal bool HasLocalExecutionInGroup(StringName group) =>
        _presentation.HasLocalExecutionInGroup(group);

    internal bool TryGetExecutionPresentationInGroup(
        StringName group,
        out GameplayActionExecutionPresentation presentation
    ) => _presentation.TryGetInGroup(group, out presentation);

    /// <summary>Publishes or clears normalized discrete progress for an active authoritative execution.</summary>
    public bool ReportExecutionProgress(ulong executionId, float? progress)
    {
        if (
            !_executionsById.TryGetValue(executionId, out ActiveExecution execution)
            || !IsAuthoritative
        )
        {
            return false;
        }

        bool changed = _presentation.ReportPublished(executionId, execution.Action, progress);
        if (changed)
        {
            NotifyRequesterProgress(execution);
        }

        return changed;
    }

    /// <summary>Attaches a local callable used to derive presentation progress for an active execution.</summary>
    public bool SetExecutionProgressSource(ulong executionId, Callable source) =>
        _presentation.SetSource(executionId, source);

    /// <summary>Removes a derived progress source, returning to the last published/transported value.</summary>
    public bool ClearExecutionProgressSource(ulong executionId) =>
        _presentation.ClearSource(executionId);

    internal bool ReportExecutionLinearProgress(
        ulong executionId,
        float progressBase,
        float progressPerSecond
    )
    {
        if (
            !_executionsById.TryGetValue(executionId, out ActiveExecution execution)
            || !IsAuthoritative
        )
        {
            return false;
        }

        bool changed = _presentation.ReportLinear(
            executionId,
            execution.Action,
            progressBase,
            progressPerSecond
        );
        if (changed)
        {
            NotifyRequesterProgress(execution);
        }

        return changed;
    }

    internal bool TryGetProgressSample(
        ulong executionId,
        out bool hasProgress,
        out GameplayActionProgressSample sample
    )
    {
        return _presentation.TryGetSample(executionId, out hasProgress, out sample);
    }

    internal Godot.Collections.Array<Godot.Collections.Dictionary<
        string,
        Variant
    >> BuildReplicatedExecutionEntries()
    {
        List<GameplayActionExecutionPresentationSource> executions = new();
        foreach (ActiveExecution execution in _executionsById.Values)
        {
            executions.Add(
                new GameplayActionExecutionPresentationSource(execution.Id, execution.Action)
            );
        }

        return _presentation.BuildReplicatedEntries(executions);
    }

    internal void ApplyReplicatedExecutionEntries(
        Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>> entries
    ) => _presentation.ApplyReplicatedEntries(entries);

    /// <summary>Completes an active authoritative execution and releases its reservation.</summary>
    public bool CompleteExecution(ulong executionId)
    {
        ActiveExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        GameplayActionContext context = BuildExecutionContext(execution.Value);
        execution.Value.Action.Executor?.OnExecutionCompleted(context);
        FinalizeRetiredAction(execution.Value.Action);
        EmitCompleted(execution.Value);
        return true;
    }

    /// <summary>Cancels an active authoritative execution and releases its reservation.</summary>
    public bool CancelExecution(ulong executionId, string reason = "")
    {
        ActiveExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        GameplayActionContext context = BuildExecutionContext(execution.Value);
        execution.Value.Action.Executor?.OnExecutionCancelled(context, reason);
        FinalizeRetiredAction(execution.Value.Action);
        EmitCancelled(execution.Value, reason);
        return true;
    }

    /// <summary>Fails an active authoritative execution and releases its reservation.</summary>
    public bool FailExecution(ulong executionId, string reason)
    {
        ActiveExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        GameplayActionContext context = BuildExecutionContext(execution.Value);
        execution.Value.Action.Executor?.OnExecutionFailed(context, reason);
        FinalizeRetiredAction(execution.Value.Action);
        EmitFailed(execution.Value, reason);
        return true;
    }

    private bool RegisterAction(GameplayAction action, bool allowUnparented)
    {
        if (!CanRegister(action, allowUnparented))
        {
            return false;
        }

        action.Component = this;
        _actionsById.Add(action.Definition!.Id, action);
        return true;
    }

    private bool CanRegister(GameplayAction? action, bool allowUnparented)
    {
        if (action?.Definition is null || action.Definition.Id.IsEmpty || action.Executor is null)
        {
            GD.PushError(
                $"{GetPath()}: action registration requires a definition, a non-empty ID, and an executor."
            );
            return false;
        }

        if (action.Component is not null && action.Component != this)
        {
            GD.PushError(
                $"{GetPath()}: action '{action.Definition.Id}' is already owned by another component."
            );
            return false;
        }

        if (_actionsById.ContainsKey(action.Definition.Id))
        {
            GD.PushError($"{GetPath()}: duplicate action ID '{action.Definition.Id}'.");
            return false;
        }

        foreach (GameplayAction retiring in _retiringActions)
        {
            if (retiring.Definition?.Id == action.Definition.Id)
            {
                GD.PushError($"{GetPath()}: action ID '{action.Definition.Id}' is still retiring.");
                return false;
            }
        }

        Node? parent = action.GetParent();
        if (parent is not null && parent != this)
        {
            GD.PushError(
                $"{GetPath()}: action '{action.Definition.Id}' is parented outside its component."
            );
            return false;
        }

        if (!allowUnparented && parent != this)
        {
            GD.PushError(
                $"{GetPath()}: authored action '{action.Definition.Id}' must be a direct child."
            );
            return false;
        }

        return true;
    }

    private static GameplayActionAvailability EvaluateRules(
        Godot.Collections.Array<GameplayActionRule> rules,
        in GameplayActionContext context
    )
    {
        foreach (GameplayActionRule rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            GameplayActionAvailability availability = rule.Evaluate(context);
            if (availability is not GameplayActionAllowed)
            {
                return availability;
            }
        }

        return new GameplayActionAllowed();
    }

    private ActiveExecution? ReserveExecutionCore(
        GameplayAction action,
        Node? instigator,
        Node? requester
    )
    {
        StringName actionId = action.Definition!.Id;
        StringName group = action.GetHostConcurrencyGroup();
        foreach (ActiveExecution active in _executionsById.Values)
        {
            if (active.Action.Definition?.Id == actionId || active.ConcurrencyGroup == group)
            {
                return null;
            }
        }

        if (_nextExecutionId > (ulong)long.MaxValue)
        {
            GD.PushError($"{GetPath()}: gameplay action execution identifier space is exhausted.");
            return null;
        }

        ActiveExecution execution = new(_nextExecutionId++, action, group, instigator, requester);
        _executionsById.Add(execution.Id, execution);
        return execution;
    }

    private void ApplyExecutionResultCore(
        in ActiveExecution execution,
        in GameplayActionExecutionResult result
    )
    {
        switch (result)
        {
            case GameplayActionExecutionRunning:
                AddExecutionPresentation(execution);
                EmitStarted(execution);
                return;
            case GameplayActionExecutionCompleted:
                ReleaseExecutionCore(execution.Id);
                execution.Action.Executor?.OnExecutionCompleted(BuildExecutionContext(execution));
                FinalizeRetiredAction(execution.Action);
                EmitStarted(execution);
                EmitCompleted(execution);
                break;
            case GameplayActionExecutionFailed failed:
                ReleaseExecutionCore(execution.Id);
                execution.Action.Executor?.OnExecutionFailed(
                    BuildExecutionContext(execution),
                    failed.Reason
                );
                FinalizeRetiredAction(execution.Action);
                EmitStarted(execution);
                EmitFailed(execution, failed.Reason);
                break;
            case GameplayActionExecutionRejected rejected:
                ReleaseExecutionCore(execution.Id);
                FinalizeRetiredAction(execution.Action);
                DispatchRejected(
                    execution.Id,
                    execution.Action,
                    execution.Instigator,
                    execution.Requester,
                    rejected.Reason
                );
                break;
        }
    }

    private ActiveExecution? EndExecutionCore(ulong executionId)
    {
        if (!IsAuthoritative)
        {
            return null;
        }

        return ReleaseExecutionCore(executionId);
    }

    private ActiveExecution? ReleaseExecutionCore(ulong executionId)
    {
        if (!_executionsById.Remove(executionId, out ActiveExecution execution))
        {
            return null;
        }

        _presentation.RemoveExecution(executionId, execution.Action);
        return execution;
    }

    private void AddExecutionPresentation(in ActiveExecution execution)
    {
        if (!IsAuthoritative)
        {
            return;
        }

        _presentation.AddExecution(execution.Id, execution.Action);
    }

    private void EmitStarted(in ActiveExecution execution)
    {
        EmitSignal(
            SignalName.GameplayActionStarted,
            checked((long)execution.Id),
            execution.Action,
            ToVariant(execution.Instigator),
            ToVariant(execution.Requester)
        );
        if (execution.Requester is GameplayActionRunner runner)
        {
            runner.NotifyExecutionStarted(this, execution.Action, execution.Id);
        }
    }

    private void EmitCompleted(in ActiveExecution execution)
    {
        EmitSignal(
            SignalName.GameplayActionCompleted,
            checked((long)execution.Id),
            execution.Action,
            ToVariant(execution.Instigator),
            ToVariant(execution.Requester)
        );
        if (execution.Requester is GameplayActionRunner runner)
        {
            runner.NotifyExecutionCompleted(this, execution.Action, execution.Id);
        }
    }

    private void EmitCancelled(in ActiveExecution execution, string reason)
    {
        EmitSignal(
            SignalName.GameplayActionCancelled,
            checked((long)execution.Id),
            execution.Action,
            ToVariant(execution.Instigator),
            ToVariant(execution.Requester),
            reason
        );
        if (execution.Requester is GameplayActionRunner runner)
        {
            runner.NotifyExecutionCancelled(this, execution.Action, execution.Id, reason);
        }
    }

    private void EmitFailed(in ActiveExecution execution, string reason)
    {
        EmitSignal(
            SignalName.GameplayActionFailed,
            checked((long)execution.Id),
            execution.Action,
            ToVariant(execution.Instigator),
            ToVariant(execution.Requester),
            reason
        );
        if (execution.Requester is GameplayActionRunner runner)
        {
            runner.NotifyExecutionFailed(this, execution.Action, execution.Id, reason);
        }
    }

    private void DispatchRejected(
        ulong executionId,
        GameplayAction action,
        Node? instigator,
        Node? requester,
        string reason
    )
    {
        EmitSignal(
            SignalName.GameplayActionRejected,
            checked((long)executionId),
            action,
            ToVariant(instigator),
            ToVariant(requester),
            reason
        );
        if (requester is GameplayActionRunner runner && runner.IsAuthoritativeRunner)
        {
            runner.NotifyExecutionRejected(this, action, reason);
        }
    }

    private void NotifyRequesterProgress(in ActiveExecution execution)
    {
        if (execution.Requester is GameplayActionRunner runner)
        {
            runner.NotifyExecutionProgress(this, execution.Action, execution.Id);
        }
    }

    private static Variant ToVariant(Node? node) => node is null ? default : Variant.From(node);

    internal GameplayActionContext CreateContext(
        ulong executionId,
        Node? instigator,
        Node? requester,
        GameplayAction action
    ) =>
        new(
            executionId,
            instigator,
            requester,
            this,
            action,
            Host ?? GetParent(),
            World ?? (IsInsideTree() ? GetTree().CurrentScene : null)
        );

    private GameplayActionContext BuildExecutionContext(in ActiveExecution execution) =>
        CreateContext(execution.Id, execution.Instigator, execution.Requester, execution.Action);

    private void FinalizeRetiredAction(GameplayAction action)
    {
        if (_retiringActions.Remove(action))
        {
            FinalizeRemoval(action);
        }
    }

    private static void FinalizeRemoval(GameplayAction action)
    {
        action.Component = null;
        action.QueueFree();
    }

    private readonly record struct ActiveExecution(
        ulong Id,
        GameplayAction Action,
        StringName ConcurrencyGroup,
        Node? Instigator,
        Node? Requester
    );
}
