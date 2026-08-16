using System;
using System.Collections.Generic;
using Godot;
using QuestWorld.Interaction.Runtime.Interactive;

namespace QuestWorld.Interaction.Runtime.Interactor;

[Tool]
[GlobalClass]
public partial class InteractionInteractor : Node
{
    [Signal]
    public delegate void FocusedInteractiveChangedEventHandler(Node interactive);

    [Signal]
    public delegate void InteractionStatusChangedEventHandler(
        Node interactive,
        bool isAllowed,
        string reason
    );

    [Signal]
    public delegate void InteractionRequestedEventHandler(Node interactive);

    [Signal]
    public delegate void InteractionRejectedEventHandler(Node interactive, string reason);

    [Signal]
    public delegate void InteractiveIndicationAddedEventHandler(Node interactive);

    [Signal]
    public delegate void InteractiveIndicationRemovedEventHandler(Node interactive);

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
            UpdateConfigurationWarnings();
        }
    }

    [Export]
    public Node3D? InteractionOrigin { get; set; }

    [Export]
    public float MaxInteractionDistance { get; set; } = 10.0f;

    [Export(PropertyHint.Range, "0,180,1")]
    public float MaxInteractionAngleDegrees { get; set; } = 30.0f;

    [Export]
    public float DistanceScoreCoefficient { get; set; } = 0.5f;

    [ExportGroup("Input")]
    [Export]
    public StringName InteractionActionName { get; set; } = "interact";

    [Export]
    public int ServerPeerId { get; set; } = 1;

    [Export]
    public int OwnerPeerId { get; set; } = 1;

    private readonly HashSet<InteractiveComponent> _indicatedInteractives = new();
    private readonly HashSet<InteractiveComponent> _interactiveCandidates = new();
    private Node3D? _viewOrigin;
    private Node3D? _resolvedInteractionOrigin;
    private InteractiveComponent? _focusedInteractive;
    private InteractiveComponent? _activeInteractive;

    public InteractiveComponent? FocusedInteractive => _focusedInteractive;

    public bool IsLocallyControlled => OwnerPeerId == (int)Multiplayer.GetUniqueId();

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        List<string> warnings = [];
        if (ViewOrigin is null)
        {
            warnings.Add("ViewOrigin must be assigned.");
        }

        return [.. warnings];
    }
#endif

    public override void _Ready()
    {
#if TOOLS
        if (Engine.IsEditorHint())
        {
            return;
        }
#endif

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
            OwnerPeerId = (int)Multiplayer.GetUniqueId();
        }

        SetMultiplayerAuthority(ServerPeerId);
    }

    public override void _Process(double delta)
    {
#if TOOLS
        if (Engine.IsEditorHint())
        {
            return;
        }
#endif

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

    public InteractionPresentation? GetInteractionPresentation()
    {
        return _focusedInteractive?.GetPresentation(this, true);
    }

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
        return interactive.Stateful?.ReleaseInteractionInput(this) ?? false;
    }

    public override void _ExitTree()
    {
#if TOOLS
        if (Engine.IsEditorHint())
        {
            return;
        }
#endif

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
        if (senderPeerId == (int)Multiplayer.GetUniqueId())
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
