using Godot;

namespace AnimatedInteractionPlugin.Runtime;

/// <summary>Sequences one actor presentation at a time without owning gameplay execution.</summary>
[GlobalClass]
public partial class AnimatedInteractionComponent : Node
{
    [Export]
    public Node? PlaybackNode { get; set; }

    public AnimatedInteractionOperation? CurrentOperation { get; private set; }

    private IAnimatedInteractionPlayback? _playback;
    private int _currentPhaseIndex = -1;
    private double _phaseStartedAt;
    private double _cycleStartedAt;
    private double _currentPhaseDuration;
    private bool _startPending;

    public override void _EnterTree()
    {
        SetMultiplayerAuthority(1);
    }

    public override void _Ready()
    {
        ResolvePlayback();
    }

    public AnimatedInteractionOperation? TryStart(AnimatedInteractionProfile profile)
    {
        string reason = string.Empty;
        if (
            !IsAuthoritative
            || CurrentOperation is not null
            || !TryValidateProfile(profile, out reason)
        )
        {
            if (!string.IsNullOrEmpty(reason))
            {
                GD.PushWarning($"{GetPath()}: cannot start animated interaction: {reason}");
            }

            return null;
        }

        CurrentOperation = new AnimatedInteractionOperation(this, profile);
        _currentPhaseIndex = -1;
        _startPending = true;
        return CurrentOperation;
    }

    public void CancelCurrentOperation()
    {
        CurrentOperation?.Cancel();
    }

    public override void _Process(double delta)
    {
        AnimatedInteractionOperation? operation = CurrentOperation;
        if (!IsAuthoritative || operation is null)
        {
            return;
        }

        if (_startPending)
        {
            _startPending = false;
            EnterPhase(operation, 0);
            return;
        }

        AnimatedInteractionPhase? phase = operation.CurrentPhase;
        if (phase is null)
        {
            return;
        }

        double now = CurrentTimeSeconds();
        double elapsed = now - _phaseStartedAt;
        int observedPhaseIndex = _currentPhaseIndex;

        if (
            !operation.HasReachedCommit
            && phase.HasCommitPoint
            && elapsed >= phase.CommitPointSeconds
        )
        {
            operation.MarkCommitReached();
            if (!IsCurrent(operation, observedPhaseIndex))
            {
                return;
            }
        }

        if (
            phase.Playback == AnimatedInteractionPlayback.Loop
            && now - _cycleStartedAt >= _currentPhaseDuration
        )
        {
            _cycleStartedAt = now;
            BroadcastPhase(phase);
        }

        if (
            phase.Transition == AnimatedInteractionTransition.Automatic
            && elapsed >= _currentPhaseDuration
        )
        {
            EnterPhase(operation, observedPhaseIndex + 1);
        }
    }

    internal bool TryAdvance(AnimatedInteractionOperation operation)
    {
        if (!IsAuthoritative || !ReferenceEquals(CurrentOperation, operation) || _startPending)
        {
            return false;
        }

        EnterPhase(operation, _currentPhaseIndex + 1);
        return true;
    }

    internal bool TryReachCommit(AnimatedInteractionOperation operation) =>
        IsAuthoritative
        && ReferenceEquals(CurrentOperation, operation)
        && operation.MarkCommitReached();

    internal bool TryCancel(AnimatedInteractionOperation operation)
    {
        if (!IsAuthoritative || !ReferenceEquals(CurrentOperation, operation))
        {
            return false;
        }

        ClearCurrentOperation();
        BroadcastStop();
        operation.MarkCancelled();
        return true;
    }

    private void EnterPhase(AnimatedInteractionOperation operation, int phaseIndex)
    {
        if (!ReferenceEquals(CurrentOperation, operation))
        {
            return;
        }

        if (phaseIndex >= operation.Profile.Phases.Count)
        {
            ClearCurrentOperation();
            BroadcastStop();
            operation.Finish();
            return;
        }

        AnimatedInteractionPhase phase = operation.Profile.Phases[phaseIndex];
        if (!TryResolveDuration(phase, out double duration))
        {
            GD.PushWarning(
                $"{GetPath()}: animation '{phase.Animation}' became unavailable during playback."
            );
            TryCancel(operation);
            return;
        }

        _currentPhaseIndex = phaseIndex;
        _currentPhaseDuration = duration;
        _phaseStartedAt = CurrentTimeSeconds();
        _cycleStartedAt = _phaseStartedAt;
        BroadcastPhase(phase);
        operation.EnterPhase(phase);
    }

    private bool TryValidateProfile(AnimatedInteractionProfile? profile, out string reason)
    {
        reason = string.Empty;
        if (profile is null || profile.Phases.Count == 0)
        {
            reason = "the profile has no phases.";
            return false;
        }

        if (!ResolvePlayback())
        {
            reason = "the playback node does not implement IAnimatedInteractionPlayback.";
            return false;
        }

        int authoredCommitCount = 0;
        for (int index = 0; index < profile.Phases.Count; index++)
        {
            AnimatedInteractionPhase? phase = profile.Phases[index];
            if (phase is null || phase.Animation.IsEmpty)
            {
                reason = $"phase {index} has no animation.";
                return false;
            }

            if (
                phase.Playback == AnimatedInteractionPlayback.Loop
                && phase.Transition == AnimatedInteractionTransition.Automatic
            )
            {
                reason = $"phase {index} cannot be both looping and automatic.";
                return false;
            }

            if (!TryResolveDuration(phase, out double duration))
            {
                reason = $"phase {index} references unavailable animation '{phase.Animation}'.";
                return false;
            }

            if (
                phase.HasCommitPoint
                && phase.Transition == AnimatedInteractionTransition.Automatic
                && phase.CommitPointSeconds > duration
            )
            {
                reason = $"phase {index} has a commit point after its animation duration.";
                return false;
            }

            if (phase.HasCommitPoint && ++authoredCommitCount > 1)
            {
                reason = "the profile has more than one authored commit point.";
                return false;
            }
        }

        return true;
    }

    private bool TryResolveDuration(AnimatedInteractionPhase phase, out double duration)
    {
        duration = 0.0;
        return ResolvePlayback()
            && _playback!.TryGetInteractionAnimationDuration(phase.Animation, out duration)
            && double.IsFinite(duration)
            && duration > 0.0;
    }

    private bool ResolvePlayback()
    {
        if (_playback is not null)
        {
            return true;
        }

        _playback = PlaybackNode as IAnimatedInteractionPlayback;
        return _playback is not null;
    }

    private void BroadcastPhase(AnimatedInteractionPhase phase)
    {
        if (Multiplayer?.MultiplayerPeer is null)
        {
            PresentPhase(phase.Animation, (int)phase.Playback);
            return;
        }

        Rpc(nameof(PresentPhase), phase.Animation, (int)phase.Playback);
    }

    private void BroadcastStop()
    {
        if (Multiplayer?.MultiplayerPeer is null)
        {
            StopPresentation();
            return;
        }

        Rpc(nameof(StopPresentation));
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void PresentPhase(StringName animation, int playback)
    {
        if (!ResolvePlayback())
        {
            return;
        }

        _playback!.PlayInteractionAnimation(animation, (AnimatedInteractionPlayback)playback);
    }

    [Rpc(
        MultiplayerApi.RpcMode.Authority,
        CallLocal = true,
        TransferMode = MultiplayerPeer.TransferModeEnum.Reliable
    )]
    private void StopPresentation()
    {
        if (ResolvePlayback())
        {
            _playback!.StopInteractionAnimation();
        }
    }

    private bool IsCurrent(AnimatedInteractionOperation operation, int phaseIndex) =>
        ReferenceEquals(CurrentOperation, operation) && _currentPhaseIndex == phaseIndex;

    private void ClearCurrentOperation()
    {
        CurrentOperation = null;
        _currentPhaseIndex = -1;
        _phaseStartedAt = 0.0;
        _cycleStartedAt = 0.0;
        _currentPhaseDuration = 0.0;
        _startPending = false;
    }

    private bool IsAuthoritative =>
        Multiplayer is null || Multiplayer.MultiplayerPeer is null || Multiplayer.IsServer();

    private static double CurrentTimeSeconds() => Time.GetTicksMsec() / 1000.0;
}
