using System.Collections.Generic;
using Godot;
using QuestWorld.GameplayActions;
using QuestWorld.GameplayActions.Runtime.Access;
using QuestWorld.GameplayActions.Runtime.Actions;
using QuestWorld.GameplayActions.Runtime.Bindings;
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

    private readonly HashSet<InteractiveComponent> _detectedInteractives = new();
    private readonly HashSet<InteractiveComponent> _detectionBuffer = new();
    private readonly List<InteractiveComponent> _detectionEntered = new();
    private readonly List<InteractiveComponent> _detectionExited = new();
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

        // The interactor claims the instigator of its runner rather than defaulting into it. That is
        // the contract every interaction rule and executor reads: the instigator of an interaction
        // execution is the interactor that drove it, which is what makes the generic context
        // sufficient and spares Interaction a reverse index from runners back to their owners.
        Runner.Instigator = this;
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

    public IReadOnlyList<StringName> GetRelevantInputs() =>
        Runner?.GetRelevantInputs() ?? System.Array.Empty<StringName>();

    public InteractionTargetPresentation? GetInteractionPresentation() =>
        _focusedInteractive?.GetPresentation(this, true);

    public bool TryStartInteractionInput(StringName inputActionName)
    {
        if (inputActionName is null || inputActionName.IsEmpty || Runner is null)
        {
            return false;
        }

        RefreshFocusedBindings();
        return Runner.TryStartActionInput(inputActionName);
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

    /// <summary>Resolves the interaction target one host signal belongs to.</summary>
    /// <remarks>
    /// The host names the action, and an <see cref="InteractionAction"/> knows the target that
    /// prepared it, so the target is reached by ownership instead of by a reverse index from hosts
    /// back to interactives. A host carrying generic actions beside the interaction ones therefore
    /// resolves only the latter, which is exactly what the interaction signals describe.
    /// </remarks>
    private static InteractiveComponent? ResolveInteractive(Node? component, StringName actionId) =>
        (component as GameplayActionComponent)?.ResolveAction(actionId) is InteractionAction action
            ? action.Interactive
            : null;

    private void OnGameplayActionRequested(Node component, StringName actionId)
    {
        if (ResolveInteractive(component, actionId) is { } interactive)
        {
            EmitSignal(SignalName.InteractionRequested, interactive, actionId);
        }
    }

    private void OnGameplayActionRejected(Node component, StringName actionId, string reason)
    {
        InteractiveComponent? target = ResolveInteractive(component, actionId);
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
        Variant interactive = ResolveInteractive(component, actionId) is { } target
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
        Variant interactive = ResolveInteractive(component, actionId) is { } target
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
        Variant interactive = ResolveInteractive(component, actionId) is { } target
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
        Variant interactive = ResolveInteractive(component, actionId) is { } target
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
        _focusedInteractive = null;
    }

    internal void RefreshFocusedBindings()
    {
        RecalculateFocus();
        if (_focusedInteractive is not null)
        {
            Runner?.InvalidateSource(_focusedInteractive);
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
}
