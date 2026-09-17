using Godot;

public abstract class CarryOperation
{
    public double CommitTimeSec { get; private set; }
    public double EndTimeSec { get; private set; }
    public double Elapsed { get; set; } = 0f;
    public bool HasCommited { get; set; } = false;

    private CarryOperation(double commitTimeSec, double endTimeSec)
    {
        CommitTimeSec = commitTimeSec;
        EndTimeSec = endTimeSec;
    }

    public sealed class TakeOperation(
        StringName itemId,
        Node3D carriableItemObject,
        double commitTimeSec,
        double endTimeSec
    ) : CarryOperation(commitTimeSec, endTimeSec)
    {
        public StringName ItemId { get; private set; } = itemId;
        public Node3D CarriableItemObject { get; private set; } = carriableItemObject;
    }

    public sealed class DropOperation(double commitTimeSec, double endTimeSec)
        : CarryOperation(commitTimeSec, endTimeSec)
    { }

    public static TakeOperation Take(
        StringName itemId,
        Node3D carriableItemObject,
        CarryKindAnimationConfig? config
    ) =>
        new(
            itemId,
            carriableItemObject,
            config?.TakeAnimationCommitTimeSec ?? 0.0,
            config?.TakeAnimationEndTimeSec ?? 0.0
        );

    public static DropOperation Drop(CarryKindAnimationConfig? config) =>
        new(config?.DropAnimationCommitTimeSec ?? 0.0, config?.DropAnimationEndTimeSec ?? 0.0);
}
