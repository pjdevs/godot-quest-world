using Godot;

public interface ICarrier
{
    public bool IsCarrying { get; }
    public bool TryTake(StringName itemId, Node3D carriableObject);
    public bool TryDrop(StringName itemId);
}
