using System.Collections.Generic;
using GameplayActionPlugin;
using GameplayActionPlugin.Runtime.Access;
using GameplayActionPlugin.Runtime.Actions;
using GameplayActionPlugin.Runtime.Bindings;
using GameplayActionPlugin.Runtime.Runner;
using Godot;
using InteractionPlugin.Runtime.Detection;
using InteractionPlugin.Runtime.Interactive;
using InteractionPlugin.Runtime.Offers;

namespace InteractionPlugin.Runtime.Interactor;

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
    public delegate void InteractiveIndicationAddedEventHandler(Node interactive);

    [Signal]
    public delegate void InteractiveIndicationRemovedEventHandler(Node interactive);

    [ExportGroup("Detection")]
    [Export]
    public InteractionDetector? Detector { get; set; }

    [ExportGroup("Actions")]
    [Export]
    public GameplayActionRunner? Runner { get; set; }

    private readonly HashSet<InteractiveComponent> _detectedInteractives = new();
    private readonly HashSet<InteractiveComponent> _detectionBuffer = new();
    private readonly List<InteractiveComponent> _detectionEntered = new();
    private readonly List<InteractiveComponent> _detectionExited = new();
    private InteractiveComponent? _focusedInteractive;

    public InteractiveComponent? FocusedInteractive => _focusedInteractive;

    public bool IsLocallyControlled => Runner?.IsLocallyControlled ?? true;

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

        Runner.RegisterAccessProvider(InteractionOffer.InteractionAccessProviderId, this);
        ConnectRunnerSignals();
    }

    public override void _Process(double delta)
    {
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

    internal static InteractionInteractor? ResolveForInstigator(Node? instigator)
    {
        if (instigator is null || !GodotObject.IsInstanceValid(instigator))
        {
            return null;
        }

        if (instigator is InteractionInteractor interactor)
        {
            return interactor;
        }

        foreach (Node child in instigator.GetChildren())
        {
            InteractionInteractor? resolved = ResolveForInstigator(child);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    public bool CanRequest(in GameplayActionAccessContext context) => HasInteractionAccess(context);

    public bool TryAcquireRequestReservation(
        in GameplayActionAccessContext context,
        out IGameplayActionRequestReservation? reservation
    )
    {
        reservation = null;
        if (
            !TryResolveInteractionAccess(context, out InteractiveComponent? interactive)
            || interactive is null
        )
        {
            return false;
        }

        if (
            interactive.TryResolveOfferForEndpoint(
                this,
                context.Component,
                context.Action,
                out InteractionOffer? offer
            ) && offer is not null
        )
        {
            return interactive.TryAcquireTargetReservation(
                this,
                offer,
                context.Component,
                context.Action,
                out reservation
            );
        }

        return false;
    }

    private bool HasInteractionAccess(in GameplayActionAccessContext context)
    {
        if (
            !TryResolveInteractionAccess(context, out InteractiveComponent? interactive)
            || interactive is null
        )
        {
            return false;
        }

        if (
            interactive.TryResolveOfferForEndpoint(
                this,
                context.Component,
                context.Action,
                out InteractionOffer? offer
            )
            && offer is not null
            && Detector?.Detect(interactive) == InteractionDetectionKind.Interactible
        )
        {
            return interactive.EvaluateAccess(this, offer, context.Sustained)
                is GameplayActionAllowed;
        }

        return false;
    }

    private static bool TryResolveInteractionAccess(
        in GameplayActionAccessContext context,
        out InteractiveComponent? interactive
    )
    {
        interactive = context.AccessSource as InteractiveComponent;
        if (interactive is null || !GodotObject.IsInstanceValid(interactive))
        {
            return false;
        }

        Node invocationTarget = interactive.ResolveInvocationTarget();
        return GodotObject.IsInstanceValid(invocationTarget)
            && context.Target is Node target
            && GodotObject.IsInstanceValid(target)
            && target == invocationTarget;
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
        if (Runner is null)
        {
            return;
        }

        if (interactive.Offers.Count > 0)
        {
            foreach (InteractionOffer offer in interactive.Offers)
            {
                if (
                    offer?.BindingConfig is not GameplayActionBindingConfig config
                    || !interactive.TryResolveOffer(
                        this,
                        offer,
                        out InteractionOfferResolution resolution
                    )
                )
                {
                    continue;
                }

                Runner.BindAction(
                    resolution.Component,
                    resolution.Action.Definition?.Id ?? offer.ActionId,
                    interactive,
                    config,
                    presentationContext: Variant.From(interactive),
                    target: interactive.ResolveInvocationTarget(),
                    accessSource: interactive
                );
            }

            return;
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
    }

    private void DisconnectRunnerSignals()
    {
        if (Runner is null)
        {
            return;
        }

        Runner.GameplayActionRequested -= OnGameplayActionRequested;
        Runner.GameplayActionRejected -= OnGameplayActionRejected;
    }

    /// <summary>Resolves the interaction target from the contextual binding that requested an action.</summary>
    /// <remarks>
    /// Interaction is an offer and access layer, not an owner of the gameplay action. The binding's
    /// access source therefore remains the authoritative local association between a request or
    /// refusal notification and the interactive target that exposed it.
    /// </remarks>
    private InteractiveComponent? ResolveInteractive(Node? component, StringName actionId)
    {
        foreach (
            GameplayActionBinding binding in Runner?.GetBindings()
                ?? System.Array.Empty<GameplayActionBinding>()
        )
        {
            if (
                binding.Component == component
                && binding.ActionId == actionId
                && binding.AccessSource is InteractiveComponent interactive
            )
            {
                return interactive;
            }
        }

        return null;
    }

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
        if (reason == GameplayActionAvailabilityExtensions.UnavailableReason)
        {
            reason = "Interaction unavailable.";
        }

        Variant interactive = target is null ? default : Variant.From(target);
        EmitSignal(SignalName.InteractionRejected, interactive, actionId, reason);
    }

    public override void _ExitTree()
    {
        if (Runner is not null && IsInstanceValid(Runner))
        {
            if (_focusedInteractive is not null)
            {
                Runner.UnbindSource(_focusedInteractive);
            }
            Runner.UnregisterAccessProvider(InteractionOffer.InteractionAccessProviderId, this);
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

    /// <summary>
    /// Re-evaluates focus and refreshes the focused target's contextual action bindings.
    /// </summary>
    /// <remarks>Call this immediately before forwarding a local press to the runner.</remarks>
    public void RefreshFocusedBindings()
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
