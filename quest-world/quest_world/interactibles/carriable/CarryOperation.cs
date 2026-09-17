using System;
using Godot;

public abstract class CarryOperation
{
    public event Action? Committed;
    public event Action? Finished;
    public event Action<string>? Failed;
    public event Action? Cancelled;

    public double CommitTimeSec { get; private set; }
    public double EndTimeSec { get; private set; }
    public double Elapsed { get; set; } = 0f;
    public bool HasCommited { get; private set; } = false;

    private bool _isTerminal;

    private CarryOperation(double commitTimeSec, double endTimeSec)
    {
        CommitTimeSec = commitTimeSec;
        EndTimeSec = endTimeSec;
    }

    internal void Commit()
    {
        if (_isTerminal || HasCommited)
        {
            return;
        }

        HasCommited = true;
        Action? handler = Committed;
        Committed = null;
        handler?.Invoke();
    }

    internal void Finish()
    {
        if (_isTerminal)
        {
            return;
        }

        _isTerminal = true;
        Action? handler = Finished;
        ClearHandlers();
        handler?.Invoke();
    }

    internal void Fail(string reason)
    {
        if (_isTerminal)
        {
            return;
        }

        _isTerminal = true;
        Action<string>? handler = Failed;
        ClearHandlers();
        handler?.Invoke(reason);
    }

    public void Cancel()
    {
        if (_isTerminal)
        {
            return;
        }

        _isTerminal = true;
        Action? handler = Cancelled;
        ClearHandlers();
        handler?.Invoke();
    }

    private void ClearHandlers()
    {
        Committed = null;
        Finished = null;
        Failed = null;
        Cancelled = null;
    }

    public sealed class TakeOperation(
        StringName itemId,
        Node3D carriableItemObject,
        double commitTimeSec,
        double endTimeSec,
        CarryKindAnimationConfig? animationConfig
    ) : CarryOperation(commitTimeSec, endTimeSec)
    {
        public StringName ItemId { get; private set; } = itemId;
        public Node3D CarriableItemObject { get; private set; } = carriableItemObject;
        public CarryKindAnimationConfig? AnimationConfig { get; } = animationConfig;
    }

    public sealed class DropOperation(double commitTimeSec, double endTimeSec)
        : CarryOperation(commitTimeSec, endTimeSec) { }

    public static TakeOperation Take(
        StringName itemId,
        Node3D carriableItemObject,
        CarryKindAnimationConfig? config
    ) =>
        new(
            itemId,
            carriableItemObject,
            config?.TakeAnimationCommitTimeSec ?? 0.0,
            config?.TakeAnimationEndTimeSec ?? 0.0,
            config
        );

    public static DropOperation Drop(CarryKindAnimationConfig? config) =>
        new(config?.DropAnimationCommitTimeSec ?? 0.0, config?.DropAnimationEndTimeSec ?? 0.0);
}
