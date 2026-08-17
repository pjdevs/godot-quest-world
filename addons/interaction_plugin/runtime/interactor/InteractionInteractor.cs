using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Interactor;

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
    /// <summary>Emitted locally when the best target changes.</summary>
    /// <param name="interactive">New focused interactive, or null when focus is cleared.</param>
    [Signal]
    public delegate void FocusedInteractiveChangedEventHandler(Node interactive);

    /// <summary>Emitted locally when a visible target's allowed status or blocked reason changes.</summary>
    /// <param name="interactive">Interactive whose presentation status changed.</param>
    /// <param name="isAllowed">Whether interaction is currently allowed.</param>
    /// <param name="reason">Blocked reason, or an empty string when allowed.</param>
    [Signal]
    public delegate void InteractionStatusChangedEventHandler(
        Node interactive,
        bool isAllowed,
        string reason
    );

    /// <summary>Emitted locally after prevalidation and before any client RPC or host dispatch.</summary>
    /// <param name="interactive">Target requested by the owning player.</param>
    [Signal]
    public delegate void InteractionRequestedEventHandler(Node interactive);

    /// <summary>Emitted locally for a prevalidation failure or a rejection returned by the server.</summary>
    /// <param name="interactive">Rejected target, or null when no target can be resolved.</param>
    /// <param name="reason">User-facing rejection reason.</param>
    [Signal]
    public delegate void InteractionRejectedEventHandler(Node interactive, string reason);

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

    /// <summary>Gets or sets the project input action associated with this interactor.</summary>
    [ExportGroup("Input")]
    [Export]
    public StringName InteractionActionName { get; set; } = "interact";

    /// <summary>Gets or sets the server peer that receives reliable interaction RPCs.</summary>
    [Export]
    public int ServerPeerId { get; set; } = 1;

    /// <summary>Gets or sets the peer allowed to control this interactor.</summary>
    [Export]
    public int OwnerPeerId { get; set; } = 1;

    private readonly HashSet<InteractiveComponent> _indicatedInteractives = new();
    private readonly HashSet<InteractiveComponent> _interactiveCandidates = new();
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
            OwnerPeerId = Multiplayer is null || Multiplayer.MultiplayerPeer is null
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

        bool focusChanged = RecalculateFocus();
        if (focusChanged && _focusedInteractive?.AutomaticInteraction == true)
        {
            TryStartInteractionInput();
        }
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
            interactive.ReleaseInteractionInput(this);
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
        if (ViewOrigin is null || _resolvedInteractionOrigin is null)
        {
            return false;
        }

        PurgeInvalidCandidates();
        InteractiveComponent? best = null;
        float bestScore = float.MinValue;
        foreach (InteractiveComponent candidate in _interactiveCandidates)
        {
            if (!IsWithinInteractionRange(candidate))
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
            if (best is not null)
            {
                EmitStatusFor(best);
            }

            return false;
        }

        _focusedInteractive = best;
        Variant focusedInteractive = _focusedInteractive is null
            ? new Variant()
            : _focusedInteractive;
        EmitSignal(SignalName.FocusedInteractiveChanged, focusedInteractive);
        if (best is not null)
        {
            EmitStatusFor(best);
        }

        return true;
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
    public InteractionPresentation? GetInteractionPresentation()
    {
        return _focusedInteractive?.GetPresentation(this, true);
    }

    /// <summary>Prevalidates the focused target and requests authoritative interaction start.</summary>
    /// <remarks>
    /// Call from the local player's input code. On a client, true means the reliable request was sent;
    /// final acceptance is reported by gameplay state or <see cref="InteractionRejected"/>.
    /// </remarks>
    /// <returns>Whether a locally valid request was dispatched or accepted by the host.</returns>
    public bool TryStartInteractionInput()
    {
        RecalculateFocus();
        InteractiveComponent? target = _focusedInteractive;
        if (target is null)
        {
            return false;
        }

        InteractionStatus localStatus = target.EvaluateStatus(this);
        if (localStatus is InteractionBlocked blocked)
        {
            EmitSignal(SignalName.InteractionRejected, target, blocked.Reason);
            return false;
        }

        EmitSignal(SignalName.InteractionRequested, target);
        if (!Multiplayer.IsServer())
        {
            RpcId(ServerPeerId, nameof(ServerTryStartInteraction), target.GetPath());
            return true;
        }

        return TryStartInteractionAuthoritatively(target, OwnerPeerId, out _);
    }

    /// <summary>Requests authoritative release of the currently active interaction input.</summary>
    /// <remarks>
    /// Call from the local player's input-release code. On a client, true means the request was sent.
    /// </remarks>
    /// <returns>Whether a request was sent or the host released an active interaction.</returns>
    public bool TryEndInteractionInput()
    {
        if (!Multiplayer.IsServer())
        {
            RpcId(ServerPeerId, nameof(ServerTryEndInteraction));
            return true;
        }

        return EndInteractionInputAuthoritatively(OwnerPeerId);
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

    internal bool ReleaseInteractionInput(InteractiveComponent interactive)
    {
        return interactive.ReleaseInteractionInput(this);
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
            _activeInteractive.ReleaseInteractionInput(this);
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
        _focusedInteractive = null;
    }

    /// <summary>Reliable client-to-server RPC that validates and starts one target.</summary>
    /// <remarks>Called by Godot RPC dispatch; input code should call <see cref="TryStartInteractionInput"/>.</remarks>
    /// <param name="targetPath">Scene-tree path of the client-selected interactive.</param>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryStartInteraction(NodePath targetPath)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        InteractiveComponent? target = GetTree()
            .Root.GetNodeOrNull<InteractiveComponent>(targetPath);
        if (target is null)
        {
            RejectInteraction(senderPeerId, targetPath, "The interaction target no longer exists.");
            return;
        }

        if (!TryStartInteractionAuthoritatively(target, senderPeerId, out string reason))
        {
            RejectInteraction(senderPeerId, targetPath, reason);
        }
    }

    /// <summary>Reliable client-to-server RPC that releases the caller's active interaction.</summary>
    /// <remarks>Called by Godot RPC dispatch; input code should call <see cref="TryEndInteractionInput"/>.</remarks>
    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryEndInteraction()
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        if (!ValidateSender(senderPeerId, out string reason))
        {
            RejectInteraction(senderPeerId, new NodePath(), reason);
            return;
        }

        EndInteractionInputAuthoritatively(senderPeerId);
    }

    /// <summary>Reliable server-to-owner RPC that reports an authoritative rejection.</summary>
    /// <remarks>Called by Godot RPC dispatch on the owning client or directly on an offline host.</remarks>
    /// <param name="targetPath">Rejected target path, which may be empty.</param>
    /// <param name="reason">User-facing rejection reason.</param>
    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ClientInteractionRejected(NodePath targetPath, string reason)
    {
        Node? target = GetTree().Root.GetNodeOrNull(targetPath);
        EmitSignal(SignalName.InteractionRejected, target, reason);
    }

    private bool TryStartInteractionAuthoritatively(
        InteractiveComponent target,
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

        InteractionStatus status = target.EvaluateStatus(this);
        if (status is InteractionBlocked blocked)
        {
            reason = blocked.Reason;
            return false;
        }

        if (!target.StartInteraction(this))
        {
            reason = "The interaction could not be started.";
            return false;
        }

        _activeInteractive = target;
        return true;
    }

    private bool EndInteractionInputAuthoritatively(int senderPeerId)
    {
        if (!ValidateSender(senderPeerId, out _))
        {
            return false;
        }

        if (_activeInteractive is null)
        {
            return false;
        }

        bool released = _activeInteractive.ReleaseInteractionInput(this);
        _activeInteractive = null;
        return released;
    }

    private void EmitStatusFor(InteractiveComponent interactive)
    {
        InteractionPresentation presentation = interactive.GetPresentation(
            this,
            interactive == _focusedInteractive
        );
        EmitSignal(
            SignalName.InteractionStatusChanged,
            interactive,
            presentation.IsAllowed,
            presentation.BlockReason
        );
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

    private void RejectInteraction(int senderPeerId, NodePath targetPath, string reason)
    {
        GD.PushWarning($"{GetPath()}: rejected interaction from peer {senderPeerId}: {reason}");
        if (senderPeerId == OwnerPeerId && IsLocallyControlled)
        {
            ClientInteractionRejected(targetPath, reason);
        }
        else if (senderPeerId > 0)
        {
            RpcId(senderPeerId, nameof(ClientInteractionRejected), targetPath, reason);
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
