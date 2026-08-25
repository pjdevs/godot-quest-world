using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Actions;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Interactor;

internal readonly record struct FocusChangeResult(
    InteractiveComponent? Previous,
    InteractiveComponent? Current,
    bool Changed
);

/// <summary>
/// Detects interaction targets, selects local focus, and routes input intentions to the server.
/// </summary>
/// <remarks>
/// Add one instance to each interacting character. Focus and presentation run only for
/// <see cref="OwnerPeerId"/>, while authoritative validation and gameplay dispatch run on the server.
/// </remarks>
[GlobalClass]
public partial class InteractionInteractor : Node
{
    private const string ReleasedReason = "The interaction input was released.";
    private const string InteractorLostReason = "The interactor left the interaction.";

    /// <summary>Emitted locally when the best target changes.</summary>
    /// <param name="interactive">New focused interactive, or null when focus is cleared.</param>
    [Signal]
    public delegate void FocusedInteractiveChangedEventHandler(Node interactive);

    /// <summary>Emitted locally when the presentation of a visible target may have changed.</summary>
    /// <remarks>
    /// The signal is a notification only. Availability is carried per action, so a consumer reads
    /// the fresh snapshot from <see cref="InteractiveComponent.GetPresentation"/> instead of relying
    /// on a target-wide summary.
    /// </remarks>
    /// <param name="interactive">Interactive whose presentation may have changed.</param>
    [Signal]
    public delegate void InteractionStatusChangedEventHandler(Node interactive);

    /// <summary>Emitted locally after prevalidation and before any client RPC or host dispatch.</summary>
    /// <param name="interactive">Target requested by the owning player.</param>
    /// <param name="actionId">Identifier of the action resolved from the local input.</param>
    [Signal]
    public delegate void InteractionRequestedEventHandler(Node interactive, StringName actionId);

    /// <summary>Emitted locally for a prevalidation failure or a rejection returned by the server.</summary>
    /// <remarks>
    /// The refusal carries the action so presentation can attach it to the right prompt instead of
    /// to the whole target. The identifier is empty when no action could be resolved at all.
    /// </remarks>
    /// <param name="interactive">Rejected target, or null when no target can be resolved.</param>
    /// <param name="actionId">Identifier of the rejected action, or an empty name.</param>
    /// <param name="reason">User-facing rejection reason.</param>
    [Signal]
    public delegate void InteractionRejectedEventHandler(
        Node interactive,
        StringName actionId,
        string reason
    );

    /// <summary>Emitted when an interactive enters the optional indication area.</summary>
    /// <param name="interactive">Interactive available for indication presentation.</param>
    [Signal]
    public delegate void InteractiveIndicationAddedEventHandler(Node interactive);

    /// <summary>Emitted when an interactive leaves the optional indication area.</summary>
    /// <param name="interactive">Interactive removed from indication presentation.</param>
    [Signal]
    public delegate void InteractiveIndicationRemovedEventHandler(Node interactive);

    /// <summary>Gets or sets the required view transform used for angle and alignment scoring.</summary>
    [ExportGroup("Detection")]
    [Export]
    public Node3D? ViewOrigin
    {
        get => _viewOrigin;
        set
        {
            if (_viewOrigin == value)
            {
                return;
            }

            _viewOrigin = value;
        }
    }

    /// <summary>
    /// Gets or sets the optional physical origin used for distance checks. Defaults to the Node3D parent.
    /// </summary>
    [Export]
    public Node3D? InteractionOrigin { get; set; }

    /// <summary>Gets or sets the maximum authoritative interaction distance in world units.</summary>
    [Export]
    public float MaxInteractionDistance { get; set; } = 10.0f;

    /// <summary>Gets or sets the maximum view angle accepted for focus and server validation.</summary>
    [Export(PropertyHint.Range, "0,180,1")]
    public float MaxInteractionAngleDegrees { get; set; } = 30.0f;

    /// <summary>Gets or sets how strongly distance reduces the focus score relative to alignment.</summary>
    [Export]
    public float DistanceScoreCoefficient { get; set; } = 0.5f;

    /// <summary>Gets or sets the server peer that receives reliable interaction RPCs.</summary>
    [ExportGroup("Network")]
    [Export]
    public int ServerPeerId { get; set; } = 1;

    /// <summary>Gets or sets the peer allowed to control this interactor.</summary>
    [Export]
    public int OwnerPeerId { get; set; } = 1;

    private readonly HashSet<InteractiveComponent> _indicatedInteractives = new();
    private readonly HashSet<InteractiveComponent> _interactiveCandidates = new();
    private readonly Dictionary<StringName, StringName> _requestedActionsByInput = new();
    private Node3D? _viewOrigin;
    private Node3D? _resolvedInteractionOrigin;
    private InteractiveComponent? _focusedInteractive;
    private InteractiveComponent? _activeInteractive;
    private bool _hasKnownLocalControl;
    private bool _lastKnownLocalControl;

    /// <summary>Gets the target currently selected by the owning peer.</summary>
    public InteractiveComponent? FocusedInteractive => _focusedInteractive;

    /// <summary>Gets whether this peer owns focus calculation, input requests, and presentation.</summary>
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

    /// <summary>Godot callback that resolves origins and keeps node authority on the server.</summary>
    public override void _Ready()
    {
        _resolvedInteractionOrigin = InteractionOrigin ?? GetParent() as Node3D;
        if (ViewOrigin is null)
        {
            GD.PushError($"{GetPath()}: InteractionInteractor requires a ViewOrigin.");
            SetProcess(false);
            return;
        }

        if (_resolvedInteractionOrigin is null)
        {
            GD.PushError(
                $"{GetPath()}: InteractionInteractor requires an InteractionOrigin or a Node3D parent."
            );
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
    }

    /// <summary>Godot callback that recalculates focus for the owning peer each frame.</summary>
    public override void _Process(double delta)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        RecalculateFocus();
    }

    internal void AddInteractiveIndication(InteractiveComponent interactive)
    {
        if (!IsUsable(interactive) || !_indicatedInteractives.Add(interactive))
        {
            return;
        }

        interactive.RegisterInteractor(this);
        EmitSignal(SignalName.InteractiveIndicationAdded, interactive);
        if (IsLocallyControlled)
        {
            EmitStatusFor(interactive);
        }
    }

    internal void RemoveInteractiveIndication(InteractiveComponent interactive)
    {
        if (!_indicatedInteractives.Remove(interactive))
        {
            return;
        }

        EmitSignal(SignalName.InteractiveIndicationRemoved, interactive);
        if (!_interactiveCandidates.Contains(interactive))
        {
            interactive.UnregisterInteractor(this);
        }
    }

    internal void AddInteractive(InteractiveComponent interactive)
    {
        if (!IsUsable(interactive) || !_interactiveCandidates.Add(interactive))
        {
            return;
        }

        interactive.RegisterInteractor(this);
        if (IsLocallyControlled)
        {
            RecalculateFocus();
        }
    }

    internal void RemoveInteractive(InteractiveComponent interactive)
    {
        if (!_interactiveCandidates.Remove(interactive))
        {
            return;
        }

        if (_activeInteractive == interactive && Multiplayer.IsServer())
        {
            interactive.CancelExecution(this, InteractorLostReason);
            _activeInteractive = null;
        }

        if (!_indicatedInteractives.Contains(interactive))
        {
            interactive.UnregisterInteractor(this);
        }

        if (IsLocallyControlled)
        {
            RecalculateFocus();
        }
        else if (_focusedInteractive == interactive)
        {
            _focusedInteractive = null;
        }
    }

    internal bool RecalculateFocus()
    {
        FocusChangeResult? result = RecalculateFocusCore();
        if (result is null)
        {
            return false;
        }

        DispatchFocusChange(result.Value);
        return result.Value.Changed;
    }

    internal FocusChangeResult? RecalculateFocusCore()
    {
        if (ViewOrigin is null || _resolvedInteractionOrigin is null)
        {
            return null;
        }

        PurgeInvalidCandidates();
        InteractiveComponent? previous = _focusedInteractive;
        InteractiveComponent? best = null;
        float bestScore = float.MinValue;
        foreach (InteractiveComponent candidate in _interactiveCandidates)
        {
            if (!IsWithinInteractionRange(candidate) || !candidate.HasVisibleAction(this))
            {
                continue;
            }

            float score = CalculateInteractionScore(candidate);
            if (best is null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (_focusedInteractive == best)
        {
            return new FocusChangeResult(previous, best, Changed: false);
        }

        _focusedInteractive = best;
        return new FocusChangeResult(previous, best, Changed: true);
    }

    internal void DispatchFocusChange(in FocusChangeResult result)
    {
        if (result.Changed)
        {
            Variant focusedInteractive = result.Current is null ? new Variant() : result.Current;
            EmitSignal(SignalName.FocusedInteractiveChanged, focusedInteractive);
        }

        if (result.Current is not null)
        {
            EmitStatusFor(result.Current);
        }

        if (result.Changed && result.Current is not null)
        {
            TryStartAutomaticInteraction(result.Current);
        }
    }

    internal float CalculateInteractionScore(InteractiveComponent interactive)
    {
        if (ViewOrigin is null || _resolvedInteractionOrigin is null)
        {
            return float.MinValue;
        }

        Vector3 interactionPosition = interactive.GetInteractionPosition();
        Vector3 viewOffset = interactionPosition - ViewOrigin.GlobalPosition;
        float distance = interactionPosition.DistanceTo(_resolvedInteractionOrigin.GlobalPosition);
        if (distance <= Mathf.Epsilon)
        {
            return 1.0f;
        }

        float alignment = Mathf.Max(0.0f, (-ViewOrigin.GlobalBasis.Z).Dot(viewOffset.Normalized()));
        return alignment / (1.0f + distance * Mathf.Max(DistanceScoreCoefficient, 0.0f));
    }

    internal bool IsWithinInteractionRange(InteractiveComponent interactive)
    {
        if (ViewOrigin is null || _resolvedInteractionOrigin is null)
        {
            return false;
        }

        Vector3 interactionPosition = interactive.GetInteractionPosition();
        Vector3 viewOffset = interactionPosition - ViewOrigin.GlobalPosition;
        float distance = interactionPosition.DistanceTo(_resolvedInteractionOrigin.GlobalPosition);
        if (distance > Mathf.Max(MaxInteractionDistance, 0.0f))
        {
            return false;
        }

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        float alignment = (-ViewOrigin.GlobalBasis.Z).Dot(viewOffset.Normalized());
        float minimumAlignment = Mathf.Cos(
            Mathf.DegToRad(Mathf.Clamp(MaxInteractionAngleDegrees, 0.0f, 180.0f))
        );
        return alignment >= minimumAlignment;
    }

    /// <summary>Builds a fresh prompt snapshot for the current focused target.</summary>
    /// <returns>The focused presentation, or null when no target is focused.</returns>
    public InteractionTargetPresentation? GetInteractionPresentation()
    {
        return _focusedInteractive?.GetPresentation(this, true);
    }

    /// <summary>Resolves one input into an action of the focused target and requests its start.</summary>
    /// <remarks>
    /// Call from the local player's input code. The resolved action is only a local intention: the
    /// authoritative peer re-resolves the identifier against its own scene and re-evaluates it. On a
    /// client, true means the reliable request was sent; final acceptance is reported by gameplay
    /// state or <see cref="InteractionRejected"/>.
    /// </remarks>
    /// <param name="inputActionName">Project input action pressed by the player.</param>
    /// <returns>Whether a locally valid request was dispatched or accepted by the host.</returns>
    public bool TryStartInteractionInput(StringName inputActionName)
    {
        RecalculateFocus();
        InteractiveComponent? target = _focusedInteractive;
        if (target is null)
        {
            return false;
        }

        InteractionAction? action = target.ResolveActionForInput(this, inputActionName);
        if (action?.Definition is null)
        {
            return false;
        }

        return TryRequestAction(target, action, inputActionName);
    }

    /// <summary>Requests authoritative cancellation of the execution started by one input.</summary>
    /// <remarks>
    /// Call from the local player's input-release code. The action is the one this interactor
    /// remembers requesting for that input, never a fresh resolution: world state may have changed
    /// since the press, and re-resolving would cancel an execution the server never started. An
    /// instant action holds no execution, so releasing its input cancels nothing.
    /// </remarks>
    /// <param name="inputActionName">Project input action released by the player.</param>
    /// <returns>Whether a request was sent or the host cancelled the matching execution.</returns>
    public bool TryEndInteractionInput(StringName inputActionName)
    {
        if (
            inputActionName is null
            || !_requestedActionsByInput.Remove(inputActionName, out StringName? actionId)
        )
        {
            return false;
        }

        if (!Multiplayer.IsServer())
        {
            RpcId(ServerPeerId, nameof(ServerTryEndInteraction), actionId);
            return true;
        }

        return EndInteractionInputAuthoritatively(OwnerPeerId, actionId);
    }

    private void TryStartAutomaticInteraction(InteractiveComponent target)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        InteractionAction? action = target.ResolveAutomaticAction(this);
        if (action?.Definition is null)
        {
            return;
        }

        TryRequestAction(target, action, inputActionName: null);
    }

    private bool TryRequestAction(
        InteractiveComponent target,
        InteractionAction action,
        StringName? inputActionName
    )
    {
        StringName actionId = action.Definition!.Id;
        InteractionAvailability localAvailability = target.EvaluateAvailability(this, action);
        if (localAvailability is not InteractionAllowed)
        {
            EmitSignal(
                SignalName.InteractionRejected,
                target,
                actionId,
                localAvailability.DescribeRefusal()
            );
            return false;
        }

        EmitSignal(SignalName.InteractionRequested, target, actionId);
        if (!Multiplayer.IsServer())
        {
            RpcId(ServerPeerId, nameof(ServerTryStartInteraction), target.GetPath(), actionId);
            RememberRequestedAction(inputActionName, actionId);
            return true;
        }

        if (!TryStartInteractionAuthoritatively(target, actionId, OwnerPeerId, out _))
        {
            return false;
        }

        RememberRequestedAction(inputActionName, actionId);
        return true;
    }

    private void RememberRequestedAction(StringName? inputActionName, StringName actionId)
    {
        if (inputActionName is not null && !inputActionName.IsEmpty)
        {
            _requestedActionsByInput[inputActionName] = actionId;
        }
    }

    internal void NotifyInteractiveStatusChanged(InteractiveComponent interactive)
    {
        if (!IsLocallyControlled)
        {
            return;
        }

        if (interactive == _focusedInteractive)
        {
            EmitStatusFor(interactive);
        }

        RecalculateFocus();
    }

    /// <summary>Godot callback that releases server reservations and unregisters detected targets.</summary>
    public override void _ExitTree()
    {
        if (
            _activeInteractive is not null
            && IsUsable(_activeInteractive)
            && Multiplayer.IsServer()
        )
        {
            _activeInteractive.CancelExecution(this, InteractorLostReason);
            _activeInteractive = null;
        }

        HashSet<InteractiveComponent> registered = new(_interactiveCandidates);
        registered.UnionWith(_indicatedInteractives);
        foreach (InteractiveComponent interactive in registered)
        {
            if (IsUsable(interactive))
            {
                interactive.UnregisterInteractor(this);
            }
        }

        _interactiveCandidates.Clear();
        _indicatedInteractives.Clear();
        _requestedActionsByInput.Clear();
        _focusedInteractive = null;
    }

    /// <summary>Reliable client-to-server RPC that validates and executes one action.</summary>
    /// <remarks>Called by Godot RPC dispatch; input code should call <see cref="TryStartInteractionInput"/>.</remarks>
    /// <param name="targetPath">Scene-tree path of the client-selected interactive.</param>
    /// <param name="actionId">Identifier of the action the client believes it can request.</param>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryStartInteraction(NodePath targetPath, StringName actionId)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        InteractiveComponent? target = GetTree()
            .Root.GetNodeOrNull<InteractiveComponent>(targetPath);
        if (target is null)
        {
            RejectInteraction(
                senderPeerId,
                targetPath,
                actionId,
                "The interaction target no longer exists."
            );
            return;
        }

        if (!TryStartInteractionAuthoritatively(target, actionId, senderPeerId, out string reason))
        {
            RejectInteraction(senderPeerId, targetPath, actionId, reason);
        }
    }

    /// <summary>Reliable client-to-server RPC that cancels the caller's running execution.</summary>
    /// <remarks>Called by Godot RPC dispatch; input code should call <see cref="TryEndInteractionInput"/>.</remarks>
    /// <param name="actionId">Identifier the client received when its request was sent.</param>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryEndInteraction(StringName actionId)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        if (!ValidateSender(senderPeerId, out string reason))
        {
            RejectInteraction(senderPeerId, new NodePath(), actionId, reason);
            return;
        }

        EndInteractionInputAuthoritatively(senderPeerId, actionId);
    }

    /// <summary>Reliable server-to-owner RPC that reports an authoritative rejection.</summary>
    /// <remarks>Called by Godot RPC dispatch on the owning client or directly on an offline host.</remarks>
    /// <param name="targetPath">Rejected target path, which may be empty.</param>
    /// <param name="actionId">Identifier of the rejected action, which may be empty.</param>
    /// <param name="reason">User-facing rejection reason.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionRejected(NodePath targetPath, StringName actionId, string reason)
    {
        Node? target = GetTree().Root.GetNodeOrNull(targetPath);
        EmitSignal(SignalName.InteractionRejected, target, actionId, reason);
    }

    private bool TryStartInteractionAuthoritatively(
        InteractiveComponent target,
        StringName actionId,
        int senderPeerId,
        out string reason
    )
    {
        reason = string.Empty;
        if (!ValidateSender(senderPeerId, out reason))
        {
            return false;
        }

        if (!_interactiveCandidates.Contains(target) || !IsWithinInteractionRange(target))
        {
            reason = "The interaction target is out of range.";
            return false;
        }

        InteractionAction? action = target.ResolveAction(actionId);
        if (action is null)
        {
            reason = InteractionAvailabilityExtensions.UnavailableReason;
            return false;
        }

        InteractionAvailability availability = target.EvaluateAvailability(this, action);
        if (availability is not InteractionAllowed)
        {
            reason = availability.DescribeRefusal();
            return false;
        }

        InteractionExecutionResult result = target.ExecuteAction(this, action);
        switch (result)
        {
            case InteractionExecutionRunning:
                // Only a running execution keeps a reservation the interactor may later release.
                _activeInteractive = target;
                return true;

            case InteractionExecutionCompleted:
                return true;

            case InteractionExecutionRejected rejected:
                reason = rejected.Reason;
                return false;

            case InteractionExecutionFailed failed:
                reason = failed.Reason;
                return false;
        }

        reason = InteractionAvailabilityExtensions.UnavailableReason;
        return false;
    }

    private bool EndInteractionInputAuthoritatively(int senderPeerId, StringName actionId)
    {
        if (!ValidateSender(senderPeerId, out _))
        {
            return false;
        }

        InteractiveComponent? target = _activeInteractive;
        if (target is null)
        {
            return false;
        }

        // A running execution only answers to the action that reserved it. An instant action holds
        // no reservation at all, so there is nothing to release once it has completed.
        if (target.ActiveAction is not null && target.ActiveAction.Definition?.Id != actionId)
        {
            return false;
        }

        _activeInteractive = null;
        return target.CancelExecution(this, ReleasedReason);
    }

    private void EmitStatusFor(InteractiveComponent interactive)
    {
        EmitSignal(SignalName.InteractionStatusChanged, interactive);
    }

    private bool ValidateSender(int senderPeerId, out string reason)
    {
        if (senderPeerId != OwnerPeerId)
        {
            reason = "The interaction owner is invalid.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private void RejectInteraction(
        int senderPeerId,
        NodePath targetPath,
        StringName actionId,
        string reason
    )
    {
        GD.PushWarning($"{GetPath()}: rejected interaction from peer {senderPeerId}: {reason}");
        if (senderPeerId == OwnerPeerId && IsLocallyControlled)
        {
            ClientInteractionRejected(targetPath, actionId, reason);
        }
        else if (senderPeerId > 0)
        {
            RpcId(senderPeerId, nameof(ClientInteractionRejected), targetPath, actionId, reason);
        }
    }

    private bool IsUsable(InteractiveComponent? interactive) =>
        interactive is not null && IsInstanceValid(interactive);

    private int GetRemoteSenderOrOwner()
    {
        int senderPeerId = (int)Multiplayer.GetRemoteSenderId();
        return senderPeerId == 0 ? OwnerPeerId : senderPeerId;
    }

    private void PurgeInvalidCandidates()
    {
        _indicatedInteractives.RemoveWhere(interactive => !IsUsable(interactive));
        _interactiveCandidates.RemoveWhere(interactive => !IsUsable(interactive));
        if (_focusedInteractive is not null && !IsUsable(_focusedInteractive))
        {
            _focusedInteractive = null;
        }
    }
}
