using System;

namespace AnimatedInteractionPlugin.Runtime;

/// <summary>Handle exposed to gameplay while one choreography is active.</summary>
public sealed class AnimatedInteractionOperation
{
    public event Action<AnimatedInteractionPhase>? PhaseEntered;
    public event Action? CommitReached;
    public event Action? Finished;
    public event Action? Cancelled;

    private readonly AnimatedInteractionComponent _owner;

    internal AnimatedInteractionOperation(
        AnimatedInteractionComponent owner,
        AnimatedInteractionProfile profile
    )
    {
        _owner = owner;
        Profile = profile;
    }

    internal AnimatedInteractionProfile Profile { get; }

    public AnimatedInteractionPhase? CurrentPhase { get; private set; }

    public bool HasReachedCommit { get; private set; }

    public bool IsTerminal { get; private set; }

    public bool Advance() => !IsTerminal && _owner.TryAdvance(this);

    public bool ReachCommit() => !IsTerminal && _owner.TryReachCommit(this);

    public bool Cancel() => !IsTerminal && _owner.TryCancel(this);

    internal void EnterPhase(AnimatedInteractionPhase phase)
    {
        if (IsTerminal)
        {
            return;
        }

        CurrentPhase = phase;
        PhaseEntered?.Invoke(phase);
    }

    internal bool MarkCommitReached()
    {
        if (IsTerminal || HasReachedCommit)
        {
            return false;
        }

        HasReachedCommit = true;
        CommitReached?.Invoke();
        return true;
    }

    internal void Finish()
    {
        if (IsTerminal)
        {
            return;
        }

        IsTerminal = true;
        Action? handler = Finished;
        ClearHandlers();
        handler?.Invoke();
    }

    internal void MarkCancelled()
    {
        if (IsTerminal)
        {
            return;
        }

        IsTerminal = true;
        Action? handler = Cancelled;
        ClearHandlers();
        handler?.Invoke();
    }

    private void ClearHandlers()
    {
        PhaseEntered = null;
        CommitReached = null;
        Finished = null;
        Cancelled = null;
    }
}
