using Godot;

public interface ICarrier
{
    public bool IsCarrying { get; }
    public bool TryCarryVisual(StringName ItemId);
    public bool TryDropVisual();
}
