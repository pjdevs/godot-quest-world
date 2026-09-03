using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;

namespace QuestWorld.GameplayActions.Runtime.Runner;

internal sealed class GameplayActionRequestPipeline(
    GameplayActionRunner owner,
    Func<GameplayActionComponent, GameplayAction, bool, bool> canAccess,
    Func<Node?> resolveInstigator
)
{
    private const string ReleasedReason = "The gameplay action input was released.";
    private const string AccessLostReason = "The requester lost access to the gameplay action.";
    private const string RequesterLostReason = "The gameplay action requester left.";
    private readonly GameplayActionRunner _owner = owner;
    private readonly Dictionary<StringName, HashSet<GameplayActionRequestKey>> _sustainedInputs =
        new();
    private readonly List<GameplayActionRequestedExecution> _requestedExecutions = new();
    private readonly HashSet<GameplayActionRequestKey> _pendingRequests = new();
    private readonly Dictionary<GameplayActionRequestKey, ulong> _acknowledgedExecutions = new();
    private MultiplayerApi? _watchedMultiplayer;
    private bool _ownerPeerLost;

    public void Ready()
    {
        SubscribeToPeerDisconnected();
    }

    public void Exit()
    {
        UnsubscribeFromPeerDisconnected();
        if (_owner.IsAuthoritativeRunner)
        {
            CancelRequesterOwnedExecutions(RequesterLostReason);
        }
    }

    public void ValidateSustainedExecutions()
    {
        for (int index = _requestedExecutions.Count - 1; index >= 0; index--)
        {
            GameplayActionRequestedExecution execution = _requestedExecutions[index];
            if (!execution.Component.IsExecutionActive(execution.ExecutionId))
            {
                _requestedExecutions.RemoveAt(index);
                continue;
            }

            if (!execution.RequiresRequesterPresence || HasSustainedAccess(execution))
            {
                continue;
            }

            _requestedExecutions.RemoveAt(index);
            execution.Component.CancelExecution(execution.ExecutionId, AccessLostReason);
        }
    }

    public bool TryRequestBinding(GameplayActionBinding binding)
    {
        GameplayAction? action = binding.Component.ResolveAction(binding.ActionId);
        if (action is null)
        {
            return false;
        }

        GameplayActionRequestKey request = new(binding.Component, binding.ActionId);
        if (
            _pendingRequests.Contains(request)
            || _acknowledgedExecutions.ContainsKey(request)
            || binding.Component.HasLocalExecution(action)
            || binding.Component.HasLocalExecutionInGroup(action.GetHostConcurrencyGroup())
            || HasPendingRequestInGroup(binding.Component, action.GetHostConcurrencyGroup())
            || HasAcknowledgedExecutionInGroup(binding.Component, action.GetHostConcurrencyGroup())
        )
        {
            return false;
        }

        _owner.EmitSignal(
            GameplayActionRunner.SignalName.GameplayActionRequested,
            binding.Component,
            binding.ActionId
        );
        if (!_owner.IsAuthoritativeRunner)
        {
            _pendingRequests.Add(request);
            PredictExecution(binding, action);
            if (binding.InputRequirement == GameplayActionInputRequirement.Pressed)
            {
                RememberSustainedInput(
                    binding.InputActionName,
                    binding.Component,
                    binding.ActionId
                );
            }

            _owner.RpcId(
                _owner.ServerPeerId,
                nameof(ServerTryStartAction),
                GetNetworkPath(binding.Component),
                binding.ActionId
            );
            return true;
        }

        GameplayActionExecutionResult result = TryStartAuthoritatively(
            binding.Component,
            binding.ActionId,
            _owner.OwnerPeerId,
            GetNetworkPath(binding.Component)
        );
        if (
            result is GameplayActionExecutionRunning
            && binding.InputRequirement == GameplayActionInputRequirement.Pressed
        )
        {
            RememberSustainedInput(binding.InputActionName, binding.Component, binding.ActionId);
        }

        return result is GameplayActionExecutionRunning or GameplayActionExecutionCompleted;
    }

    public bool CancelSustainedInput(StringName input)
    {
        if (!_sustainedInputs.Remove(input, out HashSet<GameplayActionRequestKey>? requests))
        {
            return false;
        }

        bool cancelled = false;
        foreach (GameplayActionRequestKey request in requests)
        {
            if (!_owner.IsAuthoritativeRunner)
            {
                ClearLocalRequestPresentation(request, includeAcknowledged: true);
                _owner.RpcId(
                    _owner.ServerPeerId,
                    nameof(ServerTryCancelAction),
                    GetNetworkPath(request.Component),
                    request.ActionId
                );
                cancelled = true;
            }
            else
            {
                cancelled |= CancelRequestedExecution(request.Component, request.ActionId);
            }
        }

        return cancelled;
    }

    private bool CancelRequestedExecution(GameplayActionComponent component, StringName actionId)
    {
        for (int index = _requestedExecutions.Count - 1; index >= 0; index--)
        {
            GameplayActionRequestedExecution execution = _requestedExecutions[index];
            if (execution.Component != component || execution.ActionId != actionId)
            {
                continue;
            }

            _requestedExecutions.RemoveAt(index);
            return component.CancelExecution(execution.ExecutionId, ReleasedReason);
        }

        return false;
    }

    private void RememberSustainedInput(
        StringName input,
        GameplayActionComponent component,
        StringName actionId
    )
    {
        if (!_sustainedInputs.TryGetValue(input, out HashSet<GameplayActionRequestKey>? requests))
        {
            requests = new HashSet<GameplayActionRequestKey>();
            _sustainedInputs.Add(input, requests);
        }

        requests.Add(new GameplayActionRequestKey(component, actionId));
    }

    private bool HasSustainedAccess(in GameplayActionRequestedExecution execution)
    {
        GameplayAction? action = execution.Component.ResolveAction(execution.ActionId);
        return action is not null && canAccess(execution.Component, action, true);
    }

    private void CancelRequesterOwnedExecutions(string reason)
    {
        for (int index = _requestedExecutions.Count - 1; index >= 0; index--)
        {
            GameplayActionRequestedExecution execution = _requestedExecutions[index];
            if (!execution.RequiresRequesterPresence)
            {
                continue;
            }

            _requestedExecutions.RemoveAt(index);
            execution.Component.CancelExecution(execution.ExecutionId, reason);
        }
    }

    private void PredictExecution(GameplayActionBinding binding, GameplayAction action)
    {
        GameplayActionProgressSample? sample = action.Executor?.GetPredictionSample(
            new GameplayActionContext(
                resolveInstigator(),
                _owner,
                binding.Component,
                action,
                GameplayActionInvocationKind.PlayerRequest
            )
        );
        if (sample is GameplayActionProgressSample prediction)
        {
            binding.Component.AddPendingExecutionPresentation(binding.ActionId, prediction);
        }
    }

    private bool HasPendingRequestInGroup(GameplayActionComponent component, StringName group)
    {
        foreach (GameplayActionRequestKey pending in _pendingRequests)
        {
            if (
                pending.Component == component
                && component.ResolveAction(pending.ActionId)?.GetHostConcurrencyGroup() == group
            )
            {
                return true;
            }
        }

        return false;
    }

    private bool HasAcknowledgedExecutionInGroup(
        GameplayActionComponent component,
        StringName group
    )
    {
        foreach (GameplayActionRequestKey acknowledged in _acknowledgedExecutions.Keys)
        {
            if (
                acknowledged.Component == component
                && component.ResolveAction(acknowledged.ActionId)?.GetHostConcurrencyGroup()
                    == group
            )
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Drops the local presentation of one request.</summary>
    /// <remarks>
    /// A prediction is always dropped: nothing acknowledged it. An acknowledged execution is only
    /// dropped when the requester itself gave the action up, such as releasing a sustained input. A
    /// refusal must leave it alone, because the refusal answers the latest request and not the
    /// execution already running — a player pressing again mid-action would otherwise erase a bar
    /// the authority is still driving.
    /// </remarks>
    private void ClearLocalRequestPresentation(
        in GameplayActionRequestKey request,
        bool includeAcknowledged
    )
    {
        _pendingRequests.Remove(request);
        if (
            request.Component.TryGetExecutionPresentation(
                request.ActionId,
                out GameplayActionExecutionPresentation presentation
            )
        )
        {
            if (presentation.ExecutionId == 0ul)
            {
                request.Component.RemovePendingExecution(request.ActionId);
            }
            else if (includeAcknowledged)
            {
                request.Component.RemoveRequesterExecution(
                    request.ActionId,
                    presentation.ExecutionId
                );
            }
        }
    }

    public void ServerTryStartAction(NodePath componentPath, StringName actionId)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        if (!ValidateSender(senderPeerId))
        {
            RejectRequest(
                senderPeerId,
                componentPath,
                actionId,
                GameplayActionAvailabilityExtensions.UnavailableReason
            );
            return;
        }

        GameplayActionComponent? component =
            ResolveNetworkPath(componentPath) as GameplayActionComponent;
        if (component is null)
        {
            RejectRequest(
                senderPeerId,
                componentPath,
                actionId,
                GameplayActionAvailabilityExtensions.UnavailableReason
            );
            return;
        }

        TryStartAuthoritatively(component, actionId, senderPeerId, componentPath);
    }

    public void ServerTryCancelAction(NodePath componentPath, StringName actionId)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        if (!ValidateSender(senderPeerId))
        {
            return;
        }

        GameplayActionComponent? component =
            ResolveNetworkPath(componentPath) as GameplayActionComponent;
        if (component is not null)
        {
            CancelRequestedExecution(component, actionId);
        }
    }

    public void ClientActionRejected(NodePath componentPath, StringName actionId, string reason)
    {
        GameplayActionComponent? component =
            ResolveNetworkPath(componentPath) as GameplayActionComponent;
        if (component is not null)
        {
            ClearLocalRequestPresentation(
                new GameplayActionRequestKey(component, actionId),
                includeAcknowledged: false
            );
            RemoveSustainedRequest(component, actionId);
        }

        _owner.EmitSignal(
            GameplayActionRunner.SignalName.GameplayActionRejected,
            ToVariant(component),
            actionId,
            reason
        );
    }

    public void ClientActionStarted(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        int visibility,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    )
    {
        if (executionId <= 0)
        {
            return;
        }

        GameplayActionComponent? component =
            ResolveNetworkPath(componentPath) as GameplayActionComponent;
        if (component is null)
        {
            return;
        }

        GameplayActionRequestKey request = new(component, actionId);
        _pendingRequests.Remove(request);
        bool accepted;
        if (
            (GameplayActionExecutionVisibility)visibility
            != GameplayActionExecutionVisibility.AuthorityOnly
        )
        {
            accepted = component.ConfirmRequesterExecution(
                actionId,
                (ulong)executionId,
                hasProgress,
                new GameplayActionProgressSample(progressBase, progressPerSecond, revision)
            );
        }
        else
        {
            component.RemovePendingExecution(actionId);
            accepted = true;
        }

        if (!accepted)
        {
            return;
        }

        _acknowledgedExecutions[request] = (ulong)executionId;

        _owner.EmitSignal(
            GameplayActionRunner.SignalName.GameplayActionStarted,
            ToVariant(component),
            actionId,
            executionId
        );
    }

    public void ClientActionProgress(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    )
    {
        if (executionId <= 0)
        {
            return;
        }

        GameplayActionComponent? component =
            ResolveNetworkPath(componentPath) as GameplayActionComponent;
        if (
            component is null
            || !component.ApplyRequesterProgress(
                actionId,
                (ulong)executionId,
                hasProgress,
                new GameplayActionProgressSample(progressBase, progressPerSecond, revision)
            )
        )
        {
            return;
        }

        _owner.EmitSignal(
            GameplayActionRunner.SignalName.GameplayActionProgressed,
            component,
            actionId,
            executionId
        );
    }

    public void ClientActionCompleted(
        NodePath componentPath,
        StringName actionId,
        long executionId
    ) => EndClientExecution(componentPath, actionId, executionId, ClientEndKind.Completed);

    public void ClientActionCancelled(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        string reason
    ) => EndClientExecution(componentPath, actionId, executionId, ClientEndKind.Cancelled, reason);

    public void ClientActionFailed(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        string reason
    ) => EndClientExecution(componentPath, actionId, executionId, ClientEndKind.Failed, reason);

    internal void NotifyExecutionStarted(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId
    )
    {
        if (!TryBuildAcknowledgement(component, action, out NodePath path, out StringName actionId))
        {
            return;
        }

        component.TryGetProgressSample(
            executionId,
            out bool hasProgress,
            out GameplayActionProgressSample sample
        );
        if (_owner.IsLocallyControlled)
        {
            ClientActionStarted(
                path,
                actionId,
                checked((long)executionId),
                (int)action.ExecutionVisibility,
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
        else if (CanSendToOwner)
        {
            _owner.RpcId(
                _owner.OwnerPeerId,
                nameof(ClientActionStarted),
                path,
                actionId,
                checked((long)executionId),
                (int)action.ExecutionVisibility,
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
    }

    internal void NotifyExecutionProgress(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId
    )
    {
        if (
            !TryBuildAcknowledgement(component, action, out NodePath path, out StringName actionId)
            || !component.TryGetProgressSample(
                executionId,
                out bool hasProgress,
                out GameplayActionProgressSample sample
            )
        )
        {
            return;
        }

        if (_owner.IsLocallyControlled)
        {
            ClientActionProgress(
                path,
                actionId,
                checked((long)executionId),
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
        else if (CanSendToOwner)
        {
            _owner.RpcId(
                _owner.OwnerPeerId,
                nameof(ClientActionProgress),
                path,
                actionId,
                checked((long)executionId),
                hasProgress,
                sample.ProgressBase,
                sample.ProgressPerSecond,
                sample.Revision
            );
        }
    }

    internal void NotifyExecutionCompleted(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId
    ) => SendTerminal(component, action, executionId, ClientEndKind.Completed, string.Empty);

    internal void NotifyExecutionCancelled(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId,
        string reason
    ) => SendTerminal(component, action, executionId, ClientEndKind.Cancelled, reason);

    internal void NotifyExecutionFailed(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId,
        string reason
    ) => SendTerminal(component, action, executionId, ClientEndKind.Failed, reason);

    internal void NotifyExecutionRejected(
        GameplayActionComponent component,
        GameplayAction action,
        string reason
    )
    {
        if (TryBuildAcknowledgement(component, action, out NodePath path, out StringName actionId))
        {
            RejectRequest(_owner.OwnerPeerId, path, actionId, reason);
        }
    }

    private GameplayActionExecutionResult TryStartAuthoritatively(
        GameplayActionComponent component,
        StringName actionId,
        int senderPeerId,
        NodePath componentPath
    )
    {
        if (!ValidateSender(senderPeerId))
        {
            RejectRequest(
                senderPeerId,
                componentPath,
                actionId,
                GameplayActionAvailabilityExtensions.UnavailableReason
            );
            return new GameplayActionExecutionRejected();
        }

        GameplayAction? action = component.ResolveAction(actionId);
        if (action is null || !canAccess(component, action, false))
        {
            RejectRequest(
                senderPeerId,
                componentPath,
                actionId,
                GameplayActionAvailabilityExtensions.UnavailableReason
            );
            return new GameplayActionExecutionRejected();
        }

        GameplayActionExecutionResult result = component.ExecutePlayerRequest(
            actionId,
            out ulong executionId,
            resolveInstigator(),
            _owner
        );
        if (result is GameplayActionExecutionRunning)
        {
            _requestedExecutions.Add(
                new GameplayActionRequestedExecution(
                    component,
                    actionId,
                    executionId,
                    action.Executor?.RequiresRequesterPresence != false
                )
            );
        }

        return result;
    }

    private void SendTerminal(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId,
        ClientEndKind kind,
        string reason
    )
    {
        RemoveRequestedExecution(component, executionId);
        if (!TryBuildAcknowledgement(component, action, out NodePath path, out StringName actionId))
        {
            return;
        }

        if (_owner.IsLocallyControlled)
        {
            EndClientExecution(path, actionId, checked((long)executionId), kind, reason);
            return;
        }

        if (!CanSendToOwner)
        {
            return;
        }

        string method = kind switch
        {
            ClientEndKind.Completed => nameof(ClientActionCompleted),
            ClientEndKind.Cancelled => nameof(ClientActionCancelled),
            _ => nameof(ClientActionFailed),
        };
        if (kind == ClientEndKind.Completed)
        {
            _owner.RpcId(_owner.OwnerPeerId, method, path, actionId, checked((long)executionId));
        }
        else
        {
            _owner.RpcId(
                _owner.OwnerPeerId,
                method,
                path,
                actionId,
                checked((long)executionId),
                reason
            );
        }
    }

    private void EndClientExecution(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        ClientEndKind kind,
        string reason = ""
    )
    {
        if (executionId <= 0)
        {
            return;
        }

        GameplayActionComponent? component =
            ResolveNetworkPath(componentPath) as GameplayActionComponent;
        if (component is null)
        {
            return;
        }

        GameplayActionRequestKey request = new(component, actionId);
        if (
            !_acknowledgedExecutions.TryGetValue(request, out ulong acknowledgedId)
            || acknowledgedId != (ulong)executionId
        )
        {
            return;
        }

        _acknowledgedExecutions.Remove(request);
        component.RemoveRequesterExecution(actionId, (ulong)executionId);
        RemoveSustainedRequest(component, actionId);

        switch (kind)
        {
            case ClientEndKind.Completed:
                _owner.EmitSignal(
                    GameplayActionRunner.SignalName.GameplayActionCompleted,
                    ToVariant(component),
                    actionId,
                    executionId
                );
                break;
            case ClientEndKind.Cancelled:
                _owner.EmitSignal(
                    GameplayActionRunner.SignalName.GameplayActionCancelled,
                    ToVariant(component),
                    actionId,
                    executionId,
                    reason
                );
                break;
            case ClientEndKind.Failed:
                _owner.EmitSignal(
                    GameplayActionRunner.SignalName.GameplayActionFailed,
                    ToVariant(component),
                    actionId,
                    executionId,
                    reason
                );
                break;
        }
    }

    private bool TryBuildAcknowledgement(
        GameplayActionComponent component,
        GameplayAction action,
        out NodePath componentPath,
        out StringName actionId
    )
    {
        componentPath = new NodePath();
        actionId = new StringName();
        if (!_owner.IsAuthoritativeRunner || action.Definition is null)
        {
            return false;
        }

        componentPath = GetNetworkPath(component);
        actionId = action.Definition.Id;
        return true;
    }

    private void RejectRequest(
        int peerId,
        NodePath componentPath,
        StringName actionId,
        string reason
    )
    {
        if (peerId == _owner.OwnerPeerId && _owner.IsLocallyControlled)
        {
            ClientActionRejected(componentPath, actionId, reason);
        }
        else if (peerId > 0 && _owner.Multiplayer?.MultiplayerPeer is not null)
        {
            _owner.RpcId(peerId, nameof(ClientActionRejected), componentPath, actionId, reason);
        }
    }

    private bool ValidateSender(int senderPeerId) => senderPeerId == _owner.OwnerPeerId;

    private int GetRemoteSenderOrOwner()
    {
        if (_owner.Multiplayer is null || _owner.Multiplayer.MultiplayerPeer is null)
        {
            return _owner.OwnerPeerId;
        }

        int sender = (int)_owner.Multiplayer.GetRemoteSenderId();
        return sender > 0 ? sender : _owner.OwnerPeerId;
    }

    private bool CanSendToOwner =>
        _owner.OwnerPeerId > 0
        && !_ownerPeerLost
        && _owner.Multiplayer is not null
        && _owner.Multiplayer.MultiplayerPeer is not null;

    private void RemoveRequestedExecution(GameplayActionComponent component, ulong executionId)
    {
        _requestedExecutions.RemoveAll(execution =>
            execution.Component == component && execution.ExecutionId == executionId
        );
    }

    private void RemoveSustainedRequest(GameplayActionComponent component, StringName actionId)
    {
        GameplayActionRequestKey request = new(component, actionId);
        List<StringName> emptyInputs = new();
        foreach (
            KeyValuePair<
                StringName,
                HashSet<GameplayActionRequestKey>
            > sustained in _sustainedInputs
        )
        {
            sustained.Value.Remove(request);
            if (sustained.Value.Count == 0)
            {
                emptyInputs.Add(sustained.Key);
            }
        }

        foreach (StringName input in emptyInputs)
        {
            _sustainedInputs.Remove(input);
        }
    }

    private void SubscribeToPeerDisconnected()
    {
        if (_owner.Multiplayer is null || _watchedMultiplayer is not null)
        {
            return;
        }

        _watchedMultiplayer = _owner.Multiplayer;
        _watchedMultiplayer.PeerDisconnected += OnPeerDisconnected;
    }

    private void UnsubscribeFromPeerDisconnected()
    {
        if (_watchedMultiplayer is null)
        {
            return;
        }

        _watchedMultiplayer.PeerDisconnected -= OnPeerDisconnected;
        _watchedMultiplayer = null;
    }

    private void OnPeerDisconnected(long peerId)
    {
        if ((int)peerId != _owner.OwnerPeerId)
        {
            return;
        }

        _ownerPeerLost = true;
        if (_owner.IsAuthoritativeRunner)
        {
            CancelRequesterOwnedExecutions(RequesterLostReason);
        }
    }

    private Node? ResolveNetworkPath(NodePath path)
    {
        Node? root = GetNetworkRoot();
        return root is null || path is null || path.IsEmpty ? null : root.GetNodeOrNull(path);
    }

    private static Variant ToVariant(Node? node) => node is null ? default : Variant.From(node);

    public NodePath GetNetworkPath(Node node)
    {
        Node? root = GetNetworkRoot();
        return root is null ? node.GetPath() : root.GetPathTo(node);
    }

    private Node? GetNetworkRoot()
    {
        SceneTree? tree = _owner.GetTree();
        if (tree is null)
        {
            return null;
        }

        return _owner.Multiplayer is SceneMultiplayer scene && !scene.RootPath.IsEmpty
            ? tree.Root.GetNodeOrNull(scene.RootPath)
            : tree.Root;
    }

    private readonly record struct GameplayActionRequestKey(
        GameplayActionComponent Component,
        StringName ActionId
    );

    private readonly record struct GameplayActionRequestedExecution(
        GameplayActionComponent Component,
        StringName ActionId,
        ulong ExecutionId,
        bool RequiresRequesterPresence
    );

    private enum ClientEndKind
    {
        Completed,
        Cancelled,
        Failed,
    }
}
