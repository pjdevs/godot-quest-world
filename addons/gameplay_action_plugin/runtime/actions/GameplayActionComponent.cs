using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Rules;

namespace QuestWorld.GameplayActions.Runtime.Actions;

[GlobalClass]
public partial class GameplayActionComponent : Node
{
    private const string NotConfiguredReason = "Action is not configured.";
    private const string AlreadyRunningReason = "Action is already running.";
    private readonly Dictionary<StringName, GameplayAction> _actionsById = new();
    private readonly Dictionary<ulong, ActiveExecution> _executionsById = new();
    private readonly HashSet<GameplayAction> _retiringActions = new();
    private ulong _nextExecutionId = 1;

    [Export]
    public Godot.Collections.Array<GameplayAction> Actions { get; set; } = new();

    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    public override void _Ready()
    {
        foreach (GameplayAction action in Actions)
        {
            RegisterAction(action, false);
        }
    }

    public bool AddAction(GameplayAction action)
    {
        if (!CanRegister(action, true))
        {
            return false;
        }

        GameplayActionDefinition definition = action.Definition!;
        action.Component = this;
        _actionsById.Add(definition.Id, action);
        if (action.GetParent() is null)
        {
            AddChild(action);
        }

        return true;
    }

    public GameplayAction? ResolveAction(StringName actionId) =>
        actionId is not null && _actionsById.TryGetValue(actionId, out GameplayAction? action)
            ? action
            : null;

    public bool RemoveAction(StringName actionId)
    {
        GameplayAction? action = ResolveAction(actionId);
        if (action is null)
        {
            return false;
        }

        _actionsById.Remove(actionId);
        Actions.Remove(action);
        if (IsActionExecuting(actionId))
        {
            _retiringActions.Add(action);
        }
        else
        {
            FinalizeRemoval(action);
        }

        return true;
    }

    public GameplayActionAvailability EvaluateAction(
        StringName actionId,
        Node? instigator = null,
        Node? requester = null,
        GameplayActionInvocationKind invocationKind = GameplayActionInvocationKind.Programmatic
    )
    {
        GameplayAction? action = ResolveAction(actionId);
        if (action?.Definition is null || action.Executor is null)
        {
            return new GameplayActionBlocked(NotConfiguredReason);
        }

        GameplayActionContext context = new(instigator, requester, this, action, invocationKind);
        return EvaluateRules(action.Rules, context);
    }

    public GameplayActionExecutionResult ExecuteProgrammatic(
        StringName actionId,
        out ulong executionId,
        Node? instigator = null,
        Node? requester = null
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

        GameplayActionAvailability availability = EvaluateAction(
            actionId,
            instigator,
            requester,
            GameplayActionInvocationKind.Programmatic
        );
        if (availability is not GameplayActionAllowed)
        {
            return new GameplayActionExecutionRejected(availability.DescribeRefusal());
        }

        ActiveExecution? reservation = ReserveExecutionCore(action, instigator, requester);
        if (reservation is null)
        {
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

    public bool CompleteExecution(ulong executionId)
    {
        ActiveExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        GameplayActionExecutionContext context = BuildExecutionContext(execution.Value);
        execution.Value.Action.Executor?.OnExecutionCompleted(context);
        FinalizeRetiredAction(execution.Value.Action);
        return true;
    }

    public bool CancelExecution(ulong executionId, string reason = "")
    {
        ActiveExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        GameplayActionExecutionContext context = BuildExecutionContext(execution.Value);
        execution.Value.Action.Executor?.OnExecutionCancelled(context, reason);
        FinalizeRetiredAction(execution.Value.Action);
        return true;
    }

    public bool FailExecution(ulong executionId, string reason)
    {
        ActiveExecution? execution = EndExecutionCore(executionId);
        if (execution is null)
        {
            return false;
        }

        GameplayActionExecutionContext context = BuildExecutionContext(execution.Value);
        execution.Value.Action.Executor?.OnExecutionFailed(context, reason);
        FinalizeRetiredAction(execution.Value.Action);
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

        ActiveExecution execution = new(
            _nextExecutionId++,
            action,
            group,
            instigator,
            requester,
            GameplayActionInvocationKind.Programmatic
        );
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
                return;
            case GameplayActionExecutionCompleted:
                _executionsById.Remove(execution.Id);
                execution.Action.Executor?.OnExecutionCompleted(BuildExecutionContext(execution));
                break;
            case GameplayActionExecutionFailed failed:
                _executionsById.Remove(execution.Id);
                execution.Action.Executor?.OnExecutionFailed(
                    BuildExecutionContext(execution),
                    failed.Reason
                );
                break;
            case GameplayActionExecutionRejected:
                _executionsById.Remove(execution.Id);
                break;
        }

        FinalizeRetiredAction(execution.Action);
    }

    private ActiveExecution? EndExecutionCore(ulong executionId)
    {
        if (!IsAuthoritative || !_executionsById.Remove(executionId, out ActiveExecution execution))
        {
            return null;
        }

        return execution;
    }

    private GameplayActionExecutionContext BuildExecutionContext(in ActiveExecution execution) =>
        new(
            execution.Id,
            execution.Instigator,
            execution.Requester,
            this,
            execution.Action,
            execution.InvocationKind
        );

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
        Node? Requester,
        GameplayActionInvocationKind InvocationKind
    );
}
