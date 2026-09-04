using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions.Runtime.Access;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;

namespace QuestWorld.GameplayActions.Runtime.Runner;

[GlobalClass]
public partial class GameplayActionRunner : Node
{
    [Signal]
    public delegate void GameplayActionRequestedEventHandler(Node component, StringName actionId);

    [Signal]
    public delegate void GameplayActionRejectedEventHandler(
        Node component,
        StringName actionId,
        string reason
    );

    [Signal]
    public delegate void GameplayActionStartedEventHandler(
        Node component,
        StringName actionId,
        long executionId
    );

    [Signal]
    public delegate void GameplayActionProgressedEventHandler(
        Node component,
        StringName actionId,
        long executionId
    );

    [Signal]
    public delegate void GameplayActionCompletedEventHandler(
        Node component,
        StringName actionId,
        long executionId
    );

    [Signal]
    public delegate void GameplayActionCancelledEventHandler(
        Node component,
        StringName actionId,
        long executionId,
        string reason
    );

    [Signal]
    public delegate void GameplayActionFailedEventHandler(
        Node component,
        StringName actionId,
        long executionId,
        string reason
    );

    [Signal]
    public delegate void GameplayActionBindingInvalidatedEventHandler(long bindingId);

    private readonly GameplayActionBindingStore _bindings;
    private readonly GameplayActionGestureResolver _gestures;
    private readonly GameplayActionRequestPipeline _requests;
    private readonly Dictionary<StringName, IGameplayActionAccessProvider> _accessProviders = new();
    private readonly List<StringName> _relevantInputs = new();
    private GameplayActionComponent? _observedOwnedActionComponent;

    [Export]
    public GameplayActionComponent? OwnedActionComponent { get; set; }

    [Export]
    public Node? Instigator { get; set; }

    [ExportGroup("Network")]
    [Export]
    public int ServerPeerId { get; set; } = 1;

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

    public bool IsLocallyControlled =>
        Multiplayer is null
        || Multiplayer.MultiplayerPeer is null
        || Multiplayer.GetUniqueId() == OwnerPeerId;

    public override void _Ready()
    {
        _requests.Ready();
        ObserveOwnedActionComponent();
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

    public bool UnbindAction(ulong bindingId) => _bindings.Remove(bindingId);

    public int UnbindSource(GodotObject source) => _bindings.RemoveSource(source);

    public IReadOnlyList<GameplayActionBinding> GetBindings() => _bindings.GetBindings();

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

    public GameplayActionAvailability GetBindingAvailability(ulong bindingId) =>
        _bindings.GetAvailability(bindingId);

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

    public void InvalidateSource(GodotObject source)
    {
        List<ulong> affected = FindBindingIds(binding => binding.Source == source);
        IReadOnlyList<GameplayActionBindingCandidate> automaticEdges = _bindings.InvalidateSource(
            source
        );
        NotifyBindingsInvalidated(affected);
        RequestAutomaticEdges(automaticEdges);
    }

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

    public bool UnregisterAccessProvider(
        StringName providerId,
        IGameplayActionAccessProvider provider
    ) =>
        _accessProviders.TryGetValue(providerId, out IGameplayActionAccessProvider? registered)
        && registered == provider
        && _accessProviders.Remove(providerId);

    public bool TryStartActionInput(StringName inputActionName) =>
        _gestures.TryStart(inputActionName);

    public bool TryEndActionInput(StringName inputActionName) => _gestures.TryEnd(inputActionName);

    public void AdvanceGestures(float delta) => _gestures.Advance(delta);

    public bool TryGetGestureProgress(out StringName inputActionName, out float progress) =>
        _gestures.TryGetProgress(out inputActionName, out progress);

    public void ValidateSustainedExecutions() => _requests.ValidateSustainedExecutions();

    public override void _Process(double delta)
    {
        AdvanceGestures((float)delta);
        if (IsAuthoritative)
        {
            ValidateSustainedExecutions();
        }
    }

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

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryStartAction(NodePath componentPath, StringName actionId) =>
        _requests.ServerTryStartAction(componentPath, actionId);

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryCancelAction(NodePath componentPath, StringName actionId) =>
        _requests.ServerTryCancelAction(componentPath, actionId);

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientActionRejected(NodePath componentPath, StringName actionId, string reason) =>
        _requests.ClientActionRejected(componentPath, actionId, reason);

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

    private Node? ResolveInstigator() => Instigator ?? GetParent();

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
