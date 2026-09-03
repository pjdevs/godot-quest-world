using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Access;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
using QuestWorld.GameplayActions.Runtime.Execution;
using QuestWorld.GameplayActions.Runtime.Runner;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Detection;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Interactor;

internal readonly record struct FocusChangeResult(
    InteractiveComponent? Previous,
    InteractiveComponent? Current,
    bool Changed
);

/// <summary>
/// Spatial Interaction integration for one gameplay-action runner.
/// </summary>
/// <remarks>
/// This node owns detection, focus, spatial access, contextual bindings and Interaction-facing
/// presentation signals. Request routing, prediction, acknowledgements, sustained execution tracking,
/// cancellation and network transport are owned by <see cref="GameplayActionRunner"/>.
/// </remarks>
[GlobalClass]
public partial class InteractionInteractor : Node, IGameplayActionAccessProvider
{
    [Signal]
    public delegate void FocusedInteractiveChangedEventHandler(Node interactive);

    [Signal]
    public delegate void InteractionStatusChangedEventHandler(Node interactive);

    [Signal]
    public delegate void InteractionRequestedEventHandler(Node interactive, StringName actionId);

    [Signal]
    public delegate void InteractionRejectedEventHandler(
        Node interactive,
        StringName actionId,
        string reason
    );

    [Signal]
    public delegate void InteractionStartedEventHandler(
        Node interactive,
        StringName actionId,
        ulong executionId
    );

    [Signal]
    public delegate void InteractionCompletedEventHandler(Node interactive, StringName actionId);

    [Signal]
    public delegate void InteractionCancelledEventHandler(
        Node interactive,
        StringName actionId,
        string reason
    );

    [Signal]
    public delegate void InteractionFailedEventHandler(
        Node interactive,
        StringName actionId,
        string reason
    );

    [Signal]
    public delegate void InteractiveIndicationAddedEventHandler(Node interactive);

    [Signal]
    public delegate void InteractiveIndicationRemovedEventHandler(Node interactive);

    [ExportGroup("Detection")]
    [Export]
    public InteractionDetector? Detector { get; set; }

    [ExportGroup("Network")]
    [Export]
    public int ServerPeerId { get; set; } = 1;

    [Export]
    public int OwnerPeerId { get; set; } = 1;

    [ExportGroup("Actions")]
    [Export]
    public GameplayActionRunner? Runner { get; set; }

    private static readonly Dictionary<ulong, InteractionInteractor> _runnerOwners = new();

    private readonly HashSet<InteractiveComponent> _detectedInteractives = new();
    private readonly HashSet<InteractiveComponent> _detectionBuffer = new();
    private readonly List<InteractiveComponent> _detectionEntered = new();
    private readonly List<InteractiveComponent> _detectionExited = new();
    private readonly List<StringName> _relevantInputs = new();

    private InteractiveComponent? _focusedInteractive;
    private bool _hasKnownLocalControl;
    private bool _lastKnownLocalControl;

    public InteractiveComponent? FocusedInteractive => _focusedInteractive;

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

    public override void _Ready()
    {
        if (Detector is null)
        {
            GD.PushError($"{GetPath()}: InteractionInteractor requires a Detector.");
            SetProcess(false);
        }

        if (Runner is null)
        {
            GD.PushError($"{GetPath()}: InteractionInteractor requires a GameplayActionRunner.");
            SetProcess(false);
            return;
        }

        if (OwnerPeerId <= 0)
        {
            OwnerPeerId =
                Multiplayer is null || Multiplayer.MultiplayerPeer is null
                    ? 1
                    : (int)Multiplayer.GetUniqueId();
        }

        SetMultiplayerAuthority(ServerPeerId);
        SyncRunnerConfiguration();
        Runner.RegisterAccessProvider(InteractionAction.InteractionAccessProviderId, this);
        _runnerOwners[Runner.GetInstanceId()] = this;
        ConnectRunnerSignals();
    }

    public override void _Process(double delta)
    {
        SyncRunnerConfiguration();
        bool locallyControlled = IsLocallyControlled;
        Detector?.SetCandidateSourceActive(locallyControlled);
        if (locallyControlled)
        {
            RecalculateFocus();
            if (_focusedInteractive is not null)
            {
                Runner?.InvalidateSource(_focusedInteractive);
            }
        }
    }

    private void SyncRunnerConfiguration()
    {
        if (Runner is null)
        {
            return;
        }

        Runner.ServerPeerId = ServerPeerId;
        Runner.OwnerPeerId = OwnerPeerId;
        Runner.Instigator ??= this;
    }

    internal static InteractionInteractor? FindByRunner(GameplayActionRunner? runner)
    {
        return
            runner is not null
            && IsInstanceValid(runner)
            && _runnerOwners.TryGetValue(runner.GetInstanceId(), out InteractionInteractor? owner)
            && IsInstanceValid(owner)
            ? owner
            : null;
    }

    public bool CanRequest(in GameplayActionAccessContext context) =>
        HasInteractionAccess(context.Action);

    private bool HasInteractionAccess(GameplayAction action)
    {
        return action is InteractionAction interactionAction
            && interactionAction.Interactive is InteractiveComponent interactive
            && Detector?.Detect(interactive) == InteractionDetectionKind.Interactible;
    }

    public bool TryGetGestureProgress(out StringName inputActionName, out float progress)
    {
        if (Runner is not null)
        {
            return Runner.TryGetGestureProgress(out inputActionName, out progress);
        }

        inputActionName = new StringName();
        progress = 0.0f;
        return false;
    }

    public bool TryGetGestureElapsed(out StringName inputActionName, out float seconds)
    {
        if (
            Runner is not null
            && Runner.TryGetGestureProgress(out inputActionName, out float progress)
        )
        {
            seconds =
                (_focusedInteractive?.GetLongestHoldThreshold(this, inputActionName) ?? 0.0f)
                * progress;
            return true;
        }

        inputActionName = new StringName();
        seconds = 0.0f;
        return false;
    }

    internal void NotifyInteractiveRemoved(InteractiveComponent interactive)
    {
        Runner?.UnbindSource(interactive);
        Detector?.Forget(interactive);
        if (_detectedInteractives.Remove(interactive))
        {
            interactive.UnregisterInteractor(this);
            EmitSignal(SignalName.InteractiveIndicationRemoved, interactive);
        }

        if (_focusedInteractive == interactive)
        {
            _focusedInteractive = null;
        }

        if (IsLocallyControlled)
        {
            RecalculateFocus();
        }
    }

    internal bool RecalculateFocus()
    {
        FocusChangeResult? result = RecalculateFocusCore();
        if (result is null)
        {
            return false;
        }

        DispatchDetectionChanges();
        DispatchFocusChange(result.Value);
        return result.Value.Changed;
    }

    internal FocusChangeResult? RecalculateFocusCore()
    {
        if (Detector is null)
        {
            return null;
        }

        PurgeDetectedInteractives();
        InteractiveComponent? previous = _focusedInteractive;
        InteractiveComponent? best = null;
        float bestScore = float.MinValue;
        _detectionBuffer.Clear();
        foreach (InteractiveComponent candidate in Detector.GetCandidates())
        {
            if (!IsUsable(candidate) || _detectionBuffer.Contains(candidate))
            {
                continue;
            }

            InteractionDetectionKind kind = Detector.Detect(candidate);
            if (kind == InteractionDetectionKind.None)
            {
                continue;
            }

            _detectionBuffer.Add(candidate);
            if (kind != InteractionDetectionKind.Interactible || !candidate.HasVisibleAction(this))
            {
                continue;
            }

            float score = Detector.Score(candidate);
            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        ReconcileDetectedInteractives();
        if (_focusedInteractive == best)
        {
            return new FocusChangeResult(previous, best, Changed: false);
        }

        _focusedInteractive = best;
        return new FocusChangeResult(previous, best, Changed: true);
    }

    internal void DispatchFocusChange(in FocusChangeResult result)
    {
        if (!result.Changed)
        {
            return;
        }

        if (result.Previous is not null)
        {
            Runner?.UnbindSource(result.Previous);
        }

        if (result.Current is not null)
        {
            BindFocusedActions(result.Current);
        }

        Variant focusedInteractive = result.Current is null
            ? default
            : Variant.From(result.Current);
        EmitSignal(SignalName.FocusedInteractiveChanged, focusedInteractive);
        if (result.Current is not null)
        {
            EmitStatusFor(result.Current);
        }
    }

    private void BindFocusedActions(InteractiveComponent interactive)
    {
        if (Runner is null || interactive.ActionComponent is null)
        {
            return;
        }

        foreach (InteractionAction action in interactive.Actions)
        {
            if (action?.InteractionDefinition is null)
            {
                continue;
            }

            Runner.BindAction(
                interactive.ActionComponent,
                action.InteractionDefinition.Id,
                interactive,
                action.BuildBindingConfig(),
                Variant.From(interactive)
            );
        }
    }

    internal void RefreshFocusedBindings(InteractiveComponent interactive)
    {
        if (_focusedInteractive != interactive || Runner is null)
        {
            return;
        }

        Runner.UnbindSource(interactive);
        BindFocusedActions(interactive);
    }

    private void ReconcileDetectedInteractives()
    {
        _detectionEntered.Clear();
        _detectionExited.Clear();
        foreach (InteractiveComponent detected in _detectionBuffer)
        {
            if (!_detectedInteractives.Contains(detected))
            {
                _detectionEntered.Add(detected);
            }
        }

        foreach (InteractiveComponent tracked in _detectedInteractives)
        {
            if (!_detectionBuffer.Contains(tracked))
            {
                _detectionExited.Add(tracked);
            }
        }

        foreach (InteractiveComponent entered in _detectionEntered)
        {
            _detectedInteractives.Add(entered);
            entered.RegisterInteractor(this);
        }

        foreach (InteractiveComponent exited in _detectionExited)
        {
            _detectedInteractives.Remove(exited);
            if (IsUsable(exited))
            {
                exited.UnregisterInteractor(this);
            }
        }
    }

    internal void DispatchDetectionChanges()
    {
        foreach (InteractiveComponent entered in _detectionEntered)
        {
            if (!IsUsable(entered))
            {
                continue;
            }

            EmitSignal(SignalName.InteractiveIndicationAdded, entered);
            EmitStatusFor(entered);
        }
        _detectionEntered.Clear();

        foreach (InteractiveComponent exited in _detectionExited)
        {
            EmitSignal(SignalName.InteractiveIndicationRemoved, exited);
        }
        _detectionExited.Clear();
    }

    public IReadOnlyList<StringName> GetRelevantInputs()
    {
        _relevantInputs.Clear();
        if (!IsLocallyControlled || Runner is null)
        {
            return _relevantInputs;
        }

        foreach (GameplayActionBinding binding in Runner.GetBindings())
        {
            if (
                binding.Source == _focusedInteractive
                && binding.ActivationMode != GameplayActionActivationMode.Automatic
            )
            {
                AddRelevantInput(binding.InputActionName);
            }
        }

        foreach (StringName consumed in Runner.GetConsumedInputs())
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

    public InteractionTargetPresentation? GetInteractionPresentation() =>
        _focusedInteractive?.GetPresentation(this, true);

    public bool TryStartInteractionInput(StringName inputActionName)
    {
        if (inputActionName is null || inputActionName.IsEmpty || Runner is null)
        {
            return false;
        }

        RecalculateFocus();
        if (_focusedInteractive is not null)
        {
            Runner.InvalidateSource(_focusedInteractive);
        }
        return _focusedInteractive is not null && Runner.TryStartActionInput(inputActionName);
    }

    public bool TryEndInteractionInput(StringName inputActionName) =>
        inputActionName is not null
        && !inputActionName.IsEmpty
        && Runner?.TryEndActionInput(inputActionName) == true;

    internal void NotifyInteractiveStatusChanged(InteractiveComponent interactive)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        if (interactive == _focusedInteractive)
        {
            Runner?.InvalidateSource(interactive);
            EmitStatusFor(interactive);
        }

        RecalculateFocus();
    }

    private void ConnectRunnerSignals()
    {
        if (Runner is null)
        {
            return;
        }

        Runner.GameplayActionRequested += OnGameplayActionRequested;
        Runner.GameplayActionRejected += OnGameplayActionRejected;
        Runner.GameplayActionStarted += OnGameplayActionStarted;
        Runner.GameplayActionCompleted += OnGameplayActionCompleted;
        Runner.GameplayActionCancelled += OnGameplayActionCancelled;
        Runner.GameplayActionFailed += OnGameplayActionFailed;
    }

    private void DisconnectRunnerSignals()
    {
        if (Runner is null)
        {
            return;
        }

        Runner.GameplayActionRequested -= OnGameplayActionRequested;
        Runner.GameplayActionRejected -= OnGameplayActionRejected;
        Runner.GameplayActionStarted -= OnGameplayActionStarted;
        Runner.GameplayActionCompleted -= OnGameplayActionCompleted;
        Runner.GameplayActionCancelled -= OnGameplayActionCancelled;
        Runner.GameplayActionFailed -= OnGameplayActionFailed;
    }

    private static InteractiveComponent? ResolveInteractive(Node? component) =>
        InteractiveComponent.FindByActionComponent(component as GameplayActionComponent);

    private void OnGameplayActionRequested(Node component, StringName actionId)
    {
        if (ResolveInteractive(component) is { } interactive)
        {
            EmitSignal(SignalName.InteractionRequested, interactive, actionId);
        }
    }

    private void OnGameplayActionRejected(Node component, StringName actionId, string reason)
    {
        InteractiveComponent? target = ResolveInteractive(component);
        if (target?.ResolveAction(actionId) is InteractionAction action)
        {
            reason = target.AdaptRejectionReason(this, action, reason);
        }
        else if (reason == GameplayActionAvailabilityExtensions.UnavailableReason)
        {
            reason = InteractionAvailabilityExtensions.UnavailableReason;
        }

        Variant interactive = target is null ? default : Variant.From(target);
        EmitSignal(SignalName.InteractionRejected, interactive, actionId, reason);
    }

    private void OnGameplayActionStarted(Node component, StringName actionId, long executionId)
    {
        Variant interactive = ResolveInteractive(component) is { } target
            ? Variant.From(target)
            : default;
        EmitSignal(
            SignalName.InteractionStarted,
            interactive,
            actionId,
            checked((ulong)executionId)
        );
    }

    private void OnGameplayActionCompleted(Node component, StringName actionId, long executionId)
    {
        Variant interactive = ResolveInteractive(component) is { } target
            ? Variant.From(target)
            : default;
        EmitSignal(SignalName.InteractionCompleted, interactive, actionId);
    }

    private void OnGameplayActionCancelled(
        Node component,
        StringName actionId,
        long executionId,
        string reason
    )
    {
        Variant interactive = ResolveInteractive(component) is { } target
            ? Variant.From(target)
            : default;
        EmitSignal(SignalName.InteractionCancelled, interactive, actionId, reason);
    }

    private void OnGameplayActionFailed(
        Node component,
        StringName actionId,
        long executionId,
        string reason
    )
    {
        Variant interactive = ResolveInteractive(component) is { } target
            ? Variant.From(target)
            : default;
        EmitSignal(SignalName.InteractionFailed, interactive, actionId, reason);
    }

    public override void _ExitTree()
    {
        if (Runner is not null && IsInstanceValid(Runner))
        {
            if (_focusedInteractive is not null)
            {
                Runner.UnbindSource(_focusedInteractive);
            }
            Runner.UnregisterAccessProvider(InteractionAction.InteractionAccessProviderId, this);
            DisconnectRunnerSignals();
            _runnerOwners.Remove(Runner.GetInstanceId());
        }

        foreach (InteractiveComponent interactive in _detectedInteractives)
        {
            if (IsUsable(interactive))
            {
                interactive.UnregisterInteractor(this);
            }
        }

        _detectedInteractives.Clear();
        _detectionBuffer.Clear();
        _detectionEntered.Clear();
        _detectionExited.Clear();
        _relevantInputs.Clear();
        _focusedInteractive = null;
    }

    // Compatibility adapters retained through Task 4. Normal Interaction input never uses these
    // RPCs; it talks to GameplayActionRunner directly. Task 5 removes them with the old scene API.
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryStartInteraction(NodePath targetPath, StringName actionId)
    {
        InteractiveComponent? target = ResolveNetworkPath(targetPath) as InteractiveComponent;
        if (Runner is null || target?.ActionComponent is null)
        {
            EmitSignal(
                SignalName.InteractionRejected,
                target is null ? default : Variant.From(target),
                actionId,
                GameplayActionAvailabilityExtensions.UnavailableReason
            );
            return;
        }

        Runner.ServerTryStartAction(GetNetworkPath(target.ActionComponent), actionId);
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryEndInteraction(StringName inputActionName)
    {
        Runner?.TryEndActionInput(inputActionName);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionRejected(NodePath targetPath, StringName actionId, string reason)
    {
        InteractiveComponent? target = ResolveNetworkPath(targetPath) as InteractiveComponent;
        if (
            target?.ActionComponent is GameplayActionComponent component
            && component.TryGetExecutionPresentation(
                actionId,
                out GameplayActionExecutionPresentation presentation
            )
            && presentation.ExecutionId == 0ul
        )
        {
            component.RemovePendingExecution(actionId);
        }

        EmitSignal(
            SignalName.InteractionRejected,
            target is null ? default : Variant.From(target),
            actionId,
            reason
        );
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionStarted(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        InteractionExecutionVisibility visibility,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    )
    {
        InteractiveComponent? target = ResolveNetworkPath(targetPath) as InteractiveComponent;
        if (target?.ActionComponent is GameplayActionComponent component)
        {
            if (visibility == InteractionExecutionVisibility.AuthorityOnly)
            {
                component.RemovePendingExecution(actionId);
            }
            else
            {
                component.ConfirmRequesterExecution(
                    actionId,
                    executionId,
                    hasProgress,
                    new GameplayActionProgressSample(progressBase, progressPerSecond, revision)
                );
            }
        }

        EmitSignal(
            SignalName.InteractionStarted,
            target is null ? default : Variant.From(target),
            actionId,
            executionId
        );
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionProgress(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        bool hasProgress,
        float progressBase,
        float progressPerSecond,
        long revision
    )
    {
        InteractiveComponent? target = ResolveNetworkPath(targetPath) as InteractiveComponent;
        target?.ActionComponent?.ApplyRequesterProgress(
            actionId,
            executionId,
            hasProgress,
            new GameplayActionProgressSample(progressBase, progressPerSecond, revision)
        );
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionCompleted(
        NodePath targetPath,
        StringName actionId,
        ulong executionId
    ) => EndCompatibilityAcknowledgement(targetPath, actionId, executionId, null, false);

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionCancelled(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        string reason
    ) => EndCompatibilityAcknowledgement(targetPath, actionId, executionId, reason, true);

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionFailed(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        string reason
    )
    {
        InteractiveComponent? target = ResolveNetworkPath(targetPath) as InteractiveComponent;
        target?.ActionComponent?.RemoveRequesterExecution(actionId, executionId);
        EmitSignal(
            SignalName.InteractionFailed,
            target is null ? default : Variant.From(target),
            actionId,
            reason
        );
    }

    private void EndCompatibilityAcknowledgement(
        NodePath targetPath,
        StringName actionId,
        ulong executionId,
        string? reason,
        bool cancelled
    )
    {
        InteractiveComponent? target = ResolveNetworkPath(targetPath) as InteractiveComponent;
        target?.ActionComponent?.RemoveRequesterExecution(actionId, executionId);
        Variant value = target is null ? default : Variant.From(target);
        if (cancelled)
        {
            EmitSignal(SignalName.InteractionCancelled, value, actionId, reason ?? string.Empty);
        }
        else
        {
            EmitSignal(SignalName.InteractionCompleted, value, actionId);
        }
    }

    // Compatibility callbacks for the old Interaction-only execution helpers that remain until
    // Task 5. They no longer own request or acknowledgement state.
    internal void NotifyExecutionStarted(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId
    )
    {
        if (action.InteractionDefinition is not null)
        {
            EmitSignal(
                SignalName.InteractionStarted,
                interactive,
                action.InteractionDefinition.Id,
                executionId
            );
        }
    }

    internal void NotifyExecutionProgress(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId,
        bool hasProgress,
        InteractionProgressSample sample
    ) { }

    internal void NotifyExecutionCompleted(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId
    )
    {
        if (action.InteractionDefinition is not null)
        {
            EmitSignal(
                SignalName.InteractionCompleted,
                interactive,
                action.InteractionDefinition.Id
            );
        }
    }

    internal void NotifyExecutionCancelled(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId,
        string reason
    )
    {
        if (action.InteractionDefinition is not null)
        {
            EmitSignal(
                SignalName.InteractionCancelled,
                interactive,
                action.InteractionDefinition.Id,
                reason
            );
        }
    }

    internal void NotifyExecutionFailed(
        InteractiveComponent interactive,
        InteractionAction action,
        ulong executionId,
        string reason
    )
    {
        if (action.InteractionDefinition is not null)
        {
            EmitSignal(
                SignalName.InteractionFailed,
                interactive,
                action.InteractionDefinition.Id,
                reason
            );
        }
    }

    private void EmitStatusFor(InteractiveComponent interactive) =>
        EmitSignal(SignalName.InteractionStatusChanged, interactive);

    private bool IsUsable(InteractiveComponent? interactive) =>
        interactive is not null && IsInstanceValid(interactive);

    private void PurgeDetectedInteractives()
    {
        if (_detectedInteractives.Count > 0)
        {
            _detectedInteractives.RemoveWhere(interactive => !IsUsable(interactive));
        }
        if (_focusedInteractive is not null && !IsUsable(_focusedInteractive))
        {
            _focusedInteractive = null;
        }
    }

    private NodePath GetNetworkPath(Node node)
    {
        Node? root = GetNetworkRoot();
        return root is null ? node.GetPath() : root.GetPathTo(node);
    }

    private Node? ResolveNetworkPath(NodePath path)
    {
        Node? root = GetNetworkRoot();
        return root is null || path is null || path.IsEmpty ? null : root.GetNodeOrNull(path);
    }

    private Node? GetNetworkRoot()
    {
        SceneTree? tree = GetTree();
        if (tree is null)
        {
            return null;
        }

        return Multiplayer is SceneMultiplayer scene && !scene.RootPath.IsEmpty
            ? tree.Root.GetNodeOrNull(scene.RootPath)
            : tree.Root;
    }
}
