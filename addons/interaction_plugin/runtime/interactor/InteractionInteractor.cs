using System;
using System.Collections.Generic;
using Godot;

namespace QuestWorld.Interaction;

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

    [ExportGroup("Detection")]
    [Export]
    public NodePath ViewOriginPath { get; set; } = new();

    [Export]
    public float MaxInteractionDistance { get; set; } = 3.0f;

    [Export(PropertyHint.Range, "0,180,1")]
    public float MaxInteractionAngleDegrees { get; set; } = 60.0f;

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
    private Node3D _viewOrigin = null!;
    private InteractiveComponent _focusedInteractive = null!;
    private InteractiveComponent _activeInteractive = null!;
    private InteractiveComponent _autoInteractionTarget = null!;
    private bool _configurationValid;

    public InteractiveComponent FocusedInteractive => _focusedInteractive;

    public IReadOnlyCollection<InteractiveComponent> IndicatedInteractives =>
        _indicatedInteractives;

    public IReadOnlyCollection<InteractiveComponent> InteractiveCandidates =>
        _interactiveCandidates;

    public Node3D ViewOrigin => _viewOrigin;

    public bool IsConfigurationValid => _configurationValid;

    public override void _Ready()
    {
        _viewOrigin = ResolveViewOrigin();
        _configurationValid = _viewOrigin != null;
        if (!_configurationValid)
        {
            GD.PushError(
                $"{GetPath()}: InteractionInteractor requires a ViewOrigin (configured path: '{ViewOriginPath}')."
            );
            SetProcess(false);
            return;
        }

        if (OwnerPeerId <= 0)
        {
            OwnerPeerId = (int)Multiplayer.GetUniqueId();
        }

        if (IsInsideTree())
        {
            SetMultiplayerAuthority(OwnerPeerId);
        }
    }

    public override void _Process(double delta)
    {
        if (!_configurationValid || !IsLocalAuthority())
        {
            return;
        }

        bool focusChanged = RecalculateFocus();
        if (focusChanged && _focusedInteractive != null && _focusedInteractive.AutomaticInteraction)
        {
            TryStartInteractionInput();
        }
    }

    public void AddInteractiveIndication(InteractiveComponent interactive)
    {
        if (!IsUsable(interactive))
        {
            return;
        }

        _indicatedInteractives.Add(interactive);
        interactive.RegisterInteractor(this);
        EmitStatusFor(interactive);
    }

    public void RemoveInteractiveIndication(InteractiveComponent interactive)
    {
        _indicatedInteractives.Remove(interactive);
        if (!_interactiveCandidates.Contains(interactive))
        {
            interactive?.UnregisterInteractor(this);
        }
    }

    public void AddInteractive(InteractiveComponent interactive)
    {
        if (!IsUsable(interactive))
        {
            return;
        }

        _interactiveCandidates.Add(interactive);
        interactive.RegisterInteractor(this);
        RecalculateFocus();
    }

    public void RemoveInteractive(InteractiveComponent interactive)
    {
        if (interactive == null)
        {
            return;
        }

        _interactiveCandidates.Remove(interactive);
        if (_activeInteractive == interactive && IsLocalAuthority())
        {
            interactive.ReleaseInteractionInput(this);
            _activeInteractive = null!;
        }

        if (!_indicatedInteractives.Contains(interactive))
        {
            interactive.UnregisterInteractor(this);
        }

        RecalculateFocus();
    }

    public bool RecalculateFocus()
    {
        if (!_configurationValid)
        {
            return false;
        }

        PurgeInvalidCandidates();
        InteractiveComponent best = null!;
        float bestScore = float.MinValue;
        foreach (InteractiveComponent candidate in _interactiveCandidates)
        {
            if (!IsWithinInteractionRange(candidate))
            {
                continue;
            }

            float score = CalculateInteractionScore(candidate);
            if (best == null || score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (_focusedInteractive == best)
        {
            EmitStatusFor(_focusedInteractive);
            return false;
        }

        _focusedInteractive = best;
        EmitSignal(SignalName.FocusedInteractiveChanged, _focusedInteractive);
        EmitStatusFor(_focusedInteractive);
        return true;
    }

    public float CalculateInteractionScore(InteractiveComponent interactive)
    {
        Vector3 offset = interactive.GetInteractionPosition() - _viewOrigin.GlobalPosition;
        float distance = offset.Length();
        if (distance <= Mathf.Epsilon)
        {
            return 1.0f;
        }

        float alignment = Mathf.Max(0.0f, (-_viewOrigin.GlobalBasis.Z).Dot(offset.Normalized()));
        return alignment / (1.0f + distance * Mathf.Max(DistanceScoreCoefficient, 0.0f));
    }

    public bool IsWithinInteractionRange(InteractiveComponent interactive)
    {
        if (interactive == null || _viewOrigin == null)
        {
            return false;
        }

        Vector3 offset = interactive.GetInteractionPosition() - _viewOrigin.GlobalPosition;
        float distance = offset.Length();
        if (distance > Mathf.Max(MaxInteractionDistance, 0.0f))
        {
            return false;
        }

        if (distance <= Mathf.Epsilon)
        {
            return true;
        }

        float alignment = (-_viewOrigin.GlobalBasis.Z).Dot(offset.Normalized());
        float minimumAlignment = Mathf.Cos(
            Mathf.DegToRad(Mathf.Clamp(MaxInteractionAngleDegrees, 0.0f, 180.0f))
        );
        return alignment >= minimumAlignment;
    }

    public InteractionPresentation GetInteractionPresentation()
    {
        if (_focusedInteractive == null)
        {
            return new InteractionPresentation(
                null!,
                string.Empty,
                string.Empty,
                InteractionActionName,
                new InteractionBlocked("No interaction target."),
                false
            );
        }

        return _focusedInteractive.GetPresentation(this, true);
    }

    public bool TryStartInteractionInput()
    {
        RecalculateFocus();
        InteractiveComponent target = _focusedInteractive;
        if (target == null)
        {
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

    public void NotifyInteractiveStatusChanged(InteractiveComponent interactive)
    {
        if (interactive == _focusedInteractive)
        {
            EmitStatusFor(interactive);
        }

        RecalculateFocus();
    }

    public bool ReleaseInteractionInput(InteractiveComponent interactive)
    {
        return interactive.Stateful?.ReleaseInteractionInput(this) ?? false;
    }

    [Rpc(
        MultiplayerApi.RpcMode.AnyPeer,
        CallLocal = false,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    public void ServerTryStartInteraction(NodePath targetPath)
    {
        int senderPeerId = GetRemoteSenderOrOwner();
        InteractiveComponent target = GetTree()
            .Root.GetNodeOrNull<InteractiveComponent>(targetPath);
        if (target == null)
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
        Node target = GetTree().Root.GetNodeOrNull(targetPath);
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

        if (_activeInteractive == null)
        {
            return false;
        }

        bool released = _activeInteractive.ReleaseInteractionInput(this);
        _activeInteractive = null!;
        return released;
    }

    private void EmitStatusFor(InteractiveComponent interactive)
    {
        if (interactive == null)
        {
            return;
        }

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

    private Node3D ResolveViewOrigin()
    {
        if (ViewOriginPath != null && !ViewOriginPath.IsEmpty)
        {
            Node3D explicitOrigin = GetNodeOrNull<Node3D>(ViewOriginPath);
            if (explicitOrigin != null)
            {
                return explicitOrigin;
            }

            Node3D parentOrigin = GetParent()?.GetNodeOrNull<Node3D>(ViewOriginPath)!;
            if (parentOrigin != null)
            {
                return parentOrigin;
            }
        }

        return GetNodeOrNull<Node3D>("ViewOrigin")
            ?? GetParent()?.GetNodeOrNull<Node3D>("ViewOrigin")
            ?? FindFirstNode<Camera3D>(GetParent())!;
    }

    private bool IsLocalAuthority() => !IsInsideTree() || IsMultiplayerAuthority();

    private bool IsUsable(InteractiveComponent interactive) =>
        interactive != null && GodotObject.IsInstanceValid(interactive);

    private int GetRemoteSenderOrOwner()
    {
        int senderPeerId = (int)Multiplayer.GetRemoteSenderId();
        return senderPeerId == 0 ? OwnerPeerId : senderPeerId;
    }

    private void PurgeInvalidCandidates()
    {
        _indicatedInteractives.RemoveWhere(interactive => !IsUsable(interactive));
        _interactiveCandidates.RemoveWhere(interactive => !IsUsable(interactive));
        if (_focusedInteractive != null && !IsUsable(_focusedInteractive))
        {
            _focusedInteractive = null!;
        }
    }

    private static T FindFirstNode<T>(Node root)
        where T : Node
    {
        if (root == null)
        {
            return null!;
        }

        if (root is T match)
        {
            return match;
        }

        foreach (Node child in root.GetChildren())
        {
            T nested = FindFirstNode<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null!;
    }
}
