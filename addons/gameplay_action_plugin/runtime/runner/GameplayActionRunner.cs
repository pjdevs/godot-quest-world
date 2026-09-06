using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Access;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;

namespace QuestWorld.GameplayActions.Runtime.Runner;

/// <summary>Owns local action bindings, input gestures, request access and requester networking.</summary>
[GlobalClass]
public partial class GameplayActionRunner : Node
{
    /// <summary>Emitted when this runner submits an action request.</summary>
    [Signal]
    public delegate void GameplayActionRequestedEventHandler(Node component, StringName actionId);

    /// <summary>Emitted when authority refuses this runner's action request.</summary>
    [Signal]
    public delegate void GameplayActionRejectedEventHandler(
        Node component,
        StringName actionId,
        string reason
    );

    /// <summary>Emitted when authority acknowledges that this runner's action started.</summary>
    [Signal]
    public delegate void GameplayActionStartedEventHandler(
        Node component,
        StringName actionId,
        long executionId
    );

    /// <summary>Emitted when requester-visible execution progress changes.</summary>
    [Signal]
    public delegate void GameplayActionProgressedEventHandler(
        Node component,
        StringName actionId,
        long executionId
    );

    /// <summary>Emitted when this runner's requested execution completes.</summary>
    [Signal]
    public delegate void GameplayActionCompletedEventHandler(
        Node component,
        StringName actionId,
        long executionId
    );

    /// <summary>Emitted when this runner's requested execution is cancelled.</summary>
    [Signal]
    public delegate void GameplayActionCancelledEventHandler(
        Node component,
        StringName actionId,
        long executionId,
        string reason
    );

    /// <summary>Emitted when this runner's requested execution fails.</summary>
    [Signal]
    public delegate void GameplayActionFailedEventHandler(
        Node component,
        StringName actionId,
        long executionId,
        string reason
    );

    /// <summary>Emitted after a cached binding availability has been explicitly re-evaluated.</summary>
    [Signal]
    public delegate void GameplayActionBindingInvalidatedEventHandler(long bindingId);

    private readonly GameplayActionBindingStore _bindings;
    private readonly GameplayActionGestureResolver _gestures;
    private readonly GameplayActionRequestPipeline _requests;
    private readonly Dictionary<StringName, IGameplayActionAccessProvider> _accessProviders = new();
    private readonly List<StringName> _relevantInputs = new();
    private GameplayActionComponent? _observedOwnedActionComponent;
    private int _serverPeerId = 1;
    private bool _hasKnownLocalControl;
    private bool _lastKnownLocalControl;

    /// <summary>Gets or sets the component whose input actions are automatically bound as owned actions.</summary>
    [Export]
    public GameplayActionComponent? OwnedActionComponent { get; set; }

    /// <summary>Gets or sets the gameplay actor attributed to requests; the parent is used when null.</summary>
    [Export]
    public Node? Instigator { get; set; }

    /// <summary>Gets or sets the authoritative peer that owns this runner's RPC endpoints.</summary>
    [ExportGroup("Network")]
    [Export]
    public int ServerPeerId
    {
        get => _serverPeerId;
        set
        {
            _serverPeerId = value;
            ApplyNetworkAuthority();
        }
    }

    /// <summary>Gets or sets the peer allowed to drive local input for this runner.</summary>
    [Export]
    public int OwnerPeerId { get; set; } = 1;

    public GameplayActionRunner()
    {
        _bindings = new GameplayActionBindingStore(EvaluateBinding);
        _requests = new GameplayActionRequestPipeline(this, CanAccess, ResolveInstigator);
        _gestures = new GameplayActionGestureResolver(
            _bindings,
            RequestBest,
            _requests.CancelSustainedInput
        );
    }

    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    internal bool IsAuthoritativeRunner => IsAuthoritative;

    /// <summary>Gets whether this peer is allowed to sample and submit local input for this runner.</summary>
    public bool IsLocallyControlled
    {
        get
        {
            if (Multiplayer is null || Multiplayer.MultiplayerPeer is null)
            {
                return _hasKnownLocalControl ? _lastKnownLocalControl : OwnerPeerId == 1;
            }

            _lastKnownLocalControl = OwnerPeerId == (int)Multiplayer.GetUniqueId();
            _hasKnownLocalControl = true;
            return _lastKnownLocalControl;
        }
    }

    /// <summary>Initializes RPC authority, requester state and automatic owned-action bindings.</summary>
    public override void _Ready()
    {
        ApplyNetworkAuthority();
        _requests.Ready();
        ObserveOwnedActionComponent();
    }

    private void ApplyNetworkAuthority()
    {
        if (!IsInsideTree() || ServerPeerId <= 0 || GetMultiplayerAuthority() == ServerPeerId)
        {
            return;
        }

        SetMultiplayerAuthority(ServerPeerId);
    }

    private void ObserveOwnedActionComponent()
    {
        if (_observedOwnedActionComponent == OwnedActionComponent)
        {
            return;
        }

        if (_observedOwnedActionComponent is not null)
        {
            _observedOwnedActionComponent.GameplayActionAdded -= OnOwnedActionAdded;
            _observedOwnedActionComponent.GameplayActionRemoved -= OnOwnedActionRemoved;
        }

        _observedOwnedActionComponent = OwnedActionComponent;
        if (_observedOwnedActionComponent is null)
        {
            return;
        }

        _observedOwnedActionComponent.GameplayActionAdded += OnOwnedActionAdded;
        _observedOwnedActionComponent.GameplayActionRemoved += OnOwnedActionRemoved;
        foreach (GameplayAction action in _observedOwnedActionComponent.Actions)
        {
            BindOwnedInputAction(action);
        }
    }

    private void OnOwnedActionAdded(GameplayAction action) => BindOwnedInputAction(action);

    private void OnOwnedActionRemoved(GameplayAction action) => UnbindSource(action);

    private void BindOwnedInputAction(GameplayAction action)
    {
        if (
            action is not InputGameplayAction inputAction
            || inputAction.DefaultBindingConfig is not GameplayActionBindingConfig config
            || inputAction.Definition is null
        )
        {
            return;
        }

        BindAction(OwnedActionComponent!, inputAction.Definition.Id, inputAction, config);
    }

    /// <summary>Creates one runner-local binding to an action still owned by its component.</summary>
    /// <returns>The created binding, or null when the binding configuration is invalid.</returns>
    public GameplayActionBinding? BindAction(
        GameplayActionComponent component,
        StringName actionId,
        GodotObject source,
        GameplayActionBindingConfig config,
        Variant presentationContext = default
    )
    {
        GameplayActionBinding? binding = _bindings.Add(
            component,
            actionId,
            source,
            config,
            presentationContext,
            out bool automaticEdge
        );
        if (automaticEdge && binding is not null)
        {
            RequestAutomaticEdges(
                new[] { new GameplayActionBindingCandidate(binding, new GameplayActionAllowed()) }
            );
        }

        return binding;
    }

    /// <summary>Removes one local binding by its runner-local identifier.</summary>
    public bool UnbindAction(ulong bindingId) => _bindings.Remove(bindingId);

    /// <summary>Removes every local binding owned by the supplied cleanup source.</summary>
    public int UnbindSource(GodotObject source) => _bindings.RemoveSource(source);

    /// <summary>Returns a snapshot of all bindings currently owned by this runner.</summary>
    public IReadOnlyList<GameplayActionBinding> GetBindings() => _bindings.GetBindings();

    /// <summary>Finds the binding matching one component/action/source tuple.</summary>
    public bool TryGetBinding(
        GameplayActionComponent component,
        StringName actionId,
        GodotObject source,
        out GameplayActionBinding? binding
    ) => _bindings.TryGet(component, actionId, source, out binding);

    /// <summary>Returns Input Map actions the locally controlled game loop should currently sample.</summary>
    /// <remarks>Automatic bindings are excluded; consumed/sustained inputs remain until released.</remarks>
    public IReadOnlyList<StringName> GetRelevantInputs()
    {
        _relevantInputs.Clear();
        if (!IsLocallyControlled)
        {
            return _relevantInputs;
        }

        foreach (GameplayActionBinding binding in _bindings.GetBindings())
        {
            if (binding.ActivationMode != GameplayActionActivationMode.Automatic)
            {
                AddRelevantInput(binding.InputActionName);
            }
        }

        foreach (StringName consumed in _gestures.GetConsumedInputs())
        {
            AddRelevantInput(consumed);
        }

        return _relevantInputs;
    }

    private void AddRelevantInput(StringName inputActionName)
    {
        if (
            inputActionName is not null
            && !inputActionName.IsEmpty
            && !_relevantInputs.Contains(inputActionName)
        )
        {
            _relevantInputs.Add(inputActionName);
        }
    }

    /// <summary>Gets the last cached availability for one binding.</summary>
    public GameplayActionAvailability GetBindingAvailability(ulong bindingId) =>
        _bindings.GetAvailability(bindingId);

    /// <summary>Re-evaluates one binding and triggers an automatic edge when it becomes eligible.</summary>
    public void InvalidateBinding(ulong bindingId)
    {
        bool exists = _bindings.TryGet(bindingId, out _);
        IReadOnlyList<GameplayActionBindingCandidate> automaticEdges = _bindings.InvalidateBinding(
            bindingId
        );
        if (exists)
        {
            NotifyBindingInvalidated(bindingId);
        }

        RequestAutomaticEdges(automaticEdges);
    }

    /// <summary>Re-evaluates every binding created by one cleanup/invalidation source.</summary>
    public void InvalidateSource(GodotObject source)
    {
        List<ulong> affected = FindBindingIds(binding => binding.Source == source);
        IReadOnlyList<GameplayActionBindingCandidate> automaticEdges = _bindings.InvalidateSource(
            source
        );
        NotifyBindingsInvalidated(affected);
        RequestAutomaticEdges(automaticEdges);
    }

    /// <summary>Re-evaluates every binding referring to one action occurrence identity.</summary>
    public void InvalidateAction(GameplayActionComponent component, StringName actionId)
    {
        List<ulong> affected = FindBindingIds(binding =>
            binding.Component == component && binding.ActionId == actionId
        );
        IReadOnlyList<GameplayActionBindingCandidate> automaticEdges = _bindings.InvalidateAction(
            component,
            actionId
        );
        NotifyBindingsInvalidated(affected);
        RequestAutomaticEdges(automaticEdges);
    }

    /// <summary>Registers the domain adapter used to validate externally owned actions with this ID.</summary>
    public void RegisterAccessProvider(
        StringName providerId,
        IGameplayActionAccessProvider provider
    )
    {
        if (providerId is null || providerId.IsEmpty || provider is null)
        {
            return;
        }

        _accessProviders[providerId] = provider;
    }

    /// <summary>Removes an access provider only when the same instance is still registered.</summary>
    public bool UnregisterAccessProvider(
        StringName providerId,
        IGameplayActionAccessProvider provider
    ) =>
        _accessProviders.TryGetValue(providerId, out IGameplayActionAccessProvider? registered)
        && registered == provider
        && _accessProviders.Remove(providerId);

    /// <summary>Feeds one input press edge into the local gesture resolver.</summary>
    public bool TryStartActionInput(StringName inputActionName) =>
        _gestures.TryStart(inputActionName);

    /// <summary>Feeds one input release edge into the local gesture/sustained-request resolver.</summary>
    public bool TryEndActionInput(StringName inputActionName) => _gestures.TryEnd(inputActionName);

    /// <summary>Advances active local hold gestures by the supplied delta in seconds.</summary>
    public void AdvanceGestures(float delta) => _gestures.Advance(delta);

    /// <summary>Gets local hold-selection progress for a binding captured by an active gesture.</summary>
    public bool TryGetBindingHoldProgress(ulong bindingId, out float progress, out float elapsed) =>
        _gestures.TryGetBindingHoldProgress(bindingId, out progress, out elapsed);

    /// <summary>On authority, cancels requested executions whose sustained access has been lost.</summary>
    public void ValidateSustainedExecutions() => _requests.ValidateSustainedExecutions();

    /// <summary>Advances local gestures and authority-side sustained access validation.</summary>
    public override void _Process(double delta)
    {
        AdvanceGestures((float)delta);
        if (IsAuthoritative)
        {
            ValidateSustainedExecutions();
        }
    }

    /// <summary>Releases requester state and subscriptions when the runner leaves the tree.</summary>
    public override void _ExitTree()
    {
        _requests.Exit();
    }

    private GameplayActionAvailability EvaluateBinding(GameplayActionBinding binding)
    {
        GameplayAction? action = binding.Component.ResolveAction(binding.ActionId);
        if (action is null || !CanAccess(binding.Component, action, sustained: false))
        {
            return new GameplayActionHidden();
        }

        return binding.Component.EvaluateAction(binding.ActionId, ResolveInstigator(), this);
    }

    private bool CanAccess(GameplayActionComponent component, GameplayAction action, bool sustained)
    {
        if (component == OwnedActionComponent)
        {
            return true;
        }

        StringName providerId = action.AccessProviderId;
        if (
            providerId is null
            || providerId.IsEmpty
            || !_accessProviders.TryGetValue(
                providerId,
                out IGameplayActionAccessProvider? provider
            )
        )
        {
            return false;
        }

        GameplayActionAccessContext context = new(this, component, action);
        return provider.CanRequest(context);
    }

    private void RequestAutomaticEdges(IReadOnlyList<GameplayActionBindingCandidate> automaticEdges)
    {
        if (automaticEdges.Count > 0)
        {
            RequestBest(automaticEdges, GameplayActionActivationMode.Automatic);
        }
    }

    private List<ulong> FindBindingIds(System.Func<GameplayActionBinding, bool> matches)
    {
        List<ulong> affected = new();
        foreach (GameplayActionBinding binding in _bindings.GetBindings())
        {
            if (matches(binding))
            {
                affected.Add(binding.Id);
            }
        }

        return affected;
    }

    private void NotifyBindingsInvalidated(IReadOnlyList<ulong> bindingIds)
    {
        foreach (ulong bindingId in bindingIds)
        {
            NotifyBindingInvalidated(bindingId);
        }
    }

    private void NotifyBindingInvalidated(ulong bindingId) =>
        EmitSignal(SignalName.GameplayActionBindingInvalidated, checked((long)bindingId));

    private bool RequestBest(
        IReadOnlyList<GameplayActionBindingCandidate> candidates,
        GameplayActionActivationMode? activationMode
    )
    {
        List<GameplayActionBindingCandidate> matching = new();
        foreach (GameplayActionBindingCandidate candidate in candidates)
        {
            if (
                candidate.Availability is not GameplayActionHidden
                && (
                    activationMode is null
                    || candidate.Binding.ActivationMode == activationMode.Value
                )
            )
            {
                matching.Add(candidate);
            }
        }

        matching.Sort(CompareCandidates);
        if (matching.Count == 0 || matching[0].Availability is not GameplayActionAllowed)
        {
            return false;
        }

        WarnEqualPriority(matching);
        return _requests.TryRequestBinding(matching[0].Binding);
    }

    /// <summary>Reliable server RPC endpoint used by local request transport to start an action.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryStartAction(NodePath componentPath, StringName actionId) =>
        _requests.ServerTryStartAction(componentPath, actionId);

    /// <summary>Reliable server RPC endpoint used by requester input release/cancellation.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryCancelAction(NodePath componentPath, StringName actionId) =>
        _requests.ServerTryCancelAction(componentPath, actionId);

    /// <summary>Authority RPC endpoint reconciling a refused local request.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientActionRejected(NodePath componentPath, StringName actionId, string reason) =>
        _requests.ClientActionRejected(componentPath, actionId, reason);

    /// <summary>Authority RPC endpoint confirming the execution ID and optional progress sample.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientActionStarted(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        int visibility,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    ) =>
        _requests.ClientActionStarted(
            componentPath,
            actionId,
            executionId,
            visibility,
            hasProgress,
            progressBase,
            progressPerSecond,
            revision
        );

    /// <summary>Authority RPC endpoint applying a requester-only progress correction.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientActionProgress(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    ) =>
        _requests.ClientActionProgress(
            componentPath,
            actionId,
            executionId,
            hasProgress,
            progressBase,
            progressPerSecond,
            revision
        );

    /// <summary>Authority RPC endpoint reconciling successful terminal completion.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientActionCompleted(
        NodePath componentPath,
        StringName actionId,
        long executionId
    ) => _requests.ClientActionCompleted(componentPath, actionId, executionId);

    /// <summary>Authority RPC endpoint reconciling terminal cancellation.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientActionCancelled(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        string reason
    ) => _requests.ClientActionCancelled(componentPath, actionId, executionId, reason);

    /// <summary>Authority RPC endpoint reconciling terminal failure.</summary>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientActionFailed(
        NodePath componentPath,
        StringName actionId,
        long executionId,
        string reason
    ) => _requests.ClientActionFailed(componentPath, actionId, executionId, reason);

    internal void NotifyExecutionStarted(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId
    ) => _requests.NotifyExecutionStarted(component, action, executionId);

    internal void NotifyExecutionProgress(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId
    ) => _requests.NotifyExecutionProgress(component, action, executionId);

    internal void NotifyExecutionCompleted(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId
    ) => _requests.NotifyExecutionCompleted(component, action, executionId);

    internal void NotifyExecutionCancelled(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId,
        string reason
    ) => _requests.NotifyExecutionCancelled(component, action, executionId, reason);

    internal void NotifyExecutionFailed(
        GameplayActionComponent component,
        GameplayAction action,
        ulong executionId,
        string reason
    ) => _requests.NotifyExecutionFailed(component, action, executionId, reason);

    internal void NotifyExecutionRejected(
        GameplayActionComponent component,
        GameplayAction action,
        string reason
    ) => _requests.NotifyExecutionRejected(component, action, reason);

    internal Node? ResolveInstigator() => Instigator ?? GetParent();

    private int CompareCandidates(
        GameplayActionBindingCandidate left,
        GameplayActionBindingCandidate right
    )
    {
        int availability = AvailabilityRank(right.Availability)
            .CompareTo(AvailabilityRank(left.Availability));
        if (availability != 0)
        {
            return availability;
        }

        int priority = right.Binding.Priority.CompareTo(left.Binding.Priority);
        return priority != 0
            ? priority
            : string.CompareOrdinal(StableIdentity(left.Binding), StableIdentity(right.Binding));
    }

    private static int AvailabilityRank(GameplayActionAvailability availability) =>
        availability switch
        {
            GameplayActionAllowed => 2,
            GameplayActionBlocked => 1,
            _ => 0,
        };

    private string StableIdentity(GameplayActionBinding binding) =>
        $"{_requests.GetNetworkPath(binding.Component)}:{binding.ActionId}";

    private void WarnEqualPriority(IReadOnlyList<GameplayActionBindingCandidate> candidates)
    {
        if (
            candidates.Count < 2
            || AvailabilityRank(candidates[0].Availability)
                != AvailabilityRank(candidates[1].Availability)
            || candidates[0].Binding.Priority != candidates[1].Binding.Priority
        )
        {
            return;
        }

        GD.PushWarning(
            $"{GetPath()}: gameplay action bindings '{StableIdentity(candidates[0].Binding)}' and "
                + $"'{StableIdentity(candidates[1].Binding)}' share an input priority."
        );
    }
}
